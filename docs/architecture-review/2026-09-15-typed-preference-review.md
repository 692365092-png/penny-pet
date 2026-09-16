# 持久位置偏好改为完整的不可变值

接续 `1d43826`，继续处理 PC-3 的运行时模型。`StickyNoteData` 中五个分别可写的
Preferred 字段已删除，改为一个 `WindowPlacementPreference PreferredPlacement`。
没有保留旧属性转发层。null 表示尚未建立偏好；非 null 同时包含目标屏幕和正尺寸逻辑
矩形，构造后不可修改。

旧实现允许“只有屏幕 ID”“只有宽高”“屏幕已改但矩形尚未改”等半成品，每个消费者
都要重新判断 key、width、height。现在使用项目已有的位置值类型，构造时确立完整性，
删除重复 `IsValid` 查询。恢复、缺屏临时安置、偏好屏返回、组恢复只检查偏好是否存在。
resize 仍按原规则保留未受本次手势影响的维度，在另一屏幕上使用实际窗口事实重新
解释坐标。

Workspace 中将偏好拆成多个输出参数的 `TryBuildPreference` 和接收五个标量的
`CommitHostedStickyPreferred` 已删除。已接受的用户操作直接把完整值交给 Core 的
`TryCommitPreferred`，保留原有 reason 限制。v10 迁移与首次实际显示后的采用仍只填补
空偏好，不覆盖既有意图。持久化克隆直接共享不可变值，后续移动替换 live 引用不会
改变已经捕获的保存内容。

文件格式仍是 v11 的 37 个字段。Codec 在读取时一次构造偏好：缺失/超长 key、无效或
非正尺寸变成 null；正尺寸保留原来的上限；无法解析的 X/Y 仍退回 0。原来
`RepairForDisplay` 中反复清空、修补五个字段的整段逻辑已删除。坏文件字段的测试从
原始行输入注入，不再构造一个运行时不应存在的半成品。写出空偏好仍是空 key 加四个
0，v1–v11 历史文件夹具保持通过。

同时收紧实际事实转偏好的入口：没有有效 DPI 或正尺寸 HWND 矩形时返回失败，不再
把无效窗口尺寸通过 `Math.Max(1, ...)` 变成看似有效的持久位置。

验证共 576 次实际 MSTest 方法 / DataRow 调用通过，0 失败。本节点净增加 19 次调用：
原有 reason 检查扩展为十种原因下的实际提交结果，另有不可变保存副本、坏数字、key
长度边界和不完整 HWND 事实用例。已有缺屏、组恢复、隐藏成员高度、混合 DPI resize、
导入/导出及 writer 故障用例通过。Core、测试、Windows Core 与 SelfTests 编译通过，
警告视为错误；Windows GUI 未在 Linux 上运行。

这个节点解决了 Preferred 的类型完整性和重复拆装。PC-3 剩余的是物理与 v10 兼容
字段仍在运行时模型，旧文件待迁移位置仍需与 actual facts 隔离。不能把五个字段换成
一个值就算作 Geometry Authority 全部结束；Dock 的 restore/topology 编排和退出时
同步等待也仍有后续工作。
