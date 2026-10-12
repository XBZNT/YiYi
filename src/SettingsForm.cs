using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace YiYi
{
    /// <summary>
    /// 设置窗口：目标语言 / 翻译引擎 / OCR 语言 / 快捷键 / 可选密钥。
    /// 快捷键：点击按钮后直接按下新组合键（需含 Ctrl 或 Alt），保存后立即生效。
    /// </summary>
    public class SettingsForm : Form
    {
        static readonly string[] HkNames = { "屏幕翻译", "翻译剪贴板", "文本翻译", "文档翻译" };
        static readonly string[] HkDefaults =
        {
            HotkeyParser.DefaultScreen, HotkeyParser.DefaultClipboard,
            HotkeyParser.DefaultText, HotkeyParser.DefaultDoc
        };

        readonly Func<uint, uint, bool> _probeHotkey;
        readonly Action _onSaved;

        ComboBox _to;
        CheckedListBox _engines;
        ComboBox _ocr;
        List<string> _ocrTags;
        NumericUpDown _delay;
        TextBox _baiduId, _baiduSecret, _deeplKey;

        Button[] _hkBtns;
        string[] _hkVal = new string[4];
        int _capturing = -1;
        Label _hint;

        public SettingsForm(Func<uint, uint, bool> probeHotkey, Action onSaved)
        {
            _probeHotkey = probeHotkey;
            _onSaved = onSaved;

            Text = "设置 - 易译";
            Font = new Font("Microsoft YaHei UI", 10f);
            ClientSize = new Size(540, 648);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            AppSettings s = Program.Settings;

            Label l1 = new Label { Text = "目标语言：", AutoSize = true, Location = new Point(14, 16) };
            _to = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(110, 12), Width = 200 };
            Langs.Fill(_to, s.TargetLang);

            Label l2 = new Label { Text = "翻译引擎（按列表顺序尝试，勾选启用）：", AutoSize = true, Location = new Point(14, 50) };
            _engines = new CheckedListBox { CheckOnClick = true, Location = new Point(14, 72), Size = new Size(512, 108) };
            string[] items =
            {
                "bing|必应翻译（免费，国内直连，推荐）",
                "qq|腾讯交互翻译（免费）",
                "youdao|有道（免费）",
                "google|谷歌翻译（需要能访问 Google）",
                "mymemory|MyMemory（免费，备用）",
                "baidu|百度翻译（需自己申请密钥）",
                "deepl|DeepL（需自己申请密钥）"
            };
            foreach (string it in items) _engines.Items.Add(it, false);
            foreach (string name in s.EngineOrder)
                for (int i = 0; i < items.Length; i++)
                    if (items[i].StartsWith(name + "|")) _engines.SetItemChecked(i, true);

            Label l3 = new Label { Text = "屏幕 OCR 识别语言：", AutoSize = true, Location = new Point(14, 196) };
            _ocr = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(14, 218), Width = 240 };
            _ocrTags = new List<string>();
            _ocr.Items.Add("auto（自动识别中/英）");
            _ocrTags.Add("auto");
            _ocr.Items.Add("en-US（英语）");
            _ocrTags.Add("en-US");
            _ocr.Items.Add("zh-CN（中文）");
            _ocrTags.Add("zh-CN");
            foreach (string tag in OcrHelper.AvailableLanguages())
            {
                if (tag != "en-US" && tag != "zh-CN")
                {
                    _ocr.Items.Add(tag);
                    _ocrTags.Add(tag);
                }
            }
            int oi = _ocrTags.IndexOf(string.IsNullOrEmpty(s.ScreenOcrLang) ? "auto" : s.ScreenOcrLang);
            _ocr.SelectedIndex = oi >= 0 ? oi : 0;

            Label l4 = new Label { Text = "请求间隔(毫秒)：", AutoSize = true, Location = new Point(300, 196) };
            _delay = new NumericUpDown { Location = new Point(300, 218), Width = 90, Minimum = 0, Maximum = 2000, Increment = 50, Value = s.RequestDelayMs };

            // ---------- 快捷键 ----------
            Label lHk = new Label { Text = "快捷键（点击按钮后按下新组合键，保存后生效）：", AutoSize = true, Location = new Point(14, 258) };
            _hkBtns = new Button[4];
            string[] current =
            {
                HotkeyParser.Display(s.HotkeyScreen), HotkeyParser.Display(s.HotkeyClipboard),
                HotkeyParser.Display(s.HotkeyText), HotkeyParser.Display(s.HotkeyDoc)
            };
            for (int i = 0; i < 4; i++)
            {
                _hkVal[i] = current[i];
                Label name = new Label { Text = HkNames[i] + "：", AutoSize = true, Location = new Point(14, 292 + i * 30) };
                Button btn = new Button
                {
                    Text = current[i],
                    Location = new Point(110, 287 + i * 30),
                    Size = new Size(170, 26),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                btn.Click += OnHkButtonClicked;
                _hkBtns[i] = btn;
                Controls.Add(name);
                Controls.Add(btn);
            }
            Button reset = new Button { Text = "恢复默认", Location = new Point(296, 287), Size = new Size(90, 26) };
            reset.Click += delegate
            {
                for (int i = 0; i < 4; i++)
                {
                    _hkVal[i] = HkDefaults[i];
                    _hkBtns[i].Text = HkDefaults[i];
                }
                _hint.Text = "已恢复默认快捷键（保存后生效）";
            };
            _hint = new Label
            {
                Text = "组合键需包含 Ctrl 或 Alt，按键支持字母 / 数字 / F1-F12；Esc 取消。",
                AutoSize = true,
                Location = new Point(14, 412),
                ForeColor = Color.DimGray,
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };

            // ---------- 可选密钥 ----------
            Label l5 = new Label { Text = "可选 API 密钥（不填则只用免费接口）：", AutoSize = true, Location = new Point(14, 444) };
            Label lb1 = new Label { Text = "百度 AppID：", AutoSize = true, Location = new Point(14, 474) };
            _baiduId = new TextBox { Location = new Point(110, 470), Width = 240, Text = s.BaiduAppId };
            Label lb2 = new Label { Text = "百度密钥：", AutoSize = true, Location = new Point(14, 504) };
            _baiduSecret = new TextBox { Location = new Point(110, 500), Width = 240, Text = s.BaiduSecret };
            Label lb3 = new Label { Text = "DeepL Key：", AutoSize = true, Location = new Point(14, 534) };
            _deeplKey = new TextBox { Location = new Point(110, 530), Width = 240, Text = s.DeepLKey };

            Label l6 = new Label
            {
                Text = "输出文件生成在原文件旁边，名字加 .translated，不改动原文件。",
                AutoSize = true,
                Location = new Point(14, 570),
                ForeColor = Color.DimGray,
                Font = new Font("Microsoft YaHei UI", 8.5f)
            };

            Button ok = new Button { Text = "保存", Location = new Point(310, 606), Size = new Size(100, 30) };
            Button cancel = new Button { Text = "取消", Location = new Point(426, 606), Size = new Size(100, 30) };
            ok.Click += delegate { Save(); Close(); };
            cancel.Click += delegate { Close(); };

            Controls.Add(l1); Controls.Add(_to); Controls.Add(l2); Controls.Add(_engines);
            Controls.Add(l3); Controls.Add(_ocr); Controls.Add(l4); Controls.Add(_delay);
            Controls.Add(lHk); Controls.Add(reset); Controls.Add(_hint);
            Controls.Add(l5); Controls.Add(lb1); Controls.Add(_baiduId);
            Controls.Add(lb2); Controls.Add(_baiduSecret);
            Controls.Add(lb3); Controls.Add(_deeplKey);
            Controls.Add(l6);
            Controls.Add(ok); Controls.Add(cancel);

            KeyDown += OnFormKeyDown;
        }

        void OnHkButtonClicked(object sender, EventArgs e)
        {
            for (int i = 0; i < 4; i++)
            {
                if (sender == _hkBtns[i])
                {
                    if (_capturing >= 0) EndCapture();
                    _capturing = i;
                    _hkBtns[i].Text = "请按下新组合键…";
                    _hint.Text = "按下组合键（含 Ctrl 或 Alt）；Esc 取消";
                    _hint.ForeColor = Color.Chocolate;
                    return;
                }
            }
        }

        void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (_capturing < 0)
            {
                if (e.KeyCode == Keys.Escape) Close();
                return;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;

            if (e.KeyCode == Keys.Escape)
            {
                EndCapture();
                _hint.Text = "已取消。";
                _hint.ForeColor = Color.DimGray;
                return;
            }
            Keys kc = e.KeyCode;
            if (kc == Keys.ControlKey || kc == Keys.Menu || kc == Keys.ShiftKey
                || kc == Keys.LControlKey || kc == Keys.RControlKey
                || kc == Keys.LMenu || kc == Keys.RMenu
                || kc == Keys.LShiftKey || kc == Keys.RShiftKey)
                return; // 只按了修饰键，继续等主键

            bool validKey = (kc >= Keys.A && kc <= Keys.Z) || (kc >= Keys.D0 && kc <= Keys.D9) || (kc >= Keys.F1 && kc <= Keys.F12);
            if (!validKey)
            {
                _hint.Text = "无效按键：只支持字母 / 数字 / F1-F12。";
                return;
            }
            uint mod = (e.Control ? Native.MOD_CONTROL : 0u)
                     | (e.Alt ? Native.MOD_ALT : 0u)
                     | (e.Shift ? Native.MOD_SHIFT : 0u);
            if ((mod & (Native.MOD_CONTROL | Native.MOD_ALT)) == 0)
            {
                _hint.Text = "组合键必须包含 Ctrl 或 Alt（避免单个按键被全局占用）。";
                return;
            }
            string combo = HotkeyParser.Format(mod, (uint)kc);
            for (int i = 0; i < 4; i++)
            {
                if (i != _capturing && _hkVal[i] == combo)
                {
                    _hint.Text = "与「" + HkNames[i] + "」重复了。";
                    return;
                }
            }
            if (_probeHotkey != null && !_probeHotkey(mod, (uint)kc))
            {
                _hint.Text = "组合键 " + combo + " 已被其他程序占用。";
                return;
            }
            _hkVal[_capturing] = combo;
            EndCapture();
            _hint.Text = "已设置 " + combo + "（点“保存”后生效）。";
            _hint.ForeColor = Color.Green;
        }

        void EndCapture()
        {
            if (_capturing >= 0)
            {
                _hkBtns[_capturing].Text = _hkVal[_capturing];
                _capturing = -1;
            }
        }

        void Save()
        {
            EndCapture();
            AppSettings s = Program.Settings;
            s.TargetLang = Langs.CodeAt(_to.SelectedIndex);
            List<string> order = new List<string>();
            foreach (object it in _engines.CheckedItems)
            {
                string str = it.ToString();
                int p = str.IndexOf('|');
                order.Add(p > 0 ? str.Substring(0, p) : str);
            }
            if (order.Count > 0) s.EngineOrder = order.ToArray();
            if (_ocr.SelectedIndex >= 0 && _ocr.SelectedIndex < _ocrTags.Count)
                s.ScreenOcrLang = _ocrTags[_ocr.SelectedIndex];
            s.RequestDelayMs = (int)_delay.Value;
            s.BaiduAppId = _baiduId.Text.Trim();
            s.BaiduSecret = _baiduSecret.Text.Trim();
            s.DeepLKey = _deeplKey.Text.Trim();
            s.HotkeyScreen = _hkVal[0];
            s.HotkeyClipboard = _hkVal[1];
            s.HotkeyText = _hkVal[2];
            s.HotkeyDoc = _hkVal[3];
            s.Save();
            Program.Client.UpdateSettings(s);
            if (_onSaved != null) _onSaved();
        }
    }
}
