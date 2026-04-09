using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ActiMetrics.Shared.Converters;
using ActiMetrics.Shared.Models;

namespace ActiMetrics.Service.Services
{
    public class WebSocketService
    {
        private readonly ScreenshotService _screenshotService;
        private readonly TokenService _tokenService;

        private readonly ILogger<WebSocketService> _logger;
        private readonly string _workerId;
        private ClientWebSocket _client = new();

        private const string ServerPORT = "8080";
        private const string ServerUrl = $"wss://tracerconn.asistentevirtualsas.com";


        public event Action<(string Title, string Text)>? OnNotification;
        public event Action? OnRestart;

        public WebSocketService(ScreenshotService screenshotService, TokenService tokenService, ILogger<WebSocketService> logger)
        {
            _screenshotService = screenshotService;
            _tokenService = tokenService;
            _logger = logger;

        }


        public async Task StartAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {

                try
                {
                    var token = await _tokenService.GenerateTokenAsync();
                    _client = new ClientWebSocket();
                    await _client.ConnectAsync(new Uri($"{ServerUrl}?token={token}"), stoppingToken);
                    _logger.LogInformation("[Tracer] WebSocket connected");
                    await ReceiveLoopAsync(stoppingToken);

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WebSocket connection error");
                    await Task.Delay(5000, stoppingToken); // Espera antes de reconectar



                }
            }
        }




        private async Task ReceiveLoopAsync(CancellationToken stoppingToken)
        {
            var buffer = new ArraySegment<byte>(new byte[4096]);

            while (_client.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                // Acumula fragmentos hasta EndOfMessage
                do
                {
                    result = await _client.ReceiveAsync(buffer, stoppingToken);
                    ms.Write(buffer.Array!, buffer.Offset, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", stoppingToken);
                    break;
                }

                var json = Encoding.UTF8.GetString(ms.ToArray());
                await HandleMessageAsync(json);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();

        private async Task HandleMessageAsync(string json)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(), new WsMessageConverter() }
            };

            var message = JsonSerializer.Deserialize<WsMessage>(json, options);
            _logger.LogInformation($"[Tracer] Received message: {message}");

            if (message is null) return;

            await (message switch
            {
                WsActionMessage m => HandleActionAsync(m),
                WsFileMessage m => HandleFileAsync(m),
                WsNotificationMessage m => HandleNotificationAsync(m),
                _ => Task.CompletedTask
            });
        }
        private async Task HandleActionAsync(WsActionMessage m)
        {

            await (m.Action switch
            {
                WsActionType.Lock => Task.Run(() => LockWorkStation()),
                WsActionType.Restart => Task.Run(() => Process.Start("shutdown", "/r /t 0")),
                WsActionType.Shutdown => Task.Run(() => Process.Start("shutdown", "/s /t 0")),
                WsActionType.Logoff => Task.Run(() => Process.Start("shutdown", "/l")),

                WsActionType.Sync => SyncAsync(),
                WsActionType.RestartApp => Task.Run(() => OnRestart?.Invoke()),
                _ => Task.CompletedTask
            });
        }

        private Task SyncAsync()
        {
            // Por ahora fuerza el flush del intervalo de apps
            Console.WriteLine("[Sync] Forzando sync...");
            return Task.CompletedTask;
        }
        private Task HandleNotificationAsync(WsNotificationMessage m)
        {
            OnNotification?.Invoke((m.Title, m.Text));
            return Task.CompletedTask;
        }
        private async Task HandleFileAsync(WsFileMessage m)
        {
            Console.WriteLine($"[File] FileName: {m.FileName}");
            Console.WriteLine($"[File] FileSize: {m.FileSize}");
            Console.WriteLine($"[File] Base64 length: {m.Data?.Length}");

            var bytes = Convert.FromBase64String(m.Data);
            var folder = m.SavePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents", "TracerFiles");

            Directory.CreateDirectory(folder);
            var filePath = Path.Combine(folder, m.FileName);
            await File.WriteAllBytesAsync(filePath, bytes);

            _logger.LogInformation("[File] Guardado → {path}", filePath);
            OnNotification?.Invoke(("Archivo recibido", m.FileName));
        }


    }
}
