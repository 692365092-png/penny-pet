using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed class ReverseStepDateTimePicker : DateTimePicker
    {
        private const int WmKeyDown = 0x0100;
        private const int WmNotify = 0x004E;
        private const int VkUp = 0x26;
        private const int VkDown = 0x28;
        private const int UdnDeltaPos = -722;
        private bool _syntheticStep;

        protected override void WndProc(ref Message message)
        {
            if (!_syntheticStep && message.Msg == WmKeyDown)
            {
                int key = message.WParam.ToInt32();
                if (key == VkUp || key == VkDown)
                    message.WParam = new IntPtr(ReverseVirtualKey(key));
            }
            else if (message.Msg == WmNotify && message.LParam != IntPtr.Zero)
            {
                try
                {
                    NativeUpDown change = (NativeUpDown)Marshal.PtrToStructure(
                        message.LParam, typeof(NativeUpDown));
                    if (change.Header.Code == UdnDeltaPos)
                    {
                        change.Delta = -change.Delta;
                        Marshal.StructureToPtr(change, message.LParam, false);
                    }
                }
                catch { }
            }
            base.WndProc(ref message);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int key = e.Delta > 0 ? VkDown : VkUp;
            Message synthetic = Message.Create(Handle, WmKeyDown,
                new IntPtr(key), IntPtr.Zero);
            _syntheticStep = true;
            try { base.WndProc(ref synthetic); }
            finally { _syntheticStep = false; }
        }

        internal static int ReverseVirtualKey(int key)
        {
            if (key == VkUp) return VkDown;
            if (key == VkDown) return VkUp;
            return key;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeNotifyHeader
        {
            public IntPtr WindowFrom;
            public UIntPtr IdFrom;
            public int Code;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeUpDown
        {
            public NativeNotifyHeader Header;
            public int Position;
            public int Delta;
        }
    }

    internal sealed class ReminderDialog : Form
    {
        private readonly DateTimePicker _date;
        private readonly DateTimePicker _time;
        private readonly TextBox _text;
        private readonly ComboBox _fontSize;
        private readonly Label _preview;
        private readonly CheckBox _preAlert;
        private readonly ToolTip _stepHint;
        private bool _ownedResourcesDisposed;

        public ReminderDialog() : this(null, 10.5F, false, null)
        {
        }

        public ReminderDialog(string initialText, float initialFontSize,
            bool initialPreAlert)
            : this(initialText, initialFontSize, initialPreAlert, null)
        {
        }

        public ReminderDialog(string initialText, float initialFontSize,
            bool initialPreAlert, DateTime? initialDeadlineLocal)
        {
            DateTime suggested = initialDeadlineLocal.HasValue
                ? initialDeadlineLocal.Value : DefaultSuggestedLocal();
            if (suggested < DateTime.Today) suggested = DefaultSuggestedLocal();
            DateTime latestAllowed = DateTime.Today.AddYears(10);
            if (suggested.Date > latestAllowed) suggested = latestAllowed;
            Text = initialDeadlineLocal.HasValue ? "修改提醒" : "添加提醒";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(440, 487);
            Font = StickyNoteWindow.CreateSafeFont("Microsoft YaHei UI", 9F,
                FontStyle.Regular);
            ImeMode = ImeMode.NoControl;

            Label timeLabel = new Label();
            timeLabel.Text = "具体提醒时间（跟随 Windows 系统时间）：";
            timeLabel.AutoSize = true;
            timeLabel.Location = new Point(24, 22);

            _date = new ReverseStepDateTimePicker();
            _date.Format = DateTimePickerFormat.Custom;
            _date.CustomFormat = "yyyy年 MM月 dd日";
            _date.MinDate = DateTime.Today;
            _date.MaxDate = latestAllowed;
            _date.Value = suggested.Date;
            _date.Location = new Point(24, 52);
            _date.Size = new Size(205, 28);

            _time = new ReverseStepDateTimePicker();
            _time.Format = DateTimePickerFormat.Custom;
            _time.CustomFormat = "HH:mm:ss";
            _time.ShowUpDown = true;
            _time.Value = suggested;
            _time.Location = new Point(245, 52);
            _time.Size = new Size(165, 28);

            _stepHint = new ToolTip();
            _stepHint.SetToolTip(_date,
                "鼠标滚轮向上：往前；向下：往后。只修改当前选中的年月日。 ");
            _stepHint.SetToolTip(_time,
                "↑ 或滚轮向上：往前；↓ 或滚轮向下：往后。只修改当前选中的时分秒。 ");

            _preAlert = new CheckBox();
            _preAlert.Text = "提前二十秒提醒";
            _preAlert.AutoSize = true;
            _preAlert.Checked = initialPreAlert;
            _preAlert.Location = new Point(24, 88);
            _stepHint.SetToolTip(_preAlert,
                "勾选后，到期前 20 秒显示小字号倒计时气泡。");

            Label textLabel = new Label();
            textLabel.Text = "提醒文字：";
            textLabel.AutoSize = true;
            textLabel.Location = new Point(24, 122);

            _text = new TextBox();
            _text.Multiline = true;
            _text.AcceptsReturn = true;
            _text.ScrollBars = ScrollBars.Vertical;
            _text.MaxLength = ShortItemText.MaximumInputCharacters;
            _text.ImeMode = ImeMode.NoControl;
            _text.Text = String.IsNullOrWhiteSpace(initialText)
                ? "该休息一下啦。" : initialText.Trim();
            _text.Location = new Point(24, 150);
            _text.Size = new Size(386, 92);

            Label formatLabel = new Label();
            formatLabel.Text = "便利贴内倒计时字号：";
            formatLabel.AutoSize = true;
            formatLabel.Location = new Point(24, 254);

            _fontSize = new ComboBox();
            _fontSize.DropDownStyle = ComboBoxStyle.DropDownList;
            _fontSize.Items.AddRange(new object[] {
                "特小 9", "小 10.5", "中 16", "大 22", "特大 48" });
            _fontSize.Location = new Point(24, 278);
            _fontSize.Size = new Size(150, 28);
            SelectClosestFontSize(initialFontSize);

            Label previewLabel = new Label();
            previewLabel.Text = "便利贴内显示预览：";
            previewLabel.AutoSize = true;
            previewLabel.Location = new Point(24, 320);
            _preview = new Label();
            _preview.BorderStyle = BorderStyle.FixedSingle;
            _preview.TextAlign = ContentAlignment.MiddleCenter;
            _preview.AutoEllipsis = true;
            _preview.Location = new Point(24, 344);
            _preview.Size = new Size(386, 78);

            Button ok = new Button();
            ok.Text = "添加";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(240, 436);
            ok.Size = new Size(80, 34);
            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(330, 436);
            cancel.Size = new Size(80, 34);

            Controls.Add(timeLabel);
            Controls.Add(_date);
            Controls.Add(_time);
            Controls.Add(_preAlert);
            Controls.Add(textLabel);
            Controls.Add(_text);
            Controls.Add(formatLabel);
            Controls.Add(_fontSize);
            Controls.Add(previewLabel);
            Controls.Add(_preview);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            _text.TextChanged += delegate { UpdatePreview(); };
            _fontSize.SelectedIndexChanged += delegate
            {
                UpdatePreview();
                EventHandler handler = ReminderFontSizePreviewChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            UpdatePreview();
            FormClosing += ValidateBeforeClose;
        }

        public event EventHandler ReminderFontSizePreviewChanged;

        public DateTime DeadlineLocal
        {
            get
            {
                DateTime value = _date.Value.Date + _time.Value.TimeOfDay;
                return DateTime.SpecifyKind(value, DateTimeKind.Local);
            }
        }

        public string ReminderText
        {
            get { return ShortItemText.Normalize(_text.Text); }
        }

        public float ReminderFontSizePoints
        {
            get
            {
                float points;
                return StickyNoteWindow.TryParseFontSize(
                    Convert.ToString(_fontSize.SelectedItem), out points)
                    ? points : 10.5F;
            }
        }

        public bool PreAlertEnabled
        {
            get { return _preAlert.Checked; }
        }

        internal bool UsesUnforcedMultilingualIme
        {
            get { return ImeMode == ImeMode.NoControl &&
                _text.ImeMode == ImeMode.NoControl; }
        }

        internal bool ExerciseSizePreviewForTest()
        {
            _text.Text = "字号切换后内容保留测试";
            for (int index = 0; index < _fontSize.Items.Count; index++)
            {
                float points;
                if (!StickyNoteWindow.TryParseFontSize(
                    Convert.ToString(_fontSize.Items[index]), out points) ||
                    Math.Abs(points - 48F) >= 0.1F) continue;
                _fontSize.SelectedIndex = index;
                break;
            }
            _preAlert.Checked = true;
            UpdatePreview();
            return ReminderText == "字号切换后内容保留测试" &&
                _preview.Text == "字号切换后内容保留测试" &&
                _preview.Font != null &&
                Math.Abs(_preview.Font.SizeInPoints - 48F) < 0.2F &&
                String.Equals(_preview.Font.Name, "Microsoft YaHei UI",
                    StringComparison.CurrentCultureIgnoreCase) &&
                _fontSize.Items.Count == 5 &&
                Convert.ToString(_fontSize.Items[0]) == "特小 9" &&
                Convert.ToString(_fontSize.Items[1]) == "小 10.5" &&
                Convert.ToString(_fontSize.Items[2]) == "中 16" &&
                Convert.ToString(_fontSize.Items[3]) == "大 22" &&
                Convert.ToString(_fontSize.Items[4]) == "特大 48" &&
                PreAlertEnabled;
        }

        internal static DateTime DefaultSuggestedLocal()
        {
            return DateTime.Now;
        }

        private void SelectClosestFontSize(float initialPoints)
        {
            float requested = Math.Max(6F, Math.Min(72F, initialPoints));
            int bestIndex = 0;
            float bestDistance = Single.MaxValue;
            for (int index = 0; index < _fontSize.Items.Count; index++)
            {
                float candidate;
                if (!StickyNoteWindow.TryParseFontSize(
                    Convert.ToString(_fontSize.Items[index]), out candidate)) continue;
                float distance = Math.Abs(candidate - requested);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestIndex = index;
            }
            _fontSize.SelectedIndex = bestIndex;
        }

        private void UpdatePreview()
        {
            if (_preview == null || _fontSize == null) return;
            string text = (_text.Text ?? String.Empty).Trim();
            _preview.Text = text.Length == 0 ? "提醒文字预览" : text;
            _preview.Font = StickyNoteWindow.CreateSafeFont("Microsoft YaHei UI",
                ReminderFontSizePoints, FontStyle.Regular);
        }

        private void ValidateBeforeClose(object sender, FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK) return;
            if (String.IsNullOrWhiteSpace(ReminderText))
            {
                MessageBox.Show(this, "请输入要显示的提醒文字。", Text);
                e.Cancel = true;
                return;
            }
            if (!ShortItemText.Fits(ReminderText))
            {
                MessageBox.Show(this,
                    "提醒内容最多约 50 个汉字或 100 个英文字符。",
                    Text);
                e.Cancel = true;
                return;
            }
            if (DeadlineLocal <= DateTime.Now.AddSeconds(1))
            {
                MessageBox.Show(this, "提醒时间必须晚于当前系统时间。", Text);
                e.Cancel = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_ownedResourcesDisposed)
            {
                _ownedResourcesDisposed = true;
                if (_stepHint != null) _stepHint.Dispose();
            }
            base.Dispose(disposing);
        }
    }


}
