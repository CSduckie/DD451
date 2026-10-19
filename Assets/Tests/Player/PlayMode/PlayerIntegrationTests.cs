#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class PlayerIntegrationTests : PlayerPlayModeTestBase
{
    [UnityTest]
    public IEnumerator RealItemsUseRealHealthAndKeepFailedUsesUnspent()
    {
        PlayerController player = CreatePlayer(Vector3.zero);
        player.Body.useGravity = false;
        ItemManager items = CreateItemModule();
        ItemInstance medkit = CreateItem(items, "medkit");
        ItemInstance foodA = CreateItem(items, "food");
        ItemInstance foodB = CreateItem(items, "food");
        yield return null;

        Assert.That(player.IsPrepared, Is.False);
        Assert.That(items.TryUseItem(foodA.InstanceId, player.health), Is.EqualTo(ItemUseResult.InvalidTarget));
        Assert.That(items.BeginRound(1), Is.True);
        Assert.That(player.ResetForExploration(1, Vector3.zero), Is.True);
        Assert.That(items.TryUseItem(medkit.InstanceId, player.health), Is.EqualTo(ItemUseResult.FullHealth));
        Assert.That(items.TryUseItem(foodA.InstanceId, player.health), Is.EqualTo(ItemUseResult.FullHealth));
        Assert.That(medkit.UsesThisRound, Is.Zero);
        Assert.That(foodA.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(foodA.UsesThisRound, Is.Zero);
        Assert.That(foodA.InstanceId, Is.Not.EqualTo(foodB.InstanceId));

        Assert.That(player.health.TakeDamage(2), Is.True);
        Assert.That(items.TrySetOwner(foodB.InstanceId, ItemOwner.Storage, "test-storage"), Is.True);
        Assert.That(items.TryUseItem(foodB.InstanceId, player.health), Is.EqualTo(ItemUseResult.NotCarried));
        Assert.That(foodB.UsesThisRound, Is.Zero);
        Assert.That(foodB.Owner, Is.EqualTo(ItemOwner.Storage));
        Assert.That(items.TrySetOwner(foodB.InstanceId, ItemOwner.Backpack, "test-backpack"), Is.True);
        Assert.That(items.TryUseItem(medkit.InstanceId, player.health), Is.EqualTo(ItemUseResult.Success));
        Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth));
        Assert.That(medkit.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        Assert.That(player.health.TakeDamage(2), Is.True);
        Assert.That(items.TryUseItem(medkit.InstanceId, player.health), Is.EqualTo(ItemUseResult.RoundLimitReached));

        Assert.That(items.TryUseItem(foodA.InstanceId, player.health), Is.EqualTo(ItemUseResult.Success));
        Assert.That(player.health.CurrentHealth, Is.EqualTo(2));
        Assert.That(foodA.Owner, Is.EqualTo(ItemOwner.Consumed));
        Assert.That(foodA.UsesThisRound, Is.EqualTo(1));
        Assert.That(foodB.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(foodB.UsesThisRound, Is.Zero);
        Assert.That(items.TryUseItem(foodB.InstanceId, player.health), Is.EqualTo(ItemUseResult.Success));
        Assert.That(player.health.CurrentHealth, Is.EqualTo(3));
        Assert.That(foodB.Owner, Is.EqualTo(ItemOwner.Consumed));
        Assert.That(items.InstanceCount, Is.EqualTo(3));
    }

    [UnityTest]
    public IEnumerator ObserverExceptionCannotInterruptRealItemAccounting()
    {
        PlayerController player = CreatePlayer(Vector3.zero);
        player.Body.useGravity = false;
        ItemManager items = CreateItemModule();
        ItemInstance medkit = CreateItem(items, "medkit");
        yield return null;
        Assert.That(player.ResetForExploration(1, Vector3.zero), Is.True);
        Assert.That(items.BeginRound(1), Is.True);
        Assert.That(player.health.TakeDamage(1), Is.True);
        int notified = 0;
        Action<int, int> brokenDisplay = (current, maximum) => throw new InvalidOperationException("observer failed");
        Action<int, int> otherDisplay = (current, maximum) => notified++;
        player.health.HealthChanged += brokenDisplay;
        player.health.HealthChanged += otherDisplay;
        LogAssert.Expect(LogType.Exception, new Regex("observer failed"));
        Assert.That(items.TryUseItem(medkit.InstanceId, player.health), Is.EqualTo(ItemUseResult.Success));
        Assert.That(player.health.CurrentHealth, Is.EqualTo(3));
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        Assert.That(notified, Is.EqualTo(1), "A broken display must not prevent other notifications.");
        player.health.HealthChanged -= brokenDisplay;
        player.health.HealthChanged -= otherDisplay;
    }

    [UnityTest]
    public IEnumerator DeathReenableAndDuplicateRoundCannotReviveOrConsumeFood()
    {
        PlayerController player = CreatePlayer(Vector3.zero);
        player.Body.useGravity = false;
        ItemManager items = CreateItemModule();
        ItemInstance food = CreateItem(items, "food");
        yield return null;
        Assert.That(player.ResetForExploration(5, new Vector3(1f, 0f, 99f)), Is.True);
        Assert.That(items.BeginRound(5), Is.True);
        Assert.That(player.transform.position.z, Is.EqualTo(player.config.planeZ).Within(0.001f));
        int deaths = 0;
        player.health.Died += () => deaths++;
        Assert.That(player.health.TakeDamage(1), Is.True);
        Assert.That(player.ResetForExploration(5, Vector3.zero), Is.False);
        Assert.That(player.health.CurrentHealth, Is.EqualTo(2));
        player.gameObject.SetActive(false);
        yield return null;
        player.gameObject.SetActive(true);
        yield return null;
        Assert.That(player.health.CurrentHealth, Is.EqualTo(2), "OnEnable must not heal.");
        Assert.That(player.health.TakeDamage(100), Is.True);
        Assert.That(player.IsDead, Is.True);
        Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Die));
        player.SetBackpackOpen(false);
        player.health.Heal(100);
        Assert.That(player.health.TakeDamage(1), Is.False);
        Assert.That(items.TryUseItem(food.InstanceId, player.health), Is.EqualTo(ItemUseResult.InvalidTarget));
        Assert.That(food.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(food.UsesThisRound, Is.Zero);
        player.gameObject.SetActive(false);
        yield return null;
        player.gameObject.SetActive(true);
        yield return null;
        Assert.That(player.IsDead, Is.True);
        Assert.That(player.CanAct, Is.False);
        Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Die));
        Assert.That(deaths, Is.EqualTo(1));
        Assert.That(player.ResetForExploration(4, Vector3.zero), Is.False);
        Assert.That(player.ResetForExploration(5, Vector3.zero), Is.False);
        Assert.That(player.health.CurrentHealth, Is.Zero);
        Assert.That(player.ResetForExploration(6, new Vector3(4f, 0f, -99f)), Is.True);
        Assert.That(items.BeginRound(6), Is.True);
        Assert.That(player.health.CurrentHealth, Is.EqualTo(3));
        Assert.That(player.IsDead, Is.False);
        Assert.That(player.IsBackpackOpen, Is.False);
        Assert.That(player.Body.linearVelocity, Is.EqualTo(Vector3.zero));
        Assert.That(player.transform.position.z, Is.EqualTo(player.config.planeZ).Within(0.001f));
    }

    [UnityTest]
    public IEnumerator RealSceneRequiresExplicitPreparationAndReloadPreservesConsumedItems()
    {
        var boundsLogs = new List<string>();
        Application.LogCallback captureBoundsLog = (condition, stackTrace, type) =>
        {
            if (condition != null && condition.StartsWith("[PlayerCameraBounds]", StringComparison.Ordinal))
            {
                boundsLogs.Add(type + ": " + condition);
            }
        };
        Application.logMessageReceived += captureBoundsLog;
        try
        {
            yield return LoadPlayerScene();
            yield return null;
            PlayerTestDriver driver = Object.FindFirstObjectByType<PlayerTestDriver>();
            Assert.That(driver, Is.Not.Null);
            Assert.That(driver.IsReady, Is.True);
            GameBootstrap entry = GameBootstrap.Instance;
            Track(entry.gameObject);
            AssertLoadedCameraBoundWithoutDiagnostics(driver.player, boundsLogs);
            ItemManager items = driver.itemManager;
            Assert.That(items.CurrentRound, Is.Zero);
            Assert.That(driver.player.IsPrepared, Is.False);
            Assert.That(driver.player.CanAct, Is.False);
            Assert.That(items.InstanceCount, Is.EqualTo(3));
            driver.PrepareNextRound();
            Assert.That(driver.player.health.CurrentRound, Is.EqualTo(1));
            Assert.That(items.CurrentRound, Is.EqualTo(1));
            driver.ToggleBackpack();
            Assert.That(driver.player.IsBackpackOpen, Is.True);
            driver.DamageOne();
            Assert.That(driver.player.IsBackpackOpen, Is.False, "The test driver simulates 04's damage response.");
            driver.UseFirstFood();
            ItemInstance consumed = items.GetInstances().Find(item => item.Owner == ItemOwner.Consumed);
            Assert.That(consumed, Is.Not.Null);
            yield return new WaitForSeconds(driver.player.config.damageInvulnerabilitySeconds + 0.05f);
            driver.DamageLethal();
            Assert.That(driver.player.IsDead, Is.True);
            yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneByPath(PlayerScenePath));
            yield return LoadPlayerScene();
            yield return null;

            driver = Object.FindFirstObjectByType<PlayerTestDriver>();
            Assert.That(driver, Is.Not.Null);
            Assert.That(driver.IsReady, Is.True);
            AssertLoadedCameraBoundWithoutDiagnostics(driver.player, boundsLogs);
            Assert.That(GameBootstrap.Instance, Is.SameAs(entry));
            Assert.That(driver.itemManager, Is.SameAs(items));
            Assert.That(items.InstanceCount, Is.EqualTo(3));
            Assert.That(items.CurrentRound, Is.EqualTo(1));
            Assert.That(driver.player.IsPrepared, Is.False, "A new scene object needs an explicit round preparation.");
            Assert.That(driver.player.CanAct, Is.False);
            Assert.That(items.TryGetInstance(consumed.InstanceId, out ItemInstance sameFood), Is.True);
            Assert.That(sameFood, Is.SameAs(consumed));
            Assert.That(sameFood.Owner, Is.EqualTo(ItemOwner.Consumed));
            driver.PrepareNextRound();
            Assert.That(driver.player.health.CurrentRound, Is.EqualTo(2));
            Assert.That(items.CurrentRound, Is.EqualTo(2));
            Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(driver.player.health.MaxHealth));
            Assert.That(items.InstanceCount, Is.EqualTo(3));
            Assert.That(sameFood.Owner, Is.EqualTo(ItemOwner.Consumed), "New rounds do not replenish consumables.");
            Assert.That(boundsLogs, Is.Empty, string.Join(Environment.NewLine, boundsLogs));
        }
        finally { Application.logMessageReceived -= captureBoundsLog; }
    }

    private static void AssertLoadedCameraBoundWithoutDiagnostics(PlayerController player, List<string> boundsLogs)
    {
        // 普通加载/重载不接受暂态假警告，也不能靠静默不绑定让日志断言通过。
        Assert.That(boundsLogs, Is.Empty, string.Join(Environment.NewLine, boundsLogs));
        PlayerCameraTargetBinder[] binders = Object.FindObjectsByType<PlayerCameraTargetBinder>(FindObjectsSortMode.None);
        Assert.That(binders.Length, Is.EqualTo(1));
        PlayerCameraTargetBinder binder = binders[0];
        Assert.That(binder.isActiveAndEnabled, Is.True);
        Assert.That(binder.gameObject.scene.isLoaded, Is.True);
        Assert.That(binder.gameObject.scene, Is.EqualTo(player.gameObject.scene));
        Assert.That(binder.player, Is.SameAs(player));
        Assert.That(binder.followCamera, Is.Not.Null);
        Assert.That(binder.followCamera.isActiveAndEnabled, Is.True);
        Assert.That(binder.confiner, Is.Not.Null);
        Assert.That(binder.ValidateBounds(out string error), Is.True, error);
        Assert.That(binder.LastBoundsError, Is.Null.Or.Empty);
        Assert.That(binder.confiner.IsValid, Is.True);
        Assert.That(binder.followCamera.Follow, Is.SameAs(player.cameraTarget != null ? player.cameraTarget : player.transform));
    }
}

// 两组PlayMode测试共用资产装载/清理；不提供替代生命或物品实现。
public abstract class PlayerPlayModeTestBase
{
    protected const string PlayerScenePath = "Assets/Scenes/Tests/Player/PlayerTest.unity";
    private readonly List<Object> createdObjects = new List<Object>();
    private bool loadedPlayerScene;
    private InputSettings savedInputSettings;
    private InputSettings testInputSettings;
    private bool savedRunInBackground;

    [SetUp]
    public void RequireCleanTestScene()
    {
        if (Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0
            || Object.FindObjectsByType<ItemManager>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0
            || Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
        {
            Assert.Ignore("Run Player PlayMode tests from the Test Runner's clean test scene.");
        }

        // Test Runner may execute without a focused Game View. Keep real queued keyboard events
        // in the player loop using a transient settings copy; never edit the project's asset.
        savedInputSettings = InputSystem.settings;
        savedRunInBackground = Application.runInBackground;
        testInputSettings = Object.Instantiate(savedInputSettings);
        testInputSettings.name = "Player PlayMode test input settings";
        testInputSettings.hideFlags = HideFlags.HideAndDontSave;
        testInputSettings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        testInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        Application.runInBackground = true;
        InputSystem.settings = testInputSettings;
    }

    [TearDown]
    public void RestoreInputEnvironment()
    {
        if (savedInputSettings == null) { return; }
        try
        {
            InputSystem.settings = savedInputSettings;
        }
        finally
        {
            Application.runInBackground = savedRunInBackground;
            if (testInputSettings != null) { Object.DestroyImmediate(testInputSettings); }
            testInputSettings = null;
            savedInputSettings = null;
        }
    }

    [UnityTearDown]
    public IEnumerator CleanupOwnedObjects()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] is GameObject)
            {
                Object.Destroy(createdObjects[i]);
            }
        }
        yield return null;
        if (loadedPlayerScene)
        {
            Scene scene = SceneManager.GetSceneByPath(PlayerScenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            loadedPlayerScene = false;
        }
        foreach (Object created in createdObjects)
        {
            if (created != null)
            {
                Object.Destroy(created);
            }
        }
        createdObjects.Clear();
        yield return null;
    }

    protected void Track(Object item)
    {
        createdObjects.Add(item);
    }
    protected GameObject CreatePrefab(string path, Transform parent = null)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(asset, Is.Not.Null, path);
        GameObject instance = Object.Instantiate(asset, parent);
        Track(instance);
        return instance;
    }

    protected PlayerController CreatePlayer(Vector3 position)
    {
        GameObject holder = new GameObject("Player test owned holder");
        holder.SetActive(false);
        Track(holder);
        PlayerController player = CreatePrefab("Assets/Prefabs/Player/Player.prefab", holder.transform)
            .GetComponent<PlayerController>();
        Assert.That(player, Is.Not.Null);
        Assert.That(player.config, Is.Not.Null);
        Assert.That(player.health, Is.Not.Null);
        Assert.That(player.inputReader, Is.Not.Null);
        Assert.That(player.inputReader.inputActions, Is.Not.Null);
        PlayerConfigSO config = Object.Instantiate(player.config);
        Track(config);
        config.maxHealth = 3;
        config.damageInvulnerabilitySeconds = 0f;
        // 原有移动/碰撞用例明确覆盖Hold；切换模式由PlayerToggleCrouchTests单独覆盖。
        config.crouchInputMode = CrouchInputMode.Hold;
        config.jumpSpeed = 6f;
        // These integration scenarios retain their original 6m/s, ordinary-gravity contract.
        // Shipping reference-jump tuning has its own physical trajectory tests.
        config.gravityScale = 1f;
        config.fallMultiplier = 1f;
        config.lowJumpMultiplier = 1f;
        config.planeZ = 0f;
        player.config = config;
        player.health.config = config;
        InputActionAsset input = Object.Instantiate(player.inputReader.inputActions);
        Track(input);
        player.inputReader.inputActions = input;
        player.transform.position = position;
        holder.SetActive(true);
        return player;
    }

    protected ItemManager CreateItemModule()
    {
        GameObject entry = CreatePrefab("Assets/Prefabs/Bootstrap/GameBootstrap.prefab");
        Assert.That(GameBootstrap.Instance, Is.SameAs(entry.GetComponent<GameBootstrap>()));
        ItemManager items = CreatePrefab("Assets/Prefabs/Items/ItemModule.prefab", entry.transform)
            .GetComponent<ItemManager>();
        Assert.That(items, Is.Not.Null);
        Assert.That(items.itemLibrary, Is.Not.Null);
        return items;
    }

    protected static ItemInstance CreateItem(ItemManager items, string definitionId)
    {
        ItemDataSO definition = items.itemLibrary.FindDefinition(definitionId);
        Assert.That(definition, Is.Not.Null, definitionId);
        Assert.That(items.TryCreateInstance(definition, ItemOwner.Backpack, "test-backpack",
            "player-integration-test", "player-integration-test/medkit-origin", out ItemInstance item), Is.True);
        return item;
    }

    protected IEnumerator LoadPlayerScene()
    {
        Scene existing = SceneManager.GetSceneByPath(PlayerScenePath);
        Assert.That(existing.IsValid() && existing.isLoaded, Is.False);
        loadedPlayerScene = true;
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(PlayerScenePath,
            new LoadSceneParameters(LoadSceneMode.Additive));
    }
}
#endif
