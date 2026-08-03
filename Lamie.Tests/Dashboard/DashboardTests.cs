using System.Text.Json;
using Lamie.API.Controllers;
using Lamie.API.Services;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Lamie.Tests.Dashboard;

public sealed class DashboardTests
{
    [Fact]
    public void ControllerUsesDashboardViewPolicyAndSingleAggregateRoute()
    {
        var authorize = Assert.Single(
            typeof(DashboardController).GetCustomAttributes(typeof(AuthorizeAttribute), true)) as AuthorizeAttribute;
        Assert.Equal(PermissionNames.DashboardView, authorize?.Policy);
        var method = typeof(DashboardController).GetMethod(nameof(DashboardController.Get));
        Assert.NotNull(method);
        var route = Assert.Single(method!.GetCustomAttributes(typeof(HttpGetAttribute), true)) as HttpGetAttribute;
        Assert.Null(route?.Template);
    }

    [Theory]
    [InlineData(PaymentStatus.Paid, OrderStatus.Created, true)]
    [InlineData(PaymentStatus.Paid, OrderStatus.Completed, true)]
    [InlineData(PaymentStatus.Paid, OrderStatus.Cancelled, false)]
    [InlineData(PaymentStatus.Deposited, OrderStatus.Completed, false)]
    public void RevenueRuleMatchesSourceDashboard(
        PaymentStatus paymentStatus,
        OrderStatus orderStatus,
        bool expected) =>
        Assert.Equal(expected, DashboardService.CountsAsRevenue(paymentStatus, orderStatus));

    [Fact]
    public void ThirtyDayPeriodUsesHoChiMinhMidnightAndAdjacentPreviousRange()
    {
        var generatedAt = new DateTimeOffset(2026, 7, 28, 10, 30, 0, TimeSpan.Zero);

        var range = DashboardService.CreatePeriod("30d", generatedAt);

        Assert.Equal(30, range.Days);
        Assert.Equal(new DateTimeOffset(2026, 6, 28, 17, 0, 0, TimeSpan.Zero), range.CurrentStart);
        Assert.Equal(new DateTimeOffset(2026, 5, 29, 17, 0, 0, TimeSpan.Zero), range.PreviousStart);
        Assert.Equal(range.CurrentStart.AddTicks(-1), range.PreviousEnd);
        Assert.Equal(generatedAt, range.CurrentEnd);
    }

    [Fact]
    public void InvalidPeriodIsRejected()
    {
        Assert.Throws<ValidationException>(() =>
            DashboardService.CreatePeriod("365d", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void PeriodContractSerializesAsFrontendRange()
    {
        var range = DashboardService.CreatePeriod(
            "7d",
            new DateTimeOffset(2026, 7, 28, 10, 30, 0, TimeSpan.Zero));

        var json = JsonSerializer.Serialize(range, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"key\":\"7d\"", json);
        Assert.Contains("\"currentStart\"", json);
        Assert.Contains("\"previousEnd\"", json);
        Assert.Contains("+00:00", json);
    }

    [Fact]
    public void RevenueDailyAggregateTranslatesAndInventoryIndexCoversDashboardFilter()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=LamieDashboardModelOnly;Trusted_Connection=True")
            .Options;
        using var dbContext = new AppDbContext(options);
        var from = new DateTime(2026, 7, 1, 17, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 7, 28, 10, 30, 0, DateTimeKind.Utc);
        var query = dbContext.Orders
            .Where(order =>
                order.CreatedAt >= from &&
                order.CreatedAt <= to &&
                order.PaymentStatus == PaymentStatus.Paid &&
                order.OrderStatus != OrderStatus.Cancelled)
            .GroupBy(order => order.CreatedAt.AddHours(7).Date)
            .Select(group => new
            {
                Date = group.Key,
                Revenue = group.Sum(order => order.TotalAmount),
                OrderCount = group.Count()
            });

        var sql = query.ToQueryString();
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var productType = model.FindEntityType(typeof(Product));

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DATEADD", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(productType!.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Product.IsActive), nameof(Product.Stock)]));
    }
}
