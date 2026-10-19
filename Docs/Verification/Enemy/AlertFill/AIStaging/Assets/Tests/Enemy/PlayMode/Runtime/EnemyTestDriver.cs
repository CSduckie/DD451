using TMPro;
using UnityEngine;

// 009独立场景启动；玩家准备只在这里发生，敌人轮次按钮不冒充完整探索结算。
public class EnemyTestDriver : MonoBehaviour
{
    public PlayerController player;
    public PlayerCameraTargetBinder cameraBinder;
    public BackpackUIController backpackUI;
    public LootUIController lootUI;
    public ItemTransferCoordinator transfers;
    public ItemTransferUIBridge transferUI;
    public LootBox lootBox;
    public DroneController[] drones = new DroneController[0];
    public TMP_Text statusText;
    public TMP_Text resultText;

    public bool IsReady { get; private set; }
    public int EnemyRound { get; private set; }
    public ItemManager Items { get; private set; }
    public BackpackManager Backpack { get; private set; }
    public LootRuntimeManager Loot { get; private set; }

    private LocalizationManager language;
    private string resultKey = "enemy.test.ready";

    private void Start()
    {
        GameBootstrap root = GameBootstrap.Instance;
        if (root == null || player == null || cameraBinder == null || backpackUI == null || lootUI == null
            || transfers == null || transferUI == null || lootBox == null || drones == null || drones.Length == 0)
        {
            Fail("EnemyTest requires the real player, Bootstrap, Backpack and Loot bindings.");
            return;
        }

        // 场景重载时解析存活入口，不使用重复Bootstrap中即将销毁的模块引用。
        Items = root.GetComponentInChildren<ItemManager>(true);
        Backpack = root.GetComponentInChildren<BackpackManager>(true);
        Loot = root.GetComponentInChildren<LootRuntimeManager>(true);
        language = LocalizationManager.Resolve();
        if (Items == null || Backpack == null || Loot == null || language == null
            || !Backpack.Initialize(Items))
        {
            Fail("EnemyTest could not initialize its existing modules.");
            return;
        }

        bool cleanStart = Items.CurrentRound == 0 && Items.InstanceCount == 0
            && Backpack.GetPlacements().Count == 0;
        if (cleanStart && !Items.BeginRound(1))
        {
            Fail("EnemyTest could not prepare the initial item round.");
            return;
        }
        if (Items.CurrentRound <= 0 || (cleanStart && !SeedBackpack()))
        {
            Fail("EnemyTest requires a valid round and could not prepare its two sample items.");
            return;
        }

        backpackUI.manager = Backpack;
        backpackUI.BindPlayer(player);
        backpackUI.SetShelterContext(false);
        Loot.itemManager = Items;
        lootUI.runtime = Loot;
        lootUI.localization = language;
        lootBox.runtimeManager = Loot;
        lootBox.lootUI = lootUI;
        transfers.player = player;
        string error = null;
        if (!transfers.Initialize() || !Loot.PrepareRound(Items.CurrentRound, out error))
        {
            Fail("EnemyTest transfer or loot initialization failed: " + error);
            return;
        }
        Loot.SetExplorationContext("enemy-test", true);
        transferUI.Bind();
        if (!player.IsPrepared && !player.ResetForExploration(Items.CurrentRound, player.transform.position))
        {
            Fail("EnemyTest could not prepare the real player.");
            return;
        }
        cameraBinder.Bind(player);
        if (!cameraBinder.ValidateBounds(out string boundsError))
        {
            Fail("EnemyTest camera bounds are not ready: " + boundsError);
            return;
        }
        BoxCollider trigger = lootBox.interactionTrigger;
        // 这里只诊断真实引用缺失/错接；临时停用范围不是自动补配置或抢先激活的理由。
        if (trigger == null || !trigger.isTrigger || trigger.GetComponentInParent<Interactable>(true) != lootBox)
        {
            Fail("EnemyTest requires the real LootBox interactionTrigger upgraded by 07C.");
            return;
        }

        EnemyRound = 1;
        foreach (DroneController drone in drones)
        {
            if (drone != null) { EnemyRound = Mathf.Max(EnemyRound, drone.CurrentRound); }
        }
        foreach (DroneController drone in drones)
        {
            if (drone != null) { drone.BeginRound(EnemyRound); }
        }
        IsReady = true;
        language.LanguageChanged += RefreshText;
        RefreshText();
    }

    private bool SeedBackpack()
    {
        ItemDataSO food = Items.itemLibrary != null ? Items.itemLibrary.FindDefinition("food") : null;
        ItemDataSO battery = Items.itemLibrary != null ? Items.itemLibrary.FindDefinition("battery") : null;
        if (food == null || battery == null || !food.TryValidate(out _) || !battery.TryValidate(out _)
            || food.isUnique || battery.isUnique || food.width != 2 || food.height != 2
            || battery.width != 1 || battery.height != 2 || Backpack.Width < 3 || Backpack.Height < 2)
        {
            return false;
        }
        if (!AddSample(food, 0, 0) || !AddSample(battery, 2, 0)) { return false; }
        Backpack.NotifyChanged();
        return true;
    }

    private bool AddSample(ItemDataSO definition, int x, int y)
    {
        return Items.TryCreateInstance(definition, ItemOwner.Backpack, Backpack.ContainerId,
            "enemy-test-seed", "enemy-test/seed/" + definition.definitionId, out ItemInstance item)
            && Backpack.TryAddPlacement(item.InstanceId, x, y, false) == BackpackResult.Success;
    }

    public void RepeatEnemyRound()
    {
        if (!IsReady) { return; }
        foreach (DroneController drone in drones)
        {
            if (drone != null) { drone.BeginRound(EnemyRound); }
        }
        resultKey = "enemy.test.same_round_done";
        RefreshText();
    }

    public void BeginNextEnemyRound()
    {
        if (!IsReady || EnemyRound == int.MaxValue) { return; }
        EnemyRound++;
        foreach (DroneController drone in drones)
        {
            if (drone != null && !drone.BeginRound(EnemyRound))
            {
                Fail("EnemyTest drone rejected the explicit next enemy round: " + drone.name);
                return;
            }
        }
        // 此按钮只验证009入口：不回血，不刷新背包物品，也不处理010的死亡流程。
        resultKey = "enemy.test.next_round_done";
        RefreshText();
    }

    private void Update()
    {
        if (IsReady) { RefreshText(); }
    }

    private void RefreshText()
    {
        if (language == null || player == null || player.health == null) { return; }
        if (statusText != null)
        {
            string crouch = language.GetText(player.IsCrouching ? "enemy.test.crouching" : "enemy.test.standing");
            string backpack = language.GetText(backpackUI.IsOpen ? "enemy.test.open" : "enemy.test.closed");
            string status = language.FormatText("enemy.test.status", EnemyRound,
                player.health.CurrentHealth, player.health.MaxHealth, crouch, backpack);
            for (int i = 0; i < drones.Length; i++)
            {
                DroneController drone = drones[i];
                string state = language.GetText("enemy.state." + (drone != null ? drone.CurrentState.ToString().ToLowerInvariant() : "none"));
                if (drone != null && drone.CurrentState == DroneState.Alert)
                {
                    state += " " + Mathf.FloorToInt(drone.AlertProgress * 100f) + "%";
                }
                status += language.FormatText("enemy.test.drone_status", i + 1, state);
            }
            statusText.text = status;
        }
        if (resultText != null) { resultText.text = language.GetText(resultKey); }
    }

    private void Fail(string message)
    {
        Debug.LogError(message, this);
        IsReady = false;
        resultKey = "enemy.test.failed";
        RefreshText();
        enabled = false;
    }

    private void OnDisable()
    {
        if (language != null) { language.LanguageChanged -= RefreshText; }
        language = null;
        if (backpackUI != null) { backpackUI.Close(); }
        if (Loot != null) { Loot.SetExplorationContext("enemy-test", false); }
    }
}
