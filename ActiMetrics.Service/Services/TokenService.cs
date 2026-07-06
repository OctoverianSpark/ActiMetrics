using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text;

namespace ActiMetrics.Service.Services
{
    public class TokenService(Session session, IConfiguration configuration, ILogger<TokenService> logger)
    {
        private readonly Session _session = session;
        private readonly IConfiguration _configuration = configuration;
        private readonly ILogger<TokenService> _logger = logger;

        public Task<string> GenerateTokenAsync(CancellationToken ct = default)
        {
            string secret = _configuration["Secret"]
                ?? throw new InvalidOperationException("Falta 'Secret' en appsettings");

            var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var email = _session.GetEmail() ?? string.Empty;

            _logger.LogInformation("[Token] Generando JWT para {Email}", email);

            var (brand, model) = GetMachineInfo();
            var localIp  = GetRealLocalIp()?.ToString() ?? string.Empty;
            var isRdp    = IsRemoteSession();

            _logger.LogDebug("[Token] MachineId={Id}, LocalIP={Ip}, RDP={Rdp}", Environment.MachineName, localIp, isRdp);

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
                new Claim("localIp",       localIp),
                new Claim("isRdp",         isRdp ? "true" : "false"),
            };

            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.UtcNow.AddYears(1),
                signingCredentials: creds);

            _logger.LogInformation("[Token] JWT generado correctamente");
            return Task.FromResult(new JwtSecurityTokenHandler().WriteToken(token));
        }

        private string GetMachineId() =>
            NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .Select(n => n.GetPhysicalAddress().ToString())
                .FirstOrDefault(m => !string.IsNullOrEmpty(m))
            ?? Environment.MachineName;

        public static (string Brand, string Model) GetMachineInfo()
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

        public string? GetLocalIp() => GetRealLocalIp()?.ToString();

        public string GetMachineSerial()
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

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private static bool IsRemoteSession() => GetSystemMetrics(0x1000) != 0; // SM_REMOTESESSION

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
