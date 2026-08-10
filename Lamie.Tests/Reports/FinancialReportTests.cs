using System.Text;
using System.Xml.Linq;
using Lamie.API.Controllers;
using Lamie.API.Services;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Identity;
using Lamie.Application.Reports;
using Lamie.Domain.Entities;
using Lamie.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lamie.Tests.Reports;

public sealed class FinancialReportTests
{
    private static readonly DateTimeOffset GeneratedAt =
        new(2026, 8, 3, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ControllerExposesProtectedFinancialAndExportRoutes()
    {
        var authorize = Assert.Single(
            typeof(ReportsController).GetCustomAttributes(typeof(AuthorizeAttribute), true)) as AuthorizeAttribute;
        Assert.Equal(PermissionNames.ReportsView, authorize?.Policy);

        AssertRoute(nameof(ReportsController.Financial), "financial");
        AssertRoute(nameof(ReportsController.ExportExcel), "financial/export/excel");
        AssertRoute(nameof(ReportsController.ExportPrint), "financial/export/print");
    }

    [Fact]
    public void ReportRangeDefaultsToBusinessMonthAndAutomaticallySelectsGranularity()
    {
        var daily = FinancialReportService.ResolveRange(
            new FinancialReportQuery(),
            new DateOnly(2026, 8, 3));
        var monthly = FinancialReportService.ResolveRange(
            new FinancialReportQuery
            {
                From = new DateOnly(2026, 1, 1),
                To = new DateOnly(2026, 4, 30)
            },
            new DateOnly(2026, 8, 3));

        Assert.Equal(new DateOnly(2026, 8, 1), daily.From);
        Assert.Equal("day", daily.GroupBy);
        Assert.Equal("month", monthly.GroupBy);
        Assert.Equal(120, monthly.Days);
    }

    [Theory]
    [InlineData(PaymentStatus.Paid, OrderStatus.Created, true)]
    [InlineData(PaymentStatus.Paid, OrderStatus.Completed, true)]
    [InlineData(PaymentStatus.Paid, OrderStatus.Cancelled, false)]
    [InlineData(PaymentStatus.Deposited, OrderStatus.Created, false)]
    public void RevenueRuleUsesPaidNonCancelledOrders(
        PaymentStatus paymentStatus,
        OrderStatus orderStatus,
        bool expected) =>
        Assert.Equal(expected, FinancialReportService.CountsAsRevenue(paymentStatus, orderStatus));

    [Fact]
    public void ReportRangeRejectsIncompleteInvalidAndOversizedRequests()
    {
        var today = new DateOnly(2026, 8, 3);

        Assert.Throws<ValidationException>(() => FinancialReportService.ResolveRange(
            new FinancialReportQuery { From = new DateOnly(2026, 8, 1) },
            today));
        Assert.Throws<ValidationException>(() => FinancialReportService.ResolveRange(
            new FinancialReportQuery
            {
                From = new DateOnly(2026, 8, 2),
                To = new DateOnly(2026, 8, 1)
            },
            today));
        Assert.Throws<ValidationException>(() => FinancialReportService.ResolveRange(
            new FinancialReportQuery
            {
                From = new DateOnly(2025, 1, 1),
                To = new DateOnly(2026, 8, 1)
            },
            today));
        Assert.Throws<ValidationException>(() => FinancialReportService.ResolveRange(
            new FinancialReportQuery
            {
                From = new DateOnly(2026, 8, 1),
                To = new DateOnly(2026, 8, 3),
                GroupBy = "week"
            },
            today));
    }

    [Fact]
    public void ExcelAndPrintableExportsAreCompleteAndEscapeContent()
    {
        var report = CreateReport("<script>alert('x')</script> & Vật tư");
        var exporter = new FinancialReportExportService();

        var excel = exporter.CreateExcel(report);
        var printable = exporter.CreatePrintableHtml(report);
        using var stream = new MemoryStream(excel.Content);
        var workbook = XDocument.Load(stream);
        var html = Encoding.UTF8.GetString(printable.Content);

        Assert.Equal("application/vnd.ms-excel", excel.ContentType);
        Assert.EndsWith(".xls", excel.FileName, StringComparison.Ordinal);
        Assert.Equal("Workbook", workbook.Root?.Name.LocalName);
        Assert.Equal(3, workbook.Descendants().Count(element => element.Name.LocalName == "Worksheet"));
        Assert.Equal("text/html; charset=utf-8", printable.ContentType);
        Assert.Contains("window.print()", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert('x')</script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceUsesDeliveryDateForRevenueAndCombinesExpenseProfitAndCategoryBreakdown()
    {
        var databaseName = $"LamieReport_{Guid.NewGuid():N}";
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);

        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var createdOutsidePeriod = new DateTime(2026, 7, 31, 10, 0, 0, DateTimeKind.Utc);
            var deliveryInsidePeriod = new DateTime(2026, 8, 2, 18, 0, 0, DateTimeKind.Utc);
            var deliveryOutsidePeriod = new DateTime(2026, 8, 3, 17, 0, 0, DateTimeKind.Utc);
            var paidOrder = CreateOrder("REPORT-PAID", 500000, createdOutsidePeriod, deliveryInsidePeriod);
            paidOrder.ChangePaymentStatus(PaymentStatus.Paid, createdOutsidePeriod.AddMinutes(1), null, "report-test");
            var paidOutsidePeriod = CreateOrder("REPORT-OUTSIDE", 300000, deliveryInsidePeriod, deliveryOutsidePeriod);
            paidOutsidePeriod.ChangePaymentStatus(PaymentStatus.Paid, deliveryInsidePeriod.AddMinutes(1), null, "report-test");
            var cancelledOrder = CreateOrder("REPORT-CANCELLED", 200000, createdOutsidePeriod, deliveryInsidePeriod);
            cancelledOrder.ChangePaymentStatus(PaymentStatus.Paid, createdOutsidePeriod.AddMinutes(1), null, "report-test");
            cancelledOrder.ChangeStatus(OrderStatus.Cancelled, false, createdOutsidePeriod.AddMinutes(2), null, "report-test");
            var category = new ExpenseCategory("Vận chuyển", null, 10, true, createdOutsidePeriod);
            var expense = new Expense(
                category.Id,
                new DateOnly(2026, 8, 3),
                150000,
                "Phí giao hàng",
                null,
                createdOutsidePeriod);
            dbContext.Orders.AddRange(paidOrder, paidOutsidePeriod, cancelledOrder);
            dbContext.ExpenseCategories.Add(category);
            dbContext.Expenses.Add(expense);
            await dbContext.SaveChangesAsync();

            var service = new FinancialReportService(dbContext, new FixedTimeProvider(GeneratedAt));
            var report = await service.GetAsync(new FinancialReportQuery
            {
                From = new DateOnly(2026, 8, 3),
                To = new DateOnly(2026, 8, 3),
                GroupBy = "day"
            }, CancellationToken.None);

            Assert.Equal(500000, report.Revenue);
            Assert.Equal(150000, report.Expense);
            Assert.Equal(350000, report.Profit);
            Assert.Equal(70, report.ProfitMarginPercent);
            Assert.Equal(1, report.OrderCount);
            Assert.Equal(1, report.ExpenseCount);
            Assert.Equal(350000, Assert.Single(report.Points).Profit);
            Assert.Equal("Vận chuyển", Assert.Single(report.ExpensesByCategory).ExpenseCategoryName);
            Assert.Contains("ngày giao hàng", report.RevenueBasis);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private static Order CreateOrder(
        string code,
        decimal amount,
        DateTime createdAt,
        DateTime deliveryAt) => new(
        code,
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
            deliveryAt,
            null,
            0,
            0,
            null,
            null,
            null),
        [new OrderItemSnapshot(null, null, "Hoa theo yêu cầu", null, amount, 1)],
        createdAt,
        null,
        "report-test");

    private static FinancialReportDto CreateReport(string categoryName)
    {
        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 3);
        return new FinancialReportDto(
            new FinancialReportPeriodDto(from, to, "day", 3),
            GeneratedAt,
            500000,
            150000,
            350000,
            70,
            1,
            1,
            [new FinancialReportPointDto(from, to, "01/08-03/08", 500000, 150000, 350000, 1, 1)],
            [new FinancialReportExpenseCategoryDto(Guid.NewGuid(), categoryName, 150000, 1)],
            "Revenue basis",
            "Profit basis");
    }

    private static void AssertRoute(string methodName, string template)
    {
        var method = typeof(ReportsController).GetMethod(methodName);
        Assert.NotNull(method);
        var route = Assert.Single(method!.GetCustomAttributes(typeof(HttpGetAttribute), true)) as HttpGetAttribute;
        Assert.Equal(template, route?.Template);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
