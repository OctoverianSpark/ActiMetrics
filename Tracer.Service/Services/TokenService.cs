using System.IdentityModel.Tokens.Jwt;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Tracer.Service.Services
{
    public class TokenService
    {
        private readonly string _secret;

        public TokenService(string secret)
        {
            _secret = secret;
        }

        public async Task<string> GenerateTokenAsync()
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
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