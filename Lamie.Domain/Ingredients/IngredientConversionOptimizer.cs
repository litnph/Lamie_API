using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Ingredients;

public sealed record IngredientPackOption(
    int ConversionId,
    string Code,
    string Name,
    string UnitName,
    string? UnitSymbol,
    decimal FactorToBase);

public sealed record IngredientPackCount(
    int ConversionId,
    string Code,
    string Name,
    string UnitName,
    string? UnitSymbol,
    decimal FactorToBase,
    long Count);

public sealed record IngredientConversionBreakdown(
    decimal TotalBaseQuantity,
    IReadOnlyList<IngredientPackCount> Packs,
    decimal BaseRemainder);

public static class IngredientConversionOptimizer
{
    private const long DecimalScale = 1_000_000;
    private const long MaximumDynamicProgrammingCells = 2_000_000;

    public static IngredientConversionBreakdown Optimize(
        decimal totalBaseQuantity,
        IEnumerable<IngredientPackOption> activeOptions)
    {
        if (totalBaseQuantity < 0)
            throw new DomainException("Total base quantity cannot be negative.");

        var total = Normalize(totalBaseQuantity);
        var options = activeOptions
            .Where(option => option.FactorToBase > 1)
            .Select(option => option with { FactorToBase = Normalize(option.FactorToBase) })
            .Where(option => option.FactorToBase > 1)
            .GroupBy(option => option.ConversionId)
            .Select(group => group.First())
            .OrderByDescending(option => option.FactorToBase)
            .ThenBy(option => option.ConversionId)
            .ToArray();
        if (total == 0 || options.Length == 0)
            return new IngredientConversionBreakdown(total, [], total);

        var totalScaled = Scale(total);
        var factorScaled = options.Select(option => Scale(option.FactorToBase)).ToArray();
        var divisor = factorScaled.Aggregate(GreatestCommonDivisor);
        var normalizedTotal = totalScaled / divisor;
        var normalizedFactors = factorScaled.Select(value => value / divisor).ToArray();
        if (normalizedTotal > int.MaxValue
            || normalizedTotal + 1 > MaximumDynamicProgrammingCells / options.Length)
        {
            throw new DomainException(
                "The quantity and conversion precision are too large to optimize exactly. Reduce the report range or use conversion factors with a common practical precision.");
        }

        var limit = (int)normalizedTotal;
        var minimumPacks = new int[limit + 1];
        Array.Fill(minimumPacks, int.MaxValue);
        minimumPacks[0] = 0;
        var counts = new int[checked((limit + 1) * options.Length)];

        for (var amount = 1; amount <= limit; amount++)
        {
            for (var optionIndex = 0; optionIndex < options.Length; optionIndex++)
            {
                var factor = normalizedFactors[optionIndex];
                if (factor > amount)
                    continue;
                var previousAmount = amount - (int)factor;
                if (minimumPacks[previousAmount] == int.MaxValue)
                    continue;

                var candidatePacks = checked(minimumPacks[previousAmount] + 1);
                if (candidatePacks > minimumPacks[amount])
                    continue;
                if (candidatePacks == minimumPacks[amount]
                    && !IsLexicographicallyBetter(
                        counts,
                        previousAmount,
                        amount,
                        optionIndex,
                        options.Length))
                {
                    continue;
                }

                minimumPacks[amount] = candidatePacks;
                var previousOffset = previousAmount * options.Length;
                var currentOffset = amount * options.Length;
                Array.Copy(counts, previousOffset, counts, currentOffset, options.Length);
                counts[currentOffset + optionIndex]++;
            }
        }

        var packedAmount = limit;
        while (packedAmount > 0 && minimumPacks[packedAmount] == int.MaxValue)
            packedAmount--;

        var packedOffset = packedAmount * options.Length;
        var packs = options.Select((option, index) => (option, count: counts[packedOffset + index]))
            .Where(item => item.count > 0)
            .Select(item => new IngredientPackCount(
                item.option.ConversionId,
                item.option.Code,
                item.option.Name,
                item.option.UnitName,
                item.option.UnitSymbol,
                item.option.FactorToBase,
                item.count))
            .ToArray();
        var packedScaled = checked((long)packedAmount * divisor);
        var remainder = Unscale(totalScaled - packedScaled);
        return new IngredientConversionBreakdown(total, packs, remainder);
    }

    private static bool IsLexicographicallyBetter(
        int[] counts,
        int previousAmount,
        int currentAmount,
        int addedOptionIndex,
        int optionCount)
    {
        var previousOffset = previousAmount * optionCount;
        var currentOffset = currentAmount * optionCount;
        for (var index = 0; index < optionCount; index++)
        {
            var candidate = counts[previousOffset + index] + (index == addedOptionIndex ? 1 : 0);
            var current = counts[currentOffset + index];
            if (candidate != current)
                return candidate > current;
        }
        return false;
    }

    private static long GreatestCommonDivisor(long left, long right)
    {
        left = Math.Abs(left);
        right = Math.Abs(right);
        while (right != 0)
        {
            var remainder = left % right;
            left = right;
            right = remainder;
        }
        return left;
    }

    private static long Scale(decimal value)
    {
        var normalized = Normalize(value);
        if (normalized > long.MaxValue / (decimal)DecimalScale)
            throw new DomainException("Quantity is too large to optimize.");
        return decimal.ToInt64(normalized * DecimalScale);
    }

    private static decimal Unscale(long value) => Normalize(value / (decimal)DecimalScale);

    private static decimal Normalize(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);
}
