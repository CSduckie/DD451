// Unity MCP execute_code statements. Run only in Play after the owners release the Editor.
var driver = UnityEngine.Object.FindFirstObjectByType<EnemyTestDriver>();
if (!UnityEditor.EditorApplication.isPlaying || driver == null || !driver.IsReady)
    throw new System.InvalidOperationException("Start EnemyTest normally and wait for the real driver.");
var drone = System.Array.Find(driver.drones, d => d != null && d.name == "Drone_B");
if (drone == null || drone.vision == null || !drone.IsRunning)
    throw new System.InvalidOperationException("The real Drone_B and its vision must be running.");
var player = driver.player;
var capsule = player.GetComponent<UnityEngine.CapsuleCollider>();
var forward = drone.transform.TransformDirection(drone.FacingRotation * UnityEngine.Vector3.right);
forward.y = 0f;
forward.z = 0f;
forward.Normalize();
var targetCentre = drone.vision.transform.position + forward * 2.5f;
var centreOffset = capsule.bounds.center - player.Body.position;
var playerPosition = targetCentre - centreOffset;
playerPosition.z = player.config.planeZ;
if (!player.RelocateWithinRun(playerPosition))
    throw new System.InvalidOperationException("Real player relocation was rejected.");
player.Body.isKinematic = true;
UnityEngine.Physics.SyncTransforms();
return new
{
    playerPosition = player.Body.position,
    playerCentre = capsule.bounds.center,
    dronePosition = drone.Body.position,
    eligible = DroneController.CanTarget(player),
    visible = drone.vision.CanSee(player),
    stateBeforeAIUpdate = drone.CurrentState.ToString(),
    progressBeforeAIUpdate = drone.AlertProgress,
    note = "Positioning only. The game's own Update must perform observation and state changes."
};
