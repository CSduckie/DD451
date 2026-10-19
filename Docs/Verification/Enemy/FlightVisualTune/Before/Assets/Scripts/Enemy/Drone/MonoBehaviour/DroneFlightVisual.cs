using UnityEngine;

// FBX没有Clip，但四个桨叶有独立轴心；只动Visual，绝不改变物理根/视野。
[DisallowMultipleComponent]
public class DroneFlightVisual : MonoBehaviour
{
    public Transform visual;
    public Transform[] propellers = new Transform[0];
    [Min(0f)] public float rotorDegreesPerSecond = 900f;
    [Min(0f)] public float movingBobHeight = 0.035f;
    [Min(0f)] public float bobFrequency = 2f;
    [Range(0f, 15f)] public float tiltDegrees = 6f;
    private DroneController drone;
    private Vector3 restPosition;
    private Quaternion restRotation;
    private bool initialized;

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

    private void LateUpdate()
    {
        CapturePose();
        if (!initialized || drone == null || !drone.IsRunning) { return; }
        float speed = drone.FlightVelocity.magnitude;
        float amount = Mathf.Clamp01(speed / Mathf.Max(0.1f, drone.config.chaseSpeed));
        float bob = Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * movingBobHeight * amount;
        visual.localPosition = Vector3.Lerp(visual.localPosition, restPosition + Vector3.up * bob, 12f * Time.deltaTime);
        float tilt = -drone.FlightVelocity.x / Mathf.Max(0.1f, drone.config.chaseSpeed) * tiltDegrees;
        Quaternion facing = Quaternion.Euler(0f, drone.FacingDirection < 0 ? 180f : 0f, 0f);
        // 先转向再沿飞行平面倾斜，向左飞也保持机头略向下。
        visual.localRotation = Quaternion.AngleAxis(tilt, Vector3.forward) * facing * restRotation;
        for (int i = 0; i < propellers.Length; i++)
        {
            // 模型桨轴为局部Z，相邻桨反向转；悬停/搜索时仍持续旋转。
            if (propellers[i] != null)
            {
                propellers[i].Rotate(Vector3.forward, rotorDegreesPerSecond * Time.deltaTime * (i % 2 == 0 ? 1f : -1f), Space.Self);
            }
        }
    }

    private void OnDisable()
    {
        if (!initialized || visual == null) { return; }
        visual.localPosition = restPosition;
        visual.localRotation = restRotation;
    }
}
