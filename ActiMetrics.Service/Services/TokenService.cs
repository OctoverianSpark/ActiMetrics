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

        private static readonly string[] IpServices =
        [
            "https://api.ipify.org",
            "https://checkip.amazonaws.com",
            "https://icanhazip.com",
            "https://api4.my-ip.io/ip",
        ];

        private async Task<string> GetPublicIpAsync(CancellationToken ct = default)
        {
            // Timeout global de 20 s; cada servicio tiene 8 s individuales
            using var globalCts  = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var combined   = CancellationTokenSource.CreateLinkedTokenSource(ct, globalCts.Token);

            var localIp = GetRealLocalIp();

            HttpClient BuildClient()
            {
                if (localIp == null) return new HttpClient();
                var handler = new SocketsHttpHandler
                {
                    ConnectCallback = async (ctx, innerCt) =>
                    {
                        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        try
                        {
                            socket.Bind(new IPEndPoint(localIp, 0));
                        }
                        catch
                        {
                            // Bind fallido (cuenta de servicio sin acceso directo a la interfaz);
                            // reconectar sin bind y dejar que el OS elija la ruta.
                            socket.Dispose();
                            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        }
                        await socket.ConnectAsync(ctx.DnsEndPoint, innerCt);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                };
                return new HttpClient(handler);
            }

            foreach (var service in IpServices)
            {
                if (combined.Token.IsCancellationRequested) break;
                try
                {
                    using var svcCts      = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    using var svcCombined = CancellationTokenSource.CreateLinkedTokenSource(combined.Token, svcCts.Token);
                    using var http        = BuildClient();

                    var ip = (await http.GetStringAsync(service, svcCombined.Token)).Trim();
                    if (!string.IsNullOrWhiteSpace(ip))
                    {
                        _logger.LogDebug("[Token] IP pública obtenida de {Service}: {Ip}", service, ip);
                        return ip;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[Token] {Service} no disponible, intentando siguiente", service);
                }
            }

            _logger.LogWarning("[Token] Ningún servicio de IP respondió");
            return "unknown";
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
