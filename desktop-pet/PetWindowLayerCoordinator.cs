using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PennyPet
{
    // Windows-only z-order boundary for Pet-owned modal windows and
    // non-activating transient chrome. The stack is runtime state only.
    internal sealed class PetWindowLayerCoordinator
    {
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpNoOwnerZOrder = 0x0200;
        private readonly List<Form> _modalStack = new List<Form>();

        internal event EventHandler LayerChanged;

        internal bool HasActiveModal
        {
            get { return ModalZOrderFloor != null; }
        }

        internal Rectangle? ModalAvoidanceBounds
        {
            get
            {
                Rectangle? bounds = null;
                for (int index = 0; index < _modalStack.Count; index++)
                {
                    Form modal = _modalStack[index];
                    if (!IsVisibleModal(modal)) continue;
                    bounds = bounds.HasValue
                        ? Rectangle.Union(bounds.Value, modal.Bounds)
                        : modal.Bounds;
                }
                return bounds;
            }
        }

        internal DialogResult ShowModal(IWin32Window owner, Form dialog)
        {
            if (dialog == null) throw new ArgumentNullException("dialog");
            EventHandler changed = delegate { RaiseLayerChanged(); };
            _modalStack.Add(dialog);
            dialog.Shown += changed;
            dialog.LocationChanged += changed;
            dialog.SizeChanged += changed;
            dialog.VisibleChanged += changed;
            RaiseLayerChanged();
            try
            {
                return dialog.ShowDialog(owner);
            }
            finally
            {
                dialog.Shown -= changed;
                dialog.LocationChanged -= changed;
                dialog.SizeChanged -= changed;
                dialog.VisibleChanged -= changed;
                _modalStack.Remove(dialog);
                RaiseLayerChanged();
            }
        }

        internal void KeepTransientBelowModal(Form transient)
        {
            Form floor = ModalZOrderFloor;
            if (floor == null || transient == null || transient.IsDisposed ||
                !transient.Visible || !transient.IsHandleCreated ||
                !floor.IsHandleCreated) return;
            SetWindowPos(transient.Handle, floor.Handle, 0, 0, 0, 0,
                SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoOwnerZOrder);
        }

        private Form ModalZOrderFloor
        {
            get
            {
                for (int index = 0; index < _modalStack.Count; index++)
                {
                    Form modal = _modalStack[index];
                    if (IsVisibleModal(modal)) return modal;
                }
                return null;
            }
        }

        private static bool IsVisibleModal(Form modal)
        {
            return modal != null && !modal.IsDisposed && modal.Visible;
        }

        private void RaiseLayerChanged()
        {
            EventHandler handler = LayerChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window,
            IntPtr insertAfter, int x, int y, int width, int height,
            uint flags);
    }
}
