using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Interop;
using WF = System.Windows.Forms;

namespace PennyPet
{
    internal sealed class ScheduleItemDialog : Window
    {
        private readonly TextBox _nameBox;
        private readonly ReverseStepDateTimePicker _datePicker;

        internal ScheduleItemDialog(string text, DateTime targetDate)
        {
            Title = String.IsNullOrWhiteSpace(text) ? "新建日程" : "编辑日程";
            Width = 390;
            Height = 225;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
            FontSize = 14;
            SourceInitialized += delegate { RemoveWindowIcon(); };

            Grid root = new Grid();
            root.Margin = new Thickness(18);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1,
                GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,
                GridUnitType.Star) });

            TextBlock nameLabel = Label("日程名称：");
            Grid.SetRow(nameLabel, 0);
            Grid.SetColumn(nameLabel, 0);
            root.Children.Add(nameLabel);

            _nameBox = new TextBox();
            _nameBox.Text = text ?? String.Empty;
            _nameBox.MaxLength = ShortItemText.MaximumInputCharacters;
            _nameBox.Margin = new Thickness(0, 0, 0, 12);
            _nameBox.Padding = new Thickness(6, 4, 6, 4);
            InputMethod.SetIsInputMethodEnabled(_nameBox, true);
            InputMethod.SetIsInputMethodSuspended(_nameBox, false);
            Grid.SetRow(_nameBox, 0);
            Grid.SetColumn(_nameBox, 1);
            root.Children.Add(_nameBox);

            TextBlock dateLabel = Label("选择日期：");
            Grid.SetRow(dateLabel, 1);
            Grid.SetColumn(dateLabel, 0);
            root.Children.Add(dateLabel);

            _datePicker = new ReverseStepDateTimePicker();
            _datePicker.Format = WF.DateTimePickerFormat.Custom;
            _datePicker.CustomFormat = "yyyy年 MM月 dd日";
            _datePicker.Value = targetDate.Date;
            _datePicker.Dock = WF.DockStyle.Fill;
            WindowsFormsHost dateHost = new WindowsFormsHost();
            dateHost.Child = _datePicker;
            dateHost.Height = 29;
            dateHost.Margin = new Thickness(0, 0, 0, 12);
            Grid.SetRow(dateHost, 1);
            Grid.SetColumn(dateHost, 1);
            root.Children.Add(dateHost);

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            Button confirm = new Button();
            confirm.Content = "确定";
            confirm.Width = 86;
            confirm.Height = 31;
            confirm.Margin = new Thickness(0, 0, 10, 0);
            confirm.IsDefault = true;
            confirm.Click += delegate { Confirm(); };
            Button cancel = new Button();
            cancel.Content = "取消";
            cancel.Width = 86;
            cancel.Height = 31;
            cancel.IsCancel = true;
            cancel.Click += delegate { DialogResult = false; };
            actions.Children.Add(confirm);
            actions.Children.Add(cancel);
            Grid.SetRow(actions, 3);
            Grid.SetColumn(actions, 1);
            root.Children.Add(actions);

            Content = root;
            Loaded += delegate
            {
                _nameBox.Focus();
                _nameBox.SelectAll();
            };
        }

        internal string ScheduleText { get; private set; }
        internal DateTime ScheduleDate { get; private set; }

        internal static DateTime StepDateWithMouseWheel(DateTime current,
            int wheelDelta)
        {
            int notches = Math.Max(1, Math.Abs(wheelDelta) / 120);
            int direction = wheelDelta > 0 ? -1 : 1;
            try { return current.Date.AddDays(direction * notches); }
            catch { return current.Date; }
        }

        private static TextBlock Label(string text)
        {
            TextBlock label = new TextBlock();
            label.Text = text;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 0, 8, 12);
            return label;
        }

        private void Confirm()
        {
            string text = ShortItemText.NormalizeAndTruncate(_nameBox.Text);
            if (String.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(this, "请输入日程名称。", "Penny 日程",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                _nameBox.Focus();
                return;
            }
            ScheduleText = text;
            ScheduleDate = _datePicker.Value.Date;
            DialogResult = true;
        }

        private void RemoveWindowIcon()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            int style = GetWindowLong(handle, GwlExStyle);
            SetWindowLong(handle, GwlExStyle, style | WsExDlgModalFrame);
            SendMessage(handle, WmSetIcon, IntPtr.Zero, IntPtr.Zero);
            SendMessage(handle, WmSetIcon, new IntPtr(1), IntPtr.Zero);
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
        }

        private const int GwlExStyle = -20;
        private const int WsExDlgModalFrame = 0x00000001;
        private const int WmSetIcon = 0x0080;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpFrameChanged = 0x0020;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr window, int index,
            int value);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message,
            IntPtr word, IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window,
            IntPtr insertAfter, int x, int y, int width, int height,
            uint flags);
    }
}
