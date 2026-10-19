using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class LocalizationManager : MonoBehaviour
{
    [Header("文本配置（物品名称和描述只读原物品目录）")]
    public List<LocalizationTableSO> tables = new List<LocalizationTableSO>();
    public ItemLibrarySO itemLibrary;
    [Header("现有 Player/ToggleLanguage 动作")]
    public InputActionReference toggleLanguageAction;

    private static LocalizationManager instance;
    private static int playSession;
    private readonly Dictionary<string, LocalizationEntry> textByKey = new Dictionary<string, LocalizationEntry>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> sourceByKey = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly HashSet<string> reportedProblems = new HashSet<string>(StringComparer.Ordinal);
    private InputAction runtimeToggleAction;
    private int initializedSession = -1;
    private GameLanguage currentLanguage = GameLanguage.Chinese;
    private bool isNotifying;

    public static LocalizationManager Instance { get { return Resolve(); } }
    public GameLanguage CurrentLanguage
    {
        get
        {
            Initialize();
            return currentLanguage;
        }
    }
    public event Action LanguageChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticReference()
    {
        instance = null;
        // Domain Reload 关闭时，实例中的初始化标记也必须区别于上一轮 Play。
        playSession++;
    }

    public static bool TryGetActive(out LocalizationManager manager)
    {
        manager = Resolve();
        return manager != null;
    }

    public static LocalizationManager Resolve(LocalizationManager preferred = null)
    {
        GameBootstrap bootstrap = GameBootstrap.Instance;
        if (bootstrap != null && bootstrap.isActiveAndEnabled)
        {
            if (instance != null && instance.isActiveAndEnabled
                && instance.GetComponentInParent<GameBootstrap>() == bootstrap)
            {
                instance.Initialize();
                return instance;
            }

            // 不信任新场景保存的引用：它可能属于本帧末尾才销毁的重复入口。
            foreach (LocalizationManager candidate in bootstrap.GetComponentsInChildren<LocalizationManager>(true))
            {
                if (candidate.isActiveAndEnabled && candidate.GetComponentInParent<GameBootstrap>() == bootstrap)
                {
                    instance = candidate;
                    candidate.Initialize();
                    return candidate;
                }
            }
        }

        instance = null;
        // 编辑模式下允许 Inspector/查询测试直接使用明确指定的组件，不创建全局入口。
        if (!Application.isPlaying && preferred != null)
        {
            preferred.Initialize();
            return preferred;
        }
        return null;
    }

    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        Initialize();
        TryEnableInput();
    }

    private void Start()
    {
        // Bootstrap 和界面 Awake 的顺序没有约定；Start 再核对真实入口。
        TryEnableInput();
    }

    public void Initialize()
    {
        if (initializedSession == playSession)
        {
            return;
        }

        ReleaseInput();
        initializedSession = playSession;
        currentLanguage = GameLanguage.Chinese;
        isNotifying = false;
        textByKey.Clear();
        sourceByKey.Clear();
        reportedProblems.Clear();
        BuildDictionary();
    }

    private void BuildDictionary()
    {
        if (tables != null)
        {
            for (int i = 0; i < tables.Count; i++)
            {
                LocalizationTableSO table = tables[i];
                if (table == null || table.entries == null)
                {
                    ReportOnce($"table:{i}", $"Localization：语言表第 {i + 1} 项为空。");
                    continue;
                }
                for (int j = 0; j < table.entries.Count; j++)
                {
                    LocalizationEntry entry = table.entries[j];
                    string source = $"语言表 '{table.name}' 第 {j + 1} 项";
                    AddText(entry, source);
                }
            }
        }

        if (itemLibrary == null || itemLibrary.itemLibraryList == null)
        {
            return;
        }
        foreach (ItemLibraryEntry libraryEntry in itemLibrary.itemLibraryList)
        {
            ItemDataSO item = libraryEntry.itemData;
            if (item == null)
            {
                ReportOnce("null-item", $"Localization：物品目录 '{itemLibrary.name}' 有空物品引用。");
                continue;
            }
            string source = $"物品 '{item.name}' ({item.definitionId})，目录 '{itemLibrary.name}'";
            AddText(new LocalizationEntry { key = item.nameKey, chinese = item.nameZh, english = item.nameEn }, source + " nameKey");
            AddText(new LocalizationEntry { key = item.descriptionKey, chinese = item.descriptionZh, english = item.descriptionEn }, source + " descriptionKey");
        }
    }

    private void AddText(LocalizationEntry entry, string source)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.key))
        {
            ReportOnce("empty:" + source, $"Localization：{source} 缺少稳定 key。");
            return;
        }
        if (textByKey.ContainsKey(entry.key))
        {
            ReportOnce("duplicate:" + entry.key, $"Localization：重复 key '{entry.key}'。保留 {sourceByKey[entry.key]}；拒绝 {source}。");
            return;
        }
        textByKey.Add(entry.key, entry);
        sourceByKey.Add(entry.key, source);
    }

    public string GetText(string key)
    {
        Initialize();
        if (string.IsNullOrWhiteSpace(key) || !textByKey.TryGetValue(key, out LocalizationEntry entry))
        {
            ReportOnce("missing:" + key, $"Localization：找不到 key '{key ?? "null"}'，请检查 '{name}' 的语言表和物品目录。");
            return "[" + (key ?? "null") + "]";
        }

        string text = currentLanguage == GameLanguage.Chinese ? entry.chinese : entry.english;
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        ReportOnce($"translation:{currentLanguage}:{key}", $"Localization：{sourceByKey[key]} 的 key '{key}' 缺少 {currentLanguage} 译文，尝试另一种语言。");
        string fallback = currentLanguage == GameLanguage.Chinese ? entry.english : entry.chinese;
        return string.IsNullOrWhiteSpace(fallback) ? "[" + key + "]" : fallback;
    }

    public string FormatText(string key, params object[] args)
    {
        string format = GetText(key);
        try
        {
            return string.Format(format, args ?? Array.Empty<object>());
        }
        catch (FormatException exception)
        {
            string source = key != null && sourceByKey.TryGetValue(key, out string foundSource) ? foundSource : name;
            ReportOnce($"format:{currentLanguage}:{key}", $"Localization：{source} 的 key '{key}' ({currentLanguage}) 占位符或参数错误：{exception.Message}");
            return "[" + (key ?? "null") + "]";
        }
    }

    public void SetLanguage(GameLanguage language)
    {
        Initialize();
        if (language != GameLanguage.Chinese && language != GameLanguage.English)
        {
            ReportOnce("language:" + (int)language, $"Localization：不支持语言值 {(int)language}。");
            return;
        }
        if (currentLanguage == language || isNotifying)
        {
            return;
        }

        currentLanguage = language;
        isNotifying = true;
        try
        {
            if (LanguageChanged != null)
            {
                foreach (Action listener in LanguageChanged.GetInvocationList())
                {
                    try
                    {
                        listener();
                    }
                    catch (Exception exception)
                    {
                        // 一个界面出错不能阻止其他界面刷新，更不能打断当前玩法调用。
                        ReportOnce("listener:" + listener.Method, $"Localization：语言刷新回调 '{listener.Method.Name}' 失败：{exception}");
                    }
                }
            }
        }
        finally
        {
            isNotifying = false;
        }
    }

    public void ToggleLanguage()
    {
        SetLanguage(CurrentLanguage == GameLanguage.Chinese ? GameLanguage.English : GameLanguage.Chinese);
    }

    private void TryEnableInput()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || Resolve() != this || runtimeToggleAction != null)
        {
            return;
        }
        if (toggleLanguageAction == null || toggleLanguageAction.action == null)
        {
            ReportOnce("input", "Localization：请引用现有 Player/ToggleLanguage 输入动作。");
            return;
        }
        // 沿用 PlayerInputReader 的 Clone 所有权，只启停自己的一项动作。
        runtimeToggleAction = toggleLanguageAction.action.Clone();
        runtimeToggleAction.bindingMask = InputBinding.MaskByGroup("Keyboard&Mouse");
        runtimeToggleAction.performed += OnToggleLanguage;
        runtimeToggleAction.Enable();
    }

    private void OnToggleLanguage(InputAction.CallbackContext context)
    {
        // 原动作使用 Press(behavior=0)，按住不会再次 performed。
        if (Resolve() == this)
        {
            ToggleLanguage();
        }
    }

    private void ReleaseInput()
    {
        if (runtimeToggleAction == null)
        {
            return;
        }
        runtimeToggleAction.performed -= OnToggleLanguage;
        runtimeToggleAction.Dispose();
        runtimeToggleAction = null;
    }

    private void OnDisable()
    {
        ReleaseInput();
        if (instance == this)
        {
            instance = null;
        }
    }

    private void OnDestroy()
    {
        ReleaseInput();
        LanguageChanged = null;
        if (instance == this)
        {
            instance = null;
        }
    }

    private void ReportOnce(string problem, string message)
    {
        if (reportedProblems.Add(problem))
        {
            Debug.LogWarning(message, this);
        }
    }
}
