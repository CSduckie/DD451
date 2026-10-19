using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public class ItemsPlayModeTests
{
    private const string BaseScenePath = "Assets/Scenes/Tests/Base/BaseTest.unity";
    private const string ItemsScenePath = "Assets/Scenes/Tests/Items/ItemsTest.unity";
    private const string BootstrapPrefabPath = "Assets/Prefabs/Bootstrap/GameBootstrap.prefab";
    private const string ItemPrefabPath = "Assets/Prefabs/Items/ItemModule.prefab";

    private readonly List<GameObject> createdObjects = new List<GameObject>();
    private readonly List<string> loadedScenePaths = new List<string>();

    [SetUp]
    public void SetUp()
    {
        // 只清理由本测试创建的对象；不删除用户已运行的入口或物品数据。
        if (Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include,
            FindObjectsSortMode.None).Length != 0 || FindManagers().Length != 0
            || IsLoaded(BaseScenePath) || IsLoaded(ItemsScenePath))
        {
            Assert.Ignore("Run Items PlayMode tests from the Test Runner's clean test scene.");
        }
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (GameObject created in createdObjects)
        {
            if (created != null)
            {
                Object.Destroy(created);
            }
        }
        createdObjects.Clear();
        yield return null;

        foreach (string path in loadedScenePaths)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            if (scene.IsValid() && scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(scene);
            }
        }
        loadedScenePaths.Clear();
    }

    [UnityTest]
    public IEnumerator RealPrefabsKeepIdentityAndUseStateAcrossSceneReloads()
    {
#if UNITY_EDITOR
        GameObject entryObject = CreatePrefab(BootstrapPrefabPath);
        GameBootstrap entry = entryObject.GetComponent<GameBootstrap>();
        Assert.That(GameBootstrap.Instance, Is.SameAs(entry));
        GameObject module = CreatePrefab(ItemPrefabPath, entry.transform);
        ItemManager manager = module.GetComponent<ItemManager>();
        Assert.That(manager, Is.Not.Null);
        Assert.That(manager.itemLibrary, Is.Not.Null);
        Assert.That(manager.itemLibrary.TryValidate(out string libraryError), Is.True, libraryError);

        ItemsTestHealth health = CreateHealth(entry.transform);
        Assert.That(manager.BeginRound(7), Is.True);
        ItemInstance medkit = CreateItem(manager, "medkit", "test-medkit-origin");
        ItemInstance foodA = CreateItem(manager, "food", string.Empty);
        ItemInstance foodB = CreateItem(manager, "food", string.Empty);
        Assert.That(foodA.InstanceId, Is.Not.EqualTo(foodB.InstanceId));
        Assert.That(manager.TrySetRetained(medkit.InstanceId, true), Is.True);
        Assert.That(manager.TryUseItem(medkit.InstanceId, health), Is.EqualTo(ItemUseResult.Success));
        health.Damage(1);

        string[] paths = { BaseScenePath, ItemsScenePath, ItemsScenePath };
        foreach (string path in paths)
        {
            yield return LoadTestScene(path);
            yield return null;

            Assert.That(GameBootstrap.Instance, Is.SameAs(entry));
            Assert.That(entry.gameObject.scene.name, Is.EqualTo("DontDestroyOnLoad"));
            Assert.That(Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length, Is.EqualTo(1), "The duplicate scene entry must be destroyed.");
            Assert.That(FindManagers(), Has.Length.EqualTo(1));
            Assert.That(FindManagers()[0], Is.SameAs(manager));
            Assert.That(manager.InstanceCount, Is.EqualTo(3), "Reloading ItemsTest must not create more items.");
            Assert.That(manager.CurrentRound, Is.EqualTo(7));
            Assert.That(manager.TryGetInstance(medkit.InstanceId, out ItemInstance reloaded), Is.True);
            Assert.That(reloaded, Is.SameAs(medkit));
            Assert.That(reloaded.Owner, Is.EqualTo(ItemOwner.Backpack));
            Assert.That(reloaded.ContainerId, Is.EqualTo("test-backpack"));
            Assert.That(reloaded.IsRetained, Is.True);
            Assert.That(reloaded.UseRound, Is.EqualTo(7));
            Assert.That(reloaded.UsesThisRound, Is.EqualTo(1));
            Assert.That(manager.TryUseItem(medkit.InstanceId, health), Is.EqualTo(ItemUseResult.RoundLimitReached));
            Assert.That(manager.TryGetInstance(foodA.InstanceId, out ItemInstance sameFood), Is.True);
            Assert.That(sameFood, Is.SameAs(foodA));
            Assert.That(foodB.Owner, Is.EqualTo(ItemOwner.Backpack));

            yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneByPath(path));
            Assert.That(manager != null, Is.True);
            Assert.That(manager.TryGetInstance(medkit.InstanceId, out reloaded), Is.True);
            Assert.That(reloaded, Is.SameAs(medkit));
            Assert.That(reloaded.UsesThisRound, Is.EqualTo(1));
        }
#else
        Assert.Ignore("This test loads existing assets by Editor path without changing Build Settings.");
        yield break;
#endif
    }

    [UnityTest]
    public IEnumerator ItemsSceneInitializesOnceAndRetainsConsumedFoodOnReload()
    {
#if UNITY_EDITOR
        yield return LoadTestScene(ItemsScenePath);
        GameBootstrap entry = GameBootstrap.Instance;
        Assert.That(entry, Is.Not.Null);
        createdObjects.Add(entry.gameObject);
        yield return null;

        ItemManager manager = entry.GetComponentInChildren<ItemManager>();
        ItemsTestDriver driver = entry.GetComponentInChildren<ItemsTestDriver>();
        ItemsTestHealth health = entry.GetComponentInChildren<ItemsTestHealth>();
        Assert.That(manager, Is.Not.Null);
        Assert.That(driver, Is.Not.Null);
        Assert.That(health, Is.Not.Null);
        Assert.That(driver.itemManager, Is.SameAs(manager));
        Assert.That(driver.health, Is.SameAs(health));
        Assert.That(driver.itemLibrary, Is.SameAs(manager.itemLibrary));
        Assert.That(manager.CurrentRound, Is.EqualTo(1));
        Assert.That(manager.InstanceCount, Is.EqualTo(3));

        List<ItemInstance> food = manager.GetInstances().FindAll(item => item.DefinitionId == "food");
        Assert.That(food, Has.Count.EqualTo(2));
        Assert.That(food[0].Definition, Is.SameAs(food[1].Definition));
        Assert.That(food[0].InstanceId, Is.Not.EqualTo(food[1].InstanceId));
        Assert.That(manager.TryUseItem(food[0].InstanceId, health), Is.EqualTo(ItemUseResult.Success));
        Assert.That(food[0].Owner, Is.EqualTo(ItemOwner.Consumed));
        Assert.That(food[1].Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(health.CurrentHealth, Is.EqualTo(3));

        yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneByPath(ItemsScenePath));
        yield return LoadTestScene(ItemsScenePath);
        yield return null;

        Assert.That(GameBootstrap.Instance, Is.SameAs(entry));
        Assert.That(FindManagers(), Has.Length.EqualTo(1));
        Assert.That(FindManagers()[0], Is.SameAs(manager));
        Assert.That(manager.InstanceCount, Is.EqualTo(3));
        Assert.That(manager.TryGetInstance(food[0].InstanceId, out ItemInstance consumed), Is.True);
        Assert.That(consumed, Is.SameAs(food[0]));
        Assert.That(consumed.Owner, Is.EqualTo(ItemOwner.Consumed));
        Assert.That(consumed.UsesThisRound, Is.EqualTo(1));
        Assert.That(food[1].UsesThisRound, Is.Zero);
        Assert.That(health.CurrentHealth, Is.EqualTo(3));
#else
        Assert.Ignore("This test loads the real ItemsTest scene by Editor path.");
        yield break;
#endif
    }

    [UnityTest]
    public IEnumerator DestroyedUnityHealthTargetDoesNotConsumeOrSpendAllowance()
    {
#if UNITY_EDITOR
        ItemManager manager = CreatePrefab(ItemPrefabPath).GetComponent<ItemManager>();
        Assert.That(manager, Is.Not.Null);
        Assert.That(manager.BeginRound(1), Is.True);
        ItemInstance medkit = CreateItem(manager, "medkit", "test-medkit-origin");
        ItemInstance food = CreateItem(manager, "food", string.Empty);
        Assert.That(manager.TrySetRetained(food.InstanceId, true), Is.True);
        ItemsTestHealth health = CreateHealth(null);
        IHealthTarget destroyedTarget = health;
        Object.Destroy(health.gameObject);
        yield return null;

        Assert.That(health == null, Is.True);
        Assert.That(ReferenceEquals(destroyedTarget, null), Is.False,
            "An interface retains its CLR reference after Unity destroys its component.");
        Assert.That(manager.TryUseItem(medkit.InstanceId, destroyedTarget), Is.EqualTo(ItemUseResult.InvalidTarget));
        Assert.That(manager.TryUseItem(food.InstanceId, destroyedTarget), Is.EqualTo(ItemUseResult.InvalidTarget));
        Assert.That(medkit.UsesThisRound, Is.Zero);
        Assert.That(food.UsesThisRound, Is.Zero);
        Assert.That(medkit.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(food.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(food.ContainerId, Is.EqualTo("test-backpack"));
        Assert.That(food.IsRetained, Is.True);
#else
        Assert.Ignore("This test uses the configured ItemModule asset by Editor path.");
        yield break;
#endif
    }

#if UNITY_EDITOR
    private GameObject CreatePrefab(string path, Transform parent = null)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null, path);
        GameObject instance = Object.Instantiate(prefab, parent);
        createdObjects.Add(instance);
        return instance;
    }

    private IEnumerator LoadTestScene(string path)
    {
        Assert.That(IsLoaded(path), Is.False, "Only scenes loaded by this test may be unloaded.");
        if (!loadedScenePaths.Contains(path))
        {
            loadedScenePaths.Add(path);
        }
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path,
            new LoadSceneParameters(LoadSceneMode.Additive));
        Assert.That(IsLoaded(path), Is.True, path);
    }
#endif

    private ItemsTestHealth CreateHealth(Transform parent)
    {
        GameObject testObject = new GameObject("Items PlayMode Test Health");
        createdObjects.Add(testObject);
        testObject.transform.SetParent(parent);
        return testObject.AddComponent<ItemsTestHealth>();
    }

    private static ItemInstance CreateItem(ItemManager manager, string definitionId, string origin)
    {
        Assert.That(manager.itemLibrary, Is.Not.Null);
        ItemDataSO definition = manager.itemLibrary.FindDefinition(definitionId);
        Assert.That(definition, Is.Not.Null, definitionId);
        Assert.That(manager.TryCreateInstance(definition, ItemOwner.Backpack, "test-backpack",
            "items-playmode-test", origin, out ItemInstance instance), Is.True, definitionId);
        return instance;
    }

    private static ItemManager[] FindManagers()
    {
        return Object.FindObjectsByType<ItemManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    private static bool IsLoaded(string path)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        return scene.IsValid() && scene.isLoaded;
    }
}
