namespace Lamie.Domain.Entities;

public class ProductStyle : Entity
{
    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public int StyleId { get; private set; }

    private ProductStyle() { } // EF

    internal ProductStyle(int styleId)
    {
        StyleId = styleId;
    }
}

