using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class PlayerTriggerInteractionTests
{
    private GameObject testRoot;
    private PlayerInteraction interaction;
    private CapsuleCollider playerBody;

    [SetUp]
    public void SetUp()
    {
        testRoot = new GameObject("PlayerTriggerInteractionTests");
        testRoot.transform.position = new Vector3(4000f, 4000f, 0f);
        GameObject player = CreateChild("Player", Vector3.zero);
        Rigidbody body = player.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        playerBody = player.AddComponent<CapsuleCollider>();
        playerBody.radius = 0.4f;
        playerBody.height = 2f;
        interaction = player.AddComponent<PlayerInteraction>();
        interaction.queryOffset = Vector3.zero;
        interaction.interactionRadius = 2f;
        interaction.maxDepthDifference = 0.5f;
        interaction.SetInputBlocked(false);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(testRoot);
    }

    [Test]
    public void OutsideTriggerRejectsDespiteLegacySphereProximity()
    {
        PlayerTestInteractable target = CreateTarget("Small authored range", Vector3.right * 1.2f,
            new Vector3(0.4f, 2f, 1f));
        Physics.SyncTransforms();
        Assert.That(Vector3.Distance(interaction.transform.position, target.transform.position),
            Is.LessThan(interaction.interactionRadius));

        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.Null);
        Assert.That(interaction.CurrentPromptKey, Is.Empty);
        Assert.That(interaction.CanReachTarget(target), Is.False);
        Assert.That(interaction.TryInteract(), Is.False);
        Assert.That(target.InteractionCount, Is.Zero);
    }

    [UnityTest]
    public IEnumerator EnlargedTriggerAllowsModelGapBeyondLegacyRadiusWithoutAutoInteraction()
    {
        PlayerTestInteractable target = CreateTarget("Distant model", Vector3.right * 4f,
            new Vector3(9f, 3f, 2f));
        Physics.SyncTransforms();
        Assert.That(playerBody.bounds.Intersects(target.GetComponent<SphereCollider>().bounds), Is.False,
            "Only the interaction range overlaps; the model body must remain visibly separated.");
        Assert.That(Vector3.Distance(interaction.transform.position, target.transform.position),
            Is.GreaterThan(interaction.interactionRadius));
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.SameAs(target));
        Assert.That(interaction.CanReachTarget(target), Is.True);
        Assert.That(target.InteractionCount, Is.Zero, "Entering a TriggerBox must not execute an interaction.");
        Assert.That(interaction.TryInteract(), Is.True);
        Assert.That(target.InteractionCount, Is.EqualTo(1));
    }

    [Test]
    public void AuthoredTriggerCenterAndDepthAllowOffsetModelAndControlReach()
    {
        PlayerTestInteractable target = CreateTarget("Background model", new Vector3(1.5f, 0f, 3f),
            new Vector3(4f, 3f, 0.5f));
        target.interactionTrigger.center = Vector3.back * 3f;
        Physics.SyncTransforms();

        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.SameAs(target),
            "The target root's visual depth must not clip a TriggerBox authored around the player plane.");
        Assert.That(interaction.CanReachTarget(target), Is.True);
        target.interactionTrigger.center = Vector3.zero;
        Physics.SyncTransforms();
        Assert.That(interaction.CanReachTarget(target), Is.False,
            "Moving the authored box out of the player plane must remove reachability.");
        Assert.That(interaction.CurrentTarget, Is.Null);
    }

    [Test]
    public void LargeTriggerCannotBypassWallToExplicitInteractionPoint()
    {
        PlayerTestInteractable target = CreateTarget("Offset target anchor", Vector3.forward * 3f,
            new Vector3(8f, 3f, 8f));
        Transform point = CreateAnchor(target, new Vector3(3f, 0f, 0f));
        target.interactionPoint = point;
        GameObject wall = CreateChild("Wall to interaction point", Vector3.right * 1.5f);
        BoxCollider wallBody = wall.AddComponent<BoxCollider>();
        wallBody.size = new Vector3(0.2f, 3f, 1f);
        Physics.SyncTransforms();
        Assert.That(target.interactionTrigger.bounds.Contains(interaction.transform.position), Is.True);

        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.Null);
        Assert.That(interaction.CanReachTarget(target), Is.False);
        Assert.That(interaction.TryInteract(), Is.False);
        target.interactionPoint = null;
        Assert.That(interaction.CanReachTarget(target), Is.True,
            "A missing optional point falls back to the target root, whose path is unobstructed here.");
        target.interactionPoint = point;
        wallBody.isTrigger = true;
        Physics.SyncTransforms();
        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.SameAs(target), "Decorative triggers must not block the anchor ray.");
        Assert.That(interaction.TryInteract(), Is.True);
        Assert.That(target.InteractionCount, Is.EqualTo(1));
    }

    [Test]
    public void RotatedTriggerBoundsDoNotCountAsActualBodyOverlap()
    {
        Vector3 targetPosition = new Vector3(1.2f, 0f, 1.2f);
        PlayerTestInteractable target = CreateTarget("Rotated thin range", targetPosition,
            new Vector3(3.2f, 2f, 0.2f));
        target.interactionTrigger.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
        Physics.SyncTransforms();
        Assert.That(target.interactionTrigger.bounds.Intersects(playerBody.bounds), Is.True,
            "The test deliberately overlaps bounding boxes while keeping the actual shapes separated.");
        Assert.That(interaction.CanReachTarget(target), Is.False);
        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.Null);

        target.interactionTrigger.transform.localPosition = -targetPosition;
        Physics.SyncTransforms();
        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.SameAs(target));
        Assert.That(interaction.CanReachTarget(target), Is.True);
    }

    [UnityTest]
    public IEnumerator TeleportDisableReenableAndDestroyInvalidateWithoutTriggerCallbacks()
    {
        PlayerTestInteractable target = CreateTarget("Lifecycle target", Vector3.right * 5f,
            new Vector3(4f, 3f, 2f));
        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.Null);

        // No OnTriggerEnter or manual SyncTransforms between the teleport and the query.
        interaction.transform.localPosition = Vector3.right * 4f;
        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.SameAs(target));
        target.interactionTrigger.enabled = false;
        Assert.That(interaction.CurrentTarget, Is.Null);
        Assert.That(interaction.CanReachTarget(target), Is.False);
        target.interactionTrigger.enabled = true;
        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.SameAs(target));

        target.enabled = false;
        Assert.That(interaction.CurrentTarget, Is.Null);
        target.enabled = true;
        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.SameAs(target));
        target.gameObject.SetActive(false);
        Assert.That(target.TryGetInteractionTrigger(out _), Is.False,
            "Inactive loading ancestors must not be diagnosed as foreign trigger ownership.");
        LogAssert.NoUnexpectedReceived();
        Assert.That(interaction.CanReachTarget(target), Is.False);
        target.gameObject.SetActive(true);
        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.SameAs(target));

        Object.Destroy(target.interactionTrigger.gameObject);
        yield return null;
        Assert.That(interaction.CurrentTarget, Is.Null);
        Assert.That(interaction.CanReachTarget(target), Is.False);
        Assert.That(interaction.TryInteract(), Is.False);
        AddInteractionTrigger(target, new Vector3(4f, 3f, 2f));
        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.SameAs(target));
        Object.Destroy(target.gameObject);
        yield return null;
        Assert.That(interaction.CurrentTarget, Is.Null);
        Assert.That(interaction.CurrentPromptKey, Is.Empty);
        Assert.That(interaction.TryInteract(), Is.False);
    }

    [UnityTest]
    public IEnumerator MultipleSolidBodyCollidersWorkAndRemovalDoesNotLeaveStaleReach()
    {
        PlayerTestInteractable target = CreateTarget("Auxiliary body range", Vector3.right * 3.7f,
            new Vector3(0.8f, 2f, 1f));
        Physics.SyncTransforms();
        Assert.That(interaction.CanReachTarget(target), Is.False);
        GameObject child = new GameObject("Additional solid body");
        child.transform.SetParent(interaction.transform, false);
        child.transform.localPosition = Vector3.right * 3.1f;
        BoxCollider childBody = child.AddComponent<BoxCollider>();
        childBody.size = Vector3.one * 0.6f;
        Assert.That(childBody.attachedRigidbody, Is.SameAs(playerBody.attachedRigidbody));
        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.SameAs(target));
        Assert.That(interaction.TryInteract(), Is.True);
        Assert.That(target.InteractionCount, Is.EqualTo(1));

        childBody.isTrigger = true;
        Assert.That(interaction.CanReachTarget(target), Is.False, "A player's sensor is not its solid body.");
        childBody.isTrigger = false;
        Assert.That(interaction.CanReachTarget(target), Is.True);
        childBody.enabled = false;
        Assert.That(interaction.CurrentTarget, Is.Null);
        childBody.enabled = true;
        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.SameAs(target));
        Object.Destroy(child);
        yield return null;
        Assert.That(interaction.CanReachTarget(target), Is.False);
        Assert.That(interaction.CurrentTarget, Is.Null);
        interaction.transform.localPosition = Vector3.right * 3.7f;
        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.SameAs(target), "The remaining CapsuleCollider can enter on its own.");
    }

    [Test]
    public void AnchorDistanceAndInstanceIdChooseExactlyOneStableTarget()
    {
        PlayerTestInteractable nearRoot = CreateTarget("Near root", Vector3.right,
            new Vector3(14f, 3f, 2f));
        PlayerTestInteractable farRoot = CreateTarget("Far root close anchor", Vector3.right * 6f,
            new Vector3(14f, 3f, 2f));
        nearRoot.interactionPoint = CreateAnchor(nearRoot, Vector3.right * 2f);
        farRoot.interactionPoint = CreateAnchor(farRoot, Vector3.right * 0.5f);
        Physics.SyncTransforms();
        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.SameAs(farRoot), "Distance uses the interaction point, not the root or range edge.");

        nearRoot.interactionPoint.position = farRoot.interactionPoint.position;
        PlayerTestInteractable expected = nearRoot.GetInstanceID() < farRoot.GetInstanceID() ? nearRoot : farRoot;
        PlayerTestInteractable other = expected == nearRoot ? farRoot : nearRoot;
        for (int i = 0; i < 8; i++)
        {
            interaction.RefreshTarget();
            Assert.That(interaction.CurrentTarget, Is.SameAs(expected));
        }
        Assert.That(interaction.TryInteract(), Is.True);
        Assert.That(expected.InteractionCount, Is.EqualTo(1));
        Assert.That(other.InteractionCount, Is.Zero);
        expected.enabled = false;
        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.SameAs(other));
    }

    [Test]
    public void InputLockAndBusinessAvailabilityDoNotInvalidateSpatialReach()
    {
        PlayerTestInteractable target = CreateTarget("Open UI source", Vector3.right * 1.5f,
            new Vector3(4f, 3f, 2f));
        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.SameAs(target));
        interaction.SetInputBlocked(true);
        target.available = false;
        Assert.That(interaction.CurrentTarget, Is.Null);
        Assert.That(interaction.TryInteract(), Is.False);
        Assert.That(interaction.CanReachTarget(target), Is.True,
            "An open source session retains spatial reach even when E and new business actions are unavailable.");
        interaction.interactionRadius = 0f;
        interaction.maxDepthDifference = 0f;
        Assert.That(interaction.CanReachTarget(target), Is.True, "Legacy point limits do not disable TriggerBox reach.");
        Assert.That(interaction.CanReachWorldPoint(interaction.transform.position + Vector3.right), Is.False);
        interaction.interactionRadius = 2f;
        Assert.That(interaction.CanReachWorldPoint(interaction.transform.position + Vector3.right), Is.True,
            "World-point validation remains independent of the UI input lock.");
        Assert.That(interaction.CanReachWorldPoint(interaction.transform.position + Vector3.forward), Is.False);
        interaction.transform.localPosition = Vector3.right * 5f;
        Assert.That(interaction.CanReachTarget(target), Is.False);
        interaction.transform.localPosition = Vector3.zero;
        Assert.That(interaction.CanReachTarget(target), Is.True);
        target.enabled = false;
        Assert.That(interaction.CanReachTarget(target), Is.False);
    }

    [Test]
    public void MissingNonTriggerAndForeignOwnedRangesAreDiagnosedOnceUntilRepaired()
    {
        PlayerTestInteractable target = CreateTarget("Invalid range target", Vector3.right,
            new Vector3(4f, 3f, 2f));
        BoxCollider original = target.interactionTrigger;
        target.interactionTrigger = null;
        ExpectConfigurationWarning();
        Assert.That(interaction.CanReachTarget(target), Is.False);
        interaction.RefreshTarget();
        Assert.That(target.TryGetInteractionTrigger(out _), Is.False);
        Assert.That(interaction.CurrentTarget, Is.Null);
        Assert.That(interaction.TryInteract(), Is.False);
        LogAssert.NoUnexpectedReceived();

        target.interactionTrigger = original;
        Assert.That(interaction.CanReachTarget(target), Is.True);
        target.interactionTrigger = null;
        ExpectConfigurationWarning();
        Assert.That(interaction.CanReachTarget(target), Is.False, "Repair must re-arm diagnostics for a later configuration error.");
        target.interactionTrigger = original;
        Assert.That(interaction.CanReachTarget(target), Is.True);
        original.isTrigger = false;
        ExpectConfigurationWarning();
        Assert.That(interaction.CanReachTarget(target), Is.False);
        Assert.That(target.TryGetInteractionTrigger(out _), Is.False);
        original.isTrigger = true;
        Assert.That(interaction.CanReachTarget(target), Is.True);

        PlayerTestInteractable foreign = CreateTarget("Foreign owner", Vector3.left,
            new Vector3(4f, 3f, 2f));
        target.interactionTrigger = foreign.interactionTrigger;
        ExpectConfigurationWarning();
        Assert.That(interaction.CanReachTarget(target), Is.False);
        interaction.RefreshTarget();
        Assert.That(interaction.CurrentTarget, Is.SameAs(foreign));
        Assert.That(target.InteractionCount, Is.Zero);

        target.interactionTrigger = original;
        Assert.That(target.TryGetInteractionTrigger(out _), Is.True);
        GameObject nestedObject = new GameObject("Nested range owner");
        nestedObject.transform.SetParent(target.transform, false);
        PlayerTestInteractable nested = nestedObject.AddComponent<PlayerTestInteractable>();
        original.transform.SetParent(nestedObject.transform, false);
        nested.interactionTrigger = original;
        ExpectConfigurationWarning();
        Assert.That(target.TryGetInteractionTrigger(out _), Is.False,
            "A descendant range owned by a nearer nested Interactable cannot be borrowed by its ancestor.");
        Assert.That(nested.TryGetInteractionTrigger(out _), Is.True);
    }

    private PlayerTestInteractable CreateTarget(string name, Vector3 position, Vector3 triggerSize)
    {
        GameObject targetObject = CreateChild(name, position);
        targetObject.AddComponent<SphereCollider>().radius = 0.15f;
        PlayerTestInteractable target = targetObject.AddComponent<PlayerTestInteractable>();
        AddInteractionTrigger(target, triggerSize);
        return target;
    }

    private void AddInteractionTrigger(PlayerTestInteractable target, Vector3 size)
    {
        GameObject triggerObject = new GameObject("TriggerBox");
        triggerObject.transform.SetParent(target.transform, false);
        BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = size;
        target.interactionTrigger = trigger;
    }

    private Transform CreateAnchor(PlayerTestInteractable target, Vector3 positionInFixture)
    {
        GameObject point = new GameObject("InteractionPoint");
        point.transform.SetParent(target.transform, false);
        point.transform.position = testRoot.transform.position + positionInFixture;
        return point.transform;
    }

    private GameObject CreateChild(string name, Vector3 position)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(testRoot.transform, false);
        child.transform.localPosition = position;
        return child;
    }

    private static void ExpectConfigurationWarning()
    {
        LogAssert.Expect(LogType.Warning, new Regex("^" + Regex.Escape("交互配置错误 [interactionTrigger]：")));
    }
}
