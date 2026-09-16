# Geometry Authority 与 ab522d9 保存补丁整合审查

后续状态：本文对应 `780285e` 整合检查点。随后完成的 Dock 成员关系、拖拽状态和竞态修正，见 [Dock 所有权审查](2026-09-12-dock-ownership-review.md)。
范围：相对 `codex/project-system-experiment` 的 `d8b682c`，延续 `43e088c` 几何检查点，审查并整合用户提供的 `penny-pet-ab522d9.patch`。补丁内容先以 `dff526b` 保存，随后在 `b0cb8b7` 修正文件所有权、紧急导出及测试边界。本分支不是 PR #3 的整体合入。

## 结论

补丁确实把 Save、SaveAsync、导入和完整恢复的主文件写入排序收到了一个 owner；这比删除重复判断更接近根因。但“所有文件共用一条队列”不是正确的目标。单写入者应对应同一个被修改的资源，独立导出不应依赖已经卡住的主文件写入。

当前可以确认的是几何运行链路和主文件写入链路的代码改动。Dock 状态所有权、双重成员关系及 PetForm 责任拆分仍未完成。同步 Save 的调用方仍会等待 I/O；此次没有把它等同于 UI 已完全无阻塞，也没有宣称取得最高性能。

## 补丁审查与采用决定

| 内容 | 采用结果 | 原因与实际修改 |
| --- | --- | --- |
| 一个队列排序主文件写入 | 采用 | `StickyNoteWriter` 独占 requested/saved revision、pending、dirty 和写入错误；Repository 不再同时维护同步、异步两套 generation 和 I/O 锁 |
| 只合并相邻、尚未执行的自动保存 | 采用 | 显式 Save 和导入是顺序屏障，各有完成结果；失败不会丢掉后续已经入队的新快照 |
| 导入先备份和写主文件，成功后替换内存集合 | 采用 | 备份失败时不发布新数据集；旧错误不应永久短路一次新的重试 |
| 从每次保存中移除 NormalizeAll | 采用并验证边界 | 普通保存只序列化模型线程捕获的深拷贝；载入、迁移和导入验证仍负责修复。它不代表 Dock 的双重关系已消失 |
| 导出也入主队列 | 修正 | 后台 I/O 卡住时，退出后的紧急导出会跟着无限等待。独立目的文件的导出直接写其快照，不更新主文件 revision 或 dirty |
| SaveToFile 任意目的路径都算 workspace 成功 | 修正 | 另存副本路由到导出；副本成功不能清除主文件保存失败。导出拒绝覆盖正在使用的主文件及其滚动备份 |
| AtomicTextFile 进程级全局锁 | 移除 | 即便导出绕过上层队列，这把锁仍会阻塞它。每次写入使用同目录唯一临时文件，保留原子替换及滚动备份；同一路径的写入次序由该文件 owner 保证 |
| 直接合并 HostedRuntime 和 PlacementRuntime | 暂不照搬 | 共用字典并不自动合并状态机。session/lease、effective 水位和临时重定位意图的生命周期必须分别保留；HWND 销毁后应清掉事实水位，但不能误删重定位意图 |

原始上传补丁不作修改。此文记录采用后的实现，替代补丁内检查点文档中“所有导出都排队”的结论。

## 与 Geometry Authority 的结合

| 数据 | 当前职责 | 禁止成为的内容 |
| --- | --- | --- |
| Preferred target + logical rect | 用户选择的持久放置意图 | 临时重定位不能自动改写它 |
| WindowPlacementPlan | Pet 侧选好的期望位置，跨 STA 传递 | 不能在 HWND 应用成功前冒充 actual |
| WindowFacts + 捕获时 topology | 运行中的实际位置、尺寸和 DPI | 不能用之后发生变化的显示器原点重新解释旧物理坐标 |
| 物理 X/Y/W/H 与 v10 字段 | 历史恢复输入、序列化兼容镜像 | 不能在正常 Dock 规划、预览、遮挡判断或完成事件中兜底替代实际事实 |
| 保存快照 | 模型线程的一次深拷贝 | 后台 writer 不能查询窗口、修复 live 模型或重新决定 Preferred |

已经改变的调用链：

- `StickyPlacementRecovery` 选择 Preferred → 有效 v10 → 物理恢复；`StickyWindowSession` 不再自行选择这些持久字段。实际投影仍使用到达目标屏幕后取得的 HWND DPI。
- 正常 Dock 规划使用每个成员的实际高度和各自 HWND DPI，以及拖动源的实际宽度。缺少成员事实时不偷偷混入旧持久坐标。
- 运行时保留事实对应的 capture topology。新拓扑修复需要旧帧逻辑尺寸时，使用旧帧自己的原点和比例。
- 普通命令、几何事件和退出收尾共用实际事实接收路径；STA 输出内容快照，退出最终快照显式携带 facts/topology。
- 分隔线完成使用实际最终高度；WM_SIZING 的提议高度仅用于实时调整。不能拿上一帧 runtime 高度提交最终偏好。
- 下发 Dock 目标不再提前写成实际几何。预览接缝取消对已经是物理像素的坐标再次乘 Pet DPI。
- 保存队列只接受上述模型状态的深拷贝；它不参与几何决策。

仍保留的恢复例外包括未建立 HWND 的初始化、历史独立窗口恢复、旧 Dock 数据恢复以及找不到首选屏幕时的有界位置选择。持久格式仍为 v11，没有把 v10/物理兼容字段从磁盘格式中删除。PC-3 的 Windows 验收及兼容镜像的最终退役仍是开放项。

## 五项架构债的真实状态

| 用户指出的问题 | 本次状态 | 关闭条件 |
| --- | --- | --- |
| Geometry Authority / PC-3 | 正常几何读取和写回链路已迁移；未完成全平台验收 | 完成混合 DPI、热插拔、恢复、拖动、调整尺寸的 Windows 矩阵；明确保留的恢复与序列化边界 |
| Dock 分布式状态机 / PC-6 | 未完成 | 一个 owner 管理 gesture phase/source/members/baseline、失效、finalizing 和收尾；删除 PetForm 中对应重复字段 |
| 保存 single writer | 主文件队列已实现；调用方阻塞仍存在 | Windows 退出、重试、导入/恢复验收；后续把耗时等待移出 UI 调用链时保留提交/关闭顺序 |
| DockParent 与 GroupId/Order 双关系 / PC-6、PC-8 | 未完成，已明确补入验收 | 一个包含隐藏成员的 ordered group 是成员关系事实；Parent 和旧字段仅在兼容边界生成；删除往返修复环 |
| PetForm、PetSticky partial、StickyWindowSession 过大 / PC-5、PC-7 | Session 删除了部分真实职责；PetForm 未完成架构拆分 | 生命周期和 Dock 控制器独立拥有状态与行为；PetForm 只负责组合、UI 适配和事件接线，不能靠互相调用私有方法的转发类假拆分 |

体量对比不是完成证明：Session 从 1,218 行到 987 行；两个 PetSticky partial 合计从 5,106 行到 5,004 行，仍然很大。Repository 从 976 行到 802 行，另有 169 行 writer；收益在于写入次序和状态集中，而不是把挪出的行数算成删除。

## 后续架构顺序与边界

1. 以一个完整 `ordered NoteIds` 列表表达 Dock 组，保留隐藏成员的位置、稳定 ID 和各自高度。一次拓扑操作完成后生成需要的兼容字段。不能再增加第四份可独立修改的成员图。
2. 收拢 Dock gesture 所有权。baseline 是一个手势开始时的不可变帧，actual 是运行时接受的最新帧；mailbox 只运输计划，不另行决定状态。restore/topology 的事务生命周期和跨事务 PlanSequence 分配不能盲目塞进 drag 生命周期。
3. 任何批量提交仍然先完整验证，再一次修改。reject 必须不改 canonical、effective、lease、membership 和保存状态；预检与提交之间不能 await、调用 native 或重入。
4. 将生命周期、Dock 行为从 PetForm 移至真正的控制器，删除原字段及其迁移转发。保持 IME、焦点、无激活显示和 Z-order 合同。
5. 待正常读取完全闭合，再决定兼容镜像是否值得退役；此次不引入 v12。导入仍默认 merge，保留现有提醒归属、备份及未来版本拒绝规则。

这些是尚待实施的条件，不是已有实现清单。不得用“合并 runtime”或“拆分 partial 文件”直接替代验收。

## 验证与限制

实际证据见 [evidence](integration-evidence/README.md)。完整 Windows 源码使用官方 .NET Framework 4.8 引用程序集编译成功；这验证 C# 类型和引用，不运行 Windows 消息循环。

MSTest 方法和 DataRow 由辅助运行器直接调用，使用真实 MSTest 断言。包含旧版本 codec、几何行为、源码边界、单 writer 及真实 Repository/原子文件 I/O 回归。Repository 测试仅替换 Windows 诊断输出端，没有替换保存、队列或文件实现。

标准 VSTest 17.11.1 在开始执行之前发生 `TestEngine.ShouldRunInProcess` 内部 NullReferenceException；辅助运行结果不能冒充标准测试宿主通过。HWND、IME、焦点/Z-order、实际多屏 DPI/热插拔和完整 Windows 打包验收尚未运行。因此 PR 保持 draft，不宣称无功能回归或性能最优。

移除 AtomicTextFile 全局锁后，该 helper 只提供每次替换的原子性，不再为同一路径的多个独立 owner 排序。应用必须保持同一主文件只有一个活跃 Repository writer；现有应用单实例入口保留。
