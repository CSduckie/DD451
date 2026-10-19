using System;

public enum ItemOwner
{
    World,
    Container,
    Backpack,
    Storage,
    PendingRecovery,
    Consumed,
    Lost
}

public enum ItemLossReason
{
    ExplorationDeath,
    ShelterAbandonment,
    LeftBehindOnExtraction,
    BackpackDiscard
}

public enum ItemUseResult
{
    Success,
    NotFound,
    NotCarried,
    NotUsable,
    InvalidTarget,
    FullHealth,
    RoundNotStarted,
    RoundLimitReached,
    InvalidConfiguration,
    NoHealingApplied,
    Busy
}

// 外部只能查询；归属、额度和丢失记录统一经ItemManager修改。
public sealed class ItemInstance
{
    public string InstanceId { get; }
    public ItemDataSO Definition { get; }
    public string DefinitionId { get; }
    public string UniqueId { get; }
    public ItemOwner Owner { get; internal set; }
    public string ContainerId { get; internal set; }
    public string SourceId { get; }
    public bool IsRetained { get; internal set; }
    public int UseRound { get; internal set; }
    public int UsesThisRound { get; internal set; }
    public string OriginalSpawnPointId { get; }
    public bool IsPendingRecovery { get { return Owner == ItemOwner.PendingRecovery; } }
    public int RecoveryRound { get; internal set; }
    public ItemLossReason? LastLossReason { get; internal set; }
    public bool SpawnClaimed { get; internal set; }

    internal ItemInstance(ItemDataSO definition, ItemOwner owner, string containerId,
        string sourceId, string originalSpawnPointId, int round)
    {
        InstanceId = Guid.NewGuid().ToString("N");
        Definition = definition;
        // 身份字段在登记时固定，避免运行时编辑SO造成索引变动。
        DefinitionId = definition.definitionId;
        UniqueId = definition.isUnique ? definition.uniqueId : string.Empty;
        Owner = owner;
        ContainerId = containerId;
        SourceId = sourceId;
        OriginalSpawnPointId = originalSpawnPointId;
        UseRound = round;
        SpawnClaimed = owner == ItemOwner.Backpack || owner == ItemOwner.Storage;
    }
}
