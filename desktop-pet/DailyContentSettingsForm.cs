using System;
using System.Drawing;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed class DailyContentSettingsForm : Form
    {
        private readonly CheckBox _dailyContent;
        private readonly CheckBox _solarTerm;
        private readonly ComboBox _zodiac;

        internal DailyContentSettingsForm(bool dailyContentEnabled,
            bool solarTermEnabled, ZodiacSign zodiacSign)
        {
            Text = "每日内容";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(410, 220);
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

            Label zodiacLabel = new Label();
            zodiacLabel.Text = "我的星座：";
            zodiacLabel.AutoSize = true;
            zodiacLabel.Location = new Point(54, 122);

            _zodiac = new ComboBox();
            _zodiac.DropDownStyle = ComboBoxStyle.DropDownList;
            _zodiac.FormattingEnabled = true;
            _zodiac.Location = new Point(136, 117);
            _zodiac.Size = new Size(180, 28);
            _zodiac.Format += delegate(object sender,
                ListControlConvertEventArgs e)
            {
                if (e.ListItem is ZodiacSign)
                    e.Value = ZodiacDisplayName((ZodiacSign)e.ListItem);
            };
            foreach (ZodiacSign sign in Enum.GetValues(typeof(ZodiacSign)))
                _zodiac.Items.Add(sign);
            _zodiac.SelectedItem = PetSettingRules.NormalizeZodiacSign(
                zodiacSign);

            Button ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(239, 172);
            ok.Size = new Size(72, 30);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(319, 172);
            cancel.Size = new Size(72, 30);

            Controls.Add(title);
            Controls.Add(_dailyContent);
            Controls.Add(_solarTerm);
            Controls.Add(zodiacLabel);
            Controls.Add(_zodiac);
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

        internal ZodiacSign SelectedZodiacSign
        {
            get
            {
                return _zodiac.SelectedItem is ZodiacSign
                    ? (ZodiacSign)_zodiac.SelectedItem : ZodiacSign.None;
            }
        }

        internal bool ApplyIfAccepted(PetSettingsData settings,
            DialogResult result)
        {
            if (settings == null || result != DialogResult.OK) return false;
            bool changed = settings.DailyContentEnabled !=
                    DailyContentEnabled ||
                settings.SolarTermEnabled != SolarTermEnabled ||
                settings.ZodiacSign != SelectedZodiacSign;
            settings.DailyContentEnabled = DailyContentEnabled;
            settings.SolarTermEnabled = SolarTermEnabled;
            settings.ZodiacSign = SelectedZodiacSign;
            return changed;
        }

        internal bool SolarTermControlEnabledForTest
        {
            get { return _solarTerm.Enabled; }
        }

        internal bool ZodiacControlEnabledForTest
        {
            get { return _zodiac.Enabled; }
        }

        internal string ZodiacDisplayNameForTest
        {
            get { return _zodiac.GetItemText(_zodiac.SelectedItem); }
        }

        internal void SetDailyContentEnabledForTest(bool enabled)
        {
            _dailyContent.Checked = enabled;
        }

        internal void SetZodiacSignForTest(ZodiacSign sign)
        {
            _zodiac.SelectedItem = PetSettingRules.NormalizeZodiacSign(sign);
        }

        private void RefreshChildState()
        {
            _solarTerm.Enabled = _dailyContent.Checked;
            _zodiac.Enabled = _dailyContent.Checked;
        }

        private static string ZodiacDisplayName(ZodiacSign sign)
        {
            switch (sign)
            {
                case ZodiacSign.Aries: return "白羊座";
                case ZodiacSign.Taurus: return "金牛座";
                case ZodiacSign.Gemini: return "双子座";
                case ZodiacSign.Cancer: return "巨蟹座";
                case ZodiacSign.Leo: return "狮子座";
                case ZodiacSign.Virgo: return "处女座";
                case ZodiacSign.Libra: return "天秤座";
                case ZodiacSign.Scorpio: return "天蝎座";
                case ZodiacSign.Sagittarius: return "射手座";
                case ZodiacSign.Capricorn: return "摩羯座";
                case ZodiacSign.Aquarius: return "水瓶座";
                case ZodiacSign.Pisces: return "双鱼座";
                default: return "暂未设置";
            }
        }
    }
}
