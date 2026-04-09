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
        private readonly ScreenshotRepository _screenshotRepository;
        private readonly HttpClient _httpClient;
        private readonly string _apiUrl;
        private readonly string _workerId;
        private readonly string _workerUserName;
        private readonly string _ticketsUrl;
        public SyncService(StateRepository stateRepository, AppUsageRepository appUsageRepository, ScreenshotRepository screenshotRepository)
        {
            _stateRepository = stateRepository;
            _appUsageRepository = appUsageRepository;
            _screenshotRepository = screenshotRepository;
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


        public async Task<Programation?> GetTodayScheduleAsync(int personalId)
        {
            var response = await _httpClient.GetAsync($"{_apiUrl}/schedules?personal_id={personalId}").ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var schedules = JsonSerializer.Deserialize<List<Schedule>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var today = DateTime.Now.DayOfWeek switch
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

            Console.WriteLine($"[Sync] Día actual: {today}");
            Console.WriteLine($"[Sync] Horarios recibidos: {schedules?.Count ?? 0}");

            var schedule = schedules?.FirstOrDefault(s => s.Day_Of_Week == today);

            if (schedule is null)
            {
                Console.WriteLine($"[Sync] Sin programación para el día {today}");
                return null;
            }

            var res = await _httpClient.GetAsync($"{_apiUrl}/programations/{schedule.Programation_Id}").ConfigureAwait(false);

            if (!res.IsSuccessStatusCode) return null;

            json = await res.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<Programation>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
    }
}