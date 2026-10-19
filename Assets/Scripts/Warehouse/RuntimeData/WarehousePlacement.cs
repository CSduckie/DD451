// 仓库只记录同一权威物品的摆放，归属、保有状态和使用次数仍由01管理。
// 移动时替换记录，避免外部保存的原摆放跟着改变而无法检查或恢复。
public sealed class WarehousePlacement
{
    public ItemInstance Item { get; }
    public string InstanceId { get { return Item != null ? Item.InstanceId : null; } }
    public int X { get; }
    public int Y { get; }
    public bool Rotated { get; }
    public int Width
    {
        get { return Item != null && Item.Definition != null ? (Rotated ? Item.Definition.height : Item.Definition.width) : 0; }
    }
    public int Height
    {
        get { return Item != null && Item.Definition != null ? (Rotated ? Item.Definition.width : Item.Definition.height) : 0; }
    }

    public WarehousePlacement(ItemInstance item, int x, int y, bool rotated)
    {
        Item = item;
        X = x;
        Y = y;
        Rotated = rotated;
    }
}
