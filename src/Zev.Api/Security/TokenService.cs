using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Zev.Api.Domain;

namespace Zev.Api.Security;

public sealed class TokenService(IConfiguration configuration)
{
    public const string RefreshCookieName = "zev-refresh";

    public SymmetricSecurityKey SigningKey
    {
        get
        {
            var key = configuration["Jwt:SigningKey"];
            if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            {
                throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 UTF-8 bytes.");
            }

            return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        }
    }

    public string Issuer => configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is required.");
    public string Audience => configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience is required.");

    public (string AccessToken, DateTimeOffset ExpiresUtc) CreateAccessToken(AppUser user)
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(15);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim("security_version", user.SecurityVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ],
            notBefore: DateTime.UtcNow,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public static string NewRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));

    public static string HashRefreshToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
