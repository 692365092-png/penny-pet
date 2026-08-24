using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed class ContactAuthorForm : Form
    {
        private const string ArtworkResourceName =
            "PennyPet.ContactAuthor.Image";
        internal const string XiaohongshuNumber = "638176366";
        internal const string XiaohongshuProfileUrl =
            "https://www.xiaohongshu.com/user/profile/59bd4b0b51783a7612f6fc43";

        private readonly PictureBox _xiaohongshuLogo;
        private readonly CopyOnlyTextBox _xiaohongshuNumber;
        private Point _linkMouseDown;
        private bool _linkMouseDragged;

        internal ContactAuthorForm()
        {
            Text = String.Empty;
            ClientSize = new Size(330, 190);
            BackColor = Color.FromArgb(245, 245, 245);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };

            Label title = new Label();
            title.Text = "联系作者";
            title.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
            title.TextAlign = ContentAlignment.MiddleCenter;
            title.SetBounds(0, 14, ClientSize.Width, 31);
            title.TabStop = false;
            Controls.Add(title);

            _xiaohongshuLogo = ArtworkBox(new Rectangle(0, 0, 104, 53));
            _xiaohongshuLogo.SetBounds((ClientSize.Width - 104) / 2,
                51, 104, 53);
            Controls.Add(_xiaohongshuLogo);

            _xiaohongshuNumber = NumberBox(XiaohongshuNumber);
            _xiaohongshuNumber.SetBounds((ClientSize.Width - 140) / 2,
                113, 140, 34);
            _xiaohongshuNumber.MouseDown += XiaohongshuMouseDown;
            _xiaohongshuNumber.MouseMove += XiaohongshuMouseMove;
            _xiaohongshuNumber.MouseUp += XiaohongshuMouseUp;
            _xiaohongshuNumber.KeyDown += XiaohongshuKeyDown;
            Controls.Add(_xiaohongshuNumber);
        }

        private static PictureBox ArtworkBox(Rectangle sourceRectangle)
        {
            PictureBox box = new PictureBox();
            box.BackColor = Color.White;
            box.SizeMode = PictureBoxSizeMode.Zoom;
            box.TabStop = false;
            box.AllowDrop = false;
            box.Cursor = Cursors.Default;
            using (Stream stream = typeof(ContactAuthorForm).Assembly
                .GetManifestResourceStream(ArtworkResourceName))
            {
                if (stream == null)
                    throw new FileNotFoundException(
                        "联系作者图片没有嵌入 Penny pet。", ArtworkResourceName);
                using (Bitmap artwork = new Bitmap(stream))
                {
                    Rectangle safe = Rectangle.Intersect(sourceRectangle,
                        new Rectangle(Point.Empty, artwork.Size));
                    if (safe.Width != sourceRectangle.Width ||
                        safe.Height != sourceRectangle.Height)
                        throw new InvalidDataException("联系作者图片尺寸不正确。");
                    box.Image = artwork.Clone(safe, PixelFormat.Format32bppArgb);
                }
            }
            return box;
        }

        private CopyOnlyTextBox NumberBox(string text)
        {
            CopyOnlyTextBox box = new CopyOnlyTextBox();
            box.Text = text;
            box.Font = new Font("Microsoft YaHei UI", 16F,
                FontStyle.Bold | FontStyle.Underline);
            box.TextAlign = HorizontalAlignment.Center;
            box.BackColor = BackColor;
            box.ForeColor = Color.Black;
            box.Cursor = Cursors.Hand;
            box.AccessibleName = "小红书号";
            return box;
        }

        private void XiaohongshuMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _linkMouseDown = e.Location;
            _linkMouseDragged = false;
        }

        private void XiaohongshuMouseMove(object sender, MouseEventArgs e)
        {
            if ((e.Button & MouseButtons.Left) == 0) return;
            int dx = e.X - _linkMouseDown.X;
            int dy = e.Y - _linkMouseDown.Y;
            if (dx * dx + dy * dy >= 16) _linkMouseDragged = true;
        }

        private void XiaohongshuMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !_linkMouseDragged)
                OpenXiaohongshuProfile();
        }

        private void XiaohongshuKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            OpenXiaohongshuProfile();
        }

        private void OpenXiaohongshuProfile()
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(
                    XiaohongshuProfileUrl);
                start.UseShellExecute = true;
                Process.Start(start);
            }
            catch (Exception error)
            {
                MessageBox.Show(this,
                    "暂时无法打开默认浏览器。\n" + (error.Message ?? String.Empty),
                    "Penny pet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        internal bool CopyAndArtworkBehaviorConfigured
        {
            get
            {
                return _xiaohongshuNumber.ReadOnly &&
                    _xiaohongshuNumber.ShortcutsEnabled &&
                    _xiaohongshuLogo.TabStop == false &&
                    _xiaohongshuLogo.ContextMenuStrip == null;
            }
        }

        internal string DisplayedXiaohongshuNumber
        {
            get { return _xiaohongshuNumber.Text; }
        }

        internal bool XiaohongshuOnlyLayoutForTest
        {
            get
            {
                return ClientSize.Width == 330 && ClientSize.Height == 190 &&
                    _xiaohongshuLogo.Left + _xiaohongshuLogo.Width / 2 ==
                        ClientSize.Width / 2 &&
                    _xiaohongshuNumber.Left + _xiaohongshuNumber.Width / 2 ==
                        ClientSize.Width / 2;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _xiaohongshuNumber.SelectionStart = 0;
            _xiaohongshuNumber.SelectionLength = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_xiaohongshuLogo != null && _xiaohongshuLogo.Image != null)
                {
                    _xiaohongshuLogo.Image.Dispose();
                    _xiaohongshuLogo.Image = null;
                }
            }
            base.Dispose(disposing);
        }

        private sealed class CopyOnlyTextBox : TextBox
        {
            internal CopyOnlyTextBox()
            {
                ReadOnly = true;
                BorderStyle = BorderStyle.None;
                ShortcutsEnabled = true;
                HideSelection = false;
                AutoSize = false;
                TabStop = true;
            }

            protected override void WndProc(ref Message message)
            {
                const int WmCut = 0x0300;
                const int WmPaste = 0x0302;
                const int WmClear = 0x0303;
                if (message.Msg == WmCut || message.Msg == WmPaste ||
                    message.Msg == WmClear) return;
                base.WndProc(ref message);
            }
        }
    }
}
