using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Color = System.Drawing.Color;
using WF = System.Windows.Forms;
using W = System.Windows;
using WC = System.Windows.Controls;

namespace PennyPet
{
    internal sealed partial class StickyNoteWindow
    {

        private static void AddFontSizeOptions(WC.ComboBox sizeBox,
            bool compactListOnly)
        {
            sizeBox.Items.Clear();
            string[] values = compactListOnly
                ? new string[] { "特小 9", "小 10.5", "中 16", "大 22",
                    "特大 48" }
                : new string[] { "小五 9", "五号 10.5", "小四 12", "四号 14",
                    "小三 15", "三号 16", "小二 18", "二号 22", "小一 24",
                    "一号 26", "小初 36", "初号 42", "48", "56", "72" };
            foreach (string value in values) sizeBox.Items.Add(value);
        }

        private static void SelectComboText(WC.ComboBox combo, string value)
        {
            int match = -1;
            for (int index = 0; index < combo.Items.Count; index++)
            {
                string item = Convert.ToString(combo.Items[index]);
                if (String.Equals(item, value,
                    StringComparison.CurrentCultureIgnoreCase) ||
                    item.EndsWith(" " + value, StringComparison.Ordinal))
                {
                    match = index;
                    break;
                }
            }
            combo.SelectedIndex = match;
        }

        private static void ApplyTextBrush(W.DependencyObject root,
            System.Windows.Media.Brush brush)
        {
            if (root == null) return;
            WC.TextBlock text = root as WC.TextBlock;
            if (text != null) text.Foreground = brush;
            WC.TextBox input = root as WC.TextBox;
            if (input != null) input.Foreground = brush;
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < count; index++)
                ApplyTextBrush(System.Windows.Media.VisualTreeHelper.GetChild(
                    root, index), brush);
        }

        internal static string FormatScheduleCountdown(DateTime targetDate,
            DateTime today)
        {
            int days = (targetDate.Date - today.Date).Days;
            if (days == 0) return "今天";
            if (days > 0) return days + "天";
            return "已过" + Math.Abs(days) + "天";
        }

        internal static int ScheduleInsertionIndex(
            IList<StickyScheduleItem> items, StickyScheduleItem inserted)
        {
            if (items == null || inserted == null) return 0;
            int index = 0;
            if (inserted.IsPinned)
            {
                while (index < items.Count && items[index] != null &&
                    items[index].IsPinned) index++;
                return index;
            }
            while (index < items.Count && items[index] != null &&
                items[index].IsPinned) index++;
            while (index < items.Count && items[index] != null &&
                !items[index].IsPinned &&
                items[index].TargetDate <= inserted.TargetDate) index++;
            return index;
        }

        internal static float NormalizeScheduleFontSize(float points)
        {
            if (points <= 9.75F) return 9F;
            if (points <= 13.25F) return 10.5F;
            if (points <= 19F) return 16F;
            if (points <= 35F) return 22F;
            return 48F;
        }

        internal static string ScheduleFontSizeLabel(float points)
        {
            float normalized = NormalizeScheduleFontSize(points);
            if (normalized <= 9F) return "特小 9";
            if (normalized <= 10.5F) return "小 10.5";
            if (normalized <= 16F) return "中 16";
            if (normalized <= 22F) return "大 22";
            return "特大 48";
        }

        internal static string BuildPlainTextFromSchedules(
            IEnumerable<StickyScheduleItem> items)
        {
            StringBuilder body = new StringBuilder();
            if (items == null) return String.Empty;
            foreach (StickyScheduleItem item in items)
            {
                if (item == null) continue;
                if (body.Length > 0) body.AppendLine();
                body.Append(item.TargetDate.ToString("yyyy-MM-dd"))
                    .Append(' ').Append(item.Text);
            }
            return body.ToString();
        }
    }
}
