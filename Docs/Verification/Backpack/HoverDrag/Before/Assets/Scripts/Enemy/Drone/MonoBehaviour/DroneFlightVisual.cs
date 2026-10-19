using UnityEngine;

// FBX没有Clip，代码平滑前倾/回正；Controller先更新模型姿态，再让视野跟随模型。
[DisallowMultipleComponent]
public class DroneFlightVisual : MonoBehaviour
{
    public Transform visual;
    public Transform[] propellers = new Transform[0];
    [Min(0f)] public float rotorDegreesPerSecond = 2400f;
    [Range(0f, 20f)] public float tiltDegrees = 10f;
    [Min(0.01f)] public float tiltSmoothTime = 0.3f;
    private DroneController drone;
    private Vector3 restPosition;
    private Quaternion restRotation;
    private bool initialized;
    private float currentTilt;
    private float tiltVelocity;

    private void Awake()
    {
        CapturePose();
    }

    private void CapturePose()
    {
        if (initialized || visual == null) { return; }
        drone = GetComponent<DroneController>();
        restPosition = visual.localPosition;
        restRotation = visual.localRotation;
        initialized = true;
    }

    public void UpdatePose()
    {
        CapturePose();
        if (!isActiveAndEnabled || !initialized || drone == null || !drone.IsRunning) { return; }
        Vector3 forward = transform.TransformDirection(drone.FacingRotation * Vector3.right);
        float forwardSpeed = Vector3.Dot(drone.FlightVelocity, forward);
        float targetTilt = -Mathf.Clamp(forwardSpeed / Mathf.Max(0.1f, drone.config.chaseSpeed), -1f, 1f) * tiltDegrees;
        // 速度为零时目标角度回到零；不再添加周期摇晃或上下漂浮。
        currentTilt = Mathf.SmoothDampAngle(currentTilt, targetTilt, ref tiltVelocity,
            Mathf.Max(0.01f, tiltSmoothTime), Mathf.Infinity, Time.deltaTime);
        visual.localPosition = restPosition;
        visual.localRotation = drone.FacingRotation * Quaternion.AngleAxis(currentTilt, Vector3.forward) * restRotation;
    }

    private void LateUpdate()
    {
        if (!initialized || drone == null || !drone.IsRunning) { return; }
        for (int i = 0; i < propellers.Length; i++)
        {
            // 模型桨轴为局部Z，相邻桨反向转；悬停/搜索时仍持续旋转。
            if (propellers[i] != null)
            {
                propellers[i].Rotate(Vector3.forward, rotorDegreesPerSecond * Time.deltaTime * (i % 2 == 0 ? 1f : -1f), Space.Self);
            }
        }
    }

    public void ResetPose()
    {
        CapturePose();
        currentTilt = 0f;
        tiltVelocity = 0f;
        if (!initialized || visual == null) { return; }
        visual.localPosition = restPosition;
        visual.localRotation = (drone != null ? drone.FacingRotation : Quaternion.identity) * restRotation;
    }

    private void OnDisable()
    {
        currentTilt = 0f;
        tiltVelocity = 0f;
        if (!initialized || visual == null) { return; }
        visual.localPosition = restPosition;
        visual.localRotation = restRotation;
    }
}
