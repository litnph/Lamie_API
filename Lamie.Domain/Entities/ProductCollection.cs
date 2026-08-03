namespace Lamie.Domain.Entities;

public class ProductCollection : Entity
{
    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public int CollectionId { get; private set; }

    private ProductCollection() { } // EF

    internal ProductCollection(int collectionId)
    {
        CollectionId = collectionId;
    }
}

