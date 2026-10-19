Shelter 后处理最小普通 Play 验证准备（仅准备，尚未执行）
审计日期：2026-10-19；全部文件位于 Docs，不导入 Assets，不新增测试组件。

离线静态验证补记
2026-10-19T08:13:07Z：使用本机 Unity 随附 Roslyn、C#6 及已有 Library/Bee reference assemblies 离线编译五个方法体成功，ExitCode=0。记录 OfflineCompile/Result.json、Compile.log、Compile.rsp；产物 ShelterPPValidation.dll 只保存在 Docs，未加载执行。
唯一编译警告 CS1701 是 Newtonsoft.Json 的 netstandard2.0 引用与当前 netstandard2.1 兼容引用匹配提示，无 C# 字段/API/语法错误。此结果不等于 Unity运行、实际画面或场景流程通过；后续仍需 root 在获得 Editor 后执行。

执行前提
1. 只有主操作者读到协调文件明确的 009 NIGHT DEFAULT EXPLICIT RELEASE TO SHELTER POSTPROCESS，完成 fresh Editor 状态检查、保留最新文件/GUID 基线后，才可调用 Unity。
2. 本目录脚本是 MCP execute_code 的方法体，不是产品 C# 源码。每次用 execute_code 执行单个文件文本；不要放到 Assets。此准备未调用 Unity，也未声称这些片段已运行。
3. 从已保存的正式 Bootstrap 普通 Play 进入；不从 TestRunner 或直接打开 Shelter 开始。Game View 使用固定 1920×1080，先确认 Screen.width/height。保持 timeScale=1，绝不修改共享 Prefab/URP 资产/灯光/天空盒来完成验证。
4. 运行下述步骤前先读当前项目代码是否又有变化。当前已核对 GameFlowManager、GameFlowSceneBindings、GameFlowInteractable、PlayerController、PlayerInteraction、PlayerCameraTargetBinder 与 RuinsIntegration 既有 runtime probe。

最小顺序
A. 正式 Bootstrap 自动到 Shelter 后，执行 01_Begin.cs；记录唯一实际玩家 ID、初始 round=0 和输出目录。
B. 执行 02_Capture.cs，默认标签 01-Shelter-Before。这是实际 ScreenCapture framebuffer（含真实 UI），不是 Scene View/临时 Camera.Render。返回 armed 后必须轮询 SessionState 的 ShelterPP.CaptureResult，等待 PASS/FAIL，再读 PNG 与 metadata.json。操作结束前不可切图或切 PP。
C. 调整本场景 PP 后，保持同一个玩家/相机位置，用 SessionState.SetString("ShelterPP.CaptureLabel", "02-Shelter-After") 设置新标签并再执行 02；同机位比较应核对两份 metadata 的 player.position/camera.position/camera.rotation/fov/orthographicSize。截图脚本会拒绝覆盖已有同名证据。
D. 可选交互物可读性：SetString("ShelterPP.Target", "Storage"); SetBool("ShelterPP.Invoke", false); 执行 03_InteractFlowTarget.cs，仅经公开定位 API 进入真实 Trigger。等至少两帧后用新标签拍提示；然后 Invoke=true 再执行 03，可通过真实 PlayerInteraction 打开仓库；用新标签拍 UI。关 UI 用当前 GameFlowSceneBindings.CloseSessions()，不得手改玩家锁。若 API 入范围失败，保留失败并检查实际阻挡/Trigger，不扩大或禁用 Collider。
E. Target="Departure"、Invoke=true 执行03，通过真实 PlayerInteraction 发起离开。间隔读取 GameFlowManager.Stage，等 Exploring 后拍 03-Ruins（检查 PP 没继承 Shelter）；用公开 GameFlowManager.TryDepart 属于较窄的流程 API 检查，不能替代上述交互验证。
F. Target="Extraction"、Invoke=true 执行03，通过真实 Trigger/CanReach/CurrentTarget/TryInteract 发起返回。等 Shelter 后执行04_VerifyReturned.cs；再拍 04-Shelter-Returned，检查同一个玩家、round=settledRound=1、旧 Ruins 卸载、当前相机 PP 与局部 Volume 回来仍正确。
G. 最后必须执行05_CancelCapture.cs（即使没有待执行也可），等待 ShelterPP.CaptureRunning=false。停止普通 Play，确认所有场景 clean，无残留回调/测试，读 Console 错误/警告与引用，再由 root 写交接与释放 Editor。

调用示例（execute_code 方法体，不是本准备的已执行结果）
UnityEditor.SessionState.SetString("ShelterPP.CaptureLabel", "03-Ruins"); return true;
UnityEditor.SessionState.SetString("ShelterPP.Target", "Extraction"); UnityEditor.SessionState.SetBool("ShelterPP.Invoke", true); return true;
return new { running = UnityEditor.SessionState.GetBool("ShelterPP.CaptureRunning", false), result = UnityEditor.SessionState.GetString("ShelterPP.CaptureResult", ""), flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>()?.Stage.ToString() };

准确的公开 API 与限制
- GameFlowManager.Player 是正式唯一玩家；PlayerController.Position 只读，Body 是公开 Rigidbody 属性；不要访问不存在的 player.Rigidbody。
- PlayerController.RelocateWithinRun(Vector3) 保留生命/死亡/轮次/医疗次数，清输入与速度，并触发 Teleported。PlayerCameraTargetBinder 收到事件后调用 Cinemachine.OnTargetObjectWarped、PreviousStateIsValid=false，因此应使用此 API 而非直接 transform.position。
- target.TryGetInteractionTrigger(out BoxCollider) 获取真实范围；用 trigger.bounds.center - (capsule.bounds.center - player.Body.position) 对齐身体中心，z=player.config.planeZ。PrepareSpatialQuery 自身会 Physics.SyncTransforms。定位不模拟行走/跳跃/避敌，也不是实体键盘输入。
- RefreshTarget() 后检查 CurrentTarget==目标、CanReachTarget(目标)，再 TryInteract()。其返回 true 只表示调用了 Interactable.Interact，不直接保证业务操作成功；还必须检查 flow.Stage 或真实仓库 UI IsOpen。
- GameFlowManager.TryDepart/TryExtract 可验证业务流程，但没有 Trigger/射线路径，因此本准备优先使用 PlayerInteraction。
- 任何 Input System QueueStateEvent/虚拟设备也应标“合成输入”，不能写成实体键鼠。实体键鼠只指操作者通过系统实际输入/计算机控制在真实 Game 视图操作；没有做就明确未验证。

Volume stack 时序依据（本地 URP17.2 源码）
- UniversalRenderPipeline.cs 的 CameraRenderingScope 析构调用 EndCameraRendering；Base Camera 在该 scope 内先 UpdateVolumeFramework 后正常渲染。因此目标 Camera 的 RenderPipelineManager.endCameraRendering 内可读取当帧 VolumeManager.instance.stack。
- UpdateVolumeFramework 每帧更新模式会 DestroyVolumeStack(additionalCameraData) 并 ResetMainStack + Update。故 additionalCameraData.volumeStack==null 不代表没有 Volume，也不可在任意 Editor tick 把共享 stack 当作目标相机的结果。
- 本准备只在目标正式 Camera 的 endCameraRendering 读取共享 stack，不调用 VolumeManager.Update 来制造结果。记录 stack 的值与相机 PP 开关分别存在；PP=false 时 stack有曝光等值，也不说明这些值实际显示到画面。
- 场景 Volume 读取 sharedProfile，避免访问 profile getter 产生运行时副本。来源数量/权重/层级用于解释混合结果，最终实际参数来自当帧 stack。

评价边界
这项低影响配置不需要重新跑全部背包/敌人测试。真实截图应分别看角色轮廓、交互物/提示、室内暗部层次、明亮局部光源和黑位，避免把“整体更暗”直接等同“不发灰”。对比只证明此次配置的视觉差异，不证明先前发灰根因。
流程完成证明必须来自04结果及对应场景截图；本准备文件和脚本存在本身不是验收证据。若无人机在 API 辅助验证时攻击导致玩家死亡，应如实中止/重新开正式会话，不关 AI、改血或假造成功返回。
