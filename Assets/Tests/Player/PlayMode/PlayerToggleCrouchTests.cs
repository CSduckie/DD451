#if UNITY_EDITOR
using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

// 经正式动作绑定、InputReader、状态机和3D碰撞体验证；不直接写蹲伏状态来代替输入。
public class PlayerToggleCrouchTests : PlayerPlayModeTestBase
{
    private Keyboard keyboard;

    [SetUp]
    public void AddToggleKeyboard()
    {
        keyboard = InputSystem.AddDevice<Keyboard>();
    }

    [TearDown]
    public void RemoveToggleKeyboard()
    {
        if (keyboard != null && keyboard.added) { InputSystem.RemoveDevice(keyboard); }
        keyboard = null;
    }

    [Test]
    public void ShippingAndNewConfigurationsDefaultToToggle()
    {
        PlayerConfigSO shipping = AssetDatabase.LoadAssetAtPath<PlayerConfigSO>("Assets/Data/Player/PlayerConfig.asset");
        Assert.That(shipping, Is.Not.Null);
        Assert.That(shipping.crouchInputMode, Is.EqualTo(CrouchInputMode.Toggle));
        PlayerConfigSO fresh = ScriptableObject.CreateInstance<PlayerConfigSO>();
        Track(fresh);
        Assert.That(fresh.crouchInputMode, Is.EqualTo(CrouchInputMode.Toggle));
        InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Input/GameInputActions.inputactions");
        bool hasC = false;
        foreach (InputBinding binding in actions.FindAction("Player/Crouch", true).bindings)
        {
            if (binding.path == "<Keyboard>/c" && binding.groups.Contains("Keyboard&Mouse")) { hasC = true; }
        }
        Assert.That(hasC, Is.True, "The default key remains C in the shared input asset.");
    }

    [UnityTest]
    public IEnumerator TogglePressCrouchesReleaseKeepsPoseAndSecondPressStands()
    {
        PlayerController player = PreparePlayer();
        yield return WaitFor(() => IsSettled(player), "Player must settle on the floor.");
        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        float feet = capsule.bounds.min.y;
        SetKeys(Key.C);
        yield return WaitFor(() => player.IsCrouching, "First C press must crouch.");
        Assert.That(player.inputReader.CrouchHeld, Is.True);
        Assert.That(capsule.height, Is.EqualTo(2f * player.config.crouchVisualHeight).Within(0.001f));
        Assert.That(capsule.bounds.min.y, Is.EqualTo(feet).Within(0.03f));
        yield return StayTrueFor(() => player.IsCrouching, "Holding C must not repeat the toggle.");
        SetKeys();
        yield return StayTrueFor(() => player.IsCrouching && !player.inputReader.CrouchHeld,
            "Releasing C keeps the pose; CrouchHeld still reports the physical button.");
        SetKeys(Key.C);
        yield return WaitFor(() => !player.IsCrouching, "Second C press must stand.");
        Assert.That(capsule.height, Is.EqualTo(2f).Within(0.001f));
        yield return StayTrueFor(() => !player.IsCrouching, "Holding the second press must keep standing.");
        SetKeys();
    }

    [UnityTest]
    public IEnumerator HoldOptionStillCrouchesOnlyWhileTheBoundButtonIsHeld()
    {
        PlayerController player = PreparePlayer();
        player.config.crouchInputMode = CrouchInputMode.Hold;
        yield return WaitFor(() => IsSettled(player), "Player must settle on the floor.");
        SetKeys(Key.C);
        yield return WaitFor(() => player.IsCrouching && player.inputReader.CrouchHeld, "Hold must crouch.");
        SetKeys();
        yield return WaitFor(() => !player.IsCrouching && !player.inputReader.CrouchHeld, "Release must stand in Hold mode.");
    }

    [UnityTest]
    public IEnumerator BackpackIgnoresCWithoutChangingTheIntentRestoredOnDamageClose()
    {
        PlayerController player = PreparePlayer();
        yield return WaitFor(() => IsSettled(player), "Player must settle on the floor.");
        yield return TapCrouch();
        Assert.That(player.IsCrouching, Is.True);
        player.SetBackpackOpen(true);
        yield return TapCrouch();
        Assert.That(player.IsCrouching, Is.True, "C during the open bag must not change pose or queue an intent.");
        Action<int> closeOnDamage = damage => player.SetBackpackOpen(false);
        player.health.NonLethalDamaged += closeOnDamage;
        try
        {
            Assert.That(player.health.TakeDamage(1), Is.True);
            Assert.That(player.IsBackpackOpen, Is.False);
            yield return StayTrueFor(() => player.IsCrouching, "Damage closes the bag but preserves the original toggle intent.");
        }
        finally { player.health.NonLethalDamaged -= closeOnDamage; }
        yield return TapCrouch();
        Assert.That(player.IsCrouching, Is.False, "One fresh C press must now stand.");
        player.SetBackpackOpen(true);
        yield return TapCrouch();
        player.SetBackpackOpen(false);
        yield return StayTrueFor(() => !player.IsCrouching, "C ignored in a standing bag must not replay when it closes.");
    }

    [UnityTest]
    public IEnumerator LowCeilingDefersToggleStandRequestUntilThePlayerWalksOut()
    {
        PlayerController player = PreparePlayer(new Vector3(-1f, 0.05f, 0f));
        CreateBox("Toggle low passage", new Vector3(2.5f, 1.7f, 0f), new Vector3(4f, 0.4f, 3f));
        yield return WaitFor(() => IsSettled(player), "Player must settle beside the low passage.");
        yield return TapCrouch();
        SetKeys(Key.D);
        yield return WaitFor(() => player.Body.position.x > 1.2f, "Crouched collider must enter the passage.", 5f);
        yield return TapCrouch();
        SetKeys(Key.Space);
        yield return StayTrueFor(() => player.IsCrouching && player.IsGrounded && !player.CanJump,
            "Second C requests standing, but a low ceiling must keep the collider crouched and block jumping.");
        SetKeys(Key.D);
        yield return WaitFor(() => player.Body.position.x > 5f && !player.IsCrouching,
            "Walking out must complete the pending stand request without a third C press.", 5f);
        SetKeys();
        Assert.That(player.GetComponent<CapsuleCollider>().height, Is.EqualTo(2f).Within(0.001f));
    }

    [UnityTest]
    public IEnumerator TransitionAndReenableResetIntentWithoutReplayingAStillHeldButton()
    {
        PlayerController player = PreparePlayer();
        yield return WaitFor(() => IsSettled(player), "Player must settle on the floor.");
        SetKeys(Key.C);
        yield return WaitFor(() => player.IsCrouching, "C must crouch before the transition.");
        player.SetSceneTransitionBlocked(true);
        yield return null;
        Assert.That(player.RelocateWithinRun(new Vector3(2f, 0.05f, 0f)), Is.True);
        player.SetSceneTransitionBlocked(false);
        yield return StayTrueFor(() => !player.IsCrouching, "Transition must clear old intent even while the old key remains held.");
        SetKeys();
        yield return null;
        yield return TapCrouch();
        Assert.That(player.IsCrouching, Is.True, "A fresh press must still work after transition.");
        yield return TapCrouch();
        Assert.That(player.IsCrouching, Is.False);
        SetKeys(Key.C);
        yield return WaitFor(() => player.IsCrouching, "C must crouch before disable.");
        player.gameObject.SetActive(false);
        yield return null;
        player.gameObject.SetActive(true);
        yield return StayTrueFor(() => !player.IsCrouching, "Reenable must not turn an old held C into a fresh toggle.");
        SetKeys();
        yield return null;
        yield return TapCrouch();
        Assert.That(player.IsCrouching, Is.True, "Fresh input must work after reenable.");
    }

    [UnityTest]
    public IEnumerator NewRoundAndSameRoundDeathRecoveryResetCrouchIntent()
    {
        PlayerController player = PreparePlayer();
        yield return WaitFor(() => IsSettled(player), "Player must settle on the floor.");
        SetKeys(Key.C);
        yield return WaitFor(() => player.IsCrouching, "C must crouch before a new round.");
        Assert.That(player.ResetForExploration(2, new Vector3(2f, 0.05f, 0f)), Is.True);
        yield return StayTrueFor(() => !player.IsCrouching, "New round must reset the pose and ignore an old held key.");
        SetKeys();
        yield return null;
        SetKeys(Key.C);
        yield return WaitFor(() => player.IsCrouching, "Fresh C must crouch before death.");
        Assert.That(player.health.TakeDamage(100), Is.True);
        Assert.That(player.IsDead, Is.True);
        player.SetSceneTransitionBlocked(true);
        Assert.That(player.RecoverAfterDeathInShelter(new Vector3(4f, 0.05f, 0f)), Is.True);
        player.SetSceneTransitionBlocked(false);
        Assert.That(player.health.CurrentRound, Is.EqualTo(2), "Recovery must not start an extra round.");
        Assert.That(player.IsDead, Is.False);
        yield return StayTrueFor(() => !player.IsCrouching, "Death recovery must clear the old crouch intent.");
        SetKeys();
        yield return null;
        yield return TapCrouch();
        Assert.That(player.IsCrouching, Is.True, "A fresh press must work after recovery.");
    }

    [UnityTest]
    public IEnumerator ToggleUsesTheReboundCrouchActionInsteadOfHardCodingC()
    {
        PlayerController player = PreparePlayer();
        player.inputReader.enabled = false;
        InputAction crouch = player.inputReader.inputActions.FindAction("Player/Crouch", true);
        for (int i = 0; i < crouch.bindings.Count; i++)
        {
            if (crouch.bindings[i].groups.Contains("Keyboard&Mouse"))
            {
                crouch.ApplyBindingOverride(i, "<Keyboard>/v");
            }
        }
        player.inputReader.enabled = true;
        yield return WaitFor(() => IsSettled(player), "Player must settle on the floor.");
        yield return TapCrouch();
        Assert.That(player.IsCrouching, Is.False, "The old C binding must stop controlling crouch.");
        SetKeys(Key.V);
        yield return WaitFor(() => player.IsCrouching && player.inputReader.CrouchHeld, "Rebound V must crouch.");
        SetKeys();
        yield return StayTrueFor(() => player.IsCrouching && !player.inputReader.CrouchHeld,
            "The rebound action must retain Toggle semantics.");
        SetKeys(Key.V);
        yield return WaitFor(() => !player.IsCrouching, "Second V must stand.");
        SetKeys();
    }

    private PlayerController PreparePlayer(Vector3? position = null)
    {
        CreateBox("Toggle physics floor", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 8f));
        PlayerController player = CreatePlayer(Vector3.zero);
        player.config.crouchInputMode = CrouchInputMode.Toggle;
        Assert.That(player.ResetForExploration(1, position ?? new Vector3(0f, 0.05f, 0f)), Is.True);
        return player;
    }

    private void CreateBox(string name, Vector3 position, Vector3 scale)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Track(box);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = scale;
        Physics.SyncTransforms();
    }

    private void SetKeys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    }

    private IEnumerator TapCrouch()
    {
        SetKeys(Key.C);
        yield return null;
        yield return null;
        SetKeys();
        yield return null;
        yield return null;
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
