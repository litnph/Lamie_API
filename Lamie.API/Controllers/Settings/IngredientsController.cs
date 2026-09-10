using Lamie.Application.Identity;
using Lamie.Application.Ingredients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers.Settings;

[ApiController]
[Authorize(Policy = PermissionNames.IngredientsView)]
[Route("api/settings/ingredients")]
public sealed class IngredientsController : ControllerBase
{
    private readonly IIngredientCatalogService _service;

    public IngredientsController(IIngredientCatalogService service)
    {
        _service = service;
    }

    [HttpGet]
    public Task<PagedIngredientsDto> List(
        [FromQuery] CatalogListQuery query,
        CancellationToken cancellationToken) =>
        _service.ListIngredientsAsync(query, cancellationToken);

    [HttpGet("{id:int}")]
    public Task<IngredientDto> Get(int id, CancellationToken cancellationToken) =>
        _service.GetIngredientAsync(id, cancellationToken);

    [HttpPost]
    [Authorize(Policy = PermissionNames.IngredientsManage)]
    public async Task<IActionResult> Create(
        SaveIngredientRequest request,
        CancellationToken cancellationToken)
    {
        var id = await _service.CreateIngredientAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = PermissionNames.IngredientsManage)]
    public async Task<IActionResult> Update(
        int id,
        SaveIngredientRequest request,
        CancellationToken cancellationToken)
    {
        await _service.UpdateIngredientAsync(id, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = PermissionNames.IngredientsManage)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _service.DeleteIngredientAsync(id, cancellationToken);
        return NoContent();
    }
}
