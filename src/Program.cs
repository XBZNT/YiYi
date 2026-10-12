using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace YiYi
{
    static class Program
    {
        public static AppSettings Settings;
        public static EngineClient Client;

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Native.SetProcessDPIAware(); } catch { }
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch { }
            Settings = AppSettings.Load();
            Client = new EngineClient(Settings);

            if (args != null && args.Length > 0) { RunCli(args); return; }

            bool created;
            Mutex mutex = new Mutex(true, "YiYiTranslator_SingleInstance", out created);
            if (!created)
            {
                MessageBox.Show("易译已经在运行了（请看任务栏右下角托盘图标）。", "易译");
                return;
            }
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { LogError(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                LogError(e.ExceptionObject as Exception);
            };
            Application.Run(new AppContext());
            GC.KeepAlive(mutex);
        }

        // ---------- 命令行模式（脚本/自测用） ----------

        static void RunCli(string[] args)
        {
            Native.TryAttachConsole();
            try
            {
                if (args[0] == "--selftest") { SelfTest.RunAll(); return; }
                if (args[0] == "--translate-doc" && args.Length >= 2)
                {
                    string file = args[1];
                    for (int i = 2; i < args.Length - 1; i++)
                        if (args[i] == "--to") Settings.TargetLang = args[i + 1];
                    Client.UpdateSettings(Settings);
                    string outPath = DocTranslator.TranslateFile(file, Client, Settings,
                        delegate(string m) { Console.WriteLine(m); }).Result;
                    Console.WriteLine("完成！输出文件: " + outPath);
                    return;
                }
                Console.WriteLine("用法:");
                Console.WriteLine("  YiYiTranslator.exe --selftest");
                Console.WriteLine("  YiYiTranslator.exe --translate-doc <文件路径> [--to zh-CN]");
            }
            catch (Exception ex)
            {
                AggregateException ag = ex as AggregateException;
                Console.WriteLine("错误: " + (ag != null ? ag.GetBaseException().Message : ex.Message));
                Environment.ExitCode = 1;
            }
        }

        public static void LogError(Exception ex)
        {
            if (ex == null) return;
            try
            {
                File.AppendAllText(Path.Combine(AppSettings.Dir(), "error.log"),
                    DateTime.Now + "  " + ex + "\r\n\r\n");
            }
            catch { }
        }
    }

    /// <summary>托盘常驻应用上下文：热键 + 菜单 + 各功能入口。</summary>
    public class AppContext : ApplicationContext
    {
        NotifyIcon _tray;
        HotkeyWindow _hk;
        TextTranslateForm _textForm;
        DocTranslateForm _docForm;
        SettingsForm _settingsForm;
        ToolStripItem _miScreen, _miClip, _miText, _miDoc;
        DateTime _lastHotkeyAt = DateTime.MinValue;
        bool _screenBusy;
        bool _clipBusy;

        public AppContext()
        {
            _hk = new HotkeyWindow();
            _hk.HotkeyPressed += OnHotkey;

            _tray = new NotifyIcon();
            _tray.Icon = MakeIcon();
            _tray.Text = "易译 - 文档与屏幕翻译";
            _tray.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            _miScreen = menu.Items.Add("屏幕翻译", null, (EventHandler)delegate { ScreenTranslate(); });
            _miClip = menu.Items.Add("翻译剪贴板", null, (EventHandler)delegate { ClipboardTranslate(); });
            _miText = menu.Items.Add("文本翻译", null, (EventHandler)delegate { ShowText(); });
            _miDoc = menu.Items.Add("文档翻译", null, (EventHandler)delegate { ShowDoc(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("设置...", null, (EventHandler)delegate { ShowSettings(); });
            ToolStripMenuItem autoItem = new ToolStripMenuItem("开机自动启动");
            autoItem.Checked = IsAutoStart();
            autoItem.Click += delegate
            {
                SetAutoStart(!IsAutoStart());
                autoItem.Checked = IsAutoStart();
            };
            menu.Items.Add(autoItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (EventHandler)delegate { ExitApp(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowText(); };

            ApplyHotkeys();
            Balloon("易译已启动",
                "屏幕翻译 " + HotkeyParser.Display(Program.Settings.HotkeyScreen)
                + " | 剪贴板 " + HotkeyParser.Display(Program.Settings.HotkeyClipboard)
                + "\n可在托盘右键 → 设置 中修改快捷键", ToolTipIcon.Info);
        }

        /// <summary>按当前设置（重新）注册全部全局热键。</summary>
        public void ApplyHotkeys()
        {
            _hk.UnregisterAll();
            List<string> fails = new List<string>();
            RegisterOne(1, Program.Settings.HotkeyScreen, "屏幕翻译", fails);
            RegisterOne(2, Program.Settings.HotkeyClipboard, "翻译剪贴板", fails);
            RegisterOne(3, Program.Settings.HotkeyText, "文本翻译", fails);
            RegisterOne(4, Program.Settings.HotkeyDoc, "文档翻译", fails);
            if (_miScreen != null)
            {
                _miScreen.Text = "屏幕翻译 (" + HotkeyParser.Display(Program.Settings.HotkeyScreen) + ")";
                _miClip.Text = "翻译剪贴板 (" + HotkeyParser.Display(Program.Settings.HotkeyClipboard) + ")";
                _miText.Text = "文本翻译 (" + HotkeyParser.Display(Program.Settings.HotkeyText) + ")";
                _miDoc.Text = "文档翻译 (" + HotkeyParser.Display(Program.Settings.HotkeyDoc) + ")";
            }
            if (fails.Count > 0)
                Balloon("提示", "以下热键注册失败（可能被其他程序占用，可在设置中更换）：\n" + string.Join("\n", fails.ToArray()), ToolTipIcon.Warning);
        }

        void RegisterOne(int id, string combo, string name, List<string> fails)
        {
            uint mod, vk;
            if (!HotkeyParser.TryParse(combo, out mod, out vk))
            {
                fails.Add(name + "（配置 " + combo + " 无效）");
                return;
            }
            if (!_hk.Register(id, mod, vk))
                fails.Add(name + " (" + HotkeyParser.Format(mod, vk) + ")");
        }

        /// <summary>探测组合键能否注册（供设置窗口校验）；与本程序当前热键相同视为可用。</summary>
        public bool ProbeHotkey(uint mod, uint vk)
        {
            string combo = HotkeyParser.Format(mod, vk);
            string[] current =
            {
                Program.Settings.HotkeyScreen, Program.Settings.HotkeyClipboard,
                Program.Settings.HotkeyText, Program.Settings.HotkeyDoc
            };
            foreach (string c in current)
                if (!string.IsNullOrEmpty(c) && HotkeyParser.Display(c) == combo) return true;
            bool ok = Native.RegisterHotKey(_hk.Handle, 99, mod, vk);
            if (ok)
            {
                try { Native.UnregisterHotKey(_hk.Handle, 99); } catch { }
            }
            return ok;
        }

        async void OnHotkey(int id)
        {
            try
            {
                // 长按热键时键盘自动重复会连续触发，统一去抖
                if ((DateTime.UtcNow - _lastHotkeyAt).TotalMilliseconds < 400) return;
                _lastHotkeyAt = DateTime.UtcNow;

                if (id == 1) await ScreenTranslateAsync();
                else if (id == 2) await ClipboardTranslateAsync();
                else if (id == 3) ShowText();
                else if (id == 4) ShowDoc();
            }
            catch (Exception ex)
            {
                Balloon("出错", ex.Message, ToolTipIcon.Error);
                Program.LogError(ex);
            }
        }

        async Task ScreenTranslateAsync()
        {
            // 框选/识别/翻译整个流程进行中时，忽略重复触发的热键（长按会自动重复）
            if (_screenBusy) return;
            _screenBusy = true;
            try
            {
                Rectangle rect;
                using (OverlayForm ov = new OverlayForm())
                {
                    if (ov.ShowDialog() != DialogResult.OK || ov.ResultRect.Width < 8) return;
                    rect = ov.ResultRect;
                }
                System.Drawing.Rectangle vs = SystemInformation.VirtualScreen;
                Rectangle abs = new Rectangle(vs.X + rect.X, vs.Y + rect.Y, rect.Width, rect.Height);
                await Task.Delay(120);

                string ocrText;
                using (Bitmap bmp = new Bitmap(abs.Width, abs.Height))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(abs.X, abs.Y, 0, 0, abs.Size);
                    using (Bitmap big = OcrHelper.Upscale(bmp))
                    {
                        if (Program.Settings.ScreenOcrLang == "auto")
                            ocrText = await OcrHelper.RecognizeAutoAsync(big);
                        else
                            ocrText = await OcrHelper.RecognizeAsync(big, Program.Settings.ScreenOcrLang);
                    }
                }
                if (string.IsNullOrWhiteSpace(ocrText))
                {
                    Balloon("屏幕翻译", "没有识别到文字，请框选包含文字的区域。", ToolTipIcon.Warning);
                    return;
                }
                ocrText = ocrText.Trim();
                if (ocrText.Length > 4000) ocrText = ocrText.Substring(0, 4000);

                ResultForm rf = new ResultForm(ocrText, "正在翻译…");
                rf.Show();
                try { rf.SetTranslation(await Program.Client.TranslateAsync(ocrText)); }
                catch (Exception ex) { rf.SetTranslation("翻译失败：" + ex.Message); }
            }
            finally
            {
                _screenBusy = false;
            }
        }

        async Task ClipboardTranslateAsync()
        {
            // 防止长按热键自动重复导致弹出一堆结果窗口
            if (_clipBusy) return;
            _clipBusy = true;
            try
            {
                string t = "";
                try { if (Clipboard.ContainsText()) t = Clipboard.GetText(); } catch { }
                if (string.IsNullOrWhiteSpace(t))
                {
                    Balloon("翻译剪贴板", "剪贴板里没有文本。", ToolTipIcon.Warning);
                    return;
                }
                t = t.Trim();
                if (t.Length > 4000) t = t.Substring(0, 4000);
                ResultForm rf = new ResultForm(t, "正在翻译…");
                rf.Show();
                try { rf.SetTranslation(await Program.Client.TranslateAsync(t)); }
                catch (Exception ex) { rf.SetTranslation("翻译失败：" + ex.Message); }
            }
            finally
            {
                _clipBusy = false;
            }
        }

        async void ScreenTranslate()
        {
            try { await ScreenTranslateAsync(); }
            catch (Exception ex)
            {
                Balloon("出错", ex.Message, ToolTipIcon.Error);
                Program.LogError(ex);
            }
        }

        async void ClipboardTranslate()
        {
            try { await ClipboardTranslateAsync(); }
            catch (Exception ex)
            {
                Balloon("出错", ex.Message, ToolTipIcon.Error);
                Program.LogError(ex);
            }
        }

        void ShowText()
        {
            if (_textForm == null || _textForm.IsDisposed) _textForm = new TextTranslateForm();
            _textForm.Show();
            _textForm.Activate();
        }

        void ShowDoc()
        {
            if (_docForm == null || _docForm.IsDisposed) _docForm = new DocTranslateForm();
            _docForm.Show();
            _docForm.Activate();
        }

        void ShowSettings()
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
                _settingsForm = new SettingsForm(ProbeHotkey, ApplyHotkeys);
            _settingsForm.Show();
            _settingsForm.Activate();
        }

        void Balloon(string title, string msg, ToolTipIcon icon)
        {
            try { _tray.ShowBalloonTip(3000, title, msg, icon); } catch { }
        }

        void ExitApp()
        {
            _hk.UnregisterAll();
            _tray.Visible = false;
            _tray.Dispose();
            Application.Exit();
        }

        static Icon MakeIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (Font f = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold))
                        g.DrawString("译", f, Brushes.OrangeRed, new RectangleF(-3f, -5f, 38f, 38f));
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

        static bool IsAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    return k != null && k.GetValue("YiYiTranslator") != null;
            }
            catch { return false; }
        }

        static void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    string exe = Assembly.GetExecutingAssembly().Location;
                    if (on) k.SetValue("YiYiTranslator", "\"" + exe + "\"");
                    else if (k.GetValue("YiYiTranslator") != null) k.DeleteValue("YiYiTranslator");
                }
            }
            catch { }
        }
    }

    /// <summary>隐藏窗口，只用来接收全局热键消息。</summary>
    public class HotkeyWindow : Form
    {
        public event Action<int> HotkeyPressed;

        public HotkeyWindow()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            if (Handle == IntPtr.Zero) throw new InvalidOperationException("无法创建热键窗口");
        }

        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }

        public bool Register(int id, uint mod, uint vk)
        {
            return Native.RegisterHotKey(Handle, id, mod, vk);
        }

        public void UnregisterAll()
        {
            for (int i = 1; i <= 4; i++)
            {
                try { Native.UnregisterHotKey(Handle, i); } catch { }
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                if (HotkeyPressed != null) HotkeyPressed(m.WParam.ToInt32());
                return;
            }
            base.WndProc(ref m);
        }
    }
}
