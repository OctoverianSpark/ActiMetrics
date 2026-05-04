using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;

namespace ActiMetrics.Service.Services
{
    public class TokenService
    {
        private readonly Session _session;
        private readonly IConfiguration _configuration;

        public TokenService(Session session, IConfiguration configuration)
        {
            _session = session;

            _configuration = configuration;
        }

        public async Task<string> GenerateTokenAsync()
        {
            string secret = _configuration["Secret"] ?? throw new InvalidOperationException("Falta 'Secret' en appsettings");
            Console.WriteLine($"[TokenService] Generating token for secret: {secret}");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var email = _session.GetEmail() ?? string.Empty;
            Console.WriteLine($"[TokenService] Generating token for email: {email}");

            var claims = new[]
            {
                new Claim("email",         email),
                new Claim("machineId",     GetMachineId()),
                new Claim("machineSerial", GetMachineSerial()),
                new Claim("machineName",   Environment.MachineName),
                new Claim("userName",      Environment.UserName),
                new Claim("displayName",   GetDisplayName()),
                new Claim("ip",            await GetPublicIpAsync())
            };

            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.UtcNow.AddYears(1),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private string GetMachineId()
        {
            return NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .Select(n => n.GetPhysicalAddress().ToString())
                .FirstOrDefault(m => !string.IsNullOrEmpty(m)) ?? Environment.MachineName;
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

        private async Task<string> GetPublicIpAsync()
        {
            try
            {
                var localIp = GetRealLocalIp();
                HttpClient http;

                if (localIp != null)
                {
                    var handler = new SocketsHttpHandler
                    {
                        ConnectCallback = async (ctx, ct) =>
                        {
                            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                            socket.Bind(new IPEndPoint(localIp, 0));
                            await socket.ConnectAsync(ctx.DnsEndPoint, ct);
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
                    return (await http.GetStringAsync("https://api.ipify.org")).Trim();
            }
            catch
            {
                return "unknown";
            }
        }

        private static IPAddress? GetRealLocalIp()
        {
            var vpnKeywords = new[] { "vpn", "wireguard", "openvpn", "tap", "tunnel",
                                      "cisco", "nordvpn", "expressvpn", "mullvad",
                                      "proton", "fortinet", "globalprotect", "pulse", "sonicwall" };

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