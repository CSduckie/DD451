#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class PlayerReferenceMovementTests : PlayerPlayModeTestBase
{
    private Keyboard keyboard;
    private float recordedJumpHeight;

    [SetUp]
    public void AddTestKeyboard()
    {
        keyboard = InputSystem.AddDevice<Keyboard>();
    }

    [TearDown]
    public void RemoveTestKeyboard()
    {
        if (keyboard != null && keyboard.added) { InputSystem.RemoveDevice(keyboard); }
        keyboard = null;
    }

    [UnityTest]
    public IEnumerator VelocityEasesTowardInputSlowsAfterReleaseAndCanReverseInAir()
    {
        CreateFloor();
        PlayerController player = CreatePlayer(Vector3.zero);
        player.config.moveSpeed = 5f;
        // Slower local rates expose the shape of the real physics response over several steps.
        player.config.acceleration = 8f;
        player.config.deceleration = 8f;
        Assert.That(player.ResetForExploration(1, new Vector3(0f, 0.05f, 0f)), Is.True);
        yield return WaitForStableGround(player);

        SetKeys(Key.D);
        List<float> acceleratingSpeeds = new List<float>();
        float deadline = Time.time + 1f;
        while (acceleratingSpeeds.Count < 8 && Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
            if (player.Body.linearVelocity.x > 0.01f) { acceleratingSpeeds.Add(player.Body.linearVelocity.x); }
        }
        Assert.That(acceleratingSpeeds, Has.Count.EqualTo(8));
        Assert.That(acceleratingSpeeds[0], Is.InRange(0.01f, player.config.moveSpeed * 0.75f),
            "Pressing a direction must accelerate instead of immediately assigning full speed.");
        for (int i = 1; i < acceleratingSpeeds.Count; i++)
        {
            Assert.That(acceleratingSpeeds[i], Is.GreaterThan(acceleratingSpeeds[i - 1]));
            Assert.That(acceleratingSpeeds[i], Is.LessThanOrEqualTo(player.config.moveSpeed + 0.05f));
        }
        float firstGain = acceleratingSpeeds[1] - acceleratingSpeeds[0];
        float lastGain = acceleratingSpeeds[7] - acceleratingSpeeds[6];
        Assert.That(lastGain, Is.LessThan(firstGain * 0.9f),
            "The reference response eases toward its target; equal per-step acceleration is insufficient.");

        deadline = Time.time + 2f;
        while (player.Body.linearVelocity.x < player.config.moveSpeed - 0.15f && Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
        }
        Assert.That(player.Body.linearVelocity.x, Is.EqualTo(player.config.moveSpeed).Within(0.15f));
        float runningVelocity = player.Body.linearVelocity.x;
        float releasedAtX = player.Body.position.x;
        SetKeys();
        yield return null;
        yield return new WaitForFixedUpdate();
        Assert.That(player.MoveInput, Is.EqualTo(0f));
        Assert.That(player.Body.linearVelocity.x, Is.InRange(0.1f, runningVelocity - 0.01f),
            "Releasing movement must decelerate while retaining some momentum.");
        Assert.That(player.Body.position.x, Is.GreaterThan(releasedAtX));
        deadline = Time.time + 2f;
        while (Mathf.Abs(player.Body.linearVelocity.x) > 0.05f && Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
        }
        Assert.That(player.Body.linearVelocity.x, Is.EqualTo(0f).Within(0.05f));

        SetKeys(Key.D, Key.Space);
        deadline = Time.time + 0.4f;
        while ((player.Body.linearVelocity.y < 1f || player.Body.linearVelocity.x < 2f) && Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
        }
        Assert.That(player.IsGrounded, Is.False);
        Assert.That(player.Body.linearVelocity.x, Is.GreaterThan(2f));
        float rightwardVelocity = player.Body.linearVelocity.x;
        SetKeys(Key.A, Key.Space);
        yield return null;
        yield return new WaitForFixedUpdate();
        Assert.That(player.MoveInput, Is.LessThan(0f));
        Assert.That(player.Body.linearVelocity.x, Is.LessThan(rightwardVelocity));
        Assert.That(player.Body.linearVelocity.x, Is.GreaterThan(-player.config.moveSpeed * 0.8f),
            "Air control must turn the existing velocity around progressively.");
        deadline = Time.time + 0.4f;
        while (player.Body.linearVelocity.x > -1f && Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
        }
        Assert.That(player.IsGrounded, Is.False, "Reversal must complete before landing.");
        Assert.That(player.Body.linearVelocity.x, Is.LessThan(-1f));
        Assert.That(player.Body.position.z, Is.EqualTo(player.config.planeZ).Within(0.001f));
        SetKeys();
    }

    [UnityTest]
    public IEnumerator ReferenceGravityAndJumpSpeedMakeReleasedJumpLowerThanHeldJump()
    {
        CreateFloor();
        PlayerController player = CreateReferencePlayer();
        Assert.That(player.ResetForExploration(1, new Vector3(0f, 0.05f, 0f)), Is.True);
        yield return WaitForStableGround(player);
        yield return MeasureJump(player, true);
        float heldHeight = recordedJumpHeight;

        Assert.That(player.ResetForExploration(2, new Vector3(0f, 0.05f, 0f)), Is.True);
        yield return WaitForStableGround(player);
        yield return MeasureJump(player, false);
        float releasedHeight = recordedJumpHeight;

        Assert.That(heldHeight, Is.GreaterThan(1.6f), "The 13m/s reference jump must clear a useful platform height.");
        Assert.That(releasedHeight, Is.GreaterThan(0.3f), "A short press must still launch a real jump.");
        Assert.That(releasedHeight, Is.LessThan(heldHeight * 0.75f),
            "Releasing Space during ascent must apply the low-jump multiplier and produce a shorter arc.");
        Assert.That(heldHeight - releasedHeight, Is.GreaterThan(0.5f));
    }

    [UnityTest]
    public IEnumerator RealFreeFallReachesButDoesNotExceedConfiguredTerminalSpeed()
    {
        CreateFloor();
        PlayerController player = CreateReferencePlayer();
        Assert.That(player.ResetForExploration(1, new Vector3(0f, 50f, 0f)), Is.True);
        int terminalFrames = 0;
        float deadline = Time.time + 1.2f;
        while (Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
            float downwardSpeed = -player.Body.linearVelocity.y;
            Assert.That(player.IsGrounded, Is.False);
            Assert.That(downwardSpeed, Is.LessThanOrEqualTo(player.config.maxFallSpeed + 0.05f),
                "The limit must hold after actual physics, including gravity added during the step.");
            if (downwardSpeed >= player.config.maxFallSpeed - 0.05f) { terminalFrames++; }
        }
        Assert.That(terminalFrames, Is.GreaterThanOrEqualTo(3),
            "Real gravity must reach and sustain the 12m/s fall limit.");
        Assert.That(player.Body.position.y, Is.LessThan(45f));
    }

    private PlayerController CreateReferencePlayer()
    {
        PlayerController player = CreatePlayer(Vector3.zero);
        player.config.jumpSpeed = 13f;
        player.config.gravityScale = 3.5f;
        player.config.fallMultiplier = 2.5f;
        player.config.lowJumpMultiplier = 3f;
        player.config.maxFallSpeed = 12f;
        return player;
    }

    private IEnumerator MeasureJump(PlayerController player, bool holdThroughAscent)
    {
        float groundY = player.Body.position.y;
        SetKeys(Key.Space);
        float deadline = Time.time + 0.5f;
        while (player.Body.linearVelocity.y <= 1f && Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
        }
        Assert.That(player.Body.linearVelocity.y, Is.GreaterThan(1f), "The real key binding must launch the jump.");
        float highestY = player.Body.position.y;
        if (!holdThroughAscent) { SetKeys(); }
        int settledFrames = 0;
        deadline = Time.time + 2f;
        while (settledFrames < 3 && Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
            highestY = Mathf.Max(highestY, player.Body.position.y);
            settledFrames = player.IsGrounded && Mathf.Abs(player.Body.linearVelocity.y) < 0.2f
                ? settledFrames + 1 : 0;
        }
        Assert.That(settledFrames, Is.EqualTo(3), "Each trajectory must complete with a physical landing.");
        recordedJumpHeight = highestY - groundY;
        SetKeys();
        yield return null;
    }

    private IEnumerator WaitForStableGround(PlayerController player)
    {
        int stableFrames = 0;
        float deadline = Time.time + 2f;
        while (stableFrames < 3 && Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
            stableFrames = player.IsGrounded && Mathf.Abs(player.Body.linearVelocity.y) < 0.2f
                ? stableFrames + 1 : 0;
        }
        Assert.That(stableFrames, Is.EqualTo(3));
    }

    private void SetKeys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
    }

    private void CreateFloor()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Track(floor);
        floor.name = "Reference movement floor";
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(100f, 1f, 8f);
        Physics.SyncTransforms();
    }
}
#endif
