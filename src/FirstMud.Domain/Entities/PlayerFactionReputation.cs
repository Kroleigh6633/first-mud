using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;

namespace FirstMud.Domain.Entities;

public class PlayerFactionReputation
{
    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public FactionId FactionId { get; private set; }
    public ReputationScore Score { get; private set; } = ReputationScore.Zero;
    public bool HasPermanentFloor { get; private set; }
    public ReputationTier? PermanentFloor { get; private set; }

    private PlayerFactionReputation() { }

    public static PlayerFactionReputation Create(Guid playerId, FactionId factionId) => new()
    {
        Id = Guid.NewGuid(),
        PlayerId = playerId,
        FactionId = factionId,
        Score = ReputationScore.Zero
    };

    public void Adjust(int delta) => Score = delta > 0 ? Score.Add(delta) : Score.Subtract(Math.Abs(delta));

    public void SetPermanentFloor(ReputationTier floor)
    {
        HasPermanentFloor = true;
        PermanentFloor = floor;
    }

    public ReputationTier EffectiveTier =>
        HasPermanentFloor && PermanentFloor.HasValue && Score.Tier > PermanentFloor.Value
            ? PermanentFloor.Value
            : Score.Tier;
}
