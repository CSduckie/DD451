using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class PlayerFlowEntryTests
{
    private GameObject playerObject;
    private PlayerController player;
    private PlayerConfigSO config;

    [SetUp]
    public void SetUp()
    {
        config = ScriptableObject.CreateInstance<PlayerConfigSO>();
        config.maxHealth = 3;
        config.damageInvulnerabilitySeconds = 0f;
        config.planeZ = 2f;
        playerObject = new GameObject("Player flow entry test");
        player = playerObject.AddComponent<PlayerController>();
        player.config = config;
        player.health = playerObject.GetComponent<PlayerHealth>();
        player.health.config = config;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(playerObject);
        Object.DestroyImmediate(config);
    }

    [Test]
    public void FirstShelterPreparationKeepsRoundZeroAndCannotRefillExistingPlayer()
    {
        int changes = 0;
        bool readyWhenNotified = false;
        player.health.HealthChanged += (current, maximum) =>
        {
            changes++;
            readyWhenNotified = player.CanAct && player.health.CurrentRound == 0
                && player.Position == new Vector3(4f, 5f, 2f);
        };

        Assert.That(player.PrepareInitialShelter(new Vector3(4f, 5f, 99f)), Is.True);
        Assert.That(readyWhenNotified, Is.True);
        Assert.That(changes, Is.EqualTo(1));
        Assert.That(player.health.CurrentHealth, Is.EqualTo(3));
        Assert.That(player.health.CurrentRound, Is.Zero);
        Assert.That(player.IsDead, Is.False);
        Assert.That(player.Body.isKinematic, Is.False);

        player.health.TakeDamage(1);
        Assert.That(player.PrepareInitialShelter(Vector3.zero), Is.False);
        Assert.That(player.health.CurrentHealth, Is.EqualTo(2));
        Assert.That(player.health.CurrentRound, Is.Zero);
        Assert.That(player.Position, Is.EqualTo(new Vector3(4f, 5f, 2f)));
        Assert.That(player.ResetForExploration(1, Vector3.zero), Is.True);
    }

    [Test]
    public void RelocationPreservesHealthRoundAndInvulnerabilityWithoutHealthNotification()
    {
        config.damageInvulnerabilitySeconds = 10f;
        player.ResetForExploration(4, new Vector3(1f, 2f, 3f));
        player.health.TakeDamage(1);
        player.Body.linearVelocity = new Vector3(3f, 4f, 0f);
        int healthChanges = 0;
        int teleports = 0;
        Vector3 positionDelta = Vector3.zero;
        player.health.HealthChanged += (current, maximum) => healthChanges++;
        player.Teleported += delta => { teleports++; positionDelta = delta; };

        Assert.That(player.RelocateWithinRun(new Vector3(8f, 7f, -10f)), Is.True);
        Assert.That(player.Position, Is.EqualTo(new Vector3(8f, 7f, 2f)));
        Assert.That(player.Body.linearVelocity, Is.EqualTo(Vector3.zero));
        Assert.That(player.health.CurrentHealth, Is.EqualTo(2));
        Assert.That(player.health.CurrentRound, Is.EqualTo(4));
        Assert.That(player.health.TakeDamage(1), Is.False, "Moving home must not reset damage immunity.");
        Assert.That(healthChanges, Is.Zero);
        Assert.That(teleports, Is.EqualTo(1));
        Assert.That(positionDelta, Is.EqualTo(new Vector3(7f, 5f, 0f)));
    }

    [Test]
    public void DeadPlayerCanRelocateButUnlockAndFirstShelterEntryCannotReviveIt()
    {
        player.ResetForExploration(2, Vector3.zero);
        player.health.TakeDamage(100);
        player.SetSceneTransitionBlocked(true);

        Assert.That(player.RelocateWithinRun(new Vector3(9f, 3f, -4f)), Is.True);
        player.SetBackpackOpen(true);
        player.SetBackpackOpen(false);
        player.SetSceneTransitionBlocked(false);

        Assert.That(player.PrepareInitialShelter(Vector3.zero), Is.False);
        Assert.That(player.IsDead, Is.True);
        Assert.That(player.health.CurrentHealth, Is.Zero);
        Assert.That(player.health.CurrentRound, Is.EqualTo(2));
        Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Die));
        Assert.That(player.CanAct, Is.False);
        Assert.That(player.interaction.InputBlocked, Is.True);
        Assert.That(player.Body.isKinematic, Is.False, "Existing death physics still allows gravity.");
    }

    [Test]
    public void SceneLockSurvivesFirstPreparationBackpackClosingAndNewRoundReset()
    {
        player.SetSceneTransitionBlocked(true);
        Assert.That(player.PrepareInitialShelter(Vector3.zero), Is.True);
        player.SetBackpackOpen(true);
        player.SetBackpackOpen(false);
        Assert.That(player.ResetForExploration(1, new Vector3(7f, 3f, 0f)), Is.True);
        player.ApplyHorizontalVelocity(5f);
        Assert.That(player.ExecuteJump(), Is.False);

        Assert.That(player.IsSceneTransitionBlocked, Is.True);
        Assert.That(player.Body.isKinematic, Is.True);
        Assert.That(player.CanAct, Is.False);
        Assert.That(player.interaction.InputBlocked, Is.True);
        Assert.That(player.health.CurrentRound, Is.EqualTo(1));
        Assert.That(player.health.CurrentHealth, Is.EqualTo(3));
        LogAssert.NoUnexpectedReceived();

        player.SetSceneTransitionBlocked(false);
        Assert.That(player.Body.isKinematic, Is.False);
        Assert.That(player.CanAct, Is.True);
        Assert.That(player.interaction.InputBlocked, Is.False);
    }

    [Test]
    public void InvalidPositionAndUnpreparedRelocationDoNotChangePreparationOrRound()
    {
        Assert.That(player.RelocateWithinRun(Vector3.one), Is.False);
        Assert.That(player.PrepareInitialShelter(new Vector3(float.NaN, 0f, 0f)), Is.False);
        Assert.That(player.ResetForExploration(1, new Vector3(0f, float.PositiveInfinity, 0f)), Is.False);
        Assert.That(player.health.IsPrepared, Is.False);
        Assert.That(player.health.CurrentRound, Is.Zero);
        player.SetSceneTransitionBlocked(false);
        Assert.That(player.Body.isKinematic, Is.True);
        Assert.That(player.CanAct, Is.False);
    }
}
