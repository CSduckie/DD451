玩家随身补光：短动作验收准备（仅只读审查与Docs草案）
日期：2026-10-19。当前007仍独占Unity/Assets；本准备未调用Unity、未改Assets、未编译或执行脚本。主线程获得明确释放后执行。

文件
ShortAction.cs：一个MCP execute_code方法体，输入动作名通过SessionState选择，含显式Cleanup分支。只放Docs，不放Assets；没有产品脚本/组件或新的测试框架。

已核实的实际入口
- PlayerInputReader在OnEnable克隆Player/Move、Jump、Crouch、Interact，限定Keyboard&Mouse绑定组，然后启用。
- 当前GameInputActions真实键位：A/D左右，Space跳跃，C下蹲。
- PlayerMovementTests的SetKeys使用AddDevice<Keyboard>和QueueStateEvent(new KeyboardState(keys))，让正常帧处理；本草案同样不调用ChangeState、SetCrouching、ExecuteJump、ApplyHorizontalVelocity，不给玩家写位置/速度。
- PlayerController.Update读取InputReader，更新GroundState/朝向；FixedUpdate实际起跳和3D移动。timeScale=0仍会调用Update，所以截图时若释放C/A/D，状态/姿势可能立刻改变；本草案故意保留该次虚拟按键，拍完才Cleanup。
- InputSystem AddDevice<T>(string name)、GetDeviceById、ResetDevice、RemoveDevice已核对本地包源码公开签名；本准备没有运行/编译通过的宣称。

最小操作顺序（主线程）
1. 07F明确释放后fresh检查，从正式Bootstrap普通Play到Shelter，Game固定1920×1080。补光Prefab已保存重载。关背包/仓库，选择平坦开阔、无低顶和实体阻挡的位置，自然落地，确认CanAct、IsGrounded、非下蹲、timeScale1；不要禁用AI/墙或替换物理。
2. 可先用正式公开定位API RelocateWithinRun放到已确认安全点；如果用了，单独记录“API准备位置”。本ShortAction本身不定位玩家。动作期间操作者不要再按实体移动/跳跃/下蹲键，避免真实Keyboard与虚拟Keyboard的绑定同时贡献。
3. 设置动作，再执行ShortAction.cs全文，例如：
   UnityEditor.SessionState.SetString("PlayerFill.Action", "Right"); return true;
   可选SetString("PlayerFill.Directory", "E:/DD451/Docs/Verification/Rendering/PlayerFillLight/Runtime-本次标识")，未设时默认Runtime。每次结果文件用随机后缀，不覆盖先前失败。
4. 间隔读取：
   return new { running = UnityEditor.SessionState.GetBool("PlayerFill.Running", false), held = UnityEditor.SessionState.GetBool("PlayerFill.Held", false), result = UnityEditor.SessionState.GetString("PlayerFill.Result", ""), timeScale = UnityEngine.Time.timeScale };
   FROZEN表示实际输入和状态达标，且动作Editor回调已移除；这还不是视觉可读性通过。Running=false、Held=true、timeScale0为预期截图状态。
5. 直接复用现有Docs/Verification/Rendering/ShelterPostProcessing/Preparation/02_Capture.cs，先设ShelterPP.Directory为本次PlayerFillLight证据目录、ShelterPP.CaptureLabel为唯一标签（如Shelter-Right-On）。该截图方法允许timeScale0且使用真实Game ScreenCapture，不要调用01_Begin（它要求round0/timeScale1并会另建Shelter目录）。等CaptureRunning=false与结果PASS，确认PNG实际1920×1080并查看图像。
6. 同姿势做补光On/Off对照：保持timeScale0，只临时切实际玩家补光Light.enabled，分别拍不同标签；恢复为On。灯光闪烁若用Time.time/deltaTime会同时冻结；若当前效果按unscaledTime驱动，另由主线程冻结/恢复该效果的真实运行时相位，不能把动态差异误归补光。
7. 截图结束，设PlayerFill.Action="Cleanup"，再次执行同一个ShortAction.cs。若动作仍运行，它只清Token，下一Editor tick由自身回调完成清理；若已经FROZEN，它立即Reset/Remove仅本任务虚拟Keyboard并恢复原timeScale/background。不要手动把Running清false冒充回调已移除。
8. 确认Running=false、Held=false、DeviceId=-1、timeScale1；让正常帧更新并落地/起身，再开始下一动作。顺序建议Right→Cleanup→Left→Cleanup→Jump→Cleanup→自然落地→Crouch→Cleanup。左右每段至少0.22s并真的移动0.2单位；跳跃必须到Jump且升高0.25；下蹲必须到Crouch/IsCrouching。受墙/低顶/锁/死亡阻止会FAIL并自动清理，不强制姿势。
9. 完成动作后另走主线程正式Shelter→Ruins→Shelter、死亡返回流程；在各阶段检查同Player实例下恰好一盏灯且父节点是Player根，不把本短动作成功当成生命周期验证。补光隔离/Rendering Layers仍由实际相同机位开关图和层配置核对。

每次记录
动作前后frame/time、playerId/scene/position/state/facing/grounded/crouching、InputReader实际值，玩家子Light数量、灯instanceId、父节点名称/ID、是否直接挂Player根、世界和局部位置。到达状态后再验证灯数量=1、同灯ID且父节点未变；这证明随玩家的对象结构稳定，不代替截图审查照明范围和曝光。

准确报告方式
写“普通Play，Input System虚拟Keyboard合成设备事件驱动真实输入绑定、状态机和物理；timeScale0冻结实际Game截图”。不要写“实体键鼠/原生键盘已验证”。Snapshot的FROZEN仅表示条件达到；PNG/视觉隔离/死亡返回等必须独立报告。

收尾
先完成或取消ShelterPP截图回调，再Cleanup本动作；确认两套Running均false且虚拟设备不存在，timeScale/background恢复。Domain Reload若发生可能清掉Editor委托但留下SessionState，须fresh确认回调和设备真实状态后处理，不静默清标志当成功；正常本验证不触发编译/域重载。最终停止Play与Console/引用检查由主线程执行。
