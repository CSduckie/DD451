09F 普通 Play 探针（仅 Docs；由根聊天独占 Unity 执行）

执行前
- 先核对并保全最新用户 Ruins 场景。不要用旧场景/旧备份覆盖当前文件。
- 正式生产代码与测试迁移编译完毕后，从 Bootstrap 开始普通 Play，等待真实 Shelter；Game 分辨率1920×1080，Time.timeScale=1，无其它验收回调。
- 可先用主程的截图工具保存真实 Shelter 画面。不要把脚本导入 Assets。

顺序
1. 将01_Depart.cs全文作为Unity MCP execute_code方法体执行。它只用flow.TryDepart，记录持久玩家身份和正式新轮次。
2. 等真实GameFlow进入Exploring，再执行02_NaturalTimedAlert.cs。脚本动态选本场景enabled、已初始化且有路线的Drone与邻近真实CoverArea，最多观察90秒。
3. 用ReadResult.cs读取状态。运行中不另改玩家/输入/相机/AI，不暂停、不改timeScale。若需停止，执行CancelProbe.cs，等Running=false后再停止Play或接下一项。
4. 结果在SessionState记录的09F.Probe.Directory（Integration/OrdinaryPlay/Run-*）。检查CSV、02-TimedAlert.txt及六张1920截图。代码断言PASS不替代逐张画面检查。

自动经过的真实流程
原巡逻移动 → 玩家站在现有草丛内（不隐藏） → 自然首次发现、橙色原地Alert约0.5秒 → 红色Chase且仅Renderer隐藏、CanSee和真实Collider仍工作 → 排队C键真实蹲伏 → 现有CoverArea判Hidden、橙色原地2秒Alert → 等约0.45秒后玩家移到真实视锥内且离开草丛，松开C → 立即Chase → 再进同一草丛并蹲伏 → 连续隐藏2秒 → 当前路线索引恢复且再次移动。
两个警戒分支中各调用一次正式bindings.Activate/TryBeginExplorationEnemies(currentRound)，同步核对计时、状态、位置、Target完全不变。

明确边界
- 定位通过真实玩家RelocateWithinRun，并临时保持玩家Rigidbody为kinematic。蹲伏通过探针独占的InputSystem Keyboard排队C事件，沿真实PlayerInputReader/Player状态机处理；不宣称原生键盘输入。
- 不设置AI状态、计时、轮次，不直接伤害，不改无人机/掩体的位置、路线、速度、配置或相机；不暂停游戏来拉长0.5秒窗口。当前配置必须正好0.5/2秒，否则拒绝执行。
- 自动截图可能受Game渲染时序影响，根需核对内容。若低帧率完全漏过Alert，或用户新路线不靠近选中的草丛，探针报失败/超时，不强制状态补齐。
- 返回草丛时要求真实IsGrounded与Hidden；选定草丛底面需确有现有地面。默认用BoxCollider最小Y+真实胶囊半高+0.02定位，不生成临时地板/草丛。
- 本探针不替代普通墙Search、首次警戒目标失效、同物理帧隐身接触保护、自然一次伤害/同轮爆炸不复活/下一次正式出发的自动化；这些由本轮Enemy/GameFlow测试覆盖。
- 完成或取消会移除回调和探针Keyboard，恢复玩家原位置、kinematic/gravity以及runInBackground。脚本不设置Time.timeScale，异常暂停需要根按现场恢复。敌人自然经历的状态和位置保留，直到主程结束本次普通Play。

准备验证
StaticCompile.txt：四个方法体已用本机C#编译器/langversion:6，引用暂存Enemy生产模块及当前项目依赖，离线编译通过。没有启动Unity Editor、运行探针或生成验收截图；离线编译不是运行验收。
