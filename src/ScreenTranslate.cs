using System;
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

    /// <summary>翻译结果窗口（置顶，方便对着屏幕看）。</summary>
    public class ResultForm : Form
    {
        TextBox _src;
        TextBox _dst;

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
            _src = new TextBox
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
            Controls.Add(_src);
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
}
