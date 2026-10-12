using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace YiYi
{
    public class AppSettings
    {
        public string TargetLang = "zh-CN";
        public string[] EngineOrder = new string[] { "bing", "qq", "youdao", "google", "mymemory" };
        public string BaiduAppId = "";
        public string BaiduSecret = "";
        public string DeepLKey = "";
        public string ScreenOcrLang = "auto";
        public int RequestDelayMs = 200;
        public string HotkeyScreen = "Ctrl+Alt+S";
        public string HotkeyClipboard = "Ctrl+Alt+V";
        public string HotkeyText = "Ctrl+Alt+T";
        public string HotkeyDoc = "Ctrl+Alt+D";

        public static string OverrideDirForTest;

        public static string Dir()
        {
            string baseDir = OverrideDirForTest != null
                ? OverrideDirForTest
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\YiYiTranslator";
            if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);
            return baseDir;
        }

        public static AppSettings Load()
        {
            try
            {
                string path = Path.Combine(Dir(), "settings.json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    AppSettings s = Json.Ser.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        FillDefaults(s);
                        return s;
                    }
                }
            }
            catch { }
            return FillDefaults(new AppSettings());
        }

        static AppSettings FillDefaults(AppSettings s)
        {
            if (string.IsNullOrEmpty(s.TargetLang)) s.TargetLang = "zh-CN";
            if (s.EngineOrder == null || s.EngineOrder.Length == 0)
                s.EngineOrder = new string[] { "bing", "qq", "youdao", "google", "mymemory" };
            if (s.RequestDelayMs < 0) s.RequestDelayMs = 200;
            if (string.IsNullOrEmpty(s.ScreenOcrLang)) s.ScreenOcrLang = "auto";
            if (string.IsNullOrEmpty(s.HotkeyScreen)) s.HotkeyScreen = "Ctrl+Alt+S";
            if (string.IsNullOrEmpty(s.HotkeyClipboard)) s.HotkeyClipboard = "Ctrl+Alt+V";
            if (string.IsNullOrEmpty(s.HotkeyText)) s.HotkeyText = "Ctrl+Alt+T";
            if (string.IsNullOrEmpty(s.HotkeyDoc)) s.HotkeyDoc = "Ctrl+Alt+D";
            return s;
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(Path.Combine(Dir(), "settings.json"), Json.Dump(this), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    /// <summary>快捷键字符串（如 "Ctrl+Alt+S"）与 RegisterHotKey 参数之间的转换。</summary>
    public static class HotkeyParser
    {
        public const string DefaultScreen = "Ctrl+Alt+S";
        public const string DefaultClipboard = "Ctrl+Alt+V";
        public const string DefaultText = "Ctrl+Alt+T";
        public const string DefaultDoc = "Ctrl+Alt+D";

        public static bool TryParse(string s, out uint mod, out uint vk)
        {
            mod = 0; vk = 0;
            if (string.IsNullOrEmpty(s)) return false;
            string[] parts = s.Split('+');
            bool hasKey = false;
            foreach (string raw in parts)
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;
                string l = p.ToLowerInvariant();
                if (l == "ctrl" || l == "control") { mod |= Native.MOD_CONTROL; continue; }
                if (l == "alt") { mod |= Native.MOD_ALT; continue; }
                if (l == "shift") { mod |= Native.MOD_SHIFT; continue; }
                if (l == "win" || l == "super" || l == "meta") { mod |= Native.MOD_WIN; continue; }
                if (hasKey) return false;
                Keys k;
                if (!Enum.TryParse<Keys>(p, true, out k)) return false;
                bool okKey = (k >= Keys.A && k <= Keys.Z) || (k >= Keys.D0 && k <= Keys.D9) || (k >= Keys.F1 && k <= Keys.F12);
                if (!okKey) return false;
                vk = (uint)k;
                hasKey = true;
            }
            return hasKey;
        }

        public static string Format(uint mod, uint vk)
        {
            StringBuilder sb = new StringBuilder();
            if ((mod & Native.MOD_CONTROL) != 0) sb.Append("Ctrl+");
            if ((mod & Native.MOD_ALT) != 0) sb.Append("Alt+");
            if ((mod & Native.MOD_SHIFT) != 0) sb.Append("Shift+");
            if ((mod & Native.MOD_WIN) != 0) sb.Append("Win+");
            sb.Append(KeyName(vk));
            return sb.ToString();
        }

        public static string KeyName(uint vk)
        {
            Keys k = (Keys)vk;
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            return k.ToString();
        }

        /// <summary>把任意写法规范化显示；解析失败时原样返回。</summary>
        public static string Display(string s)
        {
            uint mod, vk;
            if (TryParse(s, out mod, out vk)) return Format(mod, vk);
            return s;
        }
    }
}
