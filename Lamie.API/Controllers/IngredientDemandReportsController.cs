using Lamie.Application.Identity;
using Lamie.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.IngredientReportsView)]
[Route("api/reports/ingredient-demand")]
public sealed class IngredientDemandReportsController : ControllerBase
{
    private readonly IIngredientDemandReportService _service;

    public IngredientDemandReportsController(IIngredientDemandReportService service)
    {
        _service = service;
    }

    [HttpGet]
    public Task<IngredientDemandReportDto> Get(
        [FromQuery] IngredientDemandQuery query,
        CancellationToken cancellationToken) =>
        _service.GetAsync(query, cancellationToken);
}
