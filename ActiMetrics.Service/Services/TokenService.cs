using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
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
            string secret = _configuration["Secret"];
            Console.WriteLine($"[TokenService] Generating token for secret: {secret}");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var email = _session.GetEmail();
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
                using var http = new HttpClient();
                var ip = await http.GetStringAsync("https://api.ipify.org");
                return ip.Trim();
            }
            catch
            {
                return "unknown";
            }
        }
    }
}