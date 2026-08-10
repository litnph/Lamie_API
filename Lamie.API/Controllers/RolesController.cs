using Lamie.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.RolesView)]
[Route("api/roles")]
public sealed class RolesController : ControllerBase
{
    private readonly IRoleService _roleService;

    public RolesController(IRoleService roleService)
    {
        _roleService = roleService;
    }

    [HttpGet]
    public Task<IReadOnlyList<RoleDto>> List(
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default) =>
        _roleService.GetRolesAsync(activeOnly, cancellationToken);

    [HttpGet("permissions")]
    public Task<IReadOnlyList<PermissionDto>> Permissions(CancellationToken cancellationToken) =>
        _roleService.GetPermissionsAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<RoleDto> Get(Guid id, CancellationToken cancellationToken) =>
        _roleService.GetRoleAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Policy = PermissionNames.RolesManage)]
    public async Task<ActionResult<RoleDto>> Create(
        SaveRoleRequest request,
        CancellationToken cancellationToken)
    {
        var role = await _roleService.CreateRoleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = role.Id }, role);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermissionNames.RolesManage)]
    public async Task<IActionResult> Update(
        Guid id,
        SaveRoleRequest request,
        CancellationToken cancellationToken)
    {
        await _roleService.UpdateRoleAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermissionNames.RolesManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _roleService.DeleteRoleAsync(id, cancellationToken);
        return NoContent();
    }
}
