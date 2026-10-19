using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class DroneRoundTests
{
    private readonly List<Object> owned = new List<Object>();
    private readonly Vector3 origin = new Vector3(3000f, 3000f, 12f);
    private DroneConfigSO config;
    private PlayerConfigSO playerConfig;

    [SetUp]
    public void SetUp()
    {
        config = Own(ScriptableObject.CreateInstance<DroneConfigSO>());
        playerConfig = Own(ScriptableObject.CreateInstance<PlayerConfigSO>());
        playerConfig.planeZ = origin.z;
        playerConfig.damageInvulnerabilitySeconds = 0f;
        playerConfig.groundLayers = 0;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Object item in owned) { if (item != null) { Object.DestroyImmediate(item); } }
        owned.Clear();
    }

    [Test]
    public void NonPositiveSameAndOlderRoundsCannotResetAMovedLivingDrone()
    {
        DroneController drone = CreateDrone();
        Assert.That(drone.BeginRound(0), Is.False);
        Assert.That(drone.BeginRound(-1), Is.False);
        Assert.That(drone.CurrentRound, Is.Zero);
        Assert.That(drone.BeginRound(2), Is.True);
        Vector3 moved = origin + new Vector3(2f, 1f, 0f);
        drone.Body.position = moved;
        drone.transform.position = moved;
        Assert.That(drone.BeginRound(2), Is.False);
        Assert.That(drone.BeginRound(1), Is.False);
        Assert.That(drone.Body.position, Is.EqualTo(moved));
        Assert.That(drone.CurrentRound, Is.EqualTo(2));
        Assert.That(drone.BeginRound(3), Is.True);
        Assert.That(drone.Body.position, Is.EqualTo(origin));
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
    }

    [Test]
    public void ExplosionSurvivesSameRoundAndReenableButANewRoundRestoresBodyAndVisual()
    {
        DroneController drone = CreateDrone();
        PlayerController player = CreatePlayer();
        Assert.That(drone.BeginRound(1), Is.True);
        drone.ChangeState(DroneState.Chase);
        Physics.SyncTransforms();
        Assert.That(drone.TryExplode(player), Is.True);
        Assert.That(drone.HasExploded, Is.True);
        Assert.That(drone.GetComponent<SphereCollider>().enabled, Is.False);
        Assert.That(drone.visual.gameObject.activeSelf, Is.False);
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Explode));
        int health = player.health.CurrentHealth;
        drone.enabled = false;
        drone.enabled = true;
        drone.gameObject.SetActive(false);
        drone.gameObject.SetActive(true);
        Assert.That(drone.HasExploded, Is.True);
        Assert.That(drone.BeginRound(1), Is.False);
        Assert.That(drone.TryExplode(player), Is.False);
        Assert.That(player.health.CurrentHealth, Is.EqualTo(health));
        drone.Body.position = origin + Vector3.right * 3f;
        Assert.That(drone.BeginRound(2), Is.True);
        Assert.That(drone.HasExploded, Is.False);
        Assert.That(drone.CurrentState, Is.EqualTo(DroneState.Patrol));
        Assert.That(drone.GetComponent<SphereCollider>().enabled, Is.True);
        Assert.That(drone.visual.gameObject.activeSelf, Is.True);
        Assert.That(drone.Body.position, Is.EqualTo(origin));
    }

    [Test]
    public void AThinSolidWallDoesNotTurnNearContactIntoAnExplosion()
    {
        DroneController drone = CreateDrone();
        PlayerController player = CreatePlayer();
        player.GetComponent<CapsuleCollider>().radius = 0.5f;
        player.Body.position = origin + Vector3.right * 0.71f;
        player.transform.position = player.Body.position;
        GameObject wall = Own(new GameObject("Thin contact separator"));
        wall.transform.position = origin + Vector3.right * 0.205f;
        wall.AddComponent<BoxCollider>().size = new Vector3(0.01f, 3f, 2f);
        Assert.That(drone.BeginRound(1), Is.True);
        drone.ChangeState(DroneState.Chase);
        Physics.SyncTransforms();
        Assert.That(drone.TryExplode(player), Is.False, "The physical bodies are separated even within movement skin distance.");
        drone.MoveTowards(origin + Vector3.right * 2f, 2f);
        Assert.That(drone.HasExploded, Is.False);
        Assert.That(player.health.CurrentHealth, Is.EqualTo(player.health.MaxHealth));
    }

    [Test]
    public void DamageObserverCannotReenterContactOrAdvanceRoundAndDistantApiCallDoesNothing()
    {
        DroneController drone = CreateDrone();
        PlayerController player = CreatePlayer();
        Assert.That(drone.BeginRound(1), Is.True);
        drone.ChangeState(DroneState.Chase);
        player.transform.position = origin + Vector3.right * 3f;
        player.Body.position = player.transform.position;
        Physics.SyncTransforms();
        Assert.That(drone.TryExplode(player), Is.False);
        Assert.That(drone.HasExploded, Is.False);
        player.transform.position = origin;
        player.Body.position = origin;
        Physics.SyncTransforms();
        bool roundAccepted = true;
        bool contactAccepted = true;
        bool explosionAlreadyVisible = false;
        int notifications = 0;
        player.health.NonLethalDamaged += amount =>
        {
            notifications++;
            explosionAlreadyVisible = drone.HasExploded;
            roundAccepted = drone.BeginRound(2);
            contactAccepted = drone.TryExplode(player);
        };
        Assert.That(drone.TryExplode(player), Is.True);
        Assert.That(explosionAlreadyVisible, Is.True, "Explosion must be committed before calling player health.");
        Assert.That(roundAccepted || contactAccepted, Is.False);
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(drone.CurrentRound, Is.EqualTo(1));
        Assert.That(drone.HasExploded, Is.True);
    }

    private DroneController CreateDrone()
    {
        GameObject root = Own(new GameObject("Round test drone"));
        root.transform.position = origin;
        DroneController drone = root.AddComponent<DroneController>();
        drone.config = config;
        drone.GetComponent<SphereCollider>().radius = 0.2f;
        Transform visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        drone.visual = visual;
        GameObject point = Own(new GameObject("Round test patrol point"));
        point.transform.position = origin;
        drone.patrolPoints = new[] { point.transform };
        return drone;
    }

    private PlayerController CreatePlayer()
    {
        GameObject root = Own(new GameObject("Round test player"));
        PlayerController player = root.AddComponent<PlayerController>();
        player.config = playerConfig;
        root.GetComponent<PlayerHealth>().config = playerConfig;
        Assert.That(player.ResetForExploration(1, origin), Is.True);
        return player;
    }

    private T Own<T>(T item) where T : Object
    {
        owned.Add(item);
        return item;
    }
}
