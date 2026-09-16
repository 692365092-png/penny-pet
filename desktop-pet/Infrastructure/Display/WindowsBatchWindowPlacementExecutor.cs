using System;
using System.Collections.Generic;

namespace PennyPet
{
    internal enum WindowsBatchPlacementStatus
    {
        Applied,
        BeginFailed,
        DeferFailed,
        EndFailed
    }

    // Thin typed Windows primitive: applies one immutable set of physical
    // window rects in a single deferred native batch so a Dock group moves
    // atomically on screen. No Dock policy, repository or event logic lives
    // here; it either applies every rect together or reports failure.
    internal static class WindowsBatchWindowPlacementExecutor
    {
        internal static WindowsBatchPlacementStatus Apply(
            IList<IntPtr> handles,
            IList<PhysicalRect> rects)
        {
            if (handles == null || rects == null)
                return WindowsBatchPlacementStatus.BeginFailed;
            int count = Math.Min(handles.Count, rects.Count);
            if (count == 0) return WindowsBatchPlacementStatus.BeginFailed;
            IntPtr info = NativeDisplayConfig.BeginDeferWindowPos(count);
            if (info == IntPtr.Zero)
                return WindowsBatchPlacementStatus.BeginFailed;
            try
            {
                for (int index = 0; index < count; index++)
                {
                    PhysicalRect rect = rects[index];
                    info = NativeDisplayConfig.DeferWindowPos(info,
                        handles[index], IntPtr.Zero, rect.Left, rect.Top,
                        rect.Width, rect.Height,
                        NativeDisplayConfig.SWP_NOACTIVATE |
                        NativeDisplayConfig.SWP_NOZORDER);
                    if (info == IntPtr.Zero)
                        return WindowsBatchPlacementStatus.DeferFailed;
                }
                return NativeDisplayConfig.EndDeferWindowPos(info)
                    ? WindowsBatchPlacementStatus.Applied
                    : WindowsBatchPlacementStatus.EndFailed;
            }
            catch
            {
                // A failed DeferWindowPos already frees the structure; an
                // exception leaves nothing recoverable to release here.
                return WindowsBatchPlacementStatus.DeferFailed;
            }
        }
    }
}
