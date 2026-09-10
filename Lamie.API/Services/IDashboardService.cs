using Lamie.Application.Dashboard;

namespace Lamie.API.Services;

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(
        string period,
        bool includeShippingFeeInRevenue,
        CancellationToken cancellationToken);
}
