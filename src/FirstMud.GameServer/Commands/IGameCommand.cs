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
