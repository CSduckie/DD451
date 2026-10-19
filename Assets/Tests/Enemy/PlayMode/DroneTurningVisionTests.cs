using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

// 09H：正式Prefab，自然Update/FixedUpdate转身与发现；不手工设置敌人状态或Target。
public class DroneTurningVisionTests
{
    private readonly List<Object> owned = new List<Object>();
    private readonly Vector3 origin = new Vector3(3000f, 100f, 0f);

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (Object item in owned) { if (item is GameObject go) { go.SetActive(false); } }
        foreach (Object item in owned) { if (item != null) { Object.Destroy(item); } }
        owned.Clear();
        yield return null;
    }

    [UnityTest]
    public IEnumerator MovingPlayerEnteringRightToLeftTurnKeepsNaturalDiscovery()
    {
        yield return CheckEnteringTurn(1);
    }

    [UnityTest]
    public IEnumerator MovingPlayerEnteringLeftToRightTurnKeepsNaturalDiscovery()
    {
        yield return CheckEnteringTurn(-1);
    }

    private IEnumerator CheckEnteringTurn(int initialFacing)
    {
        DroneController drone = CreateDrone(initialFacing);
        PlayerController player = CreatePlayer(origin + Vector3.up * 12f);
        Quaternion initial = drone.FacingRotation;
        float deadline = Time.realtimeSinceStartup + 2f;
        while (Quaternion.Angle(initial, drone.FacingRotation) < 10f && Time.realtimeSinceStartup < deadline)
        {
            yield return new WaitForFixedUpdate();
        }
        Assert.That(Quaternion.Angle(initial, drone.FacingRotation), Is.InRange(10f, 30f));
        Vector3 center = origin + new Vector3(initialFacing * 2.4f, 0.135f, 0f);
        PlaceCenter(player, center);
        Assert.That(drone.vision.CanSee(player), Is.True, "Player must enter the real current collider mid-turn.");
        var trace = new List<string> { "time,frame,yaw,rangeDistance,canSee,candidate,state,target,remaining" };
        bool discovered = false;
        bool lostAfterDiscovery = false;
        float discoveryTime = 0f;
        Quaternion discoveryHeading = Quaternion.identity;
        deadline = Time.realtimeSinceStartup + 1.3f;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            Vector3 bodyCenter = player.GetComponent<CapsuleCollider>().bounds.center;
            DroneVision vision = drone.vision;
            float rangeDistance = Vector3.Distance(vision.RangeCollider.ClosestPoint(bodyCenter), bodyCenter);
            bool canSee = vision.CanSee(player);
            Bounds bounds = vision.RangeCollider.bounds;
            bool candidate = false;
            foreach (Collider collider in Physics.OverlapBox(bounds.center, bounds.extents + Vector3.one * 0.01f,
                Quaternion.identity, vision.playerMask, QueryTriggerInteraction.Collide))
            {
                candidate |= collider.GetComponentInParent<PlayerController>() == player;
            }
            trace.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F5},{1},{2:F3},{3:F5},{4},{5},{6},{7},{8:F4}", Time.time, Time.frameCount,
                drone.FacingRotation.eulerAngles.y, rangeDistance, canSee, candidate, drone.CurrentState,
                drone.Target == player, drone.AlertSecondsRemaining));
            if (!discovered && drone.CurrentState == DroneState.Alert)
            {
                discovered = true;
                discoveryTime = Time.time;
                discoveryHeading = drone.FacingRotation;
            }
            if (drone.CurrentState == DroneState.Search)
            {
                lostAfterDiscovery = true;
            }
            if (discovered && drone.CurrentState == DroneState.Chase) { break; }
        }
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Docs/Verification/Enemy/TurningVision");
        Directory.CreateDirectory(folder);
        string filename = "Turn-" + initialFacing + "-" + System.DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".csv";
        File.WriteAllLines(Path.Combine(folder, filename), trace);
        TestContext.WriteLine(filename + "; discoveryYaw=" + discoveryHeading.eulerAngles.y);
        Assert.That(discovered, Is.True);
        Assert.That(lostAfterDiscovery, Is.False, "Continuing the old patrol turn must not discard the player just discovered.");
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Chase));
        Assert.That(Time.time - discoveryTime, Is.InRange(0.45f, 0.65f));
        Assert.That(drone.Target, Is.SameAs(player));
        Assert.That(drone.vision.GetComponent<MeshRenderer>().enabled, Is.False);
        Assert.That(drone.vision.RangeCollider.enabled, Is.True);
        Assert.That(drone.HasExploded, Is.False);
        for (int i = 0; i < 2; i++)
        {
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Chase));
            Assert.That(drone.Target, Is.SameAs(player));
        }
    }

    // Paste these methods inside the existing DroneTurningVisionTests class.
    // Real prefab geometry is unchanged; only the runtime turn duration is slowed for sampling.
    [UnityTest]
    public IEnumerator StationaryPlayerIsSweptIntoRightToLeftTurnAtAnIntermediateHeading()
    {
        yield return CheckStationaryPlayerSweptIntoTurn(1, false);
    }

    [UnityTest]
    public IEnumerator StationaryPlayerIsSweptIntoLeftToRightTurnAtAnIntermediateHeading()
    {
        yield return CheckStationaryPlayerSweptIntoTurn(-1, false);
    }

    [UnityTest]
    public IEnumerator StationaryPlayerIsSweptIntoRightToLeftTurnNearItsEnd()
    {
        yield return CheckStationaryPlayerSweptIntoTurn(1, true);
    }

    [UnityTest]
    public IEnumerator StationaryPlayerIsSweptIntoLeftToRightTurnNearItsEnd()
    {
        yield return CheckStationaryPlayerSweptIntoTurn(-1, true);
    }

    private IEnumerator CheckStationaryPlayerSweptIntoTurn(int initialFacing, bool nearEnd)
    {
        DroneController drone = CreateDrone(initialFacing);
        drone.turnSmoothTime = 0.6f;
        // VisionOrigin is about 0.326m forward and 0.135m high on the real prefab.
        // These fixed XY positions enter its existing 2m-thick, 90-degree volume at different yaw angles.
        // The nearer case stays clear of the player's real capsule; neither case demands visibility at 90 degrees.
        Vector3 center = origin + new Vector3(-initialFacing * (nearEnd ? 3f : 1.1f), nearEnd ? 1f : 0.135f, 0f);
        PlayerController player = CreatePlayer(center);
        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        DroneVision vision = drone.vision;
        Quaternion initialHeading = drone.FacingRotation;
        Vector3 stationaryCenter = capsule.bounds.center;
        Vector3 stationaryDrone = drone.Body.position;
        int health = player.health.CurrentHealth;
        var trace = new List<string> { "phase,time,frame,yaw,turnProgress,rangeDistance,contains,state,target,remaining,playerX,playerY,playerZ" };
        System.Action<string> record = phase =>
        {
            Vector3 currentCenter = capsule.bounds.center;
            // Passive sampling: do not call perception APIs that may RefreshPose/SyncTransforms.
            float rangeDistance = Vector3.Distance(vision.RangeCollider.ClosestPoint(currentCenter), currentCenter);
            trace.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1:F5},{2},{3:F3},{4:F3},{5:F5},{6},{7},{8},{9:F4},{10:F4},{11:F4},{12:F4}",
                phase, Time.time, Time.frameCount, drone.FacingRotation.eulerAngles.y,
                Quaternion.Angle(initialHeading, drone.FacingRotation), rangeDistance, rangeDistance <= 0.001f,
                drone.CurrentState, drone.Target == player, drone.AlertSecondsRemaining,
                currentCenter.x, currentCenter.y, currentCenter.z));
        };
        try
        {
            Assert.That(vision.playerMask.value, Is.Not.Zero, "Natural perception must stay enabled.");
            Assert.That(vision.RangeCollider.enabled && vision.RangeCollider.convex && vision.RangeCollider.isTrigger, Is.True);
            Assert.That(stationaryCenter.z, Is.EqualTo(0f).Within(0.0001f), "The real player must remain in the world XY plane.");
            Assert.That(vision.CanSee(player), Is.False);
            Assert.That(Vector3.Distance(vision.RangeCollider.ClosestPoint(stationaryCenter), stationaryCenter), Is.GreaterThan(0.01f),
                "The stationary player must start outside the actual collider, not already visible.");
            float bodyClearance = Vector3.Distance(capsule.ClosestPoint(drone.Body.position), drone.Body.position)
                - drone.GetComponent<SphereCollider>().radius;
            Assert.That(bodyClearance, Is.GreaterThan(0.35f), "This is a vision test, with a real physical gap beyond the drone body.");
            record("initial");

            bool enteredRange = false;
            bool discovered = false;
            float firstRangeYaw = float.NaN;
            float firstRangeProgress = float.NaN;
            Quaternion discoveryHeading = Quaternion.identity;
            float discoveryStarted = 0f;
            float firstRemaining = 0f;
            float deadline = Time.realtimeSinceStartup + 4f;
            while (!discovered && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                record("sweep");
                Assert.That(drone.CurrentState, Is.Not.EqualTo(DroneState.Search),
                    "A short Alert followed by Search is still a failure even if the test missed the Alert sample.");
                Assert.That(Vector3.Distance(capsule.bounds.center, stationaryCenter), Is.LessThan(0.001f));
                Assert.That(Vector3.Distance(drone.Body.position, stationaryDrone), Is.LessThan(0.002f));
                Vector3 currentCenter = capsule.bounds.center;
                bool contained = (vision.RangeCollider.ClosestPoint(currentCenter) - currentCenter).sqrMagnitude <= 0.000001f;
                if (!enteredRange && contained)
                {
                    enteredRange = true;
                    firstRangeYaw = drone.FacingRotation.eulerAngles.y;
                    firstRangeProgress = Quaternion.Angle(initialHeading, drone.FacingRotation);
                    // The subsequent natural Alert/Target is the evidence of production perception.
                }
                if (drone.CurrentState == DroneState.Alert)
                {
                    Assert.That(enteredRange, Is.True, "Natural discovery must be supported by real range containment.");
                    discovered = true;
                    discoveryHeading = drone.FacingRotation;
                    discoveryStarted = Time.time;
                    firstRemaining = drone.AlertSecondsRemaining;
                }
                else
                {
                    Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol), "A stationary sweep must naturally pass through discovery Alert before Chase.");
                }
            }
            TestContext.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Stationary sweep initialFacing={0}, nearEnd={1}, firstRangeYaw={2:F3}, turnProgress={3:F3}, center={4}",
                initialFacing, nearEnd, firstRangeYaw, firstRangeProgress, stationaryCenter));
            Assert.That(enteredRange && discovered, Is.True, "The rotating real volume must eventually discover this stationary player.");
            Assert.That(firstRangeProgress, nearEnd ? Is.InRange(150f, 179f) : Is.InRange(100f, 145f),
                "The two placements must exercise distinct, valid portions of the natural turn.");
            Assert.That(drone.Target, Is.SameAs(player));
            Assert.That(drone.IsCoverAlert, Is.False);
            Assert.That(firstRemaining, Is.GreaterThan(0.35f).And.LessThanOrEqualTo(0.5f));

            float previousRemaining = firstRemaining;
            deadline = Time.realtimeSinceStartup + 2f;
            while (drone.CurrentState == DroneState.Alert && Time.realtimeSinceStartup < deadline)
            {
                Assert.That(Quaternion.Angle(discoveryHeading, drone.FacingRotation), Is.LessThan(0.05f),
                    "Discovery must hold the heading that actually saw the player instead of continuing the old patrol turn.");
                Assert.That(drone.Target, Is.SameAs(player));
                Assert.That(drone.IsCoverAlert, Is.False);
                Assert.That(drone.AlertSecondsRemaining, Is.InRange(0f, previousRemaining + 0.0001f));
                previousRemaining = drone.AlertSecondsRemaining;
                Assert.That((vision.RangeCollider.ClosestPoint(capsule.bounds.center) - capsule.bounds.center).sqrMagnitude, Is.LessThanOrEqualTo(0.000001f));
                Assert.That(Vector3.Distance(capsule.bounds.center, stationaryCenter), Is.LessThan(0.001f));
                Assert.That(Vector3.Distance(drone.Body.position, stationaryDrone), Is.LessThan(0.002f));
                Assert.That(drone.HasExploded, Is.False);
                Assert.That(player.health.CurrentHealth, Is.EqualTo(health));
                yield return null;
                record("discovery");
                Assert.That(drone.CurrentState, Is.Not.EqualTo(DroneState.Search), "No discovery-to-search interruption is allowed for this stationary visible target.");
            }
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Chase));
            Assert.That(Time.time - discoveryStarted, Is.GreaterThanOrEqualTo(firstRemaining - Time.deltaTime - 0.02f),
                "The configured half-second discovery countdown must finish before Chase.");
            Assert.That(drone.Target, Is.SameAs(player));
            for (int i = 0; i < 2; i++)
            {
                yield return new WaitForFixedUpdate();
                record("chase-fixed");
                Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Chase));
                Assert.That(drone.Target, Is.SameAs(player));
                yield return null;
                record("chase-update");
                Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Chase), "Resuming the smooth turn must not immediately drop the discovered target.");
                Assert.That(drone.Target, Is.SameAs(player));
                Assert.That((vision.RangeCollider.ClosestPoint(capsule.bounds.center) - capsule.bounds.center).sqrMagnitude, Is.LessThanOrEqualTo(0.000001f));
                Assert.That(vision.RangeCollider.enabled, Is.True);
                Assert.That(vision.GetComponent<MeshRenderer>().enabled, Is.False);
                Assert.That(Vector3.Distance(capsule.bounds.center, stationaryCenter), Is.LessThan(0.001f));
                Assert.That(drone.HasExploded, Is.False);
                Assert.That(player.health.CurrentHealth, Is.EqualTo(health));
            }
        }
        finally
        {
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Docs/Verification/Enemy/TurningVision");
            Directory.CreateDirectory(folder);
            string filename = "StationaryTurn-" + initialFacing + (nearEnd ? "-End-" : "-Middle-")
                + System.DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".csv";
            File.WriteAllLines(Path.Combine(folder, filename), trace);
            TestContext.WriteLine(filename);
        }
    }
    [UnityTest]
    public IEnumerator TurningRangeDoesNotSeeThroughWallOrCrouchingCover()
    {
        for (int mode = 0; mode < 2; mode++)
        {
            DroneController drone = CreateDrone(1);
            PlayerController player = CreatePlayer(origin + new Vector3(-3f, 0.135f, 0f));
            CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
            GameObject blocker = Own(new GameObject(mode == 0 ? "Turning sight wall" : "Turning sight cover"));
            BoxCollider box = blocker.AddComponent<BoxCollider>();
            if (mode == 0)
            {
                blocker.transform.position = origin + Vector3.left * 1.5f;
                box.size = new Vector3(0.15f, 8f, 12f);
            }
            else
            {
                player.SetCrouching(true);
                player.SetBackpackOpen(true); // 固定真实02C蹲姿；本项隔离感知，非原生C键验收。
                blocker.transform.position = capsule.bounds.center;
                box.size = new Vector3(2f, 3f, 2f);
                box.isTrigger = true;
                blocker.AddComponent<CoverArea>();
                Assert.That(capsule.height, Is.EqualTo(1.3f).Within(0.001f));
                Assert.That(capsule.center.y, Is.EqualTo(0.65f).Within(0.001f));
            }
            Physics.SyncTransforms();
            if (mode == 1) { Assert.That(CoverArea.IsHidden(player), Is.True); }
            bool sweptInside = false;
            Quaternion initial = drone.FacingRotation;
            float limit = Time.realtimeSinceStartup + 3f;
            while (Quaternion.Angle(initial, drone.FacingRotation) < 178f && Time.realtimeSinceStartup < limit)
            {
                yield return null;
                Vector3 center = capsule.bounds.center;
                sweptInside |= (drone.vision.RangeCollider.ClosestPoint(center) - center).sqrMagnitude < 0.000001f;
                Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
                Assert.That(drone.Target, Is.Null);
                Assert.That(drone.HasExploded, Is.False);
            }
            Assert.That(sweptInside, Is.True, "Blocked target must actually be swept into the real collider.");
            Assert.That(drone.vision.CanSee(player), Is.False);
            blocker.SetActive(false);
            if (mode == 1) { player.SetBackpackOpen(false); player.SetCrouching(false); }
            Physics.SyncTransforms();
            limit = Time.realtimeSinceStartup + 0.25f;
            while (drone.CurrentState == DroneState.Patrol && Time.realtimeSinceStartup < limit) { yield return null; }
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
            Assert.That(drone.Target, Is.SameAs(player));
            drone.transform.parent.gameObject.SetActive(false);
            player.gameObject.SetActive(false);
        }
    }

    [UnityTest]
    public IEnumerator MidTurnDiscoveryStillRespondsToRealLossAndCoverTimeout()
    {
        for (int mode = 0; mode < 3; mode++)
        {
            DroneController drone = CreateDrone(1);
            PlayerController player = CreatePlayer(origin + Vector3.up * 12f);
            float limit = Time.realtimeSinceStartup + 2f;
            while (Quaternion.Angle(Quaternion.identity, drone.FacingRotation) < 10f && Time.realtimeSinceStartup < limit)
            { yield return null; }
            PlaceCenter(player, origin + new Vector3(2.4f, 0.135f, 0f));
            limit = Time.realtimeSinceStartup + 0.2f;
            while (drone.CurrentState == DroneState.Patrol && Time.realtimeSinceStartup < limit) { yield return null; }
            Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Alert));
            Quaternion seenHeading = drone.FacingRotation;
            GameObject blocker = null;
            if (mode == 0) { PlaceCenter(player, origin + Vector3.up * 12f); }
            else
            {
                blocker = Own(new GameObject(mode == 1 ? "New wall after turning discovery" : "New cover after turning discovery"));
                BoxCollider box = blocker.AddComponent<BoxCollider>();
                if (mode == 1)
                {
                    blocker.transform.position = origin + Vector3.right * 1.2f;
                    box.size = new Vector3(0.15f, 8f, 12f);
                }
                else
                {
                    blocker.transform.position = player.GetComponent<CapsuleCollider>().bounds.center;
                    box.size = new Vector3(2f, 3f, 2f);
                    box.isTrigger = true;
                    blocker.AddComponent<CoverArea>();
                    player.SetCrouching(true);
                    player.SetBackpackOpen(true);
                }
                Physics.SyncTransforms();
            }
            yield return null;
            yield return null;
            Assert.That(drone.Target, Is.Null);
            if (mode < 2) { Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Search)); }
            else
            {
                Assert.That(CoverArea.IsHidden(player), Is.True);
                Assert.That(drone.IsCoverAlert, Is.True);
                Assert.That(drone.AlertSecondsRemaining, Is.GreaterThan(1.7f));
                float started = Time.time;
                limit = Time.realtimeSinceStartup + 3f;
                while (drone.CurrentState == DroneState.Alert && Time.realtimeSinceStartup < limit)
                {
                    Assert.That(Quaternion.Angle(seenHeading, drone.FacingRotation), Is.LessThan(0.05f));
                    yield return null;
                }
                Assert.That(Time.time - started, Is.GreaterThan(1.7f));
                Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
            }
            drone.transform.parent.gameObject.SetActive(false);
            player.gameObject.SetActive(false);
            if (blocker != null) { blocker.SetActive(false); }
        }
    }

    [UnityTest]
    public IEnumerator QueuedStandingAndCrouchingMovementEntersBothNaturalTurns()
    {
        InputSettings originalSettings = InputSystem.settings;
        bool originalBackground = Application.runInBackground;
        InputSettings settings = null;
        Keyboard keyboard = null;
        var disabledKeyboards = new List<InputDevice>();
        try
        {
            settings = Object.Instantiate(originalSettings);
            settings.hideFlags = HideFlags.HideAndDontSave;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings = settings;
            Application.runInBackground = true;
            foreach (InputDevice device in new List<InputDevice>(InputSystem.devices))
            {
                if (!(device is Keyboard) || !device.enabled) { continue; }
                disabledKeyboards.Add(device);
                InputSystem.DisableDevice(device);
            }
            keyboard = InputSystem.AddDevice<Keyboard>();

            for (int variant = 0; variant < 4; variant++)
            {
                bool crouching = variant >= 2;
                int initialFacing = variant % 2 == 0 ? 1 : -1;
                string label = "crouching=" + crouching + "; initialFacing=" + initialFacing;
                DroneController drone = CreateDrone(initialFacing);
                drone.turnSmoothTime = 1f; // Runtime fixture only; keep the authored .5-second discovery delay.
                Transform[] turningRoute = drone.patrolPoints;
                drone.patrolPoints = new[] { drone.transform }; // Prepare the grounded pose before starting the turn.
                PlayerController player = CreatePlayer(origin + Vector3.up * 12f);
                CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
                float standingHeight = capsule.height;
                Vector3 standingCenter = capsule.center;
                float expectedHeight = crouching
                    ? Mathf.Max(capsule.radius * 2f, standingHeight * player.config.crouchVisualHeight)
                    : standingHeight;
                float expectedCenterY = standingCenter.y + (expectedHeight - standingHeight) * 0.5f;
                Assert.That(standingHeight, Is.EqualTo(2f).Within(0.001f));
                Assert.That(standingCenter.y, Is.EqualTo(1f).Within(0.001f));
                Assert.That(capsule.radius, Is.EqualTo(0.4f).Within(0.001f));
                Assert.That(player.transform.lossyScale, Is.EqualTo(Vector3.one));
                Assert.That(player.config.crouchVisualHeight, Is.EqualTo(0.65f).Within(0.001f));

                // The player walks OUTWARD across the upper fan edge, never towards the drone's physical body.
                // Both postures use the same desired current body center; floor height follows the real capsule.
                Vector3 desiredCenter = new Vector3(origin.x + initialFacing * 0.78f,
                    drone.vision.visionOrigin.position.y + 0.55f, origin.z);
                float floorTop = desiredCenter.y - expectedHeight * 0.5f;
                GameObject floor = Own(new GameObject("Turning movement floor " + variant));
                floor.transform.position = new Vector3(origin.x + initialFacing * 6.55f, floorTop - 0.25f, origin.z);
                floor.AddComponent<BoxCollider>().size = new Vector3(12f, 0.5f, 4f);
                Assert.That((player.config.groundLayers.value & (1 << floor.layer)) != 0, Is.True);
                PlaceCenter(player, new Vector3(desiredCenter.x, floorTop + standingHeight * 0.5f + 0.025f, desiredCenter.z));
                player.Body.isKinematic = false; // Existing PlaceCenter is a static fixture helper; restore real movement.
                player.Body.useGravity = true;
                Physics.SyncTransforms();
                QueueMovingKeys(keyboard, crouching, 0);
                yield return WaitForMovingCondition(() => player.IsGrounded && player.IsCrouching == crouching
                    && Mathf.Abs(player.Body.linearVelocity.y) < 0.1f, 2f, "Player must settle naturally: " + label);
                Assert.That(capsule.height, Is.EqualTo(expectedHeight).Within(0.001f));
                Assert.That(capsule.center.y, Is.EqualTo(expectedCenterY).Within(0.001f));
                Assert.That(capsule.bounds.min.y, Is.EqualTo(floorTop).Within(0.03f));
                Assert.That(CoverArea.IsHidden(player), Is.False);
                Assert.That(MovingRangeDistance(drone, player), Is.GreaterThan(0.03f), "Start outside the actual cone: " + label);
                Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
                AssertMovingBodyClear(drone, player);

                Quaternion initial = drone.FacingRotation;
                Quaternion finalHeading = Quaternion.Euler(0f, initialFacing > 0 ? 180f : 0f, 0f);
                drone.patrolPoints = turningRoute;
                yield return WaitForMovingCondition(() => Quaternion.Angle(initial, drone.FacingRotation) >= 10f,
                    2f, "Natural patrol FixedUpdate must initiate the turn: " + label);
                Assert.That(Quaternion.Angle(initial, drone.FacingRotation), Is.LessThan(35f));
                Assert.That(MovingRangeDistance(drone, player), Is.GreaterThan(0.01f));
                Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
                float startX = player.Body.position.x;
                int health = player.health.CurrentHealth;
                QueueMovingKeys(keyboard, crouching, initialFacing); // A/D, or C+A/C+D, through the real reader.
                bool sawMovingOutside = false;
                bool entered = false;
                float deadline = Time.realtimeSinceStartup + 2f;
                while (!entered && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    AssertMovingBodyClear(drone, player);
                    Assert.That(player.IsCrouching, Is.EqualTo(crouching));
                    float moved = (player.Body.position.x - startX) * initialFacing;
                    entered = MovingRangeDistance(drone, player) <= 0.001f;
                    if (!entered)
                    {
                        sawMovingOutside |= moved > 0.01f;
                        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
                    }
                    else
                    {
                        Assert.That(moved, Is.GreaterThan(0.05f), "Entry must follow real walking: " + label);
                        Assert.That(Quaternion.Angle(initial, drone.FacingRotation), Is.GreaterThan(10f));
                        Assert.That(Quaternion.Angle(finalHeading, drone.FacingRotation), Is.GreaterThan(15f),
                            "The turn must still be unfinished at physical entry: " + label);
                    }
                }
                Assert.That(entered && sawMovingOutside, Is.True, "Observe outside -> walking -> real cone entry: " + label);
                QueueMovingKeys(keyboard, crouching, 0); // Release direction; keep C held for the crouching variants.
                yield return WaitForMovingCondition(() => drone.CurrentState == DroneState.Alert,
                    0.25f, "Natural Update must discover the entering player: " + label);
                Assert.That(drone.IsCoverAlert, Is.False);
                Assert.That(drone.Target, Is.SameAs(player));
                Assert.That(drone.AlertSecondsRemaining, Is.GreaterThan(0.35f));
                float alertObservedAt = Time.time;
                float remainingAtObservation = drone.AlertSecondsRemaining;
                deadline = Time.realtimeSinceStartup + drone.config.discoveryAlertSeconds + 1f;
                while (drone.CurrentState == DroneState.Alert && Time.realtimeSinceStartup < deadline)
                {
                    AssertMovingBodyClear(drone, player);
                    AssertMovingViewPose(drone);
                    Assert.That(drone.Target, Is.SameAs(player));
                    Assert.That(drone.vision.GetComponent<MeshRenderer>().enabled, Is.True);
                    Assert.That(MovingRangeDistance(drone, player), Is.LessThanOrEqualTo(0.001f));
                    yield return null;
                }
                Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Chase), "Do not drop into Search during the old turn: " + label);
                Assert.That(Time.time - alertObservedAt,
                    Is.InRange(remainingAtObservation - 0.06f, remainingAtObservation + 0.15f));
                yield return WaitForMovingCondition(() => Mathf.Abs(player.Body.linearVelocity.x) < 0.05f,
                    1f, "Releasing the movement keys must naturally stop the player.");
                AssertMovingBodyClear(drone, player);
                AssertMovingViewPose(drone);
                Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Chase));
                Assert.That(drone.Target, Is.SameAs(player));
                Assert.That(drone.vision.GetComponent<MeshRenderer>().enabled, Is.False);
                Assert.That(drone.vision.CanSee(player), Is.True); // Postcondition only; never drives discovery above.
                Assert.That(capsule.height, Is.EqualTo(expectedHeight).Within(0.001f));
                Assert.That(capsule.center.y, Is.EqualTo(expectedCenterY).Within(0.001f));
                Assert.That(player.health.CurrentHealth, Is.EqualTo(health));
                TestContext.WriteLine(label + "; stopped center=" + capsule.bounds.center);
                drone.gameObject.SetActive(false);
                player.gameObject.SetActive(false);
                floor.SetActive(false);
                QueueMovingKeys(keyboard, false, 0);
                yield return null;
            }
        }
        finally
        {
            if (keyboard != null && keyboard.added) { InputSystem.RemoveDevice(keyboard); }
            InputSystem.settings = originalSettings;
            Application.runInBackground = originalBackground;
            if (settings != null) { Object.Destroy(settings); }
            foreach (InputDevice device in disabledKeyboards)
            {
                if (device.added && !device.enabled) { InputSystem.EnableDevice(device); }
            }
        }
    }

    private static void QueueMovingKeys(Keyboard keyboard, bool crouching, int direction)
    {
        if (direction == 0)
        {
            InputSystem.QueueStateEvent(keyboard, crouching ? new KeyboardState(Key.C) : new KeyboardState());
            return;
        }
        Key move = direction > 0 ? Key.D : Key.A;
        InputSystem.QueueStateEvent(keyboard, crouching ? new KeyboardState(Key.C, move) : new KeyboardState(move));
    }

    private static IEnumerator WaitForMovingCondition(System.Func<bool> condition, float seconds, string message)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) { yield return null; }
        Assert.That(condition(), Is.True, message);
    }

    private static float MovingRangeDistance(DroneController drone, PlayerController player)
    {
        Vector3 center = player.GetComponent<CapsuleCollider>().bounds.center;
        return Vector3.Distance(drone.vision.RangeCollider.ClosestPoint(center), center);
    }

    private static void AssertMovingBodyClear(DroneController drone, PlayerController player)
    {
        SphereCollider sphere = drone.GetComponent<SphereCollider>();
        Vector3 center = sphere.transform.TransformPoint(sphere.center);
        Vector3 scale = sphere.transform.lossyScale;
        float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        Assert.That(radius, Is.EqualTo(0.25f).Within(0.001f));
        Assert.That(Vector3.Distance(player.GetComponent<CapsuleCollider>().ClosestPoint(center), center),
            Is.GreaterThan(radius + 0.1f), "Vision fixture must maintain real body clearance.");
        Assert.That(sphere.enabled && !sphere.isTrigger, Is.True);
        Assert.That(drone.HasExploded, Is.False);
    }

    private static void AssertMovingViewPose(DroneController drone)
    {
        DroneVision vision = drone.vision;
        Quaternion expected = drone.transform.rotation * drone.FacingRotation * Quaternion.Euler(0f, 0f, vision.directionDegrees);
        Assert.That(Quaternion.Angle(vision.transform.rotation, expected), Is.LessThan(0.1f));
        Assert.That(vision.enabled && vision.gameObject.activeInHierarchy && vision.RangeCollider.enabled, Is.True);
        Assert.That(vision.RangeCollider.isTrigger, Is.True);
        Assert.That(vision.RangeCollider.sharedMesh, Is.Not.Null);
        Assert.That(vision.GetComponent<MeshFilter>().sharedMesh, Is.Not.Null);
        Assert.That(Mathf.Abs(vision.transform.right.y), Is.LessThan(0.001f));
    }


    private DroneController CreateDrone(int initialFacing)
    {
        GameObject parent = Own(new GameObject("Turning vision test fixture"));
        parent.SetActive(false);
        GameObject root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Drone/Drone.prefab"), parent.transform);
        root.transform.position = origin;
        DroneController drone = root.GetComponent<DroneController>();
        drone.config = Own(Object.Instantiate(drone.config));
        // 巡逻速度0仍经真实FixedUpdate选择转向；排除位置变化，保留0.5秒发现。
        drone.config.patrolSpeed = 0f;
        drone.config.chaseSpeed = 0f;
        drone.turnSmoothTime = 0.6f; // 只放慢夹具采样；正式Prefab仍保留0.18，首轮原值轨迹另存。
        drone.initialFacing = initialFacing;
        GameObject endpoint = new GameObject("Turning patrol destination");
        endpoint.transform.SetParent(parent.transform, false);
        endpoint.transform.position = origin + Vector3.left * initialFacing * 10f;
        drone.patrolPoints = new[] { endpoint.transform };
        parent.SetActive(true);
        Assert.That(drone.BeginRound(1), Is.True);
        Assert.That(drone.GetComponent<SphereCollider>().radius, Is.EqualTo(0.25f));
        Assert.That(drone.config.discoveryAlertSeconds, Is.EqualTo(0.5f));
        return drone;
    }

    private PlayerController CreatePlayer(Vector3 center)
    {
        GameObject root = Own(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player.prefab")));
        PlayerController player = root.GetComponent<PlayerController>();
        player.config = Own(Object.Instantiate(player.config));
        player.config.planeZ = origin.z;
        Assert.That(player.ResetForExploration(1, center - Vector3.up), Is.True);
        player.Body.isKinematic = true;
        PlaceCenter(player, center);
        return player;
    }

    private static void PlaceCenter(PlayerController player, Vector3 center)
    {
        Vector3 position = center - player.transform.TransformVector(player.GetComponent<CapsuleCollider>().center);
        Assert.That(player.RelocateWithinRun(position), Is.True);
        player.Body.isKinematic = true;
        Physics.SyncTransforms();
    }

    private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
}

