using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;

namespace ActiMetrics.Service.Services
{
    public class TokenService(Session session, IConfiguration configuration, ILogger<TokenService> logger)
    {
        private readonly Session _session = session;
        private readonly IConfiguration _configuration = configuration;
        private readonly ILogger<TokenService> _logger = logger;

        public async Task<string> GenerateTokenAsync(CancellationToken ct = default)
        {
            string secret = _configuration["Secret"]
                ?? throw new InvalidOperationException("Falta 'Secret' en appsettings");

            var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var email = _session.GetEmail() ?? string.Empty;

            _logger.LogInformation("[Token] Generando JWT para {Email}", email);

            var (brand, model) = GetMachineInfo();
            var ip = await GetPublicIpAsync(ct);

            _logger.LogDebug("[Token] MachineId={Id}, IP={Ip}", Environment.MachineName, ip);

            var claims = new[]
            {
                new Claim("email",         email),
                new Claim("machineId",     GetMachineId()),
                new Claim("machineSerial", GetMachineSerial()),
                new Claim("machineName",   Environment.MachineName),
                new Claim("machineBrand",  brand),
                new Claim("machineModel",  model),
                new Claim("userName",      Environment.UserName),
                new Claim("displayName",   GetDisplayName()),
                new Claim("ip",            ip),
            };

            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.UtcNow.AddYears(1),
                signingCredentials: creds);

            _logger.LogInformation("[Token] JWT generado correctamente");
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private string GetMachineId() =>
            NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .Select(n => n.GetPhysicalAddress().ToString())
                .FirstOrDefault(m => !string.IsNullOrEmpty(m))
            ?? Environment.MachineName;

        private static (string Brand, string Model) GetMachineInfo()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT Manufacturer, Model FROM Win32_ComputerSystem");
                foreach (var obj in searcher.Get())
                    return (obj["Manufacturer"]?.ToString()?.Trim() ?? string.Empty,
                            obj["Model"]?.ToString()?.Trim()         ?? string.Empty);
            }
            catch { }
            return (string.Empty, string.Empty);
        }

        private string GetMachineSerial()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT SerialNumber FROM Win32_BIOS");
                foreach (var obj in searcher.Get())
                    return obj["SerialNumber"]?.ToString() ?? Environment.MachineName;
            }
            catch { }
            return Environment.MachineName;
        }

        private string GetDisplayName()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT FullName FROM Win32_UserAccount WHERE Name='" + Environment.UserName + "'");
                foreach (var obj in searcher.Get())
                    return obj["FullName"]?.ToString() ?? Environment.UserName;
            }
            catch { }
            return Environment.UserName;
        }

        private async Task<string> GetPublicIpAsync(CancellationToken ct = default)
        {
            try
            {
                // Timeout de 10 s para no bloquear el loop de reconexión WebSocket
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var combined   = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                var localIp = GetRealLocalIp();
                HttpClient http;

                if (localIp != null)
                {
                    var handler = new SocketsHttpHandler
                    {
                        ConnectCallback = async (ctx, innerCt) =>
                        {
                            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                            socket.Bind(new IPEndPoint(localIp, 0));
                            await socket.ConnectAsync(ctx.DnsEndPoint, innerCt);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                    };
                    http = new HttpClient(handler);
                }
                else
                {
                    http = new HttpClient();
                }

                using (http)
                {
                    var ip = (await http.GetStringAsync("https://api.ipify.org", combined.Token)).Trim();
                    _logger.LogDebug("[Token] IP pública obtenida: {Ip}", ip);
                    return ip;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // Propagar cancelación externa
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Token] No se pudo obtener IP pública, usando 'unknown'");
                return "unknown";
            }
        }

        private static IPAddress? GetRealLocalIp()
        {
            var vpnKeywords = new[]
            {
                "vpn", "wireguard", "openvpn", "tap", "tunnel",
                "cisco", "nordvpn", "expressvpn", "mullvad",
                "proton", "fortinet", "globalprotect", "pulse", "sonicwall"
            };

            bool IsVpn(string name) =>
                vpnKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            (n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                             n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) &&
                            !IsVpn(n.Name) && !IsVpn(n.Description)))
            {
                var addr = ni.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork &&
                                         !IPAddress.IsLoopback(a.Address));
                if (addr != null) return addr.Address;
            }
            return null;
        }
    }
}
