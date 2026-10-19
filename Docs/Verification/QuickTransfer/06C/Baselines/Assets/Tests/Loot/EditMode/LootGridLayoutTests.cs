using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class LootGridLayoutTests
{
    private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
    private ItemManager items;
    private LootRuntimeManager runtime;
    private string mapId;

    [SetUp]
    public void SetUp()
    {
        mapId = "loot-grid-test-" + Guid.NewGuid().ToString("N");
        GameObject root = Object("LootGridTestRuntime");
        items = root.AddComponent<ItemManager>();
        runtime = root.AddComponent<LootRuntimeManager>();
        runtime.itemManager = items;
        Assert.That(items.BeginRound(1), Is.True);
        runtime.SetExplorationContext(mapId, true);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null) { UnityEngine.Object.DestroyImmediate(created[i]); }
        }
        created.Clear();
    }

    [Test]
    public void GeneratedFootprintsUseStableRowOrderWithinTheFullConfiguredGrid()
    {
        LootBox box = MixedBox();
        box.lootTable.entries.Add(new LootTableEntry { entryId = "not-generated",
            item = Definition("not-generated", 9, 9), useDefaultProbability = false, overrideProbability = 0f });
        LootSourceState source = Prepare(box);
        int notifications = 0;
        runtime.SourceChanged += changed => notifications++;

        Assert.That(source.EnsureLayout(items), Is.True);
        Assert.That(source.GridWidth, Is.EqualTo(6));
        Assert.That(source.GridHeight, Is.EqualTo(4));
        Assert.That(source.LayoutColumns, Is.EqualTo(6));
        Assert.That(source.LayoutRows, Is.EqualTo(4), "空格背景必须包含未被物品占用的最后一行。");
        Assert.That(source.Entries.Count, Is.EqualTo(4));
        Assert.That(Rectangle(source.Entries[0]), Is.EqualTo(new RectInt(0, 0, 1, 1)));
        Assert.That(Rectangle(source.Entries[1]), Is.EqualTo(new RectInt(1, 0, 1, 2)));
        Assert.That(Rectangle(source.Entries[2]), Is.EqualTo(new RectInt(2, 0, 2, 2)));
        Assert.That(Rectangle(source.Entries[3]), Is.EqualTo(new RectInt(4, 0, 2, 3)));
        foreach (LootEntryState entry in source.Entries)
        {
            Assert.That(entry.Revealed, Is.False);
            Assert.That(entry.Taken, Is.False);
            Assert.That(items.TryGetInstance(entry.InstanceId, out ItemInstance item), Is.True);
            Assert.That(item.Owner, Is.EqualTo(ItemOwner.Container));
            Assert.That(item.ContainerId, Is.EqualTo(source.SourceId));
        }
        Assert.That(source.SearchEntryId, Is.Empty);
        Assert.That(source.SearchElapsed, Is.Zero);
        Assert.That(items.InstanceCount, Is.EqualTo(4));
        Assert.That(notifications, Is.Zero);
        AssertNoOverlap(source);
    }

    [Test]
    public void TakenSlotCanBeReusedAndOriginalReturnMovesOnlyTheReturningItem()
    {
        LootBox box = MixedBox();
        LootSourceState source = Prepare(box);
        List<RectInt> original = Snapshot(source);
        Assert.That(source.AdvanceSearch(1.5f, items), Is.True);
        ItemInstance picked = Take(source, "entry-0");
        Assert.That(Rectangle(source.Entries[0]), Is.EqualTo(original[0]), "Taken保留历史坐标，但不再占用格子。");
        LootEntryState added = ReturnNewItem(source, Definition("foreign", 1, 1));
        Assert.That(Rectangle(added), Is.EqualTo(new RectInt(0, 0, 1, 1)));
        AssertOriginalLayout(source, original);
        Assert.That(runtime.BindBox(box, out string error), Is.True, error);
        Assert.That(box.State, Is.SameAs(source));

        int notifications = 0;
        runtime.SourceChanged += changed => notifications++;
        Assert.That(runtime.CanAcceptReturnedItem(source, picked), Is.True);
        AssertOriginalLayout(source, original);
        Assert.That(source.Entries[0].Taken, Is.True, "预检不能提前把物品放回。");
        Assert.That(runtime.TryAddReturnedItem(source, picked, out LootEntryState returned, out bool reused), Is.True);
        Assert.That(reused, Is.True);
        Assert.That(returned, Is.SameAs(source.Entries[0]));
        Assert.That(items.TrySetOwner(picked.InstanceId, ItemOwner.Container, source.SourceId), Is.True);
        Assert.That(source.EnsureLayout(items), Is.True);
        Assert.That(Rectangle(returned), Is.EqualTo(new RectInt(0, 1, 1, 1)));
        for (int i = 1; i < original.Count; i++) { Assert.That(Rectangle(source.Entries[i]), Is.EqualTo(original[i])); }
        Assert.That(Rectangle(added), Is.EqualTo(original[0]));
        Assert.That(source.Entries.Count, Is.EqualTo(5));
        Assert.That(source.SearchEntryId, Is.EqualTo("entry-1"));
        Assert.That(source.SearchElapsed, Is.EqualTo(1.5f));
        Assert.That(notifications, Is.Zero);
        AssertNoOverlap(source);
    }

    [Test]
    public void ReturningOriginalItemPrefersItsOldSlotEvenWhenAnEarlierHoleIsFree()
    {
        LootBox box = Box("prefer-original", Definition("first", 1, 1, 0f), Definition("second", 1, 1, 0f));
        box.gridWidth = 3;
        box.gridHeight = 1;
        LootSourceState source = Prepare(box);
        Assert.That(source.AdvanceSearch(0f, items), Is.True);
        Take(source, "entry-0");
        ItemInstance second = Take(source, "entry-1");
        Assert.That(runtime.TryAddReturnedItem(source, second, out LootEntryState returned, out bool reused), Is.True);
        Assert.That(reused, Is.True);
        Assert.That(Rectangle(returned), Is.EqualTo(new RectInt(1, 0, 1, 1)));
        Assert.That(items.TrySetOwner(second.InstanceId, ItemOwner.Container, source.SourceId), Is.True);
        Assert.That(source.Entries[0].Taken, Is.True);
        AssertNoOverlap(source);
    }

    [Test]
    public void DifferentBoxesSnapshotTheirOwnSizesAndOnlyTheNextRoundUsesEditedSizes()
    {
        LootBox first = Box("first", Definition("first-item", 1, 1));
        LootBox second = Box("second", Definition("second-item", 1, 1));
        first.gridWidth = 2;
        first.gridHeight = 2;
        second.gridWidth = 4;
        second.gridHeight = 1;
        LootSourceState original = Prepare(first);
        Assert.That(second.State.GridWidth, Is.EqualTo(4));
        Assert.That(second.State.GridHeight, Is.EqualTo(1));
        first.gridWidth = 1;
        first.gridHeight = 3;
        second.gridWidth = 3;
        second.gridHeight = 2;
        Assert.That(runtime.BindBox(first, out string error), Is.True, error);
        Assert.That(runtime.BindBox(second, out error), Is.True, error);
        Assert.That(first.State, Is.SameAs(original));
        Assert.That(first.State.GridWidth, Is.EqualTo(2));
        Assert.That(first.State.GridHeight, Is.EqualTo(2));
        Assert.That(second.State.GridWidth, Is.EqualTo(4));
        Assert.That(second.State.GridHeight, Is.EqualTo(1));
        foreach (ItemInstance item in runtime.GetRemainingWorldItems())
        {
            Assert.That(items.RecordLoss(item.InstanceId, ItemLossReason.LeftBehindOnExtraction), Is.True);
        }
        Assert.That(items.BeginRound(2), Is.True);
        Assert.That(runtime.PrepareRound(2, out error), Is.True, error);
        Assert.That(first.State, Is.Not.SameAs(original));
        Assert.That(first.State.GridWidth, Is.EqualTo(1));
        Assert.That(first.State.GridHeight, Is.EqualTo(3));
        Assert.That(second.State.GridWidth, Is.EqualTo(3));
        Assert.That(second.State.GridHeight, Is.EqualTo(2));
        Assert.That(first.State.LayoutRows, Is.EqualTo(3));
    }

    [Test]
    public void InterruptedSearchAndSameRoundRebindingKeepPositionsAndRevealedEntries()
    {
        LootBox box = MixedBox();
        LootSourceState source = Prepare(box);
        List<RectInt> original = Snapshot(source);
        Assert.That(source.AdvanceSearch(2f, items), Is.True);
        Assert.That(source.SearchEntryId, Is.EqualTo("entry-1"));
        Assert.That(source.SearchElapsed, Is.EqualTo(2f));
        source.InterruptSearch();
        Assert.That(source.Entries[0].Revealed, Is.True);
        Assert.That(source.Entries[1].Revealed, Is.False);
        Assert.That(source.SearchEntryId, Is.Empty);
        Assert.That(source.SearchElapsed, Is.Zero);
        Assert.That(runtime.PrepareRound(1, out string error), Is.True, error);
        Assert.That(runtime.BindBox(box, out error), Is.True, error);
        Assert.That(box.State, Is.SameAs(source));
        Assert.That(source.EnsureLayout(items), Is.True);
        Assert.That(source.AdvanceSearch(3.5f, items), Is.False);
        Assert.That(source.Entries[1].Revealed, Is.False, "当前未完成项重开后须重新经过完整4秒。");
        Assert.That(source.AdvanceSearch(0.5f, items), Is.True);
        Assert.That(source.Entries[1].Revealed, Is.True);
        Assert.That(source.SearchEntryId, Is.EqualTo("entry-2"));
        Assert.That(source.SearchElapsed, Is.Zero);
        AssertOriginalLayout(source, original);
        Assert.That(items.InstanceCount, Is.EqualTo(4));
    }

    [Test]
    public void ZeroSecondEntriesAndLongFrameRevealInOrderOnlyOnceAndKeepTheirLayout()
    {
        LootBox box = Box("search", Definition("zero-a", 1, 1, 0f), Definition("zero-b", 1, 2, 0f),
            Definition("two-seconds", 2, 2, 2f), Definition("three-seconds", 2, 3, 3f), Definition("zero-c", 1, 1, 0f));
        LootSourceState source = Prepare(box);
        List<RectInt> original = Snapshot(source);
        Assert.That(source.AdvanceSearch(0f, items), Is.True);
        Assert.That(source.Entries[0].Revealed, Is.True);
        Assert.That(source.Entries[1].Revealed, Is.True);
        Assert.That(source.Entries[2].Revealed, Is.False);
        Assert.That(source.SearchEntryId, Is.EqualTo("entry-2"));
        Assert.That(source.SearchElapsed, Is.Zero);
        Assert.That(source.AdvanceSearch(3.25f, items), Is.True);
        Assert.That(source.Entries[2].Revealed, Is.True);
        Assert.That(source.Entries[3].Revealed, Is.False);
        Assert.That(source.Entries[4].Revealed, Is.False);
        Assert.That(source.SearchEntryId, Is.EqualTo("entry-3"));
        Assert.That(source.SearchElapsed, Is.EqualTo(1.25f));
        Assert.That(source.AdvanceSearch(1000f, items), Is.True);
        foreach (LootEntryState entry in source.Entries) { Assert.That(entry.Revealed, Is.True); }
        Assert.That(source.GetNextUnrevealed(), Is.Null);
        Assert.That(source.SearchEntryId, Is.Empty);
        Assert.That(source.SearchElapsed, Is.Zero);
        Assert.That(source.AdvanceSearch(1000f, items), Is.False);
        Assert.That(source.EnsureLayout(items), Is.True);
        AssertOriginalLayout(source, original);
        Assert.That(items.InstanceCount, Is.EqualTo(5));
    }

    [Test]
    public void SkippingFirstCandidateKeepsTheFullPlanAndLeavesItsHoleAvailableForReturns()
    {
        LootBox box = Box("subset-layout", Definition("a", 1, 1), Definition("b", 1, 1),
            Definition("c", 1, 3), Definition("d", 1, 1), Definition("e", 2, 1));
        box.gridWidth = 3;
        box.gridHeight = 3;
        box.lootTable.entries[0].overrideProbability = 0.5f;
        UnityEngine.Random.State previousRandom = UnityEngine.Random.state;
        try
        {
            // 使用Unity真实随机入口寻找首项未命中的seed，不替换生成服务。
            int skipFirstSeed = -1;
            for (int seed = 0; seed < 1000; seed++)
            {
                UnityEngine.Random.InitState(seed);
                if (UnityEngine.Random.value >= 0.5f) { skipFirstSeed = seed; break; }
            }
            Assert.That(skipFirstSeed, Is.GreaterThanOrEqualTo(0));
            UnityEngine.Random.InitState(skipFirstSeed);
            LootSourceState source = Prepare(box);
            Assert.That(source.Entries.Count, Is.EqualTo(4));
            Assert.That(source.TryGetEntry("entry-0", out _), Is.False);
            Assert.That(source.Entries[0].EntryId, Is.EqualTo("entry-1"));
            Assert.That(Rectangle(source.Entries[0]), Is.EqualTo(new RectInt(1, 0, 1, 1)));
            Assert.That(Rectangle(source.Entries[1]), Is.EqualTo(new RectInt(2, 0, 1, 3)));
            Assert.That(Rectangle(source.Entries[2]), Is.EqualTo(new RectInt(0, 1, 1, 1)));
            Assert.That(Rectangle(source.Entries[3]), Is.EqualTo(new RectInt(0, 2, 2, 1)),
                "跳过A后不能重排B/C/D，否则会把必出的E挤出3×3箱子。");
            Assert.That(items.InstanceCount, Is.EqualTo(4));
            List<RectInt> original = Snapshot(source);
            LootEntryState returned = ReturnNewItem(source, Definition("fills-missed-a", 1, 1));
            Assert.That(Rectangle(returned), Is.EqualTo(new RectInt(0, 0, 1, 1)), "未抽中项不留下占用记录。");
            Assert.That(source.Entries.Count, Is.EqualTo(5));
            AssertOriginalLayout(source, original);
            box.lootTable.entries[0].overrideProbability = 1f;
            Assert.That(runtime.BindBox(box, out string error), Is.True, error);
            Assert.That(box.State, Is.SameAs(source));
            Assert.That(source.TryGetEntry("entry-0", out _), Is.False);
            Assert.That(source.Entries.Count, Is.EqualTo(5));
            Assert.That(items.InstanceCount, Is.EqualTo(5));
            AssertOriginalLayout(source, original);
            AssertNoOverlap(source);
        }
        finally { UnityEngine.Random.state = previousRandom; }
    }

    [TestCase(3, 1, 2, 2)]
    [TestCase(1, 3, 2, 2)]
    [TestCase(2, 1, 2, 1)]
    [TestCase(1, 2, 2, 2)]
    public void EveryPositiveChanceCandidateMustFitBeforeAnySceneSourceRollsOrCreates(
        int itemWidth, int itemHeight, int gridWidth, int gridHeight)
    {
        LootBox valid = Box("valid-first", Definition("valid-item", 1, 1));
        LootBox invalid = Box("invalid", Definition("large", itemWidth, itemHeight),
            Definition("other", itemWidth == 1 && itemHeight == 2 ? 2 : 1, 1));
        invalid.gridWidth = gridWidth;
        invalid.gridHeight = gridHeight;
        foreach (LootTableEntry entry in invalid.lootTable.entries) { entry.overrideProbability = 0.0001f; }
        UnityEngine.Random.State randomBefore = UnityEngine.Random.state;
        Assert.That(runtime.PrepareRound(1, out string error), Is.False);
        Assert.That(error, Does.Contain(invalid.SourceKey));
        Assert.That(error, Does.Contain("entry-"));
        Assert.That(items.InstanceCount, Is.Zero);
        Assert.That(runtime.PreparedRound, Is.Zero);
        Assert.That(valid.State, Is.Null);
        Assert.That(invalid.State, Is.Null);
        Assert.That(UnityEngine.Random.state, Is.EqualTo(randomBefore), "配置容量失败必须发生在独立概率抽样前。");
    }

    [TestCase(0, 2)]
    [TestCase(2, 0)]
    [TestCase(-1, 2)]
    public void InvalidGridSizeFailsBeforeAnySourceGenerates(int width, int height)
    {
        LootBox box = Box("invalid-size", Definition("item", 1, 1));
        box.gridWidth = width;
        box.gridHeight = height;
        Assert.That(runtime.PrepareRound(1, out string error), Is.False);
        Assert.That(error, Does.Contain(box.SourceKey));
        Assert.That(items.InstanceCount, Is.Zero);
        Assert.That(runtime.PreparedRound, Is.Zero);
        Assert.That(box.State, Is.Null);
    }

    [Test]
    public void ZeroChanceUniqueRecoveryCannotBeClaimedIntoAnUndersizedBox()
    {
        ItemDataSO definition = Definition("recover", 2, 1);
        definition.isUnique = true;
        definition.uniqueId = mapId + "-unique";
        LootBox box = Box("origin", definition);
        box.gridWidth = 1;
        box.gridHeight = 1;
        box.lootTable.entries[0].overrideProbability = 0f;
        string origin = box.SourceKey + "/entry-0";
        Assert.That(items.TryCreateInstance(definition, ItemOwner.Backpack, "pack", box.SourceKey, origin, out ItemInstance unique), Is.True);
        Assert.That(items.RecordLoss(unique.InstanceId, ItemLossReason.ExplorationDeath), Is.True);
        Assert.That(items.BeginRound(2), Is.True);
        Assert.That(runtime.PrepareRound(2, out string error), Is.False);
        Assert.That(error, Does.Contain(box.SourceKey));
        Assert.That(unique.Owner, Is.EqualTo(ItemOwner.PendingRecovery));
        Assert.That(unique.SpawnClaimed, Is.False);
        Assert.That(items.InstanceCount, Is.EqualTo(1));
        box.gridWidth = 2;
        Assert.That(runtime.PrepareRound(2, out error), Is.True, error);
        Assert.That(box.State.Entries.Count, Is.EqualTo(1));
        Assert.That(box.State.Entries[0].InstanceId, Is.EqualTo(unique.InstanceId));
        Assert.That(unique.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(unique.ContainerId, Is.EqualTo(box.SourceKey));
        Assert.That(unique.OriginalSpawnPointId, Is.EqualTo(origin));
        Assert.That(unique.SpawnClaimed, Is.True);
        Assert.That(items.InstanceCount, Is.EqualTo(1));
    }

    [Test]
    public void FailedCreationKeepsItsRollAndRechecksSavedCandidatesBeforeRetrying()
    {
        LootBox box = Box("saved-roll", Definition("first", 1, 1), Definition("second", 1, 1));
        box.gridWidth = 2;
        box.gridHeight = 1;
        ItemDataSO medicine = Definition("busy-medicine", 1, 1);
        medicine.canUse = true;
        HealEffectSO effect = ScriptableObject.CreateInstance<HealEffectSO>();
        created.Add(effect);
        medicine.effects.Add(effect);
        Assert.That(items.TryCreateInstance(medicine, ItemOwner.Backpack, "pack", "test", "", out ItemInstance carried), Is.True);
        bool attempted = false;
        CallbackHealth health = new CallbackHealth(() =>
        {
            Assert.That(runtime.PrepareRound(1, out _), Is.False, "真实01使用效果期间拒绝创建，保留已抽结果。");
            attempted = true;
        });
        Assert.That(items.TryUseItem(carried.InstanceId, health), Is.EqualTo(ItemUseResult.Success));
        Assert.That(attempted, Is.True);
        LootSourceState saved = box.State;
        Assert.That(saved.Generated, Is.True);
        Assert.That(saved.Entries.Count, Is.EqualTo(2));
        Assert.That(items.InstanceCount, Is.EqualTo(1));
        foreach (LootTableEntry entry in box.lootTable.entries) { entry.overrideProbability = 0f; }
        box.lootTable.entries[1].item.width = 3;
        Assert.That(runtime.BindBox(box, out string error), Is.False);
        Assert.That(error, Does.Contain("本轮保留"));
        Assert.That(items.InstanceCount, Is.EqualTo(1));
        Assert.That(box.State, Is.SameAs(saved));
        Assert.That(saved.Entries.Count, Is.EqualTo(2));
        box.lootTable.entries[1].item.width = 1;
        Assert.That(runtime.BindBox(box, out error), Is.True, error);
        Assert.That(saved.Entries.Count, Is.EqualTo(2), "重试不能按改为0%的配置再次抽样。");
        Assert.That(items.InstanceCount, Is.EqualTo(3));
        AssertNoOverlap(saved);
    }

    [TestCase(3, 1)]
    [TestCase(1, 3)]
    public void OverwideOrOvertallReturnsAreRejectedWithoutGrowingAnEmptyBox(int width, int height)
    {
        LootBox box = Box("empty-return");
        box.gridWidth = 2;
        box.gridHeight = 2;
        LootSourceState source = Prepare(box);
        Assert.That(items.TryCreateInstance(Definition("oversized", width, height), ItemOwner.Backpack,
            "pack", "elsewhere", "", out ItemInstance item), Is.True);
        int notifications = 0;
        runtime.SourceChanged += changed => notifications++;
        Assert.That(runtime.CanAcceptReturnedItem(source, item), Is.False);
        Assert.That(runtime.TryAddReturnedItem(source, item, out _, out _), Is.False);
        Assert.That(source.Entries, Is.Empty);
        Assert.That(source.GridWidth, Is.EqualTo(2));
        Assert.That(source.GridHeight, Is.EqualTo(2));
        Assert.That(source.LayoutColumns, Is.EqualTo(2));
        Assert.That(source.LayoutRows, Is.EqualTo(2));
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(item.ContainerId, Is.EqualTo("pack"));
        Assert.That(notifications, Is.Zero);
    }

    [Test]
    public void EmptyRollKeepsAllConfiguredBackgroundCellsAndStaleRoundCannotRelayout()
    {
        LootBox box = Box("empty", Definition("miss", 9, 9));
        box.gridWidth = 3;
        box.gridHeight = 2;
        box.lootTable.entries[0].overrideProbability = 0f;
        LootSourceState source = Prepare(box);
        Assert.That(source.EnsureLayout(items), Is.True);
        Assert.That(source.LayoutColumns, Is.EqualTo(3));
        Assert.That(source.LayoutRows, Is.EqualTo(2));
        Assert.That(source.Entries, Is.Empty);
        Assert.That(source.AdvanceSearch(1000f, items), Is.False);
        Assert.That(items.InstanceCount, Is.Zero);
        Assert.That(items.BeginRound(2), Is.True);
        Assert.That(source.EnsureLayout(items), Is.False);
        Assert.That(source.LayoutColumns, Is.EqualTo(3));
        Assert.That(source.LayoutRows, Is.EqualTo(2));
    }

    [Test]
    public void NativeGroundUsesItsActualFootprintWithoutBoxCapacityOrReturnSupport()
    {
        GameObject groundObject = Object("NativeGround");
        groundObject.SetActive(false);
        GroundItem ground = groundObject.AddComponent<GroundItem>();
        ground.mapId = mapId;
        ground.sourceId = "ground";
        ground.initialItem = Definition("ground-item", 7, 2);
        ground.runtimeManager = runtime;
        Assert.That(runtime.PrepareRound(1, out string error), Is.True, error);
        Assert.That(ground.State.EnsureLayout(items), Is.True);
        Assert.That(ground.State.GridWidth, Is.Zero);
        Assert.That(ground.State.GridHeight, Is.Zero);
        Assert.That(ground.State.LayoutColumns, Is.EqualTo(7));
        Assert.That(ground.State.LayoutRows, Is.EqualTo(2));
        Assert.That(ground.State.Entries[0].Revealed, Is.True);
        Assert.That(items.TryGetInstance(ground.State.Entries[0].InstanceId, out ItemInstance item), Is.True);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.World));
        Assert.That(runtime.CanAcceptReturnedItem(ground.State, item), Is.False);
    }

    private ItemInstance Take(LootSourceState source, string entryId)
    {
        Assert.That(source.TryGetEntry(entryId, out LootEntryState entry), Is.True);
        Assert.That(runtime.TryMarkTaken(source.SourceId, entryId), Is.True);
        Assert.That(items.TryGetInstance(entry.InstanceId, out ItemInstance item), Is.True);
        Assert.That(items.TrySetOwner(item.InstanceId, ItemOwner.Backpack, "pack"), Is.True);
        return item;
    }

    private LootEntryState ReturnNewItem(LootSourceState source, ItemDataSO definition)
    {
        Assert.That(items.TryCreateInstance(definition, ItemOwner.Backpack, "pack", "elsewhere", "", out ItemInstance item), Is.True);
        Assert.That(runtime.CanAcceptReturnedItem(source, item), Is.True);
        Assert.That(runtime.TryAddReturnedItem(source, item, out LootEntryState entry, out bool reused), Is.True);
        Assert.That(reused, Is.False);
        Assert.That(items.TrySetOwner(item.InstanceId, ItemOwner.Container, source.SourceId), Is.True);
        Assert.That(entry.EntryId, Is.EqualTo("returned-" + item.InstanceId));
        Assert.That(entry.Revealed, Is.True);
        return entry;
    }

    private LootBox MixedBox()
    {
        return Box("mixed", Definition("one", 1, 1, 0f), Definition("two", 1, 2, 4f),
            Definition("four", 2, 2, 3f), Definition("six", 2, 3, 2f));
    }

    private LootSourceState Prepare(LootBox box)
    {
        Assert.That(runtime.PrepareRound(1, out string error), Is.True, error);
        Assert.That(box.State, Is.Not.Null);
        return box.State;
    }

    private LootBox Box(string sourceId, params ItemDataSO[] definitions)
    {
        LootTableSO table = ScriptableObject.CreateInstance<LootTableSO>();
        created.Add(table);
        for (int i = 0; i < definitions.Length; i++)
        {
            table.entries.Add(new LootTableEntry { entryId = "entry-" + i, item = definitions[i],
                useDefaultProbability = false, overrideProbability = 1f });
        }
        GameObject gameObject = Object("LootGridTestBox-" + sourceId);
        gameObject.SetActive(false);
        LootBox box = gameObject.AddComponent<LootBox>();
        box.mapId = mapId;
        box.sourceId = sourceId;
        box.lootTable = table;
        box.runtimeManager = runtime;
        box.gridWidth = 6;
        box.gridHeight = 4;
        return box;
    }

    private ItemDataSO Definition(string id, int width, int height, float duration = 1f)
    {
        ItemDataSO definition = ScriptableObject.CreateInstance<ItemDataSO>();
        created.Add(definition);
        definition.definitionId = mapId + "-" + id;
        definition.nameZh = "测试";
        definition.nameEn = "Test";
        definition.descriptionZh = "说明";
        definition.descriptionEn = "Description";
        definition.nameKey = definition.definitionId + ".name";
        definition.descriptionKey = definition.definitionId + ".description";
        definition.width = width;
        definition.height = height;
        definition.searchSeconds = duration;
        return definition;
    }

    private GameObject Object(string name)
    {
        GameObject instance = new GameObject(name);
        created.Add(instance);
        return instance;
    }

    private static RectInt Rectangle(LootEntryState entry)
    {
        Assert.That(entry.HasLayout, Is.True);
        return new RectInt(entry.LayoutX, entry.LayoutY, entry.LayoutWidth, entry.LayoutHeight);
    }

    private static List<RectInt> Snapshot(LootSourceState source)
    {
        List<RectInt> result = new List<RectInt>();
        foreach (LootEntryState entry in source.Entries) { result.Add(Rectangle(entry)); }
        return result;
    }

    private static void AssertOriginalLayout(LootSourceState source, List<RectInt> original)
    {
        for (int i = 0; i < original.Count; i++)
        {
            Assert.That(Rectangle(source.Entries[i]), Is.EqualTo(original[i]), source.Entries[i].EntryId);
        }
    }

    private static void AssertNoOverlap(LootSourceState source)
    {
        for (int i = 0; i < source.Entries.Count; i++)
        {
            if (source.Entries[i].Taken) { continue; }
            RectInt current = Rectangle(source.Entries[i]);
            Assert.That(current.xMin, Is.GreaterThanOrEqualTo(0));
            Assert.That(current.yMin, Is.GreaterThanOrEqualTo(0));
            Assert.That(current.xMax, Is.LessThanOrEqualTo(source.LayoutColumns));
            Assert.That(current.yMax, Is.LessThanOrEqualTo(source.LayoutRows));
            for (int j = i + 1; j < source.Entries.Count; j++)
            {
                if (source.Entries[j].Taken) { continue; }
                Assert.That(current.Overlaps(Rectangle(source.Entries[j])), Is.False,
                    source.Entries[i].EntryId + " / " + source.Entries[j].EntryId);
            }
        }
    }

    private sealed class CallbackHealth : IHealthTarget
    {
        private readonly Action onHeal;
        public int CurrentHealth { get; private set; } = 1;
        public int MaxHealth { get { return 3; } }
        public CallbackHealth(Action callback) { onHeal = callback; }
        public void Heal(int amount)
        {
            CurrentHealth = Math.Min(MaxHealth, CurrentHealth + amount);
            onHeal();
        }
    }
}
