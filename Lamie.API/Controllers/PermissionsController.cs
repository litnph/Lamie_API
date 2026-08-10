using Lamie.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.RolesView)]
[Route("api/admin/permissions")]
public sealed class PermissionsController : ControllerBase
{
    private readonly IPermissionManagementService _permissionService;

    public PermissionsController(IPermissionManagementService permissionService)
    {
        _permissionService = permissionService;
    }

    [HttpGet]
    public Task<PagedPermissionsDto> List(
        [FromQuery] string? search,
        [FromQuery] string? group,
        [FromQuery] bool? system,
        [FromQuery] bool? active,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        _permissionService.GetPermissionsAsync(
            search,
            group,
            system,
            active,
            page,
            pageSize,
            cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<PermissionManagementDto> Get(Guid id, CancellationToken cancellationToken) =>
        _permissionService.GetPermissionAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Policy = PermissionNames.RolesManage)]
    public async Task<ActionResult<PermissionManagementDto>> Create(
        CreatePermissionRequest request,
        CancellationToken cancellationToken)
    {
        var permission = await _permissionService.CreatePermissionAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = permission.Id }, permission);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionNames.RolesManage)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdatePermissionRequest request,
        CancellationToken cancellationToken)
    {
        await _permissionService.UpdatePermissionAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionNames.RolesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _permissionService.DeactivatePermissionAsync(id, cancellationToken);
        return NoContent();
    }
}
