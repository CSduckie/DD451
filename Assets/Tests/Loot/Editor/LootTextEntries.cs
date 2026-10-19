public static class LootTextEntries
{
    public static void Fill(LocalizationTableSO table)
    {
        Add(table, "loot.title", "搜索来源", "Loot source");
        Add(table, "loot.close", "关闭来源", "Close source");
        Add(table, "loot.unknown", "未发现的物品", "Unknown item");
        Add(table, "loot.searching", "正在搜索", "Searching");
        Add(table, "loot.revealed", "已发现", "Revealed");
        Add(table, "loot.taken", "已取走", "Taken");
        Add(table, "loot.empty", "空箱 / 已搜空", "Empty / looted");
        Add(table, "loot.complete", "搜索完成", "Search complete");
        Add(table, "loot.progress", "{0:0.0} / {1:0.0} 秒", "{0:0.0} / {1:0.0} s");
        Add(table, "loot.countdown", "{0:0.0} 秒", "{0:0.0} s");
        Set(table, "loot.wide_hint", "箱格较宽，可用底部横向滚动条查看。", "Use the bottom scrollbar to view this wide container.");
        Set(table, "loot.layout_invalid", "箱子容量或占格配置无效，请检查箱子配置。", "Invalid container size or item layout; check its configuration.");
        Set(table, "loot.drag_hint", "拖动或Ctrl+左键将已发现物品放入背包；R旋转。", "Drag or Ctrl+click a revealed item into your backpack; R rotates.");
        Set(table, "loot.return_hint", "Ctrl+左键快捷转移；拖拽按鼠标格位放回，R旋转。", "Ctrl+click to quick transfer; drag to the target cells, R to rotate.");
        Set(table, "loot.return_ready", "松手放入框选格位，物品保持已发现。", "Release into the outlined cells; the item remains revealed.");
        Add(table, "loot.quick_no_target", "请先打开一个有效箱子或仓库。", "Open a valid box or warehouse first.");
        Add(table, "loot.quick_ambiguous", "同时打开了多个目标，请只保留一个。", "More than one destination is open; keep only one open.");
        Add(table, "loot.quick_hint", "Ctrl+左键快捷转移一件物品；目标已有物品保持原位。", "Ctrl+click to transfer one item; existing items stay in place.");
        Add(table, "loot.return_unavailable", "当前箱子暂时不能接收，物品仍留在背包。", "This box cannot receive the item now; it remains in your backpack.");
        Add(table, "loot.return_full", "箱内空间不足，物品保留在背包。", "Not enough space in this box; the item stays in your backpack.");
        Add(table, "loot.closed", "来源已关闭，未完成的搜索重新计时。", "Source closed; unfinished search will restart.");
        Add(table, "loot.source_invalid", "来源已失效", "Source is no longer valid");
        Add(table, "loot.unreachable", "超出距离或被墙体挡住", "Out of reach or obstructed");
        Add(table, "loot.drop", "丢到地面", "Drop on ground");
        Add(table, "loot.discard", "丢弃选中物品", "Discard selected");
        Add(table, "loot.discard.title", "确认丢弃", "Confirm discard");
        Add(table, "loot.discard.ordinary", "确定丢弃“{0}”？\n普通物品将永久销毁，不会掉到地面，也无法再次拾取。", "Discard “{0}”?\nThis ordinary item will be permanently destroyed. It will not drop on the ground or be available to pick up again.");
        Add(table, "loot.discard.unique", "确定丢弃“{0}”？\n该唯一物品将在下一轮探索时回到原拾取点，本轮无法再次拾取。", "Discard “{0}”?\nThis unique item will return to its original pickup point during the next exploration round. It cannot be picked up again this round.");
        Add(table, "loot.discard.confirm", "确认丢弃", "Confirm discard");
        Add(table, "loot.discard.cancel", "取消", "Cancel");
        Add(table, "loot.discard.cancelled", "已取消丢弃，物品保留。", "Discard cancelled; item kept.");
        Add(table, "loot.discard.invalid", "丢弃确认已失效，请重新选择物品。", "Discard confirmation expired. Select the item again.");
        Add(table, "loot.discard.unavailable", "丢弃确认界面尚未准备好，物品保留。", "Discard confirmation is not ready; item kept.");
        Add(table, "loot.discard.destroyed", "物品已销毁，无法再次拾取。", "Item destroyed; it cannot be picked up again.");
        Add(table, "loot.discard.pending", "唯一物品将在下轮探索回到原拾取点。", "Unique item returns to its original pickup point next exploration round.");
        Add(table, "loot.interact.box", "E 搜索箱子", "E Search box");
        Add(table, "loot.interact.ground", "E 查看地面物品", "E Inspect ground item");
        Add(table, "loot.test.title", "06 真实模块联验｜08流程由测试按钮模拟", "06 integration test | Buttons simulate the future 08 flow");
        Add(table, "loot.test.status", "第{0}轮  HP {1}/{2}  EV {3}  世界时间 {4:0.0}s", "Round {0}  HP {1}/{2}  EV {3}  World time {4:0.0}s");
        Add(table, "loot.test.exploration", "探索：A/D移动，E查看来源，Tab背包，R旋转，F10切语言。", "Explore: A/D move, E inspect, Tab backpack, R rotate, F10 language.");
        Add(table, "loot.test.shelter", "模拟庇护所：背包与仓库可双向拖放；入仓不等于撤离结算。", "Simulated shelter: drag both ways; storage transfer does not retain loot.");
        Add(table, "loot.test.enter_shelter", "模拟庇护所", "Simulate shelter");
        Add(table, "loot.test.enter_exploration", "模拟探索", "Simulate exploration");
        Add(table, "loot.test.next_round", "模拟08：结算后新轮", "Simulate 08: settle + next round");
        Add(table, "loot.test.damage", "真实伤害 1HP", "Real damage: 1 HP");
        Add(table, "loot.test.lethal", "真实致命伤", "Real lethal damage");
        Add(table, "loot.test.scope", "箱子从左至右：100%、0%、独立中间概率、空表、唯一布娃娃。地面有电池与医疗包原点。红区持续造成真实伤害。", "Boxes left to right: 100%, 0%, independent mixed chances, empty, unique doll. Ground battery and medkit origin; red zone deals real damage.");
        Add(table, "loot.test.round_ready", "测试新轮已准备；上一轮地图遗留已明确记损。", "Test round ready; previous world leftovers were explicitly settled.");
        Add(table, "loot.test.box.guaranteed", "100%箱子", "100% box");
        Add(table, "loot.test.box.zero", "0%箱子", "0% box");
        Add(table, "loot.test.box.mixed", "独立25/50/75%", "Independent 25/50/75%");
        Add(table, "loot.test.box.empty", "空表箱子", "Empty table");
        Add(table, "loot.test.box.unique-doll", "唯一布娃娃", "Unique doll");
        Add(table, "transfer.Success", "转移完成", "Transfer complete");
        Add(table, "transfer.NotReady", "模块尚未准备好", "Modules are not ready");
        Add(table, "transfer.Busy", "请先结束当前操作", "Finish the current operation first");
        Set(table, "transfer.StaleRequest", "本次操作已取消或过期", "This operation was cancelled or expired");
        Add(table, "transfer.SourceInvalid", "物品来源已变化", "The item source has changed");
        Add(table, "transfer.SourceHidden", "物品尚未搜索揭示", "The item has not been revealed");
        Add(table, "transfer.NotAllowed", "当前场景或操作状态不允许转移", "Transfer is unavailable in this context");
        Add(table, "transfer.Unreachable", "来源超出范围或被遮挡", "The source is out of reach or obstructed");
        Add(table, "transfer.OutOfBounds", "目标超出容器边界", "The target is outside the container");
        Add(table, "transfer.Occupied", "目标格被占用或空间不足", "The target cells are occupied or there is not enough space");
        Add(table, "transfer.InvalidRotation", "该物品不允许旋转", "This item cannot rotate");
        Add(table, "transfer.InvalidConfiguration", "物品或来源配置无效", "Invalid item or source configuration");
        Add(table, "transfer.TargetRejected", "目标拒绝接收，原物品保留", "Target rejected the item; source preserved");
        Add(table, "transfer.SourceRemovalFailed", "来源提交失败，目标已恢复", "Source commit failed; target restored");
        Add(table, "transfer.OwnerRejected", "归属提交失败，摆放已恢复", "Owner commit failed; placements restored");
        Add(table, "transfer.RollbackFailed", "恢复失败，请检查运行记录", "Rollback failed; inspect runtime records");
        Add(table, "transfer.NoGroundPosition", "附近没有可达的安全落点", "No safe reachable ground position nearby");
        Add(table, "transfer.PresentationFailed", "地面表现准备失败，物品留在背包", "Ground presentation failed; item remains in backpack");
    }

    private static void Add(LocalizationTableSO table, string key, string chinese, string english)
    {
        if (table.entries.Exists(entry => entry.key == key)) { return; }
        table.entries.Add(new LocalizationEntry { key = key, chinese = chinese, english = english });
    }

    private static void Set(LocalizationTableSO table, string key, string chinese, string english)
    {
        // 只替换本模块已经改变含义的提示；其余现有译文继续保留。
        LocalizationEntry entry = table.entries.Find(value => value.key == key);
        if (entry == null) { Add(table, key, chinese, english); return; }
        entry.chinese = chinese;
        entry.english = english;
    }
}
