using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 参考LiDar SceneLoader的加载协程和CardGame SceneLoadManager的分步组织。
// 数据仍归00—06；这里只安排新轮、接线、结算和同一玩家的场景迁移。
[DisallowMultipleComponent]
public class GameFlowManager : MonoBehaviour
{
    public ItemManager items;
    public BackpackManager backpack;
    public WarehouseManager warehouse;
    public ShelterManager shelter;
    public LootRuntimeManager loot;
    public ItemTransferCoordinator transfers;
    public LocalizationManager language;
    public PlayerController playerPrefab;
    public string shelterScenePath = "Assets/Scenes/Shelter.unity";
    public string ruinsScenePath = "Assets/Scenes/Ruins.unity";
    public GameFlowHUD bootstrapHUD;
    public GameObject bootstrapPresentationRoot;

    private PlayerController player;
    private GameFlowSceneBindings currentBindings;
    private GameFlowStage pendingStage = GameFlowStage.Booting;
    private Scene transitionSource;
    private Scene transitionTarget;
    private bool transitionRunning;
    private int departureRound;
    private int settledRound;
    private int settlementRound;
    private List<ItemInstance> carriedSnapshot;
    private List<ItemInstance> leftSnapshot;
    private int retainedCount;
    private int lostCount;
    private string operationError;

    public PlayerController Player { get { return player; } }
    public ItemManager Items { get { return items; } }
    public BackpackManager Backpack { get { return backpack; } }
    public WarehouseManager Warehouse { get { return warehouse; } }
    public ShelterManager Shelter { get { return shelter; } }
    public LootRuntimeManager Loot { get { return loot; } }
    public ItemTransferCoordinator Transfers { get { return transfers; } }
    public LocalizationManager Language { get { return language; } }
    public GameFlowStage Stage { get; private set; } = GameFlowStage.Booting;
    public string FeedbackKey { get; private set; } = "flow.booting";
    public string LastError { get; private set; } = string.Empty;
    public bool CanRetry { get { return Stage == GameFlowStage.Failed && !transitionRunning; } }
    public int CurrentRound { get { return items != null ? items.CurrentRound : 0; } }
    public int SettledRound { get { return settledRound; } }
    public event Action Changed;

    private void Start()
    {
        GameBootstrap root = GetComponentInParent<GameBootstrap>();
        if (root == null || root != GameBootstrap.Instance || !root.isActiveAndEnabled)
        {
            enabled = false;
            return;
        }
        transitionSource = SceneManager.GetActiveScene();
        if (bootstrapHUD != null) { bootstrapHUD.Bind(this, player); }
        StartTransition(GameFlowStage.Booting);
    }

    public bool TryOpenStorage()
    {
        return Stage == GameFlowStage.Shelter && CanStartAction() && currentBindings != null
            && currentBindings.isShelter && currentBindings.TryOpenStorage();
    }

    public bool TryDepart()
    {
        if (Stage != GameFlowStage.Shelter || !CanStartAction() || currentBindings == null
            || !currentBindings.isShelter) { return false; }
        departureRound = 0;
        transitionSource = currentBindings.gameObject.scene;
        transitionTarget = default;
        StartTransition(GameFlowStage.Departing);
        return true;
    }

    public bool TryExtract()
    {
        if (Stage != GameFlowStage.Exploring || !CanStartAction() || currentBindings == null
            || currentBindings.isShelter || CurrentRound <= settledRound || loot == null
            || loot.PreparedRound != CurrentRound || !loot.IsExplorationActive
            || player.health.CurrentRound != CurrentRound) { return false; }
        transitionSource = currentBindings.gameObject.scene;
        transitionTarget = default;
        StartTransition(GameFlowStage.Returning);
        return true;
    }

    public void Retry()
    {
        if (CanRetry) { StartTransition(pendingStage); }
    }

    private bool CanStartAction()
    {
        return !transitionRunning && player != null && player.IsPrepared && !player.IsDead
            && !player.IsSceneTransitionBlocked && transfers != null && !transfers.IsBusy;
    }

    private void StartTransition(GameFlowStage nextStage)
    {
        if (transitionRunning) { return; }
        transitionRunning = true;
        pendingStage = nextStage;
        Stage = nextStage;
        FeedbackKey = nextStage == GameFlowStage.Booting ? "flow.booting"
            : nextStage == GameFlowStage.Departing ? "flow.departing" : "flow.returning";
        LastError = string.Empty;
        // 先独立锁住玩家，再关背包；Close不能解除场景切换锁。
        if (player != null) { player.SetSceneTransitionBlocked(true); }
        if (currentBindings != null) { currentBindings.SuspendBusiness(); }
        if (warehouse != null && transitionSource.IsValid()) { warehouse.SetShelterContext(false, transitionSource); }
        if (loot != null) { loot.SetExplorationContext(loot.ActiveMapId, false); }
        NotifyChanged();
        StartCoroutine(RunTransition());
    }

    private IEnumerator RunTransition()
    {
        if (!TryInitializeModules(out string error)) { Fail(error, "flow.error.dependencies"); yield break; }
        string path = pendingStage == GameFlowStage.Departing ? ruinsScenePath : shelterScenePath;
        if (string.IsNullOrWhiteSpace(path) || shelterScenePath == ruinsScenePath
            || !Application.CanStreamedLevelBeLoaded(path))
        {
            Fail("目标场景未配置、与另一地图重复或未加入Build Settings：" + path, "flow.error.scene");
            yield break;
        }
        if (transitionTarget.IsValid() && transitionTarget.isLoaded && transitionTarget.path != path)
        {
            Fail("本次操作已加载一个目标，请修复该场景接线并恢复原路径后重试，不能叠加另一份地图。", "flow.error.scene");
            yield break;
        }

        if (pendingStage == GameFlowStage.Departing && !TryPrepareDeparture(out error))
        {
            Fail(error, "flow.error.sources");
            yield break;
        }
        yield return LoadTarget(path);
        if (!string.IsNullOrEmpty(operationError)) { Fail(operationError, "flow.error.scene"); yield break; }
        Scene targetScene = SceneManager.GetSceneByPath(path);
        transitionTarget = targetScene;
        if (!TryFindBindings(targetScene, out GameFlowSceneBindings target, out error))
        {
            Fail(error, "flow.error.scene");
            yield break;
        }
        if (target.isShelter != (pendingStage != GameFlowStage.Departing))
        {
            Fail("目标场景的庇护所/探索类型与流程不匹配。", "flow.error.scene");
            yield break;
        }
        if (pendingStage != GameFlowStage.Booting && (player == null || player.IsDead || !player.IsPrepared))
        {
            Fail("加载期间玩家已不可用，不能补血复活或提交成功撤离。", "flow.error.player");
            yield break;
        }

        if (pendingStage == GameFlowStage.Departing)
        {
            // 同轮重试只核对已加载来源；不再BeginRound或重复加载Ruins。
            if (!loot.PrepareRound(departureRound, out error)
                || !ValidateLootSources(targetScene, target.mapId, out error))
            {
                Fail(error, "flow.error.sources");
                yield break;
            }
        }
        if (pendingStage == GameFlowStage.Returning && !TrySettleExtraction(out error))
        {
            Fail(error, "flow.error.settlement");
            yield break;
        }

        if (player == null)
        {
            if (pendingStage != GameFlowStage.Booting) { Fail("运行中的唯一玩家已丢失。", "flow.error.player"); yield break; }
            player = Instantiate(playerPrefab);
            player.name = "Player";
            player.transform.SetParent(null);
            player.SetSceneTransitionBlocked(true);
        }
        // 两张地图可能重叠：玩家冻结期间先停旧环境，再迁移根节点。
        if (currentBindings != null && currentBindings != target) { currentBindings.Deactivate(); }
        if (SceneManager.GetActiveScene() != targetScene && !SceneManager.SetActiveScene(targetScene))
        {
            Fail("无法设置目标活动场景。", "flow.error.scene");
            yield break;
        }
        SceneManager.MoveGameObjectToScene(player.gameObject, targetScene);
        Vector3 spawn = target.spawnPoint.position;
        bool prepared;
        if (pendingStage == GameFlowStage.Booting && !player.IsPrepared)
        {
            prepared = player.PrepareInitialShelter(spawn);
        }
        else if (pendingStage == GameFlowStage.Departing && player.health.CurrentRound < departureRound)
        {
            prepared = player.ResetForExploration(departureRound, spawn);
        }
        else
        {
            // 返回不回血；同轮接线重试也不刷新医疗包或重复Reset。
            prepared = player.RelocateWithinRun(spawn);
        }
        if (!prepared) { Fail("玩家准备或定位被真实玩家模块拒绝。", "flow.error.player"); yield break; }
        transfers.player = player;
        if (bootstrapPresentationRoot != null) { bootstrapPresentationRoot.SetActive(false); }
        if (!target.Activate(this, player, out error, false))
        {
            target.Deactivate();
            Fail(error, "flow.error.scene");
            yield break;
        }
        currentBindings = target;

        yield return UnloadPrevious(targetScene);
        if (!string.IsNullOrEmpty(operationError)) { Fail(operationError, "flow.error.unload"); yield break; }
        bool atShelter = pendingStage != GameFlowStage.Departing;
        warehouse.SetShelterContext(atShelter, targetScene);
        loot.SetExplorationContext(target.mapId, !atShelter);
        target.ResumeBusiness();
        Physics.SyncTransforms();
        // 旧图已卸载、玩家已准备；仍持有切场景锁，敌人启动失败可沿用本轮重试。
        if (!atShelter && !target.TryBeginExplorationEnemies(CurrentRound, out error))
        {
            Fail(error, "flow.error.scene");
            yield break;
        }
        player.SetSceneTransitionBlocked(false);
        transitionRunning = false;
        Stage = atShelter ? GameFlowStage.Shelter : GameFlowStage.Exploring;
        FeedbackKey = atShelter ? "flow.shelter" : "flow.exploring";
        LastError = string.Empty;
        NotifyChanged();
    }

    private bool TryInitializeModules(out string error)
    {
        GameBootstrap root = GameBootstrap.Instance;
        if (root == null || root != GetComponentInParent<GameBootstrap>() || !root.isActiveAndEnabled)
        {
            error = "流程必须属于当前存活的GameBootstrap。";
            return false;
        }
        items = root.GetComponentInChildren<ItemManager>(true);
        backpack = root.GetComponentInChildren<BackpackManager>(true);
        warehouse = root.GetComponentInChildren<WarehouseManager>(true);
        shelter = root.GetComponentInChildren<ShelterManager>(true);
        loot = root.GetComponentInChildren<LootRuntimeManager>(true);
        transfers = root.GetComponentInChildren<ItemTransferCoordinator>(true);
        language = root.GetComponentInChildren<LocalizationManager>(true);
        if (items == null || backpack == null || warehouse == null || shelter == null || loot == null
            || transfers == null || language == null || playerPrefab == null || items.itemLibrary == null
            || !items.isActiveAndEnabled || !loot.isActiveAndEnabled || !language.isActiveAndEnabled)
        {
            error = "正式入口缺少启用的00—06模块、物品目录或真实Player.prefab。";
            return false;
        }
        if (root.GetComponentsInChildren<GameFlowManager>(true).Length != 1
            || root.GetComponentsInChildren<ItemManager>(true).Length != 1
            || root.GetComponentsInChildren<BackpackManager>(true).Length != 1
            || root.GetComponentsInChildren<WarehouseManager>(true).Length != 1
            || root.GetComponentsInChildren<ShelterManager>(true).Length != 1
            || root.GetComponentsInChildren<LootRuntimeManager>(true).Length != 1
            || root.GetComponentsInChildren<ItemTransferCoordinator>(true).Length != 1
            || root.GetComponentsInChildren<LocalizationManager>(true).Length != 1)
        {
            error = "正式入口存在重复的流程或常驻模块。";
            return false;
        }
        if (!items.itemLibrary.TryValidate(out error)) { return false; }
        language.Initialize();
        loot.itemManager = items;
        if (!backpack.Initialize(items) || !warehouse.Initialize(items) || !shelter.Initialize()
            || !transfers.Initialize() || transfers.IsBusy)
        {
            error = "真实背包、仓库、庇护所或转移模块尚未准备完成。";
            return false;
        }
        transfers.player = player;
        if (!loot.ValidateDropPrefab(out error)) { return false; }
        error = null;
        return true;
    }

    private bool TryPrepareDeparture(out string error)
    {
        error = null;
        if (player == null || player.IsDead || !player.IsPrepared)
        {
            error = "只有存活且已准备的玩家可以出发。";
            return false;
        }
        if (departureRound == 0)
        {
            Scene previousRuins = SceneManager.GetSceneByPath(ruinsScenePath);
            if ((previousRuins.IsValid() && previousRuins.isLoaded) || loot.GetRemainingWorldItems().Count != 0
                || CurrentRound != settledRound)
            {
                error = "上一轮尚未完成遗留物结算或旧废墟仍未卸载。";
                return false;
            }
            // RecordLoss需要CurrentRound+1，保留最后一个整数作为恢复轮次。
            if (CurrentRound >= int.MaxValue - 1)
            {
                error = "探索轮次已达到可用上限。";
                return false;
            }
            int nextRound = CurrentRound + 1;
            if (!items.BeginRound(nextRound)) { error = "物品模块拒绝开启新轮。"; return false; }
            departureRound = nextRound;
            carriedSnapshot = null;
            leftSnapshot = null;
            retainedCount = 0;
            lostCount = 0;
            settlementRound = 0;
        }
        if (CurrentRound != departureRound)
        {
            error = "物品轮次被流程以外的调用改变。";
            return false;
        }
        return loot.PrepareRound(departureRound, out error);
    }

    private IEnumerator LoadTarget(string path)
    {
        operationError = null;
        Scene existing = SceneManager.GetSceneByPath(path);
        if (existing.IsValid() && existing.isLoaded) { yield break; }
        AsyncOperation loading = null;
        try { loading = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive); }
        catch (Exception exception) { operationError = exception.Message; }
        if (loading == null)
        {
            operationError = operationError ?? "Unity未启动目标场景加载。";
            yield break;
        }
        while (!loading.isDone) { yield return null; }
        Scene loaded = SceneManager.GetSceneByPath(path);
        if (!loaded.IsValid() || !loaded.isLoaded) { operationError = "目标场景未完成真实加载：" + path; }
    }

    private IEnumerator UnloadPrevious(Scene target)
    {
        operationError = null;
        if (!transitionSource.IsValid() || !transitionSource.isLoaded || transitionSource == target) { yield break; }
        AsyncOperation unloading = null;
        try { unloading = SceneManager.UnloadSceneAsync(transitionSource); }
        catch (Exception exception) { operationError = exception.Message; }
        if (unloading == null)
        {
            operationError = operationError ?? "Unity未启动旧场景卸载。";
            yield break;
        }
        while (!unloading.isDone) { yield return null; }
        if (transitionSource.IsValid() && transitionSource.isLoaded) { operationError = "旧场景仍然存在，保持锁定等待重试。"; }
    }

    private static bool TryFindBindings(Scene scene, out GameFlowSceneBindings bindings, out string error)
    {
        bindings = null;
        if (!scene.IsValid() || !scene.isLoaded) { error = "目标场景不可用。"; return false; }
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (GameFlowSceneBindings candidate in root.GetComponentsInChildren<GameFlowSceneBindings>(true))
            {
                if (bindings != null) { error = "目标场景有重复GameFlowSceneBindings。"; return false; }
                bindings = candidate;
            }
        }
        if (bindings == null) { error = "目标场景缺少GameFlowSceneBindings。"; return false; }
        return bindings.Validate(out error);
    }

    private bool ValidateLootSources(Scene scene, string mapId, out string error)
    {
        int sourceCount = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (LootBox box in root.GetComponentsInChildren<LootBox>(true))
            {
                if (box.mapId != mapId || !loot.TryGetSource(box.SourceKey, out LootSourceState state)
                    || !loot.IsSourceReady(state)) { error = "废墟箱子尚未完成本轮绑定：" + box.name; return false; }
                sourceCount++;
            }
            foreach (GroundItem ground in root.GetComponentsInChildren<GroundItem>(true))
            {
                if (ground.IsRuntimePresentation) { continue; }
                if (ground.mapId != mapId || !loot.TryGetSource(ground.SourceKey, out LootSourceState state)
                    || !loot.IsSourceReady(state)) { error = "废墟地面来源尚未完成本轮绑定：" + ground.name; return false; }
                sourceCount++;
            }
        }
        error = sourceCount == 0 ? "废墟未配置真实搜刮来源。" : null;
        return error == null;
    }

    private bool TrySettleExtraction(out string error)
    {
        error = null;
        if (items == null || backpack == null || loot == null || shelter == null || transfers == null
            || transfers.IsBusy || !backpack.IsReady || !shelter.IsReady || CurrentRound <= 0
            || CurrentRound == int.MaxValue)
        {
            error = "撤离依赖、轮次或转移状态无效。";
            return false;
        }
        if (settledRound == CurrentRound) { return true; }
        if (settledRound > CurrentRound || loot.PreparedRound != CurrentRound)
        {
            error = "撤离轮次与搜刮准备轮次不一致。";
            return false;
        }
        if (settlementRound == 0)
        {
            if (!TryCaptureSettlement(out error)) { return false; }
            settlementRound = CurrentRound;
        }
        if (settlementRound != CurrentRound) { error = "结算期间轮次发生变化。"; return false; }
        // 每成功一项才推进索引。重试从失败项继续，不重复RecordLoss。
        while (retainedCount < carriedSnapshot.Count)
        {
            ItemInstance item = carriedSnapshot[retainedCount];
            if (backpack.TryGetCarriedItem(item.InstanceId, out ItemInstance carried) != BackpackResult.Success
                || !ReferenceEquals(item, carried) || !items.TrySetRetained(item.InstanceId, true))
            {
                error = "带回物品标记失败：" + item.InstanceId;
                return false;
            }
            retainedCount++;
        }
        while (lostCount < leftSnapshot.Count)
        {
            ItemInstance item = leftSnapshot[lostCount];
            if ((item.Owner != ItemOwner.World && item.Owner != ItemOwner.Container)
                || !items.RecordLoss(item.InstanceId, ItemLossReason.LeftBehindOnExtraction))
            {
                error = "本轮遗留物记损失败：" + item.InstanceId;
                return false;
            }
            lostCount++;
        }
        // 全部数据完成后才刷新；标记放在通知之前，监听者不能重复结算。
        settledRound = CurrentRound;
        backpack.NotifyChanged();
        shelter.RefreshEv();
        return true;
    }

    private bool TryCaptureSettlement(out string error)
    {
        List<ItemInstance> carried = new List<ItemInstance>();
        List<ItemInstance> remaining = loot.GetRemainingWorldItems();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (BackpackPlacement placement in backpack.GetPlacements())
        {
            if (placement == null || !seen.Add(placement.InstanceId)
                || backpack.TryGetCarriedItem(placement.InstanceId, out ItemInstance item) != BackpackResult.Success
                || !ReferenceEquals(placement.Item, item) || !ValidateItemIdentity(item))
            {
                error = "背包摆放与01身份/归属不一致，保留原数据等待修复。";
                return false;
            }
            carried.Add(item);
        }
        foreach (ItemInstance item in items.GetInstances())
        {
            if (item.Owner == ItemOwner.Backpack && (item.ContainerId != backpack.ContainerId || !seen.Contains(item.InstanceId)))
            {
                error = "01背包物品没有对应的真实摆放。";
                return false;
            }
        }
        foreach (ItemInstance item in remaining)
        {
            if (item == null || !seen.Add(item.InstanceId) || !ValidateItemIdentity(item)
                || (item.Owner != ItemOwner.World && item.Owner != ItemOwner.Container)
                || item.UseRound != CurrentRound)
            {
                error = "遗留物集合与当前轮次/背包集合冲突。";
                return false;
            }
        }
        carriedSnapshot = carried;
        leftSnapshot = remaining;
        error = null;
        return true;
    }

    private bool ValidateItemIdentity(ItemInstance item)
    {
        return item != null && item.Definition != null && item.Definition.TryValidate(out _)
            && items.TryGetInstance(item.InstanceId, out ItemInstance registered) && ReferenceEquals(registered, item)
            && item.DefinitionId == item.Definition.definitionId
            && item.UniqueId == (item.Definition.isUnique ? item.Definition.uniqueId : string.Empty);
    }

    private void Fail(string error, string feedbackKey)
    {
        transitionRunning = false;
        Stage = GameFlowStage.Failed;
        FeedbackKey = feedbackKey;
        LastError = string.IsNullOrEmpty(error) ? "场景准备失败。" : error;
        if (player != null) { player.SetSceneTransitionBlocked(true); }
        if (currentBindings != null) { currentBindings.SuspendBusiness(); }
        // 首次加载失败或旧场景已停用时，由入口HUD提供同一次操作的重试。
        if (bootstrapPresentationRoot != null
            && (currentBindings == null || currentBindings.presentationRoot == null
                || !currentBindings.presentationRoot.activeInHierarchy))
        {
            bootstrapPresentationRoot.SetActive(true);
            if (bootstrapHUD != null) { bootstrapHUD.Bind(this, player); }
        }
        Debug.LogWarning("GameFlow: " + LastError, this);
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        if (Changed == null) { return; }
        foreach (Action listener in Changed.GetInvocationList())
        {
            try { listener(); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }
}
