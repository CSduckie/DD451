using UnityEngine;

// 只模拟08的显式出发和04的非致命受击关包，不承担正式流程或物品布局。
public class PlayerTestDriver : MonoBehaviour
{
    public PlayerController player;
    public ItemManager itemManager;
    public Transform spawnPoint;
    public bool showPanel = true;
    public Font panelFont;

    private ItemInstance medkit;
    private ItemInstance firstFood;
    private ItemInstance secondFood;
    private PlayerHealth subscribedHealth;
    private string lastResultKey = "tests.player.result.waiting";
    private string resultDefinitionId;
    private string resultItemSuffix;
    private ItemUseResult resultUse;
    private int resultRound;
    private Vector2 scrollPosition;
    private GUIStyle labelStyle;
    private GUIStyle buttonStyle;

    public bool IsReady { get; private set; }
    public string LastResult
    {
        get
        {
            if (lastResultKey == "items.use.result")
            {
                return Format(lastResultKey, GetItemLabel(resultDefinitionId, resultItemSuffix), GetUseResultText(resultUse));
            }
            if (lastResultKey == "tests.player.result.existing_items")
            {
                return Format(lastResultKey, GetItemLabel("medkit", string.Empty), GetItemLabel("food", string.Empty));
            }
            return lastResultKey == "tests.player.result.round_ready"
                ? Format(lastResultKey, resultRound) : Text(lastResultKey);
        }
    }

    private void OnEnable()
    {
        SubscribeHealth();
    }

    private void Start()
    {
        // 重载场景时复用现有入口的真实数据；新场景里重复入口会在帧末销毁。
        GameBootstrap entry = GameBootstrap.Instance;
        if (entry != null)
        {
            itemManager = entry.GetComponentInChildren<ItemManager>(true);
        }
        if (player == null || player.health == null || itemManager == null
            || entry == null || !itemManager.transform.IsChildOf(entry.transform)
            || itemManager.itemLibrary == null)
        {
            Debug.LogError("PlayerTest requires a real Player and ItemModule under GameBootstrap.", this);
            enabled = false;
            return;
        }

        SubscribeHealth();
        if (itemManager.InstanceCount == 0)
        {
            ItemDataSO medkitData = itemManager.itemLibrary.FindDefinition("medkit");
            ItemDataSO foodData = itemManager.itemLibrary.FindDefinition("food");
            itemManager.TryCreateInstance(medkitData, ItemOwner.Backpack, "player-test-backpack",
                "player-test", "player-test/medkit-origin", out medkit);
            itemManager.TryCreateInstance(foodData, ItemOwner.Backpack, "player-test-backpack",
                "player-test", string.Empty, out firstFood);
            itemManager.TryCreateInstance(foodData, ItemOwner.Backpack, "player-test-backpack",
                "player-test", string.Empty, out secondFood);
        }
        else
        {
            foreach (ItemInstance item in itemManager.GetInstances())
            {
                if (item.DefinitionId == "medkit")
                {
                    medkit = item;
                }
                else if (item.DefinitionId == "food")
                {
                    if (firstFood == null)
                    {
                        firstFood = item;
                    }
                    else if (secondFood == null)
                    {
                        secondFood = item;
                    }
                }
            }
        }
        IsReady = medkit != null && firstFood != null && secondFood != null;
        if (!IsReady)
        {
            lastResultKey = "tests.player.result.existing_items";
        }
        // 此处不调用BeginRound/ResetForExploration，也不补充已消耗的食物。
    }

    private void OnDisable()
    {
        if (subscribedHealth != null)
        {
            subscribedHealth.NonLethalDamaged -= OnNonLethalDamaged;
        }
        subscribedHealth = null;
    }

    private void SubscribeHealth()
    {
        PlayerHealth next = player != null ? player.health : null;
        if (subscribedHealth == next)
        {
            return;
        }
        if (subscribedHealth != null)
        {
            subscribedHealth.NonLethalDamaged -= OnNonLethalDamaged;
        }
        subscribedHealth = next;
        if (subscribedHealth != null)
        {
            subscribedHealth.NonLethalDamaged += OnNonLethalDamaged;
        }
    }

    private void OnNonLethalDamaged(int amount)
    {
        player.SetBackpackOpen(false);
        lastResultKey = "tests.player.result.backpack_closed";
    }

    [ContextMenu("测试/准备下一轮")]
    public void PrepareNextRound()
    {
        if (!IsReady || player == null || itemManager == null)
        {
            return;
        }
        int previousRound = Mathf.Max(itemManager.CurrentRound, player.health.CurrentRound);
        if (previousRound == int.MaxValue)
        {
            lastResultKey = "tests.player.result.round_limit";
            return;
        }
        int round = previousRound + 1;
        Vector3 position = spawnPoint != null ? spawnPoint.position : transform.position;
        bool prepared = player.ResetForExploration(round, position);
        bool began = prepared && itemManager.BeginRound(round);
        resultRound = round;
        lastResultKey = began ? "tests.player.result.round_ready" : "tests.player.result.prepare_failed";
    }

    [ContextMenu("测试/开关背包")]
    public void ToggleBackpack()
    {
        if (player != null)
        {
            player.SetBackpackOpen(!player.IsBackpackOpen);
        }
    }

    [ContextMenu("测试/受到1点伤害")]
    public void DamageOne()
    {
        if (player != null && player.health != null)
        {
            bool applied = player.health.TakeDamage(1);
            if (!applied)
            {
                lastResultKey = "tests.player.result.damage_rejected";
            }
        }
    }

    [ContextMenu("测试/受到致命伤害")]
    public void DamageLethal()
    {
        if (player != null && player.health != null)
        {
            lastResultKey = player.health.TakeDamage(player.health.MaxHealth)
                ? "tests.player.result.lethal_damage" : "tests.player.result.damage_rejected";
        }
    }

    [ContextMenu("测试/使用医疗包")]
    public void UseMedkit()
    {
        Use(medkit, string.Empty);
    }
    [ContextMenu("测试/使用食物A")]
    public void UseFirstFood()
    {
        Use(firstFood, "A");
    }
    [ContextMenu("测试/使用食物B")]
    public void UseSecondFood()
    {
        Use(secondFood, "B");
    }
    private void Use(ItemInstance item, string suffix)
    {
        if (IsReady && item != null && player != null)
        {
            // 保存身份和结果，不缓存译文；切语言只重新显示，不会再次使用物品。
            resultUse = itemManager.TryUseItem(item.InstanceId, player.health);
            resultDefinitionId = item.DefinitionId;
            resultItemSuffix = suffix;
            lastResultKey = "items.use.result";
        }
    }

    private void OnGUI()
    {
        if (!showPanel || player == null || player.health == null)
        {
            return;
        }
        PrepareStyles();
        float width = Mathf.Min(560f, Mathf.Max(1f, Screen.width - 24f));
        float height = Mathf.Min(580f, Mathf.Max(1f, Screen.height - 24f));
        GUILayout.BeginArea(new Rect(12f, 12f, width, height), GUI.skin.box);
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, false);
        GUILayout.Label(Text("tests.player.title"), labelStyle);
        GUILayout.Label(Text("tests.player.controls"), labelStyle);
        GUILayout.Label(Text("common.language.toggle_hint"), labelStyle);
        GUILayout.Label(Format("tests.player.status", BoolText(player.IsPrepared), player.health.CurrentHealth,
            player.health.MaxHealth, player.health.CurrentRound, GetStateText(player.CurrentState)), labelStyle);
        GUILayout.Label(Format("tests.player.movement", Text(player.IsBackpackOpen ? "common.open" : "common.closed"),
            BoolText(player.IsGrounded), Text(player.FacingDirection > 0 ? "common.right" : "common.left"),
            player.transform.position.z), labelStyle);
        string promptKey = player.interaction != null ? player.interaction.CurrentPromptKey : string.Empty;
        if (!string.IsNullOrEmpty(promptKey))
        {
            GUILayout.Label(Format("tests.player.interaction", Text(promptKey)), labelStyle);
        }
        // 热重载可保留就绪标记但丢失普通运行对象，绘制前仍检查真实引用。
        if (IsReady && medkit != null && firstFood != null && secondFood != null)
        {
            GUILayout.Label(Format("tests.items.item_usage", GetItemLabel(medkit.DefinitionId, string.Empty),
                GetOwnerText(medkit.Owner), medkit.UsesThisRound), labelStyle);
            GUILayout.Label(Format("tests.items.food_owners", GetItemLabel(firstFood.DefinitionId, "A"),
                GetOwnerText(firstFood.Owner), GetItemLabel(secondFood.DefinitionId, "B"), GetOwnerText(secondFood.Owner)), labelStyle);
            if (GUILayout.Button(Text("tests.player.prepare_round"), buttonStyle)) { PrepareNextRound(); }
            if (GUILayout.Button(Text("tests.player.toggle_backpack"), buttonStyle)) { ToggleBackpack(); }
            bool horizontal = width >= 420f;
            if (horizontal) { GUILayout.BeginHorizontal(); }
            if (GUILayout.Button(Text("tests.damage_one"), buttonStyle)) { DamageOne(); }
            if (GUILayout.Button(Text("tests.player.damage_lethal"), buttonStyle)) { DamageLethal(); }
            if (horizontal) { GUILayout.EndHorizontal(); }
            if (horizontal) { GUILayout.BeginHorizontal(); }
            if (GUILayout.Button(Format("tests.items.use", GetItemLabel(medkit.DefinitionId, string.Empty)), buttonStyle)) { UseMedkit(); }
            if (GUILayout.Button(Format("tests.items.use", GetItemLabel(firstFood.DefinitionId, "A")), buttonStyle)) { UseFirstFood(); }
            if (GUILayout.Button(Format("tests.items.use", GetItemLabel(secondFood.DefinitionId, "B")), buttonStyle)) { UseSecondFood(); }
            if (horizontal) { GUILayout.EndHorizontal(); }
        }
        GUILayout.Label(LastResult, labelStyle);
        GUILayout.Label(Text("tests.player.placeholder_note"), labelStyle);
        GUILayout.Label(Text("tests.player.scope_note"), labelStyle);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void PrepareStyles()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 15 };
            buttonStyle = new GUIStyle(GUI.skin.button) { wordWrap = true, fontSize = 15, fixedHeight = 0f };
        }
        labelStyle.font = panelFont;
        buttonStyle.font = panelFont;
    }

    private static string Text(string key)
    {
        // UI可能来自新场景，始终读取真正存活的Bootstrap下的语言模块。
        LocalizationManager manager = LocalizationManager.Instance;
        return manager != null ? manager.GetText(key) : "[" + key + "]";
    }

    private static string Format(string key, params object[] arguments)
    {
        LocalizationManager manager = LocalizationManager.Instance;
        return manager != null ? manager.FormatText(key, arguments) : "[" + key + "]";
    }

    private static string BoolText(bool value)
    {
        return Text(value ? "common.yes" : "common.no");
    }

    private string GetItemLabel(string definitionId, string suffix)
    {
        ItemDataSO definition = itemManager != null && itemManager.itemLibrary != null
            ? itemManager.itemLibrary.FindDefinition(definitionId) : null;
        string name = definition != null ? Text(definition.nameKey) : "[" + definitionId + "]";
        return string.IsNullOrEmpty(suffix) ? name : Format("tests.items.variant", name, suffix);
    }

    // 仅转换测试面板显示，不修改运行数据中的枚举或标识。
    private static string GetStateText(PlayerState state)
    {
        switch (state)
        {
            case PlayerState.None: return Text("player.state.none");
            case PlayerState.Idle: return Text("player.state.idle");
            case PlayerState.Move: return Text("player.state.move");
            case PlayerState.Jump: return Text("player.state.jump");
            case PlayerState.Fall: return Text("player.state.fall");
            case PlayerState.Crouch: return Text("player.state.crouch");
            case PlayerState.Die: return Text("player.state.die");
            default: return Text("player.state.unknown");
        }
    }

    private static string GetOwnerText(ItemOwner owner)
    {
        switch (owner)
        {
            case ItemOwner.World: return Text("common.item_owner.world");
            case ItemOwner.Container: return Text("common.item_owner.container");
            case ItemOwner.Backpack: return Text("common.item_owner.backpack");
            case ItemOwner.Storage: return Text("common.item_owner.storage");
            case ItemOwner.PendingRecovery: return Text("common.item_owner.pending_recovery");
            case ItemOwner.Consumed: return Text("common.item_owner.consumed");
            case ItemOwner.Lost: return Text("common.item_owner.lost");
            default: return Text("common.item_owner.unknown");
        }
    }

    private static string GetUseResultText(ItemUseResult result)
    {
        switch (result)
        {
            case ItemUseResult.Success: return Text("items.use.success");
            case ItemUseResult.NotFound: return Text("items.use.not_found");
            case ItemUseResult.NotCarried: return Text("items.use.not_carried");
            case ItemUseResult.NotUsable: return Text("items.use.not_usable");
            case ItemUseResult.InvalidTarget: return Text("items.use.invalid_target");
            case ItemUseResult.FullHealth: return Text("items.use.full_health");
            case ItemUseResult.RoundNotStarted: return Text("items.use.round_not_started");
            case ItemUseResult.RoundLimitReached: return Text("items.use.round_limit_reached");
            case ItemUseResult.InvalidConfiguration: return Text("items.use.invalid_configuration");
            case ItemUseResult.NoHealingApplied: return Text("items.use.no_healing_applied");
            case ItemUseResult.Busy: return Text("items.use.busy");
            default: return Text("items.use.unknown");
        }
    }
}
