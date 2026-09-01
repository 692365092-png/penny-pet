using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed class WeatherLocationDialog : Form
    {
        private readonly PetWeatherSource _weatherSource;
        private readonly TextBox _query;
        private readonly Button _search;
        private readonly ListBox _results;
        private readonly Label _status;
        private readonly Button _ok;

        internal WeatherLocationDialog(PetWeatherSource weatherSource)
        {
            _weatherSource = weatherSource ??
                throw new ArgumentNullException(nameof(weatherSource));
            Text = "设置天气城市";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(410, 255);
            Font = SystemFonts.MessageBoxFont;

            Label hint = new Label();
            hint.Text = "输入城市名称后搜索，再从结果中选择：";
            hint.AutoSize = true;
            hint.Location = new Point(20, 18);

            _query = new TextBox();
            _query.Location = new Point(23, 48);
            _query.Size = new Size(274, 27);
            _query.KeyDown += QueryKeyDown;

            _search = new Button();
            _search.Text = "搜索";
            _search.Location = new Point(307, 46);
            _search.Size = new Size(80, 30);
            _search.Click += async delegate { await SearchAsync(); };

            _results = new ListBox();
            _results.FormattingEnabled = true;
            _results.Location = new Point(23, 84);
            _results.Size = new Size(364, 96);
            _results.Format += delegate(object sender,
                ListControlConvertEventArgs e)
            {
                WeatherLocation location = e.ListItem as WeatherLocation;
                if (location != null) e.Value = location.DisplayName;
            };
            _results.SelectedIndexChanged += delegate
            {
                _ok.Enabled = _results.SelectedItem is WeatherLocation;
            };
            _results.DoubleClick += delegate
            {
                if (_ok.Enabled) DialogResult = DialogResult.OK;
            };

            _status = new Label();
            _status.AutoEllipsis = true;
            _status.Location = new Point(23, 187);
            _status.Size = new Size(364, 23);

            _ok = new Button();
            _ok.Text = "确定";
            _ok.DialogResult = DialogResult.OK;
            _ok.Enabled = false;
            _ok.Location = new Point(221, 213);
            _ok.Size = new Size(76, 30);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(311, 213);
            cancel.Size = new Size(76, 30);

            Controls.Add(hint);
            Controls.Add(_query);
            Controls.Add(_search);
            Controls.Add(_results);
            Controls.Add(_status);
            Controls.Add(_ok);
            Controls.Add(cancel);
            AcceptButton = _ok;
            CancelButton = cancel;
        }

        internal WeatherLocation SelectedLocation
        {
            get { return _results.SelectedItem as WeatherLocation; }
        }

        internal bool UsesCompactFormattedResultsForTest(
            WeatherLocation location)
        {
            return _results.FormattingEnabled && ClientSize.Width <= 410 &&
                ClientSize.Height <= 255 && _results.Height <= 100 &&
                _results.GetItemText(location) == location.DisplayName;
        }

        private async void QueryKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await SearchAsync();
        }

        private async Task SearchAsync()
        {
            string query = _query.Text.Trim();
            if (query.Length < 2)
            {
                _status.Text = "请输入至少两个字再搜索。";
                return;
            }
            _search.Enabled = false;
            _query.Enabled = false;
            _results.Items.Clear();
            _status.Text = "正在搜索…";
            try
            {
                IReadOnlyList<WeatherLocation> locations =
                    await _weatherSource.SearchLocationsAsync(query);
                foreach (WeatherLocation location in locations)
                    _results.Items.Add(location);
                _status.Text = locations.Count == 0
                    ? "没有找到匹配城市，请换个名称试试。"
                    : "请选择与你所在地区相符的城市。";
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal("weather-geocoding",
                    error);
                _status.Text = "城市搜索暂时不可用，请稍后再试。";
            }
            finally
            {
                if (!IsDisposed)
                {
                    _query.Enabled = true;
                    _search.Enabled = true;
                    _query.Focus();
                }
            }
        }
    }
}
