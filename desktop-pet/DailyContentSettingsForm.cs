using System.Drawing;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed class DailyContentSettingsForm : Form
    {
        private readonly CheckBox _dailyContent;
        private readonly CheckBox _solarTerm;

        internal DailyContentSettingsForm(bool dailyContentEnabled,
            bool solarTermEnabled)
        {
            Text = "每日内容";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(410, 174);
            Font = SystemFonts.MessageBoxFont;

            Label title = new Label();
            title.Text = "每日内容";
            title.Font = new Font(Font, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(22, 18);

            _dailyContent = new CheckBox();
            _dailyContent.Text = "每天第一次戳 Penny 时显示每日内容";
            _dailyContent.AutoSize = true;
            _dailyContent.Location = new Point(25, 53);
            _dailyContent.Checked = dailyContentEnabled;
            _dailyContent.CheckedChanged += delegate { RefreshChildState(); };

            _solarTerm = new CheckBox();
            _solarTerm.Text = "二十四节气";
            _solarTerm.AutoSize = true;
            _solarTerm.Location = new Point(54, 84);
            _solarTerm.Checked = solarTermEnabled;

            Button ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(239, 126);
            ok.Size = new Size(72, 30);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(319, 126);
            cancel.Size = new Size(72, 30);

            Controls.Add(title);
            Controls.Add(_dailyContent);
            Controls.Add(_solarTerm);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            RefreshChildState();
        }

        internal bool DailyContentEnabled
        {
            get { return _dailyContent.Checked; }
        }

        internal bool SolarTermEnabled
        {
            get { return _solarTerm.Checked; }
        }

        internal bool SolarTermControlEnabledForTest
        {
            get { return _solarTerm.Enabled; }
        }

        internal void SetDailyContentEnabledForTest(bool enabled)
        {
            _dailyContent.Checked = enabled;
        }

        private void RefreshChildState()
        {
            _solarTerm.Enabled = _dailyContent.Checked;
        }
    }
}
