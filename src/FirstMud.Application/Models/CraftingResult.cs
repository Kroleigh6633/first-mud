using FirstMud.Domain.Entities;

namespace FirstMud.Application.Models;

public enum CraftingOutcome
{
    Success,
    NearMiss,
    UnexpectedResult,
    ComponentLoss,
    Discovery
}

public record CraftingResult(
    CraftingOutcome Outcome,
    Item? ProducedItem,
    string Message,
    bool IsFirstDiscovery,
    string? DiscoveryRecipeId
);
