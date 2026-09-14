# Sticky workspace and Dock ownership

This node follows the facts-receiver checkpoint `4500ea6` on top of `f5bd785`.

## Actual ownership changes

The two `PetForm` sticky partials are removed. `PetForm` constructs and starts one `StickyWorkspace` and disposes it on shutdown. The workspace owns the STA host, accepted hosted/placement state, fact receiver and side-tab forms. Reminder-window commands, note manager integration and content/visibility handling live with that owner. Pet HWND capture stays in `PetDisplayRuntime`; shell persistence dialogs, animation and application exit remain shell services.

`StickyDockController`, constructed by the workspace, owns header interaction state, resize sessions, restore operations, topology work, mailboxes, deferred mutations and visual indicators. They are no longer fields of `PetForm`. Hide-and-relayout belongs to this controller, so another component cannot toggle its layout suppression flag. Dock reads a small workspace port for Pet bounds/captures and feedback; it has no PetForm reference or direct native sticky-window ownership.

Workspace disposal marks its lifetime ended before retiring Dock operations. Retiring a header gesture advances its epoch and clears the mailbox; queued callbacks cannot commit the retired gesture. Deferred actions are dropped after workspace disposal. Native host shutdown and side-tab closure belong to the workspace.

This is a change of object ownership, not another `PetForm` partial-file split. It does not by itself unify every gesture kind into one state machine. The header, resize and restore operation types remain distinct because they have different captured inputs and native completion rules. Their orchestration now has one owner, making subsequent changes local and reviewable.

## Validation and remaining size

- 510 direct MSTest method/DataRow calls passed, 0 failed. Tests continue using real MSTest assertions; standard VSTest initialization remains broken in this environment.
- Separate compilation of the actual Windows core project source set (90 files) and self-test source set (5 files), respecting the Core/Windows/SelfTests assembly boundaries and friend access, passed with warnings treated as errors. This is C# compilation, not a packaged EXE build or native test execution.
- The PC2 native fixture now constructs real `StickyWorkspace`/`StickyDockController` instances without starting their UI. It no longer fabricates sticky state by assigning PetForm's private fields. It still fabricates an isolated shell to avoid loading real user settings/art/hooks.
- Source guards follow the real workspace/controller and explicit methods. Native effect/order guards remain; renamed source tokens are not counted as behavioral evidence.
- PetForm is 716 lines; StickyWorkspace is 1,742; StickyDockController is 2,213. Total code did not collapse simply by moving ownership. The new objects still warrant simplification, especially editor payloads and Dock policy/effect coupling. No claim of full PC-6/PC-7 closure or highest possible performance is made.
- Native mixed-DPI drag/resize, repeated gestures during finalization, IME, shutdown and hotplug must still be exercised on Windows.
