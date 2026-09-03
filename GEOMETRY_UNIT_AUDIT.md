# Penny Geometry Unit Audit

> 目的：在把几何迁移到统一 LogicalGeometry 之前，先明确当前每个几何字段的真实单位语义。本文只记录事实与风险，不修改 runtime。

## 统一目标单位

- 1 Penny logical unit = Windows 96-DPI 下的 1 logical pixel = WPF 1 DIP ≈ macOS 1 AppKit point。
- Core 只处理 logical geometry；Windows 边界只负责 physical ↔ logical 投影。
- DisplayScale = 显示器 DPI / 96。Core 不出现 DPI / GetDpiForWindow / WM_DPICHANGED。

## 逐项审计

### Pet / PetForm

| 项目 | 当前语义 | 迁移目标 |
| --- | --- | --- |
| `PetForm.Location` | WinForms physical pixel | 先经 `WindowsDisplayMetrics` 投影，Core/persistence 只存 logical |
| `PetForm.ClientSize` | `ScaledPetSize(UserScalePercent)` 的 physical pixel；**未乘 DisplayScale** | logical = Base(192×208) × UserScale；native = logical × DisplayScale |
| `Cursor.Position` / drag delta | physical pixel | 仅作为 native 输入；写回 persistence 前转 logical |
| `Screen.WorkingArea` | physical pixel | 转 LogicalRect 后才进入 Core 布局 |
| `PetSettings.X / Y` | 已迁移：新写入为 logical（`PhysicalToLogicalDips`），读取时按当前 DisplayScale 投影回 physical | legacy physical 值无法得知历史 DPI，按“保持可见 + work area 恢复”处理，不宣称精确恢复 |

### Sticky / Dock / SideTabs

| 项目 | 当前语义 | 风险 / 迁移目标 |
| --- | --- | --- |
| `StickyNoteData.X/Y/Width/Height` | 来自 hosted WPF 的 DIP（≈logical），持久化沿用 | 与 physical 混用时（如 SideTabs overlap）会错位；目标统一 logical |
| `StickyUiBounds` / `DockWindowFacts` / `DockLayoutTarget` / resize payload | int，WPF DIP（≈logical） | 目标改为 `LogicalRect Bounds`，去掉裸 int X/Y/W/H |
| `StickyDockGeometry` 的 `scale` 参数 | 当前调用方实际传 1F；内部仍有 280/900/220/700/520/margin/gap × scale 的遗留 | Checkpoint 5 删除 scale，常量全部 logical |
| `StickyNoteTabsForm.TabWidth=146 / TabHeight=34 / TabGap=2 / PetGap=-20` | 当前按 physical pixel 使用（AutoScaleMode.None） | 这些是 logical 常量；WinForms 边界按 DisplayScale 投影 |
| SideTab capacity 计算 | `workArea.Height / (TabHeight + TabGap)`，workArea 是 physical | 统一为 `LogicalWorkArea.Height / (LogicalTabHeight + LogicalGap)` |

### 渲染 / 呈现

| 项目 | 当前语义 | 迁移目标 |
| --- | --- | --- |
| `LayeredSpriteRenderer.UpdateLayeredWindow` | 只接收 physical bitmap / position / size | 保持；logical → physical 的转换发生在 renderer 之外 |
| Pet render cache | key 只含 UserScale，未含 DisplayScale | key = UserScale + DisplayScale；DPI 变化时 dispose 当前 owned frames 后重建 |
| Pet alpha hit test | 依赖 source/rendered bitmap 坐标 | 避免 double-scale；visible shape == hit region |
| Bubble / Loading / Keyboard Overlay / SideTabs | 跟随 Pet 缩放，未统一 DisplayScale | 统一 Pet 的 display lifecycle，避免二次缩放 |

### Persistence

| 项目 | 当前语义 | 迁移目标 |
| --- | --- | --- |
| `PetSettings.X/Y` | physical（按当前代码） | 新写入 logical |
| `StickyNoteData.X/Y/Width/Height` | WPF DIP（≈logical） | 明确声明 logical 契约 |
| legacy 数据 | 格式中没有存 display scale | 迁移时只做可验证恢复（可见 + work area），不宣称“精确还原” |

## 实施顺序（后续 checkpoint）

1. Core 逻辑几何类型（LogicalPoint / LogicalSize / LogicalRect / DisplayScale）。
2. `WindowsDisplayMetrics`：DPI → DisplayScale、physical ↔ logical、统一 AwayFromZero rounding。
3. Pet DPI：UserScale × DisplayScale、render target 按 monitor、hit test 一致、mixed DPI 不累积。
4. Bubble / Loading / Overlay / SideTabs 统一 display lifecycle。
5. Dock Core 去 scale 参数，全部 logical。
6. Sticky 跨线程几何协议统一 logical。
7. Windows Dock 边界统一处理 Screen / Cursor / HWND 转换。
8. Persistence logical 迁移 + legacy 兼容。
9. Mixed DPI regression（100/125/150/175/200/250% 与跨屏）。

## 永久规则

- logical geometry = truth；native/physical geometry = projection。
- Core 不 import System.Drawing / Windows API / DPI。
- 原生边界 rounding 统一 `Math.Round(value, MidpointRounding.AwayFromZero)`。
- 迁移几何语义时不顺手改 Dock 分组/拆分/插入/排序/resize 策略，也不改 Sticky ownership。
