using UnityEngine;

// 只在ItemsTest场景演示直接调用，不承担正式初始化、输入或背包UI职责。
public class ItemsTestDriver : MonoBehaviour
{
    public ItemManager itemManager;
    public ItemsTestHealth health;
    public ItemLibrarySO itemLibrary;
    public Font panelFont;

    private ItemInstance medkit;
    private ItemInstance firstFood;
    private ItemInstance secondFood;
    private bool isReady;
    private string lastResultKey = "tests.items.result.waiting";
    private string resultDefinitionId;
    private string resultItemSuffix;
    private ItemUseResult resultUse;
    private int resultRound;
    private int resultHealth;
    private int resultMaxHealth;
    private Vector2 scrollPosition;
    private GUIStyle labelStyle;
    private GUIStyle buttonStyle;

    public string LastResult
    {
        get
        {
            if (lastResultKey == "items.use.result")
            {
                return Format(lastResultKey, GetItemLabel(resultDefinitionId, resultItemSuffix), GetUseResultText(resultUse));
            }
            if (lastResultKey == "tests.items.result.damage")
            {
                return Format(lastResultKey, resultHealth, resultMaxHealth);
            }
            return lastResultKey == "tests.items.result.round_ready"
                ? Format(lastResultKey, resultRound) : Text(lastResultKey);
        }
    }

    private void Start()
    {
        GameBootstrap entry = GetComponentInParent<GameBootstrap>();
        if (entry == null || entry != GameBootstrap.Instance || !entry.enabled)
        {
            // 重复场景的入口会在帧末销毁；它的测试驱动不能初始化第二份数据。
            enabled = false;
            return;
        }

        if (itemManager == null || health == null || itemLibrary == null
            || !itemManager.transform.IsChildOf(entry.transform))
        {
            Debug.LogError("ItemsTest requires an ItemModule and test health wired under GameBootstrap.", this);
            enabled = false;
            return;
        }

        ItemDataSO medkitDefinition = itemLibrary.FindDefinition("medkit");
        ItemDataSO foodDefinition = itemLibrary.FindDefinition("food");
        if (medkitDefinition == null || foodDefinition == null)
        {
            Debug.LogError("ItemsTest library must contain medkit and food definitions.", this);
            enabled = false;
            return;
        }

        if (itemManager.InstanceCount == 0)
        {
            itemManager.BeginRound(1);
            itemManager.TryCreateInstance(medkitDefinition, ItemOwner.Backpack, "test-backpack",
                "items-test", "test-medkit-origin", out medkit);
            itemManager.TryCreateInstance(foodDefinition, ItemOwner.Backpack, "test-backpack",
                "items-test", string.Empty, out firstFood);
            itemManager.TryCreateInstance(foodDefinition, ItemOwner.Backpack, "test-backpack",
                "items-test", string.Empty, out secondFood);
        }
        else
        {
            // 重新接线只读取现有实例，不重置轮次、不补充已消耗的食物。
            foreach (ItemInstance instance in itemManager.GetInstances())
            {
                if (instance.DefinitionId == medkitDefinition.definitionId)
                {
                    medkit = instance;
                }
                else if (instance.DefinitionId == foodDefinition.definitionId)
                {
                    if (firstFood == null)
                    {
                        firstFood = instance;
                    }
                    else if (secondFood == null)
                    {
                        secondFood = instance;
                    }
                }
            }
        }

        isReady = medkit != null && firstFood != null && secondFood != null;
        if (!isReady)
        {
            Debug.LogError("ItemsTest could not find one medkit and two independent food instances.", this);
            enabled = false;
            return;
        }

        Debug.Log($"ItemsTest ready: test HP {health.CurrentHealth}/{health.MaxHealth}, "
            + $"round {itemManager.CurrentRound}, {itemManager.InstanceCount} instances. "
            + "Use the test dashboard or ItemsTestDriver context menu. This is not the real player or backpack.", this);
    }

    [ContextMenu("测试/受到1点伤害")]
    public void DamageOne()
    {
        if (!isReady)
        {
            return;
        }

        health.Damage(1);
        resultHealth = health.CurrentHealth;
        resultMaxHealth = health.MaxHealth;
        lastResultKey = "tests.items.result.damage";
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

    [ContextMenu("测试/开始下一轮")]
    public void BeginNextRound()
    {
        if (!isReady)
        {
            return;
        }

        bool began = itemManager.BeginRound(itemManager.CurrentRound + 1);
        resultRound = itemManager.CurrentRound;
        lastResultKey = began ? "tests.items.result.round_ready" : "tests.items.result.round_unchanged";
    }

    private void Use(ItemInstance instance, string suffix)
    {
        if (isReady && instance != null)
        {
            // 只在按钮操作时使用物品；后续显示保存的结果，不重放业务操作。
            resultUse = itemManager.TryUseItem(instance.InstanceId, health);
            resultDefinitionId = instance.DefinitionId;
            resultItemSuffix = suffix;
            lastResultKey = "items.use.result";
        }
    }

    private void OnGUI()
    {
        // 热重载后不能只依赖先前的就绪标记。
        if (!isReady || itemManager == null || health == null
            || medkit == null || firstFood == null || secondFood == null)
        {
            return;
        }

        PrepareStyles();
        float width = Mathf.Min(560f, Mathf.Max(1f, Screen.width - 24f));
        float height = Mathf.Min(530f, Mathf.Max(1f, Screen.height - 24f));
        GUILayout.BeginArea(new Rect(12f, 12f, width, height), GUI.skin.box);
        scrollPosition = GUILayout.BeginScrollView(scrollPosition, false, false);
        GUILayout.Label(Text("tests.items.title"), labelStyle);
        GUILayout.Label(Text("common.language.toggle_hint"), labelStyle);
        GUILayout.Label(Format("tests.items.status", health.CurrentHealth, health.MaxHealth,
            itemManager.CurrentRound, itemManager.InstanceCount), labelStyle);
        GUILayout.Label(Format("tests.items.item_usage", GetItemLabel(medkit.DefinitionId, string.Empty),
            GetOwnerText(medkit.Owner), medkit.UsesThisRound), labelStyle);
        GUILayout.Label(Format("tests.items.food_owners", GetItemLabel(firstFood.DefinitionId, "A"),
            GetOwnerText(firstFood.Owner), GetItemLabel(secondFood.DefinitionId, "B"), GetOwnerText(secondFood.Owner)), labelStyle);
        if (GUILayout.Button(Text("tests.damage_one"), buttonStyle))
        {
            DamageOne();
        }
        if (GUILayout.Button(Format("tests.items.use_medkit", GetItemLabel(medkit.DefinitionId, string.Empty)), buttonStyle))
        {
            UseMedkit();
        }
        bool horizontal = width >= 420f;
        if (horizontal) { GUILayout.BeginHorizontal(); }
        if (GUILayout.Button(Format("tests.items.use", GetItemLabel(firstFood.DefinitionId, "A")), buttonStyle))
        {
            UseFirstFood();
        }
        if (GUILayout.Button(Format("tests.items.use", GetItemLabel(secondFood.DefinitionId, "B")), buttonStyle))
        {
            UseSecondFood();
        }
        if (horizontal) { GUILayout.EndHorizontal(); }
        if (GUILayout.Button(Text("tests.items.next_round"), buttonStyle))
        {
            BeginNextRound();
        }
        GUILayout.Label(LastResult, labelStyle);
        GUILayout.Label(Text("tests.items.scope_note"), labelStyle);
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
        LocalizationManager manager = LocalizationManager.Instance;
        return manager != null ? manager.GetText(key) : "[" + key + "]";
    }

    private static string Format(string key, params object[] arguments)
    {
        LocalizationManager manager = LocalizationManager.Instance;
        return manager != null ? manager.FormatText(key, arguments) : "[" + key + "]";
    }

    private string GetItemLabel(string definitionId, string suffix)
    {
        ItemDataSO definition = itemLibrary != null ? itemLibrary.FindDefinition(definitionId) : null;
        string name = definition != null ? Text(definition.nameKey) : "[" + definitionId + "]";
        return string.IsNullOrEmpty(suffix) ? name : Format("tests.items.variant", name, suffix);
    }

    // 仅转换测试面板显示，不修改运行数据中的枚举或标识。
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
