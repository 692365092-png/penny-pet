using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using W = System.Windows;
using WC = System.Windows.Controls;

namespace PennyPet
{
    internal sealed partial class StickyNoteForm
    {
        private void HeaderMouseLeftButtonDown(object sender,
            MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (FindVisualParent<WC.Button>(e.OriginalSource as W.DependencyObject)
                != null) return;
            if (e.ClickCount == 2)
            {
                RenameNote();
                e.Handled = true;
                return;
            }
            base.WindowState = W.WindowState.Normal;
            _headerDragStartBounds = Bounds;
            W.Point pointer = e.GetPosition(this);
            _headerDragPointerOffset = new System.Drawing.Point(
                (int)Math.Round(pointer.X), (int)Math.Round(pointer.Y));
            _headerDragInProgress = true;
            Raise(HeaderDragStarted);
            try
            {
                DragMove();
                e.Handled = true;
            }
            catch (InvalidOperationException) { }
            finally
            {
                RecoverFromSystemGeometryChange();
                _headerDragInProgress = false;
                Raise(HeaderDragCompleted);
                PersistNow();
            }
        }

        private void RequestHideNote()
        {
            EventHandler handler = CloseRequested;
            if (handler != null) handler(this, EventArgs.Empty);
            else HideNote();
        }

        private void RecoverFromSystemGeometryChange()
        {
            if (_recoveringSystemGeometry || _headerDragStartBounds.IsEmpty)
                return;
            Rectangle current = Bounds;
            bool systemChangedGeometry =
                base.WindowState != W.WindowState.Normal ||
                current.Width != _headerDragStartBounds.Width ||
                current.Height != _headerDragStartBounds.Height;
            if (!systemChangedGeometry) return;
            System.Drawing.Point cursor;
            if (!GetCursorPos(out cursor))
                cursor = new System.Drawing.Point(current.Left +
                    _headerDragPointerOffset.X, current.Top +
                    _headerDragPointerOffset.Y);
            Rectangle recovered = CalculateRecoveredHeaderDragBounds(
                _headerDragStartBounds, current, cursor,
                _headerDragPointerOffset, true);
            _recoveringSystemGeometry = true;
            try
            {
                base.WindowState = W.WindowState.Normal;
                base.Left = recovered.Left;
                base.Top = recovered.Top;
                base.Width = recovered.Width;
                base.Height = recovered.Height;
            }
            finally { _recoveringSystemGeometry = false; }
        }

        private void RecoverUnexpectedMaximize()
        {
            if (_disposed || _recoveringSystemGeometry ||
                base.WindowState != W.WindowState.Maximized) return;
            if (_headerDragInProgress)
            {
                RecoverFromSystemGeometryChange();
                return;
            }
            _recoveringSystemGeometry = true;
            try
            {
                base.WindowState = W.WindowState.Normal;
                base.Left = Data.X;
                base.Top = Data.Y;
                base.Width = Math.Max(MinWidth, Math.Min(MaxWidth,
                    Data.Width));
                base.Height = Math.Max(MinHeight, Math.Min(MaxHeight,
                    Data.Height));
            }
            finally { _recoveringSystemGeometry = false; }
        }

        internal static Rectangle CalculateRecoveredHeaderDragBounds(
            Rectangle start, Rectangle current, System.Drawing.Point cursor,
            System.Drawing.Point pointerOffset, bool systemChangedGeometry)
        {
            if (!systemChangedGeometry) return current;
            return new Rectangle(cursor.X - pointerOffset.X,
                cursor.Y - pointerOffset.Y, start.Width, start.Height);
        }

        private static T FindVisualParent<T>(W.DependencyObject value)
            where T : W.DependencyObject
        {
            W.DependencyObject current = value;
            while (current != null)
            {
                T match = current as T;
                if (match != null) return match;
                current = InputTreeParent(current);
            }
            return null;
        }

        private static W.DependencyObject InputTreeParent(
            W.DependencyObject value)
        {
            if (value == null) return null;
            W.ContentElement content = value as W.ContentElement;
            if (content != null)
            {
                W.DependencyObject parent = W.ContentOperations.GetParent(
                    content);
                if (parent != null) return parent;
                W.FrameworkContentElement frameworkContent =
                    content as W.FrameworkContentElement;
                return frameworkContent == null ? null :
                    frameworkContent.Parent;
            }
            if (value is System.Windows.Media.Visual ||
                value is System.Windows.Media.Media3D.Visual3D)
                return VisualTreeHelper.GetParent(value);
            return W.LogicalTreeHelper.GetParent(value);
        }

        private static bool HasListItemAncestor(W.DependencyObject value)
        {
            W.DependencyObject current = value;
            while (current != null)
            {
                W.FrameworkElement element = current as W.FrameworkElement;
                if (element != null && (element.Tag is StickyTodoItem ||
                    element.Tag is StickyScheduleItem)) return true;
                current = InputTreeParent(current);
            }
            return false;
        }

        private void NoteSurfacePreviewMouseLeftButtonDown(object sender,
            MouseButtonEventArgs e)
        {
            // Cancel a still-pending first-show focus request before it can
            // steal focus from a ComboBox and immediately close its popup.
            _userInteractionGeneration++;
            if (!Data.IsTodoList && !Data.IsSchedule) return;
            W.DependencyObject source = e.OriginalSource as W.DependencyObject;
            if (HasListItemAncestor(source)) return;
            // Selection-dependent commands must see the selected item until
            // their click handler has run.  Empty chrome/body clicks clear it.
            if (FindVisualParent<WC.Button>(source) != null) return;
            if (FindVisualParent<WC.ComboBox>(source) != null) return;
            if (FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(
                source) != null) return;
            ClearListSelections();
        }

        private void WindowDeactivated(object sender, EventArgs e)
        {
            // Deactivation also occurs for WPF popups, menus and modal child
            // windows.  Only an actual click into another top-level window is
            // an "outside click"; focus transitions alone must not erase the
            // row highlight.
            bool mouseDown = (GetAsyncKeyState(0x01) & 0x8000) != 0 ||
                (GetAsyncKeyState(0x02) & 0x8000) != 0;
            if (!mouseDown) return;
            System.Drawing.Point cursor;
            if (!GetCursorPos(out cursor)) return;
            IntPtr clicked = WindowFromPoint(cursor);
            IntPtr self = new WindowInteropHelper(this).Handle;
            if (clicked != IntPtr.Zero && self != IntPtr.Zero &&
                GetAncestor(clicked, GaRootOwner) ==
                GetAncestor(self, GaRootOwner)) return;
            ClearListSelections();
        }

        private void ClearListSelections()
        {
            bool todoChanged = _selectedTodo != null;
            bool scheduleChanged = _selectedSchedule != null;
            _selectedTodo = null;
            _selectedSchedule = null;
            if (todoChanged) RefreshTodoRowColors();
            if (scheduleChanged) RefreshScheduleRowColors();
        }

        private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam,
            IntPtr lParam, ref bool handled)
        {
            if (_disposed) return IntPtr.Zero;
            if (message == WmSysCommand &&
                (wParam.ToInt64() & 0xFFF0L) == ScMaximize)
            {
                handled = true;
                return IntPtr.Zero;
            }
            if (message == WmEnterSizeMove)
            {
                _windowResizeActive = true;
                _resizeStartLeft = Left;
                _resizeStartWidth = Width;
                _dockDividerResizeActive = _dockSplitBottom &&
                    _lastResizeHitTest == HtBottom;
                return IntPtr.Zero;
            }
            if (message == WmExitSizeMove)
            {
                _windowResizeActive = false;
                _dockDividerResizeActive = false;
                _lastResizeHitTest = 0;
                return IntPtr.Zero;
            }
            if (message == WmSizing && _dockSplitBottom &&
                (_dockDividerResizeActive ||
                    _lastResizeHitTest == HtBottom) &&
                lParam != IntPtr.Zero)
            {
                NativeRect sizing = (NativeRect)Marshal.PtrToStructure(
                    lParam, typeof(NativeRect));
                double scale = DeviceScaleY();
                int minimumPixels = Math.Max(1, (int)Math.Round(
                    _dockDividerMinimumHeight * scale));
                int maximumPixels = Math.Max(minimumPixels,
                    (int)Math.Round(_dockDividerMaximumHeight * scale));
                int requested = sizing.Bottom - sizing.Top;
                requested = Math.Max(minimumPixels, Math.Min(
                    maximumPixels, requested));
                sizing.Bottom = sizing.Top + requested;
                Marshal.StructureToPtr(sizing, lParam, false);
                handled = true;
                return new IntPtr(1);
            }
            if (message == WmSizing && _dockGrouped &&
                IsHorizontalResizeHitTest(_lastResizeHitTest) &&
                lParam != IntPtr.Zero)
            {
                // WM_SIZING carries the authoritative proposed rectangle.
                // Synchronize the group from it before WPF emits its separate
                // LocationChanged/SizeChanged notifications; reconstructing a
                // left-edge resize from those later events made the right edge
                // jump and created a race between grouped windows.
                NativeRect sizing = (NativeRect)Marshal.PtrToStructure(
                    lParam, typeof(NativeRect));
                double scale = DeviceScaleX();
                double proposedWidth = Math.Max(MinWidth, Math.Min(MaxWidth,
                    (sizing.Right - sizing.Left) / scale));
                bool fromLeft = _lastResizeHitTest == HtLeft ||
                    _lastResizeHitTest == HtTopLeft ||
                    _lastResizeHitTest == HtBottomLeft;
                double proposedLeft = fromLeft
                    ? _resizeStartLeft + _resizeStartWidth - proposedWidth
                    : _resizeStartLeft;
                EventHandler<DockHorizontalResizeEventArgs> resizeHandler =
                    DockHorizontalResizing;
                if (resizeHandler != null)
                    resizeHandler(this, new DockHorizontalResizeEventArgs(
                        (int)Math.Round(proposedLeft),
                        (int)Math.Round(proposedWidth)));
                return IntPtr.Zero;
            }
            if (message != WmNcHitTest) return IntPtr.Zero;
            int screenX = unchecked((short)(long)lParam);
            int screenY = unchecked((short)((long)lParam >> 16));
            W.Point client = PointFromScreen(new W.Point(screenX, screenY));
            const double grip = 7.0;
            bool left = client.X >= 0 && client.X <= grip;
            bool right = client.X <= ActualWidth && client.X >= ActualWidth - grip;
            bool top = client.Y >= 0 && client.Y <= grip;
            bool bottom = client.Y <= ActualHeight && client.Y >= ActualHeight - grip;
            if (_dockGrouped)
            {
                top = top && _dockResizeTop;
                bottom = bottom && _dockResizeBottom;
                // An internal seam belongs to the note above it.  Keep its
                // seven-pixel hit target strictly vertical so dragging near a
                // corner cannot accidentally resize the whole dock width.
                if (bottom && _dockSplitBottom)
                {
                    left = false;
                    right = false;
                }
            }
            int result = 0;
            if (left && top) result = HtTopLeft;
            else if (right && top) result = HtTopRight;
            else if (left && bottom) result = HtBottomLeft;
            else if (right && bottom) result = HtBottomRight;
            else if (left) result = HtLeft;
            else if (right) result = HtRight;
            else if (top) result = HtTop;
            else if (bottom) result = HtBottom;
            _lastResizeHitTest = result;
            if (_dockSplitBottom && result == HtBottom)
                _dockDividerResizeActive = true;
            else if (!_windowResizeActive)
                _dockDividerResizeActive = false;
            if (result == 0) return IntPtr.Zero;
            handled = true;
            return new IntPtr(result);
        }

        private double DeviceScaleY()
        {
            W.PresentationSource source = W.PresentationSource.FromVisual(this);
            if (source == null || source.CompositionTarget == null) return 1.0;
            double scale = source.CompositionTarget.TransformToDevice.M22;
            return scale > 0.1 && scale < 8.0 ? scale : 1.0;
        }

        private double DeviceScaleX()
        {
            W.PresentationSource source = W.PresentationSource.FromVisual(this);
            if (source == null || source.CompositionTarget == null) return 1.0;
            double scale = source.CompositionTarget.TransformToDevice.M11;
            return scale > 0.1 && scale < 8.0 ? scale : 1.0;
        }

        private static bool IsHorizontalResizeHitTest(int hitTest)
        {
            return hitTest == HtLeft || hitTest == HtRight ||
                hitTest == HtTopLeft || hitTest == HtTopRight ||
                hitTest == HtBottomLeft || hitTest == HtBottomRight;
        }

        private static void DisableNativeMaximizeAndSnap(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            IntPtr style = GetWindowStyle(hwnd);
            long filtered = RemoveMaximizeStyle(style.ToInt64());
            if (filtered == style.ToInt64()) return;
            SetWindowStyle(hwnd, new IntPtr(filtered));
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate |
                SwpFrameChanged);
        }

        internal static long RemoveMaximizeStyle(long style)
        {
            return style & ~WsMaximizeBox;
        }

        private static IntPtr GetWindowStyle(IntPtr hwnd)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, GwlStyle) :
                new IntPtr(GetWindowLong32(hwnd, GwlStyle));
        }

        private static void SetWindowStyle(IntPtr hwnd, IntPtr style)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, GwlStyle, style);
            else SetWindowLong32(hwnd, GwlStyle, style.ToInt32());
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hwnd, int index,
            int newValue);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index,
            IntPtr newValue);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hwnd,
            IntPtr insertAfter, int x, int y, int width, int height,
            uint flags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hwnd,
            out NativeRect bounds);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out System.Drawing.Point point);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(System.Drawing.Point point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    }
}
