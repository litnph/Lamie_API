using Lamie.Application.Common.Exceptions;

namespace Lamie.Application.FeData;

public sealed record FeDataExportResultDto(
    DateTimeOffset GeneratedAt,
    int ProductCount,
    int ImageCount,
    string CatalogVersion,
    IReadOnlyList<string> Warnings);

public interface IFeDataExportService
{
    Task<FeDataExportResultDto> ExportAsync(CancellationToken cancellationToken);
}

public sealed class FeDataExportException(
    string message,
    IReadOnlyList<string> issues)
    : BaseException(message, "FE_DATA_EXPORT_FAILED")
{
    public IReadOnlyList<string> Issues { get; } = issues;
}
