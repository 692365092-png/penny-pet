using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        public static void RunStickyInputProbe(string outputPath)
        {
            Stopwatch timer = Stopwatch.StartNew();
            int caseCount = 0;
            int interactionCount = 0;
            bool singleWindowArchitecture = true;
            bool closeOk = true;
            bool editorInteractionOk = true;
            bool appearanceCloseStressOk = true;
            bool winFormsKeyboardInteropOk = true;
            string failure = null;
            try
            {
                int[] opacities = new int[] { 10, 20, 30, 40, 50, 60, 70, 80, 90 };
                foreach (bool todoMode in new bool[] { false, true })
                {
                    foreach (int opacity in opacities)
                    {
                        StickyNoteData data = new StickyNoteData();
                        data.Title = todoMode ? "透明待办输入压力测试" :
                            "透明正文输入压力测试";
                        data.Text = "半透明便签输入回归测试 abcdefghijklmnopqrstuvwxyz";
                        data.IsTodoList = todoMode;
                        data.BackgroundOpacityPercent = opacity;
                        data.ColorArgb = StickyNoteWindow.PaletteColorForTest(
                            caseCount % 33).ToArgb();
                        data.TextColorArgb = caseCount % 2 == 0
                            ? Color.Black.ToArgb() : Color.White.ToArgb();
                        data.Width = 420;
                        data.Height = 320;
                        if (todoMode)
                            data.TodoItems.Add(new StickyTodoItem("现有待办项目", false));
                        using (StickyNoteWindow note = new StickyNoteWindow(data))
                        {
                            note.CreateControl();
                            singleWindowArchitecture &=
                                note.LegacyInputProxyHandleForTest == IntPtr.Zero &&
                                !note.UsesLegacyInputProxyForTest &&
                                note.UsesImeCompatibleEditor;
                            if (caseCount == 0)
                            {
                                note.EnableWinFormsKeyboardInterop();
                                winFormsKeyboardInteropOk &=
                                    note.UsesWinFormsKeyboardInteropForTest;
                            }
                            if (caseCount == 0)
                                appearanceCloseStressOk &=
                                    note.ExerciseAppearanceCloseStressForTest(20);
                            editorInteractionOk &= todoMode
                                ? note.ExerciseTodoWrapAndInlineEditForTest()
                                : note.ExerciseSmoothFormatInteractionForTest();
                            interactionCount += 5;
                            note.HideNote();
                            closeOk &= !note.Visible;
                        }
                        caseCount++;
                    }
                }
            }
            catch (Exception error)
            {
                failure = error.GetType().Name + ": " + error.Message;
            }
            timer.Stop();
            bool ok = failure == null && singleWindowArchitecture && closeOk &&
                editorInteractionOk && appearanceCloseStressOk &&
                winFormsKeyboardInteropOk &&
                caseCount == 18 && interactionCount == 90;
            string escapedFailure = failure == null ? "" : failure.Replace(
                "\\", "\\\\").Replace("\"", "\\\"");
            string json = "{\n" +
                "  \"ok\": " + Bool(ok) + ",\n" +
                "  \"single_window_input\": " +
                    Bool(singleWindowArchitecture) + ",\n" +
                "  \"close_ok\": " + Bool(closeOk) + ",\n" +
                "  \"editor_interaction_ok\": " +
                    Bool(editorInteractionOk) + ",\n" +
                "  \"appearance_x_close_stress_ok\": " +
                    Bool(appearanceCloseStressOk) + ",\n" +
                "  \"winforms_keyboard_interop_ok\": " +
                    Bool(winFormsKeyboardInteropOk) + ",\n" +
                "  \"normal_and_todo_cases\": " + caseCount + ",\n" +
                "  \"interaction_checks\": " + interactionCount + ",\n" +
                "  \"elapsed_ms\": " + timer.ElapsedMilliseconds + ",\n" +
                "  \"failure\": \"" + escapedFailure + "\"\n" +
                "}\n";
            string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(outputPath, json, new UTF8Encoding(false));
        }

        public static void RunStickyWinFormsPumpProbe(string outputPath)
        {
            Stopwatch timer = Stopwatch.StartNew();
            bool visible = false;
            bool handleCreated = false;
            bool editorOk = false;
            bool dragPositionOk = false;
            bool hideShowOk = false;
            string failure = null;
            StickyNoteData data = new StickyNoteData();
            data.Title = "WinForms 消息循环集成测试";
            data.Text = "60% 单窗口编辑器";
            data.BackgroundOpacityPercent = 60;
            data.X = -2400;
            data.Y = -2400;
            data.Width = 360;
            data.Height = 260;
            using (StickyNoteWindow note = new StickyNoteWindow(data))
            using (System.Windows.Forms.Timer probeTimer =
                new System.Windows.Forms.Timer())
            {
                probeTimer.Interval = 250;
                probeTimer.Tick += delegate
                {
                    probeTimer.Stop();
                    try
                    {
                        visible = note.Visible;
                        handleCreated = note.Handle != IntPtr.Zero &&
                            note.LegacyInputProxyHandleForTest == IntPtr.Zero;
                        editorOk = note.ExerciseSmoothFormatInteractionForTest();
                        Point moved = new Point(note.Left + 17, note.Top + 13);
                        note.Location = moved;
                        dragPositionOk = note.Location == moved;
                        note.HideNote();
                        bool hidden = !note.Visible;
                        note.ShowRestored();
                        hideShowOk = hidden && note.Visible;
                    }
                    catch (Exception error)
                    {
                        failure = error.GetType().Name + ": " + error.Message;
                    }
                    finally
                    {
                        note.CloseForApplicationExit();
                        Application.ExitThread();
                    }
                };
                note.ShowRestored();
                probeTimer.Start();
                Application.Run();
            }
            timer.Stop();
            bool ok = failure == null && visible && handleCreated && editorOk &&
                dragPositionOk && hideShowOk;
            string escapedFailure = failure == null ? "" : failure.Replace(
                "\\", "\\\\").Replace("\"", "\\\"");
            string json = "{\n" +
                "  \"ok\": " + Bool(ok) + ",\n" +
                "  \"winforms_message_pump_visible\": " + Bool(visible) + ",\n" +
                "  \"single_wpf_window_handle\": " + Bool(handleCreated) + ",\n" +
                "  \"editor_dispatch_ok\": " + Bool(editorOk) + ",\n" +
                "  \"position_update_ok\": " + Bool(dragPositionOk) + ",\n" +
                "  \"hide_show_ok\": " + Bool(hideShowOk) + ",\n" +
                "  \"elapsed_ms\": " + timer.ElapsedMilliseconds + ",\n" +
                "  \"failure\": \"" + escapedFailure + "\"\n" +
                "}\n";
            string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(outputPath, json, new UTF8Encoding(false));
        }

        public static void RunStickyTransparencyOverlapProbe(string outputPath)
        {
            Stopwatch timer = Stopwatch.StartNew();
            string fullOutputPath = Path.GetFullPath(outputPath);
            string parent = Path.GetDirectoryName(fullOutputPath);
            if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            string screenshotPath = Path.ChangeExtension(fullOutputPath, ".png");

            int requestedOpacity = 60;
            int rawBodyAlpha = -1;
            int maximumRenderedAlpha = -1;
            bool transparentWindowMode = false;
            bool perPixelAlphaOk = false;
            bool opaqueTextOk = false;
            bool overlapCompositionOk = false;
            Color noteOneActual = Color.Empty;
            Color noteTwoActual = Color.Empty;
            Color overlapActual = Color.Empty;
            Color noteOneExpected = Color.Empty;
            Color noteTwoExpected = Color.Empty;
            Color overlapExpected = Color.Empty;
            int noteOneDistance = Int32.MaxValue;
            int noteTwoDistance = Int32.MaxValue;
            int overlapDistance = Int32.MaxValue;
            string failure = null;

            Color stageColor = Color.FromArgb(42, 55, 70);
            Color noteOneColor = Color.FromArgb(255, 96, 96);
            Color noteTwoColor = Color.FromArgb(80, 160, 255);

            try
            {
                StickyNoteData alphaData = CreateTransparencyProbeNote(
                    "Alpha 原始层检查", "文字必须保持完全不透明", noteOneColor,
                    requestedOpacity, 420, 320);
                using (StickyNoteWindow alphaNote = new StickyNoteWindow(alphaData))
                {
                    transparentWindowMode = alphaNote.AllowsTransparency &&
                        alphaNote.Background == System.Windows.Media.Brushes.Transparent;
                    rawBodyAlpha = alphaNote.BackgroundAlphaForTest;
                    maximumRenderedAlpha = alphaNote.TextAlphaForTest;
                }
                perPixelAlphaOk = rawBodyAlpha >= 150 && rawBodyAlpha <= 156;
                opaqueTextOk = maximumRenderedAlpha == 255;

                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                int stageWidth = Math.Min(760, Math.Max(720, work.Width - 120));
                int stageHeight = Math.Min(540, Math.Max(520, work.Height - 120));
                Rectangle stageBounds = new Rectangle(
                    work.Left + Math.Max(20, (work.Width - stageWidth) / 2),
                    work.Top + Math.Max(20, (work.Height - stageHeight) / 2),
                    stageWidth, stageHeight);

                StickyNoteData noteOneData = CreateTransparencyProbeNote(
                    "透明便签 A", String.Empty, noteOneColor, requestedOpacity,
                    420, 320);
                StickyNoteData noteTwoData = CreateTransparencyProbeNote(
                    "透明便签 B", String.Empty, noteTwoColor, requestedOpacity,
                    420, 320);
                using (Form stage = new Form())
                using (StickyNoteWindow noteOne = new StickyNoteWindow(noteOneData))
                using (StickyNoteWindow noteTwo = new StickyNoteWindow(noteTwoData))
                using (Bitmap screenshot = new Bitmap(stageBounds.Width,
                    stageBounds.Height, PixelFormat.Format32bppArgb))
                {
                    stage.Text = "Penny 双透明便签合成验收背景";
                    stage.FormBorderStyle = FormBorderStyle.None;
                    stage.StartPosition = FormStartPosition.Manual;
                    stage.ShowInTaskbar = false;
                    stage.TopMost = true;
                    stage.BackColor = stageColor;
                    stage.Bounds = stageBounds;

                    noteOne.Location = new Point(stage.Left + 40, stage.Top + 80);
                    noteTwo.Location = new Point(stage.Left + 260, stage.Top + 180);
                    noteOne.TopMost = true;
                    noteTwo.TopMost = true;
                    stage.Show();
                    noteOne.Show();
                    noteTwo.Show();
                    noteTwo.BringToFront();
                    PumpUi(700);

                    using (Graphics capture = Graphics.FromImage(screenshot))
                        capture.CopyFromScreen(stage.Left, stage.Top, 0, 0,
                            stage.Size, CopyPixelOperation.SourceCopy);
                    screenshot.Save(screenshotPath, ImageFormat.Png);

                    noteOneActual = screenshot.GetPixel(120, 300);
                    noteTwoActual = screenshot.GetPixel(600, 350);
                    overlapActual = screenshot.GetPixel(360, 300);

                    noteOneExpected = BlendForExpected(noteOneColor, stageColor,
                        requestedOpacity);
                    noteTwoExpected = BlendForExpected(noteTwoColor, stageColor,
                        requestedOpacity);
                    overlapExpected = BlendForExpected(noteTwoColor,
                        noteOneExpected, requestedOpacity);
                    noteOneDistance = ColorDistance(noteOneActual, noteOneExpected);
                    noteTwoDistance = ColorDistance(noteTwoActual, noteTwoExpected);
                    overlapDistance = ColorDistance(overlapActual, overlapExpected);
                    overlapCompositionOk = noteOneDistance <= 12 &&
                        noteTwoDistance <= 12 && overlapDistance <= 16;

                    noteTwo.CloseForApplicationExit();
                    noteOne.CloseForApplicationExit();
                    stage.Close();
                    PumpUi(50);
                }
            }
            catch (Exception error)
            {
                failure = error.GetType().Name + ": " + error.Message;
            }

            timer.Stop();
            bool ok = failure == null && transparentWindowMode && perPixelAlphaOk &&
                opaqueTextOk && overlapCompositionOk;
            string escapedFailure = failure == null ? String.Empty :
                failure.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string escapedScreenshot = screenshotPath.Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
            string json = "{\n" +
                "  \"ok\": " + Bool(ok) + ",\n" +
                "  \"true_transparent_wpf_window\": " +
                    Bool(transparentWindowMode) + ",\n" +
                "  \"requested_background_opacity_percent\": " +
                    requestedOpacity + ",\n" +
                "  \"raw_body_alpha\": " + rawBodyAlpha + ",\n" +
                "  \"per_pixel_alpha_ok\": " + Bool(perPixelAlphaOk) + ",\n" +
                "  \"maximum_rendered_alpha\": " + maximumRenderedAlpha + ",\n" +
                "  \"opaque_text_ok\": " + Bool(opaqueTextOk) + ",\n" +
                "  \"two_window_overlap_composition_ok\": " +
                    Bool(overlapCompositionOk) + ",\n" +
                "  \"note_one_only_actual\": \"" + ColorText(noteOneActual) + "\",\n" +
                "  \"note_one_only_expected\": \"" + ColorText(noteOneExpected) + "\",\n" +
                "  \"note_one_color_distance\": " + noteOneDistance + ",\n" +
                "  \"note_two_only_actual\": \"" + ColorText(noteTwoActual) + "\",\n" +
                "  \"note_two_only_expected\": \"" + ColorText(noteTwoExpected) + "\",\n" +
                "  \"note_two_color_distance\": " + noteTwoDistance + ",\n" +
                "  \"overlap_actual\": \"" + ColorText(overlapActual) + "\",\n" +
                "  \"overlap_expected\": \"" + ColorText(overlapExpected) + "\",\n" +
                "  \"overlap_color_distance\": " + overlapDistance + ",\n" +
                "  \"overlap_screenshot\": \"" + escapedScreenshot + "\",\n" +
                "  \"elapsed_ms\": " + timer.ElapsedMilliseconds + ",\n" +
                "  \"failure\": \"" + escapedFailure + "\"\n" +
                "}\n";
            File.WriteAllText(fullOutputPath, json, new UTF8Encoding(false));
        }

        private static StickyNoteData CreateTransparencyProbeNote(string title,
            string text, Color color, int opacity, int width, int height)
        {
            StickyNoteData data = new StickyNoteData();
            data.Title = title;
            data.Text = text;
            data.ColorArgb = color.ToArgb();
            data.TextColorArgb = Color.Black.ToArgb();
            data.BackgroundOpacityPercent = opacity;
            data.Width = width;
            data.Height = height;
            data.AlwaysOnTop = true;
            return data;
        }

        private static Color BlendForExpected(Color foreground, Color background,
            int opacityPercent)
        {
            int alpha = (int)Math.Round(Math.Max(0, Math.Min(100,
                opacityPercent)) * 2.55);
            int inverse = 255 - alpha;
            return Color.FromArgb(255,
                (foreground.R * alpha + background.R * inverse + 127) / 255,
                (foreground.G * alpha + background.G * inverse + 127) / 255,
                (foreground.B * alpha + background.B * inverse + 127) / 255);
        }

        private static int ColorDistance(Color actual, Color expected)
        {
            return Math.Abs(actual.R - expected.R) +
                Math.Abs(actual.G - expected.G) +
                Math.Abs(actual.B - expected.B);
        }

        private static string ColorText(Color value)
        {
            return value.IsEmpty ? "unavailable" : String.Format("#{0:X2}{1:X2}{2:X2}",
                value.R, value.G, value.B);
        }

        private static void PumpUi(int milliseconds)
        {
            Stopwatch timer = Stopwatch.StartNew();
            do
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(1);
            }
            while (timer.ElapsedMilliseconds < milliseconds);
            Application.DoEvents();
        }

        public static void RenderStickyPreview(string outputPath)
        {
            StickyNoteData data = new StickyNoteData();
            data.Title = "明天下午改卷子！";
            data.Text = "明天下午改卷子！\r\n\r\n这行文字用于检查字体、字号和样式。";
            data.Width = 480;
            data.Height = 400;
            using (RichTextBox source = new RichTextBox())
            using (Font body = new Font("Microsoft YaHei UI", 14F))
            {
                source.Text = data.Text;
                source.SelectAll();
                source.SelectionFont = body;
                source.Select(0, 8);
                using (Font heading = new Font("Microsoft YaHei UI", 18F,
                    FontStyle.Bold | FontStyle.Underline))
                    source.SelectionFont = heading;
                data.RichTextRtf = source.Rtf;
            }
            using (StickyNoteWindow note = new StickyNoteWindow(data))
            {
                note.StartPosition = FormStartPosition.Manual;
                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                note.Location = new Point(work.Left + 24, work.Top + 24);
                note.TopMost = true;
                note.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(350);
                Application.DoEvents();
                using (Bitmap canvas = new Bitmap(note.Width + 40,
                    note.Height + 40, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(canvas))
                using (Bitmap noteBitmap = new Bitmap(note.Width, note.Height,
                    PixelFormat.Format32bppArgb))
                {
                    try
                    {
                        using (Graphics screenCapture = Graphics.FromImage(noteBitmap))
                            screenCapture.CopyFromScreen(note.Left, note.Top, 0, 0,
                                note.Size, CopyPixelOperation.SourceCopy);
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        note.DrawToBitmap(noteBitmap,
                            new Rectangle(Point.Empty, note.Size));
                    }
                    graphics.Clear(Color.FromArgb(235, 238, 244));
                    graphics.DrawImageUnscaled(noteBitmap, 20, 20);
                    string parent = Path.GetDirectoryName(
                        Path.GetFullPath(outputPath));
                    if (!String.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    canvas.Save(outputPath, ImageFormat.Png);
                }
                note.Hide();
            }
        }

        public static void RenderSchedulePreview(string outputPath)
        {
            StickyNoteData data = new StickyNoteData();
            data.Title = "日程";
            data.IsSchedule = true;
            data.IsTodoList = false;
            data.FontSizeTwips = 320;
            data.Width = 390;
            data.Height = 430;
            data.ScheduleItems.Add(new StickyScheduleItem("参加画展",
                DateTime.Today.AddDays(6), true));
            data.ScheduleItems.Add(new StickyScheduleItem("五一放假",
                DateTime.Today.AddDays(22)));
            data.ScheduleItems.Add(new StickyScheduleItem("朋友生日",
                DateTime.Today.AddDays(58)));
            data.ScheduleItems.Add(new StickyScheduleItem("国庆节",
                DateTime.Today.AddDays(175)));
            using (StickyNoteWindow note = new StickyNoteWindow(data))
            {
                note.StartPosition = FormStartPosition.Manual;
                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                note.Location = new Point(work.Left + 24, work.Top + 24);
                note.TopMost = true;
                note.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(350);
                Application.DoEvents();
                using (Bitmap canvas = new Bitmap(note.Width + 40,
                    note.Height + 40, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(canvas))
                using (Bitmap noteBitmap = new Bitmap(note.Width, note.Height,
                    PixelFormat.Format32bppArgb))
                {
                    try
                    {
                        using (Graphics screenCapture = Graphics.FromImage(noteBitmap))
                            screenCapture.CopyFromScreen(note.Left, note.Top, 0, 0,
                                note.Size, CopyPixelOperation.SourceCopy);
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        note.DrawToBitmap(noteBitmap,
                            new Rectangle(Point.Empty, note.Size));
                    }
                    graphics.Clear(Color.FromArgb(235, 238, 244));
                    graphics.DrawImageUnscaled(noteBitmap, 20, 20);
                    string parent = Path.GetDirectoryName(
                        Path.GetFullPath(outputPath));
                    if (!String.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    canvas.Save(outputPath, ImageFormat.Png);
                }
                note.Hide();
            }
        }

        public static void RenderStickyAppearancePreview(string outputPath)
        {
            StickyNoteData data = new StickyNoteData();
            data.Title = "颜色与透明度预览";
            data.Text = "这段文字始终保持完全不透明。\r\n可以点击正文继续输入。";
            data.Width = 420;
            data.Height = 300;
            data.ColorArgb = StickyNoteWindow.PaletteColorForTest(24).ToArgb();
            data.BackgroundOpacityPercent = 60;
            data.TextColorArgb = Color.Black.ToArgb();
            data.FontFamilyName = "Noto Sans SC DemiLight";
            data.FontSizeTwips = 240;
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Rectangle stageBounds = new Rectangle(work.Left + 40, work.Top + 40,
                Math.Min(1160, work.Width - 80), Math.Min(520, work.Height - 80));
            using (Form stage = new Form())
            using (StickyNoteWindow note = new StickyNoteWindow(data))
            {
                stage.Text = "Penny 便签外观开发预览背景";
                stage.FormBorderStyle = FormBorderStyle.None;
                stage.StartPosition = FormStartPosition.Manual;
                stage.ShowInTaskbar = false;
                stage.TopMost = true;
                stage.BackColor = Color.FromArgb(245, 245, 240);
                stage.Bounds = stageBounds;
                stage.Show();

                note.StartPosition = FormStartPosition.Manual;
                note.Location = new Point(stage.Left + 28, stage.Top + 95);
                note.TopMost = true;
                note.Show();
                note.OpenAppearanceDialogForTest();
                Application.DoEvents();
                System.Threading.Thread.Sleep(650);
                Application.DoEvents();

                Form appearance = null;
                foreach (Form open in Application.OpenForms)
                {
                    if (open is StickyAppearanceDialog) appearance = open;
                }

                using (Bitmap canvas = new Bitmap(stage.Width, stage.Height,
                    PixelFormat.Format32bppArgb))
                using (Graphics capture = Graphics.FromImage(canvas))
                {
                    try
                    {
                        capture.CopyFromScreen(stage.Left, stage.Top, 0, 0,
                            stage.Size, CopyPixelOperation.SourceCopy);
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        capture.Clear(stage.BackColor);
                        using (Bitmap noteBitmap = new Bitmap(note.Width,
                            note.Height, PixelFormat.Format32bppArgb))
                        {
                            note.DrawToBitmap(noteBitmap,
                                new Rectangle(Point.Empty, note.Size));
                            capture.DrawImageUnscaled(noteBitmap,
                                note.Left - stage.Left, note.Top - stage.Top);
                        }
                        if (appearance != null)
                        {
                            using (Bitmap dialogBitmap = new Bitmap(
                                appearance.Width, appearance.Height,
                                PixelFormat.Format32bppArgb))
                            {
                                appearance.DrawToBitmap(dialogBitmap,
                                    new Rectangle(Point.Empty,
                                        appearance.Size));
                                capture.DrawImageUnscaled(dialogBitmap,
                                    appearance.Left - stage.Left,
                                    appearance.Top - stage.Top);
                            }
                        }
                    }
                    string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                    if (!String.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);
                    canvas.Save(outputPath, ImageFormat.Png);
                }

                if (appearance != null) appearance.Close();
                note.Hide();
                stage.Hide();
            }
        }

        public static void RenderHoverBubblePreview(string outputPath)
        {
            using (SpeechBubbleForm empty = new SpeechBubbleForm("今天想要做些什么呢？", 0))
            using (SpeechBubbleForm countdown = new SpeechBubbleForm(
                "距离最近提醒还有1小时20分钟。\n当前共有 3 条提醒。", 0))
            using (Bitmap preview = new Bitmap(empty.Width + countdown.Width +
                30, Math.Max(empty.Height, countdown.Height) + 20,
                PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(preview))
            using (Bitmap emptyBitmap = new Bitmap(empty.Width, empty.Height,
                PixelFormat.Format32bppArgb))
            using (Bitmap countdownBitmap = new Bitmap(countdown.Width, countdown.Height,
                PixelFormat.Format32bppArgb))
            {
                empty.CreateControl();
                countdown.CreateControl();
                empty.DrawToBitmap(emptyBitmap, empty.ClientRectangle);
                countdown.DrawToBitmap(countdownBitmap, countdown.ClientRectangle);
                emptyBitmap.MakeTransparent(empty.TransparencyKey);
                countdownBitmap.MakeTransparent(countdown.TransparencyKey);
                graphics.Clear(Color.FromArgb(225, 229, 236));
                graphics.DrawImageUnscaled(emptyBitmap, 5, 10);
                graphics.DrawImageUnscaled(countdownBitmap,
                    empty.Width + 20, 10);
                string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                preview.Save(outputPath, ImageFormat.Png);
            }
        }

        public static void RenderReminderPreview(string outputPath)
        {
            using (ReminderDialog dialog = new ReminderDialog(
                "下午三点提交修改后的方案", 18F, true))
            using (Bitmap bitmap = new Bitmap(dialog.Width, dialog.Height,
                PixelFormat.Format32bppArgb))
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-2400, -2400);
                dialog.Show();
                Application.DoEvents();
                dialog.DrawToBitmap(bitmap,
                    new Rectangle(Point.Empty, dialog.Size));
                dialog.Hide();
                string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                bitmap.Save(outputPath, ImageFormat.Png);
            }
        }

        public static void RenderContactAuthorPreview(string outputPath)
        {
            using (ContactAuthorForm dialog = new ContactAuthorForm())
            using (Bitmap bitmap = new Bitmap(dialog.Width, dialog.Height,
                PixelFormat.Format32bppArgb))
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-2400, -2400);
                dialog.Show();
                Application.DoEvents();
                dialog.DrawToBitmap(bitmap,
                    new Rectangle(Point.Empty, dialog.Size));
                dialog.Hide();
                string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                bitmap.Save(outputPath, ImageFormat.Png);
            }
        }

        public static void RenderPreview(string outputPath)
        {
            using (PetArtPackage art = PetArtPackage.Load(192, 208))
            using (Bitmap preview = new Bitmap(960, 208, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(preview))
            {
                graphics.Clear(Color.FromArgb(225, 229, 236));
                graphics.DrawImageUnscaled(art.GetFrame(0, 0), 0, 0);
                graphics.DrawImageUnscaled(art.GetFrame(8, 0), 192, 0);
                graphics.DrawImageUnscaled(art.GetFrame(6, 4), 384, 0);
                graphics.DrawImageUnscaled(art.GetFrame(7, 0), 576, 0);
                graphics.DrawImageUnscaled(art.GetFrame(4, 0), 768, 0);
                string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                preview.Save(outputPath, ImageFormat.Png);
            }
        }

        public static void RunStartupProbe(string outputPath)
        {
            Stopwatch timer = Stopwatch.StartNew();
            int loadedStates;
            int materializedGifFiles;
            int width;
            int height;
            bool startupCacheUsed;
            using (PetArtPackage art = PetArtPackage.Load(192, 208))
            {
                Bitmap firstFrame = art.GetFrame(0, 0);
                width = firstFrame.Width;
                height = firstFrame.Height;
                loadedStates = art.LoadedRuntimeStateCount;
                startupCacheUsed = art.LoadedStartupCache;
                materializedGifFiles = Directory.Exists(art.ArtRoot)
                    ? Directory.GetFiles(art.ArtRoot, "*.gif",
                        SearchOption.AllDirectories).Length : 0;
            }
            timer.Stop();
            string fullOutputPath = Path.GetFullPath(outputPath);
            string parent = Path.GetDirectoryName(fullOutputPath);
            if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            string json = "{\n" +
                "  \"ok\": " + Bool(width == 192 && height == 208 &&
                    loadedStates == 1) + ",\n" +
                "  \"elapsed_milliseconds\": " + timer.ElapsedMilliseconds + ",\n" +
                "  \"loaded_runtime_states\": " + loadedStates + ",\n" +
                "  \"startup_cache_used\": " + Bool(startupCacheUsed) + ",\n" +
                "  \"materialized_gif_files\": " + materializedGifFiles + "\n" +
                "}\n";
            File.WriteAllText(fullOutputPath, json, new UTF8Encoding(false));
        }

        public static void RenderFeaturePreview(string outputPath)
        {
            StickyNoteData yellowData = new StickyNoteData();
            yellowData.Title = "今日计划";
            yellowData.Text = "支持中文输入：整理方案、记录灵感。";
            yellowData.X = 0;
            yellowData.Y = 0;
            yellowData.Width = 320;
            yellowData.Height = 300;
            yellowData.ReminderUtcTicks = DateTime.UtcNow.AddHours(2).Ticks;
            StickyNoteData blueData = new StickyNoteData();
            blueData.Title = "本周待办";
            blueData.IsTodoList = true;
            blueData.TodoItems.Add(new StickyTodoItem("完成便利贴优化", true));
            blueData.TodoItems.Add(new StickyTodoItem("检查提醒倒计时", false));
            blueData.TodoItems.Add(new StickyTodoItem("整理下周计划", false));
            blueData.ColorArgb = Color.FromArgb(255, 211, 239, 255).ToArgb();
            blueData.X = 0;
            blueData.Y = 0;
            blueData.Width = 320;
            blueData.Height = 300;

            List<ReminderItem> previewReminders = new List<ReminderItem>();
            previewReminders.Add(new ReminderItem(DateTime.UtcNow.AddMinutes(18),
                "提交今日方案"));
            previewReminders.Add(new ReminderItem(DateTime.UtcNow.AddHours(2),
                "休息并喝水"));

            using (Bitmap canvas = new Bitmap(1080, 760, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(canvas))
            using (StickyNoteWindow yellow = new StickyNoteWindow(yellowData))
            using (StickyNoteWindow blue = new StickyNoteWindow(blueData))
            using (ScaleDialog scale = new ScaleDialog(100, 100))
            using (Bitmap yellowBitmap = new Bitmap(320, 300, PixelFormat.Format32bppArgb))
            using (Bitmap blueBitmap = new Bitmap(320, 300, PixelFormat.Format32bppArgb))
            using (Bitmap scaleBitmap = new Bitmap(scale.Width, scale.Height,
                PixelFormat.Format32bppArgb))
            using (Bitmap blackKeys = KeyboardOverlayForm.RenderTextPreview(
                "CTRL+W", Color.Black, 255, 60))
            using (Bitmap whiteKeys = KeyboardOverlayForm.RenderTextPreview(
                "W*3", Color.White, 255, 150))
            using (Font heading = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold))
            using (SolidBrush headingBrush = new SolidBrush(Color.FromArgb(45, 51, 60)))
            using (SolidBrush darkBackground = new SolidBrush(Color.FromArgb(35, 39, 48)))
            {
                graphics.Clear(Color.FromArgb(235, 238, 244));
                yellow.UpdateReminderBanner(previewReminders);
                blue.UpdateReminderBanner(previewReminders);
                yellow.StartPosition = FormStartPosition.Manual;
                blue.StartPosition = FormStartPosition.Manual;
                scale.StartPosition = FormStartPosition.Manual;
                yellow.Location = new Point(-2400, -2400);
                blue.Location = new Point(-2400, -2400);
                scale.Location = new Point(-2400, -2400);
                yellow.Show();
                blue.Show();
                scale.Show();
                Application.DoEvents();
                yellow.DrawToBitmap(yellowBitmap, new Rectangle(0, 0, 320, 300));
                blue.DrawToBitmap(blueBitmap, new Rectangle(0, 0, 320, 300));
                scale.DrawToBitmap(scaleBitmap, new Rectangle(Point.Empty, scale.Size));
                yellow.Hide();
                blue.Hide();
                scale.Hide();
                graphics.DrawString("便利贴顶部固定提醒 / 正文与待办清单",
                    heading, headingBrush, new PointF(24, 18));
                graphics.DrawImageUnscaled(yellowBitmap, 24, 52);
                graphics.DrawImageUnscaled(blueBitmap, 366, 52);
                graphics.DrawString("按键显示：小 60% / 大 150%",
                    heading, headingBrush, new PointF(724, 18));
                graphics.FillRectangle(Brushes.White, 724, 52, 330, 110);
                graphics.DrawImageUnscaled(blackKeys,
                    889 - blackKeys.Width / 2, 84);
                graphics.FillRectangle(darkBackground, 724, 174, 330, 110);
                graphics.DrawImageUnscaled(whiteKeys,
                    889 - whiteKeys.Width / 2, 203);
                graphics.DrawString("桌宠缩放与按键文字大小",
                    heading, headingBrush, new PointF(24, 386));
                graphics.DrawImage(scaleBitmap, new Rectangle(24, 420, 500, 290),
                    new Rectangle(0, 0, scaleBitmap.Width, scaleBitmap.Height),
                    GraphicsUnit.Pixel);
                graphics.DrawString("左右侧页签：长按拖拽排序 / 右键删除",
                    heading, headingBrush, new PointF(570, 386));
                List<StickyNoteData> tabNotes = new List<StickyNoteData>();
                string[] tabTitles = { "待办清单", "日程", "便利贴", "待办",
                    "日程", "灵感", "购物清单", "日程", "阅读记录" };
                Color[] tabColors = { Color.FromArgb(255, 239, 156),
                    Color.FromArgb(214, 246, 215), Color.FromArgb(211, 239, 255),
                    Color.FromArgb(230, 226, 239), Color.FromArgb(255, 221, 181),
                    Color.FromArgb(244, 221, 222), Color.FromArgb(255, 239, 156),
                    Color.FromArgb(211, 239, 255), Color.FromArgb(214, 246, 215) };
                for (int i = 0; i < tabTitles.Length; i++)
                {
                    StickyNoteData tabNote = new StickyNoteData();
                    tabNote.Title = tabTitles[i];
                    tabNote.ColorArgb = tabColors[i].ToArgb();
                    tabNote.Visible = false;
                    tabNote.IsTodoList = i % 3 == 0;
                    tabNote.IsSchedule = i % 3 == 1;
                    tabNotes.Add(tabNote);
                }
                Rectangle previewWork = new Rectangle(0, 0, 1920, 1080);
                int previewLeftCount = StickyNoteTabsForm.CalculateLeftCount(
                    tabNotes.Count, 208, previewWork);
                List<StickyNoteData> previewLeft = tabNotes.GetRange(0,
                    previewLeftCount);
                List<StickyNoteData> previewRight = tabNotes.GetRange(
                    previewLeftCount, tabNotes.Count - previewLeftCount);
                using (StickyNoteTabsForm leftTabs = new StickyNoteTabsForm(
                    StickyTabSide.Left, delegate(string noteId) { }))
                using (StickyNoteTabsForm rightTabs = new StickyNoteTabsForm(
                    StickyTabSide.Right, delegate(string noteId) { }))
                using (PetArtPackage petArt = PetArtPackage.Load(192, 208))
                {
                    Bitmap petFrame = petArt.GetFrame(0, 0);
                    leftTabs.Location = new Point(-2600, -2600);
                    rightTabs.Location = new Point(-2600, -2600);
                    leftTabs.SetNotes(previewLeft);
                    rightTabs.SetNotes(previewRight);
                    StickyNoteData crossSideSource = previewLeft.Count >= 3 &&
                        previewRight.Count >= 2 ? previewLeft[1] : null;
                    if (crossSideSource != null)
                    {
                        StickyNoteTabsForm.BeginDragSession(crossSideSource);
                        rightTabs.ShowDropPreviewForTest(crossSideSource, 2);
                    }
                    Application.DoEvents();
                    using (Bitmap leftTabsBitmap = new Bitmap(leftTabs.Width,
                        leftTabs.Height, PixelFormat.Format32bppArgb))
                    using (Bitmap rightTabsBitmap = new Bitmap(rightTabs.Width,
                        rightTabs.Height, PixelFormat.Format32bppArgb))
                    {
                        leftTabs.DrawToBitmap(leftTabsBitmap,
                            new Rectangle(Point.Empty, leftTabs.Size));
                        rightTabs.DrawToBitmap(rightTabsBitmap,
                            new Rectangle(Point.Empty, rightTabs.Size));
                        leftTabsBitmap.MakeTransparent(Color.Fuchsia);
                        rightTabsBitmap.MakeTransparent(Color.Fuchsia);
                        int petX = 760;
                        int petY = 454;
                    graphics.DrawImageUnscaled(leftTabsBitmap,
                            petX - leftTabsBitmap.Width - StickyNoteTabsForm.PetGap,
                            petY + (petFrame.Height - leftTabsBitmap.Height) / 2);
                        graphics.DrawImageUnscaled(petFrame, petX, petY);
                        graphics.DrawImageUnscaled(rightTabsBitmap,
                            petX + petFrame.Width + StickyNoteTabsForm.PetGap,
                            petY + (petFrame.Height - rightTabsBitmap.Height) / 2);
                    }
                    if (crossSideSource != null)
                        StickyNoteTabsForm.EndDragSession(crossSideSource);
                    leftTabs.Hide();
                    rightTabs.Hide();
                }
                using (Font noteFont = new Font("Microsoft YaHei UI", 9.5F))
                {
                    graphics.DrawString(
                        "提醒微调：↑ / 滚轮向上 = 往前，↓ / 滚轮向下 = 往后",
                        noteFont, headingBrush, new PointF(24, 724));
                    graphics.DrawString(
                        "拖拽排序：上下页签会让开并显示蓝色插入槽",
                        noteFont, headingBrush, new PointF(570, 690));
                }
                string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                canvas.Save(outputPath, ImageFormat.Png);
            }
        }


        [ThreadStatic]
        private static List<bool> _reportedChecks;

        private static void BeginCheckCollection()
        {
            _reportedChecks = new List<bool>();
        }

        private static bool EndCheckCollection()
        {
            List<bool> checks = _reportedChecks;
            _reportedChecks = null;
            if (checks == null || checks.Count == 0) return false;
            foreach (bool passed in checks)
                if (!passed) return false;
            return true;
        }

        private static void CancelCheckCollection()
        {
            _reportedChecks = null;
        }

        private static string Bool(bool value)
        {
            if (_reportedChecks != null) _reportedChecks.Add(value);
            return value ? "true" : "false";
        }

        private static bool TouchesTransparency(Bitmap bitmap, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx < 0 || nx >= 192 || ny < 0 || ny >= 208 ||
                        bitmap.GetPixel(nx, ny).A == 0)
                        return true;
                }
            }
            return false;
        }

    }
}
