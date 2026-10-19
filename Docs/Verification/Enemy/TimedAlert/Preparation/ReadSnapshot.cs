// Read-only MCP execute_code statements; no dependency on new alert timer field names.
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
if (!UnityEditor.EditorApplication.isPlaying || flow == null || flow.Player == null)
    throw new System.InvalidOperationException("Use the real Bootstrap session first.");
var drone = System.Array.Find(UnityEngine.Object.FindObjectsByType<DroneController>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None),
    d => d.gameObject.scene.path == flow.ruinsScenePath && d.name == "Drone");
if (drone == null || drone.vision == null) throw new System.InvalidOperationException("Actual formal Drone/vision not found.");
var player = flow.Player;
var effect = drone.GetComponent<DroneStatusLight>();
var light = effect != null ? effect.targetLight : null;
var renderer = drone.vision.GetComponent<UnityEngine.MeshRenderer>();
UnityEngine.Vector3 patrolPoint;
bool hasPatrolPoint = drone.TryGetPatrolPoint(out patrolPoint);
var properties = new UnityEngine.MaterialPropertyBlock();
if (renderer != null) renderer.GetPropertyBlock(properties);
return new { time = UnityEngine.Time.time, fixedTime = UnityEngine.Time.fixedTime, frame = UnityEngine.Time.frameCount,
    flowStage = flow.Stage.ToString(), flowRound = flow.CurrentRound, enemyRound = drone.CurrentRound,
    state = drone.CurrentState.ToString(), running = drone.IsRunning, exploded = drone.HasExploded,
    bodyPosition = drone.Body.position.ToString("F4"), flightVelocity = drone.FlightVelocity.ToString("F4"),
    hasPatrolPoint = hasPatrolPoint, patrolPoint = patrolPoint.ToString("F4"), lastKnown = drone.LastKnownPosition.ToString("F4"),
    playerPosition = player.Body.position.ToString("F4"), crouching = player.IsCrouching, backpackOpen = player.IsBackpackOpen,
    hidden = CoverArea.IsHidden(player), canSee = drone.vision.CanSee(player), health = player.health.CurrentHealth,
    visionObjectActive = drone.vision.gameObject.activeInHierarchy, visionComponentEnabled = drone.vision.enabled,
    rangeEnabled = drone.vision.RangeCollider != null && drone.vision.RangeCollider.enabled,
    rendererEnabled = renderer != null && renderer.enabled,
    lightColor = light != null ? light.color.ToString("F3") : "missing", lightIntensity = light != null ? light.intensity : -1f,
    visibleFanColor = properties.GetColor("_BaseColor").ToString("F3"), legacyFill = properties.GetFloat("_FillProgress") };
