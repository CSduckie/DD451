using System;
using UnityEngine;

// 01保存身份，04/05分别保存格子；这里仅协调一次同步提交。
[DisallowMultipleComponent]
public class ItemTransferCoordinator : MonoBehaviour
{
    public ItemManager itemManager;
    public BackpackManager backpackManager;
    public WarehouseManager warehouseManager;
    public ShelterManager shelterManager;
    public LootRuntimeManager lootRuntime;
    public PlayerController player;

    private bool busy;

    public bool IsBusy { get { return busy; } }
    public event Action<string> Completed;

    public bool Initialize()
    {
        GameBootstrap root = GameBootstrap.Instance;
        if (root != null && root.isActiveAndEnabled)
        {
            itemManager = root.GetComponentInChildren<ItemManager>(true);
            backpackManager = root.GetComponentInChildren<BackpackManager>(true);
            warehouseManager = root.GetComponentInChildren<WarehouseManager>(true);
            shelterManager = root.GetComponentInChildren<ShelterManager>(true);
            lootRuntime = root.GetComponentInChildren<LootRuntimeManager>(true);
        }
        return isActiveAndEnabled && itemManager != null && itemManager.isActiveAndEnabled
            && backpackManager != null && backpackManager.isActiveAndEnabled
            && backpackManager.Initialize(itemManager) && backpackManager.Items == itemManager;
    }

    public ItemTransferResult TryBackpackToWarehouse(string instanceId, int x, int y, bool rotated,
        UnityEngine.Object interactionOwner = null, Func<bool> requestIsValid = null)
    {
        return TransferContainers(true, instanceId, x, y, rotated, interactionOwner, requestIsValid);
    }

    public ItemTransferResult TryWarehouseToBackpack(string instanceId, int x, int y, bool rotated,
        UnityEngine.Object interactionOwner = null, Func<bool> requestIsValid = null)
    {
        return TransferContainers(false, instanceId, x, y, rotated, interactionOwner, requestIsValid);
    }

    private ItemTransferResult TransferContainers(bool fromBackpack, string instanceId, int x, int y,
        bool rotated, UnityEngine.Object interactionOwner, Func<bool> requestIsValid)
    {
        if (busy) { return ItemTransferResult.Busy; }
        busy = true;
        bool releaseClaim = false;
        UnityEngine.Object claimOwner = interactionOwner != null ? interactionOwner : this;
        try
        {
            ItemTransferResult validation = ValidateRequest(requestIsValid, false);
            if (validation != ItemTransferResult.Success) { return validation; }
            if (warehouseManager == null || !warehouseManager.isActiveAndEnabled
                || !warehouseManager.Initialize(itemManager) || warehouseManager.Items != itemManager)
            {
                return ItemTransferResult.NotReady;
            }
            if (!warehouseManager.IsManagementAllowed) { return ItemTransferResult.NotAllowed; }
            if (warehouseManager.HasPendingInteraction && !warehouseManager.OwnsInteraction(claimOwner))
            {
                return ItemTransferResult.Busy;
            }
            if (!warehouseManager.OwnsInteraction(claimOwner))
            {
                if (!warehouseManager.TryClaimInteraction(claimOwner)) { return ItemTransferResult.Busy; }
                releaseClaim = true;
            }

            // 保留原摆放直到确认目标合法。预览期间不移走来源，取消时无须“补回物品”。
            BackpackPlacement backpackOriginal = null;
            WarehousePlacement warehouseOriginal = null;
            ItemInstance item;
            if (fromBackpack)
            {
                if (backpackManager.TryGetCarriedItem(instanceId, out item) != BackpackResult.Success
                    || !backpackManager.TryGetPlacement(instanceId, out backpackOriginal))
                {
                    return ItemTransferResult.SourceInvalid;
                }
                validation = FromWarehouse(warehouseManager.CanPlaceExternal(instanceId, x, y, rotated));
            }
            else
            {
                if (warehouseManager.TryGetStoredItem(instanceId, out item) != WarehouseResult.Success
                    || !warehouseManager.TryGetPlacement(instanceId, out warehouseOriginal))
                {
                    return ItemTransferResult.SourceInvalid;
                }
                validation = FromBackpack(backpackManager.CanPlaceExternal(instanceId, x, y, rotated));
            }
            if (validation != ItemTransferResult.Success) { return validation; }
            if (!warehouseManager.IsManagementAllowed || !warehouseManager.OwnsInteraction(claimOwner))
            {
                return ItemTransferResult.NotAllowed;
            }
            // 申请交互会同步通知UI；监听者可能已关闭面板，首个写入前再核对。
            validation = ValidateRequest(requestIsValid, false);
            if (validation != ItemTransferResult.Success) { return validation; }

            // 以下方法均静默，期间不await、不发UI通知；两套格子仍各自执行合法性检查。
            validation = fromBackpack
                ? FromWarehouse(AddToWarehouse(instanceId, x, y, rotated))
                : FromBackpack(AddToBackpack(instanceId, x, y, rotated));
            if (validation != ItemTransferResult.Success) { return validation; }

            bool removed = fromBackpack
                ? RemoveFromBackpack(instanceId, out _) == BackpackResult.Success
                : RemoveFromWarehouse(instanceId, out _) == WarehouseResult.Success;
            if (!removed)
            {
                return UndoTarget(fromBackpack, instanceId)
                    ? ItemTransferResult.SourceRemovalFailed : ReportRollbackFailure(instanceId);
            }

            // 归属放在最后：TrySetOwner还会锁定SpawnClaimed，不能用它充当可随意撤销的预览。
            ItemOwner targetOwner = fromBackpack ? ItemOwner.Storage : ItemOwner.Backpack;
            string targetContainer = fromBackpack ? warehouseManager.ContainerId : backpackManager.ContainerId;
            if (!CommitOwner(instanceId, targetOwner, targetContainer))
            {
                bool targetRemoved = UndoTarget(fromBackpack, instanceId);
                bool sourceRestored = fromBackpack
                    ? backpackManager.TryRestorePlacement(backpackOriginal) == BackpackResult.Success
                    : warehouseManager.TryRestorePlacement(warehouseOriginal) == WarehouseResult.Success;
                return targetRemoved && sourceRestored
                    ? ItemTransferResult.OwnerRejected : ReportRollbackFailure(instanceId);
            }

            NotifyCompleted(instanceId, true, true, null);
            return ItemTransferResult.Success;
        }
        finally
        {
            // 通知也在busy内，监听者不能在收到完整状态时重入提交另一遍。
            if (releaseClaim && warehouseManager != null) { warehouseManager.ReleaseInteraction(claimOwner); }
            busy = false;
        }
    }

    public ItemTransferResult TrySourceToBackpack(LootSourceState source, string entryId,
        int x, int y, bool rotated, Func<bool> requestIsValid = null)
    {
        if (busy) { return ItemTransferResult.Busy; }
        busy = true;
        try
        {
            ItemTransferResult validation = ValidateRequest(requestIsValid);
            if (validation != ItemTransferResult.Success) { return validation; }
            if (lootRuntime == null || !lootRuntime.isActiveAndEnabled) { return ItemTransferResult.NotReady; }
            if (source == null || !lootRuntime.TryGetSource(source.SourceId, out LootSourceState current)
                || !ReferenceEquals(source, current) || source.Round != itemManager.CurrentRound
                || !source.TryGetEntry(entryId, out LootEntryState entry) || entry.Taken)
            {
                return ItemTransferResult.SourceInvalid;
            }
            if (!entry.Revealed) { return ItemTransferResult.SourceHidden; }
            if (!lootRuntime.IsExplorationActive) { return ItemTransferResult.NotAllowed; }
            if (!lootRuntime.TryValidatePickup(source.SourceId, entryId, player.interaction, out ItemInstance item))
            {
                return ItemTransferResult.Unreachable;
            }
            validation = FromBackpack(backpackManager.CanPlaceExternal(item.InstanceId, x, y, rotated));
            if (validation != ItemTransferResult.Success) { return validation; }
            validation = FromBackpack(AddToBackpack(item.InstanceId, x, y, rotated));
            if (validation != ItemTransferResult.Success) { return validation; }
            if (!lootRuntime.TryMarkTaken(source.SourceId, entryId))
            {
                return backpackManager.TryRemovePlacement(item.InstanceId, out _) == BackpackResult.Success
                    ? ItemTransferResult.SourceRemovalFailed : ReportRollbackFailure(item.InstanceId);
            }
            if (!CommitOwner(item.InstanceId, ItemOwner.Backpack, backpackManager.ContainerId))
            {
                bool targetRemoved = backpackManager.TryRemovePlacement(item.InstanceId, out _) == BackpackResult.Success;
                bool sourceRestored = lootRuntime.TryRestoreTaken(source.SourceId, entryId);
                return targetRemoved && sourceRestored
                    ? ItemTransferResult.OwnerRejected : ReportRollbackFailure(item.InstanceId);
            }
            // 表现只在数据已成功后清理；显示异常不能把已发生的转移伪报为失败。
            SafeNotify(() => lootRuntime.CompletePickupVisual(item.InstanceId));
            NotifyCompleted(item.InstanceId, true, false, source.SourceId);
            return ItemTransferResult.Success;
        }
        finally { busy = false; }
    }

    public ItemTransferResult TryBackpackToSource(LootSourceState source, string instanceId,
        Func<bool> requestIsValid = null)
    {
        if (busy) { return ItemTransferResult.Busy; }
        busy = true;
        try
        {
            ItemTransferResult validation = ValidateRequest(requestIsValid);
            if (validation != ItemTransferResult.Success) { return validation; }
            if (lootRuntime == null || !lootRuntime.isActiveAndEnabled) { return ItemTransferResult.NotReady; }
            if (source == null || !lootRuntime.TryGetSource(source.SourceId, out LootSourceState current)
                || !ReferenceEquals(source, current) || source.Round != itemManager.CurrentRound)
            {
                return ItemTransferResult.SourceInvalid;
            }
            // 箱子使用已生成的条目列表；背包丢弃走独立确认入口，不生成临时地面物品。
            if (source.IsGround || !lootRuntime.IsExplorationActive) { return ItemTransferResult.NotAllowed; }
            if (!lootRuntime.TryValidateReturnTarget(source, player.interaction)) { return ItemTransferResult.Unreachable; }
            if (backpackManager.TryGetCarriedItem(instanceId, out ItemInstance item) != BackpackResult.Success
                || !backpackManager.TryGetPlacement(instanceId, out BackpackPlacement original))
            {
                return ItemTransferResult.SourceInvalid;
            }

            // 固定箱格先检查容量；实际添加还会重查，UI预览不承担提交合法性。
            if (!lootRuntime.CanAcceptReturnedItem(source, item)) { return ItemTransferResult.Occupied; }

            // 先保留背包原摆放并静默准备目标。放回的物品已被玩家发现，不重搜、不重抽。
            if (!AddToSource(source, item, out LootEntryState entry, out bool reusedTakenEntry))
            {
                return ItemTransferResult.TargetRejected;
            }
            if (RemoveFromBackpack(instanceId, out _) != BackpackResult.Success)
            {
                return lootRuntime.TryUndoReturnedItem(source, entry.EntryId, reusedTakenEntry)
                    ? ItemTransferResult.SourceRemovalFailed : ReportRollbackFailure(instanceId);
            }
            if (!CommitOwner(instanceId, ItemOwner.Container, source.SourceId))
            {
                bool targetRestored = lootRuntime.TryUndoReturnedItem(source, entry.EntryId, reusedTakenEntry);
                bool backpackRestored = backpackManager.TryRestorePlacement(original) == BackpackResult.Success;
                return targetRestored && backpackRestored
                    ? ItemTransferResult.OwnerRejected : ReportRollbackFailure(instanceId);
            }
            NotifyCompleted(instanceId, true, false, source.SourceId);
            return ItemTransferResult.Success;
        }
        finally { busy = false; }
    }

    public ItemTransferResult TryDropFromBackpack(string instanceId, Func<bool> requestIsValid = null)
    {
        // 兼容既有调用；用户丢弃语义统一为普通物销毁、唯一物下一轮原点找回。
        return TryDiscardFromBackpack(instanceId, requestIsValid);
    }

    public ItemTransferResult TryDiscardFromBackpack(string instanceId, Func<bool> requestIsValid = null)
    {
        if (busy) { return ItemTransferResult.Busy; }
        busy = true;
        try
        {
            ItemTransferResult validation = ValidateRequest(requestIsValid);
            if (validation != ItemTransferResult.Success) { return validation; }
            if (lootRuntime != null && !lootRuntime.isActiveAndEnabled) { return ItemTransferResult.NotReady; }
            // 04独立场景可无06运行模块；已接入06时仍遵循当前探索权限。
            if ((lootRuntime != null && !lootRuntime.IsExplorationActive)
                || (warehouseManager != null && warehouseManager.IsManagementAllowed))
            {
                return ItemTransferResult.NotAllowed;
            }
            if (backpackManager.TryGetCarriedItem(instanceId, out _) != BackpackResult.Success
                || !backpackManager.TryGetPlacement(instanceId, out BackpackPlacement original))
            {
                return ItemTransferResult.SourceInvalid;
            }
            // 只静默移除占格；01记损是最后提交点，失败仍能恢复原坐标和朝向。
            if (RemoveFromBackpack(instanceId, out _) != BackpackResult.Success)
            {
                return ItemTransferResult.SourceRemovalFailed;
            }
            if (!CommitLoss(instanceId))
            {
                return backpackManager.TryRestorePlacement(original) == BackpackResult.Success
                    ? ItemTransferResult.OwnerRejected : ReportRollbackFailure(instanceId);
            }
            // 01保留身份账本；Lost不可拾取，PendingRecovery仅由正式下一轮在原点领取。
            NotifyCompleted(instanceId, true, false, null);
            return ItemTransferResult.Success;
        }
        finally { busy = false; }
    }

    private ItemTransferResult ValidateRequest(Func<bool> requestIsValid, bool requiresExplorationPlayer = true)
    {
        if (!Initialize()) { return ItemTransferResult.NotReady; }
        // 04允许庇护所无场景玩家或尚未准备探索；这里仍由05权限与UI请求共同把关。
        if ((requiresExplorationPlayer && (player == null || !player.IsPrepared))
            || (player != null && (!player.isActiveAndEnabled || player.IsDead || !player.IsBackpackOpen)))
        {
            return ItemTransferResult.NotAllowed;
        }
        // UI传入当前拖拽编号/会话的只读检查；迟到松手和已关闭会话不能提交。
        if (requestIsValid == null) { return ItemTransferResult.StaleRequest; }
        try { return requestIsValid() ? ItemTransferResult.Success : ItemTransferResult.StaleRequest; }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            return ItemTransferResult.StaleRequest;
        }
    }

    private bool UndoTarget(bool targetIsWarehouse, string instanceId)
    {
        return targetIsWarehouse
            ? warehouseManager.TryRemovePlacement(instanceId, out _) == WarehouseResult.Success
            : backpackManager.TryRemovePlacement(instanceId, out _) == BackpackResult.Success;
    }

    private ItemTransferResult ReportRollbackFailure(string instanceId)
    {
        Debug.LogError("物品转移恢复失败，需检查双方布局：" + instanceId, this);
        return ItemTransferResult.RollbackFailed;
    }

    private void NotifyCompleted(string instanceId, bool backpack, bool warehouse, string sourceId)
    {
        if (backpack) { SafeNotify(backpackManager.NotifyChanged); }
        if (warehouse) { SafeNotify(warehouseManager.NotifyChanged); }
        if (sourceId != null && lootRuntime != null) { SafeNotify(() => lootRuntime.NotifySourceChanged(sourceId)); }
        if (shelterManager != null) { SafeNotify(() => shelterManager.RefreshEv()); }
        if (Completed == null) { return; }
        foreach (Action<string> listener in Completed.GetInvocationList())
        {
            SafeNotify(() => listener(instanceId));
        }
    }

    private void SafeNotify(Action callback)
    {
        try { callback(); }
        catch (Exception exception) { Debug.LogException(exception, this); }
    }

    // 具体提交点便于故障测试拒绝其中一步；测试仍使用真实01及两套真实布局。
    // 不引入容器基类、通用事务对象或修改01/04/05的访问权限。
    protected virtual BackpackResult AddToBackpack(string instanceId, int x, int y, bool rotated)
    {
        return backpackManager.TryAddPlacement(instanceId, x, y, rotated);
    }

    protected virtual WarehouseResult AddToWarehouse(string instanceId, int x, int y, bool rotated)
    {
        return warehouseManager.TryAddPlacement(instanceId, x, y, rotated);
    }

    protected virtual BackpackResult RemoveFromBackpack(string instanceId, out BackpackPlacement removed)
    {
        return backpackManager.TryRemovePlacement(instanceId, out removed);
    }

    protected virtual WarehouseResult RemoveFromWarehouse(string instanceId, out WarehousePlacement removed)
    {
        return warehouseManager.TryRemovePlacement(instanceId, out removed);
    }

    protected virtual bool CommitOwner(string instanceId, ItemOwner owner, string containerId)
    {
        return itemManager.TrySetOwner(instanceId, owner, containerId);
    }

    protected virtual bool CommitLoss(string instanceId)
    {
        return itemManager.RecordLoss(instanceId, ItemLossReason.BackpackDiscard);
    }

    protected virtual bool AddToSource(LootSourceState source, ItemInstance item,
        out LootEntryState entry, out bool reusedTakenEntry)
    {
        return lootRuntime.TryAddReturnedItem(source, item, out entry, out reusedTakenEntry);
    }

    public static ItemTransferResult FromBackpack(BackpackResult result)
    {
        switch (result)
        {
            case BackpackResult.Success: return ItemTransferResult.Success;
            case BackpackResult.NotReady: return ItemTransferResult.NotReady;
            case BackpackResult.Busy: return ItemTransferResult.Busy;
            case BackpackResult.OutOfBounds: return ItemTransferResult.OutOfBounds;
            case BackpackResult.Overlap: return ItemTransferResult.Occupied;
            case BackpackResult.RotationNotAllowed: return ItemTransferResult.InvalidRotation;
            case BackpackResult.InvalidConfiguration: return ItemTransferResult.InvalidConfiguration;
            default: return ItemTransferResult.TargetRejected;
        }
    }

    public static ItemTransferResult FromWarehouse(WarehouseResult result)
    {
        switch (result)
        {
            case WarehouseResult.Success: return ItemTransferResult.Success;
            case WarehouseResult.NotReady: return ItemTransferResult.NotReady;
            case WarehouseResult.Busy: return ItemTransferResult.Busy;
            case WarehouseResult.NotInShelter: return ItemTransferResult.NotAllowed;
            case WarehouseResult.OutOfBounds: return ItemTransferResult.OutOfBounds;
            case WarehouseResult.Overlap: return ItemTransferResult.Occupied;
            case WarehouseResult.RotationNotAllowed: return ItemTransferResult.InvalidRotation;
            case WarehouseResult.InvalidConfiguration: return ItemTransferResult.InvalidConfiguration;
            default: return ItemTransferResult.TargetRejected;
        }
    }

    public static string ResultKey(ItemTransferResult result)
    {
        return "transfer." + result.ToString();
    }
}
