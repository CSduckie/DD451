using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DroneVision))]
public class DroneVisionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        bool changed = EditorGUI.EndChangeCheck();
        EditorGUILayout.HelpBox("实际候选范围由本对象的凸 MeshCollider 决定，调参会同步重建Collider与Game可见扇形。方向用Direction Degrees和左右朝向，厚度沿Z。检测与显示共用整厚度的保守BoxCast遮挡：墙占据厚度内任一Z切片就截断整条方向，因此不同Z的玩家也遵循相同显示；这不是体积光。普通装饰用Ignore Raycast层或不加Collider。", MessageType.Info);
        if (GUILayout.Button("重建视野 / Rebuild Vision")) { changed = true; }
        if (changed)
        {
            ((DroneVision)target).Rebuild();
            SceneView.RepaintAll();
        }
    }
}
