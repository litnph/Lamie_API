namespace Lamie.Domain.Orders;

public static class DefaultDepositCalculator
{
    public const decimal FirstThreshold = 400_000m;
    public const decimal SecondThreshold = 700_000m;
    public const decimal ThirdThreshold = 1_000_000m;
    public const decimal RoundingIncrement = 100_000m;

    public static decimal Calculate(decimal orderValue)
    {
        if (orderValue < 0)
            throw new ArgumentOutOfRangeException(nameof(orderValue));
        if (orderValue <= FirstThreshold) return 100_000m;
        if (orderValue <= SecondThreshold) return 200_000m;
        if (orderValue <= ThirdThreshold) return 300_000m;

        return decimal.Round(
            orderValue / 3m / RoundingIncrement,
            0,
            MidpointRounding.AwayFromZero) * RoundingIncrement;
    }

    public static decimal Resolve(decimal orderValue, decimal? explicitDeposit) =>
        explicitDeposit ?? Calculate(orderValue);
}
