using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class LocalizationQueryTests
{
    private readonly List<Object> createdObjects = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] != null)
            {
                Object.DestroyImmediate(createdObjects[i]);
            }
        }
        createdObjects.Clear();
    }

    [Test]
    public void InitializeAndQueriesDoNotResetLanguageOrNotify()
    {
        LocalizationManager manager = CreateManager(CreateTable("Common", "health", "生命 {0}/{1}", "Health {0}/{1}"));
        int notifications = 0;
        manager.LanguageChanged += () => notifications++;

        Assert.AreEqual(GameLanguage.Chinese, manager.CurrentLanguage);
        Assert.AreEqual("生命 2/3", manager.FormatText("health", 2, 3));
        manager.SetLanguage(GameLanguage.English);
        manager.Initialize();
        manager.SetLanguage(GameLanguage.English);
        Assert.AreEqual("Health 2/3", manager.FormatText("health", 2, 3));
        Assert.AreEqual(GameLanguage.English, manager.CurrentLanguage);
        Assert.AreEqual(1, notifications);

        manager.ToggleLanguage();
        Assert.AreEqual(GameLanguage.Chinese, manager.CurrentLanguage);
        Assert.AreEqual(2, notifications);
    }

    [Test]
    public void MissingTranslationFallsBackAndMissingKeyIsVisibleWithoutRepeatedWarnings()
    {
        LocalizationManager manager = CreateManager(CreateTable("FallbackTable", "greeting", "", "Hello"));
        LogAssert.Expect(LogType.Warning, new Regex("FallbackTable.*greeting.*缺少 Chinese"));
        Assert.AreEqual("Hello", manager.GetText("greeting"));
        Assert.AreEqual("Hello", manager.GetText("greeting"));

        LogAssert.Expect(LogType.Warning, new Regex("找不到 key 'absent'"));
        Assert.AreEqual("[absent]", manager.GetText("absent"));
        Assert.AreEqual("[absent]", manager.GetText("absent"));
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void DuplicateKeyReportsBothSourcesAndKeepsFirstText()
    {
        LocalizationTableSO first = CreateTable("FirstTable", "same", "第一条", "First");
        LocalizationTableSO second = CreateTable("SecondTable", "same", "第二条", "Second");
        LocalizationManager manager = CreateManager(first, second);
        LogAssert.Expect(LogType.Warning, new Regex("重复 key 'same'.*FirstTable.*SecondTable"));
        manager.Initialize();
        Assert.AreEqual("第一条", manager.GetText("same"));
        manager.Initialize();
        Assert.AreEqual("第一条", manager.GetText("same"));
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void InvalidFormatOrMissingArgumentsReturnKeyAndWarnOnce()
    {
        LocalizationTableSO table = CreateTable("FormatTable", "broken", "生命 {", "Health {0}/{1}");
        LocalizationManager manager = CreateManager(table);
        LogAssert.Expect(LogType.Warning, new Regex("FormatTable.*broken.*Chinese.*占位符或参数错误"));
        Assert.AreEqual("[broken]", manager.FormatText("broken", 3));
        Assert.AreEqual("[broken]", manager.FormatText("broken", 3));

        manager.SetLanguage(GameLanguage.English);
        LogAssert.Expect(LogType.Warning, new Regex("FormatTable.*broken.*English.*占位符或参数错误"));
        Assert.AreEqual("[broken]", manager.FormatText("broken", 3));
        Assert.AreEqual("Health 2/3", manager.FormatText("broken", 2, 3));
        LogAssert.NoUnexpectedReceived();
    }

    [Test]
    public void ItemNamesAndDescriptionsComeFromOriginalDefinitionWithoutMutatingIt()
    {
        ItemDataSO item = ScriptableObject.CreateInstance<ItemDataSO>();
        createdObjects.Add(item);
        item.name = "OriginalMedkit";
        item.definitionId = "medkit";
        item.nameKey = "item.medkit.name";
        item.descriptionKey = "item.medkit.description";
        item.nameZh = "医疗包";
        item.nameEn = "Medkit";
        item.descriptionZh = "恢复生命。";
        item.descriptionEn = "Restores health.";
        ItemLibrarySO library = ScriptableObject.CreateInstance<ItemLibrarySO>();
        createdObjects.Add(library);
        library.itemLibraryList.Add(new ItemLibraryEntry { itemData = item });
        LocalizationManager manager = CreateManager();
        manager.itemLibrary = library;
        string before = JsonUtility.ToJson(item);

        Assert.AreEqual("医疗包", manager.GetText(item.nameKey));
        manager.ToggleLanguage();
        Assert.AreEqual("Medkit", manager.GetText(item.nameKey));
        Assert.AreEqual("Restores health.", manager.GetText(item.descriptionKey));
        Assert.AreEqual(before, JsonUtility.ToJson(item));

        item.nameZh = "调整后的医疗包";
        LocalizationManager nextRun = CreateManager();
        nextRun.itemLibrary = library;
        Assert.AreEqual("调整后的医疗包", nextRun.GetText(item.nameKey));
    }

    [Test]
    public void LanguageChangedCannotRecursivelyToggleDuringRefresh()
    {
        LocalizationManager manager = CreateManager(CreateTable("Common", "label", "中文", "English"));
        int refreshed = 0;
        manager.LanguageChanged += () =>
        {
            manager.GetText("label");
            manager.ToggleLanguage();
            refreshed++;
        };

        manager.ToggleLanguage();
        Assert.AreEqual(GameLanguage.English, manager.CurrentLanguage);
        Assert.AreEqual(1, refreshed);
    }

    [Test]
    public void TableValidationIdentifiesDuplicateAndMissingTranslation()
    {
        LocalizationTableSO table = CreateTable("ValidationTable", "label", "中文", "English");
        table.entries.Add(new LocalizationEntry { key = "label", chinese = "重复", english = "Duplicate" });
        Assert.IsFalse(table.TryValidate(out string duplicateError));
        StringAssert.Contains("label", duplicateError);
        StringAssert.Contains("重复", duplicateError);

        table.entries.RemoveAt(1);
        table.entries[0].english = "";
        Assert.IsFalse(table.TryValidate(out string translationError));
        StringAssert.Contains("label", translationError);
        StringAssert.Contains("译文", translationError);
    }

    private LocalizationManager CreateManager(params LocalizationTableSO[] tables)
    {
        GameObject host = new GameObject("LocalizationQueryTest");
        host.SetActive(false);
        createdObjects.Add(host);
        LocalizationManager manager = host.AddComponent<LocalizationManager>();
        manager.tables.AddRange(tables);
        return manager;
    }

    private LocalizationTableSO CreateTable(string name, string key, string chinese, string english)
    {
        LocalizationTableSO table = ScriptableObject.CreateInstance<LocalizationTableSO>();
        table.name = name;
        table.entries.Add(new LocalizationEntry { key = key, chinese = chinese, english = english });
        createdObjects.Add(table);
        return table;
    }
}
