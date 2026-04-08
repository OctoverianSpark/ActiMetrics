using System.Net.Http;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using Tracer.Data;
using Tracer.Shared.Models;

namespace Tracer.Service.Services
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
            _apiUrl = "http://localhost:3000";

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

        protected async Task GetSchedule(int personal_id)
        {
            var response = await _httpClient.GetAsync($"{_apiUrl}/schedules?personal={personal_id}");

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var schedules = JsonSerializer.Deserialize<List<Schedule>>(json);
                var programations = schedules?.Select(s => s.Programation_Id).ToList();

                Console.WriteLine($"[Sync] Programaciones obtenidas: {programations?.Count}");
                Console.WriteLine($"[Sync] Horarios obtenidos: {schedules?.Count}");
            }
            else
            {
                Console.WriteLine($"[Sync] Error al obtener horarios: {response.StatusCode}");
            }
        }

    }
}