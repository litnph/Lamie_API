namespace Lamie.Domain.Entities;

public class ProductOccasion : Entity
{
    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public int OccasionId { get; private set; }

    private ProductOccasion() { } // EF

    internal ProductOccasion(int occasionId)
    {
        OccasionId = occasionId;
    }
}

