using Lamie.API.Services;
using Lamie.API.Controllers;
using Lamie.API.Controllers.Settings;
using Lamie.Application.Common.Exceptions;
using Lamie.Application.Identity;
using Lamie.Application.Reports;
using Lamie.Domain.Entities;
using Lamie.Domain.Exceptions;
using Lamie.Domain.Ingredients;
using Lamie.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Lamie.Tests.Ingredients;

public sealed class IngredientPlanningTests
{
    private static readonly DateTime Baseline = new(2026, 8, 17, 2, 0, 0, DateTimeKind.Utc);
    private static readonly IngredientPackOption Pack50 = new(1, "BUNDLE_50", "Bó 50", "Bó", "bó", 50);
    private static readonly IngredientPackOption Pack30 = new(2, "BUNDLE_30", "Bó 30", "Bó", "bó", 30);

    [Theory]
    [InlineData(80, 0, 1, 1)]
    [InlineData(60, 0, 0, 2)]
    [InlineData(70, 10, 0, 2)]
    [InlineData(20, 20, 0, 0)]
    public void OptimizerMatchesRequiredPackExamples(
        decimal total,
        decimal remainder,
        long pack50Count,
        long pack30Count)
    {
        var result = IngredientConversionOptimizer.Optimize(total, [Pack50, Pack30]);

        Assert.Equal(total, result.TotalBaseQuantity);
        Assert.Equal(remainder, result.BaseRemainder);
        Assert.Equal(pack50Count, result.Packs.SingleOrDefault(item => item.ConversionId == 1)?.Count ?? 0);
        Assert.Equal(pack30Count, result.Packs.SingleOrDefault(item => item.ConversionId == 2)?.Count ?? 0);
    }

    [Fact]
    public void OptimizerSupportsFractionalFactorsAndPrefersLargerPacksOnExactTies()
    {
        var fractional = IngredientConversionOptimizer.Optimize(
            6.25m,
            [
                new IngredientPackOption(1, "LARGE", "Large", "Pack", null, 2.5m),
                new IngredientPackOption(2, "SMALL", "Small", "Pack", null, 1.25m)
            ]);
        var tie = IngredientConversionOptimizer.Optimize(
            6m,
            [
                new IngredientPackOption(1, "FOUR", "Four", "Pack", null, 4m),
                new IngredientPackOption(2, "THREE", "Three", "Pack", null, 3m),
                new IngredientPackOption(3, "TWO", "Two", "Pack", null, 2m)
            ]);

        Assert.Equal(0, fractional.BaseRemainder);
        Assert.Equal(2, fractional.Packs.Single(item => item.ConversionId == 1).Count);
        Assert.Equal(1, fractional.Packs.Single(item => item.ConversionId == 2).Count);
        Assert.Equal([1, 3], tie.Packs.Select(item => item.ConversionId).ToArray());
        Assert.All(tie.Packs, item => Assert.Equal(1, item.Count));
    }

    [Fact]
    public void OptimizerRejectsPathologicalPrecisionInsteadOfRunningUnbounded()
    {
        Assert.Throws<DomainException>(() => IngredientConversionOptimizer.Optimize(
            1_000_000m,
            [
                new IngredientPackOption(1, "A", "A", "Pack", null, 1.000001m),
                new IngredientPackOption(2, "B", "B", "Pack", null, 1.000003m)
            ]));
    }

    [Fact]
    public void ProductRecipeIsOptionalButRejectsDuplicateIngredients()
    {
        var product = new Product("AB12", 100, 0, 1, 1, false);

        Assert.Empty(product.Ingredients);
        product.ReplaceIngredients([new ProductIngredientDefinition(1, 2.5m, "Red roses", 0)]);
        Assert.Equal(2.5m, product.Ingredients.Single().BaseQuantity);
        Assert.Throws<DomainException>(() => product.ReplaceIngredients(
        [
            new ProductIngredientDefinition(1, 1, null, 0),
            new ProductIngredientDefinition(1, 2, null, 1)
        ]));
    }

    [Fact]
    public void OrderRecipeSnapshotIsPreservedAcrossLineAndQuantityChangesUntilExplicitlyReplaced()
    {
        IngredientRecipeSnapshot[] originalRecipe =
        [
            new(1, "ROSE", "Rose", "STEM", "Stem", "stem", 20, null, 0)
        ];
        IngredientRecipeSnapshot[] changedRecipe =
        [
            new(1, "ROSE", "Rose", "STEM", "Stem", "stem", 99, null, 0)
        ];
        var order = CreateOrder(new OrderItemSnapshot(
            1,
            "AB12",
            "Bouquet",
            null,
            100,
            2,
            IngredientRecipe: originalRecipe,
            IngredientSnapshotCapturedAtUtc: Baseline));
        var item = order.Items.Single();

        order.Update(
            Channel.AdminId,
            null,
            CreateDetails(),
            [new OrderItemUpdate(item.Id, new OrderItemSnapshot(
                1,
                "AB12",
                "Renamed bouquet",
                null,
                120,
                2,
                IngredientRecipe: null,
                IngredientSnapshotCapturedAtUtc: null))],
            Baseline.AddHours(1),
            null,
            "tester");

        Assert.Equal(Baseline, item.IngredientSnapshotCapturedAtUtc);
        Assert.Equal(20, item.IngredientSnapshots.Single().PerProductBaseQuantity);
        Assert.Equal(40, item.IngredientSnapshots.Single().TotalBaseQuantity);

        order.Update(
            Channel.AdminId,
            null,
            CreateDetails(),
            [new OrderItemUpdate(item.Id, new OrderItemSnapshot(
                1,
                "AB12",
                "Renamed bouquet",
                null,
                120,
                3,
                IngredientRecipe: null,
                IngredientSnapshotCapturedAtUtc: null))],
            Baseline.AddHours(2),
            null,
            "tester");

        Assert.Equal(Baseline, item.IngredientSnapshotCapturedAtUtc);
        Assert.Equal(20, item.IngredientSnapshots.Single().PerProductBaseQuantity);
        Assert.Equal(3, item.IngredientSnapshots.Single().ProductQuantity);
        Assert.Equal(60, item.IngredientSnapshots.Single().TotalBaseQuantity);

        order.Update(
            Channel.AdminId,
            null,
            CreateDetails(),
            [new OrderItemUpdate(item.Id, new OrderItemSnapshot(
                1,
                "AB12",
                "Renamed bouquet",
                null,
                120,
                3,
                IngredientRecipe: changedRecipe,
                IngredientSnapshotCapturedAtUtc: Baseline.AddHours(3)))],
            Baseline.AddHours(3),
            null,
            "tester");

        Assert.Equal(Baseline.AddHours(3), item.IngredientSnapshotCapturedAtUtc);
        Assert.Equal(99, item.IngredientSnapshots.Single().PerProductBaseQuantity);
        Assert.Equal(297, item.IngredientSnapshots.Single().TotalBaseQuantity);
    }

    [Fact]
    public void ReportDefaultsToCreatedAndProducingAndUsesInclusiveVietnamBusinessDays()
    {
        var period = IngredientDemandReportService.ResolvePeriod(
            new IngredientDemandQuery
            {
                From = new DateOnly(2026, 8, 1),
                To = new DateOnly(2026, 8, 2)
            },
            new DateOnly(2026, 8, 17));
        var (start, endExclusive) = IngredientDemandReportService.BusinessRangeUtc(period.From, period.To);

        Assert.Equal([OrderStatus.Created, OrderStatus.Producing],
            IngredientDemandReportService.ResolveStatuses(null));
        Assert.Equal([OrderStatus.Created, OrderStatus.Producing],
            IngredientDemandReportService.ResolveStatuses("1,Producing"));
        Assert.Throws<ValidationException>(() =>
            IngredientDemandReportService.ResolveStatuses("99"));
        Assert.Equal(new DateTime(2026, 7, 31, 17, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 8, 2, 17, 0, 0, DateTimeKind.Utc), endExclusive);
    }

    [Fact]
    public void EfModelUsesSixDecimalQuantitiesAndNullableLegacyMarker()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=LamieIngredientModelOnly;Trusted_Connection=True")
            .Options;
        using var dbContext = new AppDbContext(options);
        var model = dbContext.GetService<IDesignTimeModel>().Model;

        var recipe = model.FindEntityType(typeof(ProductIngredient))!;
        var snapshot = model.FindEntityType(typeof(OrderItemIngredientSnapshot))!;
        var item = model.FindEntityType(typeof(OrderItem))!;
        Assert.Equal(18, recipe.FindProperty(nameof(ProductIngredient.BaseQuantity))!.GetPrecision());
        Assert.Equal(6, recipe.FindProperty(nameof(ProductIngredient.BaseQuantity))!.GetScale());
        Assert.Equal(18, snapshot.FindProperty(nameof(OrderItemIngredientSnapshot.TotalBaseQuantity))!.GetPrecision());
        Assert.Equal(6, snapshot.FindProperty(nameof(OrderItemIngredientSnapshot.TotalBaseQuantity))!.GetScale());
        Assert.True(item.FindProperty(nameof(OrderItem.IngredientSnapshotCapturedAtUtc))!.IsNullable);
        Assert.Contains(recipe.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(ProductIngredient.ProductId), nameof(ProductIngredient.IngredientId)]));
    }

    [Fact]
    public void IngredientControllersUseSeparateViewManageAndReportPermissions()
    {
        Assert.Equal(
            PermissionNames.IngredientsView,
            Assert.Single(typeof(MeasurementUnitsController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Policy);
        Assert.Equal(
            PermissionNames.IngredientsView,
            Assert.Single(typeof(IngredientsController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Policy);
        Assert.Equal(
            PermissionNames.IngredientReportsView,
            Assert.Single(typeof(IngredientDemandReportsController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Policy);

        Assert.All(
            new[]
            {
                nameof(MeasurementUnitsController.Create),
                nameof(MeasurementUnitsController.Update),
                nameof(MeasurementUnitsController.Delete)
            },
            method => Assert.Equal(
                PermissionNames.IngredientsManage,
                Assert.Single(typeof(MeasurementUnitsController).GetMethod(method)!
                    .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Policy));
        Assert.All(
            new[]
            {
                nameof(IngredientsController.Create),
                nameof(IngredientsController.Update),
                nameof(IngredientsController.Delete)
            },
            method => Assert.Equal(
                PermissionNames.IngredientsManage,
                Assert.Single(typeof(IngredientsController).GetMethod(method)!
                    .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Policy));
    }

    [Fact]
    public async Task DemandReportAggregatesBoundaryOrdersAndExcludesCancelledByDefault()
    {
        var databaseName = $"LamieIngredientDemandTests_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var dbContext = new AppDbContext(options);

        try
        {
            await dbContext.Database.MigrateAsync();
            var stem = new MeasurementUnit("STEM", "Stem", "stem", false, true, Baseline);
            var bundle = new MeasurementUnit("BUNDLE", "Bundle", "bundle", false, true, Baseline);
            dbContext.MeasurementUnits.AddRange(stem, bundle);
            await dbContext.SaveChangesAsync();

            var rose = new Ingredient("ROSE", "Rose", stem.Id, null, true, Baseline);
            rose.ReplaceConversions(
            [
                new IngredientConversionDefinition(null, "BUNDLE_50", "Bundle 50", bundle.Id, 50, 0, true),
                new IngredientConversionDefinition(null, "BUNDLE_30", "Bundle 30", bundle.Id, 30, 1, true)
            ], Baseline);
            dbContext.Ingredients.Add(rose);
            await dbContext.SaveChangesAsync();

            var category = new Category(10);
            var productType = new ProductType("DEMAND_TEST", 10);
            dbContext.Categories.Add(category);
            dbContext.ProductTypes.Add(productType);
            await dbContext.SaveChangesAsync();
            var currentRecipeProduct = new Product("CURRENT-RECIPE", 100, 0, category.Id, productType.Id, false);
            currentRecipeProduct.ReplaceIngredients([new ProductIngredientDefinition(rose.Id, 999, null, 0)]);
            dbContext.Products.Add(currentRecipeProduct);
            await dbContext.SaveChangesAsync();

            IngredientRecipeSnapshot Recipe(decimal quantity) => new(
                rose.Id,
                rose.Code,
                rose.Name,
                stem.Code,
                stem.Name,
                stem.Symbol,
                quantity,
                null,
                0);
            var first = CreateOrder(
                "ORD-DEMAND-START",
                new DateTime(2026, 7, 31, 17, 0, 0, DateTimeKind.Utc),
                new OrderItemSnapshot(
                    null, null, "Start bouquet", null, 100, 2,
                    IngredientRecipe: [Recipe(20)],
                    IngredientSnapshotCapturedAtUtc: Baseline));
            var second = CreateOrder(
                "ORD-DEMAND-END",
                new DateTime(2026, 8, 2, 16, 59, 59, DateTimeKind.Utc),
                new OrderItemSnapshot(
                    null, null, "End bouquet", null, 100, 3,
                    IngredientRecipe: [Recipe(10)],
                    IngredientSnapshotCapturedAtUtc: Baseline));
            second.ChangeStatus(OrderStatus.Producing, false, Baseline.AddMinutes(1), null, "tester");
            var legacyWithoutSnapshot = CreateOrder(
                "ORD-DEMAND-LEGACY",
                new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc),
                new OrderItemSnapshot(
                    currentRecipeProduct.Id, currentRecipeProduct.Sku, "Legacy bouquet", null, 100, 4));
            var cancelled = CreateOrder(
                "ORD-DEMAND-CANCELLED",
                new DateTime(2026, 8, 1, 5, 0, 0, DateTimeKind.Utc),
                new OrderItemSnapshot(
                    null, null, "Cancelled bouquet", null, 100, 100,
                    IngredientRecipe: [Recipe(20)],
                    IngredientSnapshotCapturedAtUtc: Baseline));
            cancelled.ChangeStatus(OrderStatus.Cancelled, false, Baseline.AddMinutes(1), null, "tester");
            dbContext.Orders.AddRange(first, legacyWithoutSnapshot, second, cancelled);
            await dbContext.SaveChangesAsync();

            var service = new IngredientDemandReportService(
                dbContext,
                new FixedTimeProvider(new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero)));
            var report = await service.GetAsync(new IngredientDemandQuery
            {
                From = new DateOnly(2026, 8, 1),
                To = new DateOnly(2026, 8, 2),
                Page = 1,
                PageSize = 20
            }, CancellationToken.None);

            Assert.Equal(3, report.Details.TotalCount);
            Assert.Equal(["ORD-DEMAND-START", "ORD-DEMAND-LEGACY", "ORD-DEMAND-END"],
                report.Details.Items.Select(item => item.OrderCode).ToArray());
            Assert.False(report.HasLegacyRecipeFallback);
            var legacyItem = Assert.Single(report.Details.Items.Single(item => item.OrderCode == "ORD-DEMAND-LEGACY").Items);
            Assert.Equal(IngredientDemandSource.LegacyWithoutSnapshot, legacyItem.Source);
            Assert.Empty(legacyItem.Ingredients);
            var demand = Assert.Single(report.Summary);
            Assert.Equal(70, demand.TotalBaseQuantity);
            Assert.Equal(10, demand.Breakdown.BaseRemainder);
            Assert.Equal(2, Assert.Single(demand.Breakdown.Packs).Count);
            Assert.Equal("BUNDLE_30", demand.Breakdown.Packs.Single().Code);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
        }
    }

    private static Order CreateOrder(OrderItemSnapshot snapshot) => new(
        "ORD-INGREDIENT-01",
        Channel.AdminId,
        null,
        CreateDetails(),
        [snapshot],
        Baseline,
        null,
        "tester");

    private static Order CreateOrder(string code, DateTime deliveryAt, OrderItemSnapshot snapshot) => new(
        code,
        Channel.AdminId,
        null,
        CreateDetails(deliveryAt),
        [snapshot],
        Baseline,
        null,
        "tester");

    private static OrderDetails CreateDetails(DateTime? deliveryAt = null) => new(
        "Orderer",
        "0900000000",
        "Recipient",
        "0911111111",
        false,
        false,
        "123 Flower Street",
        null,
        10.7769m,
        106.7009m,
        deliveryAt ?? Baseline.AddDays(1),
        null,
        0,
        10,
        null,
        null,
        null);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
