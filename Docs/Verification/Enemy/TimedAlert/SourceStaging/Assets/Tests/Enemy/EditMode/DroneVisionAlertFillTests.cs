using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class DroneVisionAlertFillTests
{
    private readonly List<Object> created = new List<Object>();
    private readonly Vector3 origin = new Vector3(12000f, 12000f, 0f);

    [TearDown]
    public void TearDown()
    {
        for (int i = created.Count - 1; i >= 0; i--) { if (created[i] != null) { Object.DestroyImmediate(created[i]); } }
        created.Clear();
    }

    [TestCase(-1f, 0f)]
    [TestCase(0.5f, 0.5f)]
    [TestCase(2f, 1f)]
    public void ProgressClampsWithoutReplacingGeometry(float progress, float expected)
    {
        DroneVision vision = CreateVision();
        MeshCollider collider = vision.RangeCollider;
        Mesh range = collider.sharedMesh;
        Mesh visible = vision.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] vertices = visible.vertices;
        vision.SetAlertProgress(progress);
        Assert.That(ReadProperties(vision).GetFloat("_FillProgress"), Is.EqualTo(expected));
        Assert.That(vision.RangeCollider, Is.SameAs(collider));
        Assert.That(collider.sharedMesh, Is.SameAs(range));
        Assert.That(vision.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(visible));
        CollectionAssert.AreEqual(vertices, visible.vertices);
    }

    [Test]
    public void OrdinaryTintClearsFillAndKeepsRequestedColor()
    {
        DroneVision vision = CreateVision();
        vision.SetAlertProgress(0.8f);
        Color color = new Color(0.25f, 0.8f, 1f, 0.16f);
        vision.SetTint(color);
        MaterialPropertyBlock block = ReadProperties(vision);
        Assert.That(block.GetFloat("_FillProgress"), Is.Zero);
        AssertColor(block.GetColor("_BaseColor"), color);
    }

    [Test]
    public void ClearAndDisableReenableDoNotRetainFill()
    {
        DroneVision vision = CreateVision();
        vision.SetAlertProgress(0.8f);
        vision.Clear();
        Assert.That(ReadProperties(vision).GetFloat("_FillProgress"), Is.Zero);
        Assert.That(vision.RangeCollider.enabled, Is.False);
        vision.Rebuild();
        vision.SetAlertProgress(0.8f);
        vision.enabled = false;
        Assert.That(ReadProperties(vision).GetFloat("_FillProgress"), Is.Zero);
        vision.enabled = true;
        Assert.That(ReadProperties(vision).GetFloat("_FillProgress"), Is.Zero);
        Assert.That(vision.RangeCollider.enabled, Is.True);
        vision.SetAlertProgress(0.8f);
        vision.gameObject.SetActive(false);
        vision.gameObject.SetActive(true);
        Assert.That(ReadProperties(vision).GetFloat("_FillProgress"), Is.Zero);
    }

    [Test]
    public void SharedMaterialInstancesFillAndRetreatIndependently()
    {
        Shader shader = Shader.Find("MyPrecious/DroneVision");
        Assert.That(shader, Is.Not.Null);
        Material material = new Material(shader);
        created.Add(material);
        Color originalColor = material.GetColor("_BaseColor");
        float originalProgress = material.GetFloat("_FillProgress");
        DroneVision first = CreateVision();
        DroneVision second = CreateVision();
        first.GetComponent<MeshRenderer>().sharedMaterial = material;
        second.GetComponent<MeshRenderer>().sharedMaterial = material;
        Color orange = new Color(1f, 0.67f, 0.1f, 0.2f);
        first.SetTint(orange);
        second.SetTint(orange);
        first.SetAlertProgress(0.8f);
        second.SetAlertProgress(0.6f);
        Assert.That(ReadProperties(first).GetFloat("_FillProgress"), Is.EqualTo(0.8f));
        first.SetAlertProgress(0.3f);
        Assert.That(ReadProperties(first).GetFloat("_FillProgress"), Is.EqualTo(0.3f));
        Assert.That(ReadProperties(second).GetFloat("_FillProgress"), Is.EqualTo(0.6f));
        AssertColor(ReadProperties(first).GetColor("_BaseColor"), orange);
        AssertColor(ReadProperties(second).GetColor("_BaseColor"), orange);
        Assert.That(first.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(material));
        Assert.That(second.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(material));
        Assert.That(material.GetFloat("_FillProgress"), Is.EqualTo(originalProgress));
        Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(originalColor));
    }

    [Test]
    public void WallClippedFanHasNormalizedRadialUvsAndOneSurface()
    {
        DroneVision vision = CreateVision();
        GameObject wallObject = new GameObject("AlertFillWall");
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
        GameObject go = new GameObject("AlertFillVision");
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
