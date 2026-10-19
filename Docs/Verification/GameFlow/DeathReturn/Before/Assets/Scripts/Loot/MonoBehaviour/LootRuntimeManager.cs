using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class LootRuntimeManager : MonoBehaviour
{
    public ItemManager itemManager;
    public GroundItem groundItemPrefab;
    [Min(0.05f)] public float dropRadius = 0.18f;
    [Min(0f)] public float dropClearance = 0.03f;
    public LayerMask dropGroundLayers = ~0;
    public LayerMask dropBlockingLayers = ~0;

    private readonly Dictionary<string, LootSourceState> sources = new Dictionary<string, LootSourceState>();
    private readonly Dictionary<string, Interactable> bindings = new Dictionary<string, Interactable>();
    private readonly Dictionary<string, GroundItem> presentations = new Dictionary<string, GroundItem>();
    private readonly Dictionary<string, string> uniqueOrigins = new Dictionary<string, string>();
    private readonly List<LootDropPreparation> preparedDrops = new List<LootDropPreparation>();
    private Transform stagingRoot;
    private bool notifying;

    public int PreparedRound { get; private set; }
    public string ActiveMapId { get; private set; } = string.Empty;
    public bool IsExplorationActive { get; private set; }
    public event Action<LootSourceState> SourceChanged;

    private void Awake()
    {
        GameBootstrap root = GetComponentInParent<GameBootstrap>();
        if (itemManager == null && root != null) { itemManager = root.GetComponentInChildren<ItemManager>(true); }
        if (itemManager == null) { itemManager = FindFirstObjectByType<ItemManager>(); }
    }

    public static LootRuntimeManager Resolve()
    {
        GameBootstrap root = GameBootstrap.Instance;
        if (root == null || !root.isActiveAndEnabled) { return null; }
        ItemManager items = root.GetComponentInChildren<ItemManager>(true);
        foreach (LootRuntimeManager runtime in root.GetComponentsInChildren<LootRuntimeManager>(true))
        {
            if (!runtime.isActiveAndEnabled || items == null) { continue; }
            if (runtime.itemManager == null) { runtime.itemManager = items; }
            if (runtime.itemManager == items) { return runtime; }
        }
        return null;
    }

    private void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        foreach (LootSourceState state in sources.Values) { state.InterruptSearch(); }
        foreach (LootDropPreparation drop in new List<LootDropPreparation>(preparedDrops)) { CancelPreparedDrop(drop); }
    }

    public void SetExplorationContext(string mapId, bool active)
    {
        ActiveMapId = mapId ?? string.Empty;
        IsExplorationActive = active && LootSourceState.IsStableSegment(mapId);
    }

    // 08/09可在启动探索前检查资源；与实际丢放使用同一校验，不创建任何表现或实例。
    public bool ValidateDropPrefab(out string error)
    {
        return ValidateGroundPrefab(out error);
    }

    // 08先结束旧世界实例并调用01.BeginRound；06只准备现有轮次，不自行推进。
    public bool PrepareRound(int round, out string error)
    {
        if (itemManager == null) { itemManager = FindFirstObjectByType<ItemManager>(); }
        if (!isActiveAndEnabled || notifying || itemManager == null || !itemManager.isActiveAndEnabled
            || round <= 0 || itemManager.CurrentRound != round || round < PreparedRound)
        {
            error = "轮次未由01准备或06正在通知。";
            return false;
        }
        if (!ValidateSceneSources(out error)) { return false; }
        if (PreparedRound != 0 && PreparedRound != round && GetRemainingWorldItems().Count > 0)
        {
            error = "上一轮仍有世界/箱内实例；08必须先结算，不能直接清空来源。";
            return false;
        }
        if (PreparedRound != round)
        {
            foreach (LootDropPreparation drop in new List<LootDropPreparation>(preparedDrops)) { CancelPreparedDrop(drop); }
            foreach (GroundItem item in new List<GroundItem>(presentations.Values))
            {
                if (item != null) { item.HidePresentation(); }
            }
            presentations.Clear();
            sources.Clear();
            PreparedRound = round;
        }
        return BindSceneSources(out error);
    }

    public List<ItemInstance> GetRemainingWorldItems()
    {
        List<ItemInstance> remaining = new List<ItemInstance>();
        HashSet<string> seen = new HashSet<string>();
        if (itemManager == null) { return remaining; }
        foreach (LootSourceState state in sources.Values)
        {
            if (!state.Committed) { continue; }
            foreach (LootEntryState entry in state.Entries)
            {
                if (!entry.Taken && itemManager.TryGetInstance(entry.InstanceId, out ItemInstance item)
                    && (item.Owner == ItemOwner.World || item.Owner == ItemOwner.Container) && seen.Add(item.InstanceId))
                {
                    remaining.Add(item);
                }
            }
        }
        return remaining;
    }

    public bool TryGetSource(string sourceId, out LootSourceState state)
    {
        state = null;
        return sourceId != null && sources.TryGetValue(sourceId, out state) && state.Committed;
    }

    public bool TryGetBinding(string sourceId, out Interactable target)
    {
        target = null;
        return sourceId != null && bindings.TryGetValue(sourceId, out target) && target != null;
    }

    public bool IsSourceReady(LootSourceState state)
    {
        if (!isActiveAndEnabled || state == null || !state.Committed || !state.Generated || state.Round != PreparedRound
            || itemManager == null || !itemManager.isActiveAndEnabled || itemManager.CurrentRound != PreparedRound
            || !sources.TryGetValue(state.SourceId, out LootSourceState saved) || saved != state) { return false; }
        foreach (LootEntryState entry in state.Entries)
        {
            if (!entry.Taken && (!itemManager.TryGetInstance(entry.InstanceId, out ItemInstance item)
                || (!string.IsNullOrEmpty(item.UniqueId) && !item.SpawnClaimed)
                || item.Owner != (state.IsGround ? ItemOwner.World : ItemOwner.Container)
                || item.ContainerId != state.SourceId)) { return false; }
        }
        return true;
    }

    public bool TryValidatePickup(string sourceId, string entryId, PlayerInteraction player, out ItemInstance item)
    {
        item = null;
        PlayerController controller = player == null ? null : player.GetComponentInParent<PlayerController>();
        return !notifying && IsExplorationActive && controller != null && controller.isActiveAndEnabled
            && controller.IsPrepared && !controller.IsDead && TryGetSource(sourceId, out LootSourceState state)
            && state.MapId == ActiveMapId && IsSourceReady(state)
            && state.TryGetEntry(entryId, out LootEntryState entry) && entry.Revealed && !entry.Taken
            && TryGetBinding(sourceId, out Interactable target) && player.CanReachTarget(target)
            && itemManager.TryGetInstance(entry.InstanceId, out item)
            && item.Owner == (state.IsGround ? ItemOwner.World : ItemOwner.Container) && item.ContainerId == sourceId;
    }

    public bool TryValidateReturnTarget(LootSourceState source, PlayerInteraction interaction)
    {
        PlayerController controller = interaction == null ? null : interaction.GetComponentInParent<PlayerController>();
        return !notifying && IsExplorationActive && source != null && !source.IsGround
            && source.MapId == ActiveMapId && IsSourceReady(source)
            && controller != null && controller.isActiveAndEnabled && controller.IsPrepared && !controller.IsDead
            && TryGetBinding(source.SourceId, out Interactable target) && target is LootBox box
            && box.State == source && interaction.CanReachTarget(box);
    }

    public ItemTransferResult CanPlaceReturnedItem(LootSourceState source, ItemInstance item,
        int x, int y, bool rotated)
    {
        ItemTransferResult validation = ValidateReturnedItem(source, item);
        return validation == ItemTransferResult.Success
            ? source.CanPlaceReturnedItem(item, x, y, rotated, out _, out _) : validation;
    }

    // 普通拖拽只检查指定位置；预览失败不能偷偷换到其它空格。
    public bool TryGetReturnedItemPosition(LootSourceState source, ItemInstance item,
        int x, int y, bool rotated, out RectInt position)
    {
        position = default;
        return ValidateReturnedItem(source, item) == ItemTransferResult.Success
            && source.CanPlaceReturnedItem(item, x, y, rotated, out _, out position) == ItemTransferResult.Success;
    }

    public bool TryFindReturnedItemPosition(LootSourceState source, ItemInstance item, bool preferredRotated,
        out RectInt position, out bool rotated)
    {
        position = default;
        rotated = preferredRotated;
        return ValidateReturnedItem(source, item) == ItemTransferResult.Success
            && source.TryFindReturnPosition(item, preferredRotated, out position, out rotated);
    }

    private ItemTransferResult ValidateReturnedItem(LootSourceState source, ItemInstance item)
    {
        if (notifying || !isActiveAndEnabled || itemManager == null || !itemManager.isActiveAndEnabled)
        {
            return ItemTransferResult.NotReady;
        }
        if (source == null || source.IsGround || !IsSourceReady(source) || !source.CanFitCurrentEntries(itemManager)
            || item == null || item.Owner != ItemOwner.Backpack
            || !itemManager.TryGetInstance(item.InstanceId, out ItemInstance registered) || registered != item)
        {
            return ItemTransferResult.SourceInvalid;
        }
        return ItemTransferResult.Success;
    }

    // 背包放回箱子也先静默添加目标。旧taken条目直接复用，避免同轮重绑出现两份同物品记录。
    public bool TryAddReturnedItem(LootSourceState source, ItemInstance item, int x, int y, bool rotated,
        out LootEntryState entry, out bool reusedTakenEntry)
    {
        entry = null;
        reusedTakenEntry = false;
        if (ValidateReturnedItem(source, item) != ItemTransferResult.Success
            || source.CanPlaceReturnedItem(item, x, y, rotated, out LootEntryState existing,
                out RectInt position) != ItemTransferResult.Success) { return false; }
        if (existing != null)
        {
            existing.BeginReturn(position, rotated);
            entry = existing;
            reusedTakenEntry = true;
            return true;
        }
        string entryId = "returned-" + item.InstanceId;
        if (source.TryGetEntry(entryId, out _)) { return false; }
        entry = new LootEntryState(entryId, item.InstanceId, true);
        entry.SetLayout(position, rotated);
        source.Add(entry);
        // 追加已揭示条目不清理当前搜索项；先前未完成物品仍使用原EntryId与进度。
        return true;
    }

    public bool TryUndoReturnedItem(LootSourceState source, string entryId, bool reusedTakenEntry)
    {
        // 恢复不重新要求空间/探索权限；失败可能正是来自权限撤销，不能因此留下半次添加。
        if (notifying || source == null || source.IsGround || source.Round != PreparedRound
            || !TryGetSource(source.SourceId, out LootSourceState saved) || saved != source
            || !source.TryGetEntry(entryId, out LootEntryState entry) || entry.Taken
            || itemManager == null || !itemManager.TryGetInstance(entry.InstanceId, out ItemInstance item)
            || item.Owner != ItemOwner.Backpack) { return false; }
        if (reusedTakenEntry)
        {
            return entry.UndoReturn();
        }
        return entry.EntryId == "returned-" + entry.InstanceId && source.Remove(entry);
    }

    // 转移期间只静默调整标记；归属和双方布局完成后才通知UI。
    public bool TryMarkTaken(string sourceId, string entryId)
    {
        if (notifying || !TryGetSource(sourceId, out LootSourceState state) || !IsSourceReady(state)
            || !state.TryGetEntry(entryId, out LootEntryState entry) || !entry.Revealed || entry.Taken) { return false; }
        entry.Taken = true;
        return true;
    }

    public bool TryRestoreTaken(string sourceId, string entryId)
    {
        if (notifying || !TryGetSource(sourceId, out LootSourceState state) || state.Round != PreparedRound
            || !state.TryGetEntry(entryId, out LootEntryState entry) || !entry.Taken
            || !itemManager.TryGetInstance(entry.InstanceId, out ItemInstance item)
            || item.Owner != (state.IsGround ? ItemOwner.World : ItemOwner.Container) || item.ContainerId != sourceId) { return false; }
        entry.Taken = false;
        return true;
    }

    public void NotifySourceChanged(string sourceId)
    {
        if (notifying || !TryGetSource(sourceId, out LootSourceState state)) { return; }
        notifying = true;
        try
        {
            if (SourceChanged == null) { return; }
            foreach (Action<LootSourceState> listener in SourceChanged.GetInvocationList())
            {
                try { listener(state); }
                catch (Exception exception) { Debug.LogException(exception, this); }
            }
        }
        finally { notifying = false; }
    }

    public bool BindBox(LootBox box, out string error)
    {
        error = null;
        if (box == null || !ValidateSceneSources(out error)) { return false; }
        string key = box.SourceKey;
        if (!BindTarget(key, box, out error)) { return false; }
        box.runtimeManager = this;
        // 01已进入新轮而08还未调用06准备时，只绑定场景，不用旧轮次生成内容。
        if (PreparedRound <= 0 || itemManager == null || itemManager.CurrentRound != PreparedRound) { return true; }
        if (!sources.TryGetValue(key, out LootSourceState state))
        {
            state = NewSource(box.mapId, key, false, box, box.gridWidth, box.gridHeight);
            sources.Add(key, state);
        }
        box.SetState(state);
        return GenerateSource(state, box.lootTable, out error);
    }

    public bool BindGround(GroundItem ground, out string error)
    {
        error = null;
        if (ground == null || ground.IsRuntimePresentation || !ValidateSceneSources(out error)) { return false; }
        if (!BindTarget(ground.SourceKey, ground, out error)) { return false; }
        ground.runtimeManager = this;
        if (PreparedRound <= 0 || itemManager == null || itemManager.CurrentRound != PreparedRound) { return true; }
        if (!sources.TryGetValue(ground.SourceKey, out LootSourceState state))
        {
            state = NewSource(ground.mapId, ground.SourceKey, true, ground);
            sources.Add(state.SourceId, state);
        }
        ground.SetState(state);
        LootTableSO table = ScriptableObject.CreateInstance<LootTableSO>();
        // 独立地面原点表示策划明确放置的一件物品；箱子的候选概率由LootTable配置。
        table.entries.Add(new LootTableEntry { entryId = ground.entryId, item = ground.initialItem,
            useDefaultProbability = false, overrideProbability = 1f });
        bool success = GenerateSource(state, table, out error);
        if (Application.isPlaying) { Destroy(table); } else { DestroyImmediate(table); }
        if (success) { BindGroundPresentation(ground, state); }
        return success;
    }

    public void Unbind(Interactable target, string sourceId)
    {
        if (sourceId != null && bindings.TryGetValue(sourceId, out Interactable current) && current == target)
        {
            bindings.Remove(sourceId);
            if (sources.TryGetValue(sourceId, out LootSourceState state)) { state.InterruptSearch(); }
        }
        if (target is GroundItem ground && !string.IsNullOrEmpty(ground.InstanceId)
            && presentations.TryGetValue(ground.InstanceId, out GroundItem currentGround) && currentGround == ground)
        {
            presentations.Remove(ground.InstanceId);
        }
    }

    public void CompletePickupVisual(string instanceId)
    {
        if (instanceId != null && presentations.TryGetValue(instanceId, out GroundItem ground))
        {
            presentations.Remove(instanceId);
            if (ground != null) { ground.HidePresentation(); }
        }
    }

    public bool TryPrepareDrop(ItemInstance item, PlayerController player, out LootDropPreparation preparation, out string error)
    {
        preparation = null;
        error = "没有安全且可达的地面落点。";
        if (!isActiveAndEnabled || notifying || !IsExplorationActive || PreparedRound <= 0 || itemManager == null
            || !itemManager.isActiveAndEnabled
            || itemManager.CurrentRound != PreparedRound || item == null || item.Owner != ItemOwner.Backpack
            || !itemManager.TryGetInstance(item.InstanceId, out ItemInstance actual) || actual != item
            || !IsValidDropPlayer(player) || !ValidateGroundPrefab(out error)) { return false; }
        foreach (LootDropPreparation existing in preparedDrops)
        {
            if (!existing.Finished && existing.InstanceId == item.InstanceId) { error = "同一物品已经在准备丢放。"; return false; }
        }
        if (!TryFindDropPoint(player, out Vector3 position)) { error = "没有安全且可达的地面落点。"; return false; }
        string key = ActiveMapId + "/drop-" + Guid.NewGuid().ToString("N");
        LootSourceState state = new LootSourceState(PreparedRound, ActiveMapId, key, true)
        {
            Generated = true, Committed = false, Position = position, ScenePath = SceneKey(player.gameObject.scene)
        };
        state.Add(new LootEntryState("item", item.InstanceId, true));
        GroundItem presentation = CreateInactiveGround(state, player.gameObject.scene, out error);
        if (presentation == null) { return false; }
        sources.Add(key, state);
        preparation = new LootDropPreparation
        {
            SourceId = key, InstanceId = item.InstanceId, Position = position,
            State = state, Presentation = presentation, Player = player
        };
        preparedDrops.Add(preparation);
        error = null;
        return true;
    }

    public bool CanCommitPreparedDrop(LootDropPreparation preparation)
    {
        return isActiveAndEnabled && itemManager != null && itemManager.isActiveAndEnabled
            && ValidateGroundPrefab(out _) && preparation != null && !preparation.Finished && preparedDrops.Contains(preparation)
            && preparation.Presentation != null && !preparation.Presentation.gameObject.activeInHierarchy
            && preparation.State.Round == PreparedRound && itemManager.CurrentRound == PreparedRound
            && preparation.State.MapId == ActiveMapId && IsValidDropPlayer(preparation.Player)
            && sources.TryGetValue(preparation.SourceId, out LootSourceState state) && state == preparation.State
            && IsDropPointSafe(preparation.Player, preparation.Position);
    }

    public bool CommitPreparedDrop(LootDropPreparation preparation)
    {
        if (preparation == null || preparation.Finished || !preparedDrops.Contains(preparation)
            || !itemManager.TryGetInstance(preparation.InstanceId, out ItemInstance item)
            || item.Owner != ItemOwner.World || item.ContainerId != preparation.SourceId) { return false; }
        // 归属已经提交，视觉错误也不能伪报失败或记损；保留记录供同轮补建。
        preparation.State.Committed = true;
        preparation.Finished = true;
        preparedDrops.Remove(preparation);
        try
        {
            if (preparation.Presentation != null) { ActivateGround(preparation.Presentation, preparation.State); }
        }
        catch (Exception exception) { Debug.LogException(exception, this); }
        return true;
    }

    public void CancelPreparedDrop(LootDropPreparation preparation)
    {
        if (preparation == null || preparation.Finished) { return; }
        // 若调用者已完成World归属，取消不能删掉唯一的运行记录。
        if (itemManager != null && itemManager.TryGetInstance(preparation.InstanceId, out ItemInstance item)
            && item.Owner == ItemOwner.World && item.ContainerId == preparation.SourceId)
        {
            CommitPreparedDrop(preparation);
            return;
        }
        preparation.Finished = true;
        preparedDrops.Remove(preparation);
        sources.Remove(preparation.SourceId);
        if (preparation.Presentation != null)
        {
            if (Application.isPlaying) { Destroy(preparation.Presentation.gameObject); }
            else { DestroyImmediate(preparation.Presentation.gameObject); }
        }
    }

    public void RebuildSceneRepresentations()
    {
        foreach (LootSourceState state in sources.Values)
        {
            if (!state.IsGround || !state.Committed || !IsSourceReady(state) || state.Entries.Count == 0
                || state.Entries[0].Taken) { continue; }
            if (TryGetBinding(state.SourceId, out Interactable existing) && existing is GroundItem marker)
            {
                BindGroundPresentation(marker, state);
                continue;
            }
            Scene scene = FindLoadedScene(state.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) { continue; }
            GroundItem ground = CreateInactiveGround(state, scene, out string error);
            if (ground != null) { ActivateGround(ground, state); }
            else { Debug.LogWarning(error, this); }
        }
    }

    private bool GenerateSource(LootSourceState state, LootTableSO table, out string error)
    {
        error = null;
        if (!state.Generated)
        {
            Dictionary<string, RectInt> candidatePositions = null;
            if (!state.IsGround && !TryPlanBoxCandidates(table, state.SourceId, state.GridWidth, state.GridHeight,
                out candidatePositions, out error)) { return false; }
            if (!LootGenerator.TrySelect(table, itemManager, state.SourceId, out List<LootTableEntry> selected, out error)) { return false; }
            foreach (LootTableEntry entry in selected)
            {
                LootEntryState generated = new LootEntryState(entry, state.IsGround);
                // 命中项沿用全候选预排位置。未命中项不建记录，其洞仍可接收后续返还。
                if (candidatePositions != null) { generated.SetLayout(candidatePositions[entry.entryId]); }
                state.Add(generated);
            }
            // 空结果也保存。后续失败只重试登记/绑定，不重新抽概率。
            state.Generated = true;
        }
        // 实际结果含0%唯一找回与之前保留的选择，也必须全部可放，才允许任何01身份写入。
        if (!state.CanFitCurrentEntries(itemManager))
        {
            error = state.SourceId + ": 已选物品无法放入本轮 " + state.GridWidth + "×" + state.GridHeight
                + " 格；选择记录保留，本次不继续创建或认领。";
            return false;
        }
        foreach (LootEntryState entry in state.Entries)
        {
            if (entry.Taken) { continue; }
            string origin = state.SourceId + "/" + entry.EntryId;
            ItemInstance instance;
            if (!itemManager.TryGetInstance(entry.InstanceId, out instance))
            {
                if (entry.Candidate == null) { error = origin + ": 缺少物品配置。"; return false; }
                if (!entry.Candidate.isUnique || !itemManager.TryGetUniqueInstance(entry.Candidate.uniqueId, out instance))
                {
                    if (!itemManager.TryCreateInstance(entry.Candidate, state.IsGround ? ItemOwner.World : ItemOwner.Container,
                        state.SourceId, state.SourceId, origin, out instance))
                    {
                        error = origin + ": 01拒绝创建实例；已选结果保留，可修正配置后重试。";
                        return false;
                    }
                }
                entry.InstanceId = instance.InstanceId;
            }
            if (!string.IsNullOrEmpty(instance.UniqueId) && !instance.SpawnClaimed
                && !itemManager.TryClaimUniqueSpawn(instance.InstanceId, origin))
            {
                error = origin + ": 01拒绝原点生成许可，实例记录已保留。";
                return false;
            }
            ItemOwner targetOwner = state.IsGround ? ItemOwner.World : ItemOwner.Container;
            if (instance.Owner != targetOwner || instance.ContainerId != state.SourceId)
            {
                // 找回许可先变成World；箱内来源还要恢复Container，完成后才允许展示/拿取。
                if (instance.Owner != ItemOwner.World || instance.ContainerId != origin
                    || !itemManager.TrySetOwner(instance.InstanceId, targetOwner, state.SourceId))
                {
                    error = origin + ": 归属尚未恢复，保留来源记录等待同轮重试。";
                    return false;
                }
            }
        }
        if (!state.EnsureLayout(itemManager))
        {
            error = state.SourceId + ": 实例记录已保留，当前配置无法完成展示布局。";
            return false;
        }
        return true;
    }

    private LootSourceState NewSource(string mapId, string sourceId, bool ground, Interactable target,
        int gridWidth = 0, int gridHeight = 0)
    {
        return new LootSourceState(PreparedRound, mapId, sourceId, ground, gridWidth, gridHeight)
        {
            Position = target.transform.position, ScenePath = SceneKey(target.gameObject.scene)
        };
    }

    private bool BindTarget(string key, Interactable target, out string error)
    {
        if (bindings.TryGetValue(key, out Interactable existing) && existing != null && existing != target)
        {
            error = key + ": 同时存在两个稳定来源。";
            return false;
        }
        bindings[key] = target;
        error = null;
        return true;
    }

    private void BindGroundPresentation(GroundItem ground, LootSourceState state)
    {
        if (state.Entries.Count == 0 || state.Entries[0].Taken)
        {
            ground.HidePresentation();
            return;
        }
        string id = state.Entries[0].InstanceId;
        if (presentations.TryGetValue(id, out GroundItem existing) && existing != null && existing != ground)
        {
            ground.HidePresentation();
            Debug.LogError("同一实例已有地面表现：" + id, ground);
            return;
        }
        ground.BindInstance(state, id);
        presentations[id] = ground;
        ground.ShowPresentation();
    }

    private GroundItem CreateInactiveGround(LootSourceState state, Scene scene, out string error)
    {
        if (!ValidateGroundPrefab(out error)) { return null; }
        GameObject clone = null;
        try
        {
            if (stagingRoot == null)
            {
                GameObject staging = new GameObject("LootPreparation");
                staging.transform.SetParent(transform, false);
                staging.SetActive(false);
                stagingRoot = staging.transform;
            }
            clone = Instantiate(groundItemPrefab.gameObject, stagingRoot);
            clone.SetActive(false);
            GroundItem ground = clone.GetComponent<GroundItem>();
            ground.PrepareRuntime(this, state);
            clone.transform.SetParent(null);
            SceneManager.MoveGameObjectToScene(clone, scene);
            clone.transform.position = state.Position;
            clone.transform.rotation = Quaternion.identity;
            clone.transform.localScale = groundItemPrefab.transform.localScale;
            return ground;
        }
        catch (Exception exception)
        {
            error = "地面表现准备失败：" + exception.Message;
            if (clone != null) { if (Application.isPlaying) { Destroy(clone); } else { DestroyImmediate(clone); } }
            return null;
        }
    }

    private void ActivateGround(GroundItem ground, LootSourceState state)
    {
        bindings[state.SourceId] = ground;
        ground.gameObject.SetActive(true);
        BindGroundPresentation(ground, state);
    }

    private bool ValidateGroundPrefab(out string error)
    {
        if (float.IsNaN(dropRadius) || float.IsInfinity(dropRadius) || dropRadius <= 0f
            || float.IsNaN(dropClearance) || float.IsInfinity(dropClearance) || dropClearance < 0f)
        {
            error = "丢放半径必须为正有限数，间距必须为非负有限数。";
            return false;
        }
        if (groundItemPrefab == null || groundItemPrefab.GetComponent<BoxCollider>() == null
            || !groundItemPrefab.GetComponent<BoxCollider>().enabled)
        {
            error = "地面预制体必须包含启用的小根BoxCollider与GroundItem。";
            return false;
        }
        BoxCollider collider = groundItemPrefab.GetComponent<BoxCollider>();
        BoxCollider range = groundItemPrefab.interactionTrigger;
        // Prefab和准备中的对象可以未激活；这里只查配置，不调用运行时空间可达入口。
        if (range == null || range.transform == groundItemPrefab.transform || !range.enabled || !IsInteractionRange(range)
            || range.GetComponentInParent<Interactable>(true) != groundItemPrefab)
        {
            error = "地面预制体需独立的interactionTrigger子Box，不能兼用小根占位Collider。";
            return false;
        }
        for (Transform part = range.transform; part != groundItemPrefab.transform; part = part.parent)
        {
            if (!part.gameObject.activeSelf)
            {
                error = "地面预制体的interactionTrigger子物体必须启用。";
                return false;
            }
        }
        Vector3 rangeSize = Vector3.Scale(range.size, range.transform.lossyScale);
        if (!IsFinite(rangeSize.x) || !IsFinite(rangeSize.y) || !IsFinite(rangeSize.z)
            || Mathf.Abs(rangeSize.x) <= 0f || Mathf.Abs(rangeSize.y) <= 0f || Mathf.Abs(rangeSize.z) <= 0f
            || !IsFinite(range.center.x) || !IsFinite(range.center.y) || !IsFinite(range.center.z))
        {
            error = "地面预制体的interactionTrigger尺寸和中心必须是有效有限数。";
            return false;
        }
        foreach (Collider extra in groundItemPrefab.GetComponentsInChildren<Collider>(true))
        {
            if (extra != collider && extra != range)
            {
                error = "简单地面预制体只允许小根占位Box和明确绑定的interactionTrigger。";
                return false;
            }
        }
        Vector3 size = Vector3.Scale(collider.size, groundItemPrefab.transform.lossyScale);
        if (!IsFinite(size.x) || !IsFinite(size.y) || !IsFinite(size.z)
            || !IsFinite(collider.center.x) || !IsFinite(collider.center.y) || !IsFinite(collider.center.z)
            || Mathf.Abs(size.x) <= 0f || Mathf.Abs(size.y) <= 0f || Mathf.Abs(size.z) <= 0f
            || Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) > dropRadius * 2f + 0.001f
            || collider.center.sqrMagnitude > 0.0001f || groundItemPrefab.GetComponentInChildren<Rigidbody>(true) != null)
        {
            error = "简单地面表现需中心BoxCollider、尺寸不超过dropRadius直径、无Rigidbody。";
            return false;
        }
        error = null;
        return true;
    }

    private static bool IsInteractionRange(Collider collider)
    {
        if (collider == null || !collider.isTrigger) { return false; }
        Interactable owner = collider.GetComponentInParent<Interactable>(true);
        return owner != null && owner.interactionTrigger == collider;
    }

    private bool IsValidDropPlayer(PlayerController player)
    {
        return IsExplorationActive && player != null && player.isActiveAndEnabled && player.IsPrepared
            && !player.IsDead && player.IsBackpackOpen && player.config != null
            && IsFinite(player.config.planeZ)
            && player.interaction != null && player.gameObject.scene.IsValid() && player.gameObject.scene.isLoaded;
    }

    private bool TryFindDropPoint(PlayerController player, out Vector3 position)
    {
        Physics.SyncTransforms();
        float[] offsets = { 0.8f, 1.15f, -0.8f, -1.15f };
        foreach (float offset in offsets)
        {
            Vector3 origin = player.Position + Vector3.right * offset * player.FacingDirection + Vector3.up;
            origin.z = player.config.planeZ;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 3f, dropGroundLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(player.transform) || hit.normal.y < 0.85f) { continue; }
                Vector3 point = hit.point + Vector3.up * (dropRadius + dropClearance);
                point.z = player.config.planeZ;
                if (IsDropPointSafe(player, point)) { position = point; return true; }
                break;
            }
        }
        position = default;
        return false;
    }

    private bool IsDropPointSafe(PlayerController player, Vector3 point)
    {
        if (player == null || player.config == null || Mathf.Abs(point.z - player.config.planeZ) > 0.001f
            || !player.interaction.CanReachWorldPoint(point)) { return false; }
        if (!Physics.Raycast(point, Vector3.down, out RaycastHit support, dropRadius + dropClearance + 0.06f,
            dropGroundLayers, QueryTriggerInteraction.Ignore) || support.normal.y < 0.85f
            || support.collider.transform.IsChildOf(player.transform)) { return false; }
        foreach (Collider overlap in Physics.OverlapBox(point, Vector3.one * dropRadius, Quaternion.identity,
            dropBlockingLayers, QueryTriggerInteraction.Collide))
        {
            // 丢到玩家或墙体内都拒绝；不能把背包UI锁误当作不可操作。
            // 仅明确绑定的交互范围不占落点；物品的小根Trigger仍代表实际体积。
            if (overlap != null && !IsInteractionRange(overlap)
                && (!overlap.isTrigger || overlap.GetComponentInParent<GroundItem>() != null
                || overlap.GetComponentInParent<LootBox>() != null)) { return false; }
        }
        return true;
    }

    private bool ValidateSceneSources(out string error)
    {
        HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, string> origins = new Dictionary<string, string>(uniqueOrigins, StringComparer.Ordinal);
        Dictionary<string, ItemDataSO> definitions = new Dictionary<string, ItemDataSO>(StringComparer.Ordinal);
        foreach (LootBox box in FindObjectsByType<LootBox>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!ValidateMarker(box.mapId, box.sourceId, box, keys, out error)) { return false; }
            if (box.lootTable == null) { error = box.SourceKey + ": 缺少掉落表。"; return false; }
            if (!box.lootTable.TryValidate(out error)) { return false; }
            if (!ValidateBoxCapacity(box, out error)) { return false; }
            foreach (LootTableEntry entry in box.lootTable.entries)
            {
                if (!ValidateDefinition(entry.item, box.SourceKey + "/" + entry.entryId, origins, definitions, out error)) { return false; }
            }
        }
        foreach (GroundItem ground in FindObjectsByType<GroundItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (ground.IsRuntimePresentation) { continue; }
            if (!ValidateMarker(ground.mapId, ground.sourceId, ground, keys, out error)) { return false; }
            if (!LootSourceState.IsStableSegment(ground.entryId) || ground.initialItem == null)
            {
                error = ground.SourceKey + ": 地面原点缺少entryId或物品。";
                return false;
            }
            if (!ground.initialItem.TryValidate(out error)
                || !ValidateDefinition(ground.initialItem, ground.SourceKey + "/" + ground.entryId, origins, definitions, out error)) { return false; }
        }
        uniqueOrigins.Clear();
        foreach (KeyValuePair<string, string> pair in origins) { uniqueOrigins.Add(pair.Key, pair.Value); }
        error = null;
        return true;
    }

    private bool ValidateBoxCapacity(LootBox box, out string error)
    {
        int width = box.gridWidth;
        int height = box.gridHeight;
        // 同轮重绑沿用第一次绑定的容量和条目；场景上的修改从下一轮起生效。
        if (sources.TryGetValue(box.SourceKey, out LootSourceState existing) && !existing.IsGround
            && existing.Round == PreparedRound && itemManager != null && itemManager.CurrentRound == PreparedRound)
        {
            width = existing.GridWidth;
            height = existing.GridHeight;
            if (existing.Generated)
            {
                if (existing.CanFitCurrentEntries(itemManager)) { error = null; return true; }
                error = box.SourceKey + ": 本轮保留的物品无法放入 " + width + "×" + height + " 格。";
                return false;
            }
        }
        return TryPlanBoxCandidates(box.lootTable, box.SourceKey, width, height, out _, out error);
    }

    private bool TryPlanBoxCandidates(LootTableSO table, string sourceId, int width, int height,
        out Dictionary<string, RectInt> positions, out string error)
    {
        positions = new Dictionary<string, RectInt>();
        if (width <= 0 || height <= 0)
        {
            error = sourceId + ": 箱子格子宽高必须大于0。";
            return false;
        }
        List<ItemDataSO> candidates = new List<ItemDataSO>();
        List<string> entryIds = new List<string>();
        HashSet<string> recoverable = new HashSet<string>();
        if (itemManager != null)
        {
            foreach (ItemInstance item in itemManager.GetRecoverableItems()) { recoverable.Add(item.InstanceId); }
        }
        foreach (LootTableEntry entry in table.entries)
        {
            bool required = entry.Probability > 0f;
            if (!required && entry.item.isUnique && itemManager != null
                && itemManager.TryGetUniqueInstance(entry.item.uniqueId, out ItemInstance unique))
            {
                string origin = sourceId + "/" + entry.entryId;
                required = unique.OriginalSpawnPointId == origin && (recoverable.Contains(unique.InstanceId)
                    || ((unique.Owner == ItemOwner.World || unique.Owner == ItemOwner.Container)
                        && (unique.ContainerId == sourceId || unique.ContainerId == origin)));
            }
            if (!required) { continue; }
            candidates.Add(entry.item);
            entryIds.Add(entry.entryId);
        }
        if (!LootSourceState.TryPlanDefinitions(candidates, width, height, out List<RectInt> planned, out int failedIndex))
        {
            string entryId = failedIndex >= 0 && failedIndex < entryIds.Count ? entryIds[failedIndex] : "容量";
            error = sourceId + "/" + entryId + ": 候选物品无法按顺序放入 " + width + "×" + height
                + " 格，请修改箱子尺寸或掉落表。";
            return false;
        }
        for (int i = 0; i < entryIds.Count; i++) { positions.Add(entryIds[i], planned[i]); }
        error = null;
        return true;
    }

    private bool ValidateDefinition(ItemDataSO definition, string origin, Dictionary<string, string> origins,
        Dictionary<string, ItemDataSO> definitions, out string error)
    {
        if (definitions.TryGetValue(definition.definitionId, out ItemDataSO existing) && existing != definition)
        {
            error = origin + ": definitionId 指向不同资源。";
            return false;
        }
        definitions[definition.definitionId] = definition;
        if (itemManager != null)
        {
            foreach (ItemInstance registered in itemManager.GetInstances())
            {
                if (registered.DefinitionId == definition.definitionId && registered.Definition != definition)
                {
                    error = origin + ": 与01登记的物品定义冲突。";
                    return false;
                }
            }
        }
        if (definition.isUnique)
        {
            if (origins.TryGetValue(definition.uniqueId, out string original) && original != origin)
            {
                error = origin + ": 唯一物品重复原点，与 " + original + " 冲突。";
                return false;
            }
            if (itemManager != null && itemManager.TryGetUniqueInstance(definition.uniqueId, out ItemInstance instance)
                && instance.OriginalSpawnPointId != origin)
            {
                error = origin + ": 不符合01已登记的唯一原点。";
                return false;
            }
            origins[definition.uniqueId] = origin;
        }
        error = null;
        return true;
    }

    private static bool ValidateMarker(string mapId, string sourceId, Interactable target, HashSet<string> keys, out string error)
    {
        if (!LootSourceState.IsStableSegment(mapId) || !LootSourceState.IsStableSegment(sourceId) || !keys.Add(mapId + "/" + sourceId))
        {
            error = target.name + ": mapId/sourceId为空、不稳定或重复。";
            return false;
        }
        if (target.GetComponentInChildren<Collider>(true) == null)
        {
            error = mapId + "/" + sourceId + ": 来源缺少3D Collider。";
            return false;
        }
        error = null;
        return true;
    }

    private bool BindSceneSources(out string error)
    {
        foreach (LootBox box in FindObjectsByType<LootBox>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!BindBox(box, out error)) { return false; }
        }
        foreach (GroundItem ground in FindObjectsByType<GroundItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!ground.IsRuntimePresentation && !BindGround(ground, out error)) { return false; }
        }
        RebuildSceneRepresentations();
        error = null;
        return true;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        GameBootstrap root = GetComponentInParent<GameBootstrap>();
        if (root != null && (root != GameBootstrap.Instance || !root.isActiveAndEnabled)) { return; }
        if (PreparedRound > 0 && !BindSceneSources(out string error)) { Debug.LogWarning(error, this); }
    }

    private static string SceneKey(Scene scene) { return string.IsNullOrEmpty(scene.path) ? scene.name : scene.path; }

    private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

    private static Scene FindLoadedScene(string key)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (SceneKey(scene) == key) { return scene; }
        }
        return default;
    }
}
