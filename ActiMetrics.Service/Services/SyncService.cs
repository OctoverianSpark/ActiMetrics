using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ActiMetrics.Data;
using ActiMetrics.Shared.Extensions;
using ActiMetrics.Shared.Models;

namespace ActiMetrics.Service.Services
{
    // Resultado de intentar enviar un ticket — Sent=false no significa que se perdió: ya quedó
    // encolado localmente (ver TicketRepository/SyncTicketsAsync) y se reintenta solo.
    public record TicketSaveResult(bool Sent, string Message);

    public class SyncService
    {
        private readonly StateRepository _stateRepository;
        private readonly AppUsageRepository _appUsageRepository;
        private readonly ScreenshotRepository _screenshotRepository;
        private readonly TicketRepository _ticketRepository;
        private readonly Session _sessionRepository;
        private readonly TokenService _tokenService;
        private readonly ILogger<SyncService> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _apiUrl;
        private readonly string _ticketsUrl;
        private readonly string _workerId;
        private readonly string _workerUserName;

        // Reintentos EN LÍNEA mientras el usuario espera en el diálogo (ademas del reintento en
        // segundo plano de SyncTicketsAsync, que sigue solo cada tick de SyncWorker indefinidamente
        // después de esto). Cortos a propósito: no tiene sentido tener el diálogo bloqueado con
        // "Enviando…" por el backoff largo de WebSocketService (hasta 60s) para una acción
        // interactiva — si estos 3 intentos rápidos no alcanzan, el ticket ya quedó encolado y el
        // usuario puede cerrar el diálogo tranquilo.
        private static readonly TimeSpan[] TicketRetryDelays = { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) };

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public SyncService(
            StateRepository stateRepository,
            AppUsageRepository appUsageRepository,
            ScreenshotRepository screenshotRepository,
            TicketRepository ticketRepository,
            Session sessionRepository,
            TokenService tokenService,
            ILogger<SyncService> logger)
        {
            _stateRepository = stateRepository;
            _appUsageRepository = appUsageRepository;
            _screenshotRepository = screenshotRepository;
            _ticketRepository = ticketRepository;
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
            await SyncTicketsAsync();
            await RefreshStateCatalogAsync();
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

        // Encola el ticket localmente ANTES de intentar enviarlo (nunca se pierde aunque el POST
        // falle) y hace unos pocos reintentos rápidos mientras el usuario espera en el diálogo. Si
        // ninguno pega, queda con Synced=0 y SyncTicketsAsync lo reintenta solo en cada tick de
        // SyncWorker hasta que funcione — sin límite de tiempo, a diferencia de los reintentos en
        // línea de acá.
        public async Task<TicketSaveResult> SaveTicketAsync(string category, string description)
        {
            string displayName = GetDisplayName();
            var ticketId = await _ticketRepository.InsertAsync(category, description, displayName);

            for (int attempt = 0; ; attempt++)
            {
                var (success, message) = await PostTicketAsync(category, description, displayName);
                if (success)
                {
                    await _ticketRepository.MarkSyncedAsync(ticketId);
                    return new TicketSaveResult(true, message);
                }
                if (attempt >= TicketRetryDelays.Length) break;
                await Task.Delay(TicketRetryDelays[attempt]);
            }

            return new TicketSaveResult(false,
                "No se pudo enviar en este momento. Quedó guardado y se reintentará automáticamente.");
        }

        // Prioridad: 1) Session.FullName, el full_name real del appuser tal como lo tiene la API
        // (llega por el UserInfoMessage de WS al sincronizar, ver Session.SetUserInfo) — el nombre
        // "correcto" de negocio, no depende de Windows/AD para nada. 2) UserPrincipal.Current, que
        // requiere que el equipo esté unido a un dominio y pueda contactar un controlador de
        // dominio — en un equipo de workgroup (o con el DC inalcanzable) lanza
        // PrincipalServerDownException ("The server could not be contacted"); antes esto reventaba
        // ANTES de encolar el ticket, saltándose por completo la garantía de "nunca se pierde".
        // 3) Environment.UserName como último recurso si ninguno de los dos anteriores sirve.
        private string GetDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(_sessionRepository.FullName))
                return _sessionRepository.FullName!;

            try
            {
                return System.DirectoryServices.AccountManagement.UserPrincipal.Current.DisplayName
                    ?? Environment.UserName;
            }
            catch
            {
                return Environment.UserName;
            }
        }

        // Un ticket es un solo objeto por request (no un array como States/AppUsage), así que cada
        // fila pendiente se reintenta con su propio POST, no en lote.
        private async Task<(bool Success, string Message)> PostTicketAsync(string category, string description, string usuario)
        {
            var data = new { categoria = category, descripcion = description, usuario };
            var content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");

            _logger.LogInformation("[API] POST {Url} — Ticket: {Cat}", _ticketsUrl, category);
            var sw = Stopwatch.StartNew();
            try
            {
                var response = await _httpClient.PostAsync(_ticketsUrl, content);
                sw.Stop();
                _logger.LogInformation("[API] Ticket → {Status} ({Ms}ms)", response.StatusCode, sw.ElapsedMilliseconds);

                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[API] Ticket rechazado: {Status} — {Body}", response.StatusCode, json);
                    return (false, $"Error {(int)response.StatusCode}");
                }

                string message = json;
                try
                {
                    var items = JsonSerializer.Deserialize<List<JsonElement>>(json);
                    if (items?.Count > 0 && items[0].TryGetProperty("data", out var dataProp))
                        message = dataProp.GetString() ?? json;
                }
                catch { /* respuesta no tiene el shape esperado, se usa el body crudo */ }

                return (true, message);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "[API] Excepción al enviar ticket");
                return (false, ex.Message);
            }
        }

        private async Task SyncTicketsAsync()
        {
            var pending = await _ticketRepository.GetUnsyncedAsync();
            foreach (var t in pending)
            {
                var (success, _) = await PostTicketAsync(t.Category, t.Description, t.Usuario);
                if (success)
                {
                    await _ticketRepository.MarkSyncedAsync(t.Id);
                    _logger.LogInformation("[Sync] Ticket #{Id} enviado en reintento en segundo plano", t.Id);
                }
            }
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

        public IReadOnlyList<StateCategoryCatalogItem> StateCategories { get; private set; } = Array.Empty<StateCategoryCatalogItem>();
        public IReadOnlyList<StateCatalogItem> States { get; private set; } = Array.Empty<StateCatalogItem>();
        public IReadOnlySet<int> HiddenStateCodes { get; private set; } = new HashSet<int>();
        public event Action? StateCatalogRefreshed;
        public bool CanCreateTickets => _sessionRepository.CanCreateTickets;

        public async Task RefreshStateCatalogAsync()
        {
            var categories = await GetStateCategoriesAsync();
            if (categories is not null) StateCategories = categories;

            var states = await GetStatesAsync();
            if (states is not null) States = states;

            var groupId = _sessionRepository.GroupId;
            if (groupId is not null)
            {
                var hidden = await GetGroupStateVisibilityAsync(groupId.Value);
                if (hidden is not null) HiddenStateCodes = hidden.Select(h => h.Code).ToHashSet();
            }
            else
            {
                HiddenStateCodes = new HashSet<int>();
            }

            if (categories is not null || states is not null)
                StateCatalogRefreshed?.Invoke();
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

        public async Task<List<GroupStateVisibilityItem>?> GetGroupStateVisibilityAsync(int groupId)
        {
            var url = $"{_apiUrl}/group-state-visibility?group_id={groupId}";
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
                return JsonSerializer.Deserialize<List<GroupStateVisibilityItem>>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[API] No se pudo consultar la visibilidad de estados del grupo");
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
            var today = DateOnly.FromDateTime(DateTime.Now);

            _logger.LogInformation("[API] Consultando programación efectiva del día {Day} para {Email}", today, email);

            var appuser = await GetAppuserByEmailAsync(email);
            if (appuser is null)
            {
                _logger.LogWarning("[API] No se encontró appuser para {Email}", email);
                return null;
            }

            return await GetEffectiveScheduleAsync(appuser.Id, today);
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

        // GET /schedules/effective resuelve en el backend (horario fijo O rotación, ver
        // ScheduleService.getEffectiveSchedule) — reemplaza el fetch-todo-y-filtrar anterior
        // (GET /schedules?appuser_id=, que el backend ignoraba por completo devolviendo la tabla
        // ENTERA: cada agente terminaba quedándose con el horario de OTRA persona al azar —
        // cualquiera que matcheara el día de la semana primero en esa lista sin filtrar — y
        // programando su fin de jornada/apagado automático según ESE horario ajeno). Además esta
        // ruta sí resuelve rotation_cycles, que el código anterior no consultaba en absoluto.
        private async Task<Programation?> GetEffectiveScheduleAsync(int appuserId, DateOnly date)
        {
            var dateStr = date.ToString("yyyy-MM-dd");
            var url = $"{_apiUrl}/schedules/effective?appuser_id={appuserId}&date={dateStr}";
            _logger.LogDebug("[API] GET {Url}", url);
            try
            {
                var sw = Stopwatch.StartNew();
                var res = await _httpClient.GetAsync(url).ConfigureAwait(false);
                sw.Stop();
                _logger.LogInformation("[API] GetEffectiveSchedule appuserId={Id} → {Status} ({Ms}ms)", appuserId, res.StatusCode, sw.ElapsedMilliseconds);

                if (res.StatusCode == System.Net.HttpStatusCode.NoContent) return null; // sin horario ese día
                if (!res.IsSuccessStatusCode) return null;

                var json = await res.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonSerializer.Deserialize<Programation>(json, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[API] No se pudo consultar la programación efectiva para appuserId={Id}", appuserId);
                return null;
            }
        }
    }
}
