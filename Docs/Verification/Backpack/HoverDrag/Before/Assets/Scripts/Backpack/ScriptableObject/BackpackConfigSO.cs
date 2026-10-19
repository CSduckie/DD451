using UnityEngine;

[CreateAssetMenu(fileName = "BackpackConfig", menuName = "Backpack/Backpack Config")]
public class BackpackConfigSO : ScriptableObject
{
    [Header("背包格子（新模块建立时读取）")]
    [Min(1)] public int width = 4;
    [Min(1)] public int height = 4;
}
