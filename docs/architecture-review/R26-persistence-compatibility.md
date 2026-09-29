# R26 持久化兼容矩阵与退役决策

盘点基线：`36f1d8d`，2026-09-29。R26 是条件实施项。本轮不升级磁盘版本，不删除仍有恢复消费者的字段；先移除内容快照中的提醒时间镜像，验证现有迁移、导出及备份边界。

## 文件版本

| Sticky 行版本 | 最少字段数 | 新增内容 | 当前读取、迁移与写出 |
| --- | ---: | --- | --- |
| v1 | 13 | ID、可见性、物理矩形、时间戳、提醒时间、正文 | 读取；下一次保存写 v11 |
| v2 | 16 | 便签类型、标题、Todo；正文移到字段 15 | 读取；下一次保存写 v11 |
| v3 | 17 | SideTab 顺序 | 同上 |
| v4 | 18 | RTF | 同上 |
| v5 | 20 | 字体、字号 | 同上 |
| v6 | 22 | 背景透明度、文字颜色 | 同上 |
| v7 | 23 | 父链关系 | 整文件读取时转换成有序组；运行时不保留父链 |
| v8 | 25 | DockGroupId / DockGroupOrder | 显式组优先于旧父链；整文件入口统一修复 |
| v9 | 27 | 日程内容 | 读取；下一次保存写 v11 |
| v10 | 32 | GDI 显示器名和显示器局部逻辑矩形 | 仅解析为 LegacyPlacement；拓扑可解析时迁移到 preferred |
| v11 | 37 | 稳定显示器身份和 preferred 逻辑矩形 | 当前唯一写出版本 |
| 大于 v11 | 不适用 | 未知 | 加载/导入拒绝；不得以旧备份或默认模型覆盖未来数据 |

字段下标从 0 开始。`StickyNoteCodec` 保留已支持版本的尾部多余字段容忍，但不保留这些未知字段再写出。不得把该行为解释成未来版本往返支持。

`settings.ini` 是无版本的键值格式。旧单提醒 `ReminderUtcTicks` / `ReminderTextBase64` 只读迁移：仅在没有有效编号提醒时启用；写出使用 `Reminder0..4`。未知键会被忽略且不保留，当前没有 Settings 的 future-schema 拒绝协议；未来改格式必须单独设计，不能套用 Sticky 的保证。旧 `StartWithWindows` 键仍表达当前的登录启动设置。

`.pennysticky` v1 是 Sticky dataset 容器，不是 Settings 的事务快照；不携带完整 ReminderSchedule。新增/冲突副本的提醒投影清零，已有 NoteId 保留当前提醒归属。

## 字段所有权与保留条件

| 字段 | 当前消费者与写入者 | 本轮决定 | 退役前提 |
| --- | --- | --- | --- |
| Sticky X/Y/Width/Height | 恢复与缺失首选屏幕时的物理回退；actual facts 更新最后已知矩形 | 保留；不能用于 live Dock 规划 | 有替代恢复输入，并验证未建 HWND、旧数据、断屏及混合 DPI |
| Settings HasLocation/X/Y | Pet 首次恢复与 preferred 不可用时回退；PetDisplayRuntime 捕获 | 保留，与 preferred 含义不同 | 验证冷启动、断屏、旧 Settings 和回退行为 |
| Sticky v10 的 GDI 名/逻辑矩形 | 旧文件输入；可解析时迁移并清空，无法解析则保留 | 保留集中读写层；不制造新 v10 镜像 | 真实旧文件与显示器不在场的迁移证据 |
| Sticky/Settings preferred | 用户的稳定显示器位置偏好 | 保留为持久偏好 | 非退役候选；实际窗口矩形与偏好不同 |
| v7 parent 字段 | 仅整文件读取时解释；写出从当前有序组生成可见父链 | 保留文件适配；不回到 live 模型 | 明确旧格式互通终止范围及文件版本策略 |
| Sticky ReminderUtcTicks | Pet ReminderRuntime 从 schedule 投影；Manager 显示/排序；旧文件字段 | 保留磁盘列和 Pet 投影，移除 StickyNoteUiSnapshot 中的往返镜像 | Manager 改用 schedule 投影、旧格式互通与导出策略明确后再决定停写 |
| Settings Reminder0..4 | ReminderSchedule 的完整提醒记录，含 SourceNoteId | 保留为提醒持久化来源 | 不得仅凭 Sticky 时间戳恢复完整提醒 |

Sticky 窗口的提醒条已有独立 `UpdateReminders` / `UpdateAllReminders` 载荷。正文快照没有修改 reminder schedule 的权力；编辑、拖动最终快照或退出最终捕获都不应覆盖 Pet 侧的新提醒时间。

## 备份、重复迁移与回退约定

- 普通保存通过 `AtomicTextFile` 替换；已有主文件的上一份原始字节成为 `.bak`。这是滚动备份，下一次保存会覆盖，不是永久迁移档案。
- 导入/完整恢复先把当前模型写入独立 `before-import` / `before-restore` 文件，再替换 Notes，成功后才发布模型。这份备份使用当前格式，不是原始旧文件的字节副本。
- 当前版本重读、保存应稳定；导出后再次合并相同数据不应产生重复副本。旧父链和显式组在整文件边界处理，单行 codec 测试不能代替该验证。
- 允许在保存升级后取回滚动 `.bak` 的原始旧文件。没有 v11 到 v1–v10 的降级写出器，也没有证据保证旧 EXE 能读取 v11；保留旧字段不等于支持旧 EXE 往返。
- 损坏文件保全、`.bak` 恢复、future Sticky 拒绝仍由现有加载/写入测试覆盖。本轮不改变这些策略。

## 证据与缺口

`Tests/Fixtures/sticky-v1.txt` 到 `sticky-v11.txt` 及 `sticky-vFuture.txt` 在 `5512733`（R11.1）一次加入。内容使用 `legacy-vN`、`Legacy body` 等测试标识，没有真实旧客户端生成记录或来源说明，应视为合成格式样本，不能称为真实用户旧文件。当前 Git 树及可见历史没有 `.dat`、`.ini`、`.pennysticky` 旧文件样本。

现有 `StickyCodecBoundaryTests` 验证截断、尾部字段和版本 token；`StickyFactsReceiverTests` 验证 v10 单次迁移及无法解析显示器时的保存；`PetSettingsPlacementCodecTests` 验证物理回退和 preferred 独立往返；`StickyRepositoryWriterTests` 验证导入前备份、失败保留及 future-schema 边界。新增验证应针对完整文件升级的备份和重复保存/导入，以及新提醒不能被旧内容快照覆盖。

因此磁盘列删除和历史解析器删除暂不实施。后续需要有来源的旧客户端样本、原始文件保全、对应旧 EXE 的读写能力，以及明确的降级范围，才能推进格式层退役。这个条件不阻塞已确认多余的运行时镜像清理。
