using System.Collections.Generic;

// 仅供 05 资源搭建工具创建语言表；运行时查询完全交给 03。
public static class WarehouseTextEntries
{
    public static void Fill(LocalizationTableSO table)
    {
        table.entries = new List<LocalizationEntry>
        {
            Entry("warehouse.title", "仓库", "Warehouse"),
            Entry("warehouse.size", "当前容量：{0} × {1}（{2}格）", "Capacity: {0} × {1} ({2} cells)"),
            Entry("warehouse.close", "关闭", "Close"),
            Entry("warehouse.controls", "左键选择 / 拖动整理 / R旋转；滚轮或右侧滚动条上下查看", "Select / drag to rearrange / R to rotate; scroll vertically with the wheel or right scrollbar"),
            Entry("warehouse.select_hint", "选择仓库物品查看信息。", "Select a stored item to inspect it."),
            Entry("warehouse.dimensions", "占格：{0} × {1}", "Footprint: {0} × {1}"),
            Entry("warehouse.value", "保有价值：{0} EV", "Retained value: {0} EV"),
            Entry("warehouse.stored_use_hint", "仓库物品需先转入背包才可使用。", "Move items into the backpack before use."),
            Entry("warehouse.rotatable", "可旋转", "Can rotate"),
            Entry("warehouse.fixed_orientation", "不可旋转", "Cannot rotate"),
            Entry("warehouse.item_size", "{0} × {1}", "{0} × {1}"),
            Entry("warehouse.abandon", "遗弃选中物品", "Abandon selected"),
            Entry("warehouse.cancel_drag", "取消拖拽", "Cancel drag"),
            Entry("warehouse.ready", "可自由整理仓库。", "Ready to rearrange."),
            Entry("warehouse.cancelled", "已取消，原摆放保留。", "Cancelled; original placement kept."),
            Entry("warehouse.moved", "摆放已更新。", "Placement updated."),
            Entry("warehouse.outside", "请在可见仓库格子内松手。", "Release over a visible warehouse cell."),
            Entry("warehouse.confirm_title", "确认遗弃", "Confirm abandonment"),
            Entry("warehouse.confirm_body", "确定遗弃“{0}”？保有价值将失去；唯一剧情物品下轮可在原拾取点找回。", "Abandon “{0}”? Its retained value will be lost. Unique story items can be recovered at their original pickup point next round."),
            Entry("warehouse.confirm", "确认遗弃", "Confirm abandonment"),
            Entry("warehouse.cancel", "取消", "Cancel"),
            Entry("warehouse.abandoned", "物品已遗弃。", "Item abandoned."),
            Entry("warehouse.result.success", "此处可以放置。", "This placement is valid."),
            Entry("warehouse.result.not_ready", "仓库尚未准备好。", "Warehouse is not ready."),
            Entry("warehouse.result.invalid_configuration", "仓库或物品配置无效。", "Invalid warehouse or item configuration."),
            Entry("warehouse.result.not_found", "物品已不存在。", "Item is no longer available."),
            Entry("warehouse.result.wrong_owner", "物品已不在仓库。", "Item is no longer in storage."),
            Entry("warehouse.result.wrong_container", "物品不属于此仓库。", "Item belongs to a different warehouse."),
            Entry("warehouse.result.missing_placement", "仓库中没有该物品摆放。", "Item has no placement in this warehouse."),
            Entry("warehouse.result.duplicate_instance", "该物品已经在仓库中。", "Item is already in this warehouse."),
            Entry("warehouse.result.rotation_not_allowed", "此物品不可旋转。", "This item cannot rotate."),
            Entry("warehouse.result.out_of_bounds", "放置范围超出仓库。", "Placement is outside the warehouse."),
            Entry("warehouse.result.overlap", "此处与其他物品重叠。", "Placement overlaps another item."),
            Entry("warehouse.result.busy", "请先结束拖拽或遗弃确认。", "Finish the current drag or confirmation first."),
            Entry("warehouse.result.not_in_shelter", "当前场景不允许整理仓库。", "Warehouse management is unavailable here."),
            Entry("warehouse.result.loss_rejected", "遗弃未完成，原物品保持不变。", "Abandonment failed; the item was kept.")
        };
    }

    private static LocalizationEntry Entry(string key, string chinese, string english)
    {
        return new LocalizationEntry { key = key, chinese = chinese, english = english };
    }
}
