using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class DroneVisionAlignmentTests
{
    private readonly List<Object> owned = new List<Object>();
    private readonly Vector3 origin = new Vector3(1200f, 1200f, 25f);

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
    public IEnumerator VisionStaysLevelDuringModelLeanTurnAndRoundReset()
    {
        DroneController drone = CreateDrone();
        Quaternion rootRotation = drone.transform.rotation;
        Quaternion restRotation = drone.visual.localRotation;
        Assert.That(drone.BeginRound(1), Is.True);
        AssertAligned(drone.vision);

        bool sawPartialTurn = false;
        bool sawLean = false;
        float deadline = Time.realtimeSinceStartup + 2f;
        while (Time.realtimeSinceStartup < deadline)
        {
            // 在物理步后读取已完成的模型/视野姿态，不在两个Update调用之间采样。
            yield return new WaitForFixedUpdate();
            AssertAligned(drone.vision);
            float yaw = Quaternion.Angle(drone.FacingRotation, Quaternion.identity);
            sawPartialTurn |= yaw > 15f && yaw < 165f;
            Quaternion levelPose = drone.FacingRotation * restRotation;
            sawLean |= Quaternion.Angle(drone.visual.localRotation, levelPose) > 18f;
            Assert.That(Mathf.Abs(drone.vision.transform.right.y), Is.LessThan(0.001f),
                "The model may lean by about 20 degrees, but the vision center must stay level.");
            Assert.That(Quaternion.Angle(drone.Body.rotation, rootRotation), Is.LessThan(0.01f));
            Assert.That(drone.Body.position.z, Is.EqualTo(origin.z).Within(0.001f));
        }
        Assert.That(sawPartialTurn, Is.True, "The test must observe an actual smooth turn.");
        Assert.That(sawLean, Is.True, "The configured 20-degree flight lean must really occur; do not pass by disabling lean.");

        // 模型仍前倾时，以水平视野检查玩家与墙，不能只把显示扇形摆平而留下倾斜检测。
        DroneVision vision = drone.vision;
        vision.playerMask = Physics.DefaultRaycastLayers;
        PlayerController player = CreatePlayer(vision.transform.position + vision.transform.right * 3f);
        Physics.SyncTransforms();
        Assert.That(vision.CanSee(player), Is.True);
        GameObject wall = Own(new GameObject("Level vision wall"));
        wall.transform.SetPositionAndRotation(vision.transform.position + vision.transform.right * 1.5f,
            vision.transform.rotation);
        wall.AddComponent<BoxCollider>().size = new Vector3(0.1f, 6f, 3f);
        Physics.SyncTransforms();
        Assert.That(vision.CanSee(player), Is.False);
        vision.SendMessage("LateUpdate");
        foreach (Vector3 vertex in vision.GetComponent<MeshFilter>().sharedMesh.vertices)
        {
            Assert.That(vertex.x, Is.LessThanOrEqualTo(1.46f), "The visible horizontal fan must stop at the same wall.");
        }
        wall.SetActive(false);
        player.gameObject.SetActive(false);

        Assert.That(drone.BeginRound(2), Is.True);
        AssertAligned(drone.vision);
        Assert.That(Quaternion.Angle(drone.visual.localRotation, restRotation), Is.LessThan(0.01f));
        Assert.That(Vector3.Distance(drone.Body.position, origin), Is.LessThan(0.001f));
    }

    [Test]
    public void DetectionUsesMovedModelOriginImmediatelyBeforeLateUpdate()
    {
        GameObject source = Own(new GameObject("Model vision origin"));
        source.transform.SetPositionAndRotation(origin, Quaternion.Euler(0f, 0f, 30f));
        GameObject volume = Own(new GameObject("Model linked vision"));
        DroneVision vision = volume.AddComponent<DroneVision>();
        vision.visionOrigin = source.transform;
        vision.angleDegrees = 30f;
        vision.thickness = 2f;
        vision.Rebuild();
        PlayerController player = CreatePlayer(origin + source.transform.right * 3f);
        Physics.SyncTransforms();
        Assert.That(vision.CanSee(player), Is.True);

        // 改动模型而不等待LateUpdate；查询必须立即使用新朝向，不能仍看见背后的旧目标。
        source.transform.rotation = Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(0f, 0f, -30f);
        Assert.That(vision.CanSee(player), Is.False);
        source.transform.position += Vector3.up * 0.5f;
        PlacePlayerCenter(player, source.transform.position + source.transform.right * 3f);
        Physics.SyncTransforms();
        Assert.That(vision.FindVisiblePlayer(), Is.SameAs(player));
        vision.SendMessage("LateUpdate");
        AssertAligned(vision);

        // 同一模型锚点前方的墙同时截断检测和可见扇形。
        GameObject wall = Own(new GameObject("Model linked vision wall"));
        wall.transform.SetPositionAndRotation(source.transform.position + source.transform.right * 1.5f,
            source.transform.rotation);
        wall.AddComponent<BoxCollider>().size = new Vector3(0.1f, 6f, 3f);
        Physics.SyncTransforms();
        Assert.That(vision.CanSee(player), Is.False);
        vision.SendMessage("LateUpdate");
        foreach (Vector3 vertex in vision.GetComponent<MeshFilter>().sharedMesh.vertices)
        {
            Vector3 local = source.transform.InverseTransformPoint(vision.transform.TransformPoint(vertex));
            Assert.That(local.x, Is.LessThanOrEqualTo(1.46f));
        }
    }

    private DroneController CreateDrone()
    {
        DroneConfigSO config = Own(ScriptableObject.CreateInstance<DroneConfigSO>());
        config.patrolSpeed = 4f;
        config.chaseSpeed = 4f;
        GameObject root = Own(new GameObject("Vision alignment drone"));
        root.SetActive(false);
        root.transform.SetPositionAndRotation(origin, Quaternion.Euler(0f, 17f, 0f));
        DroneController drone = root.AddComponent<DroneController>();
        drone.config = config;
        drone.turnSmoothTime = 0.3f;
        Transform visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        visual.localRotation = Quaternion.Euler(0f, -90f, 0f);
        drone.visual = visual;
        DroneFlightVisual flight = root.AddComponent<DroneFlightVisual>();
        flight.visual = visual;
        flight.tiltDegrees = 20f;
        drone.flightVisual = flight;

        Transform nose = new GameObject("VisionOrigin").transform;
        nose.SetParent(visual, false);
        nose.localRotation = Quaternion.Inverse(visual.localRotation);
        nose.localPosition = Quaternion.Inverse(visual.localRotation) * new Vector3(0.35f, 0.12f, 0f);
        Transform holder = new GameObject("Rotated vision holder").transform;
        holder.SetParent(root.transform, false);
        holder.localRotation = Quaternion.Euler(0f, 35f, 0f);
        GameObject volume = new GameObject("Vision");
        volume.transform.SetParent(holder, false);
        drone.vision = volume.AddComponent<DroneVision>();
        drone.vision.visionOrigin = nose;
        drone.vision.playerMask = 0;
        drone.vision.angleDegrees = 50f;
        GameObject endpoint = Own(new GameObject("Alignment patrol point"));
        endpoint.transform.position = origin + Vector3.left * 30f;
        drone.patrolPoints = new[] { endpoint.transform };
        root.SetActive(true);
        return drone;
    }

    private PlayerController CreatePlayer(Vector3 center)
    {
        PlayerConfigSO config = Own(ScriptableObject.CreateInstance<PlayerConfigSO>());
        config.planeZ = center.z;
        config.groundLayers = 0;
        GameObject root = Own(new GameObject("Alignment target player"));
        root.SetActive(false);
        PlayerController player = root.AddComponent<PlayerController>();
        player.config = config;
        root.SetActive(true);
        Assert.That(player.ResetForExploration(1, center), Is.True);
        player.Body.isKinematic = true;
        PlacePlayerCenter(player, center);
        return player;
    }

    private static void PlacePlayerCenter(PlayerController player, Vector3 center)
    {
        Vector3 position = center - player.transform.TransformVector(player.GetComponent<CapsuleCollider>().center);
        player.Body.position = position;
        player.transform.position = position;
    }

    private static void AssertAligned(DroneVision vision)
    {
        Transform nose = vision.visionOrigin;
        DroneController drone = vision.GetComponentInParent<DroneController>();
        Quaternion heading = drone != null ? drone.transform.rotation * drone.FacingRotation : nose.rotation;
        Assert.That(Vector3.Distance(vision.transform.position, nose.position), Is.LessThan(0.001f));
        Assert.That(Quaternion.Angle(vision.transform.rotation, heading), Is.LessThan(0.05f),
            "Drone vision must share the horizontal turn without inheriting model lean or art correction.");
        // 插值刚体的显示位置与物理步可能不同；本断言检查同一当前姿态的实际Collider几何。
        Physics.SyncTransforms();
        Vector3 inside = nose.position + heading * Vector3.right * 3f;
        Assert.That(Vector3.Distance(vision.RangeCollider.ClosestPoint(inside), inside), Is.LessThan(0.002f));
        Vector3 behind = nose.position - heading * Vector3.right;
        Assert.That(Vector3.Distance(vision.RangeCollider.ClosestPoint(behind), behind), Is.GreaterThan(0.5f));
        Mesh view = vision.GetComponent<MeshFilter>().sharedMesh;
        Assert.That(view, Is.Not.Null);
        foreach (Vector3 vertex in view.vertices)
        {
            Vector3 local = Quaternion.Inverse(heading) * (vision.transform.TransformPoint(vertex) - nose.position);
            Assert.That(Mathf.Abs(local.z), Is.LessThan(0.002f));
            Assert.That(local.x, Is.GreaterThanOrEqualTo(-0.002f));
        }
    }

    private T Own<T>(T item) where T : Object
    {
        owned.Add(item);
        return item;
    }
}
