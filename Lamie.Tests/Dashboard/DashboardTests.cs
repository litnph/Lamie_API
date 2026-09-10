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
        var includeShippingParameter = Assert.Single(method.GetParameters(), parameter =>
            parameter.Name == "includeShippingFeeInRevenue");
        Assert.True((bool)includeShippingParameter.DefaultValue!);
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
                ProductRevenue = group.Sum(order => order.SubTotal - order.DiscountTotal),
                ShippingFee = group.Sum(order => (decimal?)order.ShippingFee) ?? 0,
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

    [Fact]
    public async Task DashboardRevenueAppliesShippingOptionToCurrentComparisonAndPoints()
    {
        var databaseName = $"LamieDashboardRevenue_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true")
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);
        var generatedAt = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var createdAt = new DateTime(2026, 8, 27, 4, 0, 0, DateTimeKind.Utc);
            var order = new Order(
                "DASHBOARD-REVENUE",
                Channel.AdminId,
                null,
                new OrderDetails(
                    "Người đặt",
                    string.Empty,
                    "Người nhận",
                    "0912345678",
                    false,
                    false,
                    null,
                    null,
                    null,
                    null,
                    createdAt.AddDays(1),
                    null,
                    0,
                    50000,
                    null,
                    null,
                    null),
                [new OrderItemSnapshot(null, null, "Hoa theo yêu cầu", null, 500000, 1)],
                createdAt,
                null,
                "dashboard-test");
            order.ChangePaymentStatus(PaymentStatus.Paid, createdAt.AddMinutes(1), null, "dashboard-test");
            dbContext.Orders.Add(order);
            await dbContext.SaveChangesAsync();

            var service = new DashboardService(dbContext, new FixedTimeProvider(generatedAt));
            var excludingShipping = await service.GetAsync("7d", false, CancellationToken.None);
            var includingShipping = await service.GetAsync("7d", true, CancellationToken.None);

            Assert.Equal(500000, excludingShipping.Revenue!.CurrentRevenue);
            Assert.Equal(500000, excludingShipping.Revenue.CurrentProductRevenue);
            Assert.Equal(50000, excludingShipping.Revenue.CurrentShippingFee);
            Assert.False(excludingShipping.Revenue.IncludeShippingFeeInRevenue);
            Assert.Equal(500000, excludingShipping.Revenue.Points.Sum(point => point.Revenue));
            Assert.Equal(550000, includingShipping.Revenue!.CurrentRevenue);
            Assert.Equal(550000, includingShipping.Revenue.Points.Sum(point => point.Revenue));
            Assert.Equal(500000, includingShipping.Revenue.Points.Sum(point => point.ProductRevenue));
            Assert.Equal(50000, includingShipping.Revenue.Points.Sum(point => point.ShippingFee));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
