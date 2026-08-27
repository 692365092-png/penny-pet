using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PennyPet
{
    internal static class KeyboardInputFormatter
    {
        public static string Format(int virtualKeyCode)
        {
            bool control = IsDown(Keys.LControlKey) || IsDown(Keys.RControlKey);
            bool shift = IsDown(Keys.LShiftKey) || IsDown(Keys.RShiftKey);
            bool alt = IsDown(Keys.LMenu) || IsDown(Keys.RMenu);
            bool windows = IsDown(Keys.LWin) || IsDown(Keys.RWin);
            Keys key = (Keys)virtualKeyCode;
            // Low-level hook callbacks can run before GetAsyncKeyState reflects
            // the modifier that generated the current event.
            if (IsControlKey(key)) control = true;
            if (IsShiftKey(key)) shift = true;
            if (IsAltKey(key)) alt = true;
            if (key == Keys.LWin || key == Keys.RWin) windows = true;
            return ComposeKeyName(virtualKeyCode, control, shift, alt, windows);
        }

        internal static string ComposeKeyName(int virtualKeyCode, bool control,
            bool shift, bool alt, bool windows)
        {
            Keys key = (Keys)virtualKeyCode;
            string keyName = BaseKeyName(key);
            if (String.IsNullOrEmpty(keyName)) return String.Empty;
            List<string> parts = new List<string>();
            if (control) parts.Add("CTRL");
            if (shift) parts.Add("SHIFT");
            if (alt) parts.Add("ALT");
            if (windows) parts.Add("WIN");
            if (!IsControlKey(key) && !IsShiftKey(key) && !IsAltKey(key) &&
                key != Keys.LWin && key != Keys.RWin)
                parts.Add(keyName);
            return String.Join("+", parts.ToArray());
        }

        private static string BaseKeyName(Keys key)
        {
            int code = (int)key;
            if (code == 21 || code == 23 || code == 24 || code == 25 ||
                code == 28 || code == 29 || code == 31 || code == 229 ||
                code == 231) return String.Empty;
            if (code >= (int)Keys.A && code <= (int)Keys.Z)
                return ((char)code).ToString();
            if (code >= (int)Keys.D0 && code <= (int)Keys.D9)
                return ((char)code).ToString();
            if (code >= (int)Keys.NumPad0 && code <= (int)Keys.NumPad9)
                return "NUM" + (code - (int)Keys.NumPad0);
            if (code >= (int)Keys.F1 && code <= (int)Keys.F24)
                return "F" + (code - (int)Keys.F1 + 1);

            switch (key)
            {
                case Keys.LControlKey:
                case Keys.RControlKey:
                case Keys.ControlKey: return "CTRL";
                case Keys.LShiftKey:
                case Keys.RShiftKey:
                case Keys.ShiftKey: return "SHIFT";
                case Keys.LMenu:
                case Keys.RMenu:
                case Keys.Menu: return "ALT";
                case Keys.LWin:
                case Keys.RWin: return "WIN";
                case Keys.Space: return "SPACE";
                case Keys.Enter: return "ENTER";
                case Keys.Escape: return "ESC";
                case Keys.Tab: return "TAB";
                case Keys.Back: return "BACKSPACE";
                case Keys.Delete: return "DELETE";
                case Keys.Insert: return "INSERT";
                case Keys.Home: return "HOME";
                case Keys.End: return "END";
                case Keys.PageUp: return "PAGE UP";
                case Keys.PageDown: return "PAGE DOWN";
                case Keys.Left: return "←";
                case Keys.Right: return "→";
                case Keys.Up: return "↑";
                case Keys.Down: return "↓";
                case Keys.CapsLock: return "CAPS LOCK";
                case Keys.NumLock: return "NUM LOCK";
                case Keys.Scroll: return "SCROLL LOCK";
                case Keys.PrintScreen: return "PRINT SCREEN";
                case Keys.Pause: return "PAUSE";
                case Keys.OemSemicolon: return ";";
                case Keys.Oemplus: return "=";
                case Keys.Oemcomma: return ",";
                case Keys.OemMinus: return "-";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.Oemtilde: return "`";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemPipe: return "\\";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemQuotes: return "'";
                case Keys.Multiply: return "NUM *";
                case Keys.Add: return "NUM +";
                case Keys.Subtract: return "NUM -";
                case Keys.Decimal: return "NUM .";
                case Keys.Divide: return "NUM /";
                default: return key.ToString().ToUpperInvariant();
            }
        }

        private static bool IsDown(Keys key)
        {
            return (GetAsyncKeyState((int)key) & 0x8000) != 0;
        }

        internal static bool IsVirtualKeyDown(int virtualKeyCode)
        {
            return (GetAsyncKeyState(virtualKeyCode) & 0x8000) != 0;
        }

        private static bool IsControlKey(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.LControlKey ||
                key == Keys.RControlKey;
        }

        private static bool IsShiftKey(Keys key)
        {
            return key == Keys.ShiftKey || key == Keys.LShiftKey ||
                key == Keys.RShiftKey;
        }

        private static bool IsAltKey(Keys key)
        {
            return key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKeyCode);
    }
}
