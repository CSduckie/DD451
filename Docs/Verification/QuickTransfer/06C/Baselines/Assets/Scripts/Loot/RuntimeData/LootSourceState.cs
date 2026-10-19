using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class LootEntryState
{
    public string EntryId { get; private set; }
    public string InstanceId { get; internal set; }
    public bool Revealed { get; internal set; }
    public bool Taken { get; internal set; }
    public bool HasLayout { get; private set; }
    public int LayoutX { get; private set; }
    public int LayoutY { get; private set; }
    public int LayoutWidth { get; private set; }
    public int LayoutHeight { get; private set; }
    internal ItemDataSO Candidate { get; private set; }
    private bool returnLayoutSaved;
    private bool previousHasLayout;
    private bool previousRevealed;
    private RectInt previousLayout;

    public LootEntryState(string entryId, string instanceId, bool revealed = false)
    {
        EntryId = entryId;
        InstanceId = instanceId;
        Revealed = revealed;
    }

    internal LootEntryState(LootTableEntry entry, bool revealed)
        : this(entry.entryId, string.Empty, revealed)
    {
        Candidate = entry.item;
    }

    internal void SetLayout(RectInt rectangle)
    {
        LayoutX = rectangle.x;
        LayoutY = rectangle.y;
        LayoutWidth = rectangle.width;
        LayoutHeight = rectangle.height;
        HasLayout = true;
    }

    internal void BeginReturn(RectInt rectangle)
    {
        previousHasLayout = HasLayout;
        previousLayout = new RectInt(LayoutX, LayoutY, LayoutWidth, LayoutHeight);
        previousRevealed = Revealed;
        returnLayoutSaved = true;
        SetLayout(rectangle);
        Taken = false;
        Revealed = true;
    }

    internal bool UndoReturn()
    {
        if (!returnLayoutSaved) { return false; }
        SetLayout(previousLayout);
        HasLayout = previousHasLayout;
        Revealed = previousRevealed;
        Taken = true;
        returnLayoutSaved = false;
        return true;
    }
}

// 保存01实例ID、搜刮进度与本轮展示位置；归属、次数和保有状态仍只读01。
public sealed class LootSourceState
{
    private readonly List<LootEntryState> entries = new List<LootEntryState>();
    public int Round { get; private set; }
    public string SourceId { get; private set; }
    public string MapId { get; private set; }
    public bool IsGround { get; private set; }
    public bool Generated { get; internal set; }
    public string SearchEntryId { get; private set; } = string.Empty;
    public float SearchElapsed { get; private set; }
    public int GridWidth { get; private set; }
    public int GridHeight { get; private set; }
    public int LayoutColumns { get; private set; }
    public int LayoutRows { get; private set; }
    public IReadOnlyList<LootEntryState> Entries { get { return entries; } }
    internal string ScenePath;
    internal Vector3 Position;
    internal bool Committed = true;

    public LootSourceState(int round, string mapId, string sourceId, bool isGround,
        int gridWidth = 0, int gridHeight = 0)
    {
        Round = round;
        MapId = mapId;
        SourceId = sourceId;
        IsGround = isGround;
        GridWidth = gridWidth;
        GridHeight = gridHeight;
        if (!isGround)
        {
            LayoutColumns = gridWidth;
            LayoutRows = gridHeight;
        }
    }

    public static bool IsStableSegment(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && value == value.Trim()
            && value.IndexOf('/') < 0 && value.IndexOf('\\') < 0;
    }

    public bool TryGetEntry(string entryId, out LootEntryState entry)
    {
        entry = entries.Find(value => value.EntryId == entryId);
        return entry != null;
    }

    public LootEntryState GetNextUnrevealed()
    {
        return entries.Find(value => !value.Taken && !value.Revealed);
    }

    // 箱子大小来自本轮来源快照。拿走后释放格子，仍在箱内的物品不重排。
    public bool EnsureLayout(ItemManager items)
    {
        if (!TryPlanLayout(items, out List<LootEntryState> pending, out List<RectInt> additions,
            out int columns, out int rows)) { return false; }
        for (int i = 0; i < pending.Count; i++) { pending[i].SetLayout(additions[i]); }
        LayoutColumns = columns;
        LayoutRows = rows;
        return true;
    }

    internal bool CanFitCurrentEntries(ItemManager items)
    {
        return TryPlanLayout(items, out _, out _, out _, out _);
    }

    private bool TryPlanLayout(ItemManager items, out List<LootEntryState> pending,
        out List<RectInt> additions, out int columns, out int rows)
    {
        pending = new List<LootEntryState>();
        additions = new List<RectInt>();
        columns = IsGround ? 0 : GridWidth;
        rows = IsGround ? 0 : GridHeight;
        if (items == null || items.CurrentRound != Round || !Generated || !Committed
            || (!IsGround && (columns <= 0 || rows <= 0))) { return false; }
        List<RectInt> occupied = new List<RectInt>();
        foreach (LootEntryState entry in entries)
        {
            // 已拿走条目的历史位置只供原位返还优先使用，不占容量。
            if (entry.Taken) { continue; }
            ItemDataSO definition = items.TryGetInstance(entry.InstanceId, out ItemInstance item)
                ? item.Definition : string.IsNullOrEmpty(entry.InstanceId) ? entry.Candidate : null;
            if (definition == null || definition.width <= 0 || definition.height <= 0) { return false; }
            if (entry.HasLayout)
            {
                // 创建失败后重试仍使用已保存坐标；配置改变尺寸时不能用旧小矩形绕过容量检查。
                if (definition.width != entry.LayoutWidth || definition.height != entry.LayoutHeight) { return false; }
                RectInt saved = new RectInt(entry.LayoutX, entry.LayoutY, entry.LayoutWidth, entry.LayoutHeight);
                if (IsGround)
                {
                    columns = Math.Max(columns, saved.xMax);
                    rows = Math.Max(rows, saved.yMax);
                }
                if (!FitsAt(occupied, columns, rows, saved)) { return false; }
                occupied.Add(saved);
                continue;
            }
            // 原生地面等未指定箱内位置的来源按其实际尺寸建立展示。
            pending.Add(entry);
            additions.Add(new RectInt(0, 0, definition.width, definition.height));
            if (IsGround)
            {
                columns = Math.Max(columns, definition.width);
                if ((long)rows + definition.height > int.MaxValue) { return false; }
                rows += definition.height;
            }
        }
        for (int i = 0; i < pending.Count; i++)
        {
            RectInt size = additions[i];
            if (!TryFindLayoutSpace(occupied, columns, rows, size.width, size.height, out RectInt position))
            {
                return false;
            }
            additions[i] = position;
            occupied.Add(position);
        }
        return true;
    }

    internal static bool TryPlanDefinitions(IReadOnlyList<ItemDataSO> definitions, int columns, int rows,
        out List<RectInt> positions, out int failedIndex)
    {
        positions = new List<RectInt>();
        failedIndex = -1;
        if (columns <= 0 || rows <= 0) { return false; }
        for (int i = 0; i < definitions.Count; i++)
        {
            ItemDataSO definition = definitions[i];
            if (definition == null || !TryFindLayoutSpace(positions, columns, rows,
                definition.width, definition.height, out RectInt position))
            {
                failedIndex = i;
                return false;
            }
            positions.Add(position);
        }
        return true;
    }

    internal bool TryGetReturnPosition(ItemInstance item, out LootEntryState existing, out RectInt position)
    {
        existing = null;
        position = default;
        if (IsGround || item == null || item.Definition == null || GridWidth <= 0 || GridHeight <= 0) { return false; }
        List<RectInt> occupied = new List<RectInt>();
        foreach (LootEntryState entry in entries)
        {
            if (entry.InstanceId == item.InstanceId) { existing = entry; }
            if (entry.Taken) { continue; }
            if (!entry.HasLayout) { return false; }
            occupied.Add(new RectInt(entry.LayoutX, entry.LayoutY, entry.LayoutWidth, entry.LayoutHeight));
        }
        if (existing != null && !existing.Taken) { return false; }
        int width = item.Definition.width;
        int height = item.Definition.height;
        if (existing != null && existing.HasLayout)
        {
            RectInt original = new RectInt(existing.LayoutX, existing.LayoutY, width, height);
            if (FitsAt(occupied, GridWidth, GridHeight, original)) { position = original; return true; }
        }
        return TryFindLayoutSpace(occupied, GridWidth, GridHeight, width, height, out position);
    }

    private static bool FitsAt(List<RectInt> occupied, int columns, int rows, RectInt rectangle)
    {
        if (rectangle.x < 0 || rectangle.y < 0 || rectangle.width <= 0 || rectangle.height <= 0
            || (long)rectangle.x + rectangle.width > columns || (long)rectangle.y + rectangle.height > rows) { return false; }
        foreach (RectInt other in occupied) { if (rectangle.Overlaps(other)) { return false; } }
        return true;
    }

    private static bool TryFindLayoutSpace(List<RectInt> occupied, int columns, int rows, int width, int height,
        out RectInt position)
    {
        position = default;
        if (width <= 0 || height <= 0 || width > columns || height > rows) { return false; }
        // 从上到下、从左到右；候选边界只需检查0与已有矩形的边缘。
        List<int> xs = new List<int> { 0 };
        List<int> ys = new List<int> { 0 };
        foreach (RectInt rectangle in occupied)
        {
            if (!xs.Contains(rectangle.xMax)) { xs.Add(rectangle.xMax); }
            if (!ys.Contains(rectangle.yMax)) { ys.Add(rectangle.yMax); }
        }
        xs.Sort();
        ys.Sort();
        foreach (int y in ys)
        {
            if ((long)y + height > rows) { continue; }
            foreach (int x in xs)
            {
                if ((long)x + width > columns) { continue; }
                RectInt candidate = new RectInt(x, y, width, height);
                if (!FitsAt(occupied, columns, rows, candidate)) { continue; }
                position = candidate;
                return true;
            }
        }
        position = default;
        return false;
    }

    public bool AdvanceSearch(float delta, ItemManager items)
    {
        if (items == null || items.CurrentRound != Round || !Generated || !Committed
            || float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0f)
        {
            return false;
        }
        bool changed = false;
        // 逐个推进且每个条目只揭示一次，0秒物品也不会无限循环。
        for (int i = 0; i < entries.Count; i++)
        {
            LootEntryState next = GetNextUnrevealed();
            if (next == null)
            {
                InterruptSearch();
                break;
            }
            if (!items.TryGetInstance(next.InstanceId, out ItemInstance item) || item.Definition == null)
            {
                break;
            }
            if (SearchEntryId != next.EntryId)
            {
                SearchEntryId = next.EntryId;
                SearchElapsed = 0f;
            }
            float duration = item.Definition.searchSeconds;
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f) { break; }
            float step = Mathf.Min(delta, Mathf.Max(0f, duration - SearchElapsed));
            SearchElapsed += step;
            delta -= step;
            if (SearchElapsed < duration) { break; }
            next.Revealed = true;
            changed = true;
            InterruptSearch();
        }
        return changed;
    }

    public void InterruptSearch()
    {
        SearchEntryId = string.Empty;
        SearchElapsed = 0f;
    }

    internal void Add(LootEntryState entry) { entries.Add(entry); }

    internal bool Remove(LootEntryState entry) { return entries.Remove(entry); }
}

// 一次丢地预准备的记录，没有提交前不能作为可拾取来源。
public sealed class LootDropPreparation
{
    public string SourceId { get; internal set; }
    public string InstanceId { get; internal set; }
    public Vector3 Position { get; internal set; }
    internal GroundItem Presentation;
    internal LootSourceState State;
    internal PlayerController Player;
    internal bool Finished;
}
