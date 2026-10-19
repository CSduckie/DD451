using UnityEngine;

[CreateAssetMenu(fileName = "WarehouseConfig", menuName = "Warehouse/Warehouse Config")]
public class WarehouseConfigSO : ScriptableObject
{
    [Header("首次初始化尺寸（运行中不自动改变已有布局）")]
    [Min(1)] public int width = 20;
    [Min(1)] public int height = 20;
}
