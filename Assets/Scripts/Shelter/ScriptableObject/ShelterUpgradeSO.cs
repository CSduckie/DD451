using UnityEngine;

[CreateAssetMenu(fileName = "ShelterUpgrade", menuName = "Shelter/First Warehouse Upgrade")]
public class ShelterUpgradeSO : ScriptableObject
{
    [Header("资格门槛（EV不作为消耗货币）")]
    [Min(1)] public int requiredEv = 7;
    [Header("首次扩容仅增加行数；默认20×20扩至20×24")]
    [Tooltip("必须与当前运行仓库的列数相同。首次升级只向下增加行数，不改变列数或物品显示大小。")]
    [Min(1)] public int targetWidth = 20;
    [Min(1)] public int targetHeight = 24;
}
