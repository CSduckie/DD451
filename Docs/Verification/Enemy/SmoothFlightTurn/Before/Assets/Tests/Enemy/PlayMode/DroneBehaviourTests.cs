using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class DroneBehaviourTests
{
    private readonly List<Object> owned = new List<Object>();
    private readonly Vector3 origin = new Vector3(4000f, 4000f, 40f);
    private DroneConfigSO config;

    [SetUp]
    public void SetUp()
    {
        config = Own(ScriptableObject.CreateInstance<DroneConfigSO>());
        config.patrolSpeed = 2f;
        config.chaseSpeed = 4f;
        config.arrivalDistance = 0.06f;
        config.patrolWaitSeconds = 0.04f;
        config.searchSeconds = 0.08f;
        config.blockedReturnSeconds = 0.12f;
        config.contactDamage = 1;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (Object item in owned)
        {
            if (item is GameObject gameObject) { gameObject.SetActive(false); }
        }
        foreach (Object item in owned) { if (item != null) { Object.Destroy(item); } }
        owned.Clear();
        yield return null;
    }

    [UnityTest]
    public IEnumerator TwoDronesPatrolTheirOwnRoutesAndExplosionDoesNotStopTheOther()
    {
        DroneController first = CreateDrone(origin, origin + new Vector3(1.5f, 0f, 8f));
        Vector3 secondStart = origin + new Vector3(0f, 4f, 1f);
        DroneController second = CreateDrone(secondStart, secondStart + new Vector3(-1.5f, 0f, -9f));
        string settings = JsonUtility.ToJson(config);
        Assert.That(first.BeginRound(1), Is.True);
        Assert.That(second.BeginRound(1), Is.True);
        yield return Until(() => first.Body.position.x > origin.x + 0.2f
            && second.Body.position.x < secondStart.x - 0.2f, 2f, "Both independent patrols must move.");
        Assert.That(first.CurrentState, Is.EqualTo(DroneState.Patrol));
        Assert.That(second.CurrentState, Is.EqualTo(DroneState.Patrol));
        Assert.That(first.FacingDirection, Is.EqualTo(1));
        Assert.That(second.FacingDirection, Is.EqualTo(-1));
        Assert.That(first.Body.position.z, Is.EqualTo(origin.z).Within(0.001f));
        Assert.That(second.Body.position.z, Is.EqualTo(secondStart.z).Within(0.001f));

        PlayerController player = CreatePlayer(first.Body.position);
        Physics.SyncTransforms();
        Assert.That(first.TryExplode(player), Is.True);
        Vector3 otherPosition = second.Body.position;
        yield return Until(() => Vector3.Distance(second.Body.position, otherPosition) > 0.15f, 2f,
            "One explosion must not reset or freeze another drone's state machine.");
        Assert.That(first.HasExploded, Is.True);
        Assert.That(second.HasExploded, Is.False);
        Assert.That(second.CurrentRound, Is.EqualTo(1));
        Assert.That(second.CurrentState, Is.EqualTo(DroneState.Patrol));
        Assert.That(JsonUtility.ToJson(config), Is.EqualTo(settings));
    }

    [UnityTest]
    public IEnumerator SphereSweepStopsAtSolidWallAndNeverMovesIntoAnotherDepthPlane()
    {
        DroneController drone = CreateDrone(origin, origin + new Vector3(3f, 0f, 10f));
        BoxCollider wall = CreateWall(origin + Vector3.right * 1.2f);
        Physics.SyncTransforms();
        Assert.That(drone.BeginRound(1), Is.True);
        yield return Until(() => drone.IsBlocked, 3f, "The patrol should reach the wall.");
        float furthestX = drone.Body.position.x;
        float deadline = Time.time + config.blockedReturnSeconds + config.patrolWaitSeconds + 0.2f;
        while (Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
            furthestX = Mathf.Max(furthestX, drone.Body.position.x);
            Assert.That(drone.Body.position.z, Is.EqualTo(origin.z).Within(0.001f));
        }
        Assert.That(furthestX, Is.GreaterThan(origin.x + 0.2f));
        Assert.That(furthestX + drone.GetComponent<SphereCollider>().radius,
            Is.LessThanOrEqualTo(wall.bounds.min.x + 0.01f));
        Assert.That(Quaternion.Angle(drone.Body.rotation, Quaternion.identity), Is.LessThan(0.01f));
        Assert.That(drone.HasExploded, Is.False);
    }

    [UnityTest]
    public IEnumerator LostSightKeepsOnlyTheLastSeenPositionThenSearchReturnsToPatrol()
    {
        DroneController drone = CreateDrone(origin, origin, true);
        PlayerController player = CreatePlayer(origin + Vector3.right * 3f);
        Physics.SyncTransforms();
        Assert.That(drone.BeginRound(1), Is.True);
        yield return Until(() => drone.CurrentState == DroneState.Chase, 2f, "The visible player should be acquired.");
        Assert.That(drone.Target, Is.SameAs(player));
        Vector3 lastSeen = drone.LastKnownPosition;
        MovePlayer(player, origin + new Vector3(3f, 10f, 0f));
        yield return Until(() => drone.CurrentState == DroneState.Search, 2f, "Leaving vision should start last-position search.");
        Assert.That(drone.Target, Is.Null);
        AssertVector(drone.LastKnownPosition, lastSeen);
        MovePlayer(player, origin + new Vector3(-8f, 12f, 0f));

        bool reachedLastSeen = false;
        float timeout = Vector3.Distance(drone.Body.position, lastSeen) / config.chaseSpeed + config.searchSeconds + 2f;
        float deadline = Time.realtimeSinceStartup + timeout;
        while (drone.CurrentState != DroneState.Patrol && Time.realtimeSinceStartup < deadline)
        {
            AssertVector(drone.LastKnownPosition, lastSeen);
            Assert.That(drone.Target, Is.Null);
            reachedLastSeen |= Vector3.Distance(drone.Body.position, lastSeen) <= config.arrivalDistance + 0.03f;
            yield return null;
        }
        Assert.That(reachedLastSeen, Is.True, "Search must visit the captured position, not the player's later position.");
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
        Assert.That(drone.HasExploded, Is.False);
        Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth));
    }

    [UnityTest]
    public IEnumerator ObstructedLastPositionSearchWaitsAtWallThenReturnsWithoutFollowingHiddenMovement()
    {
        DroneController drone = CreateDrone(origin, origin, true);
        PlayerController player = CreatePlayer(origin + Vector3.right * 3f);
        Physics.SyncTransforms();
        Assert.That(drone.BeginRound(1), Is.True);
        yield return Until(() => drone.CurrentState == DroneState.Chase, 2f, "Initial line of sight should acquire the player.");
        Vector3 lastSeen = drone.LastKnownPosition;
        BoxCollider wall = CreateWall(origin + Vector3.right * 1.2f);
        MovePlayer(player, origin + Vector3.right * 4f);
        yield return Until(() => drone.CurrentState == DroneState.Search, 2f, "The new solid wall should break sight.");
        AssertVector(drone.LastKnownPosition, lastSeen);
        float furthestX = drone.Body.position.x;
        float deadline = Time.realtimeSinceStartup + 3f;
        while (drone.CurrentState != DroneState.Patrol && Time.realtimeSinceStartup < deadline)
        {
            AssertVector(drone.LastKnownPosition, lastSeen);
            furthestX = Mathf.Max(furthestX, drone.Body.position.x);
            yield return null;
        }
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
        Assert.That(furthestX + drone.GetComponent<SphereCollider>().radius,
            Is.LessThanOrEqualTo(wall.bounds.min.x + 0.01f));
        Assert.That(drone.HasExploded, Is.False);
    }

    [UnityTest]
    public IEnumerator OpenBackpackContactAndInvulnerableContactEachExplodeOnlyOnce()
    {
        PlayerController player = CreatePlayer(origin);
        player.config.damageInvulnerabilitySeconds = 0.08f;
        player.SetBackpackOpen(true);
        Transform extraBody = new GameObject("Additional player body collider").transform;
        extraBody.SetParent(player.transform, false);
        extraBody.gameObject.AddComponent<SphereCollider>().radius = 0.3f;
        int nonLethalEvents = 0;
        player.health.NonLethalDamaged += amount => nonLethalEvents++;
        DroneController first = CreateDrone(origin, origin);
        DroneController second = CreateDrone(origin, origin);
        Assert.That(first.BeginRound(1), Is.True);
        Assert.That(second.BeginRound(1), Is.True);
        Physics.SyncTransforms();
        Assert.That(player.CanAct, Is.False);
        Assert.That(first.TryExplode(player), Is.True);
        Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth - 1));
        Assert.That(first.TryExplode(player), Is.False);
        Assert.That(second.TryExplode(player), Is.True, "An invulnerable target still consumes this drone's one explosion.");
        Assert.That(first.HasExploded && second.HasExploded, Is.True);
        Assert.That(nonLethalEvents, Is.EqualTo(1));
        Assert.That(first.GetComponent<SphereCollider>().enabled || second.GetComponent<SphereCollider>().enabled, Is.False);
        yield return new WaitForSeconds(player.config.damageInvulnerabilitySeconds + Time.fixedDeltaTime);
        Assert.That(first.TryExplode(player) || second.TryExplode(player), Is.False);
        Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth - 1));
        Assert.That(nonLethalEvents, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator PatrolSweepContactFinishesWithoutUsingTheUninitializedStateAgain()
    {
        DroneController drone = CreateDrone(origin, origin + Vector3.right * 3f);
        PlayerController player = CreatePlayer(origin + Vector3.right * 1.2f);
        Physics.SyncTransforms();
        Assert.That(drone.BeginRound(1), Is.True);
        yield return Until(() => drone.HasExploded, 3f, "A real patrol sweep should contact the player.");
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Explode));
        Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth - 1));
        Assert.That(drone.Target, Is.Null);
        yield return new WaitForFixedUpdate();
        Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth - 1));
    }

    [UnityTest]
    public IEnumerator ExplodedObjectCannotReviveOnReenableOrSameRoundButNewRoundRestoresIt()
    {
        DroneController drone = CreateDrone(origin, origin, true);
        PlayerController player = CreatePlayer(origin);
        Assert.That(drone.BeginRound(1), Is.True);
        Physics.SyncTransforms();
        Assert.That(drone.TryExplode(player), Is.True);
        MovePlayer(player, origin + Vector3.right * 20f);
        drone.enabled = false;
        yield return null;
        drone.enabled = true;
        drone.gameObject.SetActive(false);
        yield return null;
        drone.gameObject.SetActive(true);
        yield return new WaitForFixedUpdate();
        Assert.That(drone.HasExploded, Is.True);
        Assert.That(drone.CurrentRound, Is.EqualTo(1));
        Assert.That(drone.BeginRound(1), Is.False);
        Assert.That(drone.GetComponent<SphereCollider>().enabled, Is.False);
        Assert.That(drone.visual.gameObject.activeSelf, Is.False);
        Assert.That(drone.vision.gameObject.activeSelf, Is.False);
        drone.Body.position = origin + Vector3.up * 3f;
        Assert.That(drone.BeginRound(2), Is.True);
        yield return new WaitForFixedUpdate();
        Assert.That(drone.HasExploded, Is.False);
        Assert.That(drone.CurrentRound, Is.EqualTo(2));
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
        Assert.That(drone.GetComponent<SphereCollider>().enabled, Is.True);
        Assert.That(drone.visual.gameObject.activeSelf, Is.True);
        Assert.That(drone.vision.RangeCollider.enabled, Is.True);
        AssertVector(drone.Body.position, origin);
    }

    [UnityTest]
    public IEnumerator DamageObserverDisablingControllerStillFinishesExplosionCleanup()
    {
        DroneController drone = CreateDrone(origin, origin, true);
        PlayerController player = CreatePlayer(origin);
        player.health.NonLethalDamaged += amount => drone.enabled = false;
        Assert.That(drone.BeginRound(1), Is.True);
        Physics.SyncTransforms();
        Assert.That(drone.TryExplode(player), Is.True);
        Assert.That(drone.HasExploded, Is.True);
        Assert.That(drone.GetComponent<SphereCollider>().enabled, Is.False);
        Assert.That(drone.visual.gameObject.activeSelf, Is.False);
        Assert.That(drone.vision.gameObject.activeSelf, Is.False);
        Assert.That(drone.Target, Is.Null);
        Assert.That(drone.FlightVelocity, Is.EqualTo(Vector3.zero));
        drone.enabled = true;
        yield return new WaitForFixedUpdate();
        Assert.That(drone.IsRunning, Is.False);
        Assert.That(drone.TryExplode(player), Is.False);
        Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth - 1));
    }

    [UnityTest]
    public IEnumerator ReenablingLivingControllerRestoresVisionAndCanAcquireAgain()
    {
        config.chaseSpeed = 0f;
        DroneController drone = CreateDrone(origin, origin, true);
        PlayerController player = CreatePlayer(origin + Vector3.right * 3f);
        Physics.SyncTransforms();
        Assert.That(drone.BeginRound(1), Is.True);
        yield return Until(() => drone.Target == player, 2f, "The initial vision should acquire the player.");
        drone.enabled = false;
        Assert.That(drone.Target, Is.Null);
        Assert.That(drone.vision.RangeCollider.enabled, Is.False);
        drone.enabled = true;
        yield return Until(() => drone.Target == player, 2f,
            "Reenabling only DroneController must restore its cleared vision, even when the child stayed active.");
        Assert.That(drone.CurrentRound, Is.EqualTo(1));
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Chase));
    }

    [UnityTest]
    public IEnumerator InvalidPlayersNeverExplodeADroneEvenWhenBodyShapesOverlap()
    {
        for (int invalid = 0; invalid < 5; invalid++)
        {
            Vector3 position = origin + Vector3.right * (invalid * 6f);
            PlayerController player = CreatePlayer(position, invalid != 0);
            if (invalid == 1) { player.enabled = false; }
            if (invalid == 2) { player.gameObject.SetActive(false); }
            if (invalid == 3) { Assert.That(player.health.TakeDamage(player.health.MaxHealth), Is.True); }
            if (invalid == 4) { player.SetSceneTransitionBlocked(true); }
            DroneController drone = CreateDrone(position, position);
            Assert.That(drone.BeginRound(1), Is.True);
            Physics.SyncTransforms();
            Assert.That(DroneController.CanTarget(player), Is.False, "Unavailable fixture " + invalid);
            Assert.That(drone.TryExplode(player), Is.False, "Unavailable fixture " + invalid);
            yield return new WaitForFixedUpdate();
            Assert.That(drone.HasExploded, Is.False, "Automatic contacts must use the same eligibility rules.");
        }
        Assert.That(DroneController.CanTarget(null), Is.False);
    }

    [UnityTest]
    public IEnumerator RotorAndVisualAnimationNeverRotateOrScaleThePhysicalRootOrVision()
    {
        DroneController drone = CreateDrone(origin, origin + Vector3.right * 3f, true);
        drone.vision.playerMask = 0;
        DroneFlightVisual flight = drone.gameObject.AddComponent<DroneFlightVisual>();
        flight.visual = drone.visual;
        flight.rotorDegreesPerSecond = 360f;
        flight.propellers = new Transform[4];
        for (int i = 0; i < flight.propellers.Length; i++)
        {
            Transform rotor = new GameObject("Rotor " + i).transform;
            rotor.SetParent(drone.visual, false);
            flight.propellers[i] = rotor;
        }
        drone.flightVisual = flight;
        Vector3 restPosition = drone.visual.localPosition;
        Quaternion restRotation = drone.visual.localRotation;
        Vector3 visionScale = drone.vision.transform.localScale;
        SphereCollider contact = drone.GetComponent<SphereCollider>();
        Vector3 contactCenter = contact.center;
        float contactRadius = contact.radius;
        Assert.That(drone.BeginRound(1), Is.True);
        yield return Until(() => drone.Body.position.x > origin.x + 0.2f
            && Quaternion.Angle(flight.propellers[0].localRotation, Quaternion.identity) > 5f,
            2f, "Flight and rotor motion should both advance.");
        Assert.That(Quaternion.Angle(drone.visual.localRotation, restRotation), Is.GreaterThan(0.1f));
        Assert.That(Quaternion.Angle(flight.propellers[1].localRotation, Quaternion.identity), Is.GreaterThan(5f));
        Assert.That(Quaternion.Angle(drone.Body.rotation, Quaternion.identity), Is.LessThan(0.01f));
        AssertVector(drone.transform.localScale, Vector3.one);
        AssertVector(drone.vision.transform.localPosition, Vector3.zero);
        AssertVector(drone.vision.transform.localScale, visionScale);
        Assert.That(drone.vision.transform.IsChildOf(drone.visual), Is.False);
        AssertVector(contact.center, contactCenter);
        Assert.That(contact.radius, Is.EqualTo(contactRadius));
        flight.enabled = false;
        AssertVector(drone.visual.localPosition, restPosition);
        Assert.That(Quaternion.Angle(drone.visual.localRotation, restRotation), Is.LessThan(0.01f));
    }

    private DroneController CreateDrone(Vector3 position, Vector3 endpoint, bool withVision = false)
    {
        GameObject root = Own(new GameObject("Behaviour test drone"));
        root.transform.position = position;
        DroneController result = root.AddComponent<DroneController>();
        result.config = config;
        result.GetComponent<SphereCollider>().radius = 0.2f;
        Transform visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        result.visual = visual;
        result.patrolPoints = new[] { CreatePoint(endpoint), CreatePoint(position) };
        if (withVision)
        {
            GameObject volume = new GameObject("Vision");
            volume.transform.SetParent(root.transform, false);
            result.vision = volume.AddComponent<DroneVision>();
            result.vision.radius = 6f;
            result.vision.angleDegrees = 100f;
            result.vision.thickness = 2f;
            result.vision.Rebuild();
        }
        return result;
    }

    private Transform CreatePoint(Vector3 position)
    {
        GameObject point = Own(new GameObject("Behaviour test patrol point"));
        point.transform.position = position;
        return point.transform;
    }

    private PlayerController CreatePlayer(Vector3 position, bool prepare = true)
    {
        PlayerConfigSO settings = Own(ScriptableObject.CreateInstance<PlayerConfigSO>());
        settings.planeZ = position.z;
        settings.groundLayers = 0;
        settings.damageInvulnerabilitySeconds = 0f;
        GameObject root = Own(new GameObject("Behaviour test player"));
        root.transform.position = position;
        PlayerController result = root.AddComponent<PlayerController>();
        result.config = settings;
        result.health.config = settings;
        if (prepare) { Assert.That(result.ResetForExploration(1, position), Is.True); }
        // 夹具只固定目标位置，不禁用Player组件、不设置流程锁，保持真实可发现/受击资格。
        root.GetComponent<Rigidbody>().isKinematic = true;
        return result;
    }

    private BoxCollider CreateWall(Vector3 position)
    {
        GameObject wall = Own(new GameObject("Behaviour test solid wall"));
        wall.transform.position = position;
        BoxCollider collider = wall.AddComponent<BoxCollider>();
        collider.size = new Vector3(0.2f, 4f, 2f);
        Physics.SyncTransforms();
        return collider;
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
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.002f));
    }
}
