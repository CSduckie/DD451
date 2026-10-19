#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// 真实场景与UGUI排队输入；定位/接触由API辅助，不冒称实体键鼠或物理碰撞回调验收。
public class EnemySceneIntegrationTests
{
    private const string ScenePath = "Assets/Scenes/Tests/Enemy/EnemyTest.unity";
    private EnemyTestDriver driver;
    private GameObject persistentRoot;
    private Keyboard keyboard;
    private Mouse mouse;
    private InputSettings originalSettings;
    private InputSettings testSettings;
    private bool originalRunInBackground;
    private bool loadedScene;
    private readonly List<InputDevice> disabledDevices = new List<InputDevice>();
    private readonly List<Object> temporaryObjects = new List<Object>();

    [SetUp]
    public void PrepareInput()
    {
        loadedScene = false;
        persistentRoot = null;
        driver = null;
        if (Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0
            || SceneManager.GetSceneByPath(ScenePath).isLoaded)
        {
            Assert.Ignore("Run Enemy integration tests from the clean Test Runner scene.");
        }
        originalSettings = InputSystem.settings;
        originalRunInBackground = Application.runInBackground;
        try
        {
            testSettings = Object.Instantiate(originalSettings);
            testSettings.hideFlags = HideFlags.HideAndDontSave;
            testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings = testSettings;
            Application.runInBackground = true;
            foreach (InputDevice device in InputSystem.devices.ToArray())
            {
                if (!device.enabled || (!(device is Keyboard) && !(device is UnityEngine.InputSystem.Pointer))) { continue; }
                disabledDevices.Add(device);
                InputSystem.DisableDevice(device);
            }
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
        }
        catch
        {
            RestoreInput();
            throw;
        }
    }

    [UnitySetUp]
    public IEnumerator FocusGameViewOnce()
    {
        // InputSystem忽略焦点只影响设备；真实UI仍会在OnApplicationFocus(false)关闭。
        System.Type gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        Assert.That(gameViewType, Is.Not.Null);
        EditorWindow.GetWindow(gameViewType).Focus();
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        try
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (loadedScene && scene.IsValid() && scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(scene); }
            if (persistentRoot != null) { Object.Destroy(persistentRoot); }
            yield return null;
        }
        finally
        {
            foreach (Object item in temporaryObjects) { if (item != null) { Object.Destroy(item); } }
            temporaryObjects.Clear();
            RestoreInput();
        }
        yield return null;
    }

    private void RestoreInput()
    {
        try
        {
            if (keyboard != null && keyboard.added) { InputSystem.RemoveDevice(keyboard); }
            if (mouse != null && mouse.added) { InputSystem.RemoveDevice(mouse); }
            keyboard = null;
            mouse = null;
            if (originalSettings != null)
            {
                InputSystem.settings = originalSettings;
                Application.runInBackground = originalRunInBackground;
            }
            originalSettings = null;
            if (testSettings != null) { Object.Destroy(testSettings); }
            testSettings = null;
        }
        finally
        {
            foreach (InputDevice device in disabledDevices)
            {
                if (device.added && !device.enabled) { InputSystem.EnableDevice(device); }
            }
            disabledDevices.Clear();
        }
    }

    [UnityTest]
    public IEnumerator DroneContactClosesRealBackpackCancelsQueuedDragAndInterruptsSearchWithoutItemChanges()
    {
        yield return LoadScene();
        yield return OpenSearch();
        LootSourceState source = driver.lootBox.State;
        float limit = Time.realtimeSinceStartup + 8f;
        while (!source.Entries[0].Revealed && Time.realtimeSinceStartup < limit)
        {
            AssertSearchUiOpen();
            yield return null;
        }
        AssertSearchUiOpen();
        Assert.That(source.Entries[0].Revealed, Is.True, "The real LootUI Update must complete the first item.");
        Assert.That(source.GetNextUnrevealed(), Is.Not.Null);
        yield return null;
        Assert.That(source.SearchElapsed, Is.GreaterThan(0f));

        BackpackPlacement original = driver.Backpack.GetPlacements().First();
        Vector2 point = BackpackPoint(original.X, original.Y);
        yield return Pointer(point, false);
        yield return Pointer(point, true);
        yield return Pointer(point + Vector2.right * 24f, true);
        Assert.That(driver.backpackUI.IsDragging, Is.True, "Queued UGUI input must create the real drag.");
        int token = driver.backpackUI.DragToken;
        string items = ItemSnapshot();
        string placements = PlacementSnapshot();
        int health = driver.player.health.CurrentHealth;
        Assert.That(driver.player.CanAct, Is.False, "An open backpack locks actions, not damage eligibility.");
        Assert.That(DroneController.CanTarget(driver.player), Is.True);

        DroneController drone = PrepareContact(driver.drones[0]);
        Assert.That(drone.TryExplode(driver.player), Is.True);
        Assert.That(drone.HasExploded, Is.True);
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health - drone.config.contactDamage));
        Assert.That(driver.player.IsDead, Is.False);
        Assert.That(driver.backpackUI.IsOpen, Is.False, FocusContext());
        Assert.That(driver.backpackUI.IsDragging, Is.False);
        Assert.That(driver.lootUI.IsOpen, Is.False, FocusContext());
        Assert.That(driver.lootUI.SessionToken, Is.Zero);
        Assert.That(source.SearchElapsed, Is.Zero);
        Assert.That(source.Entries[0].Revealed, Is.True);
        Assert.That(source.GetNextUnrevealed(), Is.Not.Null);
        Assert.That(driver.transferUI.IsAwaitingDiscardConfirmation, Is.False);
        Assert.That(ItemSnapshot(), Is.EqualTo(items));
        Assert.That(PlacementSnapshot(), Is.EqualTo(placements));
        Assert.That(drone.TryExplode(driver.player), Is.False);

        yield return Pointer(point + Vector2.right * 100f, false);
        driver.backpackUI.EndDrag(token, point, null);
        Assert.That(ItemSnapshot(), Is.EqualTo(items), "Old pointer release cannot submit after damage.");
        Assert.That(PlacementSnapshot(), Is.EqualTo(placements));
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health - drone.config.contactDamage));
    }

    [UnityTest]
    public IEnumerator QueuedF10KeepsEnemyRoundExplosionHealthAndLiveSearchSession()
    {
        yield return LoadScene();
        DroneController exploded = PrepareContact(driver.drones[0]);
        Assert.That(exploded.TryExplode(driver.player), Is.True);
        DroneController patrolling = driver.drones[1];
        Assert.That(patrolling.BeginRound(patrolling.CurrentRound + 1), Is.True);
        yield return OpenSearch();
        LocalizationManager language = LocalizationManager.Resolve();
        Assert.That(language.CurrentLanguage, Is.EqualTo(GameLanguage.Chinese));
        int enemyRound = exploded.CurrentRound;
        int otherRound = patrolling.CurrentRound;
        int health = driver.player.health.CurrentHealth;
        int session = driver.lootUI.SessionToken;
        LootSourceState source = driver.lootUI.Source;
        string items = ItemSnapshot();
        string placements = PlacementSnapshot();
        float elapsed = source.SearchElapsed;
        string searchEntry = source.SearchEntryId;

        yield return Press(Key.F10);
        AssertSearchUiOpen();
        Assert.That(language.CurrentLanguage, Is.EqualTo(GameLanguage.English));
        Assert.That(driver.lootUI.SessionToken, Is.EqualTo(session));
        Assert.That(driver.lootUI.Source, Is.SameAs(source));
        Assert.That(source.SearchEntryId, Is.EqualTo(searchEntry));
        Assert.That(source.SearchElapsed, Is.GreaterThanOrEqualTo(elapsed));
        Assert.That(driver.statusText.text, Does.Contain("Enemy test round"));
        Assert.That(exploded.HasExploded, Is.True);
        Assert.That(exploded.CurrentRound, Is.EqualTo(enemyRound));
        Assert.That(patrolling.CurrentState, Is.EqualTo(DroneState.Patrol));
        Assert.That(patrolling.CurrentRound, Is.EqualTo(otherRound));
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health));
        Assert.That(ItemSnapshot(), Is.EqualTo(items));
        Assert.That(PlacementSnapshot(), Is.EqualTo(placements));

        yield return Press(Key.F10);
        AssertSearchUiOpen();
        Assert.That(language.CurrentLanguage, Is.EqualTo(GameLanguage.Chinese));
        Assert.That(driver.statusText.text, Does.Contain("敌人测试轮次"));
        Assert.That(driver.lootUI.SessionToken, Is.EqualTo(session));
        Assert.That(exploded.HasExploded, Is.True);
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health));
        Assert.That(ItemSnapshot(), Is.EqualTo(items));
    }

    [UnityTest]
    public IEnumerator RealSceneDetectionHoversForDiscoveryAlertThenChasesWithOnlyTheFanHidden()
    {
        yield return LoadScene();
        PlaceAlertPlayer(new Vector3(-1f, 8f, 0f));
        DroneController drone = PrepareAlertDrone(driver.drones[0], new Vector3(-4f, 8f, 0f));
        Vector3 position = drone.Body.position;
        int health = driver.player.health.CurrentHealth;
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
        Assert.That(drone.vision.CanSee(driver.player), Is.True, "The actual saved vision Collider must see the real player.");
        yield return WaitForDiscoveryAlert(drone);
        Assert.That(drone.Target, Is.SameAs(driver.player));
        float remaining = drone.AlertSecondsRemaining;
        float startedAt = Time.time;
        float earliestChase = remaining;
        float limit = Time.realtimeSinceStartup + drone.config.discoveryAlertSeconds + 2f;
        while (drone.CurrentState != DroneState.Chase && Time.realtimeSinceStartup < limit)
        {
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
            Assert.That(drone.IsCoverAlert, Is.False);
            Assert.That(drone.AlertSecondsRemaining, Is.LessThanOrEqualTo(remaining));
            Assert.That(drone.AlertSecondsRemaining, Is.InRange(0f, drone.config.discoveryAlertSeconds));
            Assert.That(Vector3.Distance(drone.Body.position, position), Is.LessThan(0.002f));
            Assert.That(drone.HasExploded, Is.False);
            Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health));
            AssertPureOrangeAlert(drone);
            remaining = drone.AlertSecondsRemaining;
            yield return null;
        }
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Chase));
        Assert.That(Time.time - startedAt, Is.GreaterThanOrEqualTo(earliestChase - Time.deltaTime - 0.01f),
            "Natural discovery must wait for the configured discovery alert countdown before Chase.");
        Assert.That(drone.Target, Is.SameAs(driver.player));
        Assert.That(drone.HasExploded, Is.False);
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health));
        yield return null;
        Assert.That(drone.vision.gameObject.activeInHierarchy && drone.vision.enabled, Is.True);
        Assert.That(drone.vision.RangeCollider.enabled, Is.True);
        Assert.That(drone.vision.RangeCollider.sharedMesh, Is.Not.Null);
        Assert.That(drone.vision.CanSee(driver.player), Is.True, "Chase keeps its real detection even though the fan is hidden.");
        Assert.That(drone.vision.GetComponent<MeshRenderer>().enabled, Is.False);
    }

    [UnityTest]
    public IEnumerator QueuedF10PreservesIndependentAlertTimersAndWallSearchOnTwoRealSceneDrones()
    {
        yield return LoadScene();
        LocalizationManager language = LocalizationManager.Resolve();
        Assert.That(language.CurrentLanguage, Is.EqualTo(GameLanguage.Chinese));
        PlaceAlertPlayer(new Vector3(-1f, 9f, 0f));
        DroneController first = PrepareAlertDrone(driver.drones[0], new Vector3(-4f, 8f, 0f));
        // 单独用例验证真实0.5秒默认值；排队输入夹具仅延长运行时发现计时，留足双语切换余量。
        first.config.discoveryAlertSeconds = 4f;
        yield return WaitForDiscoveryAlert(first);
        yield return WaitForAlertElapsed(first, 0.8f);
        DroneController second = PrepareAlertDrone(driver.drones[1], new Vector3(-4f, 10f, 0f));
        second.config.discoveryAlertSeconds = 4f;
        second.config.searchSeconds = 10f;
        yield return WaitForDiscoveryAlert(second);
        Assert.That(first.AlertSecondsRemaining, Is.LessThan(second.AlertSecondsRemaining - 0.4f));
        Assert.That(first.vision.CanSee(driver.player) && second.vision.CanSee(driver.player), Is.True);
        int firstRound = first.CurrentRound;
        int secondRound = second.CurrentRound;
        int health = driver.player.health.CurrentHealth;
        string items = ItemSnapshot();
        string placements = PlacementSnapshot();
        Vector3 firstPosition = first.Body.position;
        Vector3 secondPosition = second.Body.position;
        float firstRemaining = first.AlertSecondsRemaining;
        float secondRemaining = second.AlertSecondsRemaining;
        float timerDifference = secondRemaining - firstRemaining;
        float englishStartedAt = Time.time;

        yield return Press(Key.F10);
        Assert.That(language.CurrentLanguage, Is.EqualTo(GameLanguage.English));
        Assert.That(driver.statusText.text, Does.Contain("Enemy test round"));
        string englishAlert = language.GetText("enemy.state.alert");
        Assert.That(englishAlert, Is.Not.EqualTo("enemy.state.alert"));
        Assert.That(driver.statusText.text, Does.Contain(englishAlert));
        Assert.That(first.CurrentState, Is.EqualTo(DroneState.Alert));
        Assert.That(second.CurrentState, Is.EqualTo(DroneState.Alert));
        AssertCountdownContinues(first, firstRemaining, Time.time - englishStartedAt);
        AssertCountdownContinues(second, secondRemaining, Time.time - englishStartedAt);
        Assert.That(second.AlertSecondsRemaining - first.AlertSecondsRemaining, Is.EqualTo(timerDifference).Within(0.02f),
            "Two drones keep their independent timer offsets through localization.");
        AssertPureOrangeAlert(first);
        AssertPureOrangeAlert(second);

        GameObject blocker = new GameObject("Alert F10 temporary wall");
        temporaryObjects.Add(blocker);
        blocker.transform.SetParent(driver.transform, false);
        Vector3 playerCenter = driver.player.GetComponent<CapsuleCollider>().bounds.center;
        blocker.transform.position = Vector3.Lerp(second.vision.transform.position, playerCenter, 0.45f);
        blocker.AddComponent<BoxCollider>().size = new Vector3(0.15f, 0.3f, 2f);
        Physics.SyncTransforms();
        Assert.That(first.vision.CanSee(driver.player), Is.True, "The temporary wall must leave the first real sightline open.");
        Assert.That(second.vision.CanSee(driver.player), Is.False, "The second sightline must be blocked by a real 3D wall.");
        Vector3 lastKnown = second.LastKnownPosition;
        float limit = Time.realtimeSinceStartup + 2f;
        while (second.CurrentState != DroneState.Search && Time.realtimeSinceStartup < limit) { yield return null; }
        Assert.That(second.CurrentState, Is.EqualTo(DroneState.Search), "Ordinary wall loss enters Search; it is not cover alert or old fill decay.");
        Assert.That(second.IsCoverAlert, Is.False);
        Assert.That(second.AlertSecondsRemaining, Is.Zero);
        Assert.That(second.LastKnownPosition, Is.EqualTo(lastKnown));
        firstRemaining = first.AlertSecondsRemaining;
        float chineseStartedAt = Time.time;

        yield return Press(Key.F10);
        Assert.That(language.CurrentLanguage, Is.EqualTo(GameLanguage.Chinese));
        Assert.That(driver.statusText.text, Does.Contain("敌人测试轮次"));
        string chineseAlert = language.GetText("enemy.state.alert");
        Assert.That(chineseAlert, Is.Not.EqualTo("enemy.state.alert"));
        Assert.That(driver.statusText.text, Does.Contain(chineseAlert));
        Assert.That(first.CurrentState, Is.EqualTo(DroneState.Alert));
        Assert.That(second.CurrentState, Is.EqualTo(DroneState.Search));
        AssertCountdownContinues(first, firstRemaining, Time.time - chineseStartedAt);
        Assert.That(second.AlertSecondsRemaining, Is.Zero);
        Assert.That(second.IsCoverAlert, Is.False);
        Assert.That(second.LastKnownPosition, Is.EqualTo(lastKnown));
        AssertPureOrangeAlert(first);
        Assert.That(second.vision.GetComponent<MeshRenderer>().enabled, Is.True);
        Assert.That(second.vision.RangeCollider.enabled, Is.True);
        Assert.That(first.CurrentRound, Is.EqualTo(firstRound));
        Assert.That(second.CurrentRound, Is.EqualTo(secondRound));
        Assert.That(first.HasExploded || second.HasExploded, Is.False);
        Assert.That(Vector3.Distance(first.Body.position, firstPosition), Is.LessThan(0.002f));
        Assert.That(Vector3.Distance(second.Body.position, secondPosition), Is.LessThan(0.002f));
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health));
        Assert.That(ItemSnapshot(), Is.EqualTo(items));
        Assert.That(PlacementSnapshot(), Is.EqualTo(placements));
    }

    [UnityTest]
    public IEnumerator RealPrefabClearsTheOldRadiusHeadGapAndOnlyBodyContactExplodesPatrolOnce()
    {
        yield return LoadScene();
        PlaceAlertPlayer(new Vector3(-1f, 8f, 0f));
        DroneController drone = PrepareAlertDrone(driver.drones[0], new Vector3(-4f, 8f, 0f));
        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(drone.gameObject),
            Is.EqualTo("Assets/Prefabs/Enemy/Drone/Drone.prefab"));
        SphereCollider contact = drone.GetComponent<SphereCollider>();
        CapsuleCollider capsule = driver.player.GetComponent<CapsuleCollider>();
        Assert.That(contact.radius, Is.EqualTo(0.25f).Within(0.0001f), "Use the saved prefab radius, without repairing it in the fixture.");
        Assert.That(contact.center, Is.EqualTo(Vector3.zero));
        Assert.That(contact.isTrigger, Is.False);
        Assert.That((drone.collisionMask.value & (1 << capsule.gameObject.layer)) != 0, Is.True);
        Rigidbody playerBody = driver.player.Body;
        playerBody.isKinematic = false;
        playerBody.useGravity = false;
        playerBody.linearVelocity = Vector3.zero;
        playerBody.angularVelocity = Vector3.zero;
        playerBody.constraints = RigidbodyConstraints.FreezeAll;
        Physics.SyncTransforms();
        float radius = contact.bounds.extents.x;
        Assert.That(radius, Is.EqualTo(0.25f).Within(0.0001f));
        int health = driver.player.health.CurrentHealth;
        int damageEvents = 0;
        System.Action<int> onDamage = _ => damageEvents++;
        driver.player.health.NonLethalDamaged += onDamage;
        try
        {
            // 真实视锥Trigger已经接触玩家，但实体球仍相隔数米，不能因此引爆。
            MeshCollider range = drone.vision.RangeCollider;
            Assert.That(range.enabled && range.isTrigger, Is.True);
            Assert.That(Physics.ComputePenetration(capsule, capsule.transform.position, capsule.transform.rotation,
                range, range.transform.position, range.transform.rotation, out _, out _), Is.True);
            Assert.That(drone.vision.CanSee(driver.player), Is.True);
            for (int i = 0; i < 3; i++) { yield return new WaitForFixedUpdate(); }
            Assert.That(drone.HasExploded, Is.False);
            Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health));
            Assert.That(damageEvents, Is.Zero);

            // 仅屏蔽本机运行时的目标选择，让真实Patrol持续横越；检测Trigger和实体碰撞保持开启。
            drone.vision.playerMask = 0;
            drone.config.patrolSpeed = 2f;
            drone.config.patrolWaitSeconds = 10f;
            Vector3 playerCenter = capsule.bounds.center;
            float flightY = capsule.bounds.max.y + 0.35f;
            Vector3 start = new Vector3(playerCenter.x - 1.5f, flightY, playerCenter.z);
            Vector3 finish = new Vector3(playerCenter.x + 1.5f, flightY, playerCenter.z);
            GameObject endpoint = new GameObject("Low-flight clearance patrol endpoint");
            temporaryObjects.Add(endpoint);
            endpoint.transform.SetParent(driver.transform, false);
            endpoint.transform.position = finish;
            drone.patrolPoints = new[] { endpoint.transform };
            Assert.That(drone.BeginRound(drone.CurrentRound + 1), Is.True);
            drone.Body.position = start;
            drone.transform.position = start;
            Physics.SyncTransforms();
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
            Assert.That(Physics.SphereCastAll(start, radius, Vector3.right, finish.x - start.x,
                drone.collisionMask, QueryTriggerInteraction.Ignore)
                .Any(hit => hit.collider.GetComponentInParent<PlayerController>() == driver.player), Is.False);
            Assert.That(Physics.SphereCastAll(start, 0.45f, Vector3.right, finish.x - start.x,
                drone.collisionMask, QueryTriggerInteraction.Ignore)
                .Any(hit => hit.collider.GetComponentInParent<PlayerController>() == driver.player), Is.True,
                "The identical actual player geometry would block the previous 0.45m body radius.");
            float nearestClearance = float.PositiveInfinity;
            float limit = Time.realtimeSinceStartup + 4f;
            while (drone.Body.position.x < finish.x - drone.config.arrivalDistance - 0.04f
                && Time.realtimeSinceStartup < limit)
            {
                yield return new WaitForFixedUpdate();
                Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
                Assert.That(drone.IsBlocked, Is.False, "The real movement sweep must clear the player's head.");
                Assert.That(drone.HasExploded, Is.False);
                Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health));
                nearestClearance = Mathf.Min(nearestClearance,
                    Vector3.Distance(capsule.ClosestPoint(drone.Body.position), drone.Body.position));
                Assert.That(Physics.ComputePenetration(contact, drone.Body.position, drone.Body.rotation,
                    capsule, playerBody.position, playerBody.rotation, out _, out _), Is.False);
            }
            Assert.That(drone.Body.position.x, Is.GreaterThan(playerCenter.x + 0.75f), "Patrol must actually cross over the player.");
            Assert.That(nearestClearance, Is.GreaterThan(radius + 0.04f).And.LessThan(0.45f));
            Assert.That(damageEvents, Is.Zero);
            Assert.That(range.enabled && range.isTrigger, Is.True);

            // 动态玩家冻结位置；停留中的Patrol机身用真实刚体小步降到接触边缘，不直接调用伤害入口。
            drone.config.patrolSpeed = 0f;
            drone.patrolPoints = new[] { drone.transform };
            Assert.That(drone.BeginRound(drone.CurrentRound + 1), Is.True);
            Vector3 approach = new Vector3(playerCenter.x, capsule.bounds.max.y + 0.35f, playerCenter.z);
            drone.Body.position = approach;
            drone.transform.position = approach;
            Physics.SyncTransforms();
            Assert.That(Physics.ComputePenetration(contact, drone.Body.position, drone.Body.rotation,
                capsule, playerBody.position, playerBody.rotation, out _, out _), Is.False);
            yield return new WaitForFixedUpdate();
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
            Assert.That(drone.HasExploded, Is.False);
            Assert.That(DroneController.CanTarget(driver.player), Is.True);
            for (int step = 1; step <= 12 && !drone.HasExploded; step++)
            {
                Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
                drone.Body.MovePosition(approach - Vector3.up * (step * 0.01f));
                yield return new WaitForFixedUpdate();
            }
            // 最多只进入表面0.02m；再给真正的FixedUpdate/碰撞派发一帧，不使用TryExplode或强制状态。
            yield return new WaitForFixedUpdate();
            Assert.That(drone.HasExploded, Is.True, "Actual Patrol body contact must explode automatically, even without a target.");
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Explode));
            Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health - drone.config.contactDamage));
            Assert.That(damageEvents, Is.EqualTo(1));
            Assert.That(contact.enabled, Is.False);
            yield return new WaitForSeconds(driver.player.config.damageInvulnerabilitySeconds + 0.05f);
            yield return new WaitForFixedUpdate();
            Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health - drone.config.contactDamage));
            Assert.That(damageEvents, Is.EqualTo(1), "Once-only explosion must not rely on the player's temporary invulnerability.");
        }
        finally
        {
            driver.player.health.NonLethalDamaged -= onDamage;
        }
    }


    [UnityTest]
    public IEnumerator SceneUsesRealModelFourRotatingPropellersCinemachineAndGeneratedVision()
    {
        yield return LoadScene();
        Assert.That(driver.drones.Length, Is.EqualTo(2));
        Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        Assert.That(driver.cameraBinder, Is.Not.Null);
        Assert.That(driver.cameraBinder.followCamera, Is.Not.Null);
        Assert.That(driver.cameraBinder.ValidateBounds(out string boundsError), Is.True, boundsError);
        BoxCollider bounds = driver.cameraBinder.confiner.BoundingVolume as BoxCollider;
        Assert.That(bounds, Is.Not.Null);
        Assert.That(bounds.gameObject.scene, Is.EqualTo(driver.gameObject.scene));
        Assert.That(bounds.isTrigger, Is.True);
        Camera camera = driver.cameraBinder.GetComponentInChildren<Camera>();
        Assert.That(camera, Is.Not.Null);
        Assert.That(camera.enabled, Is.True);
        Assert.That(camera.GetComponent<CinemachineBrain>(), Is.Not.Null);
        Assert.That(driver.cameraBinder.followCamera.Follow, Is.EqualTo(driver.player.cameraTarget));

        foreach (DroneController drone in driver.drones)
        {
            Assert.That(drone.BeginRound(drone.CurrentRound + 1), Is.True);
            DroneFlightVisual flight = drone.flightVisual;
            Assert.That(flight, Is.Not.Null);
            Assert.That(flight.visual, Is.SameAs(drone.visual));
            Assert.That(flight.propellers.Length, Is.EqualTo(4));
            Assert.That(flight.propellers.All(propeller => propeller != null), Is.True);
            Assert.That(flight.propellers.Select(propeller => propeller.name).Distinct().Count(), Is.EqualTo(4));
            Assert.That(drone.vision.visionOrigin, Is.Not.Null, "The saved Drone prefab must bind its model vision origin.");
            Assert.That(drone.vision.visionOrigin.IsChildOf(drone.visual), Is.True);
            Assert.That(drone.vision.directionDegrees, Is.EqualTo(0f), "Extra pitch stays zero so vision remains level.");
            DroneStatusLight statusLight = drone.GetComponent<DroneStatusLight>();
            Assert.That(statusLight, Is.Not.Null);
            Assert.That(statusLight.targetLight, Is.Not.Null);
            Assert.That(statusLight.targetLight.type, Is.EqualTo(LightType.Point));
            Assert.That(statusLight.targetLight.transform.IsChildOf(drone.visual), Is.True,
                "The status effect must use this drone's existing model light, which hides with the model on explosion.");
            foreach (Transform propeller in flight.propellers)
            {
                MeshFilter filter = propeller.GetComponentInChildren<MeshFilter>();
                Assert.That(filter, Is.Not.Null);
                Assert.That(filter.sharedMesh, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(filter.sharedMesh), Is.EqualTo("Assets/Art/Model/Enemy_Patrol_Drone_A.fbx"));
                Assert.That(propeller.GetComponentInChildren<MeshRenderer>(), Is.Not.Null);
            }
            Assert.That(drone.vision.RangeCollider.convex && drone.vision.RangeCollider.isTrigger, Is.True);
            Assert.That(drone.vision.RangeCollider.sharedMesh, Is.Not.Null);
            MeshRenderer fan = drone.vision.GetComponent<MeshRenderer>();
            Assert.That(fan.enabled, Is.True);
            Assert.That(fan.sharedMaterial.shader.name, Is.EqualTo("MyPrecious/DroneVision"));
            Assert.That(fan.sharedMaterial.HasProperty("_BaseColor"), Is.True);
            Assert.That(fan.sharedMaterial.HasProperty("_FillColor"), Is.True);
            Assert.That(fan.sharedMaterial.HasProperty("_FillProgress"), Is.True);
            Assert.That(fan.sharedMaterial.GetFloat("_Cull"), Is.EqualTo(2f));
            Assert.That(drone.vision.GetComponent<MeshFilter>().sharedMesh, Is.Not.Null);
        }
        DroneFlightVisual first = driver.drones[0].flightVisual;
        Quaternion[] rotations = first.propellers.Select(propeller => propeller.localRotation).ToArray();
        yield return new WaitForSeconds(0.07f);
        for (int i = 0; i < rotations.Length; i++)
        {
            Assert.That(Quaternion.Angle(rotations[i], first.propellers[i].localRotation), Is.GreaterThan(0.1f));
        }
        Assert.That(driver.drones[0].HasExploded || driver.drones[1].HasExploded, Is.False);
    }

    private IEnumerator LoadScene()
    {
        loadedScene = true;
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
        persistentRoot = GameBootstrap.Instance != null ? GameBootstrap.Instance.gameObject : null;
        driver = Object.FindFirstObjectByType<EnemyTestDriver>();
        Assert.That(driver, Is.Not.Null);
        foreach (DroneController drone in driver.drones) { drone.gameObject.SetActive(false); }
        yield return null;
        yield return null;
        Assert.That(driver.IsReady, Is.True);
        // Start可能首次激活敌人，再次停用后才开始各自的受控验证。
        foreach (DroneController drone in driver.drones) { drone.gameObject.SetActive(false); }
        Assert.That(persistentRoot, Is.Not.Null);
        yield return new WaitForFixedUpdate();
    }

    private IEnumerator OpenSearch()
    {
        Assert.That(driver.lootBox.TryGetInteractionTrigger(out BoxCollider trigger), Is.True,
            "The saved scene must inherit the upgraded explicit TriggerBox; tests do not add a replacement.");
        Vector3 nearBox = driver.lootBox.transform.position + Vector3.left * 0.9f;
        nearBox.y = 0.05f;
        nearBox.z = driver.player.transform.position.z;
        Assert.That(driver.player.RelocateWithinRun(nearBox), Is.True);
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        yield return null;
        CapsuleCollider body = driver.player.GetComponent<CapsuleCollider>();
        Assert.That(Physics.ComputePenetration(body, body.transform.position, body.transform.rotation,
            trigger, trigger.transform.position, trigger.transform.rotation, out _, out _), Is.True,
            "The real player body must overlap the authored TriggerBox, independently of legacy distance fields.");
        Assert.That(driver.lootUI.TryOpenSource(driver.lootBox.State, driver.lootBox, driver.player.interaction), Is.True,
            "Open through the current real TriggerBox and wall check, without rewriting its shape or layer masks.");
        yield return null;
        AssertSearchUiOpen();
    }

    private void AssertSearchUiOpen()
    {
        Assert.That(driver.backpackUI.IsOpen, Is.True, "The real backpack must remain open. " + FocusContext());
        Assert.That(driver.lootUI.IsOpen, Is.True, "The real loot search must remain open. " + FocusContext());
    }

    private static string FocusContext()
    {
        EditorWindow focused = EditorWindow.focusedWindow;
        EventSystem events = EventSystem.current;
        return "Application.isFocused=" + Application.isFocused
            + "; EventSystem.isFocused=" + (events != null ? events.isFocused.ToString() : "<none>")
            + "; EditorWindow.focusedWindow=" + (focused != null ? focused.GetType().FullName : "<none>");
    }

    private DroneController PrepareContact(DroneController drone)
    {
        Assert.That(drone.BeginRound(drone.CurrentRound + 1), Is.True);
        // 此夹具只验证既有伤害/关包事件链；自然发现与定时Alert由独立场景用例验证。
        drone.ChangeState(DroneState.Chase);
        Vector3 center = driver.player.GetComponent<CapsuleCollider>().bounds.center;
        drone.Body.position = center;
        drone.transform.position = center;
        Physics.SyncTransforms();
        return drone;
    }

    private DroneController PrepareAlertDrone(DroneController drone, Vector3 position)
    {
        Assert.That(drone.config.discoveryAlertSeconds, Is.EqualTo(0.5f), "The authored discovery alert must be half a second.");
        Assert.That(drone.config.coverAlertSeconds, Is.EqualTo(2f), "The authored cover alert must be two seconds.");
        DroneConfigSO settings = Object.Instantiate(drone.config);
        settings.hideFlags = HideFlags.HideAndDontSave;
        temporaryObjects.Add(settings);
        settings.patrolSpeed = 0f;
        settings.chaseSpeed = 0f;
        drone.config = settings;
        drone.initialFacing = 1;
        drone.patrolPoints = new[] { drone.transform };
        Assert.That(drone.BeginRound(drone.CurrentRound + 1), Is.True);
        drone.Body.position = position;
        drone.transform.position = position;
        drone.vision.SetHeading(drone.FacingRotation);
        Physics.SyncTransforms();
        return drone;
    }

    private void PlaceAlertPlayer(Vector3 center)
    {
        CapsuleCollider body = driver.player.GetComponent<CapsuleCollider>();
        Vector3 centerOffset = body.bounds.center - driver.player.Position;
        Assert.That(driver.player.RelocateWithinRun(center - centerOffset), Is.True);
        // 只固定运行时夹具位置，保留真实玩家启用、生命及可发现资格，不设置流程锁。
        driver.player.Body.isKinematic = true;
        Physics.SyncTransforms();
        Assert.That(DroneController.CanTarget(driver.player), Is.True);
    }

    private IEnumerator WaitForDiscoveryAlert(DroneController drone)
    {
        float limit = Time.realtimeSinceStartup + drone.config.discoveryAlertSeconds + 2f;
        while (drone.CurrentState != DroneState.Alert && Time.realtimeSinceStartup < limit)
        {
            Assert.That(drone.CurrentState, Is.Not.EqualTo(DroneState.Chase), "Natural detection must enter Alert before Chase.");
            Assert.That(drone.HasExploded, Is.False);
            yield return null;
        }
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
        Assert.That(drone.IsCoverAlert, Is.False);
        Assert.That(drone.AlertSecondsRemaining, Is.GreaterThan(0f));
        Assert.That(drone.AlertSecondsRemaining, Is.LessThanOrEqualTo(drone.config.discoveryAlertSeconds));
    }

    private IEnumerator WaitForAlertElapsed(DroneController drone, float elapsed)
    {
        float target = drone.config.discoveryAlertSeconds - elapsed;
        float limit = Time.realtimeSinceStartup + elapsed + 2f;
        while (drone.AlertSecondsRemaining > target && Time.realtimeSinceStartup < limit)
        {
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
            yield return null;
        }
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
        Assert.That(drone.AlertSecondsRemaining, Is.GreaterThan(0f));
        Assert.That(drone.AlertSecondsRemaining, Is.LessThanOrEqualTo(target));
    }

    private static void AssertCountdownContinues(DroneController drone, float before, float elapsed)
    {
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
        Assert.That(drone.IsCoverAlert, Is.False);
        Assert.That(drone.AlertSecondsRemaining, Is.GreaterThan(0f));
        Assert.That(drone.AlertSecondsRemaining, Is.LessThan(before));
        Assert.That(drone.AlertSecondsRemaining, Is.EqualTo(before - elapsed).Within(Time.deltaTime + 0.02f),
            "Queued localization input must neither pause nor restart the running countdown.");
    }

    private static void AssertPureOrangeAlert(DroneController drone)
    {
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
        Assert.That(drone.vision.enabled && drone.vision.gameObject.activeInHierarchy, Is.True);
        Assert.That(drone.vision.RangeCollider.enabled, Is.True);
        Assert.That(drone.vision.GetComponent<MeshRenderer>().enabled, Is.True);
        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        drone.vision.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
        Assert.That(properties.GetFloat("_FillProgress"), Is.Zero, "Timed alert must not revive the previous red-fill mechanic.");
        Color tint = properties.GetColor("_BaseColor");
        Assert.That(tint.r, Is.GreaterThan(0.9f));
        Assert.That(tint.g, Is.InRange(0.4f, 0.8f));
        Assert.That(tint.b, Is.LessThan(0.25f));
    }

    private IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
    }

    private IEnumerator Pointer(Vector2 point, bool left)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = (ushort)(left ? 1 : 0) });
        yield return null;
        yield return null;
    }

    private Vector2 BackpackPoint(int x, int y)
    {
        Canvas.ForceUpdateCanvases();
        BackpackView view = driver.backpackUI.view;
        return RectTransformUtility.WorldToScreenPoint(null, view.gridRoot.TransformPoint(new Vector3(
            view.padding + x * (view.cellSize + view.gap) + view.cellSize * 0.35f,
            -view.padding - y * (view.cellSize + view.gap) - view.cellSize * 0.35f, 0)));
    }

    private string ItemSnapshot()
    {
        return string.Join("|", driver.Items.GetInstances().OrderBy(item => item.InstanceId).Select(item =>
            item.InstanceId + ":" + item.DefinitionId + ":" + item.UniqueId + ":" + item.Owner + ":" + item.ContainerId
            + ":" + item.OriginalSpawnPointId + ":" + item.UsesThisRound + ":" + item.IsRetained));
    }

    private string PlacementSnapshot()
    {
        return string.Join("|", driver.Backpack.GetPlacements().OrderBy(placement => placement.InstanceId)
            .Select(placement => placement.InstanceId + ":" + placement.X + ":" + placement.Y + ":" + placement.Rotated));
    }
}
#endif
