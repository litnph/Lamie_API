using Lamie.Domain.Entities;
using Lamie.Domain.Exceptions;
using Lamie.Domain.Orders;

namespace Lamie.Tests.Orders;

public sealed class AdminOrderFeatureTests
{
    [Theory]
    [InlineData(400000, 100000)]
    [InlineData(400001, 200000)]
    [InlineData(700000, 200000)]
    [InlineData(700001, 300000)]
    [InlineData(1000000, 300000)]
    [InlineData(1200000, 400000)]
    [InlineData(1500000, 500000)]
    [InlineData(1700000, 600000)]
    public void Default_deposit_matches_business_boundaries(decimal total, decimal expected) =>
        Assert.Equal(expected, DefaultDepositCalculator.Calculate(total));

    [Fact]
    public void Explicit_deposit_is_never_overwritten() =>
        Assert.Equal(123456m, DefaultDepositCalculator.Resolve(1_700_000m, 123456m));

    [Fact]
    public void Card_and_banner_are_trimmed_and_retained()
    {
        var item = CreateItem(new OrderItemSnapshot(null, null, "Custom bouquet", null, 500_000m, 1,
            Note: null, HasCard: true, CardMessage: "  Happy birthday  ", HasBanner: true, BannerMessage: "  Congratulations  "));
        Assert.Equal("Happy birthday", item.CardMessage);
        Assert.Equal("Congratulations", item.BannerMessage);
    }

    [Fact]
    public void Enabled_card_or_banner_requires_content()
    {
        Assert.Throws<DomainException>(() => CreateItem(new OrderItemSnapshot(null, null, "Bouquet", null, 1, 1, HasCard: true, CardMessage: "  ")));
        Assert.Throws<DomainException>(() => CreateItem(new OrderItemSnapshot(null, null, "Bouquet", null, 1, 1, HasBanner: true, BannerMessage: "")));
    }

    [Fact]
    public void Disabled_features_discard_stale_messages()
    {
        var item = CreateItem(new OrderItemSnapshot(null, null, "Bouquet", null, 1, 1, CardMessage: "stale", BannerMessage: "stale"));
        Assert.Null(item.CardMessage);
        Assert.Null(item.BannerMessage);
    }

    private static OrderItem CreateItem(OrderItemSnapshot snapshot)
    {
        var order = new Order("TEST-1", Channel.AdminId, null,
            new OrderDetails("Admin", "", "Recipient", "0900000000", true, false, null, null, null, null,
                DateTime.UtcNow.AddDays(1), null, 0, 0, null, null, null),
            [snapshot], DateTime.UtcNow, null, null);
        return Assert.Single(order.Items);
    }
}
