using Lamie.API.Services;
using Lamie.Application.Dashboard;
using Lamie.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lamie.API.Controllers;

[ApiController]
[Authorize(Policy = PermissionNames.DashboardView)]
[Route("api/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet]
    public Task<DashboardDto> Get(
        [FromQuery] string period = "30d",
        [FromQuery] bool includeShippingFeeInRevenue = true,
        CancellationToken cancellationToken = default) =>
        _dashboardService.GetAsync(period, includeShippingFeeInRevenue, cancellationToken);
}
