using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.UsersView)]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly IIdentityService _identityService;

    public UsersController(IIdentityService identityService)
    {
        _identityService = identityService;
    }

    [HttpGet]
    public Task<IReadOnlyList<AuthUserDto>> List(CancellationToken cancellationToken) =>
        _identityService.GetUsersAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<AuthUserDto> Get(Guid id, CancellationToken cancellationToken) =>
        _identityService.GetUserAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Policy = PermissionNames.UsersManage)]
    public async Task<ActionResult<AuthUserDto>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await _identityService.CreateUserAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionNames.UsersManage)]
    public async Task<IActionResult> Update(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        await _identityService.UpdateUserAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionNames.UsersManage)]
    public async Task<IActionResult> Disable(Guid id, CancellationToken cancellationToken)
    {
        await _identityService.DisableUserAsync(id, RemoteIp(), cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:guid}/reset-password")]
    [Authorize(Policy = PermissionNames.UsersManage)]
    public async Task<IActionResult> ResetPassword(
        Guid id,
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _identityService.ResetPasswordAsync(id, request, RemoteIp(), cancellationToken);
        return NoContent();
    }

    private string? RemoteIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
