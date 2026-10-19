using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class DroneVisionViewTests
{
    private readonly List<Object> created = new List<Object>();
    private readonly Vector3 origin = new Vector3(12000f, 12000f, 0f);

    [TearDown]
    public void TearDown()
    {
        for (int i = created.Count - 1; i >= 0; i--) { if (created[i] != null) { Object.DestroyImmediate(created[i]); } }
        created.Clear();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ViewVisibilityDoesNotChangeGeometryOrDetection(bool visible)
    {
        DroneVision vision = CreateVision();
        PlayerConfigSO config = ScriptableObject.CreateInstance<PlayerConfigSO>();
        created.Add(config);
        GameObject playerObject = new GameObject("ViewPlayer");
        created.Add(playerObject);
        playerObject.SetActive(false);
        PlayerController player = playerObject.AddComponent<PlayerController>();
        player.config = config;
        playerObject.SetActive(true);
        Assert.That(player.ResetForExploration(1, origin + new Vector3(3f, -1f, 0f)), Is.True);
        player.Body.useGravity = false;
        Physics.SyncTransforms();
        Mesh range = vision.RangeCollider.sharedMesh;
        Mesh view = vision.GetComponent<MeshFilter>().sharedMesh;
        vision.SetViewVisible(visible);
        vision.SendMessage("LateUpdate");
        Assert.That(vision.GetComponent<MeshRenderer>().enabled, Is.EqualTo(visible));
        Assert.That(vision.enabled && vision.gameObject.activeInHierarchy && vision.RangeCollider.enabled, Is.True);
        Assert.That(vision.RangeCollider.sharedMesh, Is.SameAs(range));
        Assert.That(vision.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(view));
        Assert.That(vision.CanSee(player), Is.True);
        Assert.That(vision.FindVisiblePlayer(), Is.SameAs(player));
    }

    [Test]
    public void RebuildKeepsHiddenViewAndActiveRange()
    {
        DroneVision vision = CreateVision();
        vision.SetViewVisible(false);
        vision.Rebuild();
        vision.SendMessage("LateUpdate");
        Assert.That(vision.GetComponent<MeshRenderer>().enabled, Is.False);
        Assert.That(vision.RangeCollider.enabled, Is.True);
        vision.SetViewVisible(true);
        Assert.That(vision.GetComponent<MeshRenderer>().enabled, Is.True);
    }

    [Test]
    public void PureTintClearsOldMaterialFillWithoutChangingSharedMaterial()
    {
        DroneVision vision = CreateVision();
        Material material = new Material(Shader.Find("MyPrecious/DroneVision"));
        created.Add(material);
        material.SetFloat("_FillProgress", 0.8f);
        vision.GetComponent<MeshRenderer>().sharedMaterial = material;
        Color orange = new Color(1f, 0.67f, 0.1f, 0.2f);
        vision.SetTint(orange);
        Assert.That(ReadProperties(vision).GetFloat("_FillProgress"), Is.Zero);
        AssertColor(ReadProperties(vision).GetColor("_BaseColor"), orange);
        Assert.That(material.GetFloat("_FillProgress"), Is.EqualTo(0.8f));
    }

    [Test]
    public void ClearAndReenableKeepRequestedDisplayWithoutRevivingDisabledDetection()
    {
        DroneVision vision = CreateVision();
        vision.SetViewVisible(false);
        vision.Clear();
        Assert.That(vision.RangeCollider.enabled, Is.False);
        Assert.That(vision.GetComponent<MeshRenderer>().enabled, Is.False);
        vision.Rebuild();
        Assert.That(vision.RangeCollider.enabled, Is.True);
        Assert.That(vision.GetComponent<MeshRenderer>().enabled, Is.False);
        vision.enabled = false;
        vision.enabled = true;
        Assert.That(vision.RangeCollider.enabled, Is.True);
        Assert.That(vision.GetComponent<MeshRenderer>().enabled, Is.False);
        vision.SetViewVisible(true);
        Assert.That(vision.GetComponent<MeshRenderer>().enabled, Is.True);
    }

    [Test]
    public void SharedMaterialInstancesKeepIndependentTintAndVisibility()
    {
        Material material = new Material(Shader.Find("MyPrecious/DroneVision"));
        created.Add(material);
        DroneVision first = CreateVision();
        DroneVision second = CreateVision();
        first.GetComponent<MeshRenderer>().sharedMaterial = material;
        second.GetComponent<MeshRenderer>().sharedMaterial = material;
        Color orange = new Color(1f, 0.67f, 0.1f, 0.2f);
        Color blue = new Color(0.25f, 0.8f, 1f, 0.16f);
        first.SetTint(orange);
        second.SetTint(blue);
        first.SetViewVisible(false);
        AssertColor(ReadProperties(first).GetColor("_BaseColor"), orange);
        AssertColor(ReadProperties(second).GetColor("_BaseColor"), blue);
        Assert.That(first.GetComponent<MeshRenderer>().enabled, Is.False);
        Assert.That(second.GetComponent<MeshRenderer>().enabled, Is.True);
        Assert.That(first.RangeCollider.enabled && second.RangeCollider.enabled, Is.True);
        Assert.That(ReadProperties(first).GetFloat("_FillProgress"), Is.Zero);
        Assert.That(ReadProperties(second).GetFloat("_FillProgress"), Is.Zero);
    }

    [Test]
    public void WallClippedFanHasNormalizedRadialUvsAndOneSurface()
    {
        DroneVision vision = CreateVision();
        GameObject wallObject = new GameObject("ViewWall");
        created.Add(wallObject);
        wallObject.transform.position = origin + Vector3.right * 2f;
        BoxCollider wall = wallObject.AddComponent<BoxCollider>();
        wall.size = new Vector3(0.1f, 10f, 2f);
        Physics.SyncTransforms();
        vision.Rebuild();
        Mesh mesh = vision.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] vertices = mesh.vertices;
        Vector2[] uvs = mesh.uv;
        int[] triangles = mesh.triangles;
        Assert.That(vertices.Length, Is.GreaterThan(3));
        Assert.That(uvs.Length, Is.EqualTo(vertices.Length));
        Assert.That(vertices[0], Is.EqualTo(Vector3.zero));
        Assert.That(uvs[0].x, Is.Zero);
        Assert.That(mesh.bounds.max.x, Is.LessThan(2f));
        Assert.That(mesh.subMeshCount, Is.EqualTo(1));
        Assert.That(triangles.Length, Is.EqualTo((vertices.Length - 2) * 6));
        for (int i = 0; i < vertices.Length; i++)
        {
            Assert.That(vertices[i].z, Is.Zero, "可见扇形保持在同一平面。");
            Assert.That(vision.transform.TransformPoint(vertices[i]).x, Is.LessThanOrEqualTo(wall.bounds.min.x + 0.002f));
            if (i > 0) { Assert.That(uvs[i].x, Is.EqualTo(1f), "墙前可见外缘仍代表100%。"); }
        }
        for (int i = 0; i < triangles.Length; i += 6)
        {
            Assert.That(triangles[i], Is.Zero);
            Assert.That(triangles[i + 3], Is.Zero);
            Assert.That(triangles[i + 1], Is.EqualTo(triangles[i + 5]));
            Assert.That(triangles[i + 2], Is.EqualTo(triangles[i + 4]));
        }
    }

    private DroneVision CreateVision()
    {
        GameObject go = new GameObject("ViewVision");
        created.Add(go);
        go.transform.position = origin;
        DroneVision vision = go.AddComponent<DroneVision>();
        vision.Rebuild();
        return vision;
    }

    private MaterialPropertyBlock ReadProperties(DroneVision vision)
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        vision.GetComponent<MeshRenderer>().GetPropertyBlock(block);
        return block;
    }

    private static void AssertColor(Color actual, Color expected)
    {
        // MPB颜色读写有浮点舍入，仍要求所有分量的总误差小于十万分之一。
        Assert.That(Vector4.Distance(actual, expected), Is.LessThan(0.00001f));
    }
}


