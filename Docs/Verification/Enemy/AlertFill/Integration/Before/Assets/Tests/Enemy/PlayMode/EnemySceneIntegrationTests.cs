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
        finally { RestoreInput(); }
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
            Assert.That(fan.sharedMaterial.shader.name, Is.EqualTo("Universal Render Pipeline/Unlit"));
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
        drone.ChangeState(DroneState.Chase);
        Vector3 center = driver.player.GetComponent<CapsuleCollider>().bounds.center;
        drone.Body.position = center;
        drone.transform.position = center;
        Physics.SyncTransforms();
        return drone;
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
