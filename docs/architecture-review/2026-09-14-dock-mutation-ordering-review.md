# Dock 最终提交与后续用户操作的顺序

基线 `04887f7`，WIP 保全提交 `0951287`，继续使用 PR #4。

## 找到的实际缺口

旧 `DeferDockResizeMutation` 只等待横向 resize final。header final 要经历一次 HWND capture 和一次最终批量放置，但这期间 Show/恢复、Close、Delete、平铺、退出和重载并不等待。它们可能改动 final 所依赖的可见性和成员关系，或者在新 preferred 尚未提交时捕获旧的恢复位置。

原保护范围也由 Pet 临时查询 resize source 当前 GroupId 决定。header 合并涉及源组和目标组，且 HWND 列表不含隐藏成员；只检查源窗口或原生 visible 列表不能覆盖实际冲突范围。

## 本轮改动

`DockMutationQueue` 统一保存捕获的成员 ID、组 ID 和等待中的用户动作。它由具体 header/resize 手势持有，替换 resize 的 `_afterFinal`，没有再增加 Pet 全局 busy 标志或另一套 generation。

| 边界 | 现在的行为 |
| --- | --- |
| header final 建立范围 | 先捕获合并两侧和拆分余组的全部成员，再过滤用于 HWND 计划的隐藏成员 |
| 横向与接缝 resize final | 共用队列；范围来自 resize 开始时捕获的完整组，包含隐藏槽位 |
| 关闭、删除、展开、平铺、退出、重载 | 经 `FindDockMutationOwner` / `DeferDockMutation`，只等待与自己范围重叠的 final；全局操作等待当前 final |
| 恢复入口 | `TryRestoreHostedDockComponent` 在迁移和捕获恢复请求之前检查，覆盖 topology restart 绕过 Show 的调用 |
| final 取得合并范围 | 取消该范围内已存在的 restore；后续独立 topology reproject 不进入该范围，晚到的相关结果也不接受 |
| 拓扑重试 | 更新 final epoch，但保留同一个动作队列和已捕获范围 |
| 成功、拒绝、异常、取消 | 先清除对应 owner/mailbox 并更新宿主 epoch，再执行等待动作；旧 epoch 的完成不能释放新队列 |
| 模型解析 | 延迟的展开、删除和关闭重新查找当前 NoteData，按已经提交的成员关系执行 |

接缝 resize 虽然已先提交源高度，followers 的最终位置和偏好仍未确认，因此也必须保护随后的恢复。此前“源高度已保存，所以无须等待”的断言已替换为等待 follower settlement 的行为测试。

header 的捕获/posting 异常现在会终止对应 final，避免留下永远不能释放的用户动作。窗口关闭和宿主故障也会结束相关等待；窗口关闭事件未通过现有 snapshot/sequence 接受规则时，不能继续删除运行时状态。失败清理捕获具体队列并检查引用身份，避免旧清理重入后清掉新队列。

原有 resize final 的同步动作释放方式继续保留：在 Pet 线程执行，不阻塞等待 HWND，也没有引入计时器、超时重试或通用任务调度器。动作会再次经过入口检查；如果前一个动作启动了新的相关 final，后一个动作可以等待那个新 owner。

## 验证

- **502 次实际 MSTest 方法 / DataRow 辅助调用通过，0 失败**，其中 **124 项**标记为 ArchitectureSourceBoundary。
- 新增 **13 个行为用例**：合并两侧及隐藏槽位、无关组独立操作、捕获组身份、拓扑重试、旧 final 不释放新队列、取消不提交合并、先提交合并再恢复、先提交 preferred 再隐藏/恢复、动作重入开启新手势、两种 resize 的完整范围。
- 新增 **4 个源码边界检查**，验证 Windows 接线、恢复准备之前等待、队列清理顺序和终止路径；这些不是 Windows 行为测试。
- net8 测试项目、完整产品 C# / 原生自测源码的 net48 官方引用程序集编译均零警告、零错误；最终 **240 个源码/项目文件哈希**一致。
- 标准 VSTest 17.11.1 仍在执行前发生内部 `TestEngine.ShouldRunInProcess` 空引用异常，辅助执行不等于标准宿主通过。

测试中“final commit 后再恢复”使用实际 Core 合并、恢复计划和队列类；Windows 的接受回调用明确的模型提交步骤模拟，没有假称执行过真实 HWND capture、WPF 输入或原生放置。

两份 PetSticky partial 从 4034 行增至 **4101 行**；生产 C# 总计净增 **118 行**，包括 53 行共享队列。这一轮补齐了原来缺失的顺序约束，并删掉横向专用队列和临时 source-group 仲裁，不是一次以减少代码行数为结果的清理。没有响应延迟或内存性能测量。

详细输出见 [验证证据](dock-mutation-ordering-evidence/README.md)。

## PC-6 仍未结案

这次统一的是 final 与相关用户可见性/成员操作之间的边界。header、resize、restore 的完整执行所有权、各自的 mailbox 和宿主 epoch 尚未合并成一个组操作执行器。

下一处具体静态路径是 `HostedStickyEventReceived` → `BeginStickyDockDrag`：新 HeaderDragStarted 会先接受 source facts，随后 `DockInteractionSession.BeginGesture` 在旧 session 仍 Finalizing 时返回 0；resize 开始捕获也会被 `_dockInteraction.IsActive` 拒绝。由代码可推断存在新手势启动丢失或旧 final 被较新 source sequence 拒绝的时序，尚未在 Windows 复现。本轮没有排队或重新播放原生鼠标事件，也没有声称解决该路径。

其他未完成事项包括：live 手势期间的全局动作规则、已有会话置顶同步、独立 topology 操作的完整所有权、原生成功后 Pet 拒绝时的完整位置补偿、其他 UI 同步 Save、PetForm 剩余生命周期编排。

Windows 必须验证：拖动松手后立即展开/关闭/删除，拖入另一组时操作隐藏页签，resize 松手后立即重开，final 中热插拔或退出，以及新 mouse-down 与旧 final 重叠。混合 DPI、IME、焦点、Z-order、打包及 306-key 原生自测仍未实际执行。PR 保持 draft。
