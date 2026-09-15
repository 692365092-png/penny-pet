# Dock 手势接替与原生执行边界

接续 `d9e23c4`。本节点修复连续操作时新手势被旧 finalizing 状态挡住的问题，
同时收拢 header / horizontal / divider 的前台手势所有权。

原实现存在两个独立的时序缺口：

- `HostedStickyEventReceived` 先接收新 header 的快照并推进 sequence，然后
  `DockInteractionSession.BeginGesture` 因旧 final 尚未结束而返回 0。新手势已经
  改变实际窗口，却没有获得 Dock 所有权；旧 final 还可能因新 sequence 被拒绝。
- 原生执行器仅检查 Pet 发布的 epoch 和 topology。如果原生窗口已开始下一次输入，
  但 Pet 尚未处理开始通知，旧 epoch 仍然有效，排队的旧计划可以移动新手势的窗口。

`DockGestureOwner` 现在持有 header session、resize session 和 header mailbox。
前台开始通知统一调用 `BeginInput`：先清空旧 session / mailbox，再返回延后动作。
控制器发布失效 epoch 后才运行这些动作；新输入随后重新检查源便签是否仍存在、可见，
以及延后动作是否通过消息循环引入了更新的输入。header 与 resize 不能同时占有前台。
旧 resize 回调仍按实例匹配，旧 header 回调仍按 epoch 匹配，不会释放新手势。

原生窗口为每次指针操作创建一个无计数器的 `DockInput` 身份。它不包含阶段、几何、
持久化状态或计时器。Session 在发送开始事件时创建它，Host 在向 Pet 排队之前记录它。
同一输入的后续事件、live/final 计划、resize 修正、final/rebase capture、Z-order
以及拆分后的剩余组归位都携带这一身份。执行入口拒绝旧输入，不需要等待 Pet 追上事件。
鼠标松开不会让旧身份重新有效；重新创建 HWND 后 sequence 归零也不会复用旧输入。

普通窗口 resize 补充开始通知，与 Dock resize 共用接替入口。header 的 `DragMove`
进入原生 size/move 循环时不额外发出普通 resize 开始通知。开始事件不依赖拓扑当前性，
几何仍由现有 WindowFacts / topology 接收规则验证；因此拓扑变化后 resize 可以保留
原输入身份，用新一代实际事实完成恢复。

产品时序明确为新输入优先：已经确认的旧操作保留；尚未确认的旧吸附 / final 计划被
取消，新手势立即获得前台。此规则也适用于另一组便签上的新输入。目前没有引入多个
组的并行 final 事务，也没有保留原始鼠标事件等待重播。

这不是 PC-6 全部结案：恢复操作、拓扑重投影及两个线程的执行与事实接收仍然存在。
它们有不同生命周期，不能仅靠删除 token 或把全部字段塞进一个类来合并。PC-3 的
旧几何字段与文件 DTO 隔离、较大的 Workspace / Session，以及退出同步屏障也仍待处理。

验证：新增 9 个实际行为用例，覆盖原生身份先于 Pet 更新时拒绝已取出的旧计划、
header / resize 接替、同窗口高 sequence 的旧事件、延后动作重入、拓扑恢复和一次修正
中的身份传递。全套 543 次 MSTest 方法 / DataRow 调用通过，0 失败。
Core、测试编译通过；Windows Core（92 个源码文件）与 SelfTests 按实际项目边界分别
编译通过，警告视为错误。

Windows 自测增加 A8，调用真实 Workspace 事件入口检查新 header 接替、旧事件不推进
sequence，以及释放的 hide 不被开始快照反向覆盖。该自测在此 Linux 环境只完成编译，
没有运行。Windows 下还需实际验证快速连续拖拽、松手后立即缩放、125% / 150% / 200%
混合 DPI、长按拆分和窗口 Z-order；本节点不声明帧率提升或已完成人工交互验收。
