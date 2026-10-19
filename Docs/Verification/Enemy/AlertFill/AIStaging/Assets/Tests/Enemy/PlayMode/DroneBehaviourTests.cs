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
        // 本夹具验证原行为链，缩短等待但仍经过真实Alert；渐增/渐退由DroneAlertTests验证。
        config.alertFillSeconds = 0.25f;
        config.alertDecaySeconds = 0.25f;
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
        first.ChangeState(DroneState.Chase);
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
        yield return WaitForAlertThenChase(drone, "The visible player should be acquired through a full Alert.");
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
        yield return WaitForAlertThenChase(drone, "Initial line of sight should acquire the player through a full Alert.");
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
        first.ChangeState(DroneState.Chase);
        second.ChangeState(DroneState.Chase);
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
    public IEnumerator PatrolAndSearchContactsStayHarmlessUntilChaseThenExplodeOnlyOnce()
    {
        config.patrolSpeed = 0f;
        config.chaseSpeed = 0f;
        config.searchSeconds = 10f;
        DroneState[] states = { DroneState.Patrol, DroneState.Search };
        for (int i = 0; i < states.Length; i++)
        {
            Vector3 position = origin + Vector3.right * (i * 6f);
            DroneController drone = CreateDrone(position, position);
            PlayerController player = CreatePlayer(position);
            // 保持真实玩家资格，以冻结位置的动态刚体同时经过物理接触回调和重叠检查。
            player.Body.isKinematic = false;
            player.Body.useGravity = false;
            player.Body.constraints = RigidbodyConstraints.FreezeAll;
            Assert.That(drone.BeginRound(1), Is.True);
            drone.ChangeState(states[i]);
            Physics.SyncTransforms();
            Assert.That(DroneController.CanTarget(player), Is.True);
            Assert.That(drone.TryExplode(player), Is.False);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(drone.CurrentState, Is.EqualTo(states[i]));
            Assert.That(drone.HasExploded, Is.False, "Patrol/Search contact cannot bypass the state gate.");
            Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth));
            Assert.That(drone.GetComponent<SphereCollider>().enabled, Is.True);

            drone.ChangeState(DroneState.Chase);
            Assert.That(drone.TryExplode(player), Is.True, "The same physical contact becomes dangerous only in Chase.");
            Assert.That(drone.TryExplode(player), Is.False);
            yield return new WaitForFixedUpdate();
            Assert.That(drone.HasExploded, Is.True);
            Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth - 1));
        }
    }

    [UnityTest]
    public IEnumerator PatrolAndSearchSweepsBlockAtPlayerWithoutDamageButChaseSweepExplodes()
    {
        config.patrolSpeed = 0f;
        config.chaseSpeed = 0f;
        DroneState[] states = { DroneState.Patrol, DroneState.Search };
        for (int i = 0; i < states.Length; i++)
        {
            Vector3 position = origin + Vector3.right * (i * 6f);
            DroneController drone = CreateDrone(position, position);
            PlayerController player = CreatePlayer(position + Vector3.right * 1.2f);
            Assert.That(drone.BeginRound(1), Is.True);
            drone.ChangeState(states[i]);
            Physics.SyncTransforms();
            // 单步故意跨过玩家，验证MoveTowards内部的SphereCast路径而不是重叠API。
            drone.MoveTowards(position + Vector3.right * 3f, 100f);
            Assert.That(drone.IsBlocked, Is.True, "A non-chasing drone still respects the player's solid collider.");
            Assert.That(drone.HasExploded, Is.False, "SphereCast must not bypass Patrol/Search protection.");
            Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth));

            drone.ChangeState(DroneState.Chase);
            drone.MoveTowards(position + Vector3.right * 3f, 100f);
            Assert.That(drone.HasExploded, Is.True, "The same sweep may explode after switching to Chase.");
            Assert.That(drone.TryExplode(player), Is.False);
            yield return new WaitForFixedUpdate();
            Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth - 1));
        }
    }

    [UnityTest]
    public IEnumerator ChaseSweepContactFinishesWithoutUsingTheUninitializedStateAgain()
    {
        DroneController drone = CreateDrone(origin, origin + Vector3.right * 3f, true);
        PlayerController player = CreatePlayer(origin + Vector3.right * 1.2f);
        Physics.SyncTransforms();
        Assert.That(drone.BeginRound(1), Is.True);
        yield return WaitForAlertThenChase(drone,
            "The visible player must fill Alert before the chase sweep can cause damage.");
        yield return Until(() => drone.HasExploded, 3f, "A real chase sweep should contact the player.");
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
        drone.ChangeState(DroneState.Chase);
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
        drone.ChangeState(DroneState.Chase);
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
        yield return WaitForAlertThenChase(drone, "Initial discovery must fill Alert before Chase.");
        drone.enabled = false;
        Assert.That(drone.Target, Is.Null);
        Assert.That(drone.vision.RangeCollider.enabled, Is.False);
        drone.enabled = true;
        yield return Until(() => drone.Target == player, 2f,
            "Reenabling only DroneController must restore its cleared vision, even when the child stayed active.");
        yield return WaitForAlertThenChase(drone, "Reenabling must acquire through a new Alert, not skip its fill.");
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
            drone.ChangeState(DroneState.Chase);
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

    [UnityTest]
    public IEnumerator FlightLeansGraduallyThenLevelsAtRestWithoutBobOrPeriodicSway()
    {
        config.patrolSpeed = config.chaseSpeed;
        config.patrolWaitSeconds = 10f;
        DroneController drone = CreateDrone(origin, origin + Vector3.right * 30f, true);
        drone.vision.playerMask = 0;
        drone.visual.localPosition = new Vector3(0.05f, 0.3f, 0.02f);
        drone.visual.localRotation = Quaternion.Euler(0f, 90f, 0f);
        DroneFlightVisual flight = drone.gameObject.AddComponent<DroneFlightVisual>();
        flight.visual = drone.visual;
        flight.tiltDegrees = 10f;
        flight.tiltSmoothTime = 0.3f;
        drone.flightVisual = flight;
        Vector3 restPosition = drone.visual.localPosition;
        Quaternion restRotation = drone.visual.localRotation;
        Assert.That(drone.BeginRound(1), Is.True);

        bool sawPartialLean = false;
        float previousLean = 0f;
        float deadline = Time.realtimeSinceStartup + 4f;
        while (previousLean < flight.tiltDegrees * 0.995f && Time.realtimeSinceStartup < deadline)
        {
            yield return new WaitForFixedUpdate();
            float lean = Quaternion.Angle(drone.visual.localRotation, restRotation);
            sawPartialLean |= lean > 0.25f && lean < flight.tiltDegrees * 0.9f;
            Assert.That(lean, Is.GreaterThanOrEqualTo(previousLean - 0.05f),
                "Constant forward flight should approach its lean without periodic rocking.");
            Assert.That(lean, Is.LessThanOrEqualTo(flight.tiltDegrees + 0.05f));
            Assert.That(Vector3.Distance(drone.visual.localPosition, restPosition), Is.LessThan(0.0001f),
                "The model must retain its authored local position while flying; bob is no longer requested.");
            previousLean = lean;
        }
        Assert.That(sawPartialLean, Is.True, "Starting flight must visibly pass through a partial lean, not snap.");
        Assert.That(previousLean, Is.GreaterThan(flight.tiltDegrees * 0.99f));
        Vector3 nose = drone.visual.localRotation * Quaternion.Inverse(restRotation) * Vector3.right;
        Assert.That(nose.y, Is.LessThan(-0.05f), "Forward movement should lower the nose.");

        Quaternion settledLean = drone.visual.localRotation;
        deadline = Time.realtimeSinceStartup + 0.8f;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return new WaitForFixedUpdate();
            Assert.That(Quaternion.Angle(drone.visual.localRotation, settledLean), Is.LessThan(0.1f),
                "Steady flight must hold its lean rather than add a time-based sway.");
            Assert.That(Vector3.Distance(drone.visual.localPosition, restPosition), Is.LessThan(0.0001f));
        }

        // 到达真实巡逻点后等待，使用正常状态路径停下，不禁用动画或直接改其内部倾角。
        foreach (Transform point in drone.patrolPoints) { point.position = drone.Body.position; }
        bool sawPartialReturn = false;
        float stoppedFrom = Quaternion.Angle(drone.visual.localRotation, restRotation);
        deadline = Time.realtimeSinceStartup + 4f;
        previousLean = stoppedFrom;
        while (previousLean > 0.05f && Time.realtimeSinceStartup < deadline)
        {
            yield return new WaitForFixedUpdate();
            float lean = Quaternion.Angle(drone.visual.localRotation, restRotation);
            sawPartialReturn |= lean > 0.25f && lean < stoppedFrom - 0.25f;
            Assert.That(lean, Is.LessThanOrEqualTo(previousLean + 0.05f),
                "Stopping should smoothly return to the authored rest rotation.");
            Assert.That(Vector3.Distance(drone.visual.localPosition, restPosition), Is.LessThan(0.0001f));
            previousLean = lean;
        }
        Assert.That(sawPartialReturn, Is.True, "Stopping must pass through a partial lean, not snap upright.");
        Assert.That(previousLean, Is.LessThanOrEqualTo(0.05f));
        Assert.That(drone.FlightVelocity.sqrMagnitude, Is.LessThan(0.0001f));
        Vector3 stoppedPosition = drone.Body.position;
        deadline = Time.realtimeSinceStartup + 0.8f;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return new WaitForFixedUpdate();
            Assert.That(Quaternion.Angle(drone.visual.localRotation, restRotation), Is.LessThan(0.1f));
            Assert.That(Vector3.Distance(drone.visual.localPosition, restPosition), Is.LessThan(0.0001f));
            AssertVector(drone.Body.position, stoppedPosition);
        }
    }

    [UnityTest]
    public IEnumerator SmoothYawSharesHeadingWithVisionSurvivesRebuildAndResetsOnlyForNewRound()
    {
        DroneController drone = CreateDrone(origin, origin + Vector3.left * 30f, true);
        drone.turnSmoothTime = 0.35f;
        drone.vision.playerMask = 0;
        drone.vision.directionDegrees = 20f;
        drone.visual.localRotation = Quaternion.Euler(0f, 90f, 0f);
        Quaternion restRotation = drone.visual.localRotation;
        DroneFlightVisual flight = drone.gameObject.AddComponent<DroneFlightVisual>();
        flight.visual = drone.visual;
        flight.tiltDegrees = 0f; // 本项隔离转头；前倾/回正由上一项验证。
        drone.flightVisual = flight;
        Assert.That(drone.BeginRound(1), Is.True);
        Assert.That(Quaternion.Angle(drone.FacingRotation, Quaternion.identity), Is.LessThan(0.01f));

        bool sawIntermediateHeading = false;
        float deadline = Time.realtimeSinceStartup + 3f;
        while (!sawIntermediateHeading && Time.realtimeSinceStartup < deadline)
        {
            // 在FixedUpdate边界读取上一帧已共同完成的Update/LateUpdate姿态，避免半帧误差。
            yield return new WaitForFixedUpdate();
            float turned = Quaternion.Angle(drone.FacingRotation, Quaternion.identity);
            if (turned <= 15f || turned >= 165f) { continue; }
            sawIntermediateHeading = true;
            Assert.That(drone.FacingDirection, Is.EqualTo(-1));
            Assert.That(Vector3.Angle(drone.FacingRotation * Vector3.up, Vector3.up), Is.LessThan(0.01f),
                "The intermediate turn must be yaw about Y, not a roll of the physical root.");
            Quaternion modelHeading = drone.visual.localRotation * Quaternion.Inverse(restRotation);
            Assert.That(Quaternion.Angle(modelHeading, drone.FacingRotation), Is.LessThan(0.05f));
            Assert.That(Vector3.Angle(drone.vision.transform.localRotation * Vector3.forward,
                drone.FacingRotation * Vector3.forward), Is.LessThan(0.05f),
                "Vision thickness and visible fan must turn with the model's current heading.");
            Assert.That(drone.vision.transform.right.y, Is.GreaterThan(0.1f), "Yaw must retain the authored upward pitch.");
            Assert.That(Quaternion.Angle(drone.Body.rotation, Quaternion.identity), Is.LessThan(0.01f));
            Assert.That(Quaternion.Angle(drone.transform.rotation, Quaternion.identity), Is.LessThan(0.01f));
            AssertVector(drone.vision.transform.localPosition, Vector3.zero);

            Quaternion headingBefore = drone.FacingRotation;
            Quaternion visionBefore = drone.vision.transform.localRotation;
            Assert.That(drone.BeginRound(1), Is.False);
            Assert.That(Quaternion.Angle(drone.FacingRotation, headingBefore), Is.LessThan(0.01f));
            drone.vision.Rebuild();
            Physics.SyncTransforms();
            Assert.That(Quaternion.Angle(drone.vision.transform.localRotation, visionBefore), Is.LessThan(0.01f),
                "Rebuilding meshes mid-turn must preserve the live heading, not jump to its left/right target.");
            Assert.That(drone.vision.RangeCollider.sharedMesh, Is.Not.Null);
        }
        Assert.That(sawIntermediateHeading, Is.True, "Turning from right to left must expose an intermediate Y angle.");
        Assert.That(drone.BeginRound(2), Is.True);
        Assert.That(Quaternion.Angle(drone.FacingRotation, Quaternion.identity), Is.LessThan(0.01f));
        Assert.That(drone.FacingDirection, Is.EqualTo(1));
        Assert.That(Quaternion.Angle(drone.visual.localRotation, restRotation), Is.LessThan(0.01f));
        Assert.That(Vector3.Angle(drone.vision.transform.localRotation * Vector3.forward, Vector3.forward), Is.LessThan(0.01f));
        Assert.That(Quaternion.Angle(drone.Body.rotation, Quaternion.identity), Is.LessThan(0.01f));
    }

    [UnityTest]
    public IEnumerator PatrolUpdatesMovedAndReorderedPointsWhileOtherDroneStaysIndependent()
    {
        DroneController first = CreateDrone(origin, origin + Vector3.right * 3f);
        Transform movedPoint = first.patrolPoints[0];
        Transform otherPoint = CreatePoint(origin + Vector3.right * 6f);
        first.patrolPoints = new[] { movedPoint, otherPoint };
        Vector3 secondStart = origin + Vector3.up * 4f;
        DroneController second = CreateDrone(secondStart, secondStart + Vector3.right * 10f);
        Assert.That(first.BeginRound(1), Is.True);
        Assert.That(second.BeginRound(1), Is.True);
        yield return Until(() => first.Body.position.x > origin.x + 0.2f, 2f, "The first authored point should start a rightward patrol.");
        Vector3 beforeMove = first.Body.position;
        Vector3 secondBefore = second.Body.position;
        Assert.That(second.TryGetPatrolPoint(out Vector3 secondRoute), Is.True);

        movedPoint.position = origin + Vector3.left * 2f;
        Assert.That(first.TryGetPatrolPoint(out Vector3 movedRoute), Is.True);
        AssertVector(movedRoute, movedPoint.position);
        AssertVector(first.Body.position, beforeMove);
        yield return Until(() => first.Body.position.x < beforeMove.x - 0.15f, 2f,
            "Moving the actual Transform must change the live route without restarting the round.");
        Assert.That(first.FacingDirection, Is.EqualTo(-1));

        Vector3 beforeReorder = first.Body.position;
        first.patrolPoints = new[] { otherPoint, movedPoint };
        Assert.That(first.TryGetPatrolPoint(out Vector3 reorderedRoute), Is.True);
        AssertVector(reorderedRoute, otherPoint.position);
        AssertVector(first.Body.position, beforeReorder);
        yield return Until(() => first.Body.position.x > beforeReorder.x + 0.15f, 2f,
            "Reordering the Inspector array must update the current indexed destination.");
        Assert.That(first.FacingDirection, Is.EqualTo(1));
        Assert.That(first.CurrentRound, Is.EqualTo(1));
        Assert.That(second.TryGetPatrolPoint(out Vector3 unchangedRoute), Is.True);
        AssertVector(unchangedRoute, secondRoute);
        Assert.That(second.Body.position.x, Is.GreaterThan(secondBefore.x + 0.1f));
        Assert.That(second.CurrentRound, Is.EqualTo(1));
        Assert.That(second.CurrentState, Is.EqualTo(DroneState.Patrol));
    }

    [UnityTest]
    public IEnumerator PatrolCyclesAuthoredArrayOrderAndSkipsNullEntries()
    {
        DroneController drone = CreateDrone(origin, origin);
        Transform first = CreatePoint(origin + Vector3.right * 0.4f);
        Transform second = CreatePoint(origin + Vector3.right * 0.8f);
        Transform third = CreatePoint(origin + Vector3.left * 0.4f);
        drone.patrolPoints = new[] { first, null, second, third };
        Vector3[] expected = { first.position, second.position, third.position };
        Assert.That(drone.BeginRound(1), Is.True);
        Assert.That(drone.TryGetPatrolPoint(out Vector3 previous), Is.True);
        AssertVector(previous, expected[0]);
        int transitions = 0;
        float deadline = Time.realtimeSinceStartup + 5f;
        while (transitions < 3 && Time.realtimeSinceStartup < deadline)
        {
            yield return new WaitForFixedUpdate();
            Assert.That(drone.TryGetPatrolPoint(out Vector3 current), Is.True);
            if (Vector3.Distance(current, previous) < 0.001f) { continue; }
            Assert.That(Vector3.Distance(drone.Body.position, previous), Is.LessThanOrEqualTo(config.arrivalDistance + 0.05f),
                "Each destination should be reached before advancing to the next non-null array entry.");
            transitions++;
            AssertVector(current, expected[transitions % expected.Length]);
            previous = current;
        }
        Assert.That(transitions, Is.EqualTo(3), "The route must visit first, second, third, then wrap to first.");
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
        Assert.That(drone.CurrentRound, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator EmptyAndNullRoutesStaySafeAndAcceptPointsWithoutANewRound()
    {
        for (int invalid = 0; invalid < 3; invalid++)
        {
            Vector3 position = origin + Vector3.right * (invalid * 15f);
            DroneController drone = CreateDrone(position, position);
            drone.patrolPoints = invalid == 0 ? null : invalid == 1 ? new Transform[0] : new Transform[] { null, null };
            LogAssert.Expect(LogType.Warning, "无人机没有有效巡逻点，将原地观察。请在DroneController配置路线。");
            Assert.That(drone.BeginRound(1), Is.True);
            Assert.That(drone.TryGetPatrolPoint(out _), Is.False);
            for (int frame = 0; frame < 3; frame++)
            {
                yield return new WaitForFixedUpdate();
                AssertVector(drone.Body.position, position);
                Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
                Assert.That(drone.HasExploded, Is.False);
            }
            drone.patrolPoints = new[] { null, CreatePoint(position + Vector3.right) };
            yield return Until(() => drone.Body.position.x > position.x + 0.15f, 2f,
                "Adding a real point should resume patrol without rebuilding or restarting the enemy.");
            Assert.That(drone.CurrentRound, Is.EqualTo(1));
            Assert.That(drone.TryGetPatrolPoint(out Vector3 route), Is.True);
            AssertVector(route, position + Vector3.right);
            drone.gameObject.SetActive(false);
        }
    }

    private IEnumerator WaitForAlertThenChase(DroneController drone, string message)
    {
        yield return Until(() => drone.CurrentState == DroneState.Alert, 2f, message + " Alert was not observed.");
        Assert.That(drone.AlertProgress, Is.InRange(0f, 0.99999f), "Discovery must expose an unfilled Alert first.");
        yield return Until(() => drone.CurrentState == DroneState.Chase, config.alertFillSeconds + 2f, message);
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
