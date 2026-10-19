#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class PlayerSceneTransitionTests
{
    private GameObject playerHolder;
    private PlayerController player;
    private PlayerConfigSO config;
    private Keyboard keyboard;
    private InputSettings savedInputSettings;
    private InputSettings testInputSettings;
    private bool savedRunInBackground;

    [SetUp]
    public void SetUp()
    {
        if (Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
        {
            Assert.Ignore("Run Player scene transition tests in the Test Runner's clean scene.");
        }

        savedInputSettings = InputSystem.settings;
        savedRunInBackground = Application.runInBackground;
        testInputSettings = Object.Instantiate(savedInputSettings);
        testInputSettings.hideFlags = HideFlags.HideAndDontSave;
        testInputSettings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        testInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings = testInputSettings;
        Application.runInBackground = true;
        keyboard = InputSystem.AddDevice<Keyboard>();

        playerHolder = new GameObject("Player scene transition test holder");
        playerHolder.SetActive(false);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player.prefab");
        Assert.That(prefab, Is.Not.Null);
        player = Object.Instantiate(prefab, playerHolder.transform).GetComponent<PlayerController>();
        config = Object.Instantiate(player.config);
        config.damageInvulnerabilitySeconds = 0f;
        config.jumpBufferSeconds = 2f;
        config.planeZ = 0f;
        player.config = config;
        player.health.config = config;
        playerHolder.SetActive(true);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (playerHolder != null) { Object.Destroy(playerHolder); }
        yield return null;
        if (config != null) { Object.Destroy(config); }
        if (keyboard != null && keyboard.added) { InputSystem.RemoveDevice(keyboard); }
        if (savedInputSettings != null)
        {
            InputSystem.settings = savedInputSettings;
            Application.runInBackground = savedRunInBackground;
        }
        if (testInputSettings != null) { Object.Destroy(testInputSettings); }
        yield return null;
    }

    [UnityTest]
    public IEnumerator TransitionFreezesPhysicalStateAndDiscardsInputsBeforeUnlock()
    {
        Assert.That(player.PrepareInitialShelter(new Vector3(0f, 10f, 0f)), Is.True);
        player.Body.linearVelocity = new Vector3(4f, 2f, 0f);
        SetKeys(Key.Space, Key.E);
        yield return null;
        Assert.That(player.JumpRequested, Is.True, "The test starts with a real buffered jump.");
        player.SetSceneTransitionBlocked(true);
        Vector3 frozenPosition = player.Body.position;
        SetKeys(Key.D, Key.Space, Key.E);
        player.SetBackpackOpen(true);
        player.SetBackpackOpen(false);

        for (int i = 0; i < 4; i++) { yield return new WaitForFixedUpdate(); }
        Assert.That(player.Body.position, Is.EqualTo(frozenPosition));
        Assert.That(player.Body.isKinematic, Is.True);
        Assert.That(player.JumpRequested, Is.False);
        Assert.That(player.interaction.InputBlocked, Is.True);

        SetKeys();
        SetKeys(Key.Space, Key.E);
        player.SetSceneTransitionBlocked(false);
        Assert.That(player.inputReader.ConsumeJump(), Is.False);
        Assert.That(player.inputReader.ConsumeInteract(), Is.False);
        SetKeys();
        yield return new WaitForFixedUpdate();
        Assert.That(player.Body.linearVelocity.y, Is.LessThan(0f));
        Assert.That(player.Body.linearVelocity.x, Is.EqualTo(0f).Within(0.01f));
        Assert.That(player.CanAct, Is.True);

        SetKeys(Key.D);
        yield return null;
        yield return new WaitForFixedUpdate();
        Assert.That(player.Body.linearVelocity.x, Is.GreaterThan(0f), "Fresh input works after unlocking.");
        LogAssert.NoUnexpectedReceived();
    }

    [UnityTest]
    public IEnumerator NewRoundWhileLockedStaysFrozenAndDeathIsPreservedOnReturn()
    {
        player.SetSceneTransitionBlocked(true);
        Assert.That(player.PrepareInitialShelter(new Vector3(0f, 10f, 0f)), Is.True);
        Assert.That(player.ResetForExploration(1, new Vector3(5f, 10f, 2f)), Is.True);
        Vector3 frozenPosition = player.Body.position;
        for (int i = 0; i < 4; i++) { yield return new WaitForFixedUpdate(); }
        Assert.That(player.Body.position, Is.EqualTo(frozenPosition));
        Assert.That(player.Body.isKinematic, Is.True);
        Assert.That(player.health.CurrentRound, Is.EqualTo(1));

        int deathNotifications = 0;
        int healthNotifications = 0;
        player.health.Died += () => deathNotifications++;
        player.health.HealthChanged += (current, maximum) => healthNotifications++;
        Assert.That(player.health.TakeDamage(1), Is.False);
        Assert.That(player.health.TakeDamage(100), Is.False);
        Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth));
        Assert.That(deathNotifications, Is.Zero);
        Assert.That(healthNotifications, Is.Zero);

        // 切场景锁先受理时拒绝伤害；解除锁后以真实致命伤害保留原死亡定位断言。
        player.SetSceneTransitionBlocked(false);
        Assert.That(player.health.TakeDamage(100), Is.True);
        Assert.That(deathNotifications, Is.EqualTo(1));
        player.SetSceneTransitionBlocked(true);
        Assert.That(player.RelocateWithinRun(new Vector3(12f, 10f, -9f)), Is.True);
        yield return new WaitForFixedUpdate();
        Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Die));
        Assert.That(player.Body.position, Is.EqualTo(new Vector3(12f, 10f, 0f)));
        player.enabled = false;
        player.enabled = true;
        Assert.That(player.Body.isKinematic, Is.True);
        player.SetSceneTransitionBlocked(false);
        yield return new WaitForFixedUpdate();

        Assert.That(player.IsDead, Is.True);
        Assert.That(player.CanAct, Is.False);
        Assert.That(player.health.CurrentHealth, Is.Zero);
        Assert.That(player.health.CurrentRound, Is.EqualTo(1));
        Assert.That(player.Body.isKinematic, Is.False);
        Assert.That(player.Body.linearVelocity.y, Is.LessThan(0f));
        LogAssert.NoUnexpectedReceived();
    }

    [UnityTest]
    public IEnumerator DeathRecoveryWaitsForNotificationsAndKeepsTheSameRoundLocked()
    {
        Assert.That(player.ResetForExploration(3, new Vector3(1f, 10f, 0f)), Is.True);
        Vector3 spawn = new Vector3(8f, 10f, 99f);
        Vector3 actualSpawn = new Vector3(8f, 10f, 0f);
        Vector3 deathPosition = player.Body.position;
        float standingHeight = player.GetComponent<CapsuleCollider>().height;
        player.SetCrouching(true);
        player.SetBackpackOpen(true);
        bool recoveryFromHealthChanged = true;
        bool recoveryFromDied = true;
        bool preparedSnapshotSeen = false;
        int healthNotifications = 0;
        int deathNotifications = 0;
        player.health.HealthChanged += (current, maximum) =>
        {
            healthNotifications++;
            if (current == 0)
            {
                player.SetSceneTransitionBlocked(true);
                recoveryFromHealthChanged = player.RecoverAfterDeathInShelter(spawn);
            }
            else
            {
                preparedSnapshotSeen = current == maximum && !player.IsDead
                    && player.CurrentState != PlayerState.Die && !player.IsBackpackOpen
                    && player.IsSceneTransitionBlocked && player.Body.isKinematic
                    && player.Body.position == actualSpawn && player.health.CurrentRound == 3;
            }
        };
        player.health.Died += () =>
        {
            deathNotifications++;
            recoveryFromDied = player.RecoverAfterDeathInShelter(spawn);
        };

        Assert.That(player.health.TakeDamage(100), Is.True);
        Assert.That(recoveryFromHealthChanged, Is.False);
        Assert.That(recoveryFromDied, Is.False);
        Assert.That(player.IsDead, Is.True);
        Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Die));
        Assert.That(player.Body.position, Is.EqualTo(deathPosition));
        Assert.That(player.health.CurrentRound, Is.EqualTo(3));
        Assert.That(healthNotifications, Is.EqualTo(1));
        yield return null;

        Assert.That(player.RecoverAfterDeathInShelter(spawn), Is.True);
        Assert.That(preparedSnapshotSeen, Is.True, "Observers must see the fully relocated but still locked player.");
        Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth));
        Assert.That(player.health.CurrentRound, Is.EqualTo(3));
        Assert.That(player.CanAct, Is.False);
        Assert.That(player.interaction.InputBlocked, Is.True);
        Assert.That(player.IsCrouching, Is.False);
        Assert.That(player.GetComponent<CapsuleCollider>().height, Is.EqualTo(standingHeight));
        Assert.That(player.ResetForExploration(3, Vector3.zero), Is.False);
        Assert.That(healthNotifications, Is.EqualTo(2));
        Assert.That(deathNotifications, Is.EqualTo(1));
        yield return new WaitForFixedUpdate();
        Assert.That(player.Body.position, Is.EqualTo(actualSpawn));

        player.SetSceneTransitionBlocked(false);
        Assert.That(player.CanAct, Is.True);
        SetKeys(Key.D);
        yield return null;
        yield return new WaitForFixedUpdate();
        Assert.That(player.Body.linearVelocity.x, Is.GreaterThan(0f));
        LogAssert.NoUnexpectedReceived();
    }

    [UnityTest]
    public IEnumerator DeathRecoveryRejectsUnpreparedLivingUnlockedInvalidAndRepeatedRequests()
    {
        Vector3 spawn = new Vector3(7f, 10f, 0f);
        player.SetSceneTransitionBlocked(true);
        Assert.That(player.RecoverAfterDeathInShelter(spawn), Is.False);
        Assert.That(player.PrepareInitialShelter(new Vector3(0f, 10f, 0f)), Is.True);
        Assert.That(player.RecoverAfterDeathInShelter(spawn), Is.False);
        Assert.That(player.health.CurrentRound, Is.Zero);
        Assert.That(player.ResetForExploration(2, new Vector3(1f, 10f, 0f)), Is.True);
        Assert.That(player.RecoverAfterDeathInShelter(spawn), Is.False);

        player.SetSceneTransitionBlocked(false);
        player.SetBackpackOpen(true);
        player.SetInteractionUiBlocked(true);
        Assert.That(player.health.TakeDamage(1), Is.True, "Backpack and UI locks must not grant transition immunity.");
        int woundedHealth = player.health.CurrentHealth;
        player.SetSceneTransitionBlocked(true);
        Vector3 originalPosition = player.Body.position;
        Assert.That(player.RecoverAfterDeathInShelter(spawn), Is.False);
        Assert.That(player.health.CurrentHealth, Is.EqualTo(woundedHealth));
        Assert.That(player.Body.position, Is.EqualTo(originalPosition));

        player.SetSceneTransitionBlocked(false);
        Assert.That(player.health.TakeDamage(100), Is.True);
        Assert.That(player.RecoverAfterDeathInShelter(spawn), Is.False, "Recovery requires the flow to retain its scene lock.");
        player.SetSceneTransitionBlocked(true);
        Assert.That(player.RecoverAfterDeathInShelter(new Vector3(float.NaN, 10f, 0f)), Is.False);
        Assert.That(player.IsDead, Is.True);
        Assert.That(player.Body.position, Is.EqualTo(originalPosition));
        yield return null;

        int teleports = 0;
        int preparedNotifications = 0;
        player.Teleported += delta => teleports++;
        player.health.HealthChanged += (current, maximum) => preparedNotifications++;
        Assert.That(player.RecoverAfterDeathInShelter(spawn), Is.True);
        Assert.That(player.RecoverAfterDeathInShelter(Vector3.zero), Is.False);
        Assert.That(teleports, Is.EqualTo(1));
        Assert.That(preparedNotifications, Is.EqualTo(1));
        Assert.That(player.Body.position, Is.EqualTo(spawn));
        Assert.That(player.health.CurrentRound, Is.EqualTo(2));

        player.SetSceneTransitionBlocked(false);
        Assert.That(player.health.TakeDamage(1), Is.True);
        player.SetSceneTransitionBlocked(true);
        Assert.That(player.RecoverAfterDeathInShelter(Vector3.zero), Is.False);
        Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth - 1));
        Assert.That(player.health.CurrentRound, Is.EqualTo(2));
        Assert.That(teleports, Is.EqualTo(1));
        LogAssert.NoUnexpectedReceived();
    }

    private void SetKeys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
    }
}
#endif
