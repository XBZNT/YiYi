using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace YiYi
{
    /// <summary>语言列表（引擎内部各自做代码映射）。</summary>
    public static class Langs
    {
        static readonly string[] Codes = { "zh-CN", "zh-TW", "en", "ja", "ko", "fr", "de", "ru", "es", "pt", "it", "vi", "th", "ar" };
        static readonly string[] Names = { "简体中文", "繁體中文", "English 英语", "日本語 日语", "한국어 韩语", "Français 法语", "Deutsch 德语", "Русский 俄语", "Español 西班牙语", "Português 葡萄牙语", "Italiano 意大利语", "Tiếng Việt 越南语", "ไทย 泰语", "العربية 阿拉伯语" };

        public static void Fill(ComboBox cb, string selected)
        {
            cb.Items.Clear();
            foreach (string n in Names) cb.Items.Add(n);
            int idx = Array.IndexOf(Codes, string.IsNullOrEmpty(selected) ? "zh-CN" : selected);
            cb.SelectedIndex = idx < 0 ? 0 : idx;
        }

        public static string CodeAt(int index)
        {
            return index >= 0 && index < Codes.Length ? Codes[index] : "zh-CN";
        }
    }

    /// <summary>手动输入文本翻译窗口。</summary>
    public class TextTranslateForm : Form
    {
        TextBox _input;
        TextBox _output;
        ComboBox _to;
        Label _status;
        Button _go;

        public TextTranslateForm()
        {
            Text = "文本翻译 - 易译";
            Font = new Font("Microsoft YaHei UI", 10f);
            ClientSize = new Size(700, 500);
            StartPosition = FormStartPosition.CenterScreen;

            Label lblIn = new Label { Text = "原文（Ctrl+Enter 翻译）", AutoSize = true, Location = new Point(12, 10) };
            _input = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(12, 32),
                Size = new Size(676, 180),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Label lblOut = new Label { Text = "译文", AutoSize = true, Location = new Point(12, 222) };
            _output = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(12, 244),
                Size = new Size(676, 180),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.White
            };
            _to = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(12, 440), Size = new Size(160, 26), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            Langs.Fill(_to, Program.Settings.TargetLang);
            _go = new Button { Text = "翻 译", Location = new Point(580, 436), Size = new Size(108, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            _status = new Label { Text = "", AutoSize = true, Location = new Point(190, 446), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };

            _go.Click += DoTranslate;
            _input.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    _go.PerformClick();
                }
            };
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Hide(); };

            Controls.Add(lblIn);
            Controls.Add(_input);
            Controls.Add(lblOut);
            Controls.Add(_output);
            Controls.Add(_to);
            Controls.Add(_go);
            Controls.Add(_status);
        }

        async void DoTranslate(object sender, EventArgs e)
        {
            string t = _input.Text.Trim();
            if (t.Length == 0) return;
            Program.Settings.TargetLang = Langs.CodeAt(_to.SelectedIndex);
            Program.Settings.Save();
            Program.Client.UpdateSettings(Program.Settings);
            _go.Enabled = false;
            _status.Text = "翻译中…";
            try
            {
                _output.Text = await Program.Client.TranslateAsync(t);
                _status.Text = "完成";
            }
            catch (Exception ex)
            {
                _status.Text = "失败";
                MessageBox.Show(this, ex.Message, "翻译失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { _go.Enabled = true; }
        }
    }
}
