using Lamie.Application.Identity;
using Lamie.Application.Ingredients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers.Settings;

[ApiController]
[Authorize(Policy = PermissionNames.IngredientsView)]
[Route("api/settings/measurement-units")]
public sealed class MeasurementUnitsController : ControllerBase
{
    private readonly IIngredientCatalogService _service;

    public MeasurementUnitsController(IIngredientCatalogService service)
    {
        _service = service;
    }

    [HttpGet]
    public Task<PagedMeasurementUnitsDto> List(
        [FromQuery] CatalogListQuery query,
        CancellationToken cancellationToken) =>
        _service.ListUnitsAsync(query, cancellationToken);

    [HttpGet("{id:int}")]
    public Task<MeasurementUnitDto> Get(int id, CancellationToken cancellationToken) =>
        _service.GetUnitAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Policy = PermissionNames.IngredientsManage)]
    public async Task<IActionResult> Create(
        SaveMeasurementUnitRequest request,
        CancellationToken cancellationToken)
    {
        var id = await _service.CreateUnitAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = PermissionNames.IngredientsManage)]
    public async Task<IActionResult> Update(
        int id,
        SaveMeasurementUnitRequest request,
        CancellationToken cancellationToken)
    {
        await _service.UpdateUnitAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = PermissionNames.IngredientsManage)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _service.DeleteUnitAsync(id, cancellationToken);
        return NoContent();
    }
}
