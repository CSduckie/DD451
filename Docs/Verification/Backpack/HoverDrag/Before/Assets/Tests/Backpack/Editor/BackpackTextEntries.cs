using System.Collections.Generic;

// 创建本模块语言资产时使用；物品名称、描述仍只读取原 ItemDataSO。
public static class BackpackTextEntries
{
    public static void Fill(LocalizationTableSO table)
    {
        table.entries = new List<LocalizationEntry>
        {
            Entry("backpack.title", "随身背包", "BACKPACK"),
            Entry("backpack.close", "关闭 · Tab", "Close · Tab"),
            Entry("backpack.item_size", "{0} × {1}", "{0} × {1}"),
            Entry("backpack.dimensions", "占格：{0} × {1}", "Size: {0} × {1} cells"),
            Entry("backpack.rotatable", "允许旋转：拖动时按 R", "Rotatable: press R while dragging"),
            Entry("backpack.fixed_orientation", "此物品不能旋转", "This item cannot be rotated"),
            Entry("backpack.uses", "本轮已用：{0} / {1}", "Uses this round: {0} / {1}"),
            Entry("backpack.uses_unlimited", "不限制本轮使用次数", "No per-round use limit"),
            Entry("backpack.consumed_on_use", "成功使用后消耗；满血不能使用", "Consumed on successful use; cannot use at full health"),
            Entry("backpack.kept_on_use", "成功使用后保留；满血不能使用", "Kept after successful use; cannot use at full health"),
            Entry("backpack.ready", "背包已打开。请选择或拖动物品。", "Backpack open. Select or drag an item."),
            Entry("backpack.moved", "摆放已更新。", "Item moved."),
            Entry("backpack.cancelled", "已取消预览，原摆放不变。", "Preview cancelled. Original placement kept."),
            Entry("backpack.outside", "不在可放置的格子上；松手取消。", "Outside the grid. Release to cancel."),
            Entry("backpack.external_pending", "仅显示外部预览，物品仍在原来源；真实转移待06接入。", "External preview only; the item stays at its source. Transfer integration is pending."),
            Entry("backpack.dragging_external", "外部物品仅预览：取消不会改变来源；暂不执行真实转移。", "External item preview only. Its source is unchanged; transfer is not connected yet."),
            Entry("backpack.player_unavailable", "玩家尚未准备或已经死亡，当前不能打开或使用。", "The player is not prepared or has died. Opening or using is unavailable."),
            Entry("backpack.drag_active", "请先结束拖拽，再使用物品。", "Finish dragging before using an item."),
            Entry("backpack.result.success", "位置合法，松开左键确认摆放。", "Valid position. Release the left mouse button to place."),
            Entry("backpack.result.not_ready", "背包、物品或语言依赖尚未就绪。", "The backpack, items or localization is not ready."),
            Entry("backpack.result.invalid_configuration", "背包或物品尺寸配置无效。", "The backpack or item size configuration is invalid."),
            Entry("backpack.result.not_found", "未找到当前物品。", "The item no longer exists."),
            Entry("backpack.result.wrong_owner", "物品已不属于背包，操作已取消。", "The item is no longer in the backpack. Operation cancelled."),
            Entry("backpack.result.wrong_container", "物品属于另一个背包，不能在这里操作。", "The item belongs to another backpack."),
            Entry("backpack.result.missing_placement", "物品没有本背包的有效摆放记录。", "The item has no valid placement in this backpack."),
            Entry("backpack.result.duplicate_instance", "同一件物品不能重复放入。", "The same item cannot be added twice."),
            Entry("backpack.result.rotation_not_allowed", "这件物品不允许旋转。", "This item cannot be rotated."),
            Entry("backpack.result.out_of_bounds", "物品超出背包边界；松手取消。", "The item extends beyond the grid. Release to cancel."),
            Entry("backpack.result.overlap", "此处与其他物品重叠；松手取消。", "This position overlaps another item. Release to cancel."),
            Entry("backpack.result.busy", "操作正在进行，请稍后再试。", "An operation is in progress. Try again shortly."),
            Entry("backpack.test.title", "背包联动测试", "BACKPACK INTEGRATION TEST"),
            Entry("backpack.test.instructions", "Tab 背包 · H 受伤 · K 致命伤 · V 外部预览 · Esc 取消 · T 庇护所上下文", "Tab backpack · H damage · K lethal · V external preview · Esc cancel · T shelter context"),
            Entry("backpack.test.status", "世界时间 {0:0.0}s · 生命 {1}/{2} · 轮次 {3}", "World {0:0.0}s · HP {1}/{2} · Round {3}"),
            Entry("backpack.test.sample", "样本共20格：包内14格，世界布娃娃6格。", "Samples need 20 cells: 14 in the backpack, 6 for the doll in the world."),
            Entry("backpack.test.scope", "模拟08准备与伤害；未接入敌人、转移或结算。", "Simulates round preparation and damage. Enemies, transfers and settlement are not connected."),
            Entry("backpack.test.context.exploration", "当前：探索上下文", "Context: exploration"),
            Entry("backpack.test.context.shelter", "当前：庇护所自由整理上下文", "Context: shelter arrangement"),
            Entry("backpack.test.external_preview", "外部布娃娃预览中；其真实来源保持不变。", "Previewing the external doll. Its actual source is unchanged."),
            Entry("backpack.test.external_unavailable", "当前无法开始外部预览。", "External preview is currently unavailable."),
            Entry("backpack.test.damage", "真实非致命伤害已生效。", "Real nonlethal damage was accepted."),
            Entry("backpack.test.damage_rejected", "伤害未被接受（例如无敌期）。", "Damage was rejected (for example, during invulnerability)."),
            Entry("backpack.test.lethal", "真实致命伤害已执行；携带物不由04结算。", "Real lethal damage applied. Backpack losses are not settled by this module."),
            Entry("backpack.test.cancelled", "测试预览已取消。", "Test preview cancelled.")
        };
        ApplySplitLayoutText(table);
    }

    public static void ApplySplitLayoutText(LocalizationTableSO table)
    {
        // 更新本模块操作提示；共享语言资产由整合者统一保存。
        if (table.entries == null) { table.entries = new List<LocalizationEntry>(); }
        LocalizationEntry[] layoutEntries =
        {
            Entry("backpack.controls", "左键拖动 · R旋转 · 右键使用\nCtrl+左键快捷转移 · 受击关包", "Drag · R rotate · Right click: use\nCtrl+Left: transfer · Damage closes"),
            Entry("backpack.select_hint", "选择物品查看详情。\n合法松手才会改变摆放。", "Select an item to inspect it.\nRelease in a valid position to move."),
            Entry("backpack.language_hint", "F10：中文 / English", "F10: Chinese / English"),
            Entry("backpack.loot.title", "搜刮容器", "LOOT CONTAINER"),
            Entry("backpack.loot.disconnected", "尚未连接搜刮来源。", "No loot source connected.")
        };
        foreach (LocalizationEntry layoutEntry in layoutEntries)
        {
            LocalizationEntry existing = table.entries.Find(entry => entry != null && entry.key == layoutEntry.key);
            if (existing == null)
            {
                table.entries.Add(layoutEntry);
            }
            else if (layoutEntry.key == "backpack.controls" || layoutEntry.key == "backpack.select_hint")
            {
                existing.chinese = layoutEntry.chinese;
                existing.english = layoutEntry.english;
            }
        }
    }

    private static LocalizationEntry Entry(string key, string chinese, string english)
    {
        return new LocalizationEntry { key = key, chinese = chinese, english = english };
    }
}
