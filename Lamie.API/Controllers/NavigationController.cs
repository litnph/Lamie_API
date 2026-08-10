using System.Security.Claims;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/navigation")]
public sealed class NavigationController : ControllerBase
{
    private readonly INavigationService _navigationService;

    public NavigationController(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    [HttpGet("me")]
    public Task<IReadOnlyList<CurrentNavigationItemDto>> Me(CancellationToken cancellationToken) =>
        _navigationService.GetCurrentUserNavigationAsync(GetUserId(), cancellationToken);

    [HttpGet("me/routes")]
    public Task<IReadOnlyList<CurrentNavigationRouteDto>> MyRoutes(CancellationToken cancellationToken) =>
        _navigationService.GetCurrentUserRoutesAsync(GetUserId(), cancellationToken);

    [HttpGet]
    [Authorize(Policy = PermissionNames.NavigationView)]
    public Task<IReadOnlyList<NavigationManagementDto>> List(CancellationToken cancellationToken) =>
        _navigationService.GetNavigationAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PermissionNames.NavigationView)]
    public Task<NavigationManagementDto> Get(Guid id, CancellationToken cancellationToken) =>
        _navigationService.GetNavigationAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Policy = PermissionNames.NavigationManage)]
    public async Task<ActionResult<NavigationManagementDto>> Create(
        SaveNavigationRequest request,
        CancellationToken cancellationToken)
    {
        var item = await _navigationService.CreateNavigationAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionNames.NavigationManage)]
    public async Task<IActionResult> Update(
        Guid id,
        SaveNavigationRequest request,
        CancellationToken cancellationToken)
    {
        await _navigationService.UpdateNavigationAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionNames.NavigationManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _navigationService.DeleteNavigationAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("reorder")]
    [Authorize(Policy = PermissionNames.NavigationManage)]
    public async Task<IActionResult> Reorder(
        NavigationReorderRequest request,
        CancellationToken cancellationToken)
    {
        await _navigationService.ReorderNavigationAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/enable")]
    [Authorize(Policy = PermissionNames.NavigationManage)]
    public async Task<IActionResult> Enable(Guid id, CancellationToken cancellationToken)
    {
        await _navigationService.SetNavigationEnabledAsync(id, true, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/disable")]
    [Authorize(Policy = PermissionNames.NavigationManage)]
    public async Task<IActionResult> Disable(Guid id, CancellationToken cancellationToken)
    {
        await _navigationService.SetNavigationEnabledAsync(id, false, cancellationToken);
        return NoContent();
    }

    private Guid GetUserId()
    {
        var value = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : throw new UnauthorizedException();
    }
}
