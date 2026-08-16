using Lamie.API.Services;
using Lamie.Application.Addresses;
using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.OrdersView)]
[Route("api/admin/administrative-units")]
public sealed class AdministrativeUnitsController : ControllerBase
{
    private readonly IAdministrativeAddressService _service;

    public AdministrativeUnitsController(IAdministrativeAddressService service)
    {
        _service = service;
    }

    [HttpGet("provinces")]
    public Task<IReadOnlyList<AdministrativeUnitDto>> GetProvinces(
        [FromQuery] AdministrativeScheme scheme = AdministrativeScheme.Current,
        CancellationToken cancellationToken = default) =>
        _service.GetProvincesAsync(scheme, cancellationToken);

    [HttpGet("{code}/children")]
    public Task<IReadOnlyList<AdministrativeUnitDto>> GetChildren(
        string code,
        [FromQuery] AdministrativeScheme scheme = AdministrativeScheme.Current,
        [FromQuery] string? query = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default) =>
        _service.GetChildrenAsync(code, scheme, query, limit, cancellationToken);

    [HttpGet("search")]
    public Task<IReadOnlyList<AdministrativeUnitDto>> Search(
        [FromQuery] string query,
        [FromQuery] AdministrativeScheme? scheme = null,
        [FromQuery] int? hierarchyLevel = null,
        [FromQuery] string? parentCode = null,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default) =>
        _service.SearchAsync(query, scheme, hierarchyLevel, parentCode, limit, cancellationToken);

    [HttpGet("transitions/{legacyCode}")]
    public Task<IReadOnlyList<AdministrativeTransitionDto>> GetTransitions(
        string legacyCode,
        CancellationToken cancellationToken = default) =>
        _service.GetTransitionsAsync(legacyCode, cancellationToken);
}

[ApiController]
[Authorize(Policy = PermissionNames.OrdersView)]
[Route("api/admin/addresses")]
public sealed class AddressesController : ControllerBase
{
    private readonly IAdministrativeAddressService _service;

    public AddressesController(IAdministrativeAddressService service)
    {
        _service = service;
    }

    [HttpPost("resolve")]
    public Task<AddressResolutionDto> Resolve(
        ResolveAddressRequest request,
        CancellationToken cancellationToken) =>
        _service.ResolveAsync(request, cancellationToken);
}
