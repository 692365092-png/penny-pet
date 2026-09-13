# Dock 尺寸调整：收拢生命周期，撤销过期效果

本轮在 PR #4 的 `1931662` 上继续实施，延续用户提供的 `ab522d9` 补丁整合及上一轮几何、writer、成员关系和 header gesture 改造。重点是内部接缝 resize 的实际状态所有权；整组横向尺寸调整只完成调用链审查，尚未重构。

## 从用户操作确定边界

内部接缝有四项必要行为：Windows 直接改变被抓住窗口的高度；下面的成员跟随移动并保留各自高度、宽度；松手后按源窗口最终实际位置重新接齐；立即隐藏再恢复必须读到新高度。

这只需要一个短生命周期对象。它拥有源窗口、成员基准、当前阶段、最终源矩形和一次更正机会。跨 STA 的邮箱负责传输，实际几何仍由 `StickyPlacementRuntime` 接收。主文件 writer 继续独立管理 I/O。合并这些不同线程、不同寿命的责任不会减少真实复杂度。

## 本轮发现及修改

| 用户可见路径 | 原实现的问题 | 已实施的变化 |
| --- | --- | --- |
| 开始或松手时遇到旧事件 | 先清空 resize 字段，再验证事件；无关窗口关闭也清空全局 resize | 验证通过才替换会话；关闭只取消包含该窗口的会话；无效 completion 不清理新会话 |
| final 执行完、Pet 尚未接收回调 | STA 调用 `CompleteFinal()` 后重新放行 live；迟到帧可能排在 final 后面 | 邮箱开始 final 后永久关闭 live，直到该对象结束；Pet 会话决定收尾何时完成 |
| 删除、隐藏、恢复、拖拽或退出打断 resize | 清空引用不撤销邮箱；排队批次仍可取出并移动窗口 | `Finish()` 关闭邮箱并清掉未取出批次；相关入口明确取消受影响会话 |
| 取消之后旧 final 回调到达 | `current == null` 被当作允许写回的情况 | 回调必须匹配当前 session 及该 session 当前 final 对象；空会话不再放行 |
| 一次更正取代第一次 final | 原 final 无身份参数，取出和完成无法区分原批次与替代批次 | `TakeFinal(expected)` / `CompleteFinal(expected)` 只处理自身，旧完成不清掉更正 |
| 原生响应不完整或次序不符 | 仅提取矩形后按基准位置对应成员，可能把其他成员的尺寸用于更正 | 更正和写回之前检查完整有序成员、事实 ID / sequence / topology、当前成员关系和两层事实水位 |
| 屏幕拓扑改变 | resize 基准没有 topology 身份，live 将旧基准包装成当前 generation | 基准携带 `WindowFacts`，开始时要求所有成员同代；topology barrier 取消旧会话。有效 completion 只用当前代的已接受事实尝试重新收尾 |
| 无完整会话时松手 | fallback 仅应用快照，漏掉独立的 preferred-height 提交 | 有效源窗口仍在当前 Pet turn 提交实际几何及新 preferred 高度，再异步保存 |

`DockDividerResizeSession` 实际执行 live 规划、进入 final、验证 follower 身份、按实际尺寸更正和结束。PetForm 中原有三份字段被删除；两个 partial 不再自行扫描基准重建 final / correction，也不再以 mailbox 的瞬时状态推断手势阶段。

`CalculateDockMemberResizeTargets` 的 PetForm 包装已无生产调用，删除该包装，并把原生自测接到真实 session + mailbox 的 200 帧循环；输出键保留。纯几何算法仍在 `StickyDockGeometry`。

## 保留的功能和必要保护

- live 使用 WM_SIZING 已处理的物理高度，不再次套用 220–700 的逻辑范围。源 HWND 不进入 follower 批次。
- 每个 follower 保留自己的物理高度、宽度；final 按源窗口实际 top / height 接齐；原有 2 像素接缝容差及最多一次更正保留。
- 更正使用经过身份与事实水位检查的实际矩形，不用旧基准尺寸覆盖原生结果。
- 源窗口的新 preferred 高度在发出 follower final 前写入内存模型；磁盘通过现有 SaveAsync 排队。源与 follower 可产生两个快照，writer 可以合并相邻 autosave；不宣称每次手势只有一次磁盘写入。
- 只关闭未取出的旧批次。已经开始执行的原生效果无法由清空邮箱撤销，回调的所有权检查阻止其污染当前模型；没有增加通用原生回滚机制。
- topology 后若 follower 的当前代事实尚未接收齐，completion 只提交有效源窗口；整组恢复仍依赖已有 topology reconcile。不能把该恢复分支写成所有热插拔时序已经验收通过。

## 简化与性能的可证明部分

基准矩形及源索引只在开始时构建，删除每帧重建整组基准及 `DockLayoutTarget` 到 `DockWindowTarget` 的中间转换。live 仍采用最新帧替换；没有新增长期缓存、全局锁、独立 revision 或跨线程等待。

两个 PetSticky partial 合计从 4797 行降到 4534 行，新会话为 184 行；计入会话、邮箱和宿主变化后，生产 C# 合计净减 77 行（不计测试与原生自测）。行数只是核对手段，关键变化是生命周期规则有唯一 owner。没有测量 Windows 帧率或输入延迟，不作“最高性能”的结论。

## 横向调整与后续债务

| 板块 | 审查结论 / 尚需完成 |
| --- | --- |
| 整组横向 resize | `ResizeStickyDockGroup` 仍逐帧逐窗 `SetBounds`，没有成组 latest-wins / final 接收；这是可见卡顿与完成顺序问题的下一处目标 |
| 横向 preferred | `CommitDockGroupResizePreferred` 把源窗口逻辑 X / Width 传播给成员；混合实际 DPI 时，统一物理宽度应分别用各成员最终事实转换。本轮未修改此语义，需连同横向 final 批次一起处理 |
| PC-6 | header gesture、内部接缝 resize 各自具备明确 owner；restore / topology 事务、横向 resize 和整体 Dock 效果编排仍开放 |
| PC-7 | PetForm 已失去内部接缝基准、final 和更正责任；创建、恢复、退出与 UI 接线仍大量留在 partial，未完成整体解耦 |
| PC-3 / PC-8 | 延续实际事实与兼容边界的隔离；历史几何列及 Windows 兼容验收仍开放。没有改变磁盘版本 |
| 保存 | 主文件 single writer 已存在；UI 中同步 Save 的等待、退出与错误呈现仍需继续迁移 |

下一步应围绕“一个物理横向尺寸意图 + 一次最终事实接收”处理横向 resize，再拆独立的恢复事务和 Sticky 生命周期。避免先抽一个只转发 PetForm 私有方法的新 manager。

## 验证与可复查证据

[resize-ownership-evidence](resize-ownership-evidence/README.md) 包含源码哈希、具名测试结果及编译日志。

- 410 次直接 MSTest 方法 / DataRow 调用通过，0 失败，其中 117 项源码边界检查。新增覆盖取消、旧完成、更正一次限制、成员变化、top 漂移、独立尺寸和 topology 来源。
- net8 测试项目、完整产品 C# 及原生自测源码的官方 net48 引用程序集编译通过，均为零警告、零错误。
- 标准 `dotnet test` 再次在执行前出现 `TestEngine.ShouldRunInProcess` 内部 NullReferenceException。辅助执行结果不等同于标准宿主通过。
- 没有 Windows 环境，尚未运行 HWND / WPF / IME / 焦点 / Z-order / 混合 DPI / 热插拔 / 打包及 306-key 原生自测。PR 保持 draft。

Windows 重点验收：内部接缝连续变高变矮、从部分屏外位置松手、final 等待时立即隐藏再恢复、删除当前或无关成员、final 后立即 header drag、100% / 150% / 200% 成员混合、resize 中及松手后的热插拔。确认源高度持久化、独立成员尺寸、组接缝和输入焦点均保持。
