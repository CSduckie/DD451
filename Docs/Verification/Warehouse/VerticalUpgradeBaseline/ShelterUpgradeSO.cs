using UnityEngine;

[CreateAssetMenu(fileName = "ShelterUpgrade", menuName = "Shelter/First Warehouse Upgrade")]
public class ShelterUpgradeSO : ScriptableObject
{
    [Header("资格门槛（EV不作为消耗货币）")]
    [Min(1)] public int requiredEv = 7;
    [Header("首次扩容目标；24×24只是可调测试默认值")]
    [Min(1)] public int targetWidth = 24;
    [Min(1)] public int targetHeight = 24;
}
