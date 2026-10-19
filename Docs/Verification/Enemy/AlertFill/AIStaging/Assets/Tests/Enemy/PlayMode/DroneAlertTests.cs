using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// 真实Collider发现与状态推进；只用API定位夹具，不强制Alert/Chase来替代发现流程。
public class DroneAlertTests
{
    private readonly List<Object> owned = new List<Object>();
    private readonly Vector3 origin = new Vector3(6000f, 5000f, 60f);
    private DroneConfigSO config;

    [SetUp]
    public void SetUp()
    {
        config = Own(ScriptableObject.CreateInstance<DroneConfigSO>());
        config.patrolSpeed = 1.5f;
        config.chaseSpeed = 2f;
        config.arrivalDistance = 0.06f;
        config.patrolWaitSeconds = 0.04f;
        config.searchSeconds = 2f;
        config.blockedReturnSeconds = 0.15f;
        config.alertFillSeconds = 1.6f;
        config.alertDecaySeconds = 1.6f;
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
    public IEnumerator RealDiscoveryStopsPatrolAndGraduallyFillsBeforeOneChaseTransition()
    {
        DroneController drone = CreateDrone(origin, origin + Vector3.right * 10f);
        PlayerController player = CreatePlayer(origin + Vector3.up * 15f);
        Assert.That(drone.BeginRound(1), Is.True);
        yield return Until(() => drone.Body.position.x > origin.x + 0.25f, 2f, "Patrol must move before discovery.");
        MovePlayer(player, drone.Body.position + Vector3.right * 3f);
        Assert.That(drone.vision.CanSee(player), Is.True);
        yield return Until(() => drone.CurrentState == DroneState.Alert, 2f, "Real discovery must enter Alert first.");
        Assert.That(drone.AlertProgress, Is.LessThan(1f));
        yield return new WaitForFixedUpdate();
        Vector3 hover = drone.Body.position;
        float previous = drone.AlertProgress;
        bool sawPartialFill = previous > 0f && previous < 1f;
        float deadline = Time.realtimeSinceStartup + config.alertFillSeconds + 2f;
        while (drone.CurrentState == DroneState.Alert && Time.realtimeSinceStartup < deadline)
        {
            AssertHovering(drone, hover);
            Assert.That(drone.AlertProgress, Is.GreaterThanOrEqualTo(previous - 0.0001f));
            Assert.That(drone.AlertProgress, Is.InRange(0f, 1f));
            AssertFillMatches(drone);
            sawPartialFill |= drone.AlertProgress > 0.1f && drone.AlertProgress < 0.9f;
            previous = drone.AlertProgress;
            Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth));
            yield return null;
        }
        Assert.That(sawPartialFill, Is.True, "Alert must expose intermediate progress, not jump straight to Chase.");
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Chase));
        Assert.That(drone.Target, Is.SameAs(player));
        Assert.That(drone.HasExploded, Is.False);
        // 保持同一个可见目标，不应每帧重新进入Alert或重置追击。
        yield return null;
        yield return null;
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Chase));
        Assert.That(drone.CurrentRound, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator WallLossDecaysResidualRefillsAndZeroResumesTheExistingRouteIndex()
    {
        DroneController drone = CreateDrone(origin, origin + Vector3.right * 0.35f);
        drone.patrolPoints = new[] { Point(origin + Vector3.right * 0.35f), Point(origin + Vector3.right * 10f) };
        PlayerController player = CreatePlayer(origin + Vector3.up * 15f);
        Assert.That(drone.BeginRound(1), Is.True);
        yield return Until(() => drone.TryGetPatrolPoint(out Vector3 point)
            && Vector3.Distance(point, origin + Vector3.right * 10f) < 0.01f
            && drone.Body.position.x > origin.x + 0.8f, 3f, "The real patrol must advance to its second authored point.");
        Assert.That(drone.TryGetPatrolPoint(out Vector3 originalRoute), Is.True);
        MovePlayer(player, drone.Body.position + Vector3.right * 3f);
        yield return ReachProgress(drone, 0.5f);
        Vector3 hover = drone.Body.position;
        Vector3 lastSeen = drone.LastKnownPosition;
        Quaternion heading = drone.FacingRotation;
        float beforeWall = drone.AlertProgress;
        BoxCollider wall = Wall(hover + new Vector3(1.5f, 0.7f, 0f));
        Assert.That(drone.vision.CanSee(player), Is.False);
        yield return Until(() => drone.AlertProgress < beforeWall - 0.12f, 2f, "A real wall must gradually drain Alert.");
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
        Assert.That(drone.AlertProgress, Is.GreaterThan(0f));
        Assert.That(drone.Target, Is.Null);
        AssertVector(drone.LastKnownPosition, lastSeen);
        AssertHovering(drone, hover);
        MovePlayer(player, hover + new Vector3(-4f, 10f, 0f));
        yield return null;
        Assert.That(Quaternion.Angle(drone.FacingRotation, heading), Is.LessThan(0.05f),
            "Alert must not turn toward the later position of a hidden player.");
        AssertVector(drone.LastKnownPosition, lastSeen);

        float residual = drone.AlertProgress;
        Assert.That(residual, Is.GreaterThan(0.05f));
        wall.enabled = false;
        MovePlayer(player, hover + Vector3.right * 3f);
        Assert.That(drone.vision.CanSee(player), Is.True);
        yield return null;
        Assert.That(drone.AlertProgress, Is.GreaterThanOrEqualTo(residual - 0.0001f),
            "Reacquiring during Alert must retain its remaining fill.");
        yield return Until(() => drone.AlertProgress > residual + 0.1f, 2f, "Visible Alert must resume increasing.");
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
        AssertHovering(drone, hover);
        AssertFillMatches(drone);

        MovePlayer(player, hover + Vector3.up * 12f);
        Assert.That(drone.vision.CanSee(player), Is.False);
        yield return DrainToPatrol(drone, hover);
        Assert.That(drone.TryGetPatrolPoint(out Vector3 restoredRoute), Is.True);
        AssertVector(restoredRoute, originalRoute);
        Assert.That(drone.Body.position.x, Is.GreaterThan(origin.x + 0.5f), "Ending Alert must not teleport to spawn.");
        float resumedX = drone.Body.position.x;
        yield return Until(() => drone.Body.position.x > resumedX + 0.15f, 2f,
            "Patrol must continue toward the same second point rather than restarting its array.");
    }

    [UnityTest]
    public IEnumerator EffectiveCrouchingCoverDrainsButStandingAndOpenGroundCrouchingDoNotHide()
    {
        config.patrolSpeed = 0f;
        DroneController drone = CreateDrone(origin, origin);
        PlayerController player = CreatePlayer(origin + Vector3.right * 3f);
        Assert.That(drone.BeginRound(1), Is.True);
        yield return ReachProgress(drone, 0.45f);
        CoverArea cover = Cover(player.transform.position + Vector3.up * 0.7f);
        player.SetCrouching(true);
        player.SetBackpackOpen(true); // 保持真实蹲姿，开包只锁主动动作，不改变可被发现资格。
        Assert.That(DroneController.CanTarget(player), Is.True);
        Assert.That(CoverArea.IsHidden(player), Is.True);
        float beforeCover = drone.AlertProgress;
        yield return Until(() => drone.AlertProgress < beforeCover - 0.12f, 2f, "Effective crouching cover must drain Alert.");
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
        Assert.That(drone.AlertProgress, Is.GreaterThan(0f));

        player.SetCrouching(false);
        Assert.That(CoverArea.IsHidden(player), Is.False);
        float standing = drone.AlertProgress;
        yield return Until(() => drone.AlertProgress > standing + 0.08f, 2f, "Standing inside a cover must remain discoverable.");
        player.SetCrouching(true);
        MovePlayer(player, origin + Vector3.right * 4.5f);
        Assert.That(cover.isActiveAndEnabled, Is.True);
        Assert.That(CoverArea.IsHidden(player), Is.False, "Crouching outside the actual BoxCollider is not hidden.");
        float outside = drone.AlertProgress;
        yield return Until(() => drone.AlertProgress > outside + 0.08f, 2f, "Open-ground crouching must continue filling Alert.");
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
        Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth));
        AssertFillMatches(drone);
    }

    [UnityTest]
    public IEnumerator InvalidAndDestroyedTargetsDrainInsteadOfBypassingAlertToPatrol()
    {
        config.patrolSpeed = 0f;
        for (int invalid = 0; invalid < 5; invalid++)
        {
            Vector3 position = origin + Vector3.right * (invalid * 20f);
            DroneController drone = CreateDrone(position, position);
            PlayerController player = CreatePlayer(position + Vector3.right * 3f);
            Assert.That(drone.BeginRound(1), Is.True);
            yield return ReachProgress(drone, 0.5f);
            float before = drone.AlertProgress;
            if (invalid == 0) { player.enabled = false; }
            else if (invalid == 1) { player.gameObject.SetActive(false); }
            else if (invalid == 2) { Assert.That(player.health.TakeDamage(player.health.MaxHealth), Is.True); }
            else if (invalid == 3) { player.SetSceneTransitionBlocked(true); }
            else { Object.Destroy(player.gameObject); }
            yield return null;
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert), "Invalid target case " + invalid + " must decay normally.");
            Assert.That(drone.AlertProgress, Is.GreaterThan(0f).And.LessThan(before));
            Assert.That(drone.Target, Is.Null);
            yield return DrainToPatrol(drone, position);
            yield return null;
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
            Assert.That(drone.AlertProgress, Is.Zero);
            Assert.That(drone.HasExploded, Is.False);
            drone.gameObject.SetActive(false);
        }
        Vector3 unpreparedPosition = origin + Vector3.right * 120f;
        DroneController observer = CreateDrone(unpreparedPosition, unpreparedPosition);
        PlayerController unprepared = CreatePlayer(unpreparedPosition + Vector3.right * 3f, false);
        Assert.That(observer.BeginRound(1), Is.True);
        yield return null;
        yield return null;
        Assert.That(DroneController.CanTarget(unprepared), Is.False);
        Assert.That(observer.CurrentState, Is.EqualTo(DroneState.Patrol));
        Assert.That(observer.AlertProgress, Is.Zero);
    }

    [UnityTest]
    public IEnumerator AlertPhysicalContactCannotDamageUntilTheFillReallyReachesChase()
    {
        config.patrolSpeed = 0f;
        DroneController drone = CreateDrone(origin, origin);
        PlayerController player = CreatePlayer(origin + Vector3.right * 3f);
        Assert.That(drone.BeginRound(1), Is.True);
        yield return ReachProgress(drone, 0.25f);
        CapsuleCollider body = player.GetComponent<CapsuleCollider>();
        Vector3 centerOffset = body.bounds.center - player.transform.position;
        MovePlayer(player, drone.Body.position + Vector3.right * 0.1f - centerOffset);
        player.Body.isKinematic = false;
        player.Body.useGravity = false;
        player.Body.constraints = RigidbodyConstraints.FreezeAll;
        Physics.SyncTransforms();
        Assert.That(drone.vision.CanSee(player), Is.True);
        Assert.That(Vector3.Distance(body.ClosestPoint(drone.Body.position), drone.Body.position),
            Is.LessThan(drone.GetComponent<SphereCollider>().radius));
        int health = player.health.CurrentHealth;
        float deadline = Time.realtimeSinceStartup + config.alertFillSeconds + 2f;
        int safeSamples = 0;
        while (drone.CurrentState == DroneState.Alert && Time.realtimeSinceStartup < deadline)
        {
            safeSamples++;
            Assert.That(drone.TryExplode(player), Is.False);
            Assert.That(drone.HasExploded, Is.False);
            Assert.That(drone.GetComponent<SphereCollider>().enabled, Is.True);
            Assert.That(player.health.CurrentHealth, Is.EqualTo(health), "Real overlap/collision during Alert must stay harmless.");
            yield return new WaitForFixedUpdate();
        }
        Assert.That(safeSamples, Is.GreaterThan(1));
        Assert.That(drone.CurrentState == DroneState.Chase || drone.HasExploded, Is.True,
            "The contact must become dangerous by completing Alert, without forcing Chase in the fixture.");
        if (!drone.HasExploded) { Assert.That(drone.TryExplode(player), Is.True); }
        Assert.That(drone.HasExploded, Is.True);
        Assert.That(player.health.CurrentHealth, Is.EqualTo(health - config.contactDamage));
        Assert.That(drone.TryExplode(player), Is.False);
        yield return new WaitForFixedUpdate();
        Assert.That(player.health.CurrentHealth, Is.EqualTo(health - config.contactDamage));
    }

    [UnityTest]
    public IEnumerator SearchReacquisitionStartsAnotherUnfilledAlertBeforeChasingAgain()
    {
        config.patrolSpeed = 0f;
        config.chaseSpeed = 0f;
        DroneController drone = CreateDrone(origin, origin);
        PlayerController player = CreatePlayer(origin + Vector3.right * 3f);
        Assert.That(drone.BeginRound(1), Is.True);
        yield return ReachProgress(drone, 0.3f);
        yield return Until(() => drone.CurrentState == DroneState.Chase, config.alertFillSeconds + 2f, "Initial Alert must fill.");
        MovePlayer(player, origin + Vector3.up * 12f);
        yield return Until(() => drone.CurrentState == DroneState.Search, 2f, "Losing a chased target must still use Search.");
        MovePlayer(player, origin + Vector3.right * 3f);
        Assert.That(drone.vision.CanSee(player), Is.True);
        yield return Until(() => drone.CurrentState == DroneState.Alert, 2f, "Search discovery must use a fresh Alert.");
        Assert.That(drone.AlertProgress, Is.LessThan(0.5f), "The previous full Alert must not carry straight back into Chase.");
        Vector3 hover = drone.Body.position;
        yield return ReachProgress(drone, 0.3f);
        AssertHovering(drone, hover);
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
    }

    [UnityTest]
    public IEnumerator TwoDronesKeepTheirProgressAndFillIndependentWhileOneDecays()
    {
        config.patrolSpeed = 0f;
        Vector3 secondPosition = origin + Vector3.up * 8f;
        DroneController first = CreateDrone(origin, origin);
        DroneController second = CreateDrone(secondPosition, secondPosition);
        CreatePlayer(origin + Vector3.right * 3f);
        PlayerController secondPlayer = CreatePlayer(secondPosition + Vector3.up * 12f);
        Assert.That(first.BeginRound(1), Is.True);
        Assert.That(second.BeginRound(1), Is.True);
        yield return ReachProgress(first, 0.5f);
        Assert.That(second.CurrentState, Is.EqualTo(DroneState.Patrol));
        Assert.That(second.AlertProgress, Is.Zero);
        AssertFillMatches(second);
        Wall(origin + new Vector3(1.5f, 0.7f, 0f));
        float firstBefore = first.AlertProgress;
        MovePlayer(secondPlayer, secondPosition + Vector3.right * 3f);
        yield return ReachProgress(second, 0.15f);
        Assert.That(first.CurrentState, Is.EqualTo(DroneState.Alert));
        Assert.That(first.AlertProgress, Is.GreaterThan(0f).And.LessThan(firstBefore));
        Assert.That(second.AlertProgress, Is.GreaterThan(0f));
        AssertFillMatches(first);
        AssertFillMatches(second);
        float secondBefore = second.AlertProgress;
        Assert.That(first.BeginRound(2), Is.True);
        Assert.That(first.AlertProgress, Is.Zero);
        Assert.That(second.CurrentRound, Is.EqualTo(1));
        Assert.That(second.AlertProgress, Is.EqualTo(secondBefore));
        AssertFillMatches(second);
    }

    [UnityTest]
    public IEnumerator SameRoundKeepsProgressButNewRoundDisableAndDestroyClearIt()
    {
        config.patrolSpeed = 0f;
        DroneController drone = CreateDrone(origin, origin);
        PlayerController player = CreatePlayer(origin + Vector3.right * 3f);
        Assert.That(drone.BeginRound(1), Is.True);
        yield return ReachProgress(drone, 0.35f);
        float progress = drone.AlertProgress;
        Assert.That(drone.BeginRound(1), Is.False);
        Assert.That(drone.AlertProgress, Is.EqualTo(progress));
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
        AssertFillMatches(drone);
        drone.ChangeState(DroneState.Alert);
        Assert.That(drone.AlertProgress, Is.EqualTo(progress), "Repeating the current state must not clear its remaining Alert.");
        AssertFillMatches(drone);
        MovePlayer(player, origin + Vector3.up * 12f);
        Assert.That(drone.BeginRound(2), Is.True);
        Assert.That(drone.AlertProgress, Is.Zero);
        Assert.That(drone.Target, Is.Null);
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
        AssertFillMatches(drone);

        MovePlayer(player, origin + Vector3.right * 3f);
        yield return ReachProgress(drone, 0.35f);
        drone.enabled = false;
        Assert.That(drone.AlertProgress, Is.Zero);
        Assert.That(drone.Target, Is.Null);
        Assert.That(drone.vision.RangeCollider.enabled, Is.False);
        AssertFillMatches(drone);
        MovePlayer(player, origin + Vector3.up * 12f);
        drone.enabled = true;
        Assert.That(drone.CurrentRound, Is.EqualTo(2));
        Assert.That(drone.AlertProgress, Is.Zero);
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));

        MovePlayer(player, origin + Vector3.right * 3f);
        yield return ReachProgress(drone, 0.35f);
        drone.gameObject.SetActive(false);
        Assert.That(drone.AlertProgress, Is.Zero);
        AssertFillMatches(drone);
        MovePlayer(player, origin + Vector3.up * 12f);
        drone.gameObject.SetActive(true);
        Assert.That(drone.AlertProgress, Is.Zero);
        Assert.That(drone.CurrentRound, Is.EqualTo(2));
        MovePlayer(player, origin + Vector3.right * 3f);
        yield return ReachProgress(drone, 0.35f);
        DroneVision remainingVision = drone.vision;
        Object.Destroy(drone);
        yield return null;
        Assert.That(drone == null, Is.True);
        Assert.That(Fill(remainingVision), Is.Zero, "Destroying the owner must not leave its surviving renderer filled.");
        Assert.That(remainingVision.RangeCollider.enabled, Is.False);
    }

    private IEnumerator ReachProgress(DroneController drone, float progress)
    {
        yield return Until(() => drone.CurrentState == DroneState.Alert, 2f, "A visible target must enter Alert naturally.");
        yield return Until(() => drone.AlertProgress >= progress || drone.CurrentState != DroneState.Alert,
            config.alertFillSeconds + 2f, "Alert must increase while the real target remains visible.");
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
        Assert.That(drone.AlertProgress, Is.GreaterThanOrEqualTo(progress).And.LessThan(1f));
        AssertFillMatches(drone);
    }

    private IEnumerator DrainToPatrol(DroneController drone, Vector3 hover)
    {
        float previous = drone.AlertProgress;
        float deadline = Time.realtimeSinceStartup + config.alertDecaySeconds + 2f;
        while (drone.CurrentState == DroneState.Alert && Time.realtimeSinceStartup < deadline)
        {
            AssertHovering(drone, hover);
            Assert.That(drone.AlertProgress, Is.LessThanOrEqualTo(previous + 0.0001f));
            AssertFillMatches(drone);
            previous = drone.AlertProgress;
            yield return null;
        }
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
        Assert.That(drone.AlertProgress, Is.Zero);
        AssertFillMatches(drone);
    }

    private static void AssertHovering(DroneController drone, Vector3 position)
    {
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
        AssertVector(drone.Body.position, position);
        Assert.That(drone.FlightVelocity.sqrMagnitude, Is.LessThan(0.0001f));
        Assert.That(drone.HasExploded, Is.False);
        Assert.That(Quaternion.Angle(drone.Body.rotation, Quaternion.identity), Is.LessThan(0.01f));
    }

    private static float Fill(DroneVision vision)
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        vision.GetComponent<MeshRenderer>().GetPropertyBlock(block);
        return block.GetFloat("_FillProgress");
    }

    private static void AssertFillMatches(DroneController drone)
    {
        Assert.That(Fill(drone.vision), Is.EqualTo(drone.AlertProgress).Within(0.0001f),
            "This instance's visual fill must reflect its controller progress; this is not rendered-image acceptance.");
    }

    private DroneController CreateDrone(Vector3 position, Vector3 endpoint)
    {
        GameObject root = Own(new GameObject("Alert test drone"));
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
        GameObject point = Own(new GameObject("Alert patrol point"));
        point.transform.position = position;
        return point.transform;
    }

    private PlayerController CreatePlayer(Vector3 position, bool prepare = true)
    {
        PlayerConfigSO settings = Own(ScriptableObject.CreateInstance<PlayerConfigSO>());
        settings.planeZ = position.z;
        settings.groundLayers = 0;
        settings.damageInvulnerabilitySeconds = 0f;
        GameObject root = Own(new GameObject("Alert test player"));
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
        GameObject root = Own(new GameObject("Alert real wall"));
        root.transform.position = position;
        BoxCollider wall = root.AddComponent<BoxCollider>();
        wall.size = new Vector3(0.2f, 5f, 3f);
        Physics.SyncTransforms();
        return wall;
    }

    private CoverArea Cover(Vector3 position)
    {
        GameObject root = Own(new GameObject("Alert real crouching cover"));
        root.transform.position = position;
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(1f, 3f, 2f);
        CoverArea cover = root.AddComponent<CoverArea>();
        Physics.SyncTransforms();
        return cover;
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
