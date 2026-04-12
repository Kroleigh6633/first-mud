using FirstMud.Domain.Enums;

namespace FirstMud.Domain.Entities;

public class ResourceNode
{
    public Guid Id { get; private set; }
    public Guid ZoneId { get; private set; }
    public ResourceType ResourceType { get; private set; }
    public int RemainingYield { get; private set; }
    public int MaxYield { get; private set; }
    public int RegenerationRate { get; private set; }

    private ResourceNode() { }

    public static ResourceNode Create(
        Guid zoneId,
        ResourceType resourceType,
        int maxYield,
        int regenerationRate = 1)
    {
        return new ResourceNode
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            ResourceType = resourceType,
            MaxYield = maxYield,
            RemainingYield = maxYield,
            RegenerationRate = regenerationRate,
        };
    }

    /// <summary>Harvest up to <paramref name="amount"/> units. Returns actual harvested count.</summary>
    public int Harvest(int amount)
    {
        var actual = Math.Min(amount, RemainingYield);
        RemainingYield -= actual;
        return actual;
    }

    /// <summary>Called on the regeneration tick — adds up to MaxYield.</summary>
    public void Regenerate()
    {
        RemainingYield = Math.Min(MaxYield, RemainingYield + RegenerationRate);
    }

    public bool IsDepletedOf(int requestedAmount) => RemainingYield < requestedAmount;
}
