using System.Net.Http;
using System.Reflection.Metadata;
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

        private readonly Session _sessionRepository;
        private readonly ScreenshotRepository _screenshotRepository;
        private readonly HttpClient _httpClient;
        private readonly string _apiUrl;
        private readonly string _workerId;
        private readonly string _workerUserName;
        private readonly string _ticketsUrl;
        public SyncService(StateRepository stateRepository, AppUsageRepository appUsageRepository, ScreenshotRepository screenshotRepository, Session sessionRepository)
        {
            _stateRepository = stateRepository;
            _appUsageRepository = appUsageRepository;
            _screenshotRepository = screenshotRepository;
            _sessionRepository = sessionRepository;
            _httpClient = new HttpClient();
            _apiUrl = "https://tracerapi.asistentevirtualsas.com";

            _ticketsUrl = "https://helpdesk.asistentevirtualsas.com/api/tickets/create";
            _workerId = Environment.MachineName;
            _workerUserName = Environment.UserName;
        }

        public async Task SyncAsync()
        {
            await SyncStatesAsync();
            await SyncAppUsageAsync();
        }
        public async Task SyncScreenshotAsync()
        {


            var screenshots = await _screenshotRepository.GetUnsyncedAsync();

            foreach (Screenshot screenshot in screenshots)
            {
                using var content = new MultipartFormDataContent();
                var fileStream = File.OpenRead(screenshot.FilePath);
                var fileName = Path.GetFileName(screenshot.FilePath);
                var fileContent = new StreamContent(fileStream);
                content.Add(fileContent, "file", fileName);
                content.Add(new StringContent(Environment.MachineName), "machine");

                var response = await _httpClient.PostAsync($"{_apiUrl}/tracer/screenshot", content);

                if (response.IsSuccessStatusCode)
                    Console.WriteLine(response.Content);
                await _screenshotRepository.MarkSyncedAsync(screenshot.Id);
            }
        }


        public async Task<HttpResponseMessage> SaveTicketAsync(string category, string description)
        {
            string username = Environment.UserName;           // "jean.pr"
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
                "application/json"
            );


            var response = await _httpClient.PostAsync(_ticketsUrl, content);
            return response.EnsureSuccessStatusCode();
        }


        private async Task SyncStatesAsync()
        {
            var pending = await _stateRepository.GetUnsyncedAsync();
            if (!pending.Any()) return;

            try
            {
                var json = JsonSerializer.Serialize(pending);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_apiUrl}/tracer/states", content);

                if (response.IsSuccessStatusCode)
                {
                    foreach (var log in pending)
                        await _stateRepository.MarkSyncedAsync(log.Id);

                    Console.WriteLine($"[Sync] {pending.Count()} estados sincronizados");
                }
                else
                {
                    Console.WriteLine($"[Sync] Error estados: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sync] Excepción estados: {ex.Message}");
            }
        }

        private async Task SyncAppUsageAsync()
        {
            var pending = await _appUsageRepository.GetUnsyncedAsync();
            if (!pending.Any()) return;

            try
            {
                var json = JsonSerializer.Serialize(pending);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_apiUrl}/tracer/app-usage", content);

                if (response.IsSuccessStatusCode)
                {
                    foreach (var log in pending)
                        await _appUsageRepository.MarkSyncedAsync(log.Id);

                    Console.WriteLine($"[Sync] {pending.Count()} app usage sincronizados");
                }
                else
                {
                    Console.WriteLine($"[Sync] Error app usage: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sync] Excepción app usage: {ex.Message}");
            }
        }


        // Fuera de la clase, estático para reutilizar
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public async Task<Programation?> GetTodayScheduleAsync()
        {
            var email = _sessionRepository.GetEmail();
            var today = GetToday();

            // 1. Obtener personal y schedules en paralelo si tienes el personal_id cacheado
            // Si no, primero necesitas el Id del personal
            var personal = await GetPersonalByEmailAsync(email);
            if (personal is null) return null;

            // 2. Buscar schedule del día
            var schedule = await GetScheduleForDayAsync(personal.Id, today);
            if (schedule is null)
            {
                Console.WriteLine("[Sync] Sin programación para el día {Day}", today);
                return null;
            }

            // 3. Obtener programation
            return await GetProgramationAsync(schedule.Programation_Id);
        }

        // Métodos privados pequeños y reutilizables
        private async Task<EmployeeData?> GetPersonalByEmailAsync(string email)
        {
            var res = await _httpClient.GetAsync($"{_apiUrl}/personal/findbyemail?email={email}").ConfigureAwait(false);

            Console.WriteLine($"[DEBUG] StatusCode: {res.StatusCode}");
            Console.WriteLine($"[DEBUG] Body: {await res.Content.ReadAsStringAsync()}");
            if (!res.IsSuccessStatusCode) return null;

            var json = await res.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<EmployeeData>(json, _jsonOptions);
        }

        private async Task<Schedule?> GetScheduleForDayAsync(int personalId, Days today)
        {
            var res = await _httpClient.GetAsync($"{_apiUrl}/schedules?personal_id={personalId}").ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;

            var json = await res.Content.ReadAsStringAsync();
            var schedules = JsonSerializer.Deserialize<List<Schedule>>(json, _jsonOptions);
            return schedules?.FirstOrDefault(s => s.Day_Of_Week == today);
        }

        private async Task<Programation?> GetProgramationAsync(int programationId)
        {
            var res = await _httpClient.GetAsync($"{_apiUrl}/programations/{programationId}").ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;

            var json = await res.Content.ReadAsStringAsync();
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