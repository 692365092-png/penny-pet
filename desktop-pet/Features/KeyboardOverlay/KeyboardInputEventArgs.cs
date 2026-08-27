using System;

namespace PennyPet
{
    internal sealed class KeyboardInputEventArgs : EventArgs
    {
        public KeyboardInputEventArgs(int virtualKeyCode, string displayText)
            : this(virtualKeyCode, displayText, 1)
        {
        }

        public KeyboardInputEventArgs(int virtualKeyCode, string displayText,
            int repeatCount)
            : this(virtualKeyCode, displayText, repeatCount, null)
        {
        }

        internal KeyboardInputEventArgs(int virtualKeyCode, string displayText,
            int repeatCount, KeyboardFocusSnapshot focusSnapshot)
        {
            VirtualKeyCode = virtualKeyCode;
            DisplayText = displayText ?? String.Empty;
            RepeatCount = Math.Max(1, repeatCount);
            FocusSnapshot = focusSnapshot;
        }

        public int VirtualKeyCode { get; private set; }
        public string DisplayText { get; private set; }
        public int RepeatCount { get; private set; }
        internal KeyboardFocusSnapshot FocusSnapshot { get; private set; }
    }
}
