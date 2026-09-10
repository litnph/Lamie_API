using Microsoft.AspNetCore.Http;

namespace Lamie.Application.Common.Uploads;

public static class InvoiceUploadPolicy
{
    public const long MaximumFileBytes = 10 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> AllowedTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
            [".pdf"] = "application/pdf"
        };

    public static bool HasAllowedMetadata(IFormFile? file)
    {
        if (file is null)
            return true;
        if (file.Length is <= 0 or > MaximumFileBytes)
            return false;
        var extension = Path.GetExtension(Path.GetFileName(file.FileName));
        return AllowedTypes.TryGetValue(extension, out var expected) &&
            string.Equals(file.ContentType, expected, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<bool> HasValidSignatureAsync(IFormFile? file, CancellationToken cancellationToken = default)
    {
        if (file is null)
            return true;
        if (!HasAllowedMetadata(file))
            return false;

        var header = new byte[12];
        await using var stream = file.OpenReadStream();
        var bytesRead = await stream.ReadAsync(header, cancellationToken);
        var extension = Path.GetExtension(Path.GetFileName(file.FileName)).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" => bytesRead >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".webp" => bytesRead >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            ".pdf" => bytesRead >= 5 && header.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
            _ => false
        };
    }
}
