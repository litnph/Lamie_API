using Lamie.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lamie.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IIdentityService _identityService;

    public AuthController(IIdentityService identityService)
    {
        _identityService = identityService;
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public Task<AuthResultDto> Login(LoginRequest request, CancellationToken cancellationToken) =>
        _identityService.LoginAsync(request, RemoteIp(), cancellationToken);

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("refresh")]
    public Task<AuthResultDto> Refresh(RefreshRequest request, CancellationToken cancellationToken) =>
        _identityService.RefreshAsync(request, RemoteIp(), cancellationToken);

    [Authorize]
    [HttpGet("me")]
    public Task<AuthUserDto> Me(CancellationToken cancellationToken) =>
        _identityService.GetCurrentUserAsync(cancellationToken);

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        await _identityService.LogoutAsync(request, RemoteIp(), cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await _identityService.ChangePasswordAsync(request, RemoteIp(), cancellationToken);
        return NoContent();
    }

    private string? RemoteIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
