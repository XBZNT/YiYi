using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace YiYi
{
    /// <summary>
    /// 调用 Windows 10 自带 OCR（离线，不联网），把图片变成文字。
    /// 像素通过 CryptographicBuffer → SoftwareBitmap 直接进入 WinRT，全程不经过文件，
    /// 也不依赖 System.Runtime.WindowsRuntime.dll 投影。
    /// </summary>
    public static class OcrHelper
    {
        public static List<string> AvailableLanguages()
        {
            List<string> list = new List<string>();
            try
            {
                foreach (Language l in OcrEngine.AvailableRecognizerLanguages) list.Add(l.LanguageTag);
            }
            catch { }
            return list;
        }

        static OcrEngine TryCreate(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tag == "auto") return null;
            try { return OcrEngine.TryCreateFromLanguage(new Language(tag)); }
            catch { return null; }
        }

        static OcrEngine CreateAuto()
        {
            OcrEngine e = TryCreate("en-US"); if (e != null) return e;
            e = TryCreate("zh-CN"); if (e != null) return e;
            try { e = OcrEngine.TryCreateFromUserProfileLanguages(); if (e != null) return e; } catch { }
            List<Language> avail = new List<Language>();
            try { avail = OcrEngine.AvailableRecognizerLanguages.ToList(); } catch { }
            if (avail.Count > 0) return TryCreate(avail[0].LanguageTag);
            return null;
        }

        /// <summary>小图放大能明显提高识别率。</summary>
        public static Bitmap Upscale(Bitmap src)
        {
            int factor = 1;
            if (src.Height < 100) factor = 3;
            else if (src.Height < 200) factor = 2;
            if (factor > 1 && (long)src.Width * src.Height * factor * factor > 4000000L) factor = 2;
            if (factor <= 1) return src;
            Bitmap dst = new Bitmap(src.Width * factor, src.Height * factor);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, dst.Width, dst.Height),
                    new Rectangle(0, 0, src.Width, src.Height), GraphicsUnit.Pixel);
            }
            return dst;
        }

        static SoftwareBitmap ToSoftwareBitmap(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            byte[] px = new byte[w * h * 4];
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                long basePtr = bd.Scan0.ToInt64();
                int stride = bd.Stride;
                if (stride == w * 4)
                {
                    Marshal.Copy(bd.Scan0, px, 0, px.Length);
                }
                else
                {
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(new IntPtr(basePtr + y * stride), px, y * w * 4, w * 4);
                }
            }
            finally { bmp.UnlockBits(bd); }
            // GDI 位图的 alpha 通道未定义，OCR 需要不透明的像素
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;
            Windows.Storage.Streams.IBuffer buf = CryptographicBuffer.CreateFromByteArray(px);
            return SoftwareBitmap.CreateCopyFromBuffer(buf, BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
        }

        public static async Task<string> RecognizeAsync(Bitmap bmp, string langTag)
        {
            using (SoftwareBitmap sb = ToSoftwareBitmap(bmp))
            {
                return await RecognizeCoreAsync(sb, langTag);
            }
        }

        static async Task<string> RecognizeCoreAsync(SoftwareBitmap sb, string langTag)
        {
            OcrEngine engine = TryCreate(langTag) ?? CreateAuto();
            if (engine == null)
                throw new Exception("系统没有可用的 OCR 语言包：请在 Windows 设置 → 时间和语言 → 语言 中添加中文或英文语言后重试。");
            OcrResult result = await engine.RecognizeAsync(sb);
            return result == null || result.Text == null ? "" : result.Text;
        }

        /// <summary>自动模式：用中/英（及系统可用语言）各识别一次，按识别质量挑最好的。</summary>
        public static async Task<string> RecognizeAutoAsync(Bitmap bmp)
        {
            using (SoftwareBitmap sb = ToSoftwareBitmap(bmp))
            {
                return await RecognizeAutoCoreAsync(sb);
            }
        }

        public static async Task<string> RecognizeAutoCoreAsync(SoftwareBitmap sb)
        {
            List<string> tags = AvailableLanguages();
            List<string> picks = new List<string>();
            foreach (string t in tags)
            {
                if (StartsWith(t, "en")) picks.Add(t);
            }
            foreach (string t in tags)
            {
                if (StartsWith(t, "zh") && !picks.Contains(t)) picks.Add(t);
            }
            if (picks.Count == 0)
                foreach (string t in tags) { picks.Add(t); if (picks.Count >= 2) break; }
            if (picks.Count == 0)
                throw new Exception("系统没有可用的 OCR 语言包：请在 Windows 设置 → 时间和语言 → 语言 中添加中文或英文语言后重试。");

            string best = null;
            int bestScore = -1;
            foreach (string tag in picks)
            {
                string text = null;
                try { text = await RecognizeCoreAsync(sb, tag); } catch { }
                if (text == null) continue;
                int score = CountLatinWords(text) * 3 + CountCjk(text);
                if (score > bestScore) { bestScore = score; best = text; }
            }
            return best ?? "";
        }

        static bool StartsWith(string s, string prefix)
        {
            return s != null && s.Length >= prefix.Length
                && string.Compare(s, 0, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        static int CountCjk(string s)
        {
            int n = 0;
            foreach (char c in s) if (c >= 0x4E00 && c <= 0x9FFF) n++;
            return n;
        }

        static int CountLatinWords(string s)
        {
            int n = 0;
            bool inWord = false;
            int len = 0;
            foreach (char c in s)
            {
                if (char.IsLetter(c) && c < 128)
                {
                    if (!inWord) { inWord = true; len = 1; }
                    else len++;
                }
                else
                {
                    if (inWord && len >= 2) n++;
                    inWord = false;
                }
            }
            if (inWord && len >= 2) n++;
            return n;
        }
    }
}
