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
        Vector3[] authoredSpawns = null;
        Vector3[][] authoredRoutes = null;
        DroneController[] previous = null;
        int loads = 0;
        onLoaded = (scene, mode) =>
        {
            if (scene.path != RuinsPath) { return; }
            DroneController[] drones = Drones(scene);
            Assert.That(drones.Length, Is.GreaterThan(0));
            Assert.That(drones.All(drone => drone.CurrentRound == 0), Is.True, "Loading alone must not start any scene drone.");
            Assert.That(player.IsSceneTransitionBlocked, Is.True);
            if (loads++ == 0)
            {
                // 用户可将多机放在普通父节点下；保存逐机出生和路线，不假设只有一个根级无人机。
                authoredSpawns = drones.Select(drone => drone.transform.position).ToArray();
                authoredRoutes = drones.Select(drone => drone.patrolPoints.Select(point => point.position).ToArray()).ToArray();
            }
            else
            {
                Assert.That(drones.Length, Is.EqualTo(authoredSpawns.Length));
                for (int i = 0; i < drones.Length; i++)
                {
                    Assert.That(drones[i].transform.position, Is.EqualTo(authoredSpawns[i]));
                    CollectionAssert.AreEqual(authoredRoutes[i], drones[i].patrolPoints.Select(point => point.position).ToArray());
                }
            }
        };
        SceneManager.sceneLoaded += onLoaded;
        for (int round = 1; round <= 2; round++)
        {
            Assert.That(flow.TryDepart(), Is.True);
            yield return Stage(GameFlowStage.Exploring);
            DroneController[] drones = Drones(SceneManager.GetSceneByPath(RuinsPath));
            Assert.That(drones.Length, Is.EqualTo(authoredSpawns.Length));
            Assert.That(flow.CurrentRound, Is.EqualTo(round));
            Assert.That(flow.Loot.PreparedRound, Is.EqualTo(round));
            Assert.That(player.health.CurrentRound, Is.EqualTo(round));
            foreach (DroneController drone in drones)
            {
                Assert.That(previous == null || previous.All(old => !ReferenceEquals(old, drone)), Is.True);
                Assert.That(drone.CurrentRound, Is.EqualTo(round));
                Assert.That(drone.IsRunning && drone.Body.isKinematic, Is.True);
                Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
                Assert.That(drone.HasExploded, Is.False);
                Assert.That(drone.AlertSecondsRemaining, Is.Zero);
                Assert.That(drone.IsCoverAlert, Is.False);
                Assert.That(drone.config.discoveryAlertSeconds, Is.EqualTo(0.5f));
                Assert.That(drone.config.coverAlertSeconds, Is.EqualTo(2f));
            }
            Vector3[] before = drones.Select(drone => drone.Body.position).ToArray();
            for (int frame = 0; frame < 10; frame++) { yield return new WaitForFixedUpdate(); }
            for (int i = 0; i < drones.Length; i++)
            {
                Vector3 displacement = drones[i].Body.position - before[i];
                Assert.That(displacement.magnitude, Is.GreaterThan(0.05f), drones[i].name);
                Assert.That(Vector3.Dot(displacement, authoredRoutes[i][0] - before[i]), Is.GreaterThan(0f), drones[i].name);
                Assert.That(drones[i].Body.position.z, Is.EqualTo(authoredSpawns[i].z).Within(0.0001f));
                CollectionAssert.AreEqual(authoredRoutes[i], drones[i].patrolPoints.Select(point => point.position).ToArray());
            }
            previous = drones;
            Assert.That(flow.TryExtract(), Is.True);
            Assert.That(player.IsSceneTransitionBlocked, Is.True);
            yield return Stage(GameFlowStage.Shelter);
            Assert.That(previous.All(drone => drone == null), Is.True, "All old map drone instances must unload before unlocking.");
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
        DroneController[] drones = Drones(scene);
        DroneController drone = drones.OrderBy(candidate => candidate.Body.position.x).First();
        DroneController[] others = drones.Where(candidate => candidate != drone).ToArray();
        Assert.That(others, Is.Not.Empty, "The current authored Ruins provides multiple drones for isolation coverage.");
        GameFlowSceneBindings bindings = Bindings(scene);
        PlayerController player = flow.Player;
        RigidbodyConstraints constraints = player.Body.constraints;
        // 等实际左右转向完成，再把玩家放在可见位置；保留原速度、0.5秒发现警戒和出生Z。
        yield return new WaitForSeconds(0.6f);
        Vector3 visible = drone.Body.position + Vector3.right * drone.FacingDirection * 2f;
        visible.y -= player.GetComponent<CapsuleCollider>().center.y;
        Assert.That(player.RelocateWithinRun(visible), Is.True);
        player.Body.constraints = RigidbodyConstraints.FreezeAll;
        Physics.SyncTransforms();
        Assert.That(drone.config.discoveryAlertSeconds, Is.EqualTo(0.5f));
        Assert.That(drone.config.coverAlertSeconds, Is.EqualTo(2f));
        yield return Until(() => drone.CurrentState == DroneState.Alert && drone.AlertSecondsRemaining > 0f, 6f);
        Assert.That(drone.IsCoverAlert, Is.False);
        Assert.That(drone.AlertSecondsRemaining, Is.LessThanOrEqualTo(drone.config.discoveryAlertSeconds));
        // 先消耗一部分真实时间，使同轮重绑若偷偷重置倒计时能被精确断言发现。
        yield return Until(() => drone.CurrentState != DroneState.Alert
            || drone.AlertSecondsRemaining < drone.config.discoveryAlertSeconds - 0.01f, 2f);
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
        Assert.That(drone.AlertSecondsRemaining, Is.GreaterThan(0f));
        Vector3 position = drone.Body.position;
        float secondsRemaining = drone.AlertSecondsRemaining;
        PlayerController target = drone.Target;
        DroneState[] otherStates = others.Select(other => other.CurrentState).ToArray();
        float[] otherTimers = others.Select(other => other.AlertSecondsRemaining).ToArray();
        Vector3[] otherPositions = others.Select(other => other.Body.position).ToArray();
        for (int i = 0; i < 3; i++)
        {
            Assert.That(bindings.Activate(flow, player, out string error), Is.True, error);
            Assert.That(bindings.TryBeginExplorationEnemies(flow.CurrentRound, out error), Is.True, error);
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
            Assert.That(drone.AlertSecondsRemaining, Is.EqualTo(secondsRemaining));
            Assert.That(drone.IsCoverAlert, Is.False);
            Assert.That(drone.Body.position, Is.EqualTo(position));
            Assert.That(drone.Target, Is.SameAs(target));
            Assert.That(drone.CurrentRound, Is.EqualTo(flow.CurrentRound));
            for (int otherIndex = 0; otherIndex < others.Length; otherIndex++)
            {
                Assert.That(others[otherIndex].isActiveAndEnabled && others[otherIndex].IsRunning, Is.True);
                Assert.That(others[otherIndex].CurrentRound, Is.EqualTo(flow.CurrentRound));
                Assert.That(others[otherIndex].CurrentState, Is.EqualTo(otherStates[otherIndex]));
                Assert.That(others[otherIndex].AlertSecondsRemaining, Is.EqualTo(otherTimers[otherIndex]));
                Assert.That(others[otherIndex].Body.position, Is.EqualTo(otherPositions[otherIndex]));
            }
        }
        int hits = 0;
        int health = player.health.CurrentHealth;
        DroneState damageState = DroneState.None;
        player.health.NonLethalDamaged += amount => { hits++; damageState = drone.CurrentState; };
        // 让自然Chase飞到玩家发生接触；在伤害回调取状态，避免低帧率漏采短暂Chase帧。
        yield return Until(() => drone.HasExploded, 8f,
            () => "state=" + drone.CurrentState + ", secondsRemaining=" + drone.AlertSecondsRemaining
                + ", coverAlert=" + drone.IsCoverAlert
                + ", drone=" + drone.Body.position + ", player=" + player.Position
                + ", visible=" + drone.vision.CanSee(player) + ", blocked=" + player.IsSceneTransitionBlocked);
        Assert.That(hits, Is.EqualTo(1));
        Assert.That(damageState, Is.EqualTo(DroneState.Chase));
        Assert.That(player.health.CurrentHealth, Is.EqualTo(health - drone.config.contactDamage));
        foreach (DroneController other in others)
        {
            Assert.That(other.isActiveAndEnabled && other.IsRunning, Is.True, "Other scene drones stay active throughout natural detection/contact.");
            Assert.That(other.HasExploded, Is.False);
            Assert.That(other.CurrentRound, Is.EqualTo(flow.CurrentRound));
        }
        for (int i = 0; i < 3; i++)
        {
            Assert.That(bindings.Activate(flow, player, out string error), Is.True, error);
            Assert.That(bindings.TryBeginExplorationEnemies(flow.CurrentRound, out error), Is.True, error);
            Assert.That(drone.HasExploded, Is.True);
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Explode));
            Assert.That(drone.IsRunning, Is.False);
            Assert.That(drone.AlertSecondsRemaining, Is.Zero);
            Assert.That(drone.IsCoverAlert, Is.False);
            Assert.That(drone.CurrentRound, Is.EqualTo(flow.CurrentRound));
            Assert.That(drone.GetComponent<SphereCollider>().enabled, Is.False);
        }
        yield return new WaitForSeconds(player.config.damageInvulnerabilitySeconds + 0.1f);
        Assert.That(hits, Is.EqualTo(1));
        player.Body.constraints = constraints;
        Assert.That(flow.TryExtract(), Is.True);
        yield return Stage(GameFlowStage.Shelter);
        Assert.That(flow.TryDepart(), Is.True);
        yield return Stage(GameFlowStage.Exploring);
        DroneController[] nextDrones = Drones(SceneManager.GetSceneByPath(RuinsPath));
        Assert.That(nextDrones.Length, Is.EqualTo(drones.Length));
        foreach (DroneController next in nextDrones)
        {
            Assert.That(drones.All(old => !ReferenceEquals(old, next)), Is.True);
            Assert.That(next.CurrentRound, Is.EqualTo(2));
            Assert.That(next.HasExploded, Is.False);
            Assert.That(next.CurrentState, Is.EqualTo(DroneState.Patrol));
            Assert.That(next.AlertSecondsRemaining, Is.Zero);
            Assert.That(next.IsCoverAlert, Is.False);
        }
        Assert.That(flow.CurrentRound, Is.EqualTo(2));
        Assert.That(flow.Loot.PreparedRound, Is.EqualTo(2));
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
        DroneController[] drones = Drones(failed.gameObject.scene);
        Assert.That(drones.Length, Is.GreaterThan(0));
        Vector3[] positions = drones.Select(drone => drone.Body.position).ToArray();
        foreach (DroneController drone in drones)
        {
            Assert.That(drone.CurrentRound, Is.Zero);
            Assert.That(drone.IsRunning, Is.False);
        }
        Assert.That(flow.Player.health.CurrentRound, Is.Zero);
        Assert.That(flow.Player.IsSceneTransitionBlocked, Is.True);
        yield return new WaitForSeconds(0.1f);
        for (int i = 0; i < drones.Length; i++) { Assert.That(drones[i].Body.position, Is.EqualTo(positions[i])); }
        int handle = failed.gameObject.scene.handle;
        failed.spawnPoint = spawn;
        flow.Retry();
        yield return Stage(GameFlowStage.Exploring);
        foreach (DroneController drone in drones)
        {
            Assert.That(drone.gameObject.scene.handle, Is.EqualTo(handle));
            Assert.That(drone.CurrentRound, Is.EqualTo(1));
        }
        Assert.That(flow.CurrentRound, Is.EqualTo(1));
        Assert.That(flow.Player.health.CurrentRound, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator InvalidEnemyConfigurationFailsBeforeAnyStartupAndRetryKeepsOneRound()
    {
        yield return Boot();
        DroneController[] drones = null;
        DroneController invalid = null;
        DroneConfigSO config = null;
        onLoaded = (scene, mode) =>
        {
            if (scene.path != RuinsPath) { return; }
            drones = Drones(scene);
            Assert.That(drones.Length, Is.GreaterThan(1), "Configuration preflight must protect the other authored scene drone as well.");
            // 只破坏本轮加载的一架真实场景机，预检必须阻止同场景其余无人机提前启动。
            invalid = drones.OrderBy(candidate => candidate.transform.position.x).Last();
            config = invalid.config;
            invalid.config = null;
        };
        SceneManager.sceneLoaded += onLoaded;
        LogAssert.Expect(LogType.Warning, new Regex("GameFlow: 探索无人机缺少配置或轮次超前"));
        Assert.That(flow.TryDepart(), Is.True);
        yield return Stage(GameFlowStage.Failed);
        Assert.That(drones, Is.Not.Null);
        foreach (DroneController drone in drones)
        {
            Assert.That(drone.CurrentRound, Is.Zero);
            Assert.That(drone.IsRunning, Is.False);
        }
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
        foreach (DroneController drone in drones)
        {
            Assert.That(drone.CurrentRound, Is.EqualTo(1));
            Assert.That(drone.IsRunning, Is.True);
        }
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
        return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<DroneController>(true))
            .Where(drone => drone.isActiveAndEnabled).ToArray();
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
