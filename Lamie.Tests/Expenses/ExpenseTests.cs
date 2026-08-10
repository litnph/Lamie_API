using System.Text.Json;
using Lamie.API.Controllers;
using Lamie.API.Services;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Expenses;
using Lamie.Application.Identity;
using Lamie.Domain.Entities;
using Lamie.Domain.Exceptions;
using Lamie.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Lamie.Tests.Expenses;

public sealed class ExpenseTests
{
    private static readonly DateTime Baseline = new(2026, 8, 3, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ExpenseCategoryNormalizesNameAndUpdatesMutableFields()
    {
        var category = new ExpenseCategory("  Vận chuyển  ", "Phí giao hàng", 10, true, Baseline);

        Assert.Equal("Vận chuyển", category.Name);
        Assert.Equal("VẬN CHUYỂN", category.NormalizedName);
        category.Update("Giao nhận", null, 20, false, Baseline.AddMinutes(1));

        Assert.Equal("Giao nhận", category.Name);
        Assert.False(category.IsActive);
        Assert.Equal(20, category.SortOrder);
        Assert.Equal(Baseline.AddMinutes(1), category.UpdatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ExpenseRejectsNonPositiveAmount(decimal amount)
    {
        Assert.Throws<DomainException>(() => new Expense(
            Guid.NewGuid(),
            new DateOnly(2026, 8, 3),
            amount,
            "Mua vật tư",
            null,
            Baseline));
    }

    [Fact]
    public void ExpenseRejectsMoreThanTwoDecimalPlaces()
    {
        Assert.Throws<DomainException>(() => new Expense(
            Guid.NewGuid(),
            new DateOnly(2026, 8, 3),
            12.345m,
            "Mua vật tư",
            null,
            Baseline));
    }

    [Fact]
    public void ControllersExposeProtectedCrudPaginationAndSummaryRoutes()
    {
        AssertControllerPolicy<ExpenseCategoriesController>(PermissionNames.ExpensesView);
        AssertRoute<ExpenseCategoriesController>(nameof(ExpenseCategoriesController.List), typeof(HttpGetAttribute), null);
        AssertRoute<ExpenseCategoriesController>(nameof(ExpenseCategoriesController.Get), typeof(HttpGetAttribute), "{id:guid}");
        AssertRoute<ExpenseCategoriesController>(nameof(ExpenseCategoriesController.Create), typeof(HttpPostAttribute), null, PermissionNames.ExpensesManage);
        AssertRoute<ExpenseCategoriesController>(nameof(ExpenseCategoriesController.Update), typeof(HttpPutAttribute), "{id:guid}", PermissionNames.ExpensesManage);
        AssertRoute<ExpenseCategoriesController>(nameof(ExpenseCategoriesController.Delete), typeof(HttpDeleteAttribute), "{id:guid}", PermissionNames.ExpensesManage);

        AssertControllerPolicy<ExpensesController>(PermissionNames.ExpensesView);
        AssertRoute<ExpensesController>(nameof(ExpensesController.List), typeof(HttpGetAttribute), null);
        AssertRoute<ExpensesController>(nameof(ExpensesController.Summary), typeof(HttpGetAttribute), "summary");
        AssertRoute<ExpensesController>(nameof(ExpensesController.Get), typeof(HttpGetAttribute), "{id:guid}");
        AssertRoute<ExpensesController>(nameof(ExpensesController.Create), typeof(HttpPostAttribute), null, PermissionNames.ExpensesManage);
        AssertRoute<ExpensesController>(nameof(ExpensesController.Update), typeof(HttpPutAttribute), "{id:guid}", PermissionNames.ExpensesManage);
        AssertRoute<ExpensesController>(nameof(ExpensesController.Delete), typeof(HttpDeleteAttribute), "{id:guid}", PermissionNames.ExpensesManage);
    }

    [Fact]
    public void EfModelUsesFinanceTablesIndexesPrecisionAndNoForeignKey()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=LamieExpenseModelOnly;Trusted_Connection=True")
            .Options;
        using var dbContext = new AppDbContext(options);
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var categoryType = model.FindEntityType(typeof(ExpenseCategory));
        var expenseType = model.FindEntityType(typeof(Expense));

        Assert.NotNull(categoryType);
        Assert.NotNull(expenseType);
        Assert.Equal("fin_expense_categories", categoryType!.GetTableName());
        Assert.Equal("fin_expenses", expenseType!.GetTableName());
        Assert.Contains(categoryType.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(ExpenseCategory.NormalizedName)]));
        Assert.Contains(expenseType.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Expense.ExpenseDate), nameof(Expense.ExpenseCategoryId)]));
        var amount = expenseType.FindProperty(nameof(Expense.Amount));
        Assert.Equal(18, amount?.GetPrecision());
        Assert.Equal(2, amount?.GetScale());
        Assert.Empty(expenseType.GetForeignKeys());
    }

    [Fact]
    public void ExpenseListRejectsInvalidPaginationAndDateRange()
    {
        var exception = Assert.Throws<ValidationException>(() => ExpenseService.ValidateListQuery(new ExpenseListQuery
        {
            Page = 0,
            PageSize = 101,
            From = new DateOnly(2026, 8, 4),
            To = new DateOnly(2026, 8, 3)
        }));

        Assert.Contains(nameof(ExpenseListQuery.Page), exception.Errors);
        Assert.Contains(nameof(ExpenseListQuery.PageSize), exception.Errors);
        Assert.Contains(nameof(ExpenseListQuery.From), exception.Errors);
    }

    [Fact]
    public void SummaryDefaultsToCurrentBusinessMonthAndRequiresCompleteCustomRange()
    {
        var today = new DateOnly(2026, 8, 3);

        var range = ExpenseService.ResolveSummaryRange(new ExpenseSummaryQuery(), today);

        Assert.Equal(new DateOnly(2026, 8, 1), range.From);
        Assert.Equal(today, range.To);
        Assert.Throws<ValidationException>(() => ExpenseService.ResolveSummaryRange(
            new ExpenseSummaryQuery { From = new DateOnly(2026, 7, 1) },
            today));
    }

    [Fact]
    public void PagedAndSummaryContractsUseAdminJsonShape()
    {
        var item = new ExpenseDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Vận chuyển",
            new DateOnly(2026, 8, 3),
            125000,
            "Phí giao hoa",
            null,
            new DateTimeOffset(Baseline),
            new DateTimeOffset(Baseline));
        var page = new PagedExpensesDto([item], 1, 1, 20, 1, false, false);
        var summary = new ExpenseSummaryDto(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 3),
            125000,
            1,
            125000,
            [new ExpenseCategorySummaryDto(item.ExpenseCategoryId, item.ExpenseCategoryName, 125000, 1)]);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var pageJson = JsonSerializer.Serialize(page, options);
        var summaryJson = JsonSerializer.Serialize(summary, options);

        Assert.Contains("\"expenseDate\":\"2026-08-03\"", pageJson);
        Assert.Contains("\"totalCount\":1", pageJson);
        Assert.Contains("\"averageAmount\":125000", summaryJson);
        Assert.Contains("\"byCategory\"", summaryJson);
    }

    [Fact]
    public async Task ServicesCoverCategoryAndExpenseCrudPaginationValidationAndSummary()
    {
        var databaseName = $"LamieExpense_{Guid.NewGuid():N}";
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);

        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            var clock = new FixedTimeProvider(new DateTimeOffset(Baseline));
            var categoryService = new ExpenseCategoryService(dbContext, clock);
            var expenseService = new ExpenseService(dbContext, clock);
            var categoryId = await categoryService.CreateAsync(
                new CreateExpenseCategoryRequest("Vận chuyển", "Chi phí giao nhận", 10, true),
                CancellationToken.None);
            var firstId = await expenseService.CreateAsync(
                new CreateExpenseRequest(
                    categoryId,
                    new DateOnly(2026, 8, 2),
                    100000,
                    "Phí giao hoa buổi sáng",
                    null),
                CancellationToken.None);
            var secondId = await expenseService.CreateAsync(
                new CreateExpenseRequest(
                    categoryId,
                    new DateOnly(2026, 8, 3),
                    50000,
                    "Phí giao hoa buổi chiều",
                    "Đã có hóa đơn"),
                CancellationToken.None);

            var page = await expenseService.ListAsync(
                new ExpenseListQuery { Search = "buổi", Page = 1, PageSize = 1 },
                CancellationToken.None);
            var summary = await expenseService.GetSummaryAsync(
                new ExpenseSummaryQuery
                {
                    From = new DateOnly(2026, 8, 1),
                    To = new DateOnly(2026, 8, 3)
                },
                CancellationToken.None);
            var categories = await categoryService.ListAsync(true, CancellationToken.None);

            Assert.Single(page.Items);
            Assert.Equal(2, page.TotalCount);
            Assert.Equal(2, page.TotalPages);
            Assert.True(page.HasNext);
            Assert.Equal(150000, summary.TotalAmount);
            Assert.Equal(2, summary.ExpenseCount);
            Assert.Equal(75000, summary.AverageAmount);
            Assert.Equal(2, Assert.Single(categories).ExpenseCount);
            await Assert.ThrowsAsync<ConflictException>(() =>
                categoryService.DeleteAsync(categoryId, CancellationToken.None));

            await expenseService.UpdateAsync(
                firstId,
                new UpdateExpenseRequest(
                    categoryId,
                    new DateOnly(2026, 8, 2),
                    120000,
                    "Phí giao hoa đã điều chỉnh",
                    null),
                CancellationToken.None);
            Assert.Equal(120000, (await expenseService.GetAsync(firstId, CancellationToken.None)).Amount);

            await expenseService.DeleteAsync(firstId, CancellationToken.None);
            await expenseService.DeleteAsync(secondId, CancellationToken.None);
            await categoryService.DeleteAsync(categoryId, CancellationToken.None);
            Assert.Empty(await categoryService.ListAsync(true, CancellationToken.None));
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
        }
    }

    private static void AssertControllerPolicy<TController>(string expectedPolicy)
    {
        var authorize = Assert.Single(
            typeof(TController).GetCustomAttributes(typeof(AuthorizeAttribute), true)) as AuthorizeAttribute;
        Assert.Equal(expectedPolicy, authorize?.Policy);
    }

    private static void AssertRoute<TController>(
        string methodName,
        Type attributeType,
        string? template,
        string? policy = null)
    {
        var method = typeof(TController).GetMethod(methodName);
        Assert.NotNull(method);
        var route = Assert.Single(method!.GetCustomAttributes(attributeType, true)) as HttpMethodAttribute;
        Assert.Equal(template, route?.Template);
        if (policy is not null)
        {
            var authorize = Assert.Single(
                method.GetCustomAttributes(typeof(AuthorizeAttribute), true)) as AuthorizeAttribute;
            Assert.Equal(policy, authorize?.Policy);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
