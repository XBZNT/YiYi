using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace YiYi
{
    /// <summary>文档翻译窗口：选文件（或拖拽进来）→ 开始翻译 → 看进度日志。</summary>
    public class DocTranslateForm : Form
    {
        TextBox _path;
        TextBox _log;
        ComboBox _to;
        Button _go;
        Button _openBtn;
        Label _status;
        string _lastOut;

        public DocTranslateForm()
        {
            Text = "文档翻译 - 易译（支持 txt / md / log / srt / docx / pdf）";
            Font = new Font("Microsoft YaHei UI", 9.5f);
            ClientSize = new Size(720, 530);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;

            Label lbl = new Label { Text = "文件（可直接拖拽到此窗口）：", AutoSize = true, Location = new Point(12, 14) };
            _path = new TextBox { Location = new Point(12, 38), Size = new Size(596, 24), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            Button browse = new Button { Text = "浏览…", Location = new Point(618, 36), Size = new Size(88, 26), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            browse.Click += delegate
            {
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Filter = "支持的文档|*.txt;*.md;*.markdown;*.log;*.srt;*.docx;*.pdf|所有文件|*.*";
                    if (dlg.ShowDialog(this) == DialogResult.OK) _path.Text = dlg.FileName;
                }
            };

            Label lblTo = new Label { Text = "目标语言：", AutoSize = true, Location = new Point(12, 76) };
            _to = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(88, 72), Size = new Size(160, 26) };
            Langs.Fill(_to, Program.Settings.TargetLang);

            _go = new Button { Text = "开始翻译", Location = new Point(618, 70), Size = new Size(88, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            _go.Click += OnGo;

            _log = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(12, 116),
                Size = new Size(694, 350),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.White,
                Font = new Font("Consolas", 9f),
                WordWrap = false
            };
            _status = new Label { Text = "", AutoSize = true, Location = new Point(12, 480), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            _openBtn = new Button { Text = "打开输出文件夹", Location = new Point(596, 476), Size = new Size(110, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Enabled = false };
            _openBtn.Click += delegate
            {
                try
                {
                    if (!string.IsNullOrEmpty(_lastOut))
                        Process.Start("explorer.exe", "/select,\"" + _lastOut + "\"");
                }
                catch { }
            };

            DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0) _path.Text = files[0];
            };

            Controls.Add(lbl);
            Controls.Add(_path);
            Controls.Add(browse);
            Controls.Add(lblTo);
            Controls.Add(_to);
            Controls.Add(_go);
            Controls.Add(_log);
            Controls.Add(_status);
            Controls.Add(_openBtn);
        }

        async void OnGo(object sender, EventArgs e)
        {
            string file = _path.Text.Trim().Trim('"');
            if (file.Length == 0 || !File.Exists(file))
            {
                MessageBox.Show(this, "请先选择一个存在的文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Program.Settings.TargetLang = Langs.CodeAt(_to.SelectedIndex);
            Program.Settings.Save();
            Program.Client.UpdateSettings(Program.Settings);

            _go.Enabled = false;
            _log.Clear();
            _status.Text = "翻译中…（期间可继续用屏幕翻译）";
            try
            {
                string outPath = await DocTranslator.TranslateFile(file, Program.Client, Program.Settings,
                    delegate(string m) { AppendLog(m); });
                AppendLog("完成！输出文件: " + outPath);
                _status.Text = "完成";
                _lastOut = outPath;
                _openBtn.Enabled = true;
            }
            catch (Exception ex)
            {
                AppendLog("出错: " + ex.Message);
                _status.Text = "失败";
                Program.LogError(ex);
            }
            finally { _go.Enabled = true; }
        }

        void AppendLog(string m)
        {
            _log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + m + Environment.NewLine);
        }
    }
}
