# PC-2A.5 人工验收 —— 高 DPI Dock 分隔条缺陷：定位 / 分诊

> 日期：2026-09-08
> 分支：`codex/project-system-experiment`
> 分诊基线：`ee3083bdd7e65168f8305a5b685f7c5ecf64ddd7`（PC-2A.5 收口）
> 性质：**仅定位 / 分诊**，不修改任何生产行为。

## 0. 结论摘要

人工验收发现的高 DPI 分隔条重叠 / 消失问题，已经在你自己的
`diagnostics.log`（2026-09-08 19:16:50–19:16:53，DISPLAY2 @ 144 DPI /
150%）中复现。证据指向这是一个**本来就存在的潜在缺陷**，而不是
PC-2A.5 引入的回归：

- PC-2A.5 的 diff（`b9a6e79..ee3083b`）对任何分隔条相关符号
  （`ResizeHostedStickyDockDivider`、`CalculateDockMemberResizeTargets`、
  `ApplyDockTargets`、`CompleteHostedStickyDockDivider`、
  `DockDividerResize*`、`SetBounds`）**零改动**。
- 分隔条 follower 的目标位置只基于「调整开始时刻」的快照，用**只算高度差**
  的公式计算，并且从不会重新锚定到 source 当前的**实际顶部**；拖拽过程中
  source 顶部发生的任何位移，都会变成永久的重叠 / 缝隙。
- 分隔条 follower 的 `SetBounds` 是每个 WM_SIZING 帧走普通
  `PostCommand` 路径投递的（`DispatcherPriority.Normal`、FIFO、**没有
  latest-wins 合并**），完成时既不会取消还在排队的中间帧，也不会校验最终
  的堆叠接缝。

## 1. 从你自己的日志复现

手势发生在便签 `945ca87c…`（分隔条 source）上，组顺序为
`945ca87c → a5ae9f39 → b269f225`，全部位于 `\\.\DISPLAY2` @ 144 DPI：

| 时间 | 便签 | 物理矩形 | 备注 |
|---|---|---|---|
| 19:16:50.813 | 945ca87c | (2133, **-170**, 480, 452) | 手势开始：source 顶部**在屏幕外** |
| 19:16:50.815 | b269f225 | (2133, 732, 480, 450) | 开始时接缝正确：-170+452=282，282+450=732 |
| 19:16:50.855–51.161 | 945ca87c | WM_SIZING 高度 452→952 | 向上拖 |
| 19:16:51.297–51.402 | 945ca87c | 高度 952→330 | 向下拖 |
| 19:16:51.580 | 945ca87c | (2133, **0**, 480, 330) | 最终 source：顶部移动了 +170，高度 330 |
| 19:16:52.311 | a5ae9f39 | (2133, **161**, 480, 450) | follower 落点高了 170 px |
| 19:16:52.312 | b269f225 | (2133, 610, 480, 450) | 与 a5ae9f39 接缝正常（161+450=611） |

最终不变量被破坏：

```text
source 底部 = 0 + 330 = 330
follower 顶部 = 161
重叠量       = 330 - 161 = 169 px   （与你反馈的 header/堆叠重叠一致）
```

follower 的落点恰好等于「只算高度差」公式的结果：

```text
startSourceTop (-170) + finalSourceHeight (330) = 160   （+1 原生舍入 → 161）
```

正确值应是 source 最终的实际底部（330）。source 顶部在手势期间发生的 +170
位移（屏幕外校正 / 原生顶部移动），从未被传播给 follower。

## 2. 与 PC-2A.5 的 A/B 归属

- `git diff b9a6e79..ee3083b` 共涉及 9 个文件；
  `PetStickyWindowCoordinator` / `PetStickyDockCoordinator` 里的分隔条处理、
  `StickyWindowSession.SetBounds`、`StickyNativeWindowBehavior`、Core 里的
  分隔条几何，**都不在改动区域内**。
- PC-2A.5 增加的是 `StickyPlacementRuntime` 的会话失效 / 接受预检、
  `EnsureSession` 元数据，以及五个 *geometry-result* 消费者的预检。分隔条
  路径一个都没用到。
- 结论：**预期为本来就存在的潜在缺陷**。仍需要你在同一 144-DPI 环境做
  `b9a6e79` vs `ee3083b` 的人工 A/B 确认；两个 SHA 都已产出带**完全相同
  埋点**的诊断包，专门用于这次对比。

## 3. 对 8 个审计问题的回答

1. **WM_SIZING 的 requested height 是否始终是物理像素？**
   是。`StickyNativeWindowBehavior.WindowHook` 读取 WM_SIZING 的屏幕矩形
   （物理像素），用 `DeviceScaleY()` 缩放后的 min/max 夹紧，再以物理像素
   抛出 `DockDividerResizeEventArgs(requested)`。Pet 侧契约注释也明确写了
   「already-clamped physical HWND height」。

2. **Started / Resizing / Completed 的高度契约是否一致？**
   一致，全是物理像素。`Started/Completed` 用 `CurrentPhysicalHeight()`
   （真实 HWND 矩形）；`Resizing` 用夹紧后的 WM_SIZING 高度。日志已证实：
   Started=452 物理、最终 Completed=330 物理。

3. **source 实际矩形与 follower 目标矩形是否同一物理坐标系？**
   捕获时刻是。`CaptureSnapshot()` 把 canonical X/Y/W/H 派生为实际 HWND
   矩形的物理投影，`DockWindowFacts.FromData` / `FromSnapshot` 读的都是这
   些物理镜像，`SetBounds` 也按物理像素定位 HWND。此路径未发现 DIP/物理
   混用。

4. **一次快速拖拽会产生多少 pending SetBounds？**
   每个被处理的 WM_SIZING 帧、每个「有变化」的 follower 一条 `SetBounds`
   （changed 是与 canonical 做差得到的，canonical 在投递时更新，所以连续
   相同帧会被跳过）。没有合并、没有 in-flight 上限。100 帧的拖拽可能在
   `DispatcherPriority.Normal` 上排队几十条 follower SetBounds。

5. **普通 PostCommand(SetBounds) 是否会让历史帧在 Sticky Dispatcher 逐条
   执行？**
   会。`StickyUiThreadHost.Post` → `PostToDispatcher` →
   `dispatcher.BeginInvoke(DispatcherPriority.Normal, …)`，每条命令 FIFO。
   `SetBounds` 没有任何 mailbox / 合并。

6. **mouse-up 之后旧 follower SetBounds 是否仍可能执行？**
   会。`DockDividerResizeCompleted` 会再投一条 follower 批量命令并清掉 Pet
   侧 resize session，但已经排队的帧不会取消，它们按 FIFO 执行（最终帧排
   最后，单纯 FIFO 顺序下通常是对的）。一旦某条排队帧失败，它的完成回调
   会清掉 resize session 并上报；之后的帧和 completed 处理器发现 session
   已消失，就停止移动 follower——事后没有任何接缝校验。

7. **观察到的症状（最终帧之后的陈旧移动 / 重叠 / 缝隙 / 出屏 / 可见性 /
   z-order）？**
   重叠：已确认（169 px），由「只算高度差」的公式完全解释。手势开始时
   source 顶部在屏幕外（-170）也直接在日志里看到——它的 header 在屏幕之
   外，与「header 被挡 / 消失」的反馈一致。可见性 / z-order：本组日志未
   观察到变化。

8. **「消失」时的真实 HWND 事实：**
   日志在手势边界已记录（WindowFacts trace）：DPI 144、gdi `\\.\DISPLAY2`、
   有 generation/target。缺口：日志此前**没有**逐条 `SetBounds` 的
   requested-vs-actual 事实、Pet 侧的分隔条事件序列、follower 可见性——
   本次分诊补上了这些埋点（`DockDividerGesture`、`DockDividerEvent`、
   `DockDividerFrame`、`DockTargetPosted/Completed`、
   `DockDividerCompleted`、`DockSetBoundsApplied`）。

## 4. 根因

分隔条 follower 管线里有两个相互独立的缺陷：

### RC-1：follower 目标只算高度差，锚定在「调整开始时刻」的快照上

`StickyDockGeometry.CalculateDockMemberResizeTargetsExact`：

```csharp
delta = finalSourceHeight - startBounds[source].Height;
follower.Top = startBounds[follower].Top + delta;
```

这等于固定住 `follower.Top - source.Top = startSourceHeight`，并假设 source
顶部永远不会动。你的日志证明 source 顶部在手势期间从 -170 移到了 0，所以
最终堆叠恰好差了这个位移（170 px）——哪怕所有 SetBounds 都成功。
`ResizeHostedStickyDockDivider` 和 `CompleteHostedStickyDockDivider` 都基于
冻结的 `_activeHostedDockResizeFacts` 计算，从不在帧时刻或完成时刻重新锚定
到 source 当前的**实际物理矩形**。

### RC-2：无合并的 Normal 优先级积压，且没有最终屏障

逐帧 `PostCommand(SetBounds)` 没有 latest-wins 语义（不像 Dock 拖拽路径，
后者用 `DockPlanMailbox` + `PostDockPlan`）。完成时又基于同一份陈旧 start
facts 投最后一批、清 session，既不等待也不校验 follower 的 HWND 事实。任何
一条失败或被超越的中间帧都会让 follower 停在堆叠中间（缝隙/重叠/跳出
stack），且没有自愈。

## 5. 建议的最小正确性修复（本次分诊不实现）

latest-wins 的分隔条 follower 效果 + 最终精确屏障，仿照现有的 Dock 拖拽
mailbox：

1. WM_SIZING 期间：合并 follower 帧——只保留最新请求帧；同一时刻最多一条
   deferred follower apply 在飞。
2. WM_EXITSIZEMOVE 时：捕获**最终的 source 实际物理矩形**（顶部 + 高度，
   不是陈旧的 start facts）；丢弃 / 替换所有 pending live 帧；重新锚定到
   最终 source 矩形，应用一次最终的 follower 布局；随后捕获 follower 实际
   HWND 事实并校验接缝不变量 `next.Top == previous.Bottom`（≤2 px 原生
   容差）。
3. 被拒绝 / 失败的 apply 不能静默地把 follower 丢下；resize session 在
   最终批量结果解决前不得清空，completed 处理器必须「校验」而不是「假设」。

禁止的捷径（按 runbook）：Task.Delay/Sleep、随意 debounce 常量、没有替换
屏障就忽略旧结果、改全局 sequence、无证据改 DPI 公式、用 Hide/Show 掩盖
重叠、全局 dispatcher 优先级 hack、mouse-up 后 NormalizeAllDockGroups 掩盖
竞态。

**是否值得引入 latest-wins 分隔条 mailbox / 最终屏障？** 值得。RC-2 与拖拽
路径已经用 `DockPlanMailbox`/`PostDockPlan` 解决的「多帧、单一原生效果队
列」是同一形状；RC-1 还额外要求最终 apply 必须重新锚定到捕获到的最终实际
source 矩形。

## 6. 本次分诊新增的埋点（仅 trace，不改行为）

- `DockDividerGesture`（Started/Completed：source 物理矩形 + scale）
- `DockDividerEvent`（session kind/seq/height）
- `DockResizePhysical`（在原有 height trace 上补充 `top=`）
- `DockDividerFrame`（Pet：source 高度 + 变化的 follower 数量）
- `DockTargetPosted` / `DockTargetCompleted`（分隔条 follower SetBounds 的
  投递 / 完成，状态 + sequence）
- `DockDividerCompleted`（Pet 最终接受结果 + 高度）
- `DockSetBoundsApplied`（每条 SetBounds：requested 矩形 vs 实际 HWND 事实、
  DPI、gdi、generation、sequence）

## 7. 下一步

在两个诊断包（`ee3083b` 基线与 `b9a6e79` 基线）上，于 144/150% DPI 环境做
人工 A/B 复现。如果 `b9a6e79` 也复现，则确认是既有缺陷，RC-1/RC-2 的收口
可以作为新的窄 checkpoint 进行。如果 `b9a6e79` **不**复现，则 STOP，先在
任何修复之前重新审计 PC-2A.5 是否存在间接回归。
