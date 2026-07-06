using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ActiMetrics.Data;
using ActiMetrics.Shared.Extensions;
using ActiMetrics.Shared.Models;

namespace ActiMetrics.Service.Services
{
    public class SyncService
    {
        private readonly StateRepository _stateRepository;
        private readonly AppUsageRepository _appUsageRepository;
        private readonly ScreenshotRepository _screenshotRepository;
        private readonly Session _sessionRepository;
        private readonly TokenService _tokenService;
        private readonly ILogger<SyncService> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _apiUrl;
        private readonly string _ticketsUrl;
        private readonly string _workerId;
        private readonly string _workerUserName;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public SyncService(
            StateRepository stateRepository,
            AppUsageRepository appUsageRepository,
            ScreenshotRepository screenshotRepository,
            Session sessionRepository,
            TokenService tokenService,
            ILogger<SyncService> logger)
        {
            _stateRepository = stateRepository;
            _appUsageRepository = appUsageRepository;
            _screenshotRepository = screenshotRepository;
            _sessionRepository = sessionRepository;
            _tokenService = tokenService;
            _logger = logger;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _apiUrl = "https://gotracerapi.asistentevirtualsas.com";
            _ticketsUrl = "https://automations.asistentevirtualsas.com/webhook/create-ticket";
            _workerId = Environment.MachineName;
            _workerUserName = Environment.UserName;
        }

        public async Task SyncAsync()
        {
            await SyncStatesAsync();
            await SyncAppUsageAsync();
        }

        public async Task<bool> CheckTakeScreenshotsAsync()
        {
            var serial = _tokenService.GetMachineSerial();
            var url = $"{_apiUrl}/tracer/permissions?serial={serial}";
            try
            {
                var sw = Stopwatch.StartNew();
                var res = await _httpClient.GetAsync(url).ConfigureAwait(false);
                sw.Stop();
                _logger.LogInformation("[API] GET {Url} → {Status} ({Ms}ms)", url, res.StatusCode, sw.ElapsedMilliseconds);

                if (!res.IsSuccessStatusCode) return true;

                var json = await res.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(json)) return true;

                var permissions = JsonSerializer.Deserialize<Permissions>(json, _jsonOptions);
                return permissions?.Take_Screenshots ?? true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[API] No se pudo consultar permisos para serial {Serial} — se asume permitido", serial);
                return true;
            }
        }

        public async Task SyncScreenshotAsync()
        {
            var screenshots = await _screenshotRepository.GetUnsyncedAsync();
            if (!screenshots.Any()) return;

            _logger.LogInformation("[API] Sincronizando {N} screenshots...", screenshots.Count());

            foreach (var screenshot in screenshots)
            {
                var fileName = Path.GetFileName(screenshot.FilePath);
                try
                {
                    var sw = Stopwatch.StartNew();
                    var url = $"{_apiUrl}/tracer/screenshot";
                    HttpResponseMessage response;

                    using (var content = new MultipartFormDataContent())
                    using (var fileStream = File.OpenRead(screenshot.FilePath))
                    {
                        content.Add(new StreamContent(fileStream), "file", fileName);
                        content.Add(new StringContent(Environment.MachineName), "machine");
                        response = await _httpClient.PostAsync(url, content);
                    }

                    sw.Stop();
                    _logger.LogInformation("[API] POST {Url} → {Status} ({Ms}ms)", url, response.StatusCode, sw.ElapsedMilliseconds);

                    if (response.IsSuccessStatusCode)
                    {
                        await _screenshotRepository.MarkSyncedAsync(screenshot.Id);
                        try
                        {
                            if (File.Exists(screenshot.FilePath))
                            {
                                File.Delete(screenshot.FilePath);
                                _logger.LogDebug("[Screenshot] Archivo local eliminado: {File}", fileName);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[Screenshot] Error al eliminar archivo local {File}", fileName);
                        }
                    }
                    else
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        _logger.LogWarning("[API] Screenshot {File} rechazado: {Status} — {Body}", fileName, response.StatusCode, body);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[API] Excepción al sincronizar screenshot {File}", fileName);
                }
            }
        }

        public async Task<HttpResponseMessage> SaveTicketAsync(string category, string description)
        {
            string displayName = System.DirectoryServices.AccountManagement
                                   .UserPrincipal.Current.DisplayName;
            var data = new
            {
                categoria = category,
                descripcion = description,
                usuario = displayName
            };

            var content = new StringContent(
                JsonSerializer.Serialize(data),
                Encoding.UTF8,
                "application/json");

            _logger.LogInformation("[API] POST {Url} — Ticket: {Cat}", _ticketsUrl, category);
            var sw = Stopwatch.StartNew();
            var response = await _httpClient.PostAsync(_ticketsUrl, content);
            sw.Stop();
            _logger.LogInformation("[API] Ticket → {Status} ({Ms}ms)", response.StatusCode, sw.ElapsedMilliseconds);

            return response.EnsureSuccessStatusCode();
        }

        public async Task<List<ReportType>?> GetReportTypesAsync()
        {
            var url = $"{_apiUrl}/report-types";
            _logger.LogDebug("[API] GET {Url}", url);
            try
            {
                var sw = Stopwatch.StartNew();
                var res = await _httpClient.GetAsync(url).ConfigureAwait(false);
                sw.Stop();
                _logger.LogInformation("[API] GET {Url} → {Status} ({Ms}ms)", url, res.StatusCode, sw.ElapsedMilliseconds);

                if (!res.IsSuccessStatusCode) return null;

                var json = await res.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonSerializer.Deserialize<List<ReportType>>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[API] No se pudo consultar el catálogo de tipos de reporte");
                return null;
            }
        }

        public async Task<HttpResponseMessage> SendReportAsync(int reportTypeId, string message)
        {
            var serial = _tokenService.GetMachineSerial();
            var url = $"{_apiUrl}/tracer/reports";
            var data = new { serial, report_type_id = reportTypeId, message };
            var content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");

            _logger.LogInformation("[API] POST {Url} — Reporte tipo {TypeId}", url, reportTypeId);
            var sw = Stopwatch.StartNew();
            var response = await _httpClient.PostAsync(url, content);
            sw.Stop();
            _logger.LogInformation("[API] Reporte → {Status} ({Ms}ms)", response.StatusCode, sw.ElapsedMilliseconds);

            return response;
        }

        public async Task<List<StateCategoryCatalogItem>?> GetStateCategoriesAsync()
        {
            var url = $"{_apiUrl}/state-categories";
            _logger.LogDebug("[API] GET {Url}", url);
            try
            {
                var sw = Stopwatch.StartNew();
                var res = await _httpClient.GetAsync(url).ConfigureAwait(false);
                sw.Stop();
                _logger.LogInformation("[API] GET {Url} → {Status} ({Ms}ms)", url, res.StatusCode, sw.ElapsedMilliseconds);

                if (!res.IsSuccessStatusCode) return null;

                var json = await res.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonSerializer.Deserialize<List<StateCategoryCatalogItem>>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[API] No se pudo consultar el catálogo de categorías de estado");
                return null;
            }
        }

        public async Task<List<StateCatalogItem>?> GetStatesAsync()
        {
            var url = $"{_apiUrl}/states";
            _logger.LogDebug("[API] GET {Url}", url);
            try
            {
                var sw = Stopwatch.StartNew();
                var res = await _httpClient.GetAsync(url).ConfigureAwait(false);
                sw.Stop();
                _logger.LogInformation("[API] GET {Url} → {Status} ({Ms}ms)", url, res.StatusCode, sw.ElapsedMilliseconds);

                if (!res.IsSuccessStatusCode) return null;

                var json = await res.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonSerializer.Deserialize<List<StateCatalogItem>>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[API] No se pudo consultar el catálogo de estados");
                return null;
            }
        }

        private async Task SyncStatesAsync()
        {
            var pending = await _stateRepository.GetUnsyncedAsync();
            if (!pending.Any()) return;

            var url = $"{_apiUrl}/tracer/states";
            _logger.LogInformation("[API] POST {Url} — {N} estados pendientes", url, pending.Count());
            try
            {
                var json = JsonSerializer.Serialize(pending);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var sw = Stopwatch.StartNew();
                var response = await _httpClient.PostAsync(url, content);
                sw.Stop();

                _logger.LogInformation("[API] States → {Status} ({Ms}ms)", response.StatusCode, sw.ElapsedMilliseconds);

                if (response.IsSuccessStatusCode)
                {
                    foreach (var log in pending)
                        await _stateRepository.MarkSyncedAsync(log.Id);

                    _logger.LogInformation("[Sync] {N} estados sincronizados", pending.Count());
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("[Sync] Error al sincronizar estados: {Status} — {Body}", response.StatusCode, body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Sync] Excepción al sincronizar estados");
            }
        }

        private async Task SyncAppUsageAsync()
        {
            var pending = await _appUsageRepository.GetUnsyncedAsync();
            if (!pending.Any()) return;

            var url = $"{_apiUrl}/tracer/app-usage";
            _logger.LogInformation("[API] POST {Url} — {N} registros de uso pendientes", url, pending.Count());
            try
            {
                var json = JsonSerializer.Serialize(pending);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var sw = Stopwatch.StartNew();
                var response = await _httpClient.PostAsync(url, content);
                sw.Stop();

                _logger.LogInformation("[API] AppUsage → {Status} ({Ms}ms)", response.StatusCode, sw.ElapsedMilliseconds);

                if (response.IsSuccessStatusCode)
                {
                    foreach (var log in pending)
                        await _appUsageRepository.MarkSyncedAsync(log.Id);

                    _logger.LogInformation("[Sync] {N} registros de uso sincronizados", pending.Count());
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("[Sync] Error al sincronizar app usage: {Status} — {Body}", response.StatusCode, body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Sync] Excepción al sincronizar app usage");
            }
        }

        public async Task<Programation?> GetTodayScheduleAsync()
        {
            var email = _sessionRepository.GetEmail() ?? string.Empty;
            var today = GetToday();

            _logger.LogInformation("[API] Consultando programación del día {Day} para {Email}", today, email);

            var appuser = await GetAppuserByEmailAsync(email);
            if (appuser is null)
            {
                _logger.LogWarning("[API] No se encontró appuser para {Email}", email);
                return null;
            }

            var schedule = await GetScheduleForDayAsync(appuser.Id, today);
            if (schedule is null)
            {
                _logger.LogInformation("[Sync] Sin programación para el día {Day}", today);
                return null;
            }

            return await GetProgramationAsync(schedule.Programation_Id);
        }

        private async Task<AppUser?> GetAppuserByEmailAsync(string email)
        {
            var url = $"{_apiUrl}/appuser/findbyemail?email={email}";
            _logger.LogDebug("[API] GET {Url}", url);
            try
            {
                var sw = Stopwatch.StartNew();
                var res = await _httpClient.GetAsync(url).ConfigureAwait(false);
                sw.Stop();
                _logger.LogInformation("[API] GetAppuser → {Status} ({Ms}ms)", res.StatusCode, sw.ElapsedMilliseconds);

                if (!res.IsSuccessStatusCode) return null;

                var json = await res.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonSerializer.Deserialize<AppUser>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[API] No se pudo consultar appuser para {Email} — sin conexión, iniciando en modo libre", email);
                return null;
            }
        }

        private async Task<Schedule?> GetScheduleForDayAsync(int userId, Days today)
        {
            var url = $"{_apiUrl}/schedules?appuser_id={userId}";
            _logger.LogDebug("[API] GET {Url}", url);
            var sw = Stopwatch.StartNew();
            var res = await _httpClient.GetAsync(url).ConfigureAwait(false);
            sw.Stop();
            _logger.LogInformation("[API] GetSchedule userId={Id} → {Status} ({Ms}ms)", userId, res.StatusCode, sw.ElapsedMilliseconds);

            if (!res.IsSuccessStatusCode) return null;

            var json = await res.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(json)) return null;
            var schedules = JsonSerializer.Deserialize<List<Schedule>>(json, _jsonOptions);
            return schedules?.FirstOrDefault(s => s.Day_Of_Week == today);
        }

        private async Task<Programation?> GetProgramationAsync(int programationId)
        {
            var url = $"{_apiUrl}/programations/{programationId}";
            _logger.LogDebug("[API] GET {Url}", url);
            var sw = Stopwatch.StartNew();
            var res = await _httpClient.GetAsync(url).ConfigureAwait(false);
            sw.Stop();
            _logger.LogInformation("[API] GetProgramation id={Id} → {Status} ({Ms}ms)", programationId, res.StatusCode, sw.ElapsedMilliseconds);

            if (!res.IsSuccessStatusCode) return null;

            var json = await res.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<Programation>(json, _jsonOptions);
        }

        private static Days GetToday() => DateTime.Now.DayOfWeek switch
        {
            DayOfWeek.Monday => Days.L,
            DayOfWeek.Tuesday => Days.M,
            DayOfWeek.Wednesday => Days.X,
            DayOfWeek.Thursday => Days.J,
            DayOfWeek.Friday => Days.V,
            DayOfWeek.Saturday => Days.S,
            DayOfWeek.Sunday => Days.D,
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}
