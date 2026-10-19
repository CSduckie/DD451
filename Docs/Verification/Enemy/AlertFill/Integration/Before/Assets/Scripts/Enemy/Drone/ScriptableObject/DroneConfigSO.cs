using UnityEngine;

[CreateAssetMenu(fileName = "DroneConfig", menuName = "Enemy/Drone Config")]
public class DroneConfigSO : ScriptableObject
{
    [Header("可调默认值：移动与到点")]
    [Min(0f)] public float patrolSpeed = 1.5f;
    [Min(0f)] public float chaseSpeed = 3f;
    [Min(0.01f)] public float arrivalDistance = 0.15f;
    [Min(0f)] public float patrolWaitSeconds = 1f;
    [Header("失去视线后，只去最后看见的位置")]
    [Min(0f)] public float searchSeconds = 2f;
    [Min(0.1f)] public float blockedReturnSeconds = 2f;
    [Header("仅追击时接触自毁；一次伤害，沿用玩家无敌时间")]
    [Min(1)] public int contactDamage = 1;
}
