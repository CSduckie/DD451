using System;
using System.Collections.Generic;

public enum WarehouseResult
{
    Success,
    NotReady,
    InvalidConfiguration,
    NotFound,
    WrongOwner,
    WrongContainer,
    MissingPlacement,
    DuplicateInstance,
    RotationNotAllowed,
    OutOfBounds,
    Overlap,
    Busy,
    NotInShelter,
    LossRejected
}

// 仓库自己的矩形布局，不调用背包格子，也不使用UI层级作为真实占格。
public sealed class WarehouseGrid
{
    private readonly List<WarehousePlacement> placements = new List<WarehousePlacement>();

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Count { get { return placements.Count; } }

    public WarehouseGrid(int width, int height)
    {
        if (width <= 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
        if (height <= 0) { throw new ArgumentOutOfRangeException(nameof(height)); }
        Width = width;
        Height = height;
    }

    public IReadOnlyList<WarehousePlacement> GetPlacements()
    {
        return Array.AsReadOnly(placements.ToArray());
    }

    public bool TryGetPlacement(string instanceId, out WarehousePlacement placement)
    {
        int index = FindIndex(instanceId);
        placement = index >= 0 ? placements[index] : null;
        return placement != null;
    }

    public WarehouseResult CanPlace(ItemInstance item, int x, int y, bool rotated)
    {
        if (item == null) { return WarehouseResult.NotFound; }
        if (FindIndex(item.InstanceId) >= 0) { return WarehouseResult.DuplicateInstance; }
        return CheckRectangle(item, x, y, rotated, Width, Height, null);
    }

    public WarehouseResult TryAdd(ItemInstance item, int x, int y, bool rotated)
    {
        WarehouseResult result = CanPlace(item, x, y, rotated);
        if (result != WarehouseResult.Success) { return result; }
        placements.Add(new WarehousePlacement(item, x, y, rotated));
        return WarehouseResult.Success;
    }

    public WarehouseResult CanMove(string instanceId, int x, int y, bool rotated)
    {
        if (!TryGetPlacement(instanceId, out WarehousePlacement original))
        {
            return WarehouseResult.MissingPlacement;
        }
        // 原占格一直保留，预览只在这次查询中忽略自己的旧矩形。
        return CheckRectangle(original.Item, x, y, rotated, Width, Height, instanceId);
    }

    public WarehouseResult TryMove(string instanceId, int x, int y, bool rotated)
    {
        WarehouseResult result = CanMove(instanceId, x, y, rotated);
        if (result != WarehouseResult.Success) { return result; }
        int index = FindIndex(instanceId);
        placements[index] = new WarehousePlacement(placements[index].Item, x, y, rotated);
        return WarehouseResult.Success;
    }

    public WarehouseResult TryRemove(string instanceId, out WarehousePlacement removed)
    {
        int index = FindIndex(instanceId);
        removed = index >= 0 ? placements[index] : null;
        if (index < 0) { return WarehouseResult.MissingPlacement; }
        placements.RemoveAt(index);
        return WarehouseResult.Success;
    }

    public WarehouseResult CanExpand(int width, int height)
    {
        if (width <= 0 || height <= 0 || width < Width || height < Height
            || (width == Width && height == Height))
        {
            return WarehouseResult.InvalidConfiguration;
        }
        // 配置在运行中可能被编辑；扩容不能借机掩盖已有非法摆放或删物品。
        foreach (WarehousePlacement placement in placements)
        {
            WarehouseResult result = CheckRectangle(placement.Item, placement.X, placement.Y,
                placement.Rotated, width, height, placement.InstanceId);
            if (result != WarehouseResult.Success) { return result; }
        }
        return WarehouseResult.Success;
    }

    public WarehouseResult TryExpand(int width, int height)
    {
        WarehouseResult result = CanExpand(width, height);
        if (result != WarehouseResult.Success) { return result; }
        Width = width;
        Height = height;
        return WarehouseResult.Success;
    }

    private WarehouseResult CheckRectangle(ItemInstance item, int x, int y, bool rotated,
        int containerWidth, int containerHeight, string ignoredId)
    {
        if (item == null) { return WarehouseResult.NotFound; }
        ItemDataSO definition = item.Definition;
        if (definition == null || definition.width <= 0 || definition.height <= 0)
        {
            return WarehouseResult.InvalidConfiguration;
        }
        if (rotated && !definition.canRotate) { return WarehouseResult.RotationNotAllowed; }
        int width = rotated ? definition.height : definition.width;
        int height = rotated ? definition.width : definition.height;
        // 使用long比较右下边界，极大输入不会因int溢出绕过边界检查。
        if (x < 0 || y < 0 || (long)x + width > containerWidth || (long)y + height > containerHeight)
        {
            return WarehouseResult.OutOfBounds;
        }
        foreach (WarehousePlacement other in placements)
        {
            if (other.InstanceId == ignoredId) { continue; }
            if (other.Width <= 0 || other.Height <= 0)
            {
                return WarehouseResult.InvalidConfiguration;
            }
            if ((long)x < (long)other.X + other.Width && (long)x + width > other.X
                && (long)y < (long)other.Y + other.Height && (long)y + height > other.Y)
            {
                return WarehouseResult.Overlap;
            }
        }
        return WarehouseResult.Success;
    }

    private int FindIndex(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) { return -1; }
        for (int i = 0; i < placements.Count; i++)
        {
            if (placements[i].InstanceId == instanceId) { return i; }
        }
        return -1;
    }
}
