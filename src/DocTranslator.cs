using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace YiYi
{
    /// <summary>
    /// 文档翻译：txt / md / srt / docx / pdf。
    /// 输出文件生成在原文件同目录，名字加 ".translated"，不改动原文件。
    /// </summary>
    public static class DocTranslator
    {
        static readonly Regex UrlRx = new Regex("https?://[^\\s\\)\\]\\}\"'>，。；]+", RegexOptions.Compiled);
        static readonly Regex CodeRx = new Regex("`[^`\\n]+`", RegexOptions.Compiled);
        static readonly Regex TokenRestoreRx = new Regex("[{\\[【]\\s*([UuCc])\\s*(\\d+)\\s*[}\\]】]", RegexOptions.Compiled);
        static readonly Regex PrefixRx = new Regex("^((?:#{1,6}\\s+)?(?:[-*+]\\s+|\\d+[.)]\\s+)?(>\\s+)?)", RegexOptions.Compiled);

        public static async Task<string> TranslateFile(string path, EngineClient client, AppSettings s, Action<string> log)
        {
            if (!File.Exists(path)) throw new Exception("文件不存在: " + path);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".txt":
                case ".md":
                case ".markdown":
                case ".log":
                case ".srt":
                    return await TranslateTextLike(path, client, log);
                case ".docx":
                    return await TranslateDocx(path, client, log);
                case ".pdf":
                    return await TranslatePdf(path, client, log);
                default:
                    throw new Exception("暂不支持的格式: " + ext + "（支持 txt / md / log / srt / docx / pdf）");
            }
        }

        // ---------------- 文本类（txt/md/srt/log） ----------------

        static async Task<string> TranslateTextLike(string path, EngineClient client, Action<string> log)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            bool isSrt = ext == ".srt";
            string[] lines = ReadLinesSmart(path);
            List<string> output = new List<string>(lines.Length);
            bool inCode = false;
            int total = lines.Count(l => IsTranslatableText(l.Trim(), isSrt));
            int done = 0;
            log("共 " + total + " 行需要翻译");
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
                {
                    inCode = !inCode;
                    output.Add(line);
                    continue;
                }
                if (inCode || !IsTranslatableText(trimmed, isSrt))
                {
                    output.Add(line);
                    continue;
                }
                string ws = line.Substring(0, line.Length - line.TrimStart().Length);
                string prefix = "";
                Match pm = PrefixRx.Match(trimmed);
                if (pm.Success && pm.Groups[1].Value.Trim().Length > 0) prefix = pm.Groups[1].Value;
                string body = prefix.Length > 0 ? trimmed.Substring(prefix.Length) : trimmed;
                string translated;
                try { translated = await TranslateProtected(body, client); }
                catch (Exception ex)
                {
                    log("第 " + (done + 1) + " 行翻译失败，保留原文: " + ex.Message);
                    output.Add(line);
                    continue;
                }
                output.Add(ws + prefix + translated);
                done++;
                if (done % 10 == 0 || done == total) log("进度: " + done + "/" + total);
            }
            string outPath = Path.Combine(Path.GetDirectoryName(path),
                Path.GetFileNameWithoutExtension(path) + ".translated" + ext);
            File.WriteAllLines(outPath, output, new UTF8Encoding(true));
            return outPath;
        }

        // ---------------- DOCX ----------------

        static async Task<string> TranslateDocx(string path, EngineClient client, Action<string> log)
        {
            string outPath = Path.Combine(Path.GetDirectoryName(path),
                Path.GetFileNameWithoutExtension(path) + ".translated.docx");
            File.Copy(path, outPath, true);

            List<List<XmlElement>> todo = new List<List<XmlElement>>();
            List<string> texts = new List<string>();

            using (FileStream fs = new FileStream(outPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Update))
            {
                ZipArchiveEntry entry = zip.GetEntry("word/document.xml");
                if (entry == null) throw new Exception("不是有效的 docx 文件（缺少 word/document.xml）");
                string xml;
                using (StreamReader sr = new StreamReader(entry.Open(), Encoding.UTF8)) xml = sr.ReadToEnd();
                XmlDocument doc = new XmlDocument();
                doc.PreserveWhitespace = true;
                doc.LoadXml(xml);
                XmlNamespaceManager nsm = new XmlNamespaceManager(doc.NameTable);
                nsm.AddNamespace("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
                XmlNodeList paras = doc.SelectNodes("//w:p", nsm);
                foreach (XmlNode p in paras)
                {
                    XmlNodeList tnodes = p.SelectNodes(".//w:t", nsm);
                    if (tnodes == null || tnodes.Count == 0) continue;
                    StringBuilder sb = new StringBuilder();
                    foreach (XmlNode t in tnodes) sb.Append(t.InnerText);
                    string text = sb.ToString().Trim();
                    if (!IsTranslatableText(text, false)) continue;
                    List<XmlElement> elist = new List<XmlElement>();
                    foreach (XmlNode t in tnodes) elist.Add((XmlElement)t);
                    todo.Add(elist);
                    texts.Add(text);
                }
                int total = todo.Count;
                int done = 0;
                log("共 " + total + " 个段落需要翻译");
                for (int i = 0; i < todo.Count; i++)
                {
                    string tr;
                    try { tr = await TranslateProtected(texts[i], client); }
                    catch (Exception ex)
                    {
                        log("段落 " + (i + 1) + " 翻译失败，保留原文: " + ex.Message);
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(tr)) continue;
                    List<XmlElement> list = todo[i];
                    for (int k = 0; k < list.Count; k++)
                    {
                        XmlElement el = list[k];
                        el.InnerText = (k == 0) ? tr : "";
                        el.SetAttribute("space", "http://www.w3.org/XML/1998/namespace", "preserve");
                    }
                    done++;
                    if (done % 5 == 0 || done == total) log("进度: " + done + "/" + total);
                }
                entry.Delete();
                ZipArchiveEntry ne = zip.CreateEntry("word/document.xml");
                using (Stream st = ne.Open())
                {
                    byte[] pre = Encoding.UTF8.GetPreamble();
                    if (pre.Length > 0) st.Write(pre, 0, pre.Length);
                    byte[] b = Encoding.UTF8.GetBytes(doc.OuterXml);
                    st.Write(b, 0, b.Length);
                }
            }
            return outPath;
        }

        // ---------------- PDF（渲染成图片后 OCR，适合扫描版和图片型 PDF） ----------------

        static async Task<string> TranslatePdf(string path, EngineClient client, Action<string> log)
        {
            string outPath = Path.Combine(Path.GetDirectoryName(path),
                Path.GetFileNameWithoutExtension(path) + ".translated.txt");
            StringBuilder sbOut = new StringBuilder();
            byte[] pdfBytes = File.ReadAllBytes(path);

            InMemoryRandomAccessStream inStream = new InMemoryRandomAccessStream();
            DataWriter writer = new DataWriter(inStream);
            writer.WriteBytes(pdfBytes);
            await writer.StoreAsync();
            inStream.Seek(0);

            PdfDocument pdf = await PdfDocument.LoadFromStreamAsync(inStream);
            uint count = pdf.PageCount;
            log("共 " + count + " 页");
            for (uint i = 0; i < count; i++)
            {
                string pageText = "";
                try
                {
                    using (PdfPage page = pdf.GetPage(i))
                    using (InMemoryRandomAccessStream renderStream = new InMemoryRandomAccessStream())
                    {
                        PdfPageRenderOptions opts = new PdfPageRenderOptions();
                        opts.DestinationWidth = 1600;
                        await page.RenderToStreamAsync(renderStream, opts);
                        renderStream.Seek(0);
                        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(renderStream);
                        using (SoftwareBitmap sbmp = await decoder.GetSoftwareBitmapAsync(
                            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied))
                        using (Bitmap renderedBmp = SoftwareBitmapToBitmap(sbmp))
                        {
                            pageText = await OcrHelper.RecognizeAsync(renderedBmp, "auto") ?? "";
                        }
                    }
                }
                catch (Exception ex)
                {
                    log("第 " + (i + 1) + " 页识别失败: " + ex.Message);
                }

                sbOut.AppendLine("────── 第 " + (i + 1) + " 页 ──────");
                string[] olines = (pageText ?? "").Replace("\r\n", "\n").Split('\n');
                foreach (string ol in olines)
                {
                    string t = ol.Trim();
                    if (!IsTranslatableText(t, false)) { if (t.Length > 0) sbOut.AppendLine(t); continue; }
                    string tr;
                    try { tr = await TranslateProtected(t, client); }
                    catch { sbOut.AppendLine(t); continue; }
                    sbOut.AppendLine(tr);
                }
                log("第 " + (i + 1) + "/" + count + " 页完成");
            }
            File.WriteAllText(outPath, sbOut.ToString(), new UTF8Encoding(true));
            return outPath;
        }

        // ---------------- 公共辅助 ----------------

        /// <summary>把 WinRT SoftwareBitmap（Bgra8）复制成 GDI Bitmap。</summary>
        static Bitmap SoftwareBitmapToBitmap(Windows.Graphics.Imaging.SoftwareBitmap sb)
        {
            int w = sb.PixelWidth, h = sb.PixelHeight;
            byte[] px = new byte[w * h * 4];
            Windows.Storage.Streams.IBuffer buf = Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(new byte[w * h * 4]);
            sb.CopyToBuffer(buf);
            Windows.Security.Cryptography.CryptographicBuffer.CopyToByteArray(buf, out px);
            Bitmap bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                long basePtr = bd.Scan0.ToInt64();
                int stride = bd.Stride;
                if (stride == w * 4)
                {
                    Marshal.Copy(px, 0, bd.Scan0, px.Length);
                }
                else
                {
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(new IntPtr(basePtr + y * stride), px, y * w * 4, w * 4);
                }
            }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }

        /// <summary>翻译一行文本；URL 和 `代码` 用占位符保护，翻完还原。</summary>
        public static async Task<string> TranslateProtected(string text, EngineClient client)
        {
            List<string> urls = new List<string>();
            List<string> codes = new List<string>();
            string work = UrlRx.Replace(text, delegate(Match m)
            {
                urls.Add(m.Value);
                return "{U" + (urls.Count - 1) + "}";
            });
            work = CodeRx.Replace(work, delegate(Match m)
            {
                codes.Add(m.Value);
                return "{C" + (codes.Count - 1) + "}";
            });
            string r = await client.TranslateAsync(work);
            r = TokenRestoreRx.Replace(r, delegate(Match m)
            {
                int i;
                if (int.TryParse(m.Groups[2].Value, out i))
                {
                    if ((m.Groups[1].Value == "U" || m.Groups[1].Value == "u") && i < urls.Count) return urls[i];
                    if ((m.Groups[1].Value == "C" || m.Groups[1].Value == "c") && i < codes.Count) return codes[i];
                }
                return m.Value;
            });
            return r;
        }

        public static bool IsTranslatableText(string t, bool isSrt)
        {
            if (string.IsNullOrEmpty(t) || t.Length < 2) return false;
            if (isSrt && t.Contains("-->")) return false;
            if (t.StartsWith("http://") || t.StartsWith("https://")) return false;
            if (t.StartsWith("![") && t.EndsWith(")")) return false;
            if (t.StartsWith("<") && t.EndsWith(">")) return false;
            if (Regex.IsMatch(t, "^\\|[-: |]+\\|$")) return false;
            if (Regex.IsMatch(t, "^[0-9\\s\\p{P}\\p{S}]+$")) return false;
            if (t.StartsWith("@") && t.IndexOf(' ') < 0) return false;
            // 必须含有字母或中日韩文字
            return Regex.IsMatch(t, "[A-Za-z\\u4e00-\\u9fff\\u3040-\\u30ff\\uac00-\\ud7af]");
        }

        static string[] ReadLinesSmart(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch
            {
                try { text = Encoding.GetEncoding("GB18030").GetString(bytes); }
                catch { text = Encoding.Default.GetString(bytes); }
            }
            if (text.Length > 0 && text[0] == 0xFEFF) text = text.Substring(1);
            return Regex.Split(text, "\r\n|\r|\n");
        }
    }
}
