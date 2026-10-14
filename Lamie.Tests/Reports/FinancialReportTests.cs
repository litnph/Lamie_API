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
        Assert.True(new FinancialReportQuery().IncludeShippingFeeInRevenue);
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
    [InlineData(PaymentStatus.Paid, OrderStatus.Created, false)]
    [InlineData(PaymentStatus.Paid, OrderStatus.Completed, true)]
    [InlineData(PaymentStatus.Paid, OrderStatus.Cancelled, false)]
    [InlineData(PaymentStatus.Deposited, OrderStatus.Created, false)]
    public void RevenueRuleUsesCompletedOrdersRegardlessOfPaymentTiming(
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
        var decodedHtml = System.Net.WebUtility.HtmlDecode(html);

        Assert.Equal("application/vnd.ms-excel", excel.ContentType);
        Assert.EndsWith(".xls", excel.FileName, StringComparison.Ordinal);
        Assert.Equal("Workbook", workbook.Root?.Name.LocalName);
        Assert.Equal(3, workbook.Descendants().Count(element => element.Name.LocalName == "Worksheet"));
        Assert.Equal("text/html; charset=utf-8", printable.ContentType);
        Assert.Contains("window.print()", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("Phí ship thực tế", decodedHtml);
        Assert.Contains("Chi phí khác", decodedHtml);
        Assert.Contains("Chênh lệch tạm tính", decodedHtml);
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
            var productCategory = new Category(1);
            productCategory.AddOrUpdateTranslation("vi-VN", "Hoa bó", null);
            var productType = new ProductType("FRESH_FLOWER", 1);
            productType.AddOrUpdateTranslation("vi", "Hoa tươi", null);
            dbContext.Categories.Add(productCategory);
            dbContext.ProductTypes.Add(productType);
            await dbContext.SaveChangesAsync();
            var product = new Product("REPORT-FLOWER", 500000, 0, productCategory.Id, productType.Id, false);
            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync();

            var paidOrder = CreateOrder(
                "REPORT-PAID",
                500000,
                createdOutsidePeriod,
                deliveryInsidePeriod,
                50000,
                product.Id,
                productType.Id,
                "Hoa tươi");
            paidOrder.ChangePaymentStatus(PaymentStatus.Paid, createdOutsidePeriod.AddMinutes(1), null, "report-test");
            Deliver(paidOrder, createdOutsidePeriod.AddMinutes(2), deliveryInsidePeriod);
            var paidOutsidePeriod = CreateOrder("REPORT-OUTSIDE", 300000, deliveryInsidePeriod, deliveryOutsidePeriod);
            paidOutsidePeriod.ChangePaymentStatus(PaymentStatus.Paid, deliveryInsidePeriod.AddMinutes(1), null, "report-test");
            Deliver(paidOutsidePeriod, deliveryInsidePeriod.AddMinutes(2), deliveryOutsidePeriod);
            var cancelledOrder = CreateOrder("REPORT-CANCELLED", 200000, createdOutsidePeriod, deliveryInsidePeriod);
            cancelledOrder.ChangePaymentStatus(PaymentStatus.Paid, createdOutsidePeriod.AddMinutes(1), null, "report-test");
            cancelledOrder.ChangeStatus(OrderStatus.Cancelled, false, createdOutsidePeriod.AddMinutes(2), null, "report-test", "Khách hủy");
            var category = new ExpenseCategory("Vận chuyển", null, 10, true, createdOutsidePeriod);
            var expense = new Expense(
                category.Id,
                new DateOnly(2026, 8, 3),
                150000,
                "Phí giao hàng",
                null,
                createdOutsidePeriod);
            dbContext.Orders.AddRange(paidOrder, paidOutsidePeriod, cancelledOrder);
            dbContext.OrderPayments.Add(new OrderPayment(
                paidOrder.Id,
                110000,
                OrderPaymentKind.Refund,
                PaymentMethod.BankTransfer,
                deliveryInsidePeriod.AddHours(2),
                "recognized-revenue-refund",
                "Hoàn một phần sau giao",
                deliveryInsidePeriod.AddHours(2),
                null,
                true,
                100000,
                10000));
            dbContext.ExpenseCategories.Add(category);
            dbContext.Expenses.Add(expense);
            await dbContext.SaveChangesAsync();

            var service = new FinancialReportService(dbContext, new FixedTimeProvider(GeneratedAt));
            var report = await service.GetAsync(new FinancialReportQuery
            {
                From = new DateOnly(2026, 8, 3),
                To = new DateOnly(2026, 8, 3),
                GroupBy = "day",
                IncludeShippingFeeInRevenue = false
            }, CancellationToken.None);

            Assert.Equal(400000, report.Revenue);
            Assert.Equal(400000, report.ProductRevenue);
            Assert.Equal(40000, report.ShippingFee);
            Assert.False(report.IncludeShippingFeeInRevenue);
            Assert.Equal(150000, report.Expense);
            Assert.Equal(290000, report.Profit);
            Assert.Equal(65.91m, report.ProfitMarginPercent);
            Assert.Equal(1, report.OrderCount);
            Assert.Equal(1, report.ExpenseCount);
            var dailyOrder = Assert.Single(report.DailyOrders);
            Assert.Equal(paidOrder.Id, dailyOrder.Id);
            Assert.Equal(new DateOnly(2026, 8, 3), dailyOrder.DeliveryDate);
            Assert.Equal(500000, dailyOrder.ProductRevenue);
            Assert.Equal(50000, dailyOrder.ShippingFee);
            Assert.Equal(290000, Assert.Single(report.Points).Profit);
            var adjustment = Assert.Single(report.DailyRevenueAdjustments);
            Assert.Equal(100000, adjustment.ProductRevenueAdjustment);
            Assert.Equal(10000, adjustment.ShippingRevenueAdjustment);
            Assert.Equal("Vận chuyển", Assert.Single(report.ExpensesByCategory).ExpenseCategoryName);
            Assert.Contains("ngày hoàn tất", report.RevenueBasis);
            var revenueByProductLine = Assert.Single(report.RevenueByProductLine);
            Assert.Equal(productType.Id, revenueByProductLine.Id);
            Assert.Equal("Hoa tươi", revenueByProductLine.Name);
            Assert.Equal(400000, revenueByProductLine.Revenue);
            var revenueByCategory = Assert.Single(report.RevenueByCategory);
            Assert.Equal(productCategory.Id, revenueByCategory.Id);
            Assert.Equal("Hoa bó", revenueByCategory.Name);
            Assert.Equal(400000, revenueByCategory.Revenue);

            var reportIncludingShipping = await service.GetAsync(new FinancialReportQuery
            {
                From = new DateOnly(2026, 8, 3),
                To = new DateOnly(2026, 8, 3),
                GroupBy = "day",
                IncludeShippingFeeInRevenue = true
            }, CancellationToken.None);

            Assert.Equal(440000, reportIncludingShipping.Revenue);
            Assert.Equal(290000, reportIncludingShipping.Profit);
            Assert.Equal(400000, reportIncludingShipping.ProductRevenue);
            Assert.Equal(40000, reportIncludingShipping.ShippingFee);
            Assert.True(reportIncludingShipping.IncludeShippingFeeInRevenue);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task DepositCompletionAndRemainderAreRecognizedOnTheirOwnBusinessDates()
    {
        var databaseName = $"LamieReportTimeline_{Guid.NewGuid():N}";
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);

        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var createdAt = new DateTime(2026, 10, 9, 2, 0, 0, DateTimeKind.Utc);
            var depositAt = new DateTime(2026, 10, 9, 3, 0, 0, DateTimeKind.Utc);
            var completedAt = new DateTime(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc);
            var remainderAt = new DateTime(2026, 10, 11, 3, 0, 0, DateTimeKind.Utc);
            var order = CreateOrder("REPORT-THREE-DAYS", 1_100_000, createdAt, completedAt, 50_000);
            Deliver(order, createdAt.AddMinutes(1), completedAt);

            dbContext.Orders.Add(order);
            dbContext.OrderPayments.AddRange(
                new OrderPayment(order.Id, 600_000, OrderPaymentKind.Deposit, PaymentMethod.BankTransfer,
                    depositAt, "three-days-deposit", null, depositAt, null),
                new OrderPayment(order.Id, 550_000, OrderPaymentKind.Remainder, PaymentMethod.Cash,
                    remainderAt, "three-days-remainder", null, remainderAt, null));
            await dbContext.SaveChangesAsync();

            var clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 12, 3, 0, 0, TimeSpan.Zero));
            var service = new FinancialReportService(dbContext, clock);
            var cash = await service.GetCashFlowAsync(new FinancialReportQuery
            {
                From = new DateOnly(2026, 10, 9),
                To = new DateOnly(2026, 10, 11),
                GroupBy = "day"
            }, CancellationToken.None);
            var revenue = await service.GetAsync(new FinancialReportQuery
            {
                From = new DateOnly(2026, 10, 9),
                To = new DateOnly(2026, 10, 11),
                GroupBy = "day"
            }, CancellationToken.None);

            Assert.Equal(600_000, cash.Points.Single(point => point.Date == new DateOnly(2026, 10, 9)).CashIn);
            Assert.Equal(550_000, cash.Points.Single(point => point.Date == new DateOnly(2026, 10, 11)).CashIn);
            Assert.Equal(1_150_000, cash.CashIn);
            Assert.Equal(2, cash.Transactions.Count);
            Assert.Equal(0, revenue.Points.Single(point => point.From == new DateOnly(2026, 10, 9)).Revenue);
            Assert.Equal(1_150_000, revenue.Points.Single(point => point.From == new DateOnly(2026, 10, 10)).Revenue);
            Assert.Equal(0, revenue.Points.Single(point => point.From == new DateOnly(2026, 10, 11)).Revenue);
            Assert.Equal(1_150_000, revenue.Revenue);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private static void Deliver(Order order, DateTime startedAt, DateTime deliveredAt)
    {
        order.ChangeStatus(OrderStatus.Confirmed, false, startedAt, null, "report-test");
        order.ChangeStatus(OrderStatus.Producing, true, startedAt.AddSeconds(1), null, "report-test");
        order.UpdateChecklist(true, true, true, true, startedAt.AddSeconds(2), null, "report-test");
        order.ChangeStatus(OrderStatus.Ready, true, startedAt.AddSeconds(3), null, "report-test");
        order.ChangeStatus(OrderStatus.Shipping, true, startedAt.AddSeconds(4), null, "report-test");
        order.ChangeStatus(OrderStatus.Delivered, true, deliveredAt, null, "report-test");
    }

    private static Order CreateOrder(
        string code,
        decimal amount,
        DateTime createdAt,
        DateTime deliveryAt,
        decimal shippingFee = 0,
        int? productId = null,
        int? productTypeId = null,
        string? productTypeName = null) => new(
        code,
        Channel.AdminId,
        null,
        new OrderDetails(
            "Người đặt",
            "0900000001",
            "Người nhận",
            "0912345678",
            false,
            false,
            "123 Test Street",
            null,
            null,
            null,
            deliveryAt,
            null,
            0,
            shippingFee,
            null,
            null,
            null),
        [new OrderItemSnapshot(productId, null, "Hoa theo yêu cầu", null, amount, 1, ProductTypeId: productTypeId, ProductTypeName: productTypeName)],
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
            [new FinancialReportPointDto(from, to, "01/08-03/08", 500000, 150000, 350000, 1, 1)
            {
                ProductRevenue = 450000,
                ShippingFee = 50000
            }],
            [new FinancialReportExpenseCategoryDto(Guid.NewGuid(), categoryName, 150000, 1)],
            "Revenue basis",
            "Profit basis")
        {
            ProductRevenue = 450000,
            ShippingFee = 50000,
            IncludeShippingFeeInRevenue = true
        };
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
