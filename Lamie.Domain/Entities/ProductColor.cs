namespace Lamie.Domain.Entities;

public class ProductColor : Entity
{
    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public int ColorId { get; private set; }

    private ProductColor() { } // EF

    internal ProductColor(int colorId)
    {
        ColorId = colorId;
    }
}

