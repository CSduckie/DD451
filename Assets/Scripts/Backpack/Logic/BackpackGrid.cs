using System;
using System.Collections.Generic;

public enum BackpackResult
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
    Busy
}

// 背包独立的矩形检查。4×4只需遍历摆放，不使用UI位置或共享仓库算法。
public sealed class BackpackGrid
{
    private readonly List<BackpackPlacement> placements = new List<BackpackPlacement>();

    public int Width { get; }
    public int Height { get; }
    public int Count { get { return placements.Count; } }

    public BackpackGrid(int width, int height)
    {
        if (width <= 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
        if (height <= 0) { throw new ArgumentOutOfRangeException(nameof(height)); }
        Width = width;
        Height = height;
    }

    public IReadOnlyList<BackpackPlacement> GetPlacements()
    {
        return Array.AsReadOnly(placements.ToArray());
    }

    public bool TryGetPlacement(string instanceId, out BackpackPlacement placement)
    {
        int index = FindIndex(instanceId);
        placement = index >= 0 ? placements[index] : null;
        return placement != null;
    }

    public BackpackResult CanPlace(ItemInstance item, int x, int y, bool rotated)
    {
        if (item == null) { return BackpackResult.NotFound; }
        if (FindIndex(item.InstanceId) >= 0) { return BackpackResult.DuplicateInstance; }
        return ValidateRectangle(item, x, y, rotated, null);
    }

    public BackpackResult TryAdd(ItemInstance item, int x, int y, bool rotated)
    {
        BackpackResult result = CanPlace(item, x, y, rotated);
        if (result != BackpackResult.Success) { return result; }
        placements.Add(new BackpackPlacement(item, x, y, rotated));
        return BackpackResult.Success;
    }

    public BackpackResult CanMove(string instanceId, int x, int y, bool rotated)
    {
        if (!TryGetPlacement(instanceId, out BackpackPlacement original))
        {
            return BackpackResult.MissingPlacement;
        }
        // 拖拽不提前移走旧摆放；仅这一次校验忽略自己的原占格。
        return ValidateRectangle(original.Item, x, y, rotated, instanceId);
    }

    public BackpackResult TryMove(string instanceId, int x, int y, bool rotated)
    {
        BackpackResult result = CanMove(instanceId, x, y, rotated);
        if (result != BackpackResult.Success) { return result; }
        int index = FindIndex(instanceId);
        BackpackPlacement original = placements[index];
        // 全部检查通过才替换一条记录；失败不需要恢复数据。
        placements[index] = new BackpackPlacement(original.Item, x, y, rotated);
        return BackpackResult.Success;
    }

    public BackpackResult TryRemove(string instanceId, out BackpackPlacement removed)
    {
        int index = FindIndex(instanceId);
        removed = index >= 0 ? placements[index] : null;
        if (index < 0) { return BackpackResult.MissingPlacement; }
        placements.RemoveAt(index);
        return BackpackResult.Success;
    }

    private BackpackResult ValidateRectangle(ItemInstance item, int x, int y, bool rotated, string ignoredId)
    {
        if (item == null) { return BackpackResult.NotFound; }
        ItemDataSO definition = item.Definition;
        if (definition == null || definition.width <= 0 || definition.height <= 0)
        {
            return BackpackResult.InvalidConfiguration;
        }
        if (rotated && !definition.canRotate) { return BackpackResult.RotationNotAllowed; }
        int width = rotated ? definition.height : definition.width;
        int height = rotated ? definition.width : definition.height;
        // 先比较尺寸，再做减法，极大坐标也不会因int相加溢出变成合法。
        if (x < 0 || y < 0 || width > Width || height > Height || x > Width - width || y > Height - height)
        {
            return BackpackResult.OutOfBounds;
        }
        foreach (BackpackPlacement other in placements)
        {
            if (other.InstanceId == ignoredId) { continue; }
            if (other.Width <= 0 || other.Height <= 0)
            {
                return BackpackResult.InvalidConfiguration;
            }
            if ((long)x < (long)other.X + other.Width && (long)x + width > other.X
                && (long)y < (long)other.Y + other.Height && (long)y + height > other.Y)
            {
                return BackpackResult.Overlap;
            }
        }
        return BackpackResult.Success;
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
