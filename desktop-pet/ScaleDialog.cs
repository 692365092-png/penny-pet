using System;
using System.Drawing;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed class ScaleDialog : Form
    {
        private readonly TrackBar _slider;
        private readonly Label _valueLabel;
        private readonly RadioButton _keySmall;
        private readonly RadioButton _keyMedium;
        private readonly RadioButton _keyLarge;

        public ScaleDialog(int currentPercent) : this(currentPercent, 100)
        {
        }

        public ScaleDialog(int currentPercent, int keyTextPercent)
        {
            Text = "调整桌宠与按键文字大小";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(500, 290);
            Font = StickyNoteForm.CreateSafeFont("Microsoft YaHei UI", 9F,
                FontStyle.Regular);

            Label hint = new Label();
            hint.Text = "拖动滑块调整桌宠大小（50%–200%，每格 10%）：";
            hint.AutoSize = true;
            hint.Location = new Point(24, 22);

            _slider = new TrackBar();
            _slider.Minimum = 5;
            _slider.Maximum = 20;
            _slider.TickFrequency = 1;
            _slider.SmallChange = 1;
            _slider.LargeChange = 1;
            _slider.Value = Math.Max(5, Math.Min(20,
                PetForm.NormalizeScalePercent(currentPercent) / 10));
            _slider.Location = new Point(20, 55);
            _slider.Size = new Size(455, 54);

            _valueLabel = new Label();
            _valueLabel.Font = StickyNoteForm.CreateSafeFont("Microsoft YaHei UI",
                16F, FontStyle.Bold);
            _valueLabel.TextAlign = ContentAlignment.MiddleCenter;
            _valueLabel.Location = new Point(190, 105);
            _valueLabel.Size = new Size(120, 40);
            _slider.ValueChanged += delegate { RefreshValue(); };

            GroupBox keySizeGroup = new GroupBox();
            keySizeGroup.Text = "键盘实时显示文字大小";
            keySizeGroup.Location = new Point(24, 145);
            keySizeGroup.Size = new Size(448, 74);
            _keySmall = new RadioButton();
            _keySmall.Text = "小（60%）";
            _keySmall.Location = new Point(34, 31);
            _keySmall.AutoSize = true;
            _keyMedium = new RadioButton();
            _keyMedium.Text = "中（100%）";
            _keyMedium.Location = new Point(170, 31);
            _keyMedium.AutoSize = true;
            _keyLarge = new RadioButton();
            _keyLarge.Text = "大（150%）";
            _keyLarge.Location = new Point(314, 31);
            _keyLarge.AutoSize = true;
            int normalizedTextSize =
                KeyboardOverlayForm.NormalizeTextScalePercent(keyTextPercent);
            _keySmall.Checked = normalizedTextSize == 60;
            _keyMedium.Checked = normalizedTextSize == 100;
            _keyLarge.Checked = normalizedTextSize == 150;
            keySizeGroup.Controls.Add(_keySmall);
            keySizeGroup.Controls.Add(_keyMedium);
            keySizeGroup.Controls.Add(_keyLarge);

            Button ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(310, 238);
            ok.Size = new Size(76, 32);
            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(396, 238);
            cancel.Size = new Size(76, 32);

            Controls.Add(hint);
            Controls.Add(_slider);
            Controls.Add(_valueLabel);
            Controls.Add(keySizeGroup);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            RefreshValue();
        }

        public int SelectedPercent
        {
            get { return _slider.Value * 10; }
        }

        public int SelectedKeyTextPercent
        {
            get
            {
                if (_keySmall.Checked) return 60;
                if (_keyLarge.Checked) return 150;
                return 100;
            }
        }

        private void RefreshValue()
        {
            _valueLabel.Text = SelectedPercent + "%";
        }
    }
}
