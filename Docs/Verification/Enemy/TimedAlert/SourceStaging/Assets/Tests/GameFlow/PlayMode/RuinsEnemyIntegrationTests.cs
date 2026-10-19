#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// 真实Bootstrap流程启动敌人；API定位只用于接触夹具，不以强制状态或逐机BeginRound替代接入。
public class RuinsEnemyIntegrationTests
{
    private const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";
    private const string ShelterPath = "Assets/Scenes/Shelter.unity";
    private const string RuinsPath = "Assets/Scenes/Ruins.unity";
    private GameFlowManager flow;
    private GameObject persistentRoot;
    private UnityAction<Scene, LoadSceneMode> onLoaded;
    private readonly List<Scene> fixtures = new List<Scene>();
    private bool started;
    private bool originalBackground;

    [SetUp]
    public void SetUp()
    {
        started = false;
        flow = null;
        persistentRoot = null;
        onLoaded = null;
        originalBackground = Application.runInBackground;
        if (GameBootstrap.Instance != null)
        {
            Assert.Ignore("Run from a clean Test Runner scene, without a user session.");
        }
        Application.runInBackground = true;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (onLoaded != null) { SceneManager.sceneLoaded -= onLoaded; }
        if (flow != null) { flow.StopAllCoroutines(); }
        foreach (Scene fixture in fixtures)
        {
            if (fixture.IsValid() && fixture.isLoaded) { yield return SceneManager.UnloadSceneAsync(fixture); }
        }
        fixtures.Clear();
        if (started)
        {
            foreach (string path in new[] { RuinsPath, ShelterPath, BootstrapPath })
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                if (scene.IsValid() && scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(scene); }
            }
            if (persistentRoot == null && GameBootstrap.Instance != null) { persistentRoot = GameBootstrap.Instance.gameObject; }
            if (persistentRoot != null) { Object.Destroy(persistentRoot); }
        }
        Application.runInBackground = originalBackground;
        yield return null;
    }

    [UnityTest]
    public IEnumerator TwoFormalDeparturesStartPatrolWithTheAuthoredSpawnRouteAndSharedRound()
    {
        yield return Boot();
        PlayerController player = flow.Player;
        Vector3 authoredSpawn = default;
        Vector3[] authoredRoute = null;
        DroneController previous = null;
        int loads = 0;
        onLoaded = (scene, mode) =>
        {
            if (scene.path != RuinsPath) { return; }
            DroneController drone = Drones(scene).Single();
            Assert.That(drone.transform.parent, Is.Null, "The user's root-level drone must be supported.");
            Assert.That(drone.CurrentRound, Is.Zero, "Loading alone must not start a round.");
            Assert.That(player.IsSceneTransitionBlocked, Is.True);
            if (loads++ == 0)
            {
                authoredSpawn = drone.transform.position;
                authoredRoute = drone.patrolPoints.Select(point => point.position).ToArray();
            }
            else
            {
                Assert.That(drone.transform.position, Is.EqualTo(authoredSpawn));
                CollectionAssert.AreEqual(authoredRoute, drone.patrolPoints.Select(point => point.position).ToArray());
            }
        };
        SceneManager.sceneLoaded += onLoaded;
        for (int round = 1; round <= 2; round++)
        {
            Assert.That(flow.TryDepart(), Is.True);
            yield return Stage(GameFlowStage.Exploring);
            DroneController drone = Drones(SceneManager.GetSceneByPath(RuinsPath)).Single();
            Assert.That(drone, Is.Not.SameAs(previous));
            Assert.That(drone.CurrentRound, Is.EqualTo(round));
            Assert.That(flow.CurrentRound, Is.EqualTo(round));
            Assert.That(flow.Loot.PreparedRound, Is.EqualTo(round));
            Assert.That(player.health.CurrentRound, Is.EqualTo(round));
            Assert.That(drone.IsRunning && drone.Body.isKinematic, Is.True);
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
            Assert.That(drone.HasExploded, Is.False);
            Vector3 before = drone.Body.position;
            for (int frame = 0; frame < 10; frame++) { yield return new WaitForFixedUpdate(); }
            Vector3 displacement = drone.Body.position - before;
            Assert.That(displacement.magnitude, Is.GreaterThan(0.05f));
            Assert.That(Vector3.Dot(displacement, authoredRoute[0] - before), Is.GreaterThan(0f));
            Assert.That(drone.Body.position.z, Is.EqualTo(authoredSpawn.z).Within(0.0001f));
            CollectionAssert.AreEqual(authoredRoute, drone.patrolPoints.Select(point => point.position).ToArray());
            previous = drone;
            Assert.That(flow.TryExtract(), Is.True);
            Assert.That(player.IsSceneTransitionBlocked, Is.True);
            yield return Stage(GameFlowStage.Shelter);
            Assert.That(previous == null, Is.True, "The old map instance must be unloaded before unlocking.");
            Assert.That(flow.Player, Is.SameAs(player));
            Assert.That(flow.SettledRound, Is.EqualTo(round));
        }
        Assert.That(loads, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator RebindingPreservesNaturalAlertAndExplosionUntilTheNextRealDeparture()
    {
        yield return Boot();
        Assert.That(flow.TryDepart(), Is.True);
        yield return Stage(GameFlowStage.Exploring);
        Scene scene = SceneManager.GetSceneByPath(RuinsPath);
        DroneController drone = Drones(scene).Single();
        GameFlowSceneBindings bindings = Bindings(scene);
        PlayerController player = flow.Player;
        RigidbodyConstraints constraints = player.Body.constraints;
        // 等实际左右转向完成，再把玩家放在可见位置；保留原速度、2秒警戒和出生Z。
        yield return new WaitForSeconds(0.6f);
        Vector3 visible = drone.Body.position + Vector3.right * drone.FacingDirection * 2f;
        visible.y -= player.GetComponent<CapsuleCollider>().center.y;
        Assert.That(player.RelocateWithinRun(visible), Is.True);
        player.Body.constraints = RigidbodyConstraints.FreezeAll;
        Physics.SyncTransforms();
        yield return Until(() => drone.CurrentState == DroneState.Alert && drone.AlertProgress >= 0.2f, 6f);
        Vector3 position = drone.Body.position;
        float progress = drone.AlertProgress;
        PlayerController target = drone.Target;
        for (int i = 0; i < 3; i++)
        {
            Assert.That(bindings.Activate(flow, player, out string error), Is.True, error);
            Assert.That(bindings.TryBeginExplorationEnemies(flow.CurrentRound, out error), Is.True, error);
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
            Assert.That(drone.AlertProgress, Is.EqualTo(progress));
            Assert.That(drone.Body.position, Is.EqualTo(position));
            Assert.That(drone.Target, Is.SameAs(target));
        }
        int hits = 0;
        int health = player.health.CurrentHealth;
        DroneState damageState = DroneState.None;
        player.health.NonLethalDamaged += amount => { hits++; damageState = drone.CurrentState; };
        // 让自然Chase飞到玩家发生接触；在伤害回调取状态，避免低帧率漏采短暂Chase帧。
        yield return Until(() => drone.HasExploded, 8f,
            () => "state=" + drone.CurrentState + ", progress=" + drone.AlertProgress
                + ", drone=" + drone.Body.position + ", player=" + player.Position
                + ", visible=" + drone.vision.CanSee(player) + ", blocked=" + player.IsSceneTransitionBlocked);
        Assert.That(hits, Is.EqualTo(1));
        Assert.That(damageState, Is.EqualTo(DroneState.Chase));
        Assert.That(player.health.CurrentHealth, Is.EqualTo(health - drone.config.contactDamage));
        for (int i = 0; i < 3; i++)
        {
            Assert.That(bindings.Activate(flow, player, out string error), Is.True, error);
            Assert.That(bindings.TryBeginExplorationEnemies(flow.CurrentRound, out error), Is.True, error);
            Assert.That(drone.HasExploded, Is.True);
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Explode));
            Assert.That(drone.IsRunning, Is.False);
            Assert.That(drone.GetComponent<SphereCollider>().enabled, Is.False);
        }
        yield return new WaitForSeconds(player.config.damageInvulnerabilitySeconds + 0.1f);
        Assert.That(hits, Is.EqualTo(1));
        player.Body.constraints = constraints;
        Assert.That(flow.TryExtract(), Is.True);
        yield return Stage(GameFlowStage.Shelter);
        Assert.That(flow.TryDepart(), Is.True);
        yield return Stage(GameFlowStage.Exploring);
        DroneController next = Drones(SceneManager.GetSceneByPath(RuinsPath)).Single();
        Assert.That(next.CurrentRound, Is.EqualTo(2));
        Assert.That(next.HasExploded, Is.False);
        Assert.That(next.CurrentState, Is.EqualTo(DroneState.Patrol));
        Assert.That(player.health.CurrentRound, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator BindingFailureLeavesEnemiesUnstartedAndRetryUsesTheAllocatedRound()
    {
        yield return Boot();
        GameFlowSceneBindings failed = null;
        Transform spawn = null;
        onLoaded = (scene, mode) =>
        {
            if (scene.path != RuinsPath) { return; }
            failed = Bindings(scene);
            spawn = failed.spawnPoint;
            failed.spawnPoint = null;
        };
        SceneManager.sceneLoaded += onLoaded;
        LogAssert.Expect(LogType.Warning, new Regex("GameFlow: 场景接线不完整"));
        Assert.That(flow.TryDepart(), Is.True);
        yield return Stage(GameFlowStage.Failed);
        DroneController drone = Drones(failed.gameObject.scene).Single();
        Vector3 position = drone.Body.position;
        Assert.That(drone.CurrentRound, Is.Zero);
        Assert.That(drone.IsRunning, Is.False);
        Assert.That(flow.Player.health.CurrentRound, Is.Zero);
        Assert.That(flow.Player.IsSceneTransitionBlocked, Is.True);
        yield return new WaitForSeconds(0.1f);
        Assert.That(drone.Body.position, Is.EqualTo(position));
        int handle = failed.gameObject.scene.handle;
        failed.spawnPoint = spawn;
        flow.Retry();
        yield return Stage(GameFlowStage.Exploring);
        Assert.That(drone.gameObject.scene.handle, Is.EqualTo(handle));
        Assert.That(drone.CurrentRound, Is.EqualTo(1));
        Assert.That(flow.CurrentRound, Is.EqualTo(1));
        Assert.That(flow.Player.health.CurrentRound, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator InvalidEnemyConfigurationFailsBeforeAnyStartupAndRetryKeepsOneRound()
    {
        yield return Boot();
        DroneController drone = null;
        DroneController invalid = null;
        DroneConfigSO config = null;
        onLoaded = (scene, mode) =>
        {
            if (scene.path != RuinsPath) { return; }
            drone = Drones(scene).Single();
            invalid = Object.Instantiate(drone);
            SceneManager.MoveGameObjectToScene(invalid.gameObject, scene);
            config = invalid.config;
            invalid.config = null;
        };
        SceneManager.sceneLoaded += onLoaded;
        LogAssert.Expect(LogType.Warning, new Regex("GameFlow: 探索无人机缺少配置或轮次超前"));
        Assert.That(flow.TryDepart(), Is.True);
        yield return Stage(GameFlowStage.Failed);
        Assert.That(drone.CurrentRound, Is.Zero);
        Assert.That(invalid.CurrentRound, Is.Zero);
        Assert.That(flow.Player.health.CurrentRound, Is.EqualTo(1));
        Assert.That(flow.Player.IsSceneTransitionBlocked, Is.True);
        Assert.That(DroneController.CanTarget(flow.Player), Is.False);
        int health = flow.Player.health.CurrentHealth;
        yield return new WaitForSeconds(0.1f);
        Assert.That(flow.Player.health.CurrentHealth, Is.EqualTo(health));
        invalid.config = config;
        flow.Retry();
        yield return Stage(GameFlowStage.Exploring);
        Assert.That(flow.CurrentRound, Is.EqualTo(1));
        Assert.That(drone.CurrentRound, Is.EqualTo(1));
        Assert.That(invalid.CurrentRound, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator EmptyShelterOtherSceneAndDisabledEnemiesRespectStartupScopeAndRoundGuards()
    {
        GameFlowSceneBindings target = Fixture("Target");
        GameFlowSceneBindings other = Fixture("Other");
        Assert.That(target.TryBeginExplorationEnemies(1, out string error), Is.True, error);
        DroneController outside = FixtureDrone(other.gameObject.scene);
        DroneController active = FixtureDrone(target.gameObject.scene);
        DroneController disabled = FixtureDrone(target.gameObject.scene);
        disabled.enabled = false;
        DroneController inactive = FixtureDrone(target.gameObject.scene);
        inactive.gameObject.SetActive(false);
        target.isShelter = true;
        Assert.That(target.TryBeginExplorationEnemies(1, out error), Is.True, error);
        Assert.That(active.CurrentRound, Is.Zero);
        target.isShelter = false;
        Assert.That(target.TryBeginExplorationEnemies(1, out error), Is.True, error);
        Assert.That(active.CurrentRound, Is.EqualTo(1));
        Assert.That(outside.CurrentRound, Is.Zero);
        Assert.That(disabled.CurrentRound, Is.Zero);
        Assert.That(disabled.enabled, Is.False);
        Assert.That(inactive.CurrentRound, Is.Zero);
        Assert.That(inactive.gameObject.activeSelf, Is.False);
        Assert.That(target.TryBeginExplorationEnemies(0, out error), Is.False);
        Assert.That(target.TryBeginExplorationEnemies(2, out error), Is.True, error);
        Vector3 before = active.Body.position;
        Assert.That(target.TryBeginExplorationEnemies(1, out error), Is.False);
        Assert.That(active.CurrentRound, Is.EqualTo(2));
        Assert.That(active.Body.position, Is.EqualTo(before));
        yield return null;
    }

    private IEnumerator Boot()
    {
        started = true;
        UnityAction<Scene, LoadSceneMode> activate = (scene, mode) =>
        {
            if (scene.path == BootstrapPath) { SceneManager.SetActiveScene(scene); }
        };
        SceneManager.sceneLoaded += activate;
        try
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(BootstrapPath, new LoadSceneParameters(LoadSceneMode.Additive));
        }
        finally { SceneManager.sceneLoaded -= activate; }
        flow = Object.FindFirstObjectByType<GameFlowManager>();
        Assert.That(flow, Is.Not.Null);
        persistentRoot = GameBootstrap.Instance.gameObject;
        yield return Stage(GameFlowStage.Shelter);
    }

    private IEnumerator Stage(GameFlowStage expected)
    {
        yield return Until(() => flow.Stage == expected || flow.Stage == GameFlowStage.Failed, 20f);
        Assert.That(flow.Stage, Is.EqualTo(expected), flow.LastError);
    }

    private static IEnumerator Until(Func<bool> condition, float seconds, Func<string> detail = null)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) { yield return null; }
        Assert.That(condition(), Is.True, "Timed out waiting for the real flow/AI condition. " + (detail == null ? "" : detail()));
    }

    private static DroneController[] Drones(Scene scene)
    {
        return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<DroneController>(true)).ToArray();
    }

    private static GameFlowSceneBindings Bindings(Scene scene)
    {
        return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GameFlowSceneBindings>(true)).Single();
    }

    private GameFlowSceneBindings Fixture(string name)
    {
        Scene scene = SceneManager.CreateScene("Enemy scope " + name);
        fixtures.Add(scene);
        GameObject root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, scene);
        return root.AddComponent<GameFlowSceneBindings>();
    }

    private static DroneController FixtureDrone(Scene scene)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Drone/Drone.prefab");
        DroneController drone = Object.Instantiate(prefab).GetComponent<DroneController>();
        SceneManager.MoveGameObjectToScene(drone.gameObject, scene);
        GameObject point = new GameObject("Fixture patrol point");
        SceneManager.MoveGameObjectToScene(point, scene);
        point.transform.position = Vector3.right * 4f;
        drone.patrolPoints = new[] { point.transform };
        return drone;
    }
}
#endif
