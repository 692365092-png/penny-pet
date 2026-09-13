# Dock 横向尺寸调整：统一手势，接收实际结果

本轮继续 PR #4，以上一轮已验证的 `00b7067` 为比较基线，把横向 resize 接入同一个尺寸手势所有者。`c0ad4c5` 只是工作区清理后的保全检查点；本文及配套证据记录补齐行为回归后的结果。延续用户提供的 `ab522d9` 补丁整合，没有整体合入 PR #3。

## 先确定用户操作的含义

拖动组内任意窗口的左右边缘，表达的是一个物理左边界和宽度。源窗口由 Windows 的原生手势改变，其他可见成员跟随；它们各自的纵向位置和高度不能被源窗口的尺寸覆盖。松手后，用实际 HWND 结果决定保存值，紧接着收起或退出也要看到这个结果。

这需要区分三件事：WM_SIZING 提出的目标、Windows 已经实现的矩形、下次恢复使用的偏好。它们是不同时间含义的数据；问题在于旧路径用源窗口的一份逻辑尺寸替代所有成员的实际结果，又让多个入口自行推断何时结束。

## 由外到内的发现与修改

| 层次 / 操作 | 旧路径的问题 | 已实施的变化 |
| --- | --- | --- |
| 松手后立即收起 | 源窗口已完成，组根窗口的 final 仍在另一个 STA 排队；收起可以先读到旧根宽度 | 相关隐藏、恢复、删除、展开平铺、退出和运行时重载，在横向 final 期间登记后续操作；当前会话结束并提交可接受的最终事实后执行 |
| 混合 DPI 下恢复尺寸 | `CommitDockGroupResizePreferred` 把源窗口的逻辑 X / Width 复制给所有可见成员 | 删除该方法；每个成员分别用自己的最终 WindowFacts、HWND DPI 和捕获 topology 生成偏好 |
| 连续横向拖动 | `ResizeStickyDockGroup` 每帧重建目标，再逐窗发送 SetBounds | 删除独立入口；横向与接缝共用 latest-wins 邮箱和显式 start / live / final 生命周期 |
| 宿主窗口效果 | 每个 follower 调用完整 SetBounds 路径，包含 Show / 恢复窗口状态 / UpdateLayout 及重复采集 | 一次原生批量放置处理所有 follower，随后每个窗口采集一次结果；源 HWND 不在批次内 |
| 高频事件 | 横向 resize 与普通 BoundsChanged 同时上报；屏蔽后若不补事实更新又会留下旧源矩形 | 手势期间屏蔽普通几何回声；已接受的 resize 事件更新源的实际事实，目标矩形只用于规划 follower |
| 几何偏好规则 | 纵向边缘的普通完成事件也会传播组宽度；跨屏后可能沿用旧屏的局部坐标 | 普通完成事件只提交源窗口；同屏时保留手势未改变的偏好维度，换屏时从实际矩形重建局部坐标；角落 resize 保存源的完整矩形 |
| Dock 拖动保存 | 最终回调在 Pet UI 上同步等待 `_notes.Save()`，且返回值未使用 | 改为已有 writer 的 SaveAsync；显式保存、导入和退出持久化屏障继续使用原有结果处理 |
| 会话快照 | 两个不同名字的 helper 实际都调用 FromContentData | 合并为一个 CaptureSnapshot；原来就不是每帧序列化 RTF，这一点不计为性能优化 |

举例：三个窗口实际宽度同为 900 像素，实际 DPI 分别是 96、144、192，它们保存的逻辑宽度应为 900、600、450。显示器声明的 scale 和源窗口的逻辑宽度都不能替代各自的 HWND DPI。

## 状态归属

| 数据 / 决策 | 唯一归属 |
| --- | --- |
| 尺寸手势的 source、类型、成员基准、阶段、最终目标、一次更正机会、后续操作 | `DockResizeSession`，位于 Pet 线程 |
| 等待执行的最新 live 和具体 final 批次 | `DockResizeMailbox`，仅负责跨线程传输 |
| 实际窗口身份、矩形、DPI 和捕获代次 | HWND 采集产生 WindowFacts，由现有 `StickyPlacementRuntime` 接收 |
| 一个成员的持久尺寸偏好如何计算 | `StickyResizePreferences` 纯规则，无窗口调用和模型写入 |
| 原生批量效果和结果采集 | `StickyUiHost` / Sticky STA |
| 主文件队列、快照、dirty 和失败状态 | 已有 `StickyNoteWriter`，未新增第二个 writer |

旧 `DockDividerResizeSession` 扩展并更名为 `DockResizeSession`，没有另建横向控制器。PetForm 仍只持有一个尺寸会话引用。Mailbox 完成不代表手势完成：Pet 必须接收当前 session、当前 final 的完整事实，才结束会话。一次更正替换 final 身份，旧回调不能清理更正或新手势。

只有横向 final 的依赖操作需要延后。内部接缝已经在源窗口完成时提交新高度，保持既有取消行为。延期是登记回调，不阻塞 Pet 线程；失败或取消也会释放回调，使用最后已接受的状态。清空当前 owner 后再执行回调，避免递归登记到已经结束的会话。单个后续操作失败沿用窗口错误呈现，其他已登记操作仍可执行。

## 简化与可证明的收益

对于 N 个可见成员，一帧横向 follower 效果由 N−1 个独立宿主命令变成至多一个待执行批次；后来的 live 覆盖未取出的旧帧。原生移动与结果采集仍是 O(N)，没有宣称消除遍历。避免完整 Show / SetBounds 路径和重复采集，是可以从调用链确认的变化；没有 Windows 帧率或输入延迟数据，不宣称“最高性能”。

删除了旧横向调度、源逻辑宽度广播、WPF 未使用的 DIP 左边缘重建辅助以及重复内容快照 helper。新增代码主要承担原路径缺少的 final 接收、各窗口偏好和动作顺序。两个 PetSticky partial 仍合计 4509 行，StickyWindowSession 为 993 行；这轮没有完成 PetForm 的整体责任拆分，也不以行数降低作为架构完成的证据。

必要的跨线程检查继续保留：成员身份和顺序、topology、窗口 sequence、当前会话、具体 final，以及一次更正上限。这些检查对应实际迟到回调和窗口生命周期问题；删掉它们会重新允许旧结果覆盖新状态。

## 验证及明确限制

见 [horizontal-resize-evidence](horizontal-resize-evidence/README.md) 的具名结果、源码哈希与编译日志。

- 440 次真实 MSTest 方法 / DataRow 直接调用通过，0 失败，其中 118 项标记为源码边界检查。新增 30 个行为用例，增加两个集中边界检查并移除两个绑定旧实现的断言。
- 新行为覆盖首 / 中 / 尾成员作为源、物理宽度不被二次逻辑限幅、49 帧合并、实际源与请求值分离、跨轴 / 过期 / 错代事件、final 与更正身份、两像素容差、部分 / 错序批次，以及延期操作的顺序与取消。
- 使用真实 repository / writer / codec 做立即收起后的磁盘往返，确认根和源各自保存 DPI 对应宽度，隐藏状态及 GroupId / Order 保留。它测试的是模型和持久化链路，不是 WPF 鼠标操作。
- net8 测试项目和完整产品 C# / 原生自测源码的官方 net48 引用程序集编译通过，均为零警告、零错误。
- 标准 VSTest 17.11.1 在执行前出现 `TestEngine.ShouldRunInProcess` 内部 NullReferenceException；辅助执行不能称为标准测试宿主通过。
- 未运行 Windows HWND / WPF / IME、焦点、Z-order、混合 DPI 跨屏、热插拔、打包或 306-key 原生自测。原生批量效果失败可能已部分生效；现有处理拒绝不完整回写，没有增加通用窗口回滚。
- 已取出的批次不能靠取消邮箱撤销。更正后仍偏离时记录实际事实和诊断，不无限重试。topology 取消、宿主故障或事实缺失时，不能保证紧接着的收起保留尚未确认的整组宽度。

Windows 验收应覆盖左右边缘及允许的角落、从中间成员发起、连续快速松手 / 收起 / 恢复、final 后删除或退出、多个实际 DPI、拖过屏幕边界及松手前后热插拔。新版诊断使用 `DockResizeFrame`、`DockResizeCompleted`、`DockResizeFinalRejected`、`DockResizeLayoutVerifyFailed`；原生接缝的 `DockDividerGesture` 保留。

## 仍开放的架构债

| 债务 | 当前结论与后续边界 |
| --- | --- |
| PC-3 几何 authority | 正常 resize 已使用实际事实；物理 / v10 / preferred 兼容字段仍在模型中。后续应继续收紧恢复和迁移边界，不能称为删光多份几何 |
| PC-6 Dock 状态 | header 与两类 resize 各有明确手势 owner；restore / topology 的组事务和整体效果编排仍分散 |
| PC-6 / PC-8 成员关系 | 延续 GroupId / Order 为运行时关系、DockParent 为加载迁移和导出投影的既有改造；本轮没有再次改磁盘格式 |
| 保存 | 主文件 writer 的独占状态延续；本轮仅消除一个高频用户路径中的 UI 同步等待。其他同步 Save、退出失败处理仍需逐路径审查 |
| PC-7 / 大型 PetForm | 4509 行 partial 仍包含创建、恢复、临时重定位、退出和 UI 接线。下一步应以完整的组恢复事务为边界迁出实际决策，不抽只转发私有方法的 manager |

PR 保持 draft，等待上述原生验收和后续责任收口；不将这一个尺寸手势改造写成 PC-6 或 PC-7 整体结案。
