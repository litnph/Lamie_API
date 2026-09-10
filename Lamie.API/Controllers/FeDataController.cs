using Lamie.Application.FeData;
using Lamie.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.ProductsManage)]
[Route("api/fe-data")]
public sealed class FeDataController : ControllerBase
{
    private readonly IFeDataExportService _exportService;

    public FeDataController(IFeDataExportService exportService)
    {
        _exportService = exportService;
    }

    [HttpPost("export")]
    public Task<FeDataExportResultDto> Export(CancellationToken cancellationToken) =>
        _exportService.ExportAsync(cancellationToken);
}
