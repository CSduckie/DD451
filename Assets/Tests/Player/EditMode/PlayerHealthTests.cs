using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class PlayerHealthTests
{
    private GameObject playerObject;
    private PlayerController player;
    private PlayerHealth health;
    private PlayerConfigSO config;

    [SetUp]
    public void SetUp()
    {
        config = ScriptableObject.CreateInstance<PlayerConfigSO>();
        config.maxHealth = 3;
        config.damageInvulnerabilitySeconds = 0f;
        config.planeZ = 2f;
        playerObject = new GameObject("PlayerHealth EditMode Test");
        player = playerObject.AddComponent<PlayerController>();
        player.config = config;
        health = playerObject.GetComponent<PlayerHealth>();
        health.config = config;
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(playerObject);
        UnityEngine.Object.DestroyImmediate(config);
    }

    [Test]
    public void CreationNeedsExplicitPositiveRoundAndDoesNotReportInitialDeath()
    {
        int changes = 0;
        int deaths = 0;
        health.HealthChanged += (current, maximum) => changes++;
        health.Died += () => deaths++;

        Assert.That(health.IsPrepared, Is.False);
        Assert.That(health.IsDead, Is.False);
        Assert.That(health.CurrentHealth, Is.Zero);
        health.Heal(int.MaxValue);
        Assert.That(health.TakeDamage(int.MaxValue), Is.False);
        Assert.That(player.ResetForExploration(0, Vector3.zero), Is.False);
        Assert.That(changes, Is.Zero);
        Assert.That(deaths, Is.Zero);

        Assert.That(player.ResetForExploration(1, Vector3.zero), Is.True);
        Assert.That(health.IsPrepared, Is.True);
        Assert.That(health.CurrentHealth, Is.EqualTo(3));
        Assert.That(health.CurrentRound, Is.EqualTo(1));
        Assert.That(changes, Is.EqualTo(1));
        Assert.That(deaths, Is.Zero);
    }

    [Test]
    public void HealingIsSynchronousClampedAndDoesNotMutateConfiguration()
    {
        player.ResetForExploration(1, Vector3.zero);
        string before = JsonUtility.ToJson(config);
        IHealthTarget target = health;
        Assert.That(health.TakeDamage(2), Is.True);
        Assert.That(target.CurrentHealth, Is.EqualTo(1));
        target.Heal(0);
        target.Heal(-100);
        Assert.That(target.CurrentHealth, Is.EqualTo(1));
        target.Heal(int.MaxValue);
        Assert.That(target.CurrentHealth, Is.EqualTo(target.MaxHealth));
        Assert.That(target.MaxHealth, Is.EqualTo(3));
        target.Heal(1);
        Assert.That(target.CurrentHealth, Is.EqualTo(3));
        Assert.That(JsonUtility.ToJson(config), Is.EqualTo(before));
    }

    [Test]
    public void InvulnerabilityBlocksDamageButAllowsHealingAndIsClearedByNewRound()
    {
        config.damageInvulnerabilitySeconds = 10f;
        player.ResetForExploration(1, Vector3.zero);
        Assert.That(health.TakeDamage(1), Is.True);
        Assert.That(health.TakeDamage(1), Is.False);
        health.Heal(1);
        Assert.That(health.CurrentHealth, Is.EqualTo(3));
        Assert.That(health.TakeDamage(1), Is.False);

        Assert.That(player.ResetForExploration(2, Vector3.zero), Is.True);
        Assert.That(health.TakeDamage(1), Is.True);
        Assert.That(health.CurrentHealth, Is.EqualTo(2));
    }

    [Test]
    public void LethalDamageEstablishesDeathBeforeNotificationsAndCannotReviveNormally()
    {
        player.ResetForExploration(1, Vector3.zero);
        int deaths = 0;
        int nonLethal = 0;
        bool deathStateObserved = false;
        health.NonLethalDamaged += damage => nonLethal += damage;
        health.Died += () => deaths++;
        health.HealthChanged += (current, maximum) =>
        {
            if (current == 0)
            {
                deathStateObserved = health.IsDead && player.CurrentState == PlayerState.Die && !player.CanAct;
            }
        };

        Assert.That(health.TakeDamage(1), Is.True);
        Assert.That(nonLethal, Is.EqualTo(1));
        Assert.That(health.TakeDamage(int.MaxValue), Is.True);
        Assert.That(health.CurrentHealth, Is.Zero);
        Assert.That(deathStateObserved, Is.True);
        Assert.That(deaths, Is.EqualTo(1));
        Assert.That(health.TakeDamage(1), Is.False);
        health.Heal(int.MaxValue);
        player.SetBackpackOpen(false);
        Assert.That(health.IsDead, Is.True);
        Assert.That(health.CurrentHealth, Is.Zero);
        Assert.That(player.CanAct, Is.False);
        Assert.That(deaths, Is.EqualTo(1));
        Assert.That(nonLethal, Is.EqualTo(1));
    }

    [Test]
    public void OnlyNewerRoundCanHealReviveOrMovePlayerAndNotificationSeesPreparedController()
    {
        player.ResetForExploration(4, new Vector3(1f, 5f, 99f));
        health.TakeDamage(1);
        Vector3 originalPosition = player.Body.position;
        Assert.That(player.ResetForExploration(4, Vector3.zero), Is.False);
        Assert.That(player.ResetForExploration(3, Vector3.zero), Is.False);
        Assert.That(health.CurrentHealth, Is.EqualTo(2));
        Assert.That(player.Body.position, Is.EqualTo(originalPosition));
        health.TakeDamage(10);
        player.SetBackpackOpen(true);
        Assert.That(player.ResetForExploration(4, Vector3.zero), Is.False);
        Assert.That(health.IsDead, Is.True);

        bool correctSnapshot = false;
        health.HealthChanged += (current, maximum) =>
        {
            correctSnapshot = current == maximum && !player.IsDead && !player.IsBackpackOpen
                && player.CurrentState != PlayerState.Die && player.Body.position == new Vector3(8f, 6f, 2f);
        };
        Assert.That(player.ResetForExploration(5, new Vector3(8f, 6f, -99f)), Is.True);
        Assert.That(correctSnapshot, Is.True);
        Assert.That(health.CurrentRound, Is.EqualTo(5));
        Assert.That(player.Body.linearVelocity, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void NotificationCannotReenterHealingDamageOrRoundPreparation()
    {
        player.ResetForExploration(1, Vector3.zero);
        bool damageAccepted = true;
        bool roundAccepted = true;
        int lastObserved = -1;
        health.HealthChanged += (current, maximum) =>
        {
            health.Heal(1);
            damageAccepted = health.TakeDamage(1);
            roundAccepted = player.ResetForExploration(2, new Vector3(50f, 50f, 50f));
        };
        health.HealthChanged += (current, maximum) => lastObserved = current;

        Assert.That(health.TakeDamage(1), Is.True);
        Assert.That(damageAccepted, Is.False);
        Assert.That(roundAccepted, Is.False);
        Assert.That(health.CurrentHealth, Is.EqualTo(2));
        Assert.That(lastObserved, Is.EqualTo(2));
        Assert.That(health.CurrentRound, Is.EqualTo(1));
        Assert.That(player.Body.position, Is.EqualTo(new Vector3(0f, 0f, 2f)));
    }

    [Test]
    public void FailedDisplayListenerDoesNotAbortSynchronousHealingOrOtherListeners()
    {
        player.ResetForExploration(1, Vector3.zero);
        health.TakeDamage(2);
        int lastObserved = -1;
        health.HealthChanged += (current, maximum) => throw new InvalidOperationException("observer failed");
        health.HealthChanged += (current, maximum) => lastObserved = current;
        LogAssert.Expect(LogType.Exception, new Regex("observer failed"));

        Assert.DoesNotThrow(() => health.Heal(int.MaxValue));
        Assert.That(health.CurrentHealth, Is.EqualTo(3));
        Assert.That(lastObserved, Is.EqualTo(3));
    }
}
