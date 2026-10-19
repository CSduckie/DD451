using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class DroneVisionGeometryTests
{
    private readonly List<Object> created = new List<Object>();
    private readonly Vector3 origin = new Vector3(10000f, 10000f, 0f);

    [TearDown]
    public void TearDown()
    {
        for (int i = created.Count - 1; i >= 0; i--) { if (created[i] != null) { Object.DestroyImmediate(created[i]); } }
        created.Clear();
    }

    [Test]
    public void ColliderControlsAngleRadiusAndThicknessAndStaysLegal()
    {
        DroneVision vision = CreateVision();
        vision.segments = 100;
        vision.Rebuild();
        Physics.SyncTransforms();
        Assert.That(vision.RangeCollider.convex, Is.True);
        Assert.That(vision.RangeCollider.isTrigger, Is.True);
        Assert.That(vision.RangeCollider.sharedMesh.triangles.Length / 3, Is.LessThanOrEqualTo(255));
        AssertInside(vision, new Vector3(3f, 0f, 0.45f), true);
        AssertInside(vision, new Vector3(3f, 0f, 0.6f), false);
        AssertInside(vision, new Vector3(7f, 0f, 0f), false);
        AssertInside(vision, new Vector3(2f, 3f, 0f), false);
        AssertInside(vision, new Vector3(-1f, 0f, 0f), false);
    }

    [Test]
    public void FacingMirrorsHorizontalDirectionAndKeepsUpwardPitch()
    {
        DroneVision vision = CreateVision();
        vision.directionDegrees = 20f;
        vision.SetFacing(1);
        Vector3 right = vision.transform.right;
        vision.SetFacing(-1);
        Vector3 left = vision.transform.right;
        Assert.That(left.x, Is.EqualTo(-right.x).Within(0.0001f));
        Assert.That(left.y, Is.EqualTo(right.y).Within(0.0001f));
    }

    [Test]
    public void MainBodyCenterDecidesRangeAndBackpackDoesNotHidePlayer()
    {
        DroneVision vision = CreateVision();
        PlayerController player = CreatePlayer(origin + Vector3.right * 3f);
        player.SetBackpackOpen(true);
        Physics.SyncTransforms();
        Assert.That(player.CanAct, Is.False);
        Assert.That(vision.CanSee(player), Is.True);
        Assert.That(vision.FindVisiblePlayer(), Is.SameAs(player));

        GameObject extra = new GameObject("ExtraPlayerCollider");
        extra.transform.SetParent(player.transform);
        extra.transform.position = origin + Vector3.right * 3f;
        extra.AddComponent<BoxCollider>();
        player.transform.position = origin + Vector3.right * 8f;
        extra.transform.position = origin + Vector3.right * 3f;
        Physics.SyncTransforms();
        Assert.That(vision.FindVisiblePlayer(), Is.Null, "An extra collider in range must not replace the main body test.");
    }

    [Test]
    public void SolidWallBlocksPlayerAndClipsVisibleMesh()
    {
        DroneVision vision = CreateVision();
        PlayerController player = CreatePlayer(origin + Vector3.right * 4f);
        BoxCollider wall = CreateWall(origin + Vector3.right * 2f, new Vector3(0.1f, 10f, 2f));
        Physics.SyncTransforms();
        vision.Rebuild();
        Assert.That(vision.CanSee(player), Is.False);
        foreach (Vector3 vertex in vision.GetComponent<MeshFilter>().sharedMesh.vertices)
        {
            Assert.That(vision.transform.TransformPoint(vertex).x, Is.LessThanOrEqualTo(wall.bounds.min.x + 0.002f));
        }
        wall.gameObject.layer = 2;
        Physics.SyncTransforms();
        Assert.That(vision.CanSee(player), Is.True, "Ignore Raycast decorations must not occlude vision.");
    }

    [Test]
    public void OriginInsideWallCannotDetectThroughIt()
    {
        DroneVision vision = CreateVision();
        PlayerController player = CreatePlayer(origin + Vector3.right * 3f);
        CreateWall(origin, Vector3.one);
        Physics.SyncTransforms();
        vision.Rebuild();
        Assert.That(vision.CanSee(player), Is.False);
        Assert.That(vision.GetComponent<MeshFilter>().sharedMesh.bounds.size.sqrMagnitude, Is.LessThan(0.0001f));
    }

    [Test]
    public void DifferentDepthNarrowWallClipsBothDetectionAndDisplayAcrossFullThickness()
    {
        DroneVision vision = CreateVision();
        vision.thickness = 2f;
        PlayerController player = CreatePlayer(origin + new Vector3(4f, 0f, 0.8f));
        BoxCollider wall = CreateWall(origin + new Vector3(2f, 0f, 0.4f), new Vector3(0.1f, 10f, 0.1f));
        Physics.SyncTransforms();
        vision.Rebuild();
        Assert.That(vision.CanSee(player), Is.False);
        foreach (Vector3 vertex in vision.GetComponent<MeshFilter>().sharedMesh.vertices)
        {
            Assert.That(vision.transform.TransformPoint(vertex).x, Is.LessThanOrEqualTo(wall.bounds.min.x + 0.002f));
        }

        // 超出整份检测厚度后，窄墙不再挡显示，也不挡不同Z的合法候选。
        wall.transform.position = origin + new Vector3(2f, 0f, 1.2f);
        Physics.SyncTransforms();
        vision.Rebuild();
        Assert.That(vision.CanSee(player), Is.True);
        Assert.That(vision.GetComponent<MeshFilter>().sharedMesh.bounds.max.x, Is.GreaterThan(5f));
    }

    [Test]
    public void WallAtAnotherDepthOfOriginBlocksTheWholeVisibleFan()
    {
        DroneVision vision = CreateVision();
        vision.thickness = 2f;
        PlayerController player = CreatePlayer(origin + new Vector3(4f, 0f, 0.8f));
        CreateWall(origin + Vector3.forward * 0.5f, new Vector3(0.1f, 0.1f, 0.1f));
        Physics.SyncTransforms();
        vision.Rebuild();
        Assert.That(vision.CanSee(player), Is.False);
        Assert.That(vision.GetComponent<MeshFilter>().sharedMesh.bounds.size.sqrMagnitude, Is.LessThan(0.0001f));
    }

    [Test]
    public void DisableClearsDetectionAndReenableRebuildsTransientMeshes()
    {
        DroneVision vision = CreateVision();
        vision.enabled = false;
        Assert.That(vision.RangeCollider.enabled, Is.False);
        Assert.That(vision.GetComponent<MeshRenderer>().enabled, Is.False);
        vision.enabled = true;
        Physics.SyncTransforms();
        Assert.That(vision.RangeCollider.sharedMesh, Is.Not.Null);
        Assert.That(vision.RangeCollider.enabled, Is.True);
        Assert.That(vision.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0));
    }

    [Test]
    public void ManualColliderToggleSynchronizesDetectionAndDisplayWithoutRebuilding()
    {
        DroneVision vision = CreateVision();
        PlayerController player = CreatePlayer(origin + Vector3.right * 3f);
        Mesh range = vision.RangeCollider.sharedMesh;
        Mesh view = vision.GetComponent<MeshFilter>().sharedMesh;
        MeshRenderer renderer = vision.GetComponent<MeshRenderer>();
        Physics.SyncTransforms();
        Assert.That(vision.CanSee(player), Is.True);

        vision.RangeCollider.enabled = false;
        vision.SendMessage("LateUpdate");
        Assert.That(vision.RangeCollider.enabled, Is.False, "Refreshing must preserve the manually disabled collider.");
        Assert.That(renderer.enabled, Is.False);
        Assert.That(vision.CanSee(player), Is.False);
        Assert.That(vision.FindVisiblePlayer(), Is.Null);

        vision.RangeCollider.enabled = true;
        Physics.SyncTransforms();
        vision.SendMessage("LateUpdate");
        Assert.That(renderer.enabled, Is.True);
        Assert.That(vision.CanSee(player), Is.True);
        Assert.That(vision.FindVisiblePlayer(), Is.SameAs(player));
        Assert.That(vision.RangeCollider.sharedMesh, Is.SameAs(range));
        Assert.That(vision.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(view));
    }

    private DroneVision CreateVision()
    {
        GameObject go = new GameObject("VisionTest");
        created.Add(go);
        go.transform.position = origin;
        DroneVision vision = go.AddComponent<DroneVision>();
        vision.radius = 6f;
        vision.angleDegrees = 70f;
        vision.thickness = 1f;
        vision.Rebuild();
        Physics.SyncTransforms();
        return vision;
    }

    private PlayerController CreatePlayer(Vector3 position)
    {
        PlayerConfigSO config = ScriptableObject.CreateInstance<PlayerConfigSO>();
        created.Add(config);
        config.planeZ = position.z;
        GameObject go = new GameObject("VisionPlayer");
        created.Add(go);
        go.SetActive(false);
        PlayerController player = go.AddComponent<PlayerController>();
        player.config = config;
        go.SetActive(true);
        Assert.That(player.ResetForExploration(1, position), Is.True);
        player.Body.useGravity = false;
        return player;
    }

    private BoxCollider CreateWall(Vector3 position, Vector3 size)
    {
        GameObject go = new GameObject("VisionWall");
        created.Add(go);
        go.transform.position = position;
        BoxCollider wall = go.AddComponent<BoxCollider>();
        wall.size = size;
        return wall;
    }

    private void AssertInside(DroneVision vision, Vector3 offset, bool expected)
    {
        Vector3 point = origin + offset;
        bool inside = (vision.RangeCollider.ClosestPoint(point) - point).sqrMagnitude < 0.000001f;
        Assert.That(inside, Is.EqualTo(expected), offset.ToString());
    }
}
