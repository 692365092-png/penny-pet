using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed class StickyAppearanceDialog : Form
    {
        private readonly Action<Color, int, Color> _preview;
        private readonly Action<bool> _completed;
        private readonly TrackBar _opacity;
        private readonly Label _opacityValue;
        private readonly RadioButton _blackText;
        private readonly RadioButton _whiteText;
        private Color _selectedColor;
        private bool _initializing;
        private bool _accepted;

        public StickyAppearanceDialog(Color[] palette, Color currentColor,
            int opacityPercent, Color textColor,
            Action<Color, int, Color> preview, Action<bool> completed)
        {
            _preview = preview;
            _completed = completed;
            _selectedColor = currentColor;
            _initializing = true;

            Text = "便签颜色与透明度";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(520, 260);
            Font = StickyNoteWindow.CreateSafeFont("Microsoft YaHei UI", 9F,
                FontStyle.Regular);
            BackColor = Color.FromArgb(250, 250, 250);

            Label colorTitle = SectionLabel("便签颜色", 14, 10);
            Controls.Add(colorTitle);
            Color[] colors = palette ?? new Color[0];
            const int columns = 11;
            const int swatchWidth = 39;
            const int swatchHeight = 30;
            const int gap = 7;
            int left = 10;
            int top = 36;
            for (int index = 0; index < colors.Length; index++)
            {
                ColorSwatch swatch = new ColorSwatch(colors[index]);
                int row = index / columns;
                int column = index % columns;
                swatch.Bounds = new Rectangle(left + column *
                    (swatchWidth + gap), top + row * (swatchHeight + gap),
                    swatchWidth, swatchHeight);
                swatch.IsSelected = colors[index].ToArgb() ==
                    currentColor.ToArgb();
                swatch.Click += SelectSwatch;
                Controls.Add(swatch);
            }

            int optionsTop = 148;
            Controls.Add(SectionLabel("背景透明度", 14, optionsTop + 4));
            _opacity = new TrackBar();
            _opacity.Minimum = 1;
            _opacity.Maximum = 10;
            _opacity.TickFrequency = 1;
            _opacity.SmallChange = 1;
            _opacity.LargeChange = 1;
            _opacity.AutoSize = false;
            _opacity.Bounds = new Rectangle(92, optionsTop, 235, 38);
            _opacity.Value = Math.Max(1, Math.Min(10,
                (int)Math.Round(opacityPercent / 10.0)));
            _opacity.ValueChanged += AppearanceValueChanged;
            Controls.Add(_opacity);
            _opacityValue = new Label();
            _opacityValue.TextAlign = ContentAlignment.MiddleLeft;
            _opacityValue.Bounds = new Rectangle(332, optionsTop + 4, 55, 24);
            Controls.Add(_opacityValue);

            Controls.Add(SectionLabel("文字颜色", 14, optionsTop + 46));
            _blackText = new RadioButton();
            _blackText.Text = "黑色";
            _blackText.AutoSize = true;
            _blackText.Location = new Point(92, optionsTop + 45);
            _blackText.CheckedChanged += AppearanceValueChanged;
            Controls.Add(_blackText);
            _whiteText = new RadioButton();
            _whiteText.Text = "白色";
            _whiteText.AutoSize = true;
            _whiteText.Location = new Point(158, optionsTop + 45);
            _whiteText.CheckedChanged += AppearanceValueChanged;
            Controls.Add(_whiteText);
            if (textColor.ToArgb() == Color.White.ToArgb())
                _whiteText.Checked = true;
            else
                _blackText.Checked = true;

            Label hint = new Label();
            hint.AutoSize = false;
            hint.TextAlign = ContentAlignment.MiddleRight;
            hint.ForeColor = Color.FromArgb(100, 100, 100);
            hint.Text = "颜色与透明度会实时应用到当前便签";
            hint.Bounds = new Rectangle(238, optionsTop + 43, 267, 26);
            Controls.Add(hint);

            Button confirm = new Button();
            confirm.Text = "确定";
            confirm.Bounds = new Rectangle(342, 226, 78, 28);
            confirm.Click += delegate
            {
                _accepted = true;
                Close();
            };
            Controls.Add(confirm);
            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Bounds = new Rectangle(428, 226, 78, 28);
            cancel.Click += delegate
            {
                _accepted = false;
                Close();
            };
            Controls.Add(cancel);
            AcceptButton = confirm;
            CancelButton = cancel;

            UpdateOpacityText();
            _initializing = false;
            FormClosed += delegate
            {
                if (_completed != null) _completed(_accepted);
            };
        }

        internal void EnableQaTargeting()
        {
            ShowInTaskbar = true;
        }

        private static Label SectionLabel(string text, int left, int top)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Font = StickyNoteWindow.CreateSafeFont("Microsoft YaHei UI", 9F,
                FontStyle.Bold);
            label.Location = new Point(left, top);
            return label;
        }

        private void SelectSwatch(object sender, EventArgs e)
        {
            ColorSwatch selected = sender as ColorSwatch;
            if (selected == null) return;
            _selectedColor = selected.SwatchColor;
            foreach (Control control in Controls)
            {
                ColorSwatch swatch = control as ColorSwatch;
                if (swatch == null) continue;
                swatch.IsSelected = Object.ReferenceEquals(swatch, selected);
                swatch.Invalidate();
            }
            RaisePreview();
        }

        private void AppearanceValueChanged(object sender, EventArgs e)
        {
            UpdateOpacityText();
            RaisePreview();
        }

        private void UpdateOpacityText()
        {
            if (_opacityValue != null && _opacity != null)
                _opacityValue.Text = (_opacity.Value * 10) + "%";
        }

        private void RaisePreview()
        {
            if (_initializing || _preview == null) return;
            Color text = _whiteText.Checked ? Color.White :
                Color.Black;
            _preview(_selectedColor, _opacity.Value * 10, text);
        }
    }

    internal sealed class ColorSwatch : Control
    {
        public ColorSwatch(Color color)
        {
            SwatchColor = color;
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint, true);
            AccessibleName = "便签颜色 " + color.R + "," + color.G + "," + color.B;
        }

        public Color SwatchColor { get; private set; }
        public bool IsSelected { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle outer = new Rectangle(1, 1,
                Math.Max(1, Width - 3), Math.Max(1, Height - 3));
            using (GraphicsPath path = RoundedRectangle(outer, 9))
            using (SolidBrush fill = new SolidBrush(SwatchColor))
            {
                e.Graphics.FillPath(fill, path);
                using (Pen border = new Pen(IsSelected
                    ? Color.FromArgb(40, 40, 40)
                    : Color.FromArgb(220, 220, 220), IsSelected ? 3F : 1F))
                    e.Graphics.DrawPath(border, path);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Space) return;
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = Math.Max(2, radius * 2);
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top,
                diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter,
                diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter,
                diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
