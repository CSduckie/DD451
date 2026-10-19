#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class PlayerJumpGraceTests : PlayerPlayModeTestBase
{
    private Keyboard keyboard;

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
    public IEnumerator ReleasedTapBeforeLandingIsBufferedAndLaunchesOnlyOnce()
    {
        CreateFloor(0f);
        PlayerController player = CreateGracePlayer();
        Assert.That(player.ResetForExploration(1, new Vector3(0f, 0.32f, 0f)), Is.True);
        player.Body.linearVelocity = Vector3.down * 3f;
        Assert.That(player.IsGrounded, Is.False);

        // Both real device events arrive while airborne: holding Space is not needed.
        TapJump();
        Assert.That(keyboard.spaceKey.isPressed, Is.False);
        int launches = 0;
        float previousVelocity = player.Body.linearVelocity.y;
        float deadline = Time.time + 2f;
        while (Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
            float velocity = player.Body.linearVelocity.y;
            if (velocity > player.config.jumpSpeed * 0.65f && previousVelocity <= 0.5f) { launches++; }
            previousVelocity = velocity;
        }

        Assert.That(launches, Is.EqualTo(1), "A released tap inside the landing buffer must launch once.");
        Assert.That(player.IsGrounded, Is.True);
        Assert.That(player.JumpRequested, Is.False, "A successful jump consumes its buffered request.");
    }

    [UnityTest]
    public IEnumerator TapTooFarBeforeLandingExpiresWithoutLaunching()
    {
        CreateFloor(0f);
        PlayerController player = CreateGracePlayer();
        Assert.That(player.ResetForExploration(1, new Vector3(0f, 3f, 0f)), Is.True);
        TapJump();
        float deadline = Time.time + 2f;
        bool reachedGround = false;
        while (Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
            Assert.That(player.Body.linearVelocity.y, Is.LessThan(0.5f),
                "An expired airborne tap must not produce a landing jump.");
            reachedGround |= player.IsGrounded && Mathf.Abs(player.Body.linearVelocity.y) < 0.2f;
        }
        Assert.That(reachedGround, Is.True, "This test must actually reach the floor after the buffer expires.");
        Assert.That(player.JumpRequested, Is.False);
    }

    [UnityTest]
    public IEnumerator WalkingOffEdgeAllowsOneCoyoteJumpButNeverAnAirborneSecondJump()
    {
        CreateFloor(-6f);
        CreateBox("Coyote test platform", new Vector3(0f, 2.5f, 0f), new Vector3(2f, 1f, 4f));
        PlayerController player = CreateGracePlayer();
        Assert.That(player.ResetForExploration(1, new Vector3(0f, 3.05f, 0f)), Is.True);
        yield return WaitForStableGround(player);
        yield return WalkOffPlatform(player);

        Assert.That(player.CanJump, Is.True, "Walking off an edge starts the coyote window.");
        TapJump();
        float deadline = Time.time + 0.25f;
        while (player.Body.linearVelocity.y <= 1f && Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
        }
        Assert.That(player.Body.linearVelocity.y, Is.GreaterThan(player.config.jumpSpeed * 0.65f));
        Assert.That(player.IsGrounded, Is.False);
        Assert.That(player.CanJump, Is.False, "Jumping immediately consumes the coyote opportunity.");

        float firstJumpVelocity = player.Body.linearVelocity.y;
        TapJump();
        deadline = Time.time + 0.18f;
        while (Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
            Assert.That(player.IsGrounded, Is.False);
            Assert.That(player.Body.linearVelocity.y, Is.LessThanOrEqualTo(firstJumpVelocity + 0.05f),
                "A second tap cannot reset vertical speed using the original coyote window.");
            Assert.That(player.CanJump, Is.False);
        }
        Assert.That(player.JumpRequested, Is.False);
    }

    [UnityTest]
    public IEnumerator WalkingOffEdgePastCoyoteWindowDoesNotAllowJump()
    {
        CreateFloor(-6f);
        CreateBox("Expired coyote platform", new Vector3(0f, 2.5f, 0f), new Vector3(2f, 1f, 4f));
        PlayerController player = CreateGracePlayer();
        Assert.That(player.ResetForExploration(1, new Vector3(0f, 3.05f, 0f)), Is.True);
        yield return WaitForStableGround(player);
        yield return WalkOffPlatform(player);
        yield return new WaitForSeconds(player.config.coyoteTimeSeconds + 0.08f);

        Assert.That(player.IsGrounded, Is.False);
        Assert.That(player.CanJump, Is.False);
        TapJump();
        float deadline = Time.time + 0.25f;
        while (Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
            Assert.That(player.Body.linearVelocity.y, Is.LessThan(0f),
                "An airborne tap after the coyote window must leave the player falling.");
        }
    }

    [UnityTest]
    public IEnumerator BackpackDeathDisableAndRoundResetDiscardPendingLandingJump()
    {
        CreateFloor(0f);
        PlayerController player = CreateGracePlayer();
        // A long buffer makes clearing observable: ordinary timeout cannot hide stale input.
        player.config.jumpBufferSeconds = 2f;
        int roundId = 0;
        for (int clearMode = 0; clearMode < 4; clearMode++)
        {
            Assert.That(player.ResetForExploration(++roundId, new Vector3(0f, 1f, 0f)), Is.True);
            TapJump();
            yield return null;
            Assert.That(player.JumpRequested, Is.True, "Each case must begin with an actual buffered input.");

            switch (clearMode)
            {
                case 0:
                    player.SetBackpackOpen(true);
                    player.SetBackpackOpen(false);
                    break;
                case 1:
                    Assert.That(player.health.TakeDamage(100), Is.True);
                    Assert.That(player.IsDead, Is.True);
                    Assert.That(player.JumpRequested, Is.False);
                    Assert.That(player.ResetForExploration(++roundId, new Vector3(0f, 1f, 0f)), Is.True);
                    break;
                case 2:
                    // Disable the controller alone; the real input reader remains active.
                    player.enabled = false;
                    player.enabled = true;
                    break;
                case 3:
                    Assert.That(player.ResetForExploration(++roundId, new Vector3(0f, 0.05f, 0f)), Is.True);
                    break;
            }

            Assert.That(player.JumpRequested, Is.False, "The clearing entry point must discard pending input.");
            float deadline = Time.time + 0.8f;
            while (Time.time < deadline)
            {
                yield return new WaitForFixedUpdate();
                Assert.That(player.Body.linearVelocity.y, Is.LessThan(0.5f),
                    "Cleared input must not replay after unlocking, reenable, death or round reset (case " + clearMode + ").");
            }
            Assert.That(player.IsGrounded, Is.True);
        }
    }

    private PlayerController CreateGracePlayer()
    {
        PlayerController player = CreatePlayer(Vector3.zero);
        player.config.gravityScale = 1f;
        player.config.fallMultiplier = 1f;
        player.config.lowJumpMultiplier = 1f;
        player.config.maxFallSpeed = 30f;
        player.config.jumpBufferSeconds = 0.12f;
        player.config.coyoteTimeSeconds = 0.12f;
        return player;
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
        Assert.That(stableFrames, Is.EqualTo(3), "The edge test must begin standing on its platform.");
    }

    private IEnumerator WalkOffPlatform(PlayerController player)
    {
        SetKeys(Key.D);
        float deadline = Time.time + 2f;
        while (player.IsGrounded && Time.time < deadline) { yield return new WaitForFixedUpdate(); }
        SetKeys();
        Assert.That(player.IsGrounded, Is.False, "The player must physically walk beyond the platform.");
        Assert.That(player.Body.position.x, Is.GreaterThan(1f));
        Assert.That(player.Body.linearVelocity.y, Is.LessThanOrEqualTo(0.1f));
    }

    private void TapJump()
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        InputSystem.Update();
    }

    private void SetKeys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
    }

    private void CreateFloor(float topY)
    {
        CreateBox("Jump grace floor", new Vector3(0f, topY - 0.5f, 0f), new Vector3(100f, 1f, 8f));
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
}
#endif
