using System.Text.Json;
using Lamie.API.Controllers;
using Lamie.API.Middlewares;
using Lamie.Application.Identity;
using Lamie.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lamie.Tests.Integration;

public sealed class IntegrationRegressionTests
{
    public static TheoryData<Type> AttributeControllers => new()
    {
        typeof(CategoriesController),
        typeof(CollectionsController),
        typeof(ColorsController),
        typeof(LanguagesController),
        typeof(OccasionsController),
        typeof(StylesController),
        typeof(TagsController)
    };

    [Theory]
    [MemberData(nameof(AttributeControllers))]
    public void EverySettingsAttributeControllerHasFiveProtectedCrudRoutes(Type controllerType)
    {
        var controllerPolicy = Assert.Single(
            controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), true)) as AuthorizeAttribute;
        Assert.Equal(PermissionNames.SettingsView, controllerPolicy?.Policy);

        var actions = controllerType.GetMethods()
            .Where(method => method.DeclaringType == controllerType)
            .SelectMany(method => method.GetCustomAttributes(typeof(HttpMethodAttribute), true)
                .Cast<HttpMethodAttribute>()
                .Select(attribute => (Method: method, Attribute: attribute)))
            .ToList();
        Assert.Equal(5, actions.Count);
        Assert.Equal(2, actions.Count(item => item.Attribute is HttpGetAttribute));
        Assert.Single(actions, item => item.Attribute is HttpPostAttribute);
        Assert.Single(actions, item => item.Attribute is HttpPutAttribute);
        var delete = Assert.Single(actions, item => item.Attribute is HttpDeleteAttribute);

        foreach (var mutation in actions.Where(item =>
                     item.Attribute is HttpPostAttribute or HttpPutAttribute or HttpDeleteAttribute))
        {
            var policy = Assert.Single(
                mutation.Method.GetCustomAttributes(typeof(AuthorizeAttribute), true)) as AuthorizeAttribute;
            Assert.Equal(PermissionNames.SettingsManage, policy?.Policy);
        }
        Assert.NotNull(delete.Attribute.Template);
    }

    [Fact]
    public async Task PersistenceConflictUsesSanitizedSourceEnvelope()
    {
        var response = await InvokeMiddlewareAsync(new DbUpdateException("database internals"));

        Assert.Equal(StatusCodes.Status409Conflict, response.StatusCode);
        Assert.Equal("PERSISTENCE_CONFLICT", response.Json.RootElement.GetProperty("code").GetString());
        Assert.False(response.Body.Contains("database internals", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnexpectedFailureUsesSanitizedSourceEnvelope()
    {
        var response = await InvokeMiddlewareAsync(new InvalidOperationException("secret implementation detail"));

        Assert.Equal(StatusCodes.Status500InternalServerError, response.StatusCode);
        Assert.Equal("INTERNAL_SERVER_ERROR", response.Json.RootElement.GetProperty("code").GetString());
        Assert.Equal("Internal server error", response.Json.RootElement.GetProperty("message").GetString());
        Assert.False(response.Body.Contains("secret implementation detail", StringComparison.Ordinal));
        Assert.False(response.Json.RootElement.TryGetProperty("detail", out _));
    }

    [Fact]
    public void MigrationChainIsOrderedAndIncludesAllImplementedDomains()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=LamieMigrationMetadataOnly;Trusted_Connection=True")
            .Options;
        using var dbContext = new AppDbContext(options);

        var migrations = dbContext.Database.GetMigrations().ToArray();

        Assert.Equal(17, migrations.Length);
        Assert.Equal(migrations.OrderBy(value => value, StringComparer.Ordinal), migrations);
        Assert.Contains(migrations, value => value.EndsWith("_AddIdentity", StringComparison.Ordinal));
        Assert.Contains(migrations, value => value.EndsWith("_AddChannels", StringComparison.Ordinal));
        Assert.Contains(migrations, value => value.EndsWith("_AddOrderDomain", StringComparison.Ordinal));
        Assert.Contains(migrations, value => value.EndsWith("_AddDashboardQueryIndex", StringComparison.Ordinal));
        Assert.Contains(migrations, value => value.EndsWith("_AddProductTypes", StringComparison.Ordinal));
        Assert.Contains(migrations, value => value.EndsWith("_AddOrderImageItemLink", StringComparison.Ordinal));
        Assert.Contains(migrations, value => value.EndsWith("_RemoveAllForeignKeyConstraints", StringComparison.Ordinal));
        Assert.Contains(migrations, value => value.EndsWith("_AddOrderDeliveryWindow", StringComparison.Ordinal));
        Assert.Contains(migrations, value => value.EndsWith("_AddProvinceShipping", StringComparison.Ordinal));
        Assert.Contains(migrations, value => value.EndsWith("_AddExpenseModule", StringComparison.Ordinal));
        Assert.Contains(migrations, value => value.EndsWith("_AddRolePermissionModel", StringComparison.Ordinal));
        Assert.Contains(migrations, value => value.EndsWith("_AddDynamicPermissionFoundation", StringComparison.Ordinal));
        Assert.Contains(migrations, value => value.EndsWith("_AddNavigationBackend", StringComparison.Ordinal));
        Assert.EndsWith("_SeedDefaultAdminNavigation", migrations[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void FreshDatabaseAndMigrationScriptsNeverCreateForeignKeys()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=LamieMigrationMetadataOnly;Trusted_Connection=True")
            .Options;
        using var dbContext = new AppDbContext(options);

        var createScript = dbContext.Database.GenerateCreateScript();
        var migrationScript = dbContext.GetService<IMigrator>().GenerateScript(
            options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.DoesNotContain("FOREIGN KEY", createScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FOREIGN KEY", migrationScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sys.foreign_keys", migrationScript, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(nameof(AuthController.Login))]
    [InlineData(nameof(AuthController.Refresh))]
    public void AnonymousTokenEndpointsAreRateLimited(string actionName)
    {
        var action = typeof(AuthController).GetMethod(actionName);
        var rateLimit = Assert.Single(
            action!.GetCustomAttributes(typeof(EnableRateLimitingAttribute), true)) as EnableRateLimitingAttribute;

        Assert.Equal("auth", rateLimit?.PolicyName);
        Assert.NotNull(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).SingleOrDefault());
    }

    [Fact]
    public void EveryControllerActionIsProtectedExceptLoginAndRefresh()
    {
        var controllers = typeof(AuthController).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type));

        foreach (var controller in controllers)
        {
            var controllerProtected = controller
                .GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Any();
            var actions = controller.GetMethods()
                .Where(method => method.DeclaringType == controller)
                .Where(method => method.GetCustomAttributes(typeof(HttpMethodAttribute), true).Any());

            foreach (var action in actions)
            {
                var isAnonymousTokenAction = controller == typeof(AuthController)
                    && action.Name is nameof(AuthController.Login) or nameof(AuthController.Refresh);
                var allowsAnonymous = action
                    .GetCustomAttributes(typeof(AllowAnonymousAttribute), true)
                    .Any();
                var actionProtected = action
                    .GetCustomAttributes(typeof(AuthorizeAttribute), true)
                    .Any();

                Assert.Equal(isAnonymousTokenAction, allowsAnonymous);
                Assert.True(
                    isAnonymousTokenAction || controllerProtected || actionProtected,
                    $"{controller.Name}.{action.Name} must require authorization.");
            }
        }
    }

    private static async Task<(int StatusCode, string Body, JsonDocument Json)> InvokeMiddlewareAsync(
        Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw exception,
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await middleware.Invoke(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        return (context.Response.StatusCode, body, JsonDocument.Parse(body));
    }
}
