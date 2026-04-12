using FirstMud.Domain.Enums;

namespace FirstMud.GameServer.Commands;

public interface IGameCommand { Guid PlayerId { get; } }

public record MoveCommand(Guid PlayerId, int DeltaX, int DeltaY) : IGameCommand;
public record AttackCommand(Guid PlayerId, Guid TargetId) : IGameCommand;
public record UseSkillCommand(Guid PlayerId, string SkillId, Guid? TargetId) : IGameCommand;
public record InteractCommand(Guid PlayerId, Guid ObjectId) : IGameCommand;
public record PickupItemCommand(Guid PlayerId, Guid ItemId) : IGameCommand;
public record OpenInventoryCommand(Guid PlayerId) : IGameCommand;
public record CraftCommand(Guid PlayerId, string RecipeId, List<Guid> ComponentIds, Guid? TaperId) : IGameCommand;
public record AcceptQuestCommand(Guid PlayerId, string QuestId) : IGameCommand;
public record CompleteQuestCommand(Guid PlayerId, string QuestId, string ChosenOutcome) : IGameCommand;
public record UsePortalCommand(Guid PlayerId, WorldId DestinationWorld) : IGameCommand;
public record ManageBaseAssetCommand(Guid PlayerId, string Action, Guid? AssetId) : IGameCommand;
public record StartCombatCommand(Guid PlayerId, Guid ZoneId) : IGameCommand;
public record UseCombatAbilityCommand(Guid PlayerId, Guid EncounterId, string AbilityName, Guid? TargetId) : IGameCommand;
public record FleeCombatCommand(Guid PlayerId, Guid EncounterId) : IGameCommand;
public record GetAvailableQuestsCommand(Guid PlayerId) : IGameCommand;
public record EnterZoneCommand(Guid PlayerId, int WorldId, Guid ZoneId) : IGameCommand;
public record PortalHomeCommand(Guid PlayerId) : IGameCommand;
public record PortalBackCommand(Guid PlayerId) : IGameCommand;
public record HarvestCommand(Guid PlayerId) : IGameCommand;
public record DepositCommand(Guid PlayerId, Guid ItemId) : IGameCommand;
public record WithdrawCommand(Guid PlayerId, Guid ItemId) : IGameCommand;
public record OpenStorageCommand(Guid PlayerId) : IGameCommand;
public record AutoFarmCommand(Guid PlayerId) : IGameCommand;
public record EquipCommand(Guid PlayerId, Guid ItemId) : IGameCommand;
public record UnequipCommand(Guid PlayerId, string Slot) : IGameCommand;
public record SalvageCommand(Guid PlayerId, Guid ItemId) : IGameCommand;
public record SalvageAllCommand(Guid PlayerId, string Category) : IGameCommand;
public record SetAutoSalvageCommand(Guid PlayerId, string Category, int MaxWorkmanship) : IGameCommand;
public record LockItemCommand(Guid PlayerId, Guid ItemId) : IGameCommand;
public record ViewCompanionsCommand(Guid PlayerId) : IGameCommand;
public record ActivateCompanionCommand(Guid PlayerId, Guid CompanionId) : IGameCommand;
public record DeactivateCompanionCommand(Guid PlayerId, Guid CompanionId) : IGameCommand;
public record ImbueCommand(Guid PlayerId, Guid ItemId, Guid TaperId) : IGameCommand;
public record AssignCompanionDutyCommand(Guid PlayerId, Guid CompanionId, string Duty) : IGameCommand;
public record RecallCompanionCommand(Guid PlayerId, Guid CompanionId) : IGameCommand;
public record QueueSalvageCommand(Guid PlayerId, Guid ItemId) : IGameCommand;
public record ViewRecipesCommand(Guid PlayerId) : IGameCommand;
public record UseConsumableCommand(Guid PlayerId, Guid ItemId) : IGameCommand;
