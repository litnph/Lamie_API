using Lamie.API.Controllers.Settings.Attributes;
using Lamie.Application.Channels;
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

namespace Lamie.Tests.Channels;

public sealed class ChannelTests
{
    private static readonly DateTime Baseline = new(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ChannelNormalizesCodeAndUpdatesMutableFields()
    {
        var channel = new Channel("  Social-Shop  ", "Social shop", null, 5, true, Baseline);

        Assert.Equal("social-shop", channel.Code);
        channel.Update("Social marketplace", "https://example.test/icon.svg", 7, false, Baseline.AddMinutes(1));

        Assert.Equal("social-shop", channel.Code);
        Assert.Equal("Social marketplace", channel.Name);
        Assert.False(channel.IsActive);
        Assert.Equal(7, channel.SortOrder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not valid")]
    [InlineData("double--hyphen")]
    public void ChannelRejectsInvalidCodes(string code)
    {
        Assert.Throws<DomainException>(() => new Channel(code, "Channel", null, 0, true, Baseline));
    }

    [Fact]
    public void DefaultChannelIdentifiersAreStableAndDistinct()
    {
        var ids = new[] { Channel.AdminId, Channel.WebsiteId, Channel.PhoneId, Channel.WalkInId, Channel.SocialId };
        Assert.Equal(5, ids.Distinct().Count());
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), Channel.AdminId);
    }

    [Fact]
    public void ControllerExposesFrontendRoutesAndPolicies()
    {
        var controllerPolicy = Assert.Single(
            typeof(ChannelsController).GetCustomAttributes(typeof(AuthorizeAttribute), true)) as AuthorizeAttribute;
        Assert.Equal(PermissionNames.ChannelsView, controllerPolicy?.Policy);

        AssertRoute(nameof(ChannelsController.List), typeof(HttpGetAttribute), null);
        AssertRoute(nameof(ChannelsController.Get), typeof(HttpGetAttribute), "{id:guid}");
        AssertRoute(nameof(ChannelsController.Create), typeof(HttpPostAttribute), null);
        AssertRoute(nameof(ChannelsController.Update), typeof(HttpPutAttribute), null);
        AssertRoute(nameof(ChannelsController.Delete), typeof(HttpDeleteAttribute), "{id:guid}");

        var createPolicy = Assert.Single(
            typeof(ChannelsController).GetMethod(nameof(ChannelsController.Create))!
                .GetCustomAttributes(typeof(AuthorizeAttribute), true)) as AuthorizeAttribute;
        Assert.Equal(PermissionNames.ChannelsManage, createPolicy?.Policy);

        var deletePolicy = Assert.Single(
            typeof(ChannelsController).GetMethod(nameof(ChannelsController.Delete))!
                .GetCustomAttributes(typeof(AuthorizeAttribute), true)) as AuthorizeAttribute;
        Assert.Equal(PermissionNames.ChannelsManage, deletePolicy?.Policy);
    }

    [Fact]
    public void EfModelHasUniqueCodeSortIndexAndFiveDefaultChannels()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=LamieModelOnly;Trusted_Connection=True")
            .Options;
        using var dbContext = new AppDbContext(options);
        var designTimeModel = dbContext.GetService<IDesignTimeModel>().Model;
        var entityType = designTimeModel.FindEntityType(typeof(Channel));

        Assert.NotNull(entityType);
        Assert.Contains(entityType!.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([nameof(Channel.Code)]));
        Assert.Contains(entityType.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(Channel.SortOrder), nameof(Channel.Name)]));
        Assert.Equal(5, entityType.GetSeedData().Count());
    }

    private static void AssertRoute(string methodName, Type attributeType, string? template)
    {
        var method = typeof(ChannelsController).GetMethod(methodName);
        Assert.NotNull(method);
        var route = Assert.Single(method!.GetCustomAttributes(attributeType, true)) as HttpMethodAttribute;
        Assert.Equal(template, route?.Template);
    }
}
