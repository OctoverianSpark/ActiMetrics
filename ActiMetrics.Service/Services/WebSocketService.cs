using Microsoft.Extensions.Logging;
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
        private readonly TokenService _tokenService;
        private readonly Session _session;
        private readonly ILogger<WebSocketService> _logger;
        private ClientWebSocket _client = new();

        private const string ServerUrl = "wss://gotracerconn.asistentevirtualsas.com";

        private static readonly JsonSerializerOptions _sendOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public event Action<(string Title, string Text)>? OnNotification;
        public event Action? OnRestart;

        private CancellationTokenSource? _sessionCts;
        private DateTime _lastReconnectTrigger = DateTime.MinValue;

        public WebSocketService(TokenService tokenService, Session session, ILogger<WebSocketService> logger)
        {
            _tokenService = tokenService;
            _session = session;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken stoppingToken)
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
            System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            int consecutiveFails = 0;

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    _sessionCts = sessionCts;

                    try
                    {
                        // ── Generar token y conectar ─────────────────────────────────────────
                        _logger.LogInformation("[WS] Generando token JWT (intento #{N})...", consecutiveFails + 1);
                        var token = await _tokenService.GenerateTokenAsync(sessionCts.Token);

                        _client = new ClientWebSocket();
                        _logger.LogInformation("[WS] Conectando a {Url}...", ServerUrl);
                        await _client.ConnectAsync(new Uri($"{ServerUrl}?token={token}"), sessionCts.Token);

                        consecutiveFails = 0;
                        _logger.LogInformation("[WS] Conexión WebSocket establecida exitosamente");

                        await SendSyncDataAsync(sessionCts.Token);

                        await ReceiveLoopAsync(sessionCts.Token);

                        _logger.LogInformation("[WS] Desconectado del servidor, reconectando...");
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("[WS] Apagado solicitado, cerrando WebSocket");
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancelado por ForceReconnect (wake / cambio de red)
                        _logger.LogInformation("[WS] Reconexión forzada por cambio de red o wake, reintentando en 1.5s...");
                        try { await Task.Delay(1500, stoppingToken); } catch { break; }
                    }
                    catch (Exception ex)
                    {
                        consecutiveFails++;
                        var delaySec = Math.Min(5 * consecutiveFails, 60);
                        _logger.LogError(ex, "[WS] Error de conexión (intento #{N}), reintentando en {D}s", consecutiveFails, delaySec);
                        try { await Task.Delay(TimeSpan.FromSeconds(delaySec), sessionCts.Token); }
                        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { } // red recuperada → reintentar
                        catch (OperationCanceledException) { break; }
                    }
                    finally
                    {
                        // CloseAsync con timeout de 3s para no colgar si la red está caída
                        if (_client?.State == WebSocketState.Open || _client?.State == WebSocketState.CloseReceived)
                        {
                            using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                            try { await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", closeCts.Token); }
                            catch { }
                        }
                        _client?.Dispose();
                    }
                }
            }
            finally
            {
                Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
                System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            }
        }

        private async Task SendSyncDataAsync(CancellationToken ct)
        {
            var (brand, model) = TokenService.GetMachineInfo();
            var message = new WsSyncDataMessage
            {
                Type = WsMessageType.SyncData,
                Hostname = Environment.MachineName,
                Ip = _tokenService.GetLocalIp(),
                Username = Environment.UserName,
                MachineBrand = brand,
                MachineModel = model
            };

            var json = JsonSerializer.Serialize(message, _sendOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            _logger.LogInformation("[WS] Enviando SyncData...");
            await _client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        private void ForceReconnect(string reason)
        {
            if ((DateTime.Now - _lastReconnectTrigger).TotalSeconds < 5) return;
            _lastReconnectTrigger = DateTime.Now;
            _logger.LogInformation("[WS] {Reason} — forzando reconexión WebSocket", reason);
            try { _client?.Abort(); } catch { }
            try { _sessionCts?.Cancel(); } catch (ObjectDisposedException) { }
        }

        private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume)
                ForceReconnect("Sistema despertó de suspensión");
        }

        private void OnNetworkAvailabilityChanged(object? sender, System.Net.NetworkInformation.NetworkAvailabilityEventArgs e)
        {
            _logger.LogInformation("[WS] Red disponible: {Available}", e.IsAvailable);
            if (e.IsAvailable) ForceReconnect("Red disponible");
        }

        private void OnNetworkAddressChanged(object? sender, EventArgs e)
        {
            ForceReconnect("Dirección de red cambió");
        }

        private async Task ReceiveLoopAsync(CancellationToken stoppingToken)
        {
            var buffer = new ArraySegment<byte>(new byte[4096]);

            while (_client.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                try
                {
                    do
                    {
                        result = await _client.ReceiveAsync(buffer, stoppingToken);
                        ms.Write(buffer.Array!, buffer.Offset, result.Count);
                    }
                    while (!result.EndOfMessage);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("[WS] Receive loop cancelado por apagado");
                    break;
                }
                catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("[WS] Receive loop cancelado por token");
                    break;
                }
                catch (TaskCanceledException ex)
                {
                    _logger.LogWarning(ex, "[WS] Receive task cancelado inesperadamente");
                    break;
                }
                catch (WebSocketException ex)
                {
                    _logger.LogWarning(ex, "[WS] Error en receive, reconectando...");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("[WS] Servidor cerró la conexión: {Status} {Desc}",
                        _client.CloseStatus, _client.CloseStatusDescription);
                    await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
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
            _logger.LogDebug("[WS] Mensaje recibido: {Type}", message?.GetType().Name ?? "null");

            if (message is null) return;

            await (message switch
            {
                WsActionMessage m => HandleActionAsync(m),
                WsFileMessage m => HandleFileAsync(m),
                WsNotificationMessage m => HandleNotificationAsync(m),
                WsUserInfoMessage m => HandleUserInfoAsync(m),
                _ => Task.CompletedTask
            });
        }

        private async Task HandleActionAsync(WsActionMessage m)
        {
            _logger.LogInformation("[WS] Acción recibida: {Action}", m.Action);
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
            _logger.LogInformation("[WS] Sync solicitado por servidor");
            return Task.CompletedTask;
        }

        private Task HandleUserInfoAsync(WsUserInfoMessage m)
        {
            _logger.LogInformation("[WS] UserInfo recibido: role={Role}, group={Group}, absence_status={AbsenceStatus}",
                m.Role, m.Group, m.Absence_Status);
            _session.SetUserInfo(m.Appuser_Id, m.Full_Name, m.Role, m.Group, m.Group_Id, m.Absence_Status, m.Access_Level);
            return Task.CompletedTask;
        }

        private Task HandleNotificationAsync(WsNotificationMessage m)
        {
            _logger.LogInformation("[WS] Notificación: {Title} — {Text}", m.Title, m.Text);
            OnNotification?.Invoke((m.Title, m.Text));
            return Task.CompletedTask;
        }

        private async Task HandleFileAsync(WsFileMessage m)
        {
            _logger.LogInformation("[WS] Archivo recibido: {Name} ({Size} bytes)", m.FileName, m.FileSize);

            if (string.IsNullOrEmpty(m.Data)) return;
            var bytes = Convert.FromBase64String(m.Data);
            var folder = m.SavePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents", "TracerFiles");

            Directory.CreateDirectory(folder);
            var filePath = Path.Combine(folder, m.FileName);
            await File.WriteAllBytesAsync(filePath, bytes);

            _logger.LogInformation("[WS] Archivo guardado → {Path}", filePath);
            OnNotification?.Invoke(("Archivo recibido", m.FileName));
        }
    }
}
