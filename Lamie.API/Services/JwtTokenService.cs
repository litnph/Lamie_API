using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Lamie.API.Options;
using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Lamie.API.Services;

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public AccessTokenResult CreateAccessToken(
        User user,
        IEnumerable<string>? permissions = null,
        string? roleCode = null)
    {
        if (Encoding.UTF8.GetByteCount(_options.SigningKey) < 32)
            throw new InvalidOperationException("Jwt:SigningKey must contain at least 32 bytes.");

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var payload = new JwtPayload
        {
            [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
            [JwtRegisteredClaimNames.UniqueName] = user.UserName,
            ["role"] = roleCode ?? user.Role.ToString(),
            [JwtRegisteredClaimNames.Iss] = _options.Issuer,
            [JwtRegisteredClaimNames.Aud] = _options.Audience,
            [JwtRegisteredClaimNames.Iat] = EpochTime.GetIntDate(now),
            [JwtRegisteredClaimNames.Nbf] = EpochTime.GetIntDate(now),
            [JwtRegisteredClaimNames.Exp] = EpochTime.GetIntDate(expiresAt)
        };

        if (permissions is not null)
        {
            payload["permission"] = permissions.Distinct(StringComparer.Ordinal).ToArray();
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(new JwtHeader(credentials), payload);

        return new AccessTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public RefreshTokenResult CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        var token = Convert.ToBase64String(bytes);
        var expiresAt = _timeProvider.GetUtcNow().UtcDateTime.AddDays(_options.RefreshTokenDays);
        return new RefreshTokenResult(token, HashRefreshToken(token), expiresAt);
    }

    public string HashRefreshToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
