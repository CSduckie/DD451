ProjectNightEnvironment.cs：仅 Docs 草稿，未导入 Assets，未调用 Unity。

用途与边界
- 建议实际落位 Assets/Editor/ProjectNightEnvironment.cs；没有额外运行时组件或场景对象。
- [InitializeOnLoad] 同步订阅 newSceneCreated 和 newSceneTemplateInstantiated；直接处理事件的 Scene 参数，没有 delayCall、Update、sceneOpened、sceneSaving 或自动保存。
- 只赋值 RenderSettings.skybox，共用 Assets/Art/Materials/Environment/ShelterNightSkybox.mat。没有调暗 Shelter，没有更改灯光、环境光、雾、反射、材质参数或后处理。
- 排除 Play、无效/未加载场景、Preview Scene、Prefab Stage。Additive 临时激活新场景，finally 恢复事件进入时的活动场景；同一材质已设置时无重复标脏。
- 模板完成回调可能再次覆盖 newSceneCreated 初始化后的值，这是确保模板自身的旧天空盒不覆盖项目新建默认值所需；不会因之后普通 OpenScene 再强制改场景。
- 模板自身如在回调之前已自动存盘，本草稿只把新场景标脏，用户/调用方下一次保存持久化。遵守本任务 no autosave。
- 此草稿不遍历/更改新场景相机；创建模板或 Builder 若明确使用 SolidColor，需要在其定义处改世界相机 ClearFlags，避免误改 UI/叠加/加载相机。

本机 API 证据（没有在 Unity 中执行反射）
路径：E:/6000.2.12f1/Editor/Data/Managed/UnityEngine/
用 Mono.Cecil 0.11.1 只读程序集 metadata：
UnityEditor.CoreModule.dll：
  public EditorSceneManager.newSceneCreated
  delegate void Invoke(Scene scene, NewSceneSetup setup, NewSceneMode mode)
  public static bool EditorSceneManager.IsPreviewScene(Scene scene)
UnityEditor.SceneTemplateModule.dll：
  public SceneTemplateService.newSceneTemplateInstantiated
  delegate void Invoke(SceneTemplateAsset sceneTemplateAsset, Scene scene, SceneAsset sceneAsset, bool additiveLoad)
注意真实事件名是 newSceneTemplateInstantiated，不是 sceneTemplateInstantiated。
对应 UnityEditor.SceneTemplateModule.xml:159 / :201 与 UnityEditor.CoreModule.xml:48205 也记录事件/委托说明；DLL metadata 给出了 XML 未完整列出的 SceneAsset、bool 参数。

准确 Builder 位置（当前源文件，只读；建议一行替换 SolidColor 为 Skybox）
1. Assets/Tests/Player/Editor/PlayerTestSceneBuilder.cs:81
   OutputCamera，是共享 PlayerCamera 世界相机 Prefab 的生成代码。当前实际 PlayerCamera Prefab 已是 Skybox，但旧 Builder 若重新生成仍写 SolidColor；背景色可保留为 fallback。不要改透视/正交、位置、镜头或 Cinemachine。
2. Assets/Tests/Warehouse/Editor/WarehouseTestSceneBuilder.cs:271
   新建 Main Camera，场景世界/主相机，建议改 Skybox；不动其 UI Canvas。
3. Assets/Tests/Localization/Editor/LocalizationTestSceneBuilder.cs:127
   新建 Main Camera，场景世界/主相机，建议改 Skybox；不动其 UI Canvas。
4. Assets/Tests/GameFlow/Editor/MinimumLoopSceneBuilder.cs:294
   BootstrapLoadingCamera，为入口加载展示，不属于前三个世界相机；应单独决定是否保留暗色清屏。若也显示夜空，只改 clearFlags。不要因此触碰入口/UI 生命周期。

全 Assets/Tests 的 .cs 搜索只有以上四处 CameraClearFlags.SolidColor。BaseTest 与 ItemsTest 保存的场景相机也是 SolidColor，但当前没有对应 Builder .cs，所以属于 root 的现有场景一次性设置，不存在可列出的 Builder 行。

Builder 后续填充会否覆盖天空盒
当前 Builder 没有 RenderSettings.skybox 赋值，因此 synchronous newSceneCreated 的 skybox 值不会被它们后续 ambientLight 写入覆盖。ambientLight 仍写亮白盒色是另一项照明策略，不在本次只设默认天空盒草稿内。
不要从带 asmdef 的 Builder 直接调用 Assembly-CSharp-Editor 中本类；本草稿处理创建事件，无需新增程序集依赖。未来若要共享显式 Apply API，应先决定独立 Editor asmdef，而不是顺手引入框架。

待 root 实机确认
EmptyScene / DefaultGameObjects / Basic(URP) 新建、Additive 新建、保存重载保持；旧场景 active/dirty 状态不被回调误改；Prefab Preview 不变。离线编译不等于这些行为的运行验收。
