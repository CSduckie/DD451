using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class PlayerInteractionTests
{
    private GameObject testRoot;
    private PlayerInteraction interaction;

    [SetUp]
    public void SetUp()
    {
        testRoot = new GameObject("PlayerInteractionTests");
        testRoot.transform.position = new Vector3(2000f, 2000f, 0f);
        GameObject player = CreateChild("Player", Vector3.zero);
        Rigidbody body = player.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        CapsuleCollider capsule = player.AddComponent<CapsuleCollider>();
        capsule.radius = 0.4f;
        capsule.height = 2f;
        interaction = player.AddComponent<PlayerInteraction>();
        interaction.queryOffset = Vector3.zero;
        interaction.interactionRadius = 3f;
        interaction.maxDepthDifference = 0.5f;
        interaction.SetInputBlocked(false);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(testRoot);
    }

    [Test]
    public void NearestEligibleTargetAndMultipleCollidersExecuteOnlyOnce()
    {
        PlayerTestInteractable near = CreateTarget("Near", new Vector3(1.2f, 0f, 0f));
        GameObject secondCollider = new GameObject("SecondCollider");
        secondCollider.transform.SetParent(near.transform, false);
        secondCollider.AddComponent<SphereCollider>().radius = 0.2f;
        PlayerTestInteractable far = CreateTarget("Far", new Vector3(-2f, 0f, 0f));
        Physics.SyncTransforms();

        interaction.RefreshTarget();
        Assert.AreSame(near, interaction.CurrentTarget);
        Assert.AreEqual("player.interact.test", interaction.CurrentPromptKey);
        Assert.IsTrue(interaction.TryInteract());
        Assert.AreEqual(1, near.InteractionCount);
        Assert.AreEqual(0, far.InteractionCount);

        near.available = false;
        interaction.RefreshTarget();
        Assert.AreSame(far, interaction.CurrentTarget);
    }

    [Test]
    public void AuthoredTriggerDepthAndSolidWallPreventInteraction()
    {
        PlayerTestInteractable background = CreateTarget("Background", new Vector3(0f, 0f, 1f));
        PlayerTestInteractable target = CreateTarget("BehindWall", new Vector3(2f, 0f, 0f));
        GameObject wall = CreateChild("Wall", new Vector3(1f, 0f, 0f));
        BoxCollider wallCollider = wall.AddComponent<BoxCollider>();
        wallCollider.size = new Vector3(0.2f, 2f, 2f);
        Physics.SyncTransforms();

        interaction.RefreshTarget();
        Assert.IsNull(interaction.CurrentTarget);
        Assert.IsFalse(interaction.TryInteract());
        Assert.AreEqual(0, background.InteractionCount);
        Assert.AreEqual(0, target.InteractionCount);

        wallCollider.enabled = false;
        Physics.SyncTransforms();
        interaction.RefreshTarget();
        Assert.AreSame(target, interaction.CurrentTarget);
        Assert.IsTrue(interaction.TryInteract());
        Assert.AreEqual(1, target.InteractionCount);
    }

    [Test]
    public void ExecutionRechecksRangeAvailabilityAndNewObstruction()
    {
        PlayerTestInteractable target = CreateTarget("Target", Vector3.right * 2f);
        Physics.SyncTransforms();
        interaction.RefreshTarget();
        target.transform.localPosition = Vector3.right * 5f;
        // 故意不手动同步：同一帧移动后按E也必须拒绝旧目标。
        Assert.IsFalse(interaction.TryInteract());
        Assert.IsNull(interaction.CurrentTarget);

        target.transform.localPosition = Vector3.right * 2f;
        Physics.SyncTransforms();
        interaction.RefreshTarget();
        target.available = false;
        Assert.IsFalse(interaction.TryInteract());

        target.available = true;
        interaction.RefreshTarget();
        GameObject wall = CreateChild("NewWall", Vector3.right);
        wall.AddComponent<BoxCollider>().size = new Vector3(0.2f, 2f, 2f);
        Assert.IsFalse(interaction.TryInteract());
        Assert.AreEqual(0, target.InteractionCount);
    }

    [Test]
    public void InputLockAndDisabledComponentsClearCachedTarget()
    {
        PlayerTestInteractable target = CreateTarget("Target", Vector3.right * 1.2f);
        Physics.SyncTransforms();
        interaction.RefreshTarget();
        interaction.SetInputBlocked(true);
        Assert.IsTrue(interaction.InputBlocked);
        Assert.IsNull(interaction.CurrentTarget);
        Assert.AreEqual(string.Empty, interaction.CurrentPromptKey);
        Assert.IsFalse(interaction.TryInteract());

        interaction.SetInputBlocked(false);
        Assert.IsFalse(interaction.TryInteract());
        interaction.RefreshTarget();
        target.enabled = false;
        Assert.IsFalse(interaction.TryInteract());

        target.enabled = true;
        interaction.RefreshTarget();
        target.interactionTrigger.enabled = false;
        Assert.IsFalse(interaction.TryInteract());

        target.interactionTrigger.enabled = true;
        interaction.RefreshTarget();
        target.gameObject.SetActive(false);
        Assert.IsFalse(interaction.TryInteract());

        target.gameObject.SetActive(true);
        interaction.RefreshTarget();
        interaction.enabled = false;
        Assert.IsNull(interaction.CurrentTarget);
        Assert.IsFalse(interaction.TryInteract());
        Assert.AreEqual(0, target.InteractionCount);
    }

    [Test]
    public void OwnCollidersAreExcludedAndTriggerTargetRemainsUsable()
    {
        GameObject ownChild = new GameObject("OwnChild");
        ownChild.transform.SetParent(interaction.transform, false);
        ownChild.transform.localPosition = Vector3.right * 0.7f;
        ownChild.AddComponent<BoxCollider>().size = Vector3.one * 0.2f;
        PlayerTestInteractable ownTarget = ownChild.AddComponent<PlayerTestInteractable>();
        AddInteractionTrigger(ownTarget);
        PlayerTestInteractable target = CreateTarget("WorldTarget", Vector3.right * 2f);
        GameObject trigger = CreateChild("UnrelatedTrigger", Vector3.right * 1.2f);
        BoxCollider triggerCollider = trigger.AddComponent<BoxCollider>();
        triggerCollider.size = Vector3.one * 0.2f;
        triggerCollider.isTrigger = true;
        Physics.SyncTransforms();

        interaction.RefreshTarget();
        Assert.AreSame(target, interaction.CurrentTarget);
        Assert.IsTrue(interaction.TryInteract());
        Assert.AreEqual(0, ownTarget.InteractionCount);
        Assert.AreEqual(1, target.InteractionCount);
    }

    [UnityTest]
    public IEnumerator DestroyedTargetCannotExecuteOrKeepPrompt()
    {
        PlayerTestInteractable target = CreateTarget("Target", Vector3.right * 1.2f);
        Physics.SyncTransforms();
        interaction.RefreshTarget();
        Assert.AreSame(target, interaction.CurrentTarget);

        Object.Destroy(target.gameObject);
        yield return null;

        Assert.IsNull(interaction.CurrentTarget);
        Assert.AreEqual(string.Empty, interaction.CurrentPromptKey);
        Assert.IsFalse(interaction.TryInteract());
    }

    private PlayerTestInteractable CreateTarget(string objectName, Vector3 position)
    {
        GameObject target = CreateChild(objectName, position);
        target.AddComponent<SphereCollider>().radius = 0.2f;
        PlayerTestInteractable interactable = target.AddComponent<PlayerTestInteractable>();
        AddInteractionTrigger(interactable);
        return interactable;
    }

    private void AddInteractionTrigger(PlayerTestInteractable target)
    {
        GameObject triggerObject = new GameObject("TriggerBox");
        triggerObject.transform.SetParent(target.transform, false);
        BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(6f, 3f, 1f);
        target.interactionTrigger = trigger;
    }

    private GameObject CreateChild(string objectName, Vector3 position)
    {
        GameObject child = new GameObject(objectName);
        child.transform.SetParent(testRoot.transform, false);
        child.transform.localPosition = position;
        return child;
    }
}
