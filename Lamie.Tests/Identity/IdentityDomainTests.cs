using System.IdentityModel.Tokens.Jwt;
using Lamie.API.Options;
using Lamie.API.Services;
using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lamie.Tests.Identity;

public sealed class IdentityDomainTests
{
    private static readonly DateTime Baseline = new(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void UserLocksAfterFiveFailedAttemptsAndUnlocksAfterWindow()
    {
        var user = CreateUser();

        for (var attempt = 0; attempt < 5; attempt++)
            user.RecordFailedLogin(Baseline.AddMinutes(attempt));

        Assert.Equal(UserStatus.Locked, user.Status);
        Assert.False(user.CanAttemptLogin(Baseline.AddMinutes(10)));
        Assert.True(user.CanAttemptLogin(Baseline.AddMinutes(20)));
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(0, user.AccessFailedCount);
    }

    [Fact]
    public void RefreshTokenCanOnlyBeActiveBeforeExpiryAndRevocation()
    {
        var user = CreateUser();
        var token = new RefreshToken(user.Id, new string('A', 64), Baseline, Baseline.AddDays(30), "127.0.0.1");

        Assert.True(token.IsActive(Baseline.AddDays(1)));
        token.Revoke(Baseline.AddDays(2), "127.0.0.1", new string('B', 64));
        Assert.False(token.IsActive(Baseline.AddDays(3)));
        Assert.Equal(new string('B', 64), token.ReplacedByTokenHash);
    }

    [Fact]
    public void JwtTokenContainsCompatibleIdentityAndRoleClaims()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "tests",
            Audience = "tests-client",
            SigningKey = "identity-tests-signing-key-with-32-bytes-minimum",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 30
        });
        var service = new JwtTokenService(options, TimeProvider.System);
        var user = CreateUser();

        var result = service.CreateAccessToken(
            user,
            [PermissionNames.ProductsView, PermissionNames.UsersManage]);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        Assert.Contains(jwt.Claims, claim => claim.Type == JwtRegisteredClaimNames.Sub && claim.Value == user.Id.ToString());
        Assert.Equal(nameof(BuiltInRole.Admin), jwt.Payload["role"]?.ToString());
        Assert.Contains(jwt.Claims, claim => claim.Type == "permission" && claim.Value == PermissionNames.ProductsView);
        Assert.Contains(jwt.Claims, claim => claim.Type == "permission" && claim.Value == PermissionNames.UsersManage);
    }

    private static User CreateUser() => new(
        "admin@lamie.test",
        "admin",
        "not-a-plaintext-password",
        "Lamie Admin",
        null,
        BuiltInRole.Admin,
        true,
        Baseline);
}
