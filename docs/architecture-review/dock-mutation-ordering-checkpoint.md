# Dock 操作顺序检查点

2026-09-14，基于 `04887f7` 的进行中修改。此检查点用于保全源码，尚不能宣称本轮回归或 Windows 验收通过。

当前实现将横向 resize 专用的后续操作列表替换为 header、横向与接缝 final 共用的 `DockMutationQueue`。范围由手势捕获的成员/组 ID 决定，包含隐藏槽位、合并两侧及拆分余组；队列由具体手势持有，拓扑重新 final 时保留，只有匹配 epoch 的完成或显式取消才能释放。

展开、关闭、删除、平铺、退出与重载接入公共检查。恢复准备前同样检查，覆盖 topology restart 直达恢复的入口；目标组已有 restore 在 header final 取得范围时取消；独立 topology reproject 避开该范围。后续动作在旧 owner/mailbox 退出后执行，并重新解析需要的当前模型。

已补行为测试和 Windows 源码边界检查，迁移了旧 resize 专用断言。仍需完成：测试项目构建、完整产品引用程序集编译、最终辅助回归、源码哈希与证据固化、PR 描述更新。

本轮限于用户可见性/成员操作与手势 final 之间的顺序。新的原生 mouse-down 在旧 final 未完成时如何调度，以及 header/resize/restore 的完整统一执行器，尚未解决；不把增加共享队列视为 PC-6 整体结案。
