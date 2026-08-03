namespace Lamie.Domain.Entities;

public class ProductTag : Entity
{
    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public int TagId { get; private set; }

    private ProductTag() { } // EF

    internal ProductTag(int tagId)
    {
        TagId = tagId;
    }
}

