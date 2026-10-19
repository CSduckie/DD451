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
        EditorGUILayout.HelpBox("实际候选范围由本对象的凸 MeshCollider 决定，调参会同步重建Collider与Game可见扇形。Vision Origin绑定模型下的Visual/VisionOrigin，只跟随机头位置；朝向与模型共用Controller的水平转头，不继承模型飞行前倾或美术校正角。Direction Degrees是额外俯仰偏角，本Drone保持0，使视野保持水平。扇形平面及局部Z厚度一起左右旋转，转到侧面时显示会变窄。检测与显示共用整厚度BoxCast：厚度内任一切片有墙就截断该方向；这不是体积光。普通装饰用Ignore Raycast层或不加Collider。", MessageType.Info);
        if (GUILayout.Button("重建视野 / Rebuild Vision")) { changed = true; }
        if (changed)
        {
            ((DroneVision)target).Rebuild();
            SceneView.RepaintAll();
        }
    }
}
