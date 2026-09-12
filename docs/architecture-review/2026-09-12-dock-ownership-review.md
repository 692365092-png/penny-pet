# Dock 成员关系与手势所有权重构审查

范围：延续 PR #4 的 `780285e`，对创建、隐藏、恢复、删除、拖拽、拆分、合并、保存及其窗口协议逐层追踪。另对编辑内容、提醒、天气和输入边界作横向抽查。本记录不等同于整个产品的 Windows 验收通过。

本轮工作检查点为 `1ed21da`，用于在工作区自动清理后保全重建的修改；该检查点尚未迁移完测试。后续提交包含完整修正、回归及本记录。

## 设计依据

一个 Dock 组需要表达成员及顺序，隐藏只是成员的可见性。组内顺序必须包含隐藏成员，当前可见邻居由该顺序筛选得到。正常操作没有理由同时维护可修改的父子图，并在两个关系冲突后猜测哪一个更完整。

保留 `DockGroupId + DockGroupOrder` 作为唯一关系的现有编码，领域操作以完整有序成员列表工作。没有再添加一份可独立修改的全局 group registry，也没有引入 v12。`DockParentId` 仅供旧文件迁移和兼容输出；运行中的布局、拆分、预览和删除都不读取它。普通查询不会根据物理位置猜成员顺序。

窗口事实、手势意图、持久偏好拥有不同生命周期：

| 数据 | 所有者及用途 |
| --- | --- |
| 成员关系与内容 | Repository 中的模型；`StickyDockGroups` / `StickyDockOperations` 执行关系变更 |
| 实际几何与捕获拓扑 | `StickyPlacementRuntime`，延续上一轮的事实接收规则 |
| 当前拖拽源、成员、基准、预览、拆分和阶段 | `DockInteractionSession`，PetForm 只读其状态并执行窗口效果 |
| 待合并关系 | 手势持有的 `DockMergePlan`；只有不可变 ID、原关系和可见性，不持有可修改的 Note 对象 |
| 待应用窗口计划 | mailbox；保留跨线程同步和最新计划替换规则 |
| 跨拖拽、恢复和拓扑事务的计划序号 | 继续独立于单次手势，不随 gesture reset 归零 |
| 主文件 revision、dirty、排队和失败 | 上一轮引入的 `StickyNoteWriter` |

`BaselineFacts` 是重定位或回退用的基准帧；`PreviewFacts` 可以包含尚未执行的目标。它们不会代替 `StickyPlacementRuntime` 的实际几何。原始鼠标按下位置和时间单独保留，拓扑重建不会重新开始长按计时。

## 从用户行为到实现的发现与修改

| 层级 / 路径 | 发现 | 实际修改 |
| --- | --- | --- |
| 删除成员 | 原删除路径只从可见链移除成员，隐藏成员可能留在另一份关系里 | `Repository.Remove` 从包含隐藏成员的完整顺序移除，再保存。删除根或中间成员的真实文件往返均有覆盖 |
| 隐藏 / 恢复 | 改可见性时重复写 Group 和 Parent，并在恢复前后修复关系 | 隐藏只改 `Visible`；恢复、显示、批量隐藏不再重建成员关系 |
| 实时查询 | 可见链走父子指针，完整组走 GroupId/Order，还通过 `SelectMoreCompleteDockOrder` 比长度 | 删除长度猜测、父链遍历和重接 helper，统一从有序组筛选可见成员及邻居 |
| 拖拽生命周期 | phase 在 Session，源、成员、起始时间、基准和拆分标记散在 PetForm | 移入 `DockInteractionSession`；集合以只读视图公开，准备、拆分、rebase、finalizing、reset 执行关联状态转换 |
| 异步收尾 | 旧 final 回调的 `finally` 无条件 reset，可能清掉重定位后的新 final 或之后的新手势 | `TryFinish(epoch, generation)` 先匹配当前 Finalizing，再一次清理；旧完成不改变当前状态 |
| mailbox 收尾 | `CompleteFinal` 对不匹配的旧序号也会清掉 `ApplyQueued` | 只允许相同 final 序号完成自己，保留新 final 或新 live 工作 |
| 合并提交 | 原代码先改关系，再等待最终窗口布局；失败时关系已改变 | 先生成 `DockMergePlan`，最终捕获包含目标组和来源组全部可见成员；最终批次及关系仍有效才发布合并 |
| 并发关系变化 | 等待窗口结果期间可能隐藏、删除或加入成员 | 合并计划校验成员 ID、原 Group/Order、可见性和完整组边界；变化则拒绝，原模型不受该合并计划修改 |
| 保存兼容 | 每次维护一条 live Parent 链，只为写入旧格式镜像 | `SerializeSnapshot` 从有序组生成 Parent 列，不修改 live 模型或 detached snapshot |
| 候选搜索 | 每个候选都重复扫描活动组和目标组，谓词实际上只有非空检查 | 删除 `CanUseDockComponents` / `IsDockParticipant`，每帧检查一次活动 ID 是否仍存在 |
| 尺寸角色刷新 | 每次先给所有窗口发默认角色，再给组成员发第二遍角色 | 一次遍历，每个窗口只发最终角色，保留根、内部接缝、末尾和隐藏窗口的原规则 |
| 测试边界 | 字段位置断言把旧拆分方式当作必须保留的架构；有索引比较会在找不到起点时误通过 | 删除两项被行为测试取代的字段检查，修正同步 arm 检查，并迁移剩余断言到实际边界 |

合并是延迟提交；长按抽出单个成员仍是用户手势立即表达的关系变更。本轮没有宣称任何失败都能撤销已经发生的整个拖拽，也没有新增一套通用回滚框架。

## 可以证明的工作量减少

- 旧 `NormalizeAll` 反复扫描成员对寻找连通分量。新实现对显式组索引并排序，v7 图仅在输入边界作邻接遍历；整体上界为 O(n log n)，正常查询不再走旧父链。
- 每帧不再为每个候选重复构建并检查整组。
- 每次 resize-role 刷新，每个现存窗口最多收到一条角色命令。
- 删除了手势结束、隐藏和恢复时的成员关系往返修复。

这些是算法和调用次数上的变化。没有测量 Windows 拖拽帧率、端到端输入延迟或低速磁盘下的卡顿，因此不声称“最高性能”。当前产品最多 100 张便签；不增加长期缓存也减少了缓存失效规则。

## 应保留的边界与后续审查

| 板块 | 本轮判断 | 状态 |
| --- | --- | --- |
| HWND epoch / topology / sequence 接收 | 跨 STA 的旧消息确实可能到达，整批预检有真实用途 | 保留；旧 final 完成的漏洞已修正 |
| 历史文件迁移、未来版本拒绝、原子替换和备份 | 这些处理外部输入及磁盘失败，承担数据保全责任 | 保留；旧 Parent 的迁移只处理尚无显式组的历史数据，显式组优先 |
| 编辑器 RTF 读写失败后保留最后成功内容及纯文本 | 避免临时渲染或序列化错误清空用户文本 | 保留；本轮不重写编辑器/IME |
| 天气网络请求超时、取消和有界缓存 | 网络确有独立失败及生命周期 | 保留必要边界；`PetWeatherSource.FetchAndStoreAsync` 用 `Task.Delay(1)` 等待注册任务，仍应改成显式注册/完成协议，尚未修改 |
| 创建 / 保存 UI | 三个创建入口仍重复，Pet 相关代码中仍有 20 处 `_notes.Save()` 调用 | 主文件已有单 writer，但 UI 仍可能等待 I/O；必须带提交结果迁移到异步调用，不能简单替换成无回执 autosave |
| 提醒 / 动画 | Core 已有产品规则与状态对象；取消过期动画有独立 generation | 横向抽查没有发现需要随 Dock 合并的状态；未完成整块重审 |
| PetForm / Windows 编排 | header gesture 字段已删除，窗口创建、restore、divider resize、显示修复和菜单接线仍很多 | PC-7 未关闭，不能把此次迁移当成完整控制器拆分 |

## 五项债务的更新状态

| 原问题 | 当前状态 |
| --- | --- |
| PC-3 Geometry Authority | 延续 PR #4 的正常实际几何链路；历史兼容字段与 Windows 验收仍开放 |
| PC-6 Dock 分布式状态机 | header drag 的状态和转换已收口，旧回调竞态已修正；divider resize、restore / topology 编排与整个 Dock 控制器独立化仍开放 |
| 保存 single writer | 主文件已实现；同步 UI 等待及 Windows 退出验收仍开放 |
| PC-6 / PC-8 双重成员关系 | 正常路径已删除 Parent 权威与修复环；GroupId/Order 是唯一关系编码，Parent 只在输入/输出边界使用；未改变磁盘版本，仍需 Windows 行为矩阵 |
| PetForm / 巨大 partial / Session | 本轮真实移除了 PetForm 的手势字段与关系修复责任；其余窗口编排仍在 partial 中，未关闭 PC-7 |

## 验证

证据在 [dock-ownership-evidence](dock-ownership-evidence/README.md)。

- 辅助执行器直接调用真实 MSTest 方法 / DataRow：393 通过，0 失败；其中 116 项标记为源码边界检查。
- 覆盖旧 v1-v11 codec、保存队列、真实 Repository/原子文件、隐藏槽位、删除根/中间成员、过期 final 回调、旧 mailbox 完成、长按阈值、rebase 不重启计时、待合并计划及关系冲突拒绝。
- 测试项目 Release 编译通过；完整产品 C# 源码使用官方 net48 引用程序集编译通过；均为零警告、零错误。
- 标准 VSTest 17.11.1 在执行前发生 `TestEngine.ShouldRunInProcess` 内部 NullReferenceException。辅助结果不等同于标准宿主通过。
- 未运行 Windows HWND、WPF/IME、焦点/Z-order、多屏 DPI、热插拔、打包和 306-key 原生自测验收。原生自测的旧反射字段和父链断言已迁移，保留输出键；不能把编译通过算成运行通过。

Windows 必须重点验证：隐藏 B 后删除 A/C 并重启；来源/目标都含隐藏成员的合并；最终布局失败时不提交合并；finalizing 期间热插拔且旧回调后到；内部接缝与整组宽度调整；混合普通、待办、日程成员；无激活恢复、输入焦点及退出保存。
