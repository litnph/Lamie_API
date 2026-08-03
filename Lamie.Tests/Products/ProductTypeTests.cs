using Lamie.API.Controllers;
using Lamie.Application.Settings.Products.Commands;
using Lamie.Domain.Entities;
using Lamie.Domain.Exceptions;
using Lamie.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Lamie.Tests.Products;

public sealed class ProductTypeTests
{
    [Fact]
    public void ProductTypeNormalizesCodeAndStoresTranslations()
    {
        var productType = new ProductType(" fresh_flower ", 10);
        productType.AddOrUpdateTranslation("vi", "Hoa tươi", "Hoa tươi tự nhiên");
        productType.AddOrUpdateTranslation("VI", "Hoa tươi", "Cập nhật");

        Assert.Equal("FRESH_FLOWER", productType.Code);
        Assert.Equal(10, productType.SortOrder);
        Assert.Single(productType.Translations);
        Assert.Equal("Cập nhật", productType.Translations.Single().Description);
        Assert.Throws<DomainException>(() => new ProductType("fresh flower", 10));
    }

    [Fact]
    public async Task ProductAndValidatorsRequireProductType()
    {
        Assert.Throws<DomainException>(() => new Product("ROSE-01", 100, 1, 1, 0));

        var command = new CreateProductCommand
        {
            Sku = "ROSE-01",
            Price = 100,
            Stock = 1,
            CategoryId = 1,
            ProductTypeId = 0,
            Translations =
            [
                new() { LanguageCode = "vi", Name = "Hoa hồng", Slug = "hoa-hong" }
            ]
        };

        var result = await new CreateProductValidator().ValidateAsync(command);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(command.ProductTypeId));
    }

    [Fact]
    public void ProductTypeApiAndEfModelExposeExpectedContract()
    {
        var route = Assert.Single(typeof(ProductTypesController)
            .GetCustomAttributes(typeof(RouteAttribute), false)
            .Cast<RouteAttribute>());
        Assert.Equal("api/settings/attributes/product-types", route.Template);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=LamieProductTypeModelOnly;Trusted_Connection=True")
            .Options;
        using var dbContext = new AppDbContext(options);
        var model = dbContext.GetService<IDesignTimeModel>().Model;
        var productEntity = model.FindEntityType(typeof(Product));
        var productTypeEntity = model.FindEntityType(typeof(ProductType));

        Assert.NotNull(productTypeEntity);
        Assert.True(productEntity!.FindProperty(nameof(Product.ProductTypeId))!.IsNullable);
        Assert.Contains(productTypeEntity!.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(ProductType.Code)]));
        Assert.Equal(DeleteBehavior.Restrict, productEntity.GetForeignKeys().Single(foreignKey =>
            foreignKey.Properties.Any(property => property.Name == nameof(Product.ProductTypeId))).DeleteBehavior);
    }
}
