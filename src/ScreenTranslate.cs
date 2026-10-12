using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace YiYi
{
    /// <summary>全屏幕半透明遮罩，拖拽框选要翻译的区域。</summary>
    public class OverlayForm : Form
    {
        Point _start;
        bool _dragging;
        Rectangle _rect;

        public Rectangle ResultRect;
        public bool Confirmed;

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            Bounds = new Rectangle(SystemInformation.VirtualScreen.Location, SystemInformation.VirtualScreen.Size);
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            Opacity = 0.30;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;

            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
            };
            MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { _dragging = true; _start = e.Location; }
            };
            MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (_dragging) { _rect = RectFrom(_start, e.Location); Invalidate(); }
            };
            MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (_dragging && e.Button == MouseButtons.Left)
                {
                    _dragging = false;
                    Rectangle r = RectFrom(_start, e.Location);
                    if (r.Width >= 8 && r.Height >= 8)
                    {
                        ResultRect = r;
                        Confirmed = true;
                        DialogResult = DialogResult.OK;
                        Close();
                    }
                    else { _rect = Rectangle.Empty; Invalidate(); }
                }
            };
            Paint += delegate(object s, PaintEventArgs e)
            {
                using (Font f = new Font("Microsoft YaHei UI", 11f))
                    e.Graphics.DrawString("拖拽框选要翻译的区域，按 Esc 取消", f, Brushes.White, 16, 14);
                if (_dragging && !_rect.IsEmpty)
                {
                    using (Pen pen = new Pen(Color.Lime, 1)) { pen.DashStyle = DashStyle.Dash; e.Graphics.DrawRectangle(pen, _rect); }
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(70, Color.Lime))) e.Graphics.FillRectangle(brush, _rect);
                }
            };
        }

        static Rectangle RectFrom(Point a, Point b)
        {
            return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW，不出现在 Alt+Tab
                return cp;
            }
        }
    }

    /// <summary>
    /// 把每行译文画回截图原位置：半透明底色块盖住原文，上面写译文。
    /// 行框坐标是归一化的（0~1），乘回图片尺寸即可。
    /// </summary>
    public static class Annotator
    {
        public static Bitmap Draw(Bitmap original, List<OcrLineInfo> lines, List<string> translations)
        {
            Bitmap dst = new Bitmap(original.Width, original.Height);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                    new Rectangle(0, 0, original.Width, original.Height), GraphicsUnit.Pixel);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                for (int i = 0; i < lines.Count && i < translations.Count; i++)
                {
                    string tr = translations[i];
                    if (string.IsNullOrEmpty(tr) || tr == lines[i].Text) continue;
                    RectangleF rf = lines[i].Rect;
                    Rectangle px = new Rectangle(
                        (int)Math.Floor(rf.X * original.Width) - 2,
                        (int)Math.Floor(rf.Y * original.Height) - 2,
                        (int)Math.Ceiling(rf.Width * original.Width) + 4,
                        (int)Math.Ceiling(rf.Height * original.Height) + 4);
                    if (px.Width < 8) px.Width = 8;
                    if (px.Height < 8) px.Height = 8;
                    px.Intersect(new Rectangle(0, 0, original.Width, original.Height));
                    if (px.Width <= 0 || px.Height <= 0) continue;

                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(214, 255, 250, 205)))
                        g.FillRectangle(bg, px);
                    using (Pen pen = new Pen(Color.FromArgb(170, 230, 180, 60), 1f))
                        g.DrawRectangle(pen, px.X, px.Y, px.Width - 1, px.Height - 1);
                    DrawFitText(g, tr, px);
                }
            }
            return dst;
        }

        static void DrawFitText(Graphics g, string text, Rectangle box)
        {
            // 字号从行高的 0.72 起步往下试，放不下就用省略号
            float size = Math.Max(8f, box.Height * 0.72f);
            while (size > 8f)
            {
                using (Font f = new Font("Microsoft YaHei UI", size, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    Size measured = TextRenderer.MeasureText(text, f);
                    if (measured.Width <= box.Width - 4 && measured.Height <= box.Height + 1)
                    {
                        TextRenderer.DrawText(g, text, f, box, Color.FromArgb(40, 40, 40),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                            | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                        return;
                    }
                }
                size -= 1f;
            }
            using (Font f = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                TextRenderer.DrawText(g, text, f, box, Color.FromArgb(40, 40, 40),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>简单文本结果窗口（剪贴板翻译用）：原文/译文两个文本框。</summary>
    public class ResultForm : Form
    {
        readonly TextBox _dst;

        public ResultForm(string original, string translation)
        {
            Text = "翻译结果 - 易译";
            TopMost = true;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 10f);
            ClientSize = new Size(560, 400);
            StartPosition = FormStartPosition.Manual;

            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int x = Cursor.Position.X + 14;
            int y = Cursor.Position.Y + 14;
            if (x + Width > wa.Right) x = wa.Right - Width;
            if (y + Height > wa.Bottom) y = wa.Bottom - Height;
            Location = new Point(Math.Max(wa.Left, x), Math.Max(wa.Top, y));

            Label lbl1 = new Label { Text = "原文", AutoSize = true, Location = new Point(12, 10) };
            TextBox src = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(12, 32),
                Size = new Size(536, 100),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.White,
                Text = original
            };
            Label lbl2 = new Label { Text = "译文", AutoSize = true, Location = new Point(12, 140) };
            _dst = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(12, 162),
                Size = new Size(536, 190),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.White,
                Text = translation
            };
            Button btnCopy = new Button { Text = "复制译文", Location = new Point(322, 362), Size = new Size(100, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            btnCopy.Click += delegate { try { Clipboard.SetText(_dst.Text); } catch { } };
            Button btnClose = new Button { Text = "关闭", Location = new Point(448, 362), Size = new Size(100, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            btnClose.Click += delegate { Close(); };

            Controls.Add(lbl1);
            Controls.Add(src);
            Controls.Add(lbl2);
            Controls.Add(_dst);
            Controls.Add(btnCopy);
            Controls.Add(btnClose);

            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }

        public void SetTranslation(string t)
        {
            _dst.Text = t;
            _dst.SelectionStart = 0;
        }
    }

    /// <summary>标注式结果窗口：上方是贴好译文的截图，下方为完整译文文本框。</summary>
    public class AnnotatedResultForm : Form
    {
        readonly PictureBox _pic;
        readonly TextBox _dst;
        Bitmap _annotated;

        public AnnotatedResultForm(Bitmap annotated, string fullTranslation)
        {
            Text = "屏幕翻译（图上为译文）- 易译";
            TopMost = true;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9.5f);
            ClientSize = new Size(660, 640);
            StartPosition = FormStartPosition.Manual;

            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int x = Cursor.Position.X + 14;
            int y = Cursor.Position.Y + 14;
            if (x + Width > wa.Right) x = wa.Right - Width;
            if (y + Height > wa.Bottom) y = wa.Bottom - Height;
            Location = new Point(Math.Max(wa.Left, x), Math.Max(wa.Top, y));

            Panel picPanel = new Panel
            {
                Location = new Point(12, 10),
                Size = new Size(616, 400),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.DimGray,
                AutoScroll = true
            };
            _pic = new PictureBox
            {
                Image = annotated,
                SizeMode = PictureBoxSizeMode.Zoom,
                Dock = DockStyle.Fill,
                BackColor = Color.DimGray
            };
            picPanel.Controls.Add(_pic);

            Label lblDst = new Label { Text = "完整译文（可复制，顺序与图中标注一致）：", AutoSize = true, Location = new Point(12, 418), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            _dst = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(12, 440),
                Size = new Size(616, 140),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.White,
                Text = fullTranslation
            };
            Button btnCopy = new Button { Text = "复制译文", Location = new Point(402, 580), Size = new Size(100, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            btnCopy.Click += delegate { try { Clipboard.SetText(_dst.Text); } catch { } };
            Button btnClose = new Button { Text = "关闭", Location = new Point(528, 580), Size = new Size(100, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            btnClose.Click += delegate { Close(); };

            Controls.Add(picPanel);
            Controls.Add(lblDst);
            Controls.Add(_dst);
            Controls.Add(btnCopy);
            Controls.Add(btnClose);

            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }
    }
}
