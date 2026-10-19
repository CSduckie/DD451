using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class DroneStatusLightTests
{
    private readonly List<Object> owned = new List<Object>();
    private readonly Vector3 origin = new Vector3(7000f, 7000f, 40f);
    private readonly Color originalColor = new Color(0.1f, 0.7f, 0.9f);
    private const float OriginalIntensity = 4f;
    private DroneController drone;
    private DroneStatusLight effect;
    private Light pointLight;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (Object item in owned)
        {
            if (item is GameObject gameObject) { gameObject.SetActive(false); }
        }
        foreach (Object item in owned) { if (item != null) { Object.Destroy(item); } }
        owned.Clear();
        yield return null;
    }

    [UnityTest]
    public IEnumerator ActualPatrolChaseAndSearchEachKeepBreathingInTheirOwnColor()
    {
        CreateDrone(true);
        PlayerController player = CreatePlayer(origin + Vector3.right * 20f);
        Assert.That(drone.BeginRound(1), Is.True);
        yield return CheckBreathing(DroneState.Patrol, originalColor);

        MovePlayer(player, origin + Vector3.right * 3f);
        yield return Until(() => drone.CurrentState == DroneState.Chase, "Visible player must start real chase.");
        yield return CheckBreathing(DroneState.Chase, effect.chaseColor);

        MovePlayer(player, origin + Vector3.right * 20f);
        yield return Until(() => drone.CurrentState == DroneState.Search, "Losing the player must start real search.");
        yield return CheckBreathing(DroneState.Search, effect.searchColor);

        Assert.That(pointLight.range, Is.EqualTo(6f));
        Assert.That(pointLight.type, Is.EqualTo(LightType.Point));
        Assert.That(pointLight.shadows, Is.EqualTo(LightShadows.Soft));
        Assert.That(pointLight.transform.localPosition, Is.EqualTo(new Vector3(0f, -0.2f, 0f)));
    }

    [UnityTest]
    public IEnumerator ExplosionStaysDarkAcrossReenableAndNextRoundRestoresOriginalStrength()
    {
        CreateDrone(false);
        PlayerController player = CreatePlayer(origin);
        Assert.That(drone.BeginRound(1), Is.True);
        Physics.SyncTransforms();
        Assert.That(drone.TryExplode(player), Is.True);
        MovePlayer(player, origin + Vector3.right * 20f);
        yield return null;
        yield return new WaitForFixedUpdate();
        Assert.That(pointLight.intensity, Is.Zero);
        Assert.That(pointLight.gameObject.activeInHierarchy, Is.False);

        effect.enabled = false;
        effect.enabled = true;
        drone.gameObject.SetActive(false);
        drone.gameObject.SetActive(true);
        yield return null;
        Assert.That(drone.BeginRound(1), Is.False);
        Assert.That(pointLight.intensity, Is.Zero, "Reenabling must not revive an exploded drone's light.");

        Assert.That(drone.BeginRound(2), Is.True);
        yield return CheckBreathing(DroneState.Patrol, originalColor);
        Assert.That(pointLight.gameObject.activeInHierarchy, Is.True);
        effect.enabled = false;
        Assert.That(pointLight.intensity, Is.EqualTo(OriginalIntensity));
        Assert.That(pointLight.color, Is.EqualTo(originalColor));
    }

    [UnityTest]
    public IEnumerator DisablingRestoresAuthoredLightAndNeverEnablesAnOriginallyDisabledLight()
    {
        CreateDrone(false);
        pointLight.enabled = false;
        Assert.That(drone.BeginRound(1), Is.True);
        yield return null;
        yield return new WaitForFixedUpdate();
        Assert.That(pointLight.enabled, Is.False);
        Assert.That(pointLight.intensity, Is.EqualTo(OriginalIntensity));

        pointLight.enabled = true;
        yield return CheckBreathing(DroneState.Patrol, originalColor);
        effect.enabled = false;
        Assert.That(pointLight.color, Is.EqualTo(originalColor));
        Assert.That(pointLight.intensity, Is.EqualTo(OriginalIntensity));
        effect.enabled = true;
        yield return CheckBreathing(DroneState.Patrol, originalColor);

        drone.enabled = false;
        yield return null;
        yield return new WaitForFixedUpdate();
        Assert.That(pointLight.color, Is.EqualTo(originalColor));
        Assert.That(pointLight.intensity, Is.EqualTo(OriginalIntensity));
        Object.Destroy(pointLight.gameObject);
        yield return null;
        yield return null;
        // Unity Test Runner会将未预期异常判为失败，这里验证目标销毁后的安全收尾。
        effect.enabled = false;
    }

    private IEnumerator CheckBreathing(DroneState state, Color expectedColor)
    {
        yield return Until(() => pointLight.color == expectedColor, "State color must reach the existing point light.");
        float minimum = float.PositiveInfinity;
        float maximum = 0f;
        float finish = Time.time + effect.breathingDuration * 1.5f;
        float timeout = Time.realtimeSinceStartup + 5f;
        while (Time.time < finish && Time.realtimeSinceStartup < timeout)
        {
            yield return new WaitForFixedUpdate();
            Assert.That(drone.CurrentState, Is.EqualTo(state));
            Assert.That(pointLight.color, Is.EqualTo(expectedColor));
            Assert.That(pointLight.intensity, Is.InRange(1f, OriginalIntensity));
            minimum = Mathf.Min(minimum, pointLight.intensity);
            maximum = Mathf.Max(maximum, pointLight.intensity);
        }
        Assert.That(minimum, Is.LessThan(2f), "Each state must visibly dim, rather than only changing color.");
        Assert.That(maximum, Is.GreaterThan(3f), "Each state must brighten again without losing its original strength.");
    }

    private void CreateDrone(bool withVision)
    {
        DroneConfigSO config = Own(ScriptableObject.CreateInstance<DroneConfigSO>());
        config.patrolSpeed = 0f;
        config.chaseSpeed = 0f;
        config.searchSeconds = 20f;
        config.blockedReturnSeconds = 0.1f;
        GameObject root = Own(new GameObject("Status light test drone"));
        root.transform.position = origin;
        drone = root.AddComponent<DroneController>();
        drone.config = config;
        drone.GetComponent<SphereCollider>().radius = 0.2f;
        drone.visual = new GameObject("Visual").transform;
        drone.visual.SetParent(root.transform, false);
        drone.patrolPoints = new[] { root.transform };
        pointLight = new GameObject("Existing point light").AddComponent<Light>();
        pointLight.transform.SetParent(drone.visual, false);
        pointLight.transform.localPosition = new Vector3(0f, -0.2f, 0f);
        pointLight.type = LightType.Point;
        pointLight.color = originalColor;
        pointLight.intensity = OriginalIntensity;
        pointLight.range = 6f;
        pointLight.shadows = LightShadows.Soft;
        effect = root.AddComponent<DroneStatusLight>();
        effect.targetLight = pointLight;
        effect.breathingDuration = 0.5f;
        effect.minIntensityMultiplier = 0.25f;
        effect.maxIntensityMultiplier = 1f;
        if (withVision)
        {
            GameObject volume = new GameObject("Vision");
            volume.transform.SetParent(root.transform, false);
            drone.vision = volume.AddComponent<DroneVision>();
            drone.vision.radius = 6f;
            drone.vision.angleDegrees = 100f;
            drone.vision.thickness = 2f;
            drone.vision.Rebuild();
        }
    }

    private PlayerController CreatePlayer(Vector3 position)
    {
        PlayerConfigSO config = Own(ScriptableObject.CreateInstance<PlayerConfigSO>());
        config.planeZ = position.z;
        config.groundLayers = 0;
        config.damageInvulnerabilitySeconds = 0f;
        GameObject root = Own(new GameObject("Status light test player"));
        root.transform.position = position;
        PlayerController player = root.AddComponent<PlayerController>();
        player.config = config;
        player.health.config = config;
        Assert.That(player.ResetForExploration(1, position), Is.True);
        player.Body.isKinematic = true;
        return player;
    }

    private static void MovePlayer(PlayerController player, Vector3 position)
    {
        player.Body.position = position;
        player.transform.position = position;
        Physics.SyncTransforms();
    }

    private T Own<T>(T item) where T : Object
    {
        owned.Add(item);
        return item;
    }

    private static IEnumerator Until(Func<bool> condition, string message)
    {
        float deadline = Time.realtimeSinceStartup + 3f;
        while (!condition() && Time.realtimeSinceStartup < deadline) { yield return null; }
        Assert.That(condition(), Is.True, message);
    }
}
