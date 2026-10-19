MyPrecious AI开发任务包分发指南
版本：2026-10-18 / Dispatch v18（Ctrl快捷转移、鼠标精确落位、去除容器多余黑框）
用途：用户将提示词复制给各开发聊天；此目录是规划与交接要求，不是实现或测试完成证明。

所有任务最新显示要求：默认16:9、1920×1080，暂不做多分辨率或小屏支持；UI、相机取景、Game试玩与Windows验收均以1920×1080为基准。此规则覆盖旧两种尺寸验收条款，历史证据保留。见ProjectRules.txt A23。

当前实施顺序
06继续转移/搜刮 → 新07最小循环整合 → 新08循环验收与Windows试玩 → 新09敌人AI/潜行 → 新10风险流程整合与完整P0回归。
详细范围、目录、依赖和验收见Roadmap_MinimumLoop.txt。新07统一负责必要流程与白盒场景，避免流程和场景两个聊天互等；不重做00—06。
旧编号映射：旧07敌人→新09；旧08成功出发/撤离与旧09白盒整合→新07；旧08死亡及后续敌人整合→新10；旧10回归拆为新08最小循环和新10完整P0。既有提示词/交接中的编号按此映射理解。

当前可分发
最新已下发：04A_BackpackQuickTransfer.txt、05A_WarehouseQuickTransfer.txt、06C_QuickTransferPrecisePlacement.txt → 07B_QuickTransferIntegration.txt。共同规则/API/文件所有权及验收矩阵见QuickTransfer_Dispatch.txt和ProjectRules A25。04/05/06并行编写各自代码测试，07等待新交接后唯一操作Unity并统一验收；旧普通返箱自动找位规则已被新鼠标精确落位覆盖，自动找位只用于快捷操作。当前为已接收执行，不代表实现或新版测试通过。
2026-10-18追加任务已下发：06A_LootGridSearch.txt（006箱子格子/未知轮廓/中心倒计时）→07A_GridIntegration.txt（007等06A释放后接入正式场景、统一实际格子和测试入口）。此为执行安排，不是已完成证明。最新规则见ProjectRules A24，格子基准66/4/6。
00_Foundation_Contracts.txt：基础检查、最小常驻入口、输入与基础验证。
01_Items_Identity_Effects.txt：物品SO、独立实例、唯一身份和治疗效果。
02_Player_Health_Interaction.txt：修改复用ARPG代码状态机、允许胶囊占位的3D横版玩家、必须使用Cinemachine、真实生命、输入限制与交互。
03_Localization.txt：默认中文、单一F10切换入口、SO语言表、读取现有物品译文、TMP与两个现有测试面板接入。
04_Backpack.txt：默认4×4的独立背包、包内拖拽/旋转/使用、Tab与受击关闭、03双语接入；实施时须核对03实际交付，当前提示词完成不代表03已实现。
05_Warehouse_Shelter.txt：默认20×20独立仓库、按IsRetained重算EV、EV7手动免费一次扩容、仓库内确认遗弃、03真实双语接口接入。03已有实现，验证状态以实际交接为准；04/06/08接入按最新源码核对。
06_Transfers_Loot.txt：保留来源的同步转移、真实跨容器拖放、3D地面拾放、箱子独立概率和逐件搜索；使用01身份与找回入口，复用04关闭流程及03双语，允许必要的04/05 UI接入修改。实施及验证状态以实际交接为准。
07_Minimum_Loop_Integration.txt：新版07完整提示词，统一负责正式场景、同一玩家迁移、成功出发/撤离、保有与遗留物收尾和下一轮；接真实00—06，不做敌人AI。06仍在开发，实施前核对最终交接，不把当前源码快照当作已联测完成。
08_Minimum_Loop_Validation.txt：新版08完整提示词，先核对07真实接线、补齐已有搭建器的执行，再进行连续三轮正式循环、缺陷修复、双语和Windows试玩；不做敌人AI。07源码及离线编译不等于正式场景/运行已验收，实施前读取最新交接。
新09—10完整提示词待按新顺序逐份核对后编写。原07_Enemy_Stealth.txt、08_GameFlow.txt、09_Scenes_UI_Integration.txt、10_Regression_Windows_Delivery.txt均为旧参考稿，停止直接分发；不能按旧顺序执行或恢复预建框架。
最新规则覆盖旧提示词：背包确认丢弃普通物直接销毁、唯一物下一轮回原点，不生成可同轮捡回的地面物；箱子放回与丢弃不同。仓库升级仅向下增行、宽度不变，同一viewport中原格子/物品不因升级缩小；当前20×24是可配置示例。见ProjectRules及08提示词。

全部后续任务共同标准
1. 作品集兼毕设：用户应能读懂、修改并在答辩时解释。按当前功能需要写代码，不提前建设无消费者的接口、注册框架或通用事务引擎。
2. 可修改复用用户CardGame、LiDarProject、2DArpg-Portfolio源码，先读真实文件，记录来源和适配理由，不修改参考仓库本身、不搬入无用依赖。
3. 新增项目自有C#不写namespace；4空格、类与方法大括号另起行、明确方法与简单集合，参考相邻和用户已有代码。测试与Editor脚本同样遵守；asmdef如需使用，rootNamespace为空。
4. 使用英文目录，配置脚本与资源实例分开；模块内部按需要分ScriptableObject、RuntimeData、Logic、MonoBehaviour、States，不预建空目录。
5. 3D场景与模型、严格横版：玩家X左右、Y跳跃及高度、Z固定。世界物理用Collider/Physics/Rigidbody，容器格子仍是二维；相机正交/透视尚未锁定。
6. 02玩家和新09敌人沿用用户ARPG的StateBase/StateMachine、Controller与具体状态类方式。由代码切换状态并播放动画，不依赖手工连接Animator Transition；真实动画需状态和Clip资源，当前玩家胶囊占位可暂不挂Animator并标动画待接入。
7. 简单实现也要保证身份唯一、使用次数正确、失败不破坏状态及订阅清理。未实际执行的检查明确写未执行，模块测试不冒充真实联测。
8. 业务规则以ProjectRules.txt和用户最新决定为准，不以旧GDD技术结构为依据。必要共享接口按实际调用形成，不能让00提前实现全部模块。
9. 摄像机必须使用Cinemachine实际驱动。02可安装兼容包并交付最小跟随预制体，新07及后续整合沿用；不做手写跟随脚本与其竞争。只授权必要包依赖改动，保持精简。

参考源码
CardGame：https://github.com/CSduckie/CardGame
本机参考：E:/Portfolio-Card-Clone
核实提交：57eabac0f946f21ef00b489e83c1c133905ea673
重点：Items/ScriptableObject/ItemDataSO.cs、ItemLibrarySO.cs，CardDataSO的效果列表与Card Effects/Effect.cs。

2DArpg-Portfolio：https://github.com/CSduckie/2DArpg-Portfolio
本机参考：E:/Portfolio-Clone
核实提交：c601c36f95efdd4d67327489f9edb58412aa54b4
重点：Assets/Scripts/FrameWork/StateMachine/、Player/States/、Enemy/Drone/States/、PlayerController与DroneController；生命变化通知参考CharacterStats。
按本项目适配3D物理及输入；检查原ChangeState返回值与Stop/销毁清理，不能照搬缺陷。

LiDarProject：https://github.com/CSduckie/LiDarProject
本机参考：E:/Portfolio-LiDar
此前核实提交：7d691ab14ff69a85cf6be7b91cc910139205507f
参考功能目录组织与明确方法调用；使用前再次核对实际源码。上述位置或版本是参考记录，不保证工作区无改动。

文件与所有权
- 最小入口：Assets/Scripts/Bootstrap/MonoBehaviour/GameBootstrap.cs；00维护入口逻辑，新07处理最小循环正式接线。
- 必要跨模块契约：Assets/Scripts/Utilities/Contracts/；实际创建任务记录维护者和消费者，跨模块修改协调统一，不能各建一份。01可因真实治疗需要增加最小IHealthTarget。
- 共用代码状态机：Assets/Scripts/Utilities/StateMachine/；02首次适配，新09复用。玩家状态放Player/States/，无人机状态放Enemy/Drone/States/。
- 状态机保留原有IStateMachineOwner，小型宿主接口放Assets/Scripts/Utilities/Contracts/StateMachine/。移除MonoManager分发，由各Controller转发更新；新09沿用这份适配实现。
- 01拥有Assets/Scripts/Items/，排除Transfers/；Assets/Scripts/Items/Transfers/归06。
- 06同时拥有Assets/Scripts/Loot/；通过一个Loot程序集及Transfers目录下的asmref组织，避免Items反向依赖容器。06提示词明确允许补齐04/05外部提交与关闭通知，不重写独立格子算法，不新增通用事务或拖拽框架。
- 01模块预制体：Assets/Prefabs/Items/ItemModule.prefab；由自身测试场景展示接入，新07接入正式常驻入口。
- 背包与仓库格子分别归04/05，不提取共用格子核心。04实现自己的Tab/R/右键唯一业务入口及02有效受击关包；新07复用并接线，不重复监听。04拖拽保留真实占格，取消先于解锁；跨来源完整转移仍归06。
- 05独立维护仓库格子与首次扩容状态；EV从01保有实例求和，不另做加减账本。仓库内遗弃经01损失入口成功后清理本仓库布局；跨容器仍归06。05的R仅处理本仓库当前拖拽，与04保持同次拖拽单一活动来源。
- 基础测试：Assets/Tests/Base/；基础测试场景：Assets/Scenes/Tests/Base/BaseTest.unity。
- 其他测试按功能使用Assets/Tests/<Module>/及Assets/Scenes/Tests/<Module>/；01使用Items。
- 配置.cs在Assets/Scripts/<Module>/ScriptableObject/；配置.asset在Assets/Data/对应功能目录。
- 03拥有语言机制和唯一F10处理入口，新07及后续任务仅接入复用；非物品文本使用SO语言表，物品名称/描述仍只在ItemDataSO编辑，03只读现有ItemLibrarySO登记查询。允许按03提示词接入现有两个测试面板、必要程序集引用和测试场景，其他模块不另造语言系统。
- 新07集中处理最小循环正式场景、整体UI、共享预制体接线与必要ProjectSettings；新08验证修复同一实现，新10后续接风险分支。模块各自维护核心；02可添加Cinemachine的必要Packages改动，其余包沿用现状。
- 本包文件与接入边界以对应精简提示词为准。共享Unity Editor的编译、场景操作和测试串行，不覆盖其他聊天改动。

任务索引（不是已批准的并行执行安排）
00 00_Foundation_Contracts.txt：工程基础、输入、常驻入口。
01 01_Items_Identity_Effects.txt：物品配置、实例、唯一身份与治疗。
02 02_Player_Health_Interaction.txt：玩家状态机、3D运动、生命与交互。
03 03_Localization.txt：中英双语、默认中文、F10切换。
04 04_Backpack.txt：独立背包格子与局部UI。
05 05_Warehouse_Shelter.txt：独立仓库、EV和免费扩容。
06 06_Transfers_Loot.txt：物品转移、地面拾放、箱子生成与搜索。
07 07_Minimum_Loop_Integration.txt：基础流程、庇护所/废墟白盒、出发/搜刮/撤离/存取与再次出发。
08 08_Minimum_Loop_Validation.txt：连续三轮真实回归、最小修复及Windows键鼠试玩。
09 敌人AI与潜行：原07后置，复用ARPG状态机、Collider视野和掩体；完整提示词待编写。
10 风险流程整合与完整P0：死亡、损失、找回、敌人整合与最终回归；完整提示词待编写。
任务包00–10与Excel清单编号01–34不同，不要混用。

接入顺序
先读取00的实际BaseSetup.txt和00_Handoff.txt。01已交付实际IHealthTarget、ItemManager与治疗效果；02直接实现现有生命接口并做真实物品/玩家联测，不能用01的测试生命对象冒充玩家。
02以明确的新轮次准备调用初始化生命和可操作状态，测试驱动只模拟正式流程，不在重载场景时自行推进轮次；新07接入真实调用。
其他包在分发前读取实际前置实现再写接入要求，不把目录存在当作接口已经交付。04/05完成真实容器后，06接入完整操作；新07一次交付无敌人最小循环，新08验收修复，再做新09与新10。
每包报告准确修改路径、实际复用来源、配置方式、少量公开入口、真实验证结果和待接入项。报告还须解释该模块用户需要在答辩中理解的关键设计。

防止旧规格回归
撤离即结算且保留背包，满仓不阻止撤离或再出发；无强制整理确认。
唯一物品死亡、主动遗弃或地面遗留后可下轮在原点找回同一身份。
背包打开世界继续运行；医疗包使用后保留、每轮一次；满血拒绝且不消耗。
P0仅Windows键鼠，运行期间保留数据；不顺带实现磁盘存档或P1–P3功能。
