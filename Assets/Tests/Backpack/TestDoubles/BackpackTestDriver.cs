using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// 只在04白盒里模拟08的首次准备与真实伤害；开关、拖拽和使用始终交给04控制器。
public class BackpackTestDriver : MonoBehaviour
{
    public PlayerController player;
    public ItemManager itemManager;
    public BackpackManager backpackManager;
    public BackpackUIController controller;
    public Transform movingObject;
    public TMP_Text statusText;
    public TMP_Text contextText;
    public TMP_Text resultText;
    public GameObject closedHintsRoot;

    private Vector3 movingOrigin;
    private float elapsedSeconds;
    private string resultKey = "backpack.test.sample";
    private LocalizationManager subscribedLanguage;

    public bool IsReady { get; private set; }
    public float ElapsedSeconds { get { return elapsedSeconds; } }
    public ItemInstance ExternalItem { get; private set; }

    private void Start()
    {
        GameBootstrap entry = GameBootstrap.Instance;
        if (entry != null)
        {
            itemManager = entry.GetComponentInChildren<ItemManager>(true);
            backpackManager = entry.GetComponentInChildren<BackpackManager>(true);
        }
        if (entry == null || player == null || controller == null || itemManager == null
            || backpackManager == null || !backpackManager.Initialize(itemManager))
        {
            Debug.LogError("BackpackTest requires the real Bootstrap, Items, Player and Backpack modules.", this);
            enabled = false;
            return;
        }

        controller.manager = backpackManager;
        controller.BindPlayer(player);
        // 仅完全干净的数据可以生成样本。重载不补发食物，也不接管其他测试的containerId。
        if (itemManager.InstanceCount == 0 && backpackManager.GetPlacements().Count == 0)
        {
            if (!SeedCleanData())
            {
                Debug.LogError("BackpackTest sample configuration failed; existing instances were preserved.", this);
                enabled = false;
                return;
            }
            if (itemManager.CurrentRound == 0) { itemManager.BeginRound(1); }
        }
        foreach (ItemInstance item in itemManager.GetInstances())
        {
            if (item.SourceId == "backpack-test" && item.DefinitionId == "doll"
                && item.Owner == ItemOwner.World)
            {
                ExternalItem = item;
            }
        }
        // 新场景中的新玩家准备为当前轮次；ItemManager没有BeginRound，所以次数不会被刷新。
        if (!player.IsPrepared && !player.IsDead && itemManager.CurrentRound > 0)
        {
            player.ResetForExploration(itemManager.CurrentRound, player.transform.position);
        }
        movingOrigin = movingObject != null ? movingObject.position : Vector3.zero;
        IsReady = true;
        RefreshText();
    }

    private bool SeedCleanData()
    {
        if (itemManager.itemLibrary == null || backpackManager.Width < 4 || backpackManager.Height < 4) { return false; }
        string[] definitions = { "medkit", "food", "food", "battery", "doll" };
        int[] x = { 0, 2, 0, 2 };
        int[] y = { 0, 0, 2, 2 };
        // 先验证全部配置，避免缺少依赖时生成半份测试样本。
        for (int i = 0; i < definitions.Length; i++)
        {
            ItemDataSO definition = itemManager.itemLibrary.FindDefinition(definitions[i]);
            if (definition == null || !definition.TryValidate(out _)) { return false; }
            int expectedWidth = definitions[i] == "battery" ? 1 : 2;
            int expectedHeight = definitions[i] == "doll" ? 3 : 2;
            if (definition.width != expectedWidth || definition.height != expectedHeight) { return false; }
        }
        for (int i = 0; i < definitions.Length; i++)
        {
            ItemDataSO definition = itemManager.itemLibrary.FindDefinition(definitions[i]);
            ItemOwner owner = i < 4 ? ItemOwner.Backpack : ItemOwner.World;
            string container = i < 4 ? backpackManager.ContainerId : string.Empty;
            if (!itemManager.TryCreateInstance(definition, owner, container, "backpack-test",
                "backpack-test/" + definitions[i] + "-origin", out ItemInstance instance)) { return false; }
            if (i < 4 && backpackManager.TryAddPlacement(instance.InstanceId, x[i], y[i], false)
                != BackpackResult.Success) { return false; }
        }
        backpackManager.NotifyChanged();
        return true;
    }

    private void Update()
    {
        if (!IsReady) { return; }
        if (closedHintsRoot != null) { closedHintsRoot.SetActive(!controller.IsOpen); }
        elapsedSeconds += Time.deltaTime;
        if (movingObject != null)
        {
            movingObject.position = movingOrigin + new Vector3(Mathf.Sin(elapsedSeconds) * 1.5f, 0f, 0f);
            movingObject.Rotate(0f, 60f * Time.deltaTime, 0f, Space.World);
        }
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.hKey.wasPressedThisFrame) { DamageOne(); }
            if (keyboard.kKey.wasPressedThisFrame) { DamageLethal(); }
            if (keyboard.vKey.wasPressedThisFrame) { PreviewExternal(); }
            if (keyboard.escapeKey.wasPressedThisFrame) { CancelPreview(); }
            if (keyboard.tKey.wasPressedThisFrame) { ToggleShelterContext(); }
        }
        RefreshText();
    }

    public void DamageOne()
    {
        if (player == null || player.health == null) { return; }
        resultKey = player.health.TakeDamage(1) ? "backpack.test.damage" : "backpack.test.damage_rejected";
    }

    public void DamageLethal()
    {
        if (player == null || player.health == null) { return; }
        resultKey = player.health.TakeDamage(player.health.MaxHealth)
            ? "backpack.test.lethal" : "backpack.test.damage_rejected";
    }

    public void PreviewExternal()
    {
        if (controller == null) { return; }
        bool opened = controller.IsOpen || controller.TryOpen();
        resultKey = opened && ExternalItem != null && controller.BeginExternalPreview(ExternalItem.InstanceId)
            ? "backpack.test.external_preview" : "backpack.test.external_unavailable";
    }

    public void CancelPreview()
    {
        if (controller != null) { controller.CancelDrag(); }
        resultKey = "backpack.test.cancelled";
    }

    public void ToggleShelterContext()
    {
        if (controller == null) { return; }
        controller.Close();
        controller.SetShelterContext(!controller.shelterContext);
    }

    private void RefreshText()
    {
        LocalizationManager language = LocalizationManager.Instance;
        if (subscribedLanguage != language)
        {
            if (subscribedLanguage != null) { subscribedLanguage.LanguageChanged -= RefreshText; }
            subscribedLanguage = language;
            if (language != null) { language.LanguageChanged += RefreshText; }
        }
        if (language == null || player == null || player.health == null) { return; }
        if (statusText != null)
        {
            statusText.text = language.FormatText("backpack.test.status", elapsedSeconds,
                player.health.CurrentHealth, player.health.MaxHealth, itemManager.CurrentRound);
        }
        if (contextText != null)
        {
            contextText.text = language.GetText(controller.shelterContext
                ? "backpack.test.context.shelter" : "backpack.test.context.exploration");
        }
        if (resultText != null) { resultText.text = language.GetText(resultKey); }
    }

    private void OnDisable()
    {
        if (subscribedLanguage != null) { subscribedLanguage.LanguageChanged -= RefreshText; }
        subscribedLanguage = null;
    }
}
