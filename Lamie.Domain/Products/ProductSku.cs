using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Lamie.Domain.Products;

public static partial class ProductSku
{
    public const int Length = 4;
    public const int MaximumGenerationAttempts = 20;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    [GeneratedRegex("^[A-Z0-9]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatRegex();

    public static bool IsValidNewSku(string? value) => value is not null && FormatRegex().IsMatch(value);

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    public static string Generate()
    {
        Span<char> result = stackalloc char[Length];
        for (var index = 0; index < result.Length; index++)
            result[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(result);
    }

    public static async Task<string> GenerateUniqueAsync(
        Func<string, CancellationToken, Task<bool>> exists,
        CancellationToken cancellationToken = default,
        Func<string>? candidateFactory = null)
    {
        candidateFactory ??= Generate;
        for (var attempt = 0; attempt < MaximumGenerationAttempts; attempt++)
        {
            var candidate = candidateFactory();
            if (!await exists(candidate, cancellationToken)) return candidate;
        }
        throw new InvalidOperationException("A unique four-character SKU could not be generated.");
    }
}
