using UnityEngine;
using UnityEngine.EventSystems;

// 每张地图只保存少量场景引用；物品、格子、生命和轮次仍归原模块。
[DefaultExecutionOrder(-200)]
public class GameFlowSceneBindings : MonoBehaviour
{
    public bool isShelter;
    public string mapId;
    public Transform spawnPoint;
    public GameObject environmentRoot;
    public GameObject presentationRoot;
    public GameObject businessRoot;
    public PlayerCameraTargetBinder cameraBinder;
    public BackpackUIController backpackUI;
    public WarehouseUIController warehouseUI;
    public LootUIController lootUI;
    public ItemTransferUIBridge transferUI;
    public ShelterView shelterView;
    public GameFlowHUD hud;

    private bool closingStorage;
    private bool subscribed;

    private void Awake()
    {
        // 编辑态开启正式相机用于Game预览；运行时先关闭，等流程接线后再开启。
        // 早于相机Binder执行，避免Additive加载时出现两套相机和输入。
        if (presentationRoot != null) { presentationRoot.SetActive(false); }
    }

    public bool Validate(out string error)
    {
        if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded || spawnPoint == null
            || spawnPoint.gameObject.scene != gameObject.scene || !LootSourceState.IsStableSegment(mapId)
            || environmentRoot == null || presentationRoot == null || businessRoot == null
            || cameraBinder == null || cameraBinder.followCamera == null
            || backpackUI == null || backpackUI.view == null || backpackUI.inputActions == null
            || lootUI == null || lootUI.view == null || transferUI == null || hud == null
            || (isShelter && (warehouseUI == null || warehouseUI.view == null || shelterView == null)))
        {
            error = "场景接线不完整：" + gameObject.scene.path;
            return false;
        }
        if (presentationRoot.GetComponentsInChildren<Camera>(true).Length != 1
            || presentationRoot.GetComponentsInChildren<AudioListener>(true).Length != 1
            || presentationRoot.GetComponentsInChildren<EventSystem>(true).Length != 1)
        {
            error = "场景必须恰好配置一套相机、AudioListener和EventSystem。";
            return false;
        }
        if (cameraBinder.gameObject.scene != gameObject.scene)
        {
            error = "场景相机必须属于当前地图：" + gameObject.scene.path;
            return false;
        }
        if (!cameraBinder.ValidateBounds(out error))
        {
            error = "场景相机边界无效：" + gameObject.scene.path + "；" + error;
            return false;
        }
        error = null;
        return true;
    }

    public bool Activate(GameFlowManager manager, PlayerController player, out string error, bool enableBusiness = true)
    {
        if (!Validate(out error)) { return false; }
        if (manager == null || player == null || manager.Items == null || manager.Backpack == null
            || manager.Warehouse == null || manager.Shelter == null || manager.Loot == null
            || manager.Transfers == null || manager.Language == null || player.gameObject.scene != gameObject.scene)
        {
            error = "当前玩家或常驻业务模块没有准备好。";
            return false;
        }
        backpackUI.manager = manager.Backpack;
        backpackUI.player = player;
        backpackUI.shelterContext = isShelter;
        if (warehouseUI != null)
        {
            warehouseUI.manager = manager.Warehouse;
            warehouseUI.localization = manager.Language;
        }
        transferUI.coordinator = manager.Transfers;
        transferUI.backpack = backpackUI;
        transferUI.warehouse = warehouseUI;
        transferUI.loot = lootUI;
        lootUI.backpack = backpackUI;
        lootUI.runtime = manager.Loot;
        lootUI.transfers = transferUI;
        lootUI.localization = manager.Language;
        if (shelterView != null)
        {
            shelterView.manager = manager.Shelter;
            shelterView.localization = manager.Language;
            shelterView.warehouseUI = warehouseUI;
        }
        cameraBinder.player = player;
        hud.Bind(manager, player);
        hud.gameObject.SetActive(true);
        foreach (GameFlowInteractable target in environmentRoot.GetComponentsInChildren<GameFlowInteractable>(true))
        {
            target.manager = manager;
        }
        foreach (LootBox box in environmentRoot.GetComponentsInChildren<LootBox>(true))
        {
            box.runtimeManager = manager.Loot;
            box.lootUI = lootUI;
        }
        foreach (GroundItem ground in environmentRoot.GetComponentsInChildren<GroundItem>(true))
        {
            ground.runtimeManager = manager.Loot;
            ground.lootUI = lootUI;
        }
        SetEnvironmentEnabled(true);
        businessRoot.SetActive(false);
        // 环境中的边界先启用；表现根OnEnable绑定时已经拥有本场景范围和玩家。
        cameraBinder.Bind(player);
        presentationRoot.GetComponentInChildren<EventSystem>(true).gameObject.SetActive(true);
        presentationRoot.SetActive(true);
        if (enableBusiness) { ResumeBusiness(); }
        error = null;
        return true;
    }

    public void ResumeBusiness()
    {
        if (businessRoot == null || !presentationRoot.activeInHierarchy) { return; }
        businessRoot.SetActive(true);
        backpackUI.BindPlayer(backpackUI.player);
        backpackUI.ResolveBindings();
        backpackUI.SetShelterContext(isShelter);
        if (warehouseUI != null) { warehouseUI.Bind(warehouseUI.manager, warehouseUI.localization); }
        lootUI.Bind();
        transferUI.Bind();
        if (shelterView != null)
        {
            shelterView.Bind(shelterView.manager, shelterView.localization);
            shelterView.Close();
        }
        SubscribeStorage();
    }

    public bool TryBeginExplorationEnemies(int roundId, out string error)
    {
        error = null;
        if (isShelter) { return true; }
        if (roundId <= 0 || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
        {
            error = "探索敌人需要已加载的目标场景和有效轮次。";
            return false;
        }
        // 从本场景根节点找，不要求策划把无人机放进Environment3D，也不启动别的地图。
        var drones = new System.Collections.Generic.List<DroneController>();
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            foreach (DroneController drone in root.GetComponentsInChildren<DroneController>(true))
            {
                if (!drone.isActiveAndEnabled) { continue; }
                if (drone.config == null || drone.CurrentRound > roundId)
                {
                    error = "探索无人机缺少配置或轮次超前：" + drone.name;
                    return false;
                }
                drones.Add(drone);
            }
        }
        foreach (DroneController drone in drones)
        {
            // 同轮包括已自毁的机；不重新启用、不回出生点、不清当前警戒进度。
            if (drone.CurrentRound == roundId) { continue; }
            if (!drone.BeginRound(roundId))
            {
                error = "探索无人机拒绝当前轮次：" + drone.name;
                return false;
            }
        }
        return true;
    }

    public bool TryOpenStorage()
    {
        if (!isShelter || !businessRoot.activeInHierarchy || backpackUI == null || warehouseUI == null) { return false; }
        if (!backpackUI.TryOpen()) { return false; }
        if (!warehouseUI.TryOpen()) { backpackUI.Close(); return false; }
        if (shelterView != null) { shelterView.Open(); }
        return true;
    }

    public void CloseSessions()
    {
        if (lootUI != null) { lootUI.CloseSource(); }
        if (transferUI != null) { transferUI.CancelAll(); }
        if (closingStorage) { return; }
        closingStorage = true;
        try
        {
            if (warehouseUI != null) { warehouseUI.Close(); }
            if (shelterView != null) { shelterView.Close(); }
            if (backpackUI != null) { backpackUI.Close(); }
        }
        finally { closingStorage = false; }
    }

    public void SuspendBusiness()
    {
        CloseSessions();
        UnsubscribeStorage();
        if (businessRoot != null) { businessRoot.SetActive(false); }
    }

    public void SetEnvironmentEnabled(bool enabled)
    {
        if (environmentRoot != null) { environmentRoot.SetActive(enabled); }
    }

    public void Deactivate()
    {
        // 旧UI的OnDisable必须先完成，避免稍后解除新UI给同一玩家设置的限制。
        SuspendBusiness();
        if (cameraBinder != null) { cameraBinder.Bind(null); }
        if (presentationRoot != null) { presentationRoot.SetActive(false); }
        SetEnvironmentEnabled(false);
    }

    private void SubscribeStorage()
    {
        if (subscribed || !isShelter || backpackUI == null || warehouseUI == null) { return; }
        backpackUI.Closing += OnBackpackClosing;
        warehouseUI.Closing += OnWarehouseClosing;
        subscribed = true;
    }

    private void UnsubscribeStorage()
    {
        if (!subscribed) { return; }
        if (backpackUI != null) { backpackUI.Closing -= OnBackpackClosing; }
        if (warehouseUI != null) { warehouseUI.Closing -= OnWarehouseClosing; }
        subscribed = false;
    }

    private void OnBackpackClosing()
    {
        if (closingStorage) { return; }
        closingStorage = true;
        try
        {
            // 通知来自背包时只关闭另一侧，不再调用正在执行的背包Close。
            if (warehouseUI != null) { warehouseUI.Close(); }
            if (shelterView != null) { shelterView.Close(); }
        }
        finally { closingStorage = false; }
    }

    private void OnWarehouseClosing()
    {
        if (closingStorage) { return; }
        closingStorage = true;
        try
        {
            if (backpackUI != null) { backpackUI.Close(); }
            if (shelterView != null) { shelterView.Close(); }
        }
        finally { closingStorage = false; }
    }

    private void OnDisable() { UnsubscribeStorage(); }
}
