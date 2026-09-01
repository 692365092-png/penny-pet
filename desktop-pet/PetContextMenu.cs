using System;
using System.Windows.Forms;

namespace PennyPet
{
    // Owns only the pet/tray menu surface. Product actions remain in PetForm
    // and are supplied as delegates so their order and behavior stay unchanged.
    internal sealed class PetContextMenu : IDisposable
    {
        private readonly PetContextMenuCommands _commands;

        internal PetContextMenu(string displayName, bool startAtLogin,
            bool showKeyOverlay, bool silentMode,
            PetContextMenuCommands commands)
        {
            if (commands == null) throw new ArgumentNullException("commands");
            _commands = commands;

            StatusItem = new ToolStripMenuItem("当前没有提醒");
            StatusItem.Enabled = false;
            SetReminderItem = new ToolStripMenuItem("添加提醒…");
            SetReminderItem.Click += delegate { _commands.ShowReminder(); };
            CancelItem = new ToolStripMenuItem("取消提醒");
            NewNoteItem = new ToolStripMenuItem("新建便利贴");
            NewNoteItem.Click += delegate { _commands.CreateNote(); };
            NewTodoItem = new ToolStripMenuItem("新建待办清单");
            NewTodoItem.Click += delegate { _commands.CreateTodo(); };
            NewScheduleItem = new ToolStripMenuItem("新建日程");
            // The collapsed tab carries the type icon. Keep the pet menu
            // command itself text-only.
            NewScheduleItem.Image = null;
            NewScheduleItem.DisplayStyle = ToolStripItemDisplayStyle.Text;
            NewScheduleItem.Click += delegate { _commands.CreateSchedule(); };
            ManageNotesItem = new ToolStripMenuItem("便利贴管理…");
            ManageNotesItem.Click += delegate { _commands.ManageNotes(); };
            CollapseNotesItem = new ToolStripMenuItem("收起全部便利贴到页签");
            CollapseNotesItem.Click += delegate { _commands.CollapseNotes(); };
            ExpandTabsItem = new ToolStripMenuItem("展开全部侧边页签");
            ExpandTabsItem.Click += delegate { _commands.ExpandTabs(); };
            RecoverWindowsItem = new ToolStripMenuItem(
                "展开全部并平铺到此屏幕");
            RecoverWindowsItem.Click += delegate { _commands.RecoverWindows(); };
            DailyContentItem = new ToolStripMenuItem("每日内容…");
            DailyContentItem.Click += delegate
            {
                _commands.ShowDailyContentSettings();
            };
            ScaleItem = new ToolStripMenuItem("调整桌宠大小…");
            ScaleItem.Click += delegate { _commands.ShowScale(); };
            StartupItem = new ToolStripMenuItem("开机自动启动");
            StartupItem.CheckOnClick = true;
            StartupItem.Checked = startAtLogin;
            StartupItem.Click += _commands.StartupClick;
            KeyboardItem = new ToolStripMenuItem("按键显示：正在检查");
            KeyboardItem.CheckOnClick = true;
            KeyboardItem.Checked = showKeyOverlay;
            KeyboardItem.Click += _commands.KeyboardClick;
            SilentItem = new ToolStripMenuItem("静默模式（隐藏日常气泡）");
            SilentItem.CheckOnClick = true;
            SilentItem.Checked = silentMode;
            SilentItem.Click += _commands.SilentClick;
            ContactAuthorItem = new ToolStripMenuItem("联系作者");
            ContactAuthorItem.Click += delegate { _commands.ContactAuthor(); };
            ToolStripMenuItem exitItem = new ToolStripMenuItem(
                "退出" + (displayName ?? String.Empty));
            exitItem.Click += delegate { _commands.Exit(); };

            Menu = new ContextMenuStrip();
            Menu.Items.Add(StatusItem);
            Menu.Items.Add(new ToolStripSeparator());
            Menu.Items.Add(NewNoteItem);
            Menu.Items.Add(NewTodoItem);
            Menu.Items.Add(NewScheduleItem);
            Menu.Items.Add(ManageNotesItem);
            Menu.Items.Add(CollapseNotesItem);
            Menu.Items.Add(ExpandTabsItem);
            Menu.Items.Add(RecoverWindowsItem);
            Menu.Items.Add(new ToolStripSeparator());
            Menu.Items.Add(SetReminderItem);
            Menu.Items.Add(CancelItem);
            Menu.Items.Add(new ToolStripSeparator());
            Menu.Items.Add(DailyContentItem);
            Menu.Items.Add(ScaleItem);
            Menu.Items.Add(KeyboardItem);
            Menu.Items.Add(SilentItem);
            Menu.Items.Add(new ToolStripSeparator());
            Menu.Items.Add(StartupItem);
            Menu.Items.Add(new ToolStripSeparator());
            Menu.Items.Add(ContactAuthorItem);
            Menu.Items.Add(new ToolStripSeparator());
            Menu.Items.Add(exitItem);
            Menu.Opening += delegate { _commands.Opening(); };
            Menu.Closed += delegate { _commands.Closed(); };
        }

        internal ContextMenuStrip Menu { get; private set; }
        internal ToolStripMenuItem StatusItem { get; private set; }
        internal ToolStripMenuItem SetReminderItem { get; private set; }
        internal ToolStripMenuItem CancelItem { get; private set; }
        internal ToolStripMenuItem NewNoteItem { get; private set; }
        internal ToolStripMenuItem NewTodoItem { get; private set; }
        internal ToolStripMenuItem NewScheduleItem { get; private set; }
        internal ToolStripMenuItem ManageNotesItem { get; private set; }
        internal ToolStripMenuItem CollapseNotesItem { get; private set; }
        internal ToolStripMenuItem ExpandTabsItem { get; private set; }
        internal ToolStripMenuItem RecoverWindowsItem { get; private set; }
        internal ToolStripMenuItem DailyContentItem { get; private set; }
        internal ToolStripMenuItem ScaleItem { get; private set; }
        internal ToolStripMenuItem StartupItem { get; private set; }
        internal ToolStripMenuItem KeyboardItem { get; private set; }
        internal ToolStripMenuItem SilentItem { get; private set; }
        internal ToolStripMenuItem ContactAuthorItem { get; private set; }

        public void Dispose()
        {
            if (Menu != null) Menu.Dispose();
        }
    }

    internal sealed class PetContextMenuCommands
    {
        internal Action Opening;
        internal Action Closed;
        internal Action ShowReminder;
        internal Action CreateNote;
        internal Action CreateTodo;
        internal Action CreateSchedule;
        internal Action ManageNotes;
        internal Action CollapseNotes;
        internal Action ExpandTabs;
        internal Action RecoverWindows;
        internal Action ShowDailyContentSettings;
        internal Action ShowScale;
        internal EventHandler StartupClick;
        internal EventHandler KeyboardClick;
        internal EventHandler SilentClick;
        internal Action ContactAuthor;
        internal Action Exit;
    }
}
