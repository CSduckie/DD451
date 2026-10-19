using UnityEngine;

// 只在白盒场景模拟接触伤害；实际敌人由07提供。
[RequireComponent(typeof(Collider))]
public class PlayerTestDamageSource : MonoBehaviour
{
    [Min(1)] public int damage = 1;

    private void OnTriggerStay(Collider other)
    {
        PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
        if (health != null)
        {
            health.TakeDamage(damage);
        }
    }
}
