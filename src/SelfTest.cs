using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YiYi
{
    /// <summary>
    /// 自测：OCR（合成图片识别）、各翻译接口、docx / md / pdf 全流程。
    /// 运行方式：TestHarness.exe 或 YiYiTranslator.exe --selftest
    /// </summary>
    public static class SelfTest
    {
        static int _fails;

        public static void RunAll()
        {
            Console.OutputEncoding = Encoding.UTF8;
            try { Native.SetProcessDPIAware(); } catch { }
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch { }
            Console.WriteLine("== 1/6 OCR 测试 ==");
            TestOcr();
            TestAnnotator();
            Console.WriteLine("== 2/6 翻译接口测试 ==");
            TestEngines().Wait();
            Console.WriteLine("== 3/6 DOCX 翻译测试 ==");
            TestDocx();
            Console.WriteLine("== 4/6 TXT/MD 翻译测试 ==");
            TestMd();
            Console.WriteLine("== 5/6 PDF 翻译测试 ==");
            TestPdf().Wait();
            Console.WriteLine("== 6/6 其他（设置回环/屏幕截图） ==");
            TestSettingsRoundTrip();
            TestScreenCapture();
            Console.WriteLine(_fails == 0 ? "全部通过" : ("有 " + _fails + " 项未通过"));
            Environment.ExitCode = _fails == 0 ? 0 : 1;
        }

        static void Fail(string name, string why) { _fails++; Console.WriteLine("[FAIL] " + name + ": " + why); }
        static void Pass(string name) { Console.WriteLine("[PASS] " + name); }

        static void TestOcr()
        {
            try
            {
                List<string> langs = OcrHelper.AvailableLanguages();
                Console.WriteLine("可用 OCR 语言: " + string.Join(", ", langs.ToArray()));
                if (langs.Count == 0) { Fail("ocr", "系统没有 OCR 语言包"); return; }

                using (Bitmap bmp = new Bitmap(640, 140))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White);
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        using (Font f = new Font("Arial", 30f)) g.DrawString("The quick brown fox 123", f, Brushes.Black, 16, 40);
                    }
                    using (Bitmap big = OcrHelper.Upscale(bmp))
                    {
                        string t = OcrHelper.RecognizeAsync(big, "en-US").Result;
                        Console.WriteLine("识别结果: " + (t ?? "").Replace("\n", " / "));
                        string low = (t ?? "").ToLowerInvariant();
                        if (low.Contains("quick") && low.Contains("fox")) Pass("ocr-en");
                        else Fail("ocr-en", "未识别出 quick/fox");

                        // 行级识别 + 归一化坐标检查
                        List<OcrLineInfo> lines = OcrHelper.RecognizeLinesAsync(big, "en-US").Result;
                        bool linesOk = lines != null && lines.Count >= 1;
                        bool rectsOk = true;
                        foreach (OcrLineInfo li in lines)
                        {
                            if (string.IsNullOrWhiteSpace(li.Text)) { linesOk = false; break; }
                            if (li.Rect.X < -0.01f || li.Rect.Y < 0f
                                || li.Rect.Right > 1.02f || li.Rect.Bottom > 1f + 1e-4f) { rectsOk = false; break; }
                        }
                        Console.WriteLine("行数: " + lines.Count + "，首行: " + (lines.Count > 0 ? lines[0].Text : "(无)")
                            + " rect=(" + (lines.Count > 0 ? lines[0].Rect.ToString() : "-") + ")");
                        if (lines.Count >= 1 && lines[0].Text.ToLowerInvariant().Contains("quick") && rectsOk) Pass("ocr-lines");
                        else Fail("ocr-lines", "行数=" + lines.Count + " rectsOk=" + rectsOk);
                    }
                }
                if (langs.Any(delegate(string t) { return t.StartsWith("zh", StringComparison.OrdinalIgnoreCase); }))
                {
                    string zhTag = langs.First(delegate(string t) { return t.StartsWith("zh", StringComparison.OrdinalIgnoreCase); });
                    using (Bitmap bmp2 = new Bitmap(640, 140))
                    {
                        using (Graphics g2 = Graphics.FromImage(bmp2))
                        {
                            g2.Clear(Color.White);
                            g2.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                            using (Font f2 = new Font("Microsoft YaHei UI", 30f)) g2.DrawString("你好世界翻译测试", f2, Brushes.Black, 16, 40);
                        }
                        using (Bitmap big2 = OcrHelper.Upscale(bmp2))
                        {
                            string t2 = OcrHelper.RecognizeAsync(big2, zhTag).Result;
                            Console.WriteLine("识别结果: " + (t2 ?? "").Replace("\n", " / "));
                            int hit = 0;
                            foreach (char c in "你好世界翻译测试")
                                if ((t2 ?? "").Contains(Convert.ToString(c))) hit++;
                            if (hit >= 3) Pass("ocr-zh");
                            else Fail("ocr-zh", "命中 " + hit + "/7");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("(跳过中文 OCR：系统未装中文语言包)");
                }
            }
            catch (Exception ex) { Fail("ocr", ex.Message); }
        }

        static void TestAnnotator()
        {
            try
            {
                using (Bitmap bmp = new Bitmap(600, 160))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White);
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                        using (Font f = new Font("Arial", 26f))
                        {
                            g.DrawString("Hello world", f, Brushes.Black, 30, 30);
                            g.DrawString("Second line here", f, Brushes.Black, 16, 95);
                        }
                    }
                    List<OcrLineInfo> lines = new List<OcrLineInfo>();
                    try { lines = OcrHelper.RecognizeLinesAsync(bmp, "en-US").Result; } catch { }
                    if (lines == null || lines.Count == 0) { Fail("annotate", "行级 OCR 无结果"); return; }

                    List<string> trs = new List<string>();
                    for (int i = 0; i < lines.Count; i++) trs.Add("测试" + (i + 1));

                    using (Bitmap before = (Bitmap)bmp.Clone())
                    using (Bitmap annotated = Annotator.Draw(bmp, lines, trs))
                    {
                        if (annotated.Width != bmp.Width || annotated.Height != bmp.Height)
                        {
                            Fail("annotate", "尺寸不一致"); return;
                        }
                        int diff = CountDifferentPixels(before, annotated);
                        Console.WriteLine("差异像素(采样): " + diff + "，行数: " + lines.Count);
                        if (diff > 0) Pass("annotate");
                        else Fail("annotate", "标注图与原图相同");
                    }
                }
            }
            catch (Exception ex) { Fail("annotate", ex.Message); }
        }

        static int CountDifferentPixels(Bitmap a, Bitmap b)
        {
            int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
            int diff = 0;
            for (int y = 0; y < h; y += 3)
                for (int x = 0; x < w; x += 3)
                    if (a.GetPixel(x, y) != b.GetPixel(x, y)) diff++;
            return diff;
        }

        static async Task TestEngines()
        {
            AppSettings s = AppSettings.Load();
            foreach (string name in new string[] { "bing", "qq", "youdao", "google", "mymemory" })
            {
                TranslateProvider p = EngineClient.Create(name, s);
                if (p == null) { Fail("engine-" + name, "未实现"); continue; }
                try
                {
                    string r = await p.TranslateAsync("Hello world, this is a translation test.", "zh-CN", CancellationToken.None);
                    Console.WriteLine(name + " => " + (r ?? "").Replace("\n", " "));
                    if (HasCjk(r)) Pass("engine-" + name);
                    else Fail("engine-" + name, "结果不含中文");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(name + " => 错误: " + ex.Message);
                    Fail("engine-" + name, ex.Message);
                }
                await Task.Delay(400);
            }
        }

        static void TestDocx()
        {
            try
            {
                string dir = PrepareTempDir();
                string src = Path.Combine(dir, "sample.docx");
                WriteSampleDocx(src);
                AppSettings s = AppSettings.Load();
                EngineClient client = new EngineClient(s);
                string outPath = DocTranslator.TranslateFile(src, client, s, delegate(string m) { Console.WriteLine("  " + m); }).Result;
                if (!File.Exists(outPath)) { Fail("docx", "没有输出文件"); return; }
                string text = ExtractDocxText(outPath);
                Console.WriteLine("  输出内容: " + text);
                if (HasCjk(text)) Pass("docx");
                else Fail("docx", "输出里没有中文");
            }
            catch (Exception ex) { Fail("docx", ex.Message); }
        }

        static void TestMd()
        {
            try
            {
                string dir = PrepareTempDir();
                string src = Path.Combine(dir, "sample.md");
                File.WriteAllText(src,
                    "# Hello World\n\nThis is a test paragraph about translation.\n\nVisit https://example.com/docs for more info.\n\n```csharp\nint x = 1;\n```\n",
                    new UTF8Encoding(false));
                AppSettings s = AppSettings.Load();
                EngineClient client = new EngineClient(s);
                string outPath = DocTranslator.TranslateFile(src, client, s, delegate(string m) { Console.WriteLine("  " + m); }).Result;
                string outText = File.ReadAllText(outPath);
                Console.WriteLine("  ---- 输出 ----");
                Console.WriteLine(outText);
                Console.WriteLine("  --------");
                bool ok = outText.Contains("# ") && outText.Contains("```") && outText.Contains("int x = 1;")
                    && outText.Contains("https://example.com/docs") && HasCjk(outText);
                if (ok) Pass("md");
                else Fail("md", "结构或代码块被破坏");
            }
            catch (Exception ex) { Fail("md", ex.Message); }
        }

        static async Task TestPdf()
        {
            try
            {
                string dir = PrepareTempDir();
                string src = Path.Combine(dir, "sample.pdf");
                File.WriteAllBytes(src, BuildSamplePdf());
                AppSettings s = AppSettings.Load();
                EngineClient client = new EngineClient(s);
                string outPath = await DocTranslator.TranslateFile(src, client, s, delegate(string m) { Console.WriteLine("  " + m); });
                string outText = File.ReadAllText(outPath);
                string flat = outText.Replace("\n", " / ");
                Console.WriteLine("  输出: " + flat.Substring(0, Math.Min(300, flat.Length)));
                if (HasCjk(outText)) Pass("pdf");
                else Fail("pdf", "没有中文输出");
            }
            catch (Exception ex) { Fail("pdf", ex.Message); }
        }

        static void TestSettingsRoundTrip()
        {
            // 用临时目录，避免覆盖用户真实配置
            AppSettings.OverrideDirForTest = Path.Combine(PrepareTempDir(), "settings");
            try
            {
                AppSettings s = new AppSettings();
                s.TargetLang = "ja";
                s.EngineOrder = new string[] { "qq", "bing" };
                s.ScreenOcrLang = "zh-Hans-CN";
                s.HotkeyScreen = "Ctrl+Alt+F1";
                s.HotkeyDoc = "Ctrl+Shift+D";
                s.Save();
                AppSettings s2 = AppSettings.Load();
                bool ok = s2.TargetLang == "ja"
                    && s2.EngineOrder != null && s2.EngineOrder.Length == 2 && s2.EngineOrder[0] == "qq"
                    && s2.ScreenOcrLang == "zh-Hans-CN"
                    && s2.HotkeyScreen == "Ctrl+Alt+F1"
                    && s2.HotkeyDoc == "Ctrl+Shift+D";
                if (ok) Pass("settings-roundtrip");
                else Fail("settings-roundtrip", "读回值不符: target=" + s2.TargetLang + " hotkey=" + s2.HotkeyScreen);

                uint mod, vk;
                bool p1 = HotkeyParser.TryParse("Ctrl+Alt+S", out mod, out vk)
                    && mod == (Native.MOD_CONTROL | Native.MOD_ALT) && vk == (uint)'S'
                    && HotkeyParser.Format(mod, vk) == "Ctrl+Alt+S";
                bool p2 = HotkeyParser.TryParse("win + F12", out mod, out vk)
                    && mod == Native.MOD_WIN && vk == (uint)Keys.F12;
                bool p3 = !HotkeyParser.TryParse("Ctrl+Alt", out mod, out vk)       // 缺主键
                    && !HotkeyParser.TryParse("Ctrl+Alt+XYZ", out mod, out vk);      // 无效主键
                if (p1 && p2 && p3) Pass("hotkey-parser");
                else Fail("hotkey-parser", "p1=" + p1 + " p2=" + p2 + " p3=" + p3);
            }
            catch (Exception ex) { Fail("settings-roundtrip", ex.Message); }
            finally
            {
                AppSettings.OverrideDirForTest = null;
            }
        }

        static void TestScreenCapture()
        {
            try
            {
                Rectangle vs = SystemInformation.VirtualScreen;
                using (Bitmap bmp = new Bitmap(Math.Min(900, vs.Width), Math.Min(500, vs.Height)))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(vs.X, vs.Y, 0, 0, bmp.Size);
                }
                Pass("screen-capture(" + vs.Width + "x" + vs.Height + ")");
            }
            catch (Exception ex) { Fail("screen-capture", ex.Message); }
        }

        // ---------- 辅助 ----------

        static string PrepareTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "yiyi_selftest");
            Directory.CreateDirectory(dir);
            return dir;
        }

        static bool HasCjk(string s)
        {
            if (s == null) return false;
            foreach (char c in s) if (c >= 0x4E00 && c <= 0x9FFF) return true;
            return false;
        }

        static string ExtractDocxText(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                ZipArchiveEntry entry = zip.GetEntry("word/document.xml");
                using (StreamReader sr = new StreamReader(entry.Open(), Encoding.UTF8))
                {
                    string xml = sr.ReadToEnd();
                    StringBuilder sb = new StringBuilder();
                    foreach (Match m in Regex.Matches(xml, "<w:t[^>]*>([^<]*)</w:t>"))
                        sb.Append(m.Groups[1].Value).Append(" / ");
                    return sb.ToString();
                }
            }
        }

        static void WriteSampleDocx(string path)
        {
            string ct = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>";
            string rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>";
            string doc = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Hello world, this is the first paragraph.</w:t></w:r></w:p><w:p><w:r><w:t>The second paragraph contains more text to translate.</w:t></w:r></w:p></w:body></w:document>";
            using (FileStream fs = new FileStream(path, FileMode.Create))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "[Content_Types].xml", ct);
                WriteEntry(zip, "_rels/.rels", rels);
                WriteEntry(zip, "word/document.xml", doc);
            }
        }

        static void WriteEntry(ZipArchive zip, string name, string content)
        {
            ZipArchiveEntry e = zip.CreateEntry(name);
            using (Stream st = e.Open())
            {
                byte[] b = Encoding.UTF8.GetBytes(content);
                st.Write(b, 0, b.Length);
            }
        }

        static byte[] BuildSamplePdf()
        {
            string content = "BT /F1 24 Tf 40 100 Td (The quick brown fox jumps over the lazy dog) Tj ET";
            string[] objs = new string[]
            {
                "<</Type/Catalog/Pages 2 0 R>>",
                "<</Type/Pages/Kids[3 0 R]/Count 1>>",
                "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 200]/Resources<</Font<</F1 5 0 R>>>>/Contents 4 0 R>>",
                "<</Length " + (content.Length + 1) + ">>stream\n" + content + "\nendstream",
                "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>"
            };
            MemoryStream ms = new MemoryStream();
            List<int> offs = new List<int>();
            Action<string> w = delegate(string str)
            {
                byte[] b = Encoding.ASCII.GetBytes(str);
                ms.Write(b, 0, b.Length);
            };
            w("%PDF-1.4\n");
            for (int i = 0; i < objs.Length; i++)
            {
                offs.Add((int)ms.Position);
                w((i + 1) + " 0 obj\n" + objs[i] + "\nendobj\n");
            }
            int xref = (int)ms.Position;
            w("xref\n0 " + (objs.Length + 1) + "\n0000000000 65535 f \n");
            foreach (int o in offs) w(o.ToString("D10", System.Globalization.CultureInfo.InvariantCulture) + " 00000 n \n");
            w("trailer\n<</Size " + (objs.Length + 1) + "/Root 1 0 R>>\nstartxref\n" + xref + "\n%%EOF\n");
            return ms.ToArray();
        }
    }
}
