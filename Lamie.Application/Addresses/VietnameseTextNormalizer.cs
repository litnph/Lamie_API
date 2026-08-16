using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lamie.Application.Addresses;

public static partial class VietnameseTextNormalizer
{
    public static string Display(string value) => WhitespaceRegex()
        .Replace(value.Normalize(NormalizationForm.FormC).Trim(), " ");

    public static string Search(string value)
    {
        var decomposed = Display(value)
            .Replace('đ', 'd')
            .Replace('Đ', 'D')
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        }

        return WhitespaceRegex().Replace(PunctuationRegex().Replace(builder.ToString(), " "), " ").Trim();
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex PunctuationRegex();
}
