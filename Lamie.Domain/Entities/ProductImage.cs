using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public class ProductImage : Entity
{
    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public string ImageUrl { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }
    public byte[]? VisualEmbedding { get; private set; }
    public string? VisualEmbeddingVersion { get; private set; }

    private ProductImage()
    {
    }

    internal ProductImage(
        string imageUrl,
        int sortOrder,
        byte[]? visualEmbedding = null,
        string? visualEmbeddingVersion = null)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new DomainException("Image URL is required");

        ImageUrl = imageUrl;
        SortOrder = sortOrder;
        IsActive = true;
        SetVisualEmbedding(visualEmbedding, visualEmbeddingVersion);
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    internal void Update(
        string imageUrl,
        int sortOrder,
        byte[]? visualEmbedding = null,
        string? visualEmbeddingVersion = null)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new DomainException("Image URL is required");

        var imageChanged = !string.Equals(ImageUrl, imageUrl, StringComparison.OrdinalIgnoreCase);
        ImageUrl = imageUrl;
        SortOrder = sortOrder;
        IsActive = true;
        if (imageChanged || visualEmbedding is not null)
            SetVisualEmbedding(visualEmbedding, visualEmbeddingVersion);
    }

    public void SetVisualEmbedding(byte[]? visualEmbedding, string? visualEmbeddingVersion)
    {
        VisualEmbedding = visualEmbedding;
        VisualEmbeddingVersion = visualEmbedding is null
            ? null
            : visualEmbeddingVersion;
    }
}
