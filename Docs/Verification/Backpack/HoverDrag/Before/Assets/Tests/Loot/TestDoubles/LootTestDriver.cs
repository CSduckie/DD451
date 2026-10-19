using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// 专用06白盒：明确模拟08的上下文、结算与轮次；不替代04/05的容器和输入。
public class LootTestDriver : MonoBehaviour
{
    public PlayerController player;
    public ItemManager items;
    public BackpackManager backpack;
    public WarehouseManager warehouse;
    public ShelterManager shelter;
    public LootRuntimeManager runtime;
    public BackpackUIController backpackUI;
    public WarehouseUIController warehouseUI;
    public LootUIController lootUI;
    public ItemTransferCoordinator coordinator;
    public TMP_Text statusText;
    public TMP_Text contextText;
    public Transform movingObject;

    public bool IsReady { get; private set; }
    public bool IsShelter { get; private set; }
    public float ElapsedSeconds { get; private set; }
    private Vector3 movingOrigin;
    private LocalizationManager language;

    private void Start()
    {
        GameBootstrap bootstrap = GameBootstrap.Instance;
        if (bootstrap == null || player == null || backpackUI == null || warehouseUI == null || lootUI == null || coordinator == null)
        {
            Fail("LootTest requires real 01—06 modules and the real player.");
            return;
        }
        // 重载必须重新解析存活模块，场景Prefab的重复Bootstrap会被00销毁。
        items = bootstrap.GetComponentInChildren<ItemManager>(true);
        backpack = bootstrap.GetComponentInChildren<BackpackManager>(true);
        warehouse = bootstrap.GetComponentInChildren<WarehouseManager>(true);
        shelter = bootstrap.GetComponentInChildren<ShelterManager>(true);
        runtime = bootstrap.GetComponentInChildren<LootRuntimeManager>(true);
        language = LocalizationManager.Resolve();
        if (items == null || backpack == null || warehouse == null || shelter == null || runtime == null
            || !backpack.Initialize(items) || !warehouse.Initialize(items) || !shelter.Initialize())
        {
            Fail("LootTest module initialization failed.");
            return;
        }
        backpackUI.manager = backpack;
        backpackUI.BindPlayer(player);
        warehouseUI.Bind(warehouse, language);
        lootUI.runtime = runtime;
        lootUI.localization = language;
        coordinator.itemManager = items;
        coordinator.backpackManager = backpack;
        coordinator.warehouseManager = warehouse;
        coordinator.shelterManager = shelter;
        coordinator.lootRuntime = runtime;
        coordinator.player = player;
        runtime.itemManager = items;
        if (!coordinator.Initialize()) { Fail("LootTest transfer binding failed."); return; }

        if (items.InstanceCount == 0 && backpack.GetPlacements().Count == 0 && warehouse.GetPlacements().Count == 0)
        {
            if (items.CurrentRound == 0 && !items.BeginRound(1)) { Fail("Cannot begin the explicit test round."); return; }
            SeedCleanData();
        }
        if (items.CurrentRound <= 0) { Fail("Existing items have no prepared round; test driver will not take over unrelated data."); return; }
        foreach (LootBox box in FindObjectsByType<LootBox>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            box.runtimeManager = runtime;
            box.lootUI = lootUI;
            if (!runtime.BindBox(box, out string error)) { Fail(error); return; }
        }
        foreach (GroundItem ground in FindObjectsByType<GroundItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            ground.runtimeManager = runtime;
            ground.lootUI = lootUI;
            // 同轮重载会先从存活记录补建丢地表现；它已绑定同一实例，不能再当作者原点生成。
            if (ground.IsRuntimePresentation) { continue; }
            if (!runtime.BindGround(ground, out string error)) { Fail(error); return; }
        }
        if (!runtime.PrepareRound(items.CurrentRound, out string roundError)) { Fail(roundError); return; }
        if (!player.IsPrepared && !player.ResetForExploration(items.CurrentRound, new Vector3(-8, 0.05f, 0)))
        {
            Fail("Real player preparation failed.");
            return;
        }
        movingOrigin = movingObject != null ? movingObject.position : Vector3.zero;
        IsReady = true;
        EnterExploration();
        shelter.RefreshEv();
        if (language != null) { language.LanguageChanged += RefreshText; }
        RefreshText();
    }

    private void SeedCleanData()
    {
        // 初始已保有医疗包的原点仍使用场景内真实地面来源。以后找回不会凭空换身份。
        AddSample("medkit", ItemOwner.Backpack, 0, 0, true, "loot-test/medical-origin/item");
        AddSample("food", ItemOwner.Backpack, 2, 0, false, "loot-test/test-seed/food");
        AddSample("battery", ItemOwner.Backpack, 0, 2, false, "loot-test/test-seed/battery");
        AddSample("food", ItemOwner.Storage, 0, 0, true, "loot-test/test-seed/stored-food");
        AddSample("battery", ItemOwner.Storage, 3, 0, true, "loot-test/test-seed/stored-battery");
        backpack.NotifyChanged();
        warehouse.NotifyChanged();
    }

    private void AddSample(string definitionId, ItemOwner owner, int x, int y, bool retained, string origin)
    {
        ItemDataSO definition = items.itemLibrary.FindDefinition(definitionId);
        string container = owner == ItemOwner.Backpack ? backpack.ContainerId : warehouse.ContainerId;
        if (!items.TryCreateInstance(definition, owner, container, "loot-test-seed", origin, out ItemInstance item))
        {
            throw new InvalidOperationException("Cannot seed real LootTest item: " + definitionId);
        }
        bool placed = owner == ItemOwner.Backpack
            ? backpack.TryAddPlacement(item.InstanceId, x, y, false) == BackpackResult.Success
            : warehouse.TryAddPlacement(item.InstanceId, x, y, false) == WarehouseResult.Success;
        if (!placed || !items.TrySetRetained(item.InstanceId, retained))
        {
            throw new InvalidOperationException("Cannot place real LootTest item: " + definitionId);
        }
    }

    private void Update()
    {
        if (!IsReady) { return; }
        ElapsedSeconds += Time.deltaTime;
        if (movingObject != null)
        {
            movingObject.position = movingOrigin + Vector3.right * Mathf.Sin(ElapsedSeconds);
            movingObject.Rotate(0, 60 * Time.deltaTime, 0, Space.World);
        }
        // 测试专用H/K/T不接管E、Tab、R、F10或右键。
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.hKey.wasPressedThisFrame) { DamageOne(); }
            if (keyboard.kKey.wasPressedThisFrame) { DamageLethal(); }
            if (keyboard.tKey.wasPressedThisFrame)
            {
                if (IsShelter) { EnterExploration(); } else { EnterShelter(); }
            }
        }
        RefreshText();
    }

    public void EnterShelter()
    {
        if (!IsReady) { return; }
        backpackUI.Close();
        warehouseUI.Close();
        runtime.SetExplorationContext("loot-test", false);
        warehouse.SetShelterContext(true, gameObject.scene);
        backpackUI.SetShelterContext(true);
        IsShelter = true;
        backpackUI.TryOpen();
        warehouseUI.TryOpen();
        RefreshText();
    }

    public void EnterExploration()
    {
        if (!IsReady) { return; }
        backpackUI.Close();
        warehouseUI.Close();
        warehouse.SetShelterContext(false, gameObject.scene);
        backpackUI.SetShelterContext(false);
        runtime.SetExplorationContext("loot-test", true);
        IsShelter = false;
        RefreshText();
    }

    public void SimulateNextRound()
    {
        if (!IsReady) { return; }
        backpackUI.Close();
        warehouseUI.Close();
        // 这是显式测试结算，场景卸载本身从不记损。死亡时携带物记损，活着则模拟撤离保有。
        foreach (BackpackPlacement placement in backpack.GetPlacements())
        {
            if (player.IsDead)
            {
                if (!items.RecordLoss(placement.InstanceId, ItemLossReason.ExplorationDeath)
                    || backpack.TryRemovePlacement(placement.InstanceId, out _) != BackpackResult.Success)
                {
                    Fail("Simulated death settlement failed.");
                    return;
                }
            }
            else if (!items.TrySetRetained(placement.InstanceId, true)) { Fail("Simulated extraction failed."); return; }
        }
        foreach (ItemInstance item in runtime.GetRemainingWorldItems())
        {
            if (!items.RecordLoss(item.InstanceId, ItemLossReason.LeftBehindOnExtraction))
            {
                Fail("Simulated leftover settlement failed.");
                return;
            }
        }
        if (!items.BeginRound(items.CurrentRound + 1) || !runtime.PrepareRound(items.CurrentRound, out string error))
        {
            Fail("Simulated next-round preparation failed.");
            return;
        }
        player.ResetForExploration(items.CurrentRound, new Vector3(-8, 0.05f, 0));
        backpack.NotifyChanged();
        shelter.RefreshEv();
        EnterExploration();
    }

    public void DamageOne()
    {
        if (IsReady && player.health != null) { player.health.TakeDamage(1); }
    }

    public void DamageLethal()
    {
        if (IsReady && player.health != null) { player.health.TakeDamage(player.health.MaxHealth); }
    }

    private void RefreshText()
    {
        if (language == null || !IsReady || player == null || player.health == null) { return; }
        if (statusText != null)
        {
            statusText.text = language.FormatText("loot.test.status", items.CurrentRound, player.health.CurrentHealth,
                player.health.MaxHealth, shelter.CurrentEv, ElapsedSeconds);
        }
        if (contextText != null)
        {
            string prompt = !IsShelter && player.interaction.CurrentTarget != null
                ? "  " + language.GetText(player.interaction.CurrentPromptKey) : string.Empty;
            contextText.text = language.GetText(IsShelter ? "loot.test.shelter" : "loot.test.exploration") + prompt;
        }
    }

    private void Fail(string error)
    {
        Debug.LogError("LootTest: " + error, this);
        IsReady = false;
        enabled = false;
    }

    private void OnDisable()
    {
        if (language != null) { language.LanguageChanged -= RefreshText; }
        if (backpackUI != null) { backpackUI.Close(); }
        if (warehouseUI != null) { warehouseUI.Close(); }
        if (warehouse != null) { warehouse.SetShelterContext(false, gameObject.scene); }
    }
}
