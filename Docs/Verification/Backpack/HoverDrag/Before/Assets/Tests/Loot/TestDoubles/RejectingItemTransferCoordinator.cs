// 只拒绝06的一个实际提交点，布局与身份始终使用真实01/04/05。
public class RejectingItemTransferCoordinator : ItemTransferCoordinator
{
    public int failStep;
    public int reachedStep;

    protected override BackpackResult AddToBackpack(string instanceId, int x, int y, bool rotated)
    {
        reachedStep = 1;
        return failStep == 1 ? BackpackResult.Busy : base.AddToBackpack(instanceId, x, y, rotated);
    }

    protected override WarehouseResult AddToWarehouse(string instanceId, int x, int y, bool rotated)
    {
        reachedStep = 1;
        return failStep == 1 ? WarehouseResult.Busy : base.AddToWarehouse(instanceId, x, y, rotated);
    }

    protected override BackpackResult RemoveFromBackpack(string instanceId, out BackpackPlacement removed)
    {
        reachedStep = 2;
        removed = null;
        return failStep == 2 ? BackpackResult.Busy : base.RemoveFromBackpack(instanceId, out removed);
    }

    protected override WarehouseResult RemoveFromWarehouse(string instanceId, out WarehousePlacement removed)
    {
        reachedStep = 2;
        removed = null;
        return failStep == 2 ? WarehouseResult.Busy : base.RemoveFromWarehouse(instanceId, out removed);
    }

    protected override bool CommitOwner(string instanceId, ItemOwner owner, string containerId)
    {
        reachedStep = 3;
        return failStep != 3 && base.CommitOwner(instanceId, owner, containerId);
    }

    protected override bool CommitLoss(string instanceId)
    {
        reachedStep = 3;
        return failStep != 3 && base.CommitLoss(instanceId);
    }

    protected override bool AddToSource(LootSourceState source, ItemInstance item, int x, int y, bool rotated,
        out LootEntryState entry, out bool reusedTakenEntry)
    {
        reachedStep = 1;
        entry = null;
        reusedTakenEntry = false;
        return failStep != 1 && base.AddToSource(source, item, x, y, rotated, out entry, out reusedTakenEntry);
    }
}
