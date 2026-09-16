# 旧格式 Dock 恢复并入整组事务

基线：PR #4 的 `3ae78f6`。这是普通整组恢复完成后的后续重构；没有改变磁盘格式，也没有合并 PR。

## 实际消除的问题

`TryRestoreHostedDockComponentLegacyFallback` 在原生成功前就修改整组 `Visible`、`AlwaysOnTop` 并登记会话；每个成员分别 Create/Show，收到回调后再 SetBounds、设置 resize role、Show。后续命令结果被忽略，失败依赖另一套 pending/createFailed 汇合，保存仍同步执行。这条路径绕过了普通恢复已建立的组所有权、取消和整批校验。

本轮删除这个执行分支。缺少完整 preferred 的组仍可恢复，但旧字段只在 `StickyPlacementRecovery.SelectDockPhysical` 转换成一次性请求。新旧格式都由 `DockRestoreOperation` 持有生命周期，都发送 `RestoreDockGroup`，由同一原生宿主准备窗口、应用批次、显示、采集结果和处理失败。

| 责任 | 现在的唯一入口或依据 |
| --- | --- |
| 格式选择、成员身份、快照和取消 | `DockRestoreOperation` 与已有的组操作注册表 |
| 旧字段解释 | `StickyPlacementRecovery.SelectDockPhysical`，只在恢复入口使用 |
| 目标、成员顺序和布局输入 | 不可变 `DockGroupReprojectPlan`；logical 和 physical recovery 由不同构造入口保证互斥 |
| HWND 准备、批量放置、失败回滚 | 原有 `StickyUiHost.ApplyDockGroupReproject`，没有旧格式执行分支 |
| 会话登记、Visible、兼容几何写回 | 原有完整批次预检及接受流程，几何来自实际 WindowFacts |
| 迁移和可见性保存 | 现有异步 writer |

`MemberIds` 从请求导出，恢复操作不再额外生成一份 ID 列表。宿主只遍历这些 ID，不需要读取逻辑几何或辨认存储格式。resize role 刷新和编辑器焦点仍是成功后的公共后续操作，不能把它们计入“整次交互仅一个命令”的声明。

## 兼容语义

- 旧 X/Y/W/H 保持物理像素含义。恢复目标不会乘以目标 DPI，也不会先换算成整数 DIP 再投影，避免放大和舍入接缝。
- 继续使用根成员宽度，保持原有 280–900 像素宽度限制；各成员高度独立，保持原有 220–700 像素限制。
- 继续只保证根标题栏可触达：至少 64 像素宽度、32 像素标题栏高度，不把整个长组强行压入屏幕。
- 显示器选择改为使用请求捕获的 topology，先比较标题栏重叠面积，完全离屏时选择最近表面。此选择意图对应 [MonitorFromRect 官方说明](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfromrect)。距离实现采用矩形间的平方距离；等距优先主屏。没有证明与 Windows 对所有平局情况逐位相同。
- 完整 preferred 仍走原来的逻辑投影；preferred 缺屏时仍保留其临时居中策略。部分 preferred 不会被物理恢复改写成猜测的偏好。
- 新创建会话的脱离模型快照继承根成员置顶状态；原始模型的置顶与可见性等待整批接受。这保留了旧分支对新建成员的组置顶语义。
- 真实目标 DPI 仍在 HWND 到达目标屏后采集。物理恢复也必须通过 DPI、目标表面、代际、成员、sequence 等相同校验；“不缩放旧像素”不等于绕过实际窗口事实。

## 验证和规模

辅助执行 **485 次实际 MSTest 方法 / DataRow 调用，0 失败**；其中 **120 项**标记为源码边界检查。本轮新增 `DockPhysicalRecoveryTests` 的 **20 个行为用例**和一个源码边界检查，并更新两个既有用例。

行为覆盖 96/120/144/192 DPI 下像素不变、奇数尺寸与接缝、宽高限制、负坐标和离屏标题栏、最大交叠与最近表面、缺失 topology、错误目标/无效 DPI、快照独立性、不改写 preferred、新会话根置顶，以及新旧请求之间的同组占用和取消。

net8 测试项目及完整产品 C#/原生自测源码的 net48 官方引用程序集编译均零警告、零错误。构建和辅助执行的 **237 个源码/项目文件哈希**已核对。标准 VSTest 17.11.1 仍在执行前因内部 `TestEngine.ShouldRunInProcess` 空引用异常退出，不能宣称标准测试宿主通过。初次辅助回归发现一个新增测试试图构造领域模型禁止的空 topology；已改为真实可发生的缺失 topology 输入，再完整执行得到上述结果。

两个 PetSticky partial 从 **4142 行降至 4034 行**，减少 108 行。计入 Core 兼容转换、请求输入和快照变化，生产 C# 总计净减 **14 行**。这里主要收益是删除一个独立执行与失败状态机，而非大幅减少算法代码；没有 Windows 延迟或内存基准测量。

证据位于 [legacy-dock-recovery-evidence](legacy-dock-recovery-evidence/README.md)。

## 仍然开放

本轮关闭的是“旧格式恢复拥有另一套执行状态机”这个缺口。物理兼容输入还需存在于恢复边界；不能据此把 Geometry Authority 或 PC-6 整体标为完成。

- header、resize、restore 仍未统一到整个组的操作序列。已有 header final 与新的展开/重开之间的顺序仍是下一处重点。
- 原生成功后 Pet 拒绝结果，仍只补偿原本隐藏成员的可见性，不承诺恢复所有既有窗口的位置。
- 已有会话的置顶状态同步、拓扑整组回滚细节、其他 UI 的同步 Save、PetForm 的其余生命周期编排仍需继续审查。
- 新旧格式现在都接受整批失败语义：其中一个成员无法满足实际目标屏/DPI 校验时，不再忽略错误继续部分展开。长组跨越上下排列屏幕、WPF 最小尺寸与高 DPI、IME、焦点、Z-order、热插拔、退出、打包及原生自测需要 Windows 实机验收；本环境没有这些执行结果。
