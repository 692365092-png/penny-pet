# Dock 操作顺序检查点

2026-09-14，基于 `04887f7`，经 `0951287` 保全的修改已完成本轮辅助回归与编译。完整结论见 [审查](2026-09-14-dock-mutation-ordering-review.md) 和 [证据](dock-mutation-ordering-evidence/README.md)。Windows 验收仍未执行。

当前实现将横向 resize 专用的后续操作列表替换为 header、横向与接缝 final 共用的 `DockMutationQueue`。范围由手势捕获的成员/组 ID 决定，包含隐藏槽位、合并两侧及拆分余组；队列由具体手势持有，拓扑重新 final 时保留，只有匹配 epoch 的完成或显式取消才能释放。

展开、关闭、删除、平铺、退出与重载接入公共检查。恢复准备前同样检查，覆盖 topology restart 直达恢复的入口；目标组已有 restore 在 header final 取得范围时取消；独立 topology reproject 避开该范围。后续动作在旧 owner/mailbox 退出后执行，并重新解析需要的当前模型。

已补行为测试和 Windows 源码边界检查，迁移了旧 resize 专用断言。最终辅助执行 502 项通过、0 失败（124 项标记为源码边界检查）；测试项目和全部产品源码引用程序集编译零警告、零错误，240 个源码/项目文件哈希已核对。标准 VSTest 在执行前内部异常，不能算标准宿主通过。

本轮限于用户可见性/成员操作与手势 final 之间的顺序。新的原生 mouse-down 在旧 final 未完成时如何调度，以及 header/resize/restore 的完整统一执行器，尚未解决；不把增加共享队列视为 PC-6 整体结案。
