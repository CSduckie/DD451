using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// 09F用固定发现/草丛警戒替换旧填条；真实Collider发现，不强制AI状态或计时。
public class DroneAlertTests
{
    private readonly List<Object> owned = new List<Object>();
    private readonly Vector3 origin = new Vector3(6000f, 5000f, 60f);
    private DroneConfigSO config;

    [SetUp]
    public void SetUp()
    {
        config = Own(ScriptableObject.CreateInstance<DroneConfigSO>());
        Assert.That(config.discoveryAlertSeconds, Is.EqualTo(0.5f));
        Assert.That(config.coverAlertSeconds, Is.EqualTo(2f));
        config.patrolSpeed = 1.5f;
        config.chaseSpeed = 2f;
        config.arrivalDistance = 0.06f;
        config.patrolWaitSeconds = 0.04f;
        config.searchSeconds = 2f;
        config.blockedReturnSeconds = 0.15f;
        config.contactDamage = 1;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (Object item in owned)
        {
            if (item != null && item is GameObject go) { go.SetActive(false); }
        }
        foreach (Object item in owned) { if (item != null) { Object.Destroy(item); } }
        owned.Clear();
        yield return null;
    }

    [UnityTest]
    public IEnumerator RealDiscoveryHoversForConfiguredDelayThenChaseHidesOnlyRenderer()
    {
        config.chaseSpeed = 0f;
        DroneController drone = CreateDrone(origin, origin + Vector3.right * 10f);
        PlayerController player = CreatePlayer(origin + Vector3.up * 15f);
        Assert.That(drone.BeginRound(1), Is.True);
        yield return Until(() => drone.Body.position.x > origin.x + 0.25f, 2f, "Patrol must move before discovery.");
        MovePlayer(player, drone.Body.position + Vector3.right * 3f);
        Assert.That(drone.vision.CanSee(player), Is.True);
        yield return WaitForDiscovery(drone);
        float started = Time.time;
        float initialRemaining = drone.AlertSecondsRemaining;
        Assert.That(initialRemaining, Is.GreaterThan(0.3f));
        yield return new WaitForFixedUpdate();
        Vector3 hover = drone.Body.position;
        float previous = drone.AlertSecondsRemaining;
        bool sawCountdown = false;
        float deadline = Time.realtimeSinceStartup + config.discoveryAlertSeconds + 2f;
        while (drone.CurrentState == DroneState.Alert && Time.realtimeSinceStartup < deadline)
        {
            AssertHovering(drone, hover);
            Assert.That(drone.IsCoverAlert, Is.False);
            Assert.That(drone.AlertSecondsRemaining, Is.InRange(0f, previous + 0.0001f));
            sawCountdown |= drone.AlertSecondsRemaining < initialRemaining - 0.1f;
            previous = drone.AlertSecondsRemaining;
            AssertPlainOrange(drone);
            Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth));
            yield return null;
        }
        Assert.That(sawCountdown, Is.True, "Discovery must expose a real countdown rather than immediate Chase.");
        Assert.That(Time.time - started, Is.GreaterThanOrEqualTo(initialRemaining - 0.08f));
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Chase));
        Assert.That(drone.Target, Is.SameAs(player));
        Assert.That(drone.HasExploded, Is.False);
        yield return null;
        yield return new WaitForFixedUpdate();
        AssertChaseDetectionRemainsActive(drone, player);
        Assert.That(drone.CurrentRound, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator ChaseCoverStopsForTwoSecondsWithoutRestartThenResumesExistingRouteIndex()
    {
        DroneController drone = CreateDrone(origin, origin + Vector3.right * 0.35f);
        drone.patrolPoints = new[] { Point(origin + Vector3.right * 0.35f), Point(origin + Vector3.right * 10f) };
        PlayerController player = CreatePlayer(origin + Vector3.up * 15f);
        Assert.That(drone.BeginRound(1), Is.True);
        yield return Until(() => drone.TryGetPatrolPoint(out Vector3 point)
            && Vector3.Distance(point, origin + Vector3.right * 10f) < 0.01f
            && drone.Body.position.x > origin.x + 0.8f, 3f, "Patrol must naturally reach its second authored destination.");
        Assert.That(drone.TryGetPatrolPoint(out Vector3 route), Is.True);
        MovePlayer(player, drone.Body.position + Vector3.right * 3f);
        yield return WaitForDiscoveryThenChase(drone);
        float chaseX = drone.Body.position.x;
        yield return Until(() => drone.Body.position.x > chaseX + 0.15f, 2f, "Chase must move before the player hides.");
        Cover(player.GetComponent<CapsuleCollider>().bounds.center);
        SetCrouchHeld(player, true);
        Assert.That(CoverArea.IsHidden(player), Is.True);
        yield return WaitForCoverAlert(drone);
        yield return new WaitForFixedUpdate();
        Vector3 hover = drone.Body.position;
        Quaternion heading = drone.FacingRotation;
        float remaining = drone.AlertSecondsRemaining;
        Assert.That(remaining, Is.GreaterThan(1.7f));
        Assert.That(drone.Target, Is.Null);
        AssertVector(drone.LastKnownPosition, Vector3.zero);
        Assert.That(drone.BeginRound(1), Is.False);
        drone.ChangeState(DroneState.Alert);
        Assert.That(drone.IsCoverAlert, Is.True);
        Assert.That(drone.AlertSecondsRemaining, Is.EqualTo(remaining), "Same state/round must not restart cover's timer.");
        // 玩家仍在同一个真实草丛中移动；警戒不能继续追踪被清掉的目标坐标。
        MovePlayer(player, player.transform.position + Vector3.right * 0.15f);
        Assert.That(CoverArea.IsHidden(player), Is.True);
        float started = Time.time;
        yield return WaitForCoverExpiry(drone, hover);
        Assert.That(Time.time - started, Is.GreaterThanOrEqualTo(remaining - 0.1f));
        Assert.That(Quaternion.Angle(drone.FacingRotation, heading), Is.LessThan(0.05f));
        Assert.That(drone.TryGetPatrolPoint(out Vector3 restoredRoute), Is.True);
        AssertVector(restoredRoute, route);
        Assert.That(drone.Body.position.x, Is.GreaterThan(origin.x + 0.5f), "Cover expiry must not teleport to spawn.");
        float resumedX = drone.Body.position.x;
        yield return Until(() => drone.Body.position.x > resumedX + 0.15f, 2f,
            "Patrol must resume the same second point without resetting the array index.");
        Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth));
    }

    [UnityTest]
    public IEnumerator CoverRediscoveryImmediatelyChasesWhenStandingOrLeavingActualCover()
    {
        config.patrolSpeed = config.chaseSpeed = 0f;
        for (int leave = 0; leave < 2; leave++)
        {
            Vector3 position = origin + Vector3.right * (leave * 20f);
            DroneController drone = CreateDrone(position, position);
            PlayerController player = CreatePlayer(position + Vector3.right * 3f);
            Cover(player.GetComponent<CapsuleCollider>().bounds.center);
            Assert.That(CoverArea.IsHidden(player), Is.False, "Standing in cover must still be discoverable.");
            Assert.That(drone.BeginRound(1), Is.True);
            yield return WaitForDiscoveryThenChase(drone);
            SetCrouchHeld(player, true);
            yield return WaitForCoverAlert(drone);
            yield return Until(() => drone.AlertSecondsRemaining < 1.7f, 2f, "The cover timer must advance while hidden.");
            Assert.That(drone.IsCoverAlert, Is.True);
            Assert.That(drone.AlertSecondsRemaining, Is.GreaterThan(config.discoveryAlertSeconds));
            if (leave == 0) { SetCrouchHeld(player, false); }
            else { MovePlayer(player, position + Vector3.right * 4.5f); }
            Assert.That(CoverArea.IsHidden(player), Is.False,
                "Standing inside or crouching outside the actual cover does not meet the hiding condition.");
            Assert.That(drone.vision.CanSee(player), Is.True);
            // 测试协程的下一帧可能先于MonoBehaviour.Update恢复；等待真实状态更新，仍拒绝新的0.5秒警戒。
            float rediscoveredAt = Time.time;
            yield return Until(() => drone.CurrentState == DroneState.Chase, 0.2f,
                "Real rediscovery must chase promptly without another half-second discovery alert.");
            Assert.That(Time.time - rediscoveredAt, Is.LessThan(config.discoveryAlertSeconds));
            Assert.That(drone.IsCoverAlert, Is.False);
            Assert.That(drone.AlertSecondsRemaining, Is.Zero);
            yield return new WaitForFixedUpdate();
            AssertChaseDetectionRemainsActive(drone, player);
            Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth));
            drone.gameObject.SetActive(false);
        }
    }

    [UnityTest]
    public IEnumerator DiscoverySwitchesToCoverDelayButWallAndDistanceLossUseSearch()
    {
        config.patrolSpeed = config.chaseSpeed = 0f;
        for (int lost = 0; lost < 3; lost++)
        {
            Vector3 position = origin + Vector3.right * (lost * 20f);
            DroneController drone = CreateDrone(position, position);
            PlayerController player = CreatePlayer(position + Vector3.right * 3f);
            Assert.That(drone.BeginRound(1), Is.True);
            yield return WaitForDiscovery(drone);
            yield return Until(() => drone.AlertSecondsRemaining < 0.35f, 1f, "The initial discovery timer must advance.");
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
            Vector3 lastSeen = drone.LastKnownPosition;
            if (lost == 0)
            {
                Cover(player.GetComponent<CapsuleCollider>().bounds.center);
                SetCrouchHeld(player, true);
                yield return WaitForCoverAlert(drone);
                Assert.That(drone.AlertSecondsRemaining, Is.GreaterThan(1.7f),
                    "Hiding during initial discovery starts the complete cover delay, not its remaining discovery timer.");
                Assert.That(drone.Target, Is.Null);
                AssertVector(drone.LastKnownPosition, Vector3.zero);
                AssertPlainOrange(drone);
            }
            else
            {
                if (lost == 1) { Wall(position + new Vector3(1.5f, 0.7f, 0f)); }
                else { MovePlayer(player, position + Vector3.up * 12f); }
                Assert.That(drone.vision.CanSee(player), Is.False);
                yield return Until(() => drone.CurrentState == DroneState.Search, 1f,
                    "Ordinary loss during discovery must search the last seen position rather than blindly finish its timer.");
                Assert.That(drone.IsCoverAlert, Is.False);
                Assert.That(drone.AlertSecondsRemaining, Is.Zero);
                AssertVector(drone.LastKnownPosition, lastSeen);
                MovePlayer(player, position + new Vector3(-4f, 12f, 0f));
                float finish = Time.time + config.discoveryAlertSeconds + 0.1f;
                while (Time.time < finish)
                {
                    Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Search));
                    Assert.That(drone.Target, Is.Null);
                    AssertVector(drone.LastKnownPosition, lastSeen);
                    Assert.That(drone.HasExploded, Is.False);
                    yield return null;
                }
            }
            drone.gameObject.SetActive(false);
        }
    }

    [UnityTest]
    public IEnumerator InvalidAndDestroyedTargetsCannotFinishDiscoveryOrResumeCoverAlert()
    {
        config.patrolSpeed = config.chaseSpeed = 0f;
        config.discoveryAlertSeconds = 0.25f;
        config.coverAlertSeconds = 0.35f;
        for (int phase = 0; phase < 2; phase++)
        {
            for (int invalid = 0; invalid < 5; invalid++)
            {
                Vector3 position = origin + Vector3.right * ((phase * 5 + invalid) * 20f);
                DroneController drone = CreateDrone(position, position);
                PlayerController player = CreatePlayer(position + Vector3.right * 3f);
                Assert.That(drone.BeginRound(1), Is.True);
                yield return WaitForDiscovery(drone);
                if (phase == 1)
                {
                    yield return Until(() => drone.CurrentState == DroneState.Chase, 2f, "Discovery must complete before testing cover.");
                    Cover(player.GetComponent<CapsuleCollider>().bounds.center);
                    SetCrouchHeld(player, true);
                    yield return WaitForCoverAlert(drone);
                }
                if (invalid == 0) { player.enabled = false; }
                else if (invalid == 1) { player.gameObject.SetActive(false); }
                else if (invalid == 2) { Assert.That(player.health.TakeDamage(player.health.MaxHealth), Is.True); }
                else if (invalid == 3) { player.SetSceneTransitionBlocked(true); }
                else { Object.Destroy(player.gameObject); }
                yield return null;
                int health = player != null ? player.health.CurrentHealth : 0;
                float finish = Time.time + Mathf.Max(config.discoveryAlertSeconds, config.coverAlertSeconds) + 0.2f;
                float deadline = Time.realtimeSinceStartup + 3f;
                while (Time.time < finish && Time.realtimeSinceStartup < deadline)
                {
                    Assert.That(drone.CurrentState, Is.Not.EqualTo(DroneState.Chase), "Invalid phase/case " + phase + "/" + invalid);
                    Assert.That(drone.HasExploded, Is.False);
                    Assert.That(drone.Target == null, Is.True);
                    if (player != null)
                    {
                        Assert.That(drone.TryExplode(player), Is.False);
                        Assert.That(player.health.CurrentHealth, Is.EqualTo(health));
                    }
                    yield return null;
                }
                Assert.That(Time.time, Is.GreaterThanOrEqualTo(finish));
                Assert.That(drone.AlertSecondsRemaining, Is.Zero);
                Assert.That(drone.IsCoverAlert, Is.False);
                drone.gameObject.SetActive(false);
            }
        }
        Vector3 unpreparedPosition = origin + Vector3.right * 240f;
        DroneController observer = CreateDrone(unpreparedPosition, unpreparedPosition);
        PlayerController unprepared = CreatePlayer(unpreparedPosition + Vector3.right * 3f, false);
        Assert.That(observer.BeginRound(1), Is.True);
        yield return null;
        yield return null;
        Assert.That(DroneController.CanTarget(unprepared), Is.False);
        Assert.That(observer.CurrentState, Is.EqualTo(DroneState.Patrol));
        Assert.That(observer.AlertSecondsRemaining, Is.Zero);
    }

    [UnityTest]
    public IEnumerator DiscoveryPhysicalContactExplodesBeforeItsTimerCompletesOnlyOnce()
    {
        config.patrolSpeed = 0f;
        DroneController drone = CreateDrone(origin, origin);
        PlayerController player = CreatePlayer(origin + Vector3.right * 3f);
        Assert.That(drone.BeginRound(1), Is.True);
        yield return WaitForDiscovery(drone);
        MovePlayerCenter(player, drone.Body.position + Vector3.right * 0.1f);
        MakeDynamicFrozen(player);
        Assert.That(drone.vision.CanSee(player), Is.True);
        Assert.That(Vector3.Distance(player.GetComponent<CapsuleCollider>().ClosestPoint(drone.Body.position), drone.Body.position),
            Is.LessThan(drone.GetComponent<SphereCollider>().radius));
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
        Assert.That(drone.AlertSecondsRemaining, Is.GreaterThan(0f));
        int health = player.health.CurrentHealth;
        int hits = 0;
        float remainingAtDamage = 0f;
        DroneState stateAtDamage = DroneState.None;
        player.health.NonLethalDamaged += amount =>
        {
            hits++;
            stateAtDamage = drone.CurrentState;
            remainingAtDamage = drone.AlertSecondsRemaining;
        };
        // 不调用TryExplode预先消耗无人机：真实物理入口必须在发现倒计时结束前处理碰撞。
        yield return new WaitForFixedUpdate();
        Assert.That(drone.HasExploded, Is.True);
        Assert.That(stateAtDamage, Is.EqualTo(DroneState.Alert));
        Assert.That(remainingAtDamage, Is.GreaterThan(0f));
        Assert.That(hits, Is.EqualTo(1));
        Assert.That(drone.GetComponent<SphereCollider>().enabled, Is.False);
        Assert.That(player.health.CurrentHealth, Is.EqualTo(health - config.contactDamage));
        Assert.That(drone.TryExplode(player), Is.False);
        yield return new WaitForFixedUpdate();
        Assert.That(hits, Is.EqualTo(1));
        Assert.That(player.health.CurrentHealth, Is.EqualTo(health - config.contactDamage));
    }

    [UnityTest]
    public IEnumerator HiddenPlayerStillExplodesOnDirectContactAndSweepBeforeAnyStateUpdate()
    {
        config.patrolSpeed = config.chaseSpeed = 0f;
        DroneController direct = CreateDrone(origin, origin);
        PlayerController directPlayer = CreatePlayer(origin + Vector3.right * 3f);
        Assert.That(direct.BeginRound(1), Is.True);
        yield return WaitForDiscoveryThenChase(direct);
        MovePlayerCenter(directPlayer, origin + Vector3.right * 0.1f);
        Cover(directPlayer.GetComponent<CapsuleCollider>().bounds.center);
        SetCrouchHeld(directPlayer, true);
        Assert.That(direct.CurrentState, Is.EqualTo(DroneState.Chase), "Do not let Update enter cover alert before checking the direct gate.");
        Assert.That(CoverArea.IsHidden(directPlayer), Is.True);
        Assert.That(direct.TryExplode(directPlayer), Is.True, "Hidden affects vision, not an already proven solid contact.");
        Assert.That(direct.HasExploded, Is.True);
        Assert.That(directPlayer.health.CurrentHealth, Is.EqualTo(directPlayer.health.MaxHealth - config.contactDamage));
        Assert.That(direct.TryExplode(directPlayer), Is.False);
        yield return new WaitForFixedUpdate();
        Assert.That(directPlayer.health.CurrentHealth, Is.EqualTo(directPlayer.health.MaxHealth - config.contactDamage));
        direct.gameObject.SetActive(false);

        Vector3 position = origin + Vector3.up * 8f;
        DroneController sweep = CreateDrone(position, position);
        PlayerController sweepPlayer = CreatePlayer(position + Vector3.right * 1.2f);
        Assert.That(sweep.BeginRound(1), Is.True);
        yield return WaitForDiscoveryThenChase(sweep);
        Cover(sweepPlayer.GetComponent<CapsuleCollider>().bounds.center);
        SetCrouchHeld(sweepPlayer, true);
        Assert.That(sweep.CurrentState, Is.EqualTo(DroneState.Chase));
        Assert.That(CoverArea.IsHidden(sweepPlayer), Is.True);
        // 真实SphereCast预检跨过隐藏玩家；接触立即自爆，不等待下一次Update改变视野状态。
        sweep.MoveTowards(position + Vector3.right * 3f, 100f);
        Assert.That(sweep.IsBlocked, Is.True);
        Assert.That(sweep.HasExploded, Is.True);
        Assert.That(sweepPlayer.health.CurrentHealth, Is.EqualTo(sweepPlayer.health.MaxHealth - config.contactDamage));
        Assert.That(sweep.TryExplode(sweepPlayer), Is.False);
        yield return new WaitForFixedUpdate();
        Assert.That(sweepPlayer.health.CurrentHealth, Is.EqualTo(sweepPlayer.health.MaxHealth - config.contactDamage));
        Assert.That(sweep.GetComponent<SphereCollider>().enabled, Is.False);
    }

    [UnityTest]
    public IEnumerator HiddenTargetStopsBeforeFixedUpdateMovementButPhysicalContactStillExplodes()
    {
        config.patrolSpeed = 0f;
        for (int touching = 0; touching < 2; touching++)
        {
            config.chaseSpeed = 0f;
            Vector3 position = origin + Vector3.right * (touching * 20f);
            DroneController drone = CreateDrone(position, position);
            PlayerController player = CreatePlayer(position + Vector3.right * 3f);
            Assert.That(drone.BeginRound(1), Is.True);
            yield return WaitForDiscoveryThenChase(drone);
            if (touching == 1) { MovePlayerCenter(player, position + Vector3.right * 0.1f); }
            Cover(player.GetComponent<CapsuleCollider>().bounds.center);
            MakeDynamicFrozen(player);
            SetCrouchHeld(player, true);
            Assert.That(CoverArea.IsHidden(player), Is.True);
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Chase));
            float separation = Vector3.Distance(player.GetComponent<CapsuleCollider>().ClosestPoint(drone.Body.position), drone.Body.position);
            Assert.That(separation < drone.GetComponent<SphereCollider>().radius, Is.EqualTo(touching == 1));
            Vector3 before = drone.Body.position;
            // 同一物理入口仍先停追。已发生接触的情况继续爆炸，未接触的情况保留草丛警戒。
            config.chaseSpeed = 3f;
            int health = player.health.CurrentHealth;
            int hits = 0;
            player.health.NonLethalDamaged += amount => hits++;
            drone.SendMessage("FixedUpdate", SendMessageOptions.RequireReceiver);
            AssertVector(drone.Body.position, before);
            Assert.That(drone.FlightVelocity.sqrMagnitude, Is.LessThan(0.0001f));
            Assert.That(drone.Target, Is.Null);
            if (touching == 0)
            {
                Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
                Assert.That(drone.IsCoverAlert, Is.True);
                Assert.That(drone.AlertSecondsRemaining, Is.GreaterThan(0f));
                yield return WaitForCoverExpiry(drone, before);
                Assert.That(hits, Is.Zero);
                Assert.That(player.health.CurrentHealth, Is.EqualTo(health));
                Assert.That(drone.GetComponent<SphereCollider>().enabled, Is.True);
            }
            else
            {
                Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Explode));
                Assert.That(drone.HasExploded, Is.True);
                Assert.That(hits, Is.EqualTo(1));
                Assert.That(drone.GetComponent<SphereCollider>().enabled, Is.False);
                Assert.That(player.health.CurrentHealth, Is.EqualTo(health - config.contactDamage));
                Assert.That(drone.TryExplode(player), Is.False);
                for (int i = 0; i < 3; i++) { yield return new WaitForFixedUpdate(); }
                Assert.That(hits, Is.EqualTo(1));
                Assert.That(player.health.CurrentHealth, Is.EqualTo(health - config.contactDamage));
            }
            drone.gameObject.SetActive(false);
        }
    }

    [UnityTest]
    public IEnumerator SearchRediscoveryUsesANewDiscoveryDelayInsteadOfCoverShortcut()
    {
        config.patrolSpeed = config.chaseSpeed = 0f;
        DroneController drone = CreateDrone(origin, origin);
        PlayerController player = CreatePlayer(origin + Vector3.right * 3f);
        Assert.That(drone.BeginRound(1), Is.True);
        yield return WaitForDiscoveryThenChase(drone);
        BoxCollider wall = Wall(origin + new Vector3(1.5f, 0.7f, 0f));
        Assert.That(drone.vision.CanSee(player), Is.False);
        yield return Until(() => drone.CurrentState == DroneState.Search, 1f, "An ordinary wall must still cause last-position search.");
        Assert.That(drone.IsCoverAlert, Is.False);
        Assert.That(drone.Target, Is.Null);
        wall.enabled = false;
        Physics.SyncTransforms();
        Assert.That(drone.vision.CanSee(player), Is.True);
        yield return WaitForDiscovery(drone);
        Assert.That(drone.AlertSecondsRemaining, Is.GreaterThan(0.3f), "Search discovery must start the configured half-second delay anew.");
        float remaining = drone.AlertSecondsRemaining;
        float started = Time.time;
        yield return Until(() => drone.CurrentState == DroneState.Chase, 2f, "Search's new discovery delay must eventually complete.");
        Assert.That(Time.time - started, Is.GreaterThanOrEqualTo(remaining - 0.08f));
    }

    [UnityTest]
    public IEnumerator TwoDronesKeepCoverAndDiscoveryTimersIndependent()
    {
        config.patrolSpeed = config.chaseSpeed = 0f;
        Vector3 secondPosition = origin + Vector3.up * 8f;
        DroneController first = CreateDrone(origin, origin);
        DroneController second = CreateDrone(secondPosition, secondPosition);
        PlayerController firstPlayer = CreatePlayer(origin + Vector3.right * 3f);
        PlayerController secondPlayer = CreatePlayer(secondPosition + Vector3.up * 12f);
        string settings = JsonUtility.ToJson(config);
        Assert.That(first.BeginRound(1), Is.True);
        Assert.That(second.BeginRound(1), Is.True);
        yield return WaitForDiscoveryThenChase(first);
        Assert.That(second.CurrentState, Is.EqualTo(DroneState.Patrol));
        Cover(firstPlayer.GetComponent<CapsuleCollider>().bounds.center);
        SetCrouchHeld(firstPlayer, true);
        yield return WaitForCoverAlert(first);
        MovePlayer(secondPlayer, secondPosition + Vector3.right * 3f);
        yield return WaitForDiscovery(second);
        Assert.That(first.IsCoverAlert, Is.True);
        Assert.That(second.IsCoverAlert, Is.False);
        Assert.That(first.AlertSecondsRemaining, Is.GreaterThan(second.AlertSecondsRemaining + 0.5f));
        float secondRemaining = second.AlertSecondsRemaining;
        Assert.That(first.BeginRound(2), Is.True);
        Assert.That(first.AlertSecondsRemaining, Is.Zero);
        Assert.That(first.IsCoverAlert, Is.False);
        Assert.That(second.CurrentRound, Is.EqualTo(1));
        Assert.That(second.AlertSecondsRemaining, Is.EqualTo(secondRemaining));
        yield return Until(() => second.CurrentState == DroneState.Chase, 2f, "Resetting another drone must not restart this discovery delay.");
        Assert.That(first.CurrentState, Is.EqualTo(DroneState.Patrol));
        Assert.That(JsonUtility.ToJson(config), Is.EqualTo(settings), "Per-drone timers must never be stored in shared config.");
    }

    [UnityTest]
    public IEnumerator SameRoundPreservesBothTimersButNewRoundDisableAndDestroyClearThem()
    {
        config.patrolSpeed = config.chaseSpeed = 0f;
        DroneController drone = CreateDrone(origin, origin);
        PlayerController player = CreatePlayer(origin + Vector3.right * 3f);
        Assert.That(drone.BeginRound(1), Is.True);
        yield return WaitForDiscovery(drone);
        float remaining = drone.AlertSecondsRemaining;
        Assert.That(drone.BeginRound(1), Is.False);
        drone.ChangeState(DroneState.Alert);
        Assert.That(drone.IsCoverAlert, Is.False);
        Assert.That(drone.AlertSecondsRemaining, Is.EqualTo(remaining));
        MovePlayer(player, origin + Vector3.up * 12f);
        Assert.That(drone.BeginRound(2), Is.True);
        AssertClearedTimer(drone);
        Assert.That(drone.Target, Is.Null);
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));

        MovePlayer(player, origin + Vector3.right * 3f);
        yield return WaitForDiscoveryThenChase(drone);
        Cover(player.GetComponent<CapsuleCollider>().bounds.center);
        SetCrouchHeld(player, true);
        yield return WaitForCoverAlert(drone);
        remaining = drone.AlertSecondsRemaining;
        Assert.That(drone.BeginRound(2), Is.False);
        drone.ChangeState(DroneState.Alert);
        Assert.That(drone.IsCoverAlert, Is.True);
        Assert.That(drone.AlertSecondsRemaining, Is.EqualTo(remaining));
        drone.enabled = false;
        AssertClearedTimer(drone);
        Assert.That(drone.Target, Is.Null);
        Assert.That(drone.vision.RangeCollider.enabled, Is.False);
        MovePlayer(player, origin + Vector3.up * 12f);
        drone.enabled = true;
        Assert.That(drone.CurrentRound, Is.EqualTo(2));
        AssertClearedTimer(drone);
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));

        SetCrouchHeld(player, false);
        MovePlayer(player, origin + Vector3.right * 3f);
        yield return WaitForDiscovery(drone);
        drone.gameObject.SetActive(false);
        AssertClearedTimer(drone);
        MovePlayer(player, origin + Vector3.up * 12f);
        drone.gameObject.SetActive(true);
        AssertClearedTimer(drone);
        Assert.That(drone.CurrentRound, Is.EqualTo(2));
        MovePlayer(player, origin + Vector3.right * 3f);
        yield return WaitForDiscovery(drone);
        DroneVision remainingVision = drone.vision;
        Object.Destroy(drone);
        yield return null;
        Assert.That(drone == null, Is.True);
        Assert.That(remainingVision.RangeCollider.enabled, Is.False);
        Assert.That(remainingVision.GetComponent<MeshRenderer>().enabled, Is.False);
        Assert.That(Properties(remainingVision).GetFloat("_FillProgress"), Is.Zero);
    }

    private IEnumerator WaitForDiscovery(DroneController drone)
    {
        yield return Until(() => drone.CurrentState == DroneState.Alert, 2f, "A visible player must naturally start discovery alert.");
        Assert.That(drone.IsCoverAlert, Is.False);
        Assert.That(drone.AlertSecondsRemaining, Is.GreaterThan(0f).And.LessThanOrEqualTo(config.discoveryAlertSeconds));
    }

    private IEnumerator WaitForDiscoveryThenChase(DroneController drone)
    {
        yield return WaitForDiscovery(drone);
        yield return Until(() => drone.CurrentState == DroneState.Chase, config.discoveryAlertSeconds + 2f,
            "The visible player must complete discovery alert before being chased.");
    }

    private IEnumerator WaitForCoverAlert(DroneController drone)
    {
        yield return Until(() => drone.CurrentState == DroneState.Alert && drone.IsCoverAlert, 1f,
            "A tracked player hidden by actual cover must begin cover alert.");
        Assert.That(drone.AlertSecondsRemaining, Is.GreaterThan(0f).And.LessThanOrEqualTo(config.coverAlertSeconds));
        Assert.That(drone.Target, Is.Null);
    }

    private IEnumerator WaitForCoverExpiry(DroneController drone, Vector3 hover)
    {
        float previous = drone.AlertSecondsRemaining;
        float deadline = Time.realtimeSinceStartup + config.coverAlertSeconds + 1f;
        while (drone.CurrentState == DroneState.Alert && Time.realtimeSinceStartup < deadline)
        {
            AssertHovering(drone, hover);
            Assert.That(drone.IsCoverAlert, Is.True);
            Assert.That(drone.AlertSecondsRemaining, Is.InRange(0f, previous + 0.0001f));
            Assert.That(drone.Target, Is.Null);
            AssertVector(drone.LastKnownPosition, Vector3.zero);
            AssertPlainOrange(drone);
            previous = drone.AlertSecondsRemaining;
            yield return null;
        }
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol), "Continuous hiding must not restart the two-second countdown every frame.");
        AssertClearedTimer(drone);
    }

    private static void AssertHovering(DroneController drone, Vector3 position)
    {
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
        AssertVector(drone.Body.position, position);
        Assert.That(drone.FlightVelocity.sqrMagnitude, Is.LessThan(0.0001f));
        Assert.That(drone.HasExploded, Is.False);
        Assert.That(Quaternion.Angle(drone.Body.rotation, Quaternion.identity), Is.LessThan(0.01f));
    }

    private static void AssertPlainOrange(DroneController drone)
    {
        MaterialPropertyBlock block = Properties(drone.vision);
        Assert.That(block.GetFloat("_FillProgress"), Is.Zero, "Timed alert must never restore the old progressive red fill.");
        Color color = block.GetColor("_BaseColor");
        Assert.That(color.r, Is.GreaterThan(0.9f));
        Assert.That(color.g, Is.InRange(0.4f, 0.8f));
        Assert.That(color.b, Is.LessThan(0.25f));
        Assert.That(drone.vision.GetComponent<MeshRenderer>().enabled, Is.True);
        Assert.That(drone.vision.RangeCollider.enabled, Is.True);
    }

    private static void AssertChaseDetectionRemainsActive(DroneController drone, PlayerController player)
    {
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Chase));
        Assert.That(drone.vision.GetComponent<MeshRenderer>().enabled, Is.False);
        Assert.That(drone.vision.gameObject.activeInHierarchy, Is.True);
        Assert.That(drone.vision.enabled, Is.True);
        Assert.That(drone.vision.RangeCollider.enabled, Is.True);
        Assert.That(drone.vision.RangeCollider.sharedMesh, Is.Not.Null);
        Assert.That(drone.vision.CanSee(player), Is.True, "Hiding the picture must preserve real detection.");
        Assert.That(drone.vision.FindVisiblePlayer(), Is.SameAs(player));
    }

    private static void AssertClearedTimer(DroneController drone)
    {
        Assert.That(drone.AlertSecondsRemaining, Is.Zero);
        Assert.That(drone.IsCoverAlert, Is.False);
        Assert.That(Properties(drone.vision).GetFloat("_FillProgress"), Is.Zero);
    }

    private static MaterialPropertyBlock Properties(DroneVision vision)
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        vision.GetComponent<MeshRenderer>().GetPropertyBlock(block);
        return block;
    }

    private DroneController CreateDrone(Vector3 position, Vector3 endpoint)
    {
        GameObject root = Own(new GameObject("Timed alert test drone"));
        root.transform.position = position;
        DroneController drone = root.AddComponent<DroneController>();
        drone.config = config;
        drone.GetComponent<SphereCollider>().radius = 0.2f;
        Transform visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        drone.visual = visual;
        drone.patrolPoints = new[] { Point(endpoint), Point(position) };
        GameObject volume = new GameObject("Vision");
        volume.transform.SetParent(root.transform, false);
        drone.vision = volume.AddComponent<DroneVision>();
        drone.vision.radius = 6f;
        drone.vision.angleDegrees = 100f;
        drone.vision.thickness = 2f;
        drone.vision.directionDegrees = 0f;
        drone.vision.Rebuild();
        return drone;
    }

    private Transform Point(Vector3 position)
    {
        GameObject point = Own(new GameObject("Timed alert patrol point"));
        point.transform.position = position;
        return point.transform;
    }

    private PlayerController CreatePlayer(Vector3 position, bool prepare = true)
    {
        PlayerConfigSO settings = Own(ScriptableObject.CreateInstance<PlayerConfigSO>());
        settings.planeZ = position.z;
        settings.groundLayers = 0;
        settings.damageInvulnerabilitySeconds = 0f;
        GameObject root = Own(new GameObject("Timed alert test player"));
        root.transform.position = position;
        PlayerController player = root.AddComponent<PlayerController>();
        player.config = settings;
        player.health.config = settings;
        if (prepare) { Assert.That(player.ResetForExploration(1, position), Is.True); }
        player.Body.isKinematic = true;
        Physics.SyncTransforms();
        return player;
    }

    private BoxCollider Wall(Vector3 position)
    {
        GameObject root = Own(new GameObject("Timed alert real wall"));
        root.transform.position = position;
        BoxCollider wall = root.AddComponent<BoxCollider>();
        wall.size = new Vector3(0.2f, 5f, 3f);
        Physics.SyncTransforms();
        return wall;
    }

    private CoverArea Cover(Vector3 position)
    {
        GameObject root = Own(new GameObject("Timed alert real crouching cover"));
        root.transform.position = position;
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(1f, 3f, 2f);
        CoverArea cover = root.AddComponent<CoverArea>();
        Physics.SyncTransforms();
        return cover;
    }

    private static void SetCrouchHeld(PlayerController player, bool crouching)
    {
        player.SetCrouching(crouching);
        player.SetBackpackOpen(true); // 保持真实蹲姿，开包不取消可发现/可受伤资格。
        Physics.SyncTransforms();
    }

    private static void MakeDynamicFrozen(PlayerController player)
    {
        player.Body.isKinematic = false;
        player.Body.useGravity = false;
        player.Body.constraints = RigidbodyConstraints.FreezeAll;
        Physics.SyncTransforms();
    }

    private static void MovePlayerCenter(PlayerController player, Vector3 center)
    {
        Vector3 offset = player.GetComponent<CapsuleCollider>().bounds.center - player.transform.position;
        MovePlayer(player, center - offset);
    }

    private static void MovePlayer(PlayerController player, Vector3 position)
    {
        player.Body.position = position;
        player.transform.position = position;
        Physics.SyncTransforms();
    }

    private T Own<T>(T item) where T : Object
    {
        owned.Add(item);
        return item;
    }

    private static IEnumerator Until(Func<bool> condition, float timeout, string message)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline) { yield return null; }
        Assert.That(condition(), Is.True, message);
    }

    private static void AssertVector(Vector3 actual, Vector3 expected)
    {
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.003f));
    }
}
