#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

// 使用正式玩家Prefab与输入绑定；测试生成的地板、顶板和键盘在结束后清理。
public class PlayerCrouchColliderTests : PlayerPlayModeTestBase
{
    private Keyboard keyboard;

    [SetUp]
    public void AddCrouchTestKeyboard()
    {
        keyboard = InputSystem.AddDevice<Keyboard>();
    }

    [TearDown]
    public void RemoveCrouchTestKeyboard()
    {
        if (keyboard != null && keyboard.added) { InputSystem.RemoveDevice(keyboard); }
        keyboard = null;
    }

    [UnityTest]
    public IEnumerator CrouchChangesCapsuleAroundFeetAndRepeatedStandingDoesNotDrift()
    {
        PlayerController player = PreparePlayer();
        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        yield return WaitFor(() => IsSettled(player), "Player must start on the floor.");
        float standingHeight = capsule.height;
        Vector3 standingCenter = capsule.center;
        Vector3 rootScale = player.transform.localScale;
        float feetY = capsule.bounds.min.y;
        Assert.That(standingHeight, Is.EqualTo(2f).Within(0.001f));
        Assert.That(standingCenter.y, Is.EqualTo(1f).Within(0.001f));
        Assert.That(capsule.radius, Is.EqualTo(0.4f).Within(0.001f));

        for (int i = 0; i < 3; i++)
        {
            SetKeys(Key.C);
            yield return WaitFor(() => player.IsCrouching, "C must enter crouch.");
            float expectedHeight = Mathf.Max(capsule.radius * 2f,
                standingHeight * player.config.crouchVisualHeight);
            Assert.That(capsule.height, Is.EqualTo(expectedHeight).Within(0.001f));
            Assert.That(capsule.center.y - capsule.height * 0.5f,
                Is.EqualTo(standingCenter.y - standingHeight * 0.5f).Within(0.001f),
                "Shrinking changes the centre as well as the height, keeping the feet anchored.");
            Assert.That(capsule.center.x, Is.EqualTo(standingCenter.x));
            Assert.That(capsule.center.z, Is.EqualTo(standingCenter.z));
            Assert.That(capsule.bounds.min.y, Is.EqualTo(feetY).Within(0.03f));
            Assert.That(player.transform.localScale, Is.EqualTo(rootScale),
                "Do not scale the Rigidbody root or its child interaction distances.");

            SetKeys();
            yield return WaitFor(() => !player.IsCrouching, "Releasing C in open space must stand.");
            Assert.That(capsule.height, Is.EqualTo(standingHeight).Within(0.001f));
            Assert.That(capsule.center, Is.EqualTo(standingCenter));
            Assert.That(capsule.bounds.min.y, Is.EqualTo(feetY).Within(0.03f));
        }
    }

    [UnityTest]
    public IEnumerator LowPassageAllowsCrouchMovementButBlocksStandingAndJumpUntilExit()
    {
        PlayerController player = PreparePlayer(new Vector3(-1f, 0.05f, 0f));
        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        // 顶板下缘1.5米，高于蹲伏胶囊1.3米，低于站立胶囊2米。
        CreateBox("Low passage ceiling", new Vector3(2.5f, 1.7f, 0f), new Vector3(4f, 0.4f, 3f));
        yield return WaitFor(() => IsSettled(player), "Player must start beside the passage.");
        SetKeys(Key.C, Key.D);
        yield return WaitFor(() => player.Body.position.x > 1.2f,
            "The actual crouching capsule must pass beneath the 1.5m ceiling.", 5f);

        SetKeys(Key.Space);
        yield return StayTrueFor(() => player.IsCrouching && player.CurrentState == PlayerState.Crouch
            && !player.CanJump && player.IsGrounded && capsule.bounds.max.y < 1.5f,
            "Releasing C and pressing jump beneath the ceiling must retain crouch.");
        SetKeys(Key.D);
        yield return WaitFor(() => player.Body.position.x > 5f && !player.IsCrouching,
            "Walking out must restore standing without another C press.", 5f);
        SetKeys();
        Assert.That(capsule.height, Is.EqualTo(2f).Within(0.001f));
        Assert.That(capsule.center.y, Is.EqualTo(1f).Within(0.001f));
        Assert.That(player.Body.position.z, Is.EqualTo(player.config.planeZ).Within(0.001f));
    }

    [UnityTest]
    public IEnumerator BackpackKeepsSmallCapsuleAndDamageCloseCannotForceStandingIntoCeiling()
    {
        PlayerController player = PreparePlayer();
        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        yield return WaitFor(() => IsSettled(player), "Player must start on the floor.");
        SetKeys(Key.C);
        yield return WaitFor(() => player.IsCrouching, "Player must crouch before opening the bag.");
        Collider ceiling = CreateBox("Bag posture ceiling", new Vector3(0f, 1.7f, 0f), new Vector3(3f, 0.4f, 3f));
        float crouchedHeight = capsule.height;
        player.SetBackpackOpen(true);
        SetKeys(Key.D, Key.Space);
        float lockedX = player.Body.position.x;
        yield return StayTrueFor(() => player.IsBackpackOpen && player.IsCrouching
            && Mathf.Abs(capsule.height - crouchedHeight) < 0.001f
            && Mathf.Abs(player.Body.position.x - lockedX) < 0.03f,
            "Opening the bag must keep the crouched collider and block active movement.");

        SetKeys();
        yield return null;
        // 玩家测试仅接入04的关闭通知边界，不冒充完整背包UI验收。
        Action<int> closeOnDamage = damage => player.SetBackpackOpen(false);
        player.health.NonLethalDamaged += closeOnDamage;
        try
        {
            Assert.That(player.health.TakeDamage(1), Is.True);
            Assert.That(player.IsBackpackOpen, Is.False);
            yield return StayTrueFor(() => player.IsCrouching && player.CurrentState == PlayerState.Crouch
                && Mathf.Abs(capsule.height - crouchedHeight) < 0.001f,
                "A damage-driven close must not force the player into the ceiling.");
        }
        finally { player.health.NonLethalDamaged -= closeOnDamage; }

        ceiling.enabled = false;
        Physics.SyncTransforms();
        yield return WaitFor(() => !player.IsCrouching, "Clearing the ceiling should restore standing.");
        Assert.That(capsule.height, Is.EqualTo(2f).Within(0.001f));
    }

    [UnityTest]
    public IEnumerator NewRoundResetsCapsuleAtDestinationInsteadOfTestingOldCeiling()
    {
        PlayerController player = PreparePlayer();
        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        yield return WaitFor(() => IsSettled(player), "Player must start on the floor.");
        SetKeys(Key.C);
        yield return WaitFor(() => player.IsCrouching, "Player must crouch first.");
        CreateBox("Old location ceiling", new Vector3(0f, 1.7f, 0f), new Vector3(3f, 0.4f, 3f));
        SetKeys();
        yield return StayTrueFor(() => player.IsCrouching, "Old location must prevent standing.");

        Assert.That(player.ResetForExploration(2, new Vector3(6f, 0.05f, 9f)), Is.True);
        Assert.That(player.IsCrouching, Is.False, "The old ceiling cannot prevent a fresh round's spawn pose.");
        Assert.That(capsule.height, Is.EqualTo(2f).Within(0.001f));
        Assert.That(capsule.center.y, Is.EqualTo(1f).Within(0.001f));
        Assert.That(player.Body.position.x, Is.EqualTo(6f).Within(0.001f));
        Assert.That(player.Body.position.z, Is.EqualTo(player.config.planeZ).Within(0.001f));
        yield return WaitFor(() => IsSettled(player) && !player.IsCrouching,
            "The standing player must settle normally at the destination.");
    }

    [UnityTest]
    public IEnumerator SideWallTriggerAndExplicitlyIgnoredColliderDoNotBlockStanding()
    {
        PlayerController player = PreparePlayer();
        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        CreateBox("Nearby side wall", new Vector3(0.7f, 2f, 0f), new Vector3(0.5f, 4f, 3f));
        yield return WaitFor(() => IsSettled(player), "Player must settle beside the wall.");
        SetKeys(Key.C);
        yield return WaitFor(() => player.IsCrouching, "Player must crouch first.");
        Collider trigger = CreateBox("Interaction trigger overhead", new Vector3(0f, 1.7f, 0f), new Vector3(2f, 0.4f, 2f));
        trigger.isTrigger = true;
        Collider ignored = CreateBox("Explicitly ignored overhead solid", new Vector3(0f, 1.7f, 0f), new Vector3(2f, 0.4f, 2f));
        Physics.IgnoreCollision(capsule, ignored, true);
        Physics.SyncTransforms();

        SetKeys();
        yield return WaitFor(() => !player.IsCrouching,
            "Only actual blocking geometry should prevent standing, not triggers or ignored pairs.");
        Assert.That(capsule.height, Is.EqualTo(2f).Within(0.001f));
        Assert.That(capsule.center.y, Is.EqualTo(1f).Within(0.001f));
        Assert.That(player.IsGrounded, Is.True, "The floor itself must not block standing.");
    }

    private PlayerController PreparePlayer(Vector3? position = null)
    {
        CreateBox("Crouch physics floor", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 8f));
        PlayerController player = CreatePlayer(Vector3.zero);
        Assert.That(player.ResetForExploration(1, position ?? new Vector3(0f, 0.05f, 0f)), Is.True);
        return player;
    }

    private Collider CreateBox(string name, Vector3 position, Vector3 scale)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Track(box);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = scale;
        Physics.SyncTransforms();
        return box.GetComponent<Collider>();
    }

    private void SetKeys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    }

    private static bool IsSettled(PlayerController player)
    {
        return player.IsGrounded && Mathf.Abs(player.Body.linearVelocity.y) < 0.1f
            && Mathf.Abs(player.Body.position.y) < 0.03f;
    }

    private static IEnumerator WaitFor(Func<bool> condition, string message, float timeout = 2f)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline) { yield return null; }
        Assert.That(condition(), Is.True, message);
    }

    private static IEnumerator StayTrueFor(Func<bool> condition, string message, float seconds = 0.2f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        do
        {
            yield return null;
            Assert.That(condition(), Is.True, message);
        }
        while (Time.realtimeSinceStartup < deadline);
    }
}
#endif
