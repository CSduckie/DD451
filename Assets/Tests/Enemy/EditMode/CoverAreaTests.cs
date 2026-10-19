using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class CoverAreaTests
{
    private readonly List<GameObject> objects = new List<GameObject>();
    private readonly Vector3 origin = new Vector3(2000f, 2000f, 0f);
    private PlayerConfigSO config;
    private PlayerController player;

    [SetUp]
    public void SetUp()
    {
        config = ScriptableObject.CreateInstance<PlayerConfigSO>();
        config.damageInvulnerabilitySeconds = 0f;
        config.groundLayers = 0;
        player = CreatePlayer(true);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject item in objects)
        {
            if (item != null) { Object.DestroyImmediate(item); }
        }
        objects.Clear();
        Object.DestroyImmediate(config);
        // 保留测试开始前场景中已有的掩体，不将其引用长期清空。
        InitializeRegistry();
    }

    [Test]
    public void OnlyCrouchingInsideCoverIsHiddenAndOpenBackpackKeepsTheRule()
    {
        CreateCover(origin, new Vector3(3f, 4f, 2f));
        Assert.That(CoverArea.IsHidden(player), Is.False);
        player.SetCrouching(true);
        Assert.That(CoverArea.IsHidden(player), Is.True);
        player.SetBackpackOpen(true);
        Assert.That(player.CanAct, Is.False);
        Assert.That(CoverArea.IsHidden(player), Is.True, "开包不应被当成无效玩家，也不改变蹲伏姿态。");
        player.SetBackpackOpen(false);
        player.SetCrouching(false);
        Assert.That(CoverArea.IsHidden(player), Is.False);
    }

    [Test]
    public void MovingOutsideAndBackUsesCurrentShapesWithoutWaitingForTriggerCallbacks()
    {
        CreateCover(origin, new Vector3(2f, 4f, 2f));
        player.SetCrouching(true);
        Assert.That(CoverArea.IsHidden(player), Is.True);
        player.transform.position = origin + Vector3.right * 5f;
        Assert.That(CoverArea.IsHidden(player), Is.False);
        player.transform.position = origin;
        Assert.That(CoverArea.IsHidden(player), Is.True);
    }

    [Test]
    public void LeavingOneOverlappingCoverKeepsTheOtherUntilItIsDisabledOrDestroyed()
    {
        CoverArea first = CreateCover(origin + Vector3.left, new Vector3(3f, 4f, 2f));
        CoverArea second = CreateCover(origin + Vector3.right, new Vector3(3f, 4f, 2f));
        player.SetCrouching(true);
        Assert.That(CoverArea.IsHidden(player), Is.True);
        player.transform.position = origin + Vector3.right * 2f;
        Assert.That(CoverArea.IsHidden(player), Is.True);
        first.enabled = false;
        Assert.That(CoverArea.IsHidden(player), Is.True);
        second.enabled = false;
        Assert.That(CoverArea.IsHidden(player), Is.False);
        second.enabled = true;
        InitializeRegistry();
        Assert.That(CoverArea.IsHidden(player), Is.True);
        Object.DestroyImmediate(second.gameObject);
        Assert.That(CoverArea.IsHidden(player), Is.False);
    }

    [Test]
    public void AnyEnabledBodyColliderCanOverlapButTriggersAndInactiveChildrenCannotHide()
    {
        CreateCover(origin + Vector3.right * 2f, Vector3.one);
        player.SetCrouching(true);
        Assert.That(CoverArea.IsHidden(player), Is.False);
        GameObject child = new GameObject("Second body collider");
        child.transform.SetParent(player.transform, false);
        child.transform.localPosition = Vector3.right * 2f;
        SphereCollider body = child.AddComponent<SphereCollider>();
        body.radius = 0.3f;
        Assert.That(CoverArea.IsHidden(player), Is.True);
        body.isTrigger = true;
        Assert.That(CoverArea.IsHidden(player), Is.False);
        body.isTrigger = false;
        body.enabled = false;
        Assert.That(CoverArea.IsHidden(player), Is.False);
        body.enabled = true;
        child.SetActive(false);
        Assert.That(CoverArea.IsHidden(player), Is.False);
        child.SetActive(true);
        Assert.That(CoverArea.IsHidden(player), Is.True);
        Object.DestroyImmediate(child);
        Assert.That(CoverArea.IsHidden(player), Is.False);
    }

    [Test]
    public void RotatedVolumeUsesItsBoxShapeAndDepthInsteadOfAnAxisAlignedBoundsCheck()
    {
        CoverArea cover = CreateCover(origin, new Vector3(4f, 2f, 1f));
        cover.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        capsule.radius = 0.1f;
        capsule.height = 0.2f;
        player.SetCrouching(true);
        player.transform.position = origin + new Vector3(1.2f, 1.2f, 0f);
        Assert.That(CoverArea.IsHidden(player), Is.True);
        player.transform.position = origin + new Vector3(1.2f, -1.2f, 0f);
        Assert.That(CoverArea.IsHidden(player), Is.False);
        player.transform.position = origin + Vector3.forward * 2f;
        Assert.That(CoverArea.IsHidden(player), Is.False);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void DisabledInactiveDeadOrTransitioningPlayerIsNeverHidden(int unavailable)
    {
        CreateCover(origin, new Vector3(3f, 4f, 2f));
        player.SetCrouching(true);
        Assert.That(CoverArea.IsHidden(player), Is.True);
        if (unavailable == 0) { player.enabled = false; }
        if (unavailable == 1) { player.gameObject.SetActive(false); }
        if (unavailable == 2) { Assert.That(player.health.TakeDamage(player.health.MaxHealth), Is.True); }
        if (unavailable == 3) { player.SetSceneTransitionBlocked(true); }
        Assert.That(CoverArea.IsHidden(player), Is.False);
    }

    [Test]
    public void UnpreparedNullAndDestroyedPlayersHaveNoRetainedHiddenState()
    {
        CreateCover(origin, new Vector3(3f, 4f, 2f));
        PlayerController unprepared = CreatePlayer(false);
        unprepared.SetCrouching(true);
        Assert.That(CoverArea.IsHidden(unprepared), Is.False);
        Assert.That(CoverArea.IsHidden(null), Is.False);
        player.SetCrouching(true);
        Assert.That(CoverArea.IsHidden(player), Is.True);
        Object.DestroyImmediate(player.gameObject);
        Assert.That(CoverArea.IsHidden(player), Is.False);
    }

    [Test]
    public void DisabledColliderAndInactiveCoverStopHidingWithoutAnExitCallback()
    {
        CoverArea cover = CreateCover(origin, new Vector3(3f, 4f, 2f));
        player.SetCrouching(true);
        BoxCollider box = cover.GetComponent<BoxCollider>();
        box.enabled = false;
        Assert.That(CoverArea.IsHidden(player), Is.False);
        box.enabled = true;
        Assert.That(CoverArea.IsHidden(player), Is.True);
        cover.gameObject.SetActive(false);
        Assert.That(CoverArea.IsHidden(player), Is.False);
    }

    [Test]
    public void RuntimeStaticResetRestoresExistingActiveCoversWithoutKeepingDestroyedOnes()
    {
        CoverArea cover = CreateCover(origin, new Vector3(3f, 4f, 2f));
        player.SetCrouching(true);
        Assert.That(CoverArea.IsHidden(player), Is.True);
        // 模拟运行初始化的两个阶段；实际Enter Play Mode组合仍需PlayMode验证。
        InitializeRegistry();
        Assert.That(CoverArea.IsHidden(player), Is.True);
        Object.DestroyImmediate(cover.gameObject);
        InitializeRegistry();
        Assert.That(CoverArea.IsHidden(player), Is.False);
    }

    private PlayerController CreatePlayer(bool prepare)
    {
        GameObject item = new GameObject("Cover test player");
        objects.Add(item);
        item.transform.position = origin;
        PlayerController result = item.AddComponent<PlayerController>();
        result.config = config;
        result.GetComponent<PlayerHealth>().config = config;
        if (prepare)
        {
            Assert.That(result.ResetForExploration(1, origin), Is.True);
            result.Body.useGravity = false;
        }
        return result;
    }

    private CoverArea CreateCover(Vector3 position, Vector3 size)
    {
        GameObject item = new GameObject("Cover test volume");
        objects.Add(item);
        item.transform.position = position;
        CoverArea result = item.AddComponent<CoverArea>();
        result.GetComponent<BoxCollider>().size = size;
        // EditMode仅搭建真实形状/查询夹具，不假装已经运行Trigger生命周期。
        InitializeRegistry();
        Physics.SyncTransforms();
        return result;
    }

    private static void InitializeRegistry()
    {
        typeof(CoverArea).GetMethod("ResetActiveAreas", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        typeof(CoverArea).GetMethod("RestoreActiveAreas", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    }
}
