namespace Lamie.Application.Common.Persistence;

public sealed record ProductReferenceSet(
    int CategoryId,
    int ProductTypeId,
    IReadOnlyCollection<int> TagIds,
    IReadOnlyCollection<int> ColorIds,
    IReadOnlyCollection<int> CollectionIds,
    IReadOnlyCollection<int> StyleIds,
    IReadOnlyCollection<int> OccasionIds,
    IReadOnlyCollection<string> LanguageCodes);

public enum ReferencedMasterData
{
    Category,
    Collection,
    Color,
    Language,
    Occasion,
    ProductType,
    Style,
    Tag
}

public interface IReferentialIntegrityService
{
    Task ValidateProductReferencesAsync(
        ProductReferenceSet references,
        CancellationToken cancellationToken = default);

    Task EnsureCanDeleteAsync(
        ReferencedMasterData target,
        object id,
        CancellationToken cancellationToken = default);
}
