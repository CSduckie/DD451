// 只保存同一物品的引用与摆放；身份、归属、次数始终读取01的ItemInstance。
// 不提供setter，旧快照不会因为下一次移动而被悄悄改写。
public sealed class BackpackPlacement
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

    public BackpackPlacement(ItemInstance item, int x, int y, bool rotated)
    {
        Item = item;
        X = x;
        Y = y;
        Rotated = rotated;
    }
}
