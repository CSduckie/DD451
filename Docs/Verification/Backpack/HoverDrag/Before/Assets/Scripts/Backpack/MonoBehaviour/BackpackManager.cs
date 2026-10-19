using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class BackpackManager : MonoBehaviour
{
    public BackpackConfigSO config;
    public ItemManager itemManager;
    public string containerId = "player-backpack";

    private BackpackGrid grid;
    private ItemManager runtimeItems;
    private string runtimeContainerId;
    private bool busy;

    public bool IsReady { get { return grid != null && runtimeItems != null; } }
    public int Width { get { return grid != null ? grid.Width : 0; } }
    public int Height { get { return grid != null ? grid.Height : 0; } }
    public string ContainerId { get { return runtimeContainerId ?? containerId; } }
    public ItemManager Items { get { return runtimeItems; } }
    public event Action Changed;

    private void Awake()
    {
        ItemManager source = itemManager;
        GameBootstrap root = GetComponentInParent<GameBootstrap>();
        if (source == null && root != null) { source = root.GetComponentInChildren<ItemManager>(true); }
        Initialize(source);
    }

    public bool Initialize(ItemManager source)
    {
        // 重绑场景UI只取已有布局；不能在开包、切语言或重载时清空、补发物品。
        if (grid != null) { return runtimeItems != null && source == runtimeItems; }
        if (source == null || config == null || config.width <= 0 || config.height <= 0
            || string.IsNullOrWhiteSpace(containerId))
        {
            return false;
        }
        runtimeItems = source;
        runtimeContainerId = containerId;
        grid = new BackpackGrid(config.width, config.height);
        return true;
    }

    public static BackpackManager Resolve()
    {
        GameBootstrap root = GameBootstrap.Instance;
        if (root == null || !root.isActiveAndEnabled) { return null; }
        ItemManager source = root.GetComponentInChildren<ItemManager>(true);
        foreach (BackpackManager manager in root.GetComponentsInChildren<BackpackManager>(true))
        {
            if (manager.isActiveAndEnabled && manager.Initialize(source)) { return manager; }
        }
        return null;
    }

    public IReadOnlyList<BackpackPlacement> GetPlacements()
    {
        return grid != null ? grid.GetPlacements() : Array.AsReadOnly(new BackpackPlacement[0]);
    }

    public bool TryGetPlacement(string instanceId, out BackpackPlacement placement)
    {
        placement = null;
        return grid != null && grid.TryGetPlacement(instanceId, out placement);
    }

    public BackpackResult TryGetCarriedItem(string instanceId, out ItemInstance item)
    {
        item = null;
        if (!IsReady) { return BackpackResult.NotReady; }
        if (!runtimeItems.TryGetInstance(instanceId, out item)) { return BackpackResult.NotFound; }
        if (item.Owner != ItemOwner.Backpack) { return BackpackResult.WrongOwner; }
        if (item.ContainerId != runtimeContainerId) { return BackpackResult.WrongContainer; }
        if (!grid.TryGetPlacement(instanceId, out BackpackPlacement placement))
        {
            return BackpackResult.MissingPlacement;
        }
        if (!ReferenceEquals(placement.Item, item)) { return BackpackResult.InvalidConfiguration; }
        // 仅有Backpack归属不够；当前摆放也必须仍合法。
        return grid.CanMove(instanceId, placement.X, placement.Y, placement.Rotated);
    }

    public BackpackResult CanMove(string instanceId, int x, int y, bool rotated)
    {
        BackpackResult result = TryGetCarriedItem(instanceId, out _);
        return result == BackpackResult.Success ? grid.CanMove(instanceId, x, y, rotated) : result;
    }

    public BackpackResult TryMove(string instanceId, int x, int y, bool rotated)
    {
        if (busy) { return BackpackResult.Busy; }
        BackpackResult result = CanMove(instanceId, x, y, rotated);
        if (result != BackpackResult.Success) { return result; }
        busy = true;
        try
        {
            result = grid.TryMove(instanceId, x, y, rotated);
            if (result == BackpackResult.Success) { RaiseChanged(); }
            return result;
        }
        finally { busy = false; }
    }

    public BackpackResult CanPlaceExternal(string instanceId, int x, int y, bool rotated)
    {
        BackpackResult result = GetPlacementCandidate(instanceId, out ItemInstance item);
        return result == BackpackResult.Success ? grid.CanPlace(item, x, y, rotated) : result;
    }

    public BackpackResult TryFindSpaceForExternal(string instanceId, bool preferredRotated,
        out Vector2Int cell, out bool rotated)
    {
        cell = new Vector2Int(-1, -1);
        rotated = preferredRotated;
        if (busy)
        {
            return BackpackResult.Busy;
        }
        BackpackResult result = GetPlacementCandidate(instanceId, out ItemInstance item);
        if (result != BackpackResult.Success)
        {
            return result;
        }
        int directions = item.Definition != null && item.Definition.canRotate ? 2 : 1;
        BackpackResult noSpace = BackpackResult.OutOfBounds;
        // 当前方向整张格子查完才换方向；查询不移动已有物品，也不暂存占格。
        for (int direction = 0; direction < directions; direction++)
        {
            bool candidateRotated = direction == 0 ? preferredRotated : !preferredRotated;
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    result = grid.CanPlace(item, x, y, candidateRotated);
                    if (result == BackpackResult.Success)
                    {
                        cell = new Vector2Int(x, y);
                        rotated = candidateRotated;
                        return BackpackResult.Success;
                    }
                    if (result == BackpackResult.Overlap)
                    {
                        noSpace = BackpackResult.Overlap;
                    }
                    else if (result != BackpackResult.OutOfBounds)
                    {
                        return result;
                    }
                }
            }
        }
        // 有能容纳尺寸的位置但被占用时返回Overlap；所有允许方向都超大则OutOfBounds。
        return noSpace;
    }

    // 以下三个入口只修改布局且不通知；06完成来源、目标与01归属后再NotifyChanged。
    // 它们不会授予拾取许可，也不会修改ItemManager归属。
    public BackpackResult TryAddPlacement(string instanceId, int x, int y, bool rotated)
    {
        if (busy) { return BackpackResult.Busy; }
        BackpackResult result = GetPlacementCandidate(instanceId, out ItemInstance item);
        return result == BackpackResult.Success ? grid.TryAdd(item, x, y, rotated) : result;
    }

    public BackpackResult TryRemovePlacement(string instanceId, out BackpackPlacement removed)
    {
        removed = null;
        if (busy) { return BackpackResult.Busy; }
        if (!IsReady) { return BackpackResult.NotReady; }
        // 06/08可在归属已转移或损失后移除旧布局，所以这里不能要求仍为Backpack。
        return grid.TryRemove(instanceId, out removed);
    }

    public BackpackResult TryRestorePlacement(BackpackPlacement placement)
    {
        if (busy) { return BackpackResult.Busy; }
        if (placement == null) { return BackpackResult.MissingPlacement; }
        BackpackResult result = GetPlacementCandidate(placement.InstanceId, out ItemInstance item);
        if (result != BackpackResult.Success) { return result; }
        if (!ReferenceEquals(item, placement.Item)) { return BackpackResult.InvalidConfiguration; }
        return grid.TryAdd(item, placement.X, placement.Y, placement.Rotated);
    }

    public BackpackResult TryUseItem(string instanceId, PlayerHealth target, out ItemUseResult useResult)
    {
        useResult = ItemUseResult.NotFound;
        if (busy)
        {
            useResult = ItemUseResult.Busy;
            return BackpackResult.Busy;
        }
        BackpackResult result = TryGetCarriedItem(instanceId, out ItemInstance item);
        if (result != BackpackResult.Success) { return result; }
        if (target == null || !target.isActiveAndEnabled || !target.IsPrepared || target.IsDead)
        {
            useResult = ItemUseResult.InvalidTarget;
            return BackpackResult.Success;
        }
        busy = true;
        try
        {
            // 真实PlayerHealth实现IHealthTarget，04不复制医疗包/食物的使用规则。
            useResult = runtimeItems.TryUseItem(instanceId, target);
            if (useResult == ItemUseResult.Success)
            {
                if (item.Owner == ItemOwner.Consumed) { grid.TryRemove(instanceId, out _); }
                // 监听者看到成功时，食物已经不占格；医疗包仍留在原格。
                RaiseChanged();
            }
            return BackpackResult.Success;
        }
        finally { busy = false; }
    }

    public void NotifyChanged()
    {
        if (!IsReady || busy) { return; }
        busy = true;
        try { RaiseChanged(); }
        finally { busy = false; }
    }

    private BackpackResult GetPlacementCandidate(string instanceId, out ItemInstance item)
    {
        item = null;
        if (!IsReady) { return BackpackResult.NotReady; }
        if (!runtimeItems.TryGetInstance(instanceId, out item)) { return BackpackResult.NotFound; }
        if (item.Owner != ItemOwner.World && item.Owner != ItemOwner.Container
            && item.Owner != ItemOwner.Storage && item.Owner != ItemOwner.Backpack)
        {
            return BackpackResult.WrongOwner;
        }
        if (item.Owner == ItemOwner.Backpack && item.ContainerId != runtimeContainerId)
        {
            return BackpackResult.WrongContainer;
        }
        return BackpackResult.Success;
    }

    private void RaiseChanged()
    {
        if (Changed == null) { return; }
        foreach (Action listener in Changed.GetInvocationList())
        {
            try { listener(); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }
}
