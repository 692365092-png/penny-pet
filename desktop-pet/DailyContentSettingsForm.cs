using System;
using System.Drawing;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed class DailyContentSettingsForm : Form
    {
        private readonly CheckBox _dailyContent;
        private readonly CheckBox _solarTerm;
        private readonly CheckBox _almanac;
        private readonly CheckBox _weather;
        private readonly Label _weatherLocationLabel;
        private readonly Button _weatherLocationButton;
        private readonly ComboBox _zodiac;
        private readonly PetWeatherSource _weatherSource;
        private readonly PetWindowLayerCoordinator _windowLayers;
        private WeatherLocation _weatherLocation;

        internal DailyContentSettingsForm(bool dailyContentEnabled,
            bool solarTermEnabled, bool almanacEnabled, bool weatherEnabled,
            WeatherLocation weatherLocation, ZodiacSign zodiacSign,
            PetWeatherSource weatherSource,
            PetWindowLayerCoordinator windowLayers = null)
        {
            _weatherSource = weatherSource;
            _windowLayers = windowLayers ?? new PetWindowLayerCoordinator();
            _weatherLocation = weatherLocation;
            Text = "每日内容";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(450, 355);
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

            _almanac = new CheckBox();
            _almanac.Text = "传统黄历（民俗）";
            _almanac.AutoSize = true;
            _almanac.Location = new Point(54, 114);
            _almanac.Checked = almanacEnabled;

            _weather = new CheckBox();
            _weather.Text = "本地天气";
            _weather.AutoSize = true;
            _weather.Location = new Point(54, 144);
            _weather.Checked = weatherEnabled;

            _weatherLocationLabel = new Label();
            _weatherLocationLabel.AutoEllipsis = true;
            _weatherLocationLabel.Location = new Point(75, 174);
            _weatherLocationLabel.Size = new Size(252, 24);

            _weatherLocationButton = new Button();
            _weatherLocationButton.Text = "设置城市…";
            _weatherLocationButton.Location = new Point(334, 168);
            _weatherLocationButton.Size = new Size(94, 30);
            _weatherLocationButton.Click += SetWeatherLocation;

            Label attribution = new Label();
            attribution.Text = "天气数据：Open-Meteo";
            attribution.AutoSize = true;
            attribution.ForeColor = SystemColors.GrayText;
            attribution.Location = new Point(75, 201);

            Label zodiacLabel = new Label();
            zodiacLabel.Text = "我的星座：";
            zodiacLabel.AutoSize = true;
            zodiacLabel.Location = new Point(54, 237);

            _zodiac = new ComboBox();
            _zodiac.DropDownStyle = ComboBoxStyle.DropDownList;
            _zodiac.FormattingEnabled = true;
            _zodiac.Location = new Point(136, 232);
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
            ok.Location = new Point(279, 307);
            ok.Size = new Size(72, 30);
            ok.Click += delegate
            {
                if (_dailyContent.Checked && _weather.Checked &&
                    _weatherLocation == null)
                {
                    MessageBox.Show(this, "请先设置天气城市。", "每日内容",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult = DialogResult.OK;
            };

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(359, 307);
            cancel.Size = new Size(72, 30);

            Controls.Add(title);
            Controls.Add(_dailyContent);
            Controls.Add(_solarTerm);
            Controls.Add(_almanac);
            Controls.Add(_weather);
            Controls.Add(_weatherLocationLabel);
            Controls.Add(_weatherLocationButton);
            Controls.Add(attribution);
            Controls.Add(zodiacLabel);
            Controls.Add(_zodiac);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            RefreshWeatherLocationText();
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

        internal bool AlmanacEnabled
        {
            get { return _almanac.Checked; }
        }

        internal bool WeatherEnabled
        {
            get { return _weather.Checked; }
        }

        internal WeatherLocation SelectedWeatherLocation
        {
            get { return _weatherLocation; }
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
            if (DailyContentEnabled && WeatherEnabled &&
                _weatherLocation == null) return false;
            bool changed = settings.DailyContentEnabled !=
                    DailyContentEnabled ||
                settings.SolarTermEnabled != SolarTermEnabled ||
                settings.AlmanacEnabled != AlmanacEnabled ||
                settings.WeatherEnabled != WeatherEnabled ||
                !HasSameWeatherLocation(settings, _weatherLocation) ||
                settings.ZodiacSign != SelectedZodiacSign;
            settings.DailyContentEnabled = DailyContentEnabled;
            settings.SolarTermEnabled = SolarTermEnabled;
            settings.AlmanacEnabled = AlmanacEnabled;
            settings.WeatherEnabled = WeatherEnabled;
            if (_weatherLocation != null)
            {
                settings.WeatherLocationName = _weatherLocation.Name;
                settings.WeatherLocationAdmin1 = _weatherLocation.Admin1;
                settings.WeatherLocationCountry = _weatherLocation.Country;
                settings.WeatherLatitude = _weatherLocation.Latitude;
                settings.WeatherLongitude = _weatherLocation.Longitude;
                settings.WeatherTimezone = _weatherLocation.Timezone;
            }
            settings.ZodiacSign = SelectedZodiacSign;
            return changed;
        }

        internal bool SolarTermControlEnabledForTest
        {
            get { return _solarTerm.Enabled; }
        }

        internal bool AlmanacControlEnabledForTest
        {
            get { return _almanac.Enabled; }
        }

        internal bool ZodiacControlEnabledForTest
        {
            get { return _zodiac.Enabled; }
        }

        internal bool WeatherControlEnabledForTest
        {
            get { return _weather.Enabled; }
        }

        internal bool WeatherLocationButtonEnabledForTest
        {
            get { return _weatherLocationButton.Enabled; }
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

        internal void SetWeatherEnabledForTest(bool enabled)
        {
            _weather.Checked = enabled;
        }

        internal void SetAlmanacEnabledForTest(bool enabled)
        {
            _almanac.Checked = enabled;
        }

        internal void SetWeatherLocationForTest(WeatherLocation location)
        {
            _weatherLocation = location;
            RefreshWeatherLocationText();
        }

        private void RefreshChildState()
        {
            _solarTerm.Enabled = _dailyContent.Checked;
            _almanac.Enabled = _dailyContent.Checked;
            _weather.Enabled = _dailyContent.Checked;
            _weatherLocationButton.Enabled = _dailyContent.Checked &&
                _weatherSource != null;
            _zodiac.Enabled = _dailyContent.Checked;
        }

        private void SetWeatherLocation(object sender, EventArgs e)
        {
            if (_weatherSource == null) return;
            using (WeatherLocationDialog dialog =
                new WeatherLocationDialog(_weatherSource))
            {
                if (_windowLayers.ShowModal(this, dialog) != DialogResult.OK ||
                    dialog.SelectedLocation == null) return;
                _weatherLocation = dialog.SelectedLocation;
                RefreshWeatherLocationText();
            }
        }

        private void RefreshWeatherLocationText()
        {
            _weatherLocationLabel.Text = _weatherLocation == null
                ? "尚未设置城市" : _weatherLocation.DisplayName;
        }

        private static bool HasSameWeatherLocation(PetSettingsData settings,
            WeatherLocation location)
        {
            if (location == null)
                return String.IsNullOrEmpty(settings.WeatherLocationName) &&
                    String.IsNullOrEmpty(settings.WeatherTimezone);
            WeatherLocation saved;
            return WeatherLocation.TryCreate(settings.WeatherLocationName,
                settings.WeatherLocationAdmin1,
                settings.WeatherLocationCountry, settings.WeatherLatitude,
                settings.WeatherLongitude, settings.WeatherTimezone,
                out saved) && saved.StableKey == location.StableKey &&
                saved.DisplayName == location.DisplayName;
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
