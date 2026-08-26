using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Forms;

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
        {
            VirtualKeyCode = virtualKeyCode;
            DisplayText = displayText ?? String.Empty;
            RepeatCount = Math.Max(1, repeatCount);
        }

        public int VirtualKeyCode { get; private set; }
        public string DisplayText { get; private set; }
        public int RepeatCount { get; private set; }
    }

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

    internal sealed class KeyDisplayAccumulator
    {
        private string _lastKey = String.Empty;
        private int _count;
        private DateTime _lastUtc = DateTime.MinValue;

        public string Register(string key, DateTime utcNow)
        {
            return Register(key, utcNow, 1);
        }

        public string Register(string key, DateTime utcNow, int occurrences)
        {
            string value = key ?? String.Empty;
            int increment = Math.Max(1, occurrences);
            if (String.Equals(value, _lastKey, StringComparison.Ordinal) &&
                utcNow - _lastUtc <= TimeSpan.FromMilliseconds(700))
                _count += increment;
            else
            {
                _lastKey = value;
                _count = increment;
            }
            _lastUtc = utcNow;
            return _count <= 1 ? value : value + "*" + _count;
        }

        public string RegisterAbsolute(string key, DateTime utcNow,
            int repeatCount)
        {
            string value = key ?? String.Empty;
            int absolute = Math.Max(1, repeatCount);
            if (String.Equals(value, _lastKey, StringComparison.Ordinal) &&
                utcNow - _lastUtc <= TimeSpan.FromMilliseconds(1400))
                _count = Math.Max(_count, absolute);
            else
            {
                _lastKey = value;
                _count = absolute;
            }
            _lastUtc = utcNow;
            return _count <= 1 ? value : value + "*" + _count;
        }

        public void Reset()
        {
            _lastKey = String.Empty;
            _count = 0;
            _lastUtc = DateTime.MinValue;
        }
    }

    internal static class SensitiveInputDetector
    {
        private const int GwlStyle = -16;
        private const int EsPassword = 0x0020;

        public static bool IsSensitiveFocus()
        {
            bool automationPassword = false;
            bool automationInspected = false;
            try
            {
                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused != null)
                {
                    object value = focused.GetCurrentPropertyValue(
                        AutomationElement.IsPasswordProperty, true);
                    automationPassword = value is bool && (bool)value;
                    if (!automationPassword)
                    {
                        string name = SafeAutomationName(focused);
                        ControlType type = SafeControlType(focused);
                        if (type == ControlType.Edit && ContainsSensitiveWord(name))
                            automationPassword = true;
                    }
                    automationInspected = true;
                }
            }
            catch
            {
                // Win32 checks below remain available if a UIA provider rejects access.
            }

            bool standardPassword = false;
            bool knownCredentialWindow = false;
            bool nativeInspected = false;
            try
            {
                IntPtr foreground = GetForegroundWindow();
                uint processId;
                uint threadId = GetWindowThreadProcessId(foreground, out processId);
                GuiThreadInfo info = new GuiThreadInfo();
                info.cbSize = Marshal.SizeOf(typeof(GuiThreadInfo));
                if (threadId != 0 && GetGUIThreadInfo(threadId, ref info) &&
                    info.hwndFocus != IntPtr.Zero)
                {
                    int style = GetWindowLong(info.hwndFocus, GwlStyle);
                    standardPassword = (style & EsPassword) != 0;
                    StringBuilder className = new StringBuilder(256);
                    GetClassName(info.hwndFocus, className, className.Capacity);
                    if (ContainsSensitiveWord(className.ToString()))
                        standardPassword = true;
                    nativeInspected = true;
                }
                if (processId != 0)
                {
                    using (Process process = Process.GetProcessById((int)processId))
                    {
                        string name = process.ProcessName ?? String.Empty;
                        knownCredentialWindow = name.Equals("CredentialUIBroker",
                            StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("LogonUI", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("consent", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch
            {
                // Failure to inspect one fallback must not break normal input.
            }
            return ShouldSuppress(automationPassword, standardPassword,
                knownCredentialWindow,
                automationInspected || nativeInspected);
        }

        internal static bool ShouldSuppress(bool automationPassword,
            bool standardPassword, bool knownCredentialWindow)
        {
            return ShouldSuppress(automationPassword, standardPassword,
                knownCredentialWindow, true);
        }

        internal static bool ShouldSuppress(bool automationPassword,
            bool standardPassword, bool knownCredentialWindow,
            bool inspectionAvailable)
        {
            // Fail closed: if neither UI Automation nor Win32 can inspect the
            // foreground input, do not publish an overlay for that key event.
            return !inspectionAvailable || automationPassword ||
                standardPassword || knownCredentialWindow;
        }

        private static string SafeAutomationName(AutomationElement element)
        {
            try { return element.Current.Name ?? String.Empty; }
            catch { return String.Empty; }
        }

        private static ControlType SafeControlType(AutomationElement element)
        {
            try { return element.Current.ControlType; }
            catch { return null; }
        }

        private static bool ContainsSensitiveWord(string value)
        {
            string text = (value ?? String.Empty).ToLowerInvariant();
            return text.Contains("password") || text.Contains("passwd") ||
                text.Contains("credential") || text.Contains("passcode") ||
                text.Contains("密码") || text.Contains("口令");
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuiThreadInfo
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public NativeRect rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window,
            out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetGUIThreadInfo(uint threadId,
            ref GuiThreadInfo info);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr window, StringBuilder text,
            int maximumCount);
    }

    internal sealed class KeyboardOverlayForm : Form
    {
        internal const string TextFontFamilyName = "Microsoft YaHei UI";
        internal const float BaseTextFontSizePoints = 15F;
        internal const FontStyle TextFontStyle = FontStyle.Bold;
        private readonly KeyDisplayAccumulator _accumulator = new KeyDisplayAccumulator();
        private readonly System.Windows.Forms.Timer _fadeTimer;
        private Font _font;
        private Bitmap _surface;
        private DateTime _lastInputUtc;
        private byte _opacity;
        private string _displayText = String.Empty;
        private int _textScalePercent = 100;
        private int _heldVirtualKeyCode;
        private bool _ownedResourcesDisposed;

        public KeyboardOverlayForm() : this(100)
        {
        }

        public KeyboardOverlayForm(int textScalePercent)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(220, 46);
            _fadeTimer = new System.Windows.Forms.Timer();
            _fadeTimer.Interval = 50;
            _fadeTimer.Tick += FadeTick;
            SetTextScale(textScalePercent);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00080000;
                value.ExStyle |= 0x08000000;
                value.ExStyle |= 0x00000080;
                return value;
            }
        }

        public void ShowKey(Form pet, string keyText)
        {
            ShowKey(pet, keyText, 1);
        }

        public void ShowKey(Form pet, string keyText, int occurrences)
        {
            ShowKey(pet, keyText, occurrences, 0);
        }

        public void ShowKey(Form pet, string keyText, int occurrences,
            int virtualKeyCode)
        {
            if (pet == null || pet.IsDisposed || String.IsNullOrEmpty(keyText)) return;
            DateTime now = DateTime.UtcNow;
            _displayText = _accumulator.Register(keyText, now, occurrences);
            _heldVirtualKeyCode = virtualKeyCode;
            _lastInputUtc = now;
            _opacity = 255;
            RenderSurface(ChooseTextColor(CalculateLocation(pet)));
            UpdatePosition(pet);
            if (!Visible) Show(pet);
            LayeredSpriteRenderer.Show(this, _surface, _opacity);
            _fadeTimer.Start();
        }

        public void ShowKeyRepeatCount(Form pet, string keyText, int repeatCount,
            int virtualKeyCode)
        {
            if (pet == null || pet.IsDisposed || String.IsNullOrEmpty(keyText)) return;
            DateTime now = DateTime.UtcNow;
            _displayText = _accumulator.RegisterAbsolute(keyText, now, repeatCount);
            _heldVirtualKeyCode = virtualKeyCode;
            _lastInputUtc = now;
            _opacity = 255;
            RenderSurface(ChooseTextColor(CalculateLocation(pet)));
            UpdatePosition(pet);
            if (!Visible) Show(pet);
            LayeredSpriteRenderer.Show(this, _surface, _opacity);
            _fadeTimer.Start();
        }

        public void UpdatePosition(Form pet)
        {
            if (pet == null || pet.IsDisposed) return;
            Location = CalculateLocation(pet);
            if (Visible && _surface != null)
                LayeredSpriteRenderer.Show(this, _surface, _opacity);
        }

        public void HideImmediately()
        {
            _fadeTimer.Stop();
            _accumulator.Reset();
            _displayText = String.Empty;
            _heldVirtualKeyCode = 0;
            if (Visible) Hide();
        }

        public void SetTextScale(int percent)
        {
            int normalized = NormalizeTextScalePercent(percent);
            if (_font != null && normalized == _textScalePercent) return;
            _textScalePercent = normalized;
            if (_font != null) _font.Dispose();
            _font = new Font(TextFontFamilyName,
                TextFontSizePoints(normalized), TextFontStyle,
                GraphicsUnit.Point);
            HideImmediately();
        }

        internal static int NormalizeTextScalePercent(int value)
        {
            if (value <= 80) return 60;
            if (value >= 125) return 150;
            return 100;
        }

        internal static float TextFontSizePoints(int textScalePercent)
        {
            return BaseTextFontSizePoints *
                NormalizeTextScalePercent(textScalePercent) / 100F;
        }

        internal static Color ChooseTextColorFromLuminance(double luminance)
        {
            return luminance >= 0.56 ? Color.Black : Color.White;
        }

        internal static Bitmap RenderTextPreview(string text, Color color,
            byte opacity)
        {
            return RenderTextPreview(text, color, opacity, 100);
        }

        internal static Bitmap RenderTextPreview(string text, Color color,
            byte opacity, int textScalePercent)
        {
            using (Font font = new Font(TextFontFamilyName,
                TextFontSizePoints(textScalePercent), TextFontStyle,
                GraphicsUnit.Point))
                return RenderTextBitmap(text, color, opacity, font);
        }

        private Point CalculateLocation(Form pet)
        {
            Rectangle work = Screen.FromRectangle(pet.Bounds).WorkingArea;
            int x = pet.Left + pet.Width / 2 - Width / 2;
            int y = pet.Bottom + 4;
            x = Math.Max(work.Left + 4, Math.Min(x, work.Right - Width - 4));
            if (y + Height > work.Bottom - 2)
                y = Math.Max(work.Top + 2, pet.Top - Height - 4);
            return new Point(x, y);
        }

        private Color ChooseTextColor(Point location)
        {
            try
            {
                int centerX = location.X + Width / 2;
                int centerY = location.Y + Height / 2;
                using (Bitmap sample = new Bitmap(12, 12, PixelFormat.Format24bppRgb))
                using (Graphics graphics = Graphics.FromImage(sample))
                {
                    graphics.CopyFromScreen(centerX - 6, centerY - 6, 0, 0,
                        sample.Size, CopyPixelOperation.SourceCopy);
                    double total = 0;
                    int count = 0;
                    for (int y = 0; y < sample.Height; y += 2)
                    {
                        for (int x = 0; x < sample.Width; x += 2)
                        {
                            Color pixel = sample.GetPixel(x, y);
                            total += (0.2126 * pixel.R + 0.7152 * pixel.G +
                                0.0722 * pixel.B) / 255.0;
                            count++;
                        }
                    }
                    return ChooseTextColorFromLuminance(total / Math.Max(1, count));
                }
            }
            catch
            {
                return ChooseTextColorFromLuminance(
                    SystemColors.Desktop.GetBrightness());
            }
        }

        private void RenderSurface(Color textColor)
        {
            Bitmap next = RenderTextBitmap(_displayText, textColor, 255, _font);
            ClientSize = next.Size;
            if (_surface != null) _surface.Dispose();
            _surface = next;
        }

        private static Bitmap RenderTextBitmap(string text, Color textColor,
            byte opacity, Font font)
        {
            string value = text ?? String.Empty;
            SizeF measured;
            using (Bitmap measurementBitmap = new Bitmap(1, 1))
            using (Graphics measurement = Graphics.FromImage(measurementBitmap))
            using (StringFormat measurementFormat =
                (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                measurementFormat.FormatFlags |= StringFormatFlags.NoWrap |
                    StringFormatFlags.MeasureTrailingSpaces;
                measured = measurement.MeasureString(value, font, 520,
                    measurementFormat);
            }
            int width = Math.Max(150, Math.Min(430,
                (int)Math.Ceiling(measured.Width) + 30));
            int height = Math.Max(40, Math.Min(72,
                (int)Math.Ceiling(measured.Height) + 14));
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (StringFormat format =
                (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags |= StringFormatFlags.NoWrap |
                    StringFormatFlags.MeasureTrailingSpaces;
                RectangleF area = new RectangleF(4, 1, width - 8, height - 3);
                Color outlineColor = textColor.R < 128
                    ? Color.FromArgb(110, 255, 255, 255)
                    : Color.FromArgb(125, 0, 0, 0);
                using (SolidBrush outline = new SolidBrush(outlineColor))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(opacity,
                    textColor.R, textColor.G, textColor.B)))
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        for (int x = -1; x <= 1; x++)
                        {
                            if (x == 0 && y == 0) continue;
                            RectangleF shifted = new RectangleF(area.X + x,
                                area.Y + y, area.Width, area.Height);
                            graphics.DrawString(value, font, outline, shifted, format);
                        }
                    }
                    graphics.DrawString(value, font, fill, area, format);
                }
            }
            return bitmap;
        }

        private void FadeTick(object sender, EventArgs e)
        {
            if (_heldVirtualKeyCode > 0 &&
                KeyboardInputFormatter.IsVirtualKeyDown(_heldVirtualKeyCode))
            {
                _lastInputUtc = DateTime.UtcNow;
                if (_opacity != 255)
                {
                    _opacity = 255;
                    if (_surface != null && Visible)
                        LayeredSpriteRenderer.Show(this, _surface, _opacity);
                }
                return;
            }
            TimeSpan silence = DateTime.UtcNow - _lastInputUtc;
            if (silence < TimeSpan.FromSeconds(1)) return;
            if (_opacity <= 30)
            {
                HideImmediately();
                return;
            }
            _opacity = (byte)(_opacity - 30);
            if (_surface != null && Visible)
                LayeredSpriteRenderer.Show(this, _surface, _opacity);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_ownedResourcesDisposed)
            {
                _ownedResourcesDisposed = true;
                _fadeTimer.Dispose();
                if (_font != null) _font.Dispose();
                if (_surface != null) _surface.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
