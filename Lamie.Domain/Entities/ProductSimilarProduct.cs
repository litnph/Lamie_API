using Lamie.Domain.Exceptions;

namespace Lamie.Domain.Entities;

public sealed class ProductSimilarProduct : Entity
{
    private ProductSimilarProduct()
    {
    }

    internal ProductSimilarProduct(int similarProductId)
    {
        if (similarProductId <= 0)
            throw new DomainException("Similar product id must be greater than 0");

        SimilarProductId = similarProductId;
    }

    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public int SimilarProductId { get; private set; }
}
