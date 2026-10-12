using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace YiYi
{
    public static class Json
    {
        static JavaScriptSerializer _ser;
        public static JavaScriptSerializer Ser
        {
            get
            {
                if (_ser == null)
                {
                    _ser = new JavaScriptSerializer();
                    _ser.MaxJsonLength = int.MaxValue / 2;
                }
                return _ser;
            }
        }
        public static object Parse(string s) { return Ser.DeserializeObject(s); }
        public static string Dump(object o) { return Ser.Serialize(o); }
        public static string Str(object o)
        {
            return o == null ? "" : Convert.ToString(o, CultureInfo.InvariantCulture);
        }
    }

    public class TranslationException : Exception
    {
        public TranslationException(string msg) : base(msg) { }
    }

    public abstract class TranslateProvider
    {
        public abstract string Name { get; }
        public abstract Task<string> TranslateAsync(string text, string target, CancellationToken ct);
        public virtual bool IsEnabled(AppSettings s) { return true; }

        protected static string BrowserUA
        {
            get
            {
                return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0";
            }
        }

        protected static HttpWebRequest BuildReq(string url, string ua, string referer)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = ua;
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            try { req.Headers.Add(HttpRequestHeader.AcceptLanguage, "zh-CN,zh;q=0.9,en;q=0.8"); } catch { }
            if (!string.IsNullOrEmpty(referer)) req.Referer = referer;
            return req;
        }

        protected static string ReadResp(HttpWebResponse resp)
        {
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        protected Tuple<string, string> HttpGetFull(string url, string ua, string referer)
        {
            HttpWebRequest req = BuildReq(url, ua, referer);
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    string body = ReadResp(resp);
                    string origin = resp.ResponseUri.GetLeftPart(UriPartial.Authority);
                    return Tuple.Create(body, origin);
                }
            }
            catch (WebException ex)
            {
                throw new TranslationException(Name + ": " + DescribeWebException(ex));
            }
        }

        protected string HttpGet(string url, string ua, string referer)
        {
            return HttpGetFull(url, ua, referer).Item1;
        }

        protected string HttpPost(string url, string contentType, byte[] body, string ua, string referer)
        {
            HttpWebRequest req = BuildReq(url, ua, referer);
            req.Method = "POST";
            req.ContentType = contentType;
            req.ContentLength = body.Length;
            try
            {
                using (Stream rs = req.GetRequestStream()) rs.Write(body, 0, body.Length);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) return ReadResp(resp);
            }
            catch (WebException ex)
            {
                throw new TranslationException(Name + ": " + DescribeWebException(ex));
            }
        }

        protected string HttpPostForm(string url, Dictionary<string, string> form, string ua, string referer)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in form)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value ?? ""));
            }
            return HttpPost(url, "application/x-www-form-urlencoded", Encoding.UTF8.GetBytes(sb.ToString()), ua, referer);
        }

        protected string HttpPostJson(string url, object payload, string ua, string referer)
        {
            string json = Json.Dump(payload);
            return HttpPost(url, "application/json; charset=UTF-8", Encoding.UTF8.GetBytes(json), ua, referer);
        }

        protected static string DescribeWebException(WebException ex)
        {
            string detail = "";
            try
            {
                HttpWebResponse resp = ex.Response as HttpWebResponse;
                if (resp != null)
                {
                    detail = ((int)resp.StatusCode).ToString(CultureInfo.InvariantCulture);
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        string s = sr.ReadToEnd();
                        if (s.Length > 200) s = s.Substring(0, 200);
                        detail += " " + s.Replace("\n", " ").Trim();
                    }
                }
            }
            catch { }
            if (detail.Length == 0) return ex.Message;
            return detail;
        }

        protected static bool LooksCjk(string s)
        {
            int cjk = 0, total = 0;
            foreach (char ch in s)
            {
                if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsDigit(ch)) continue;
                total++;
                if (ch >= 0x4E00 && ch <= 0x9FFF) cjk++;
            }
            return total > 0 && cjk * 2 >= total;
        }
    }

    // ============ 必应翻译（免费，国内直连） ============
    public class BingProvider : TranslateProvider
    {
        static readonly CookieContainer Cookies = new CookieContainer();
        static string _ig = "", _token = "", _key = "", _iid = "translator.5023", _origin = "https://cn.bing.com";
        static DateTime _tokenTime = DateTime.MinValue;

        public override string Name { get { return "bing"; } }

        void EnsureToken()
        {
            if (!string.IsNullOrEmpty(_token) && DateTime.UtcNow - _tokenTime < TimeSpan.FromMinutes(25)) return;
            HttpWebRequest req = BuildReq("https://www.bing.com/translator", BrowserUA, null);
            req.CookieContainer = Cookies;
            string body, origin;
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) body = sr.ReadToEnd();
                    origin = resp.ResponseUri.GetLeftPart(UriPartial.Authority);
                }
            }
            catch (WebException ex)
            {
                throw new TranslationException("bing: 无法访问(" + ex.Message + ")");
            }
            _origin = origin;
            Match m = Regex.Match(body, "IG:\"([A-Za-z0-9]+)\"");
            if (m.Success) _ig = m.Groups[1].Value;
            m = Regex.Match(body, "data-iid=\"([^\"]+)\"");
            if (m.Success) _iid = m.Groups[1].Value;
            // params_AbusePreventionHelper = [到期时间戳, token字符串, 刷新间隔]
            // 页面 JS: key = 第一个元素(时间戳), token = 第二个元素(字符串)
            m = Regex.Match(body, "params_AbusePreventionHelper\\s*=\\s*\\[\\s*(\\d+)\\s*,\\s*\"([^\"]+)\"");
            if (m.Success)
            {
                _key = m.Groups[1].Value;
                _token = m.Groups[2].Value;
                _tokenTime = DateTime.UtcNow;
            }
            if (string.IsNullOrEmpty(_token)) throw new TranslationException("bing: 页面未包含 token（接口可能已改版）");
        }

        public override Task<string> TranslateAsync(string text, string target, CancellationToken ct)
        {
            EnsureToken();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                string url = _origin + "/ttranslatev3?&IG=" + Uri.EscapeDataString(_ig) + "&iid=" + Uri.EscapeDataString(_iid);
                Dictionary<string, string> form = new Dictionary<string, string>();
                form["fromLang"] = "auto-detect";
                form["text"] = text;
                form["token"] = _token;
                form["key"] = _key;
                form["to"] = MapTarget(target);
                form["tryFetchingGenderDebiasedTranslations"] = "true";
                string body;
                try
                {
                    body = HttpPostForm(url, form, BrowserUA, _origin + "/translator");
                }
                catch (TranslationException)
                {
                    _token = "";
                    EnsureToken();
                    continue;
                }
                string r = ParseResponse(body);
                if (r != null) return Task.FromResult(r);
                _token = "";
                EnsureToken();
            }
            throw new TranslationException("bing: 翻译失败（token 或接口变更）");
        }

        static string ParseResponse(string body)
        {
            try
            {
                object[] arr = Json.Parse(body) as object[];
                if (arr == null || arr.Length == 0) return null;
                Dictionary<string, object> first = arr[0] as Dictionary<string, object>;
                if (first == null) return null;
                object[] trs = first.ContainsKey("translations") ? first["translations"] as object[] : null;
                if (trs == null || trs.Length == 0) return null;
                Dictionary<string, object> t0 = trs[0] as Dictionary<string, object>;
                if (t0 == null || !t0.ContainsKey("text")) return null;
                string r = Json.Str(t0["text"]);
                return string.IsNullOrWhiteSpace(r) ? null : r.Trim();
            }
            catch { return null; }
        }

        static string MapTarget(string t)
        {
            if (t == "zh-CN") return "zh-Hans";
            if (t == "zh-TW") return "zh-Hant";
            return t;
        }
    }

    // ============ 腾讯交互翻译（免费） ============
    public class QqProvider : TranslateProvider
    {
        public override string Name { get { return "qq"; } }

        public override Task<string> TranslateAsync(string text, string target, CancellationToken ct)
        {
            Dictionary<string, object> header = new Dictionary<string, object>();
            header["fn"] = "auto_translation";
            header["session"] = "";
            header["client_key"] = "browser-chromium-Win10-last";
            Dictionary<string, object> source = new Dictionary<string, object>();
            source["lang"] = "auto";
            source["text_list"] = new object[] { text };
            Dictionary<string, object> tgt = new Dictionary<string, object>();
            tgt["lang"] = target == "zh-CN" ? "zh" : target;
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["header"] = header;
            payload["model"] = "api";
            payload["source"] = source;
            payload["target"] = tgt;

            string body = HttpPostJson("https://transmart.qq.com/api/imt", payload, BrowserUA, "https://transmart.qq.com/");
            Dictionary<string, object> dict = Json.Parse(body) as Dictionary<string, object>;
            if (dict != null && dict.ContainsKey("auto_translation"))
            {
                object[] arr = dict["auto_translation"] as object[];
                if (arr != null && arr.Length > 0)
                {
                    string r = Json.Str(arr[0]);
                    if (!string.IsNullOrWhiteSpace(r)) return Task.FromResult(r.Trim());
                }
            }
            throw new TranslationException("qq: 响应异常");
        }
    }

    // ============ 有道（免费 demo 接口） ============
    public class YoudaoProvider : TranslateProvider
    {
        public override string Name { get { return "youdao"; } }

        public override Task<string> TranslateAsync(string text, string target, CancellationToken ct)
        {
            Dictionary<string, string> form = new Dictionary<string, string>();
            form["q"] = text;
            form["from"] = "auto";
            form["to"] = MapTarget(target);
            string body = HttpPostForm("https://aidemo.youdao.com/trans", form, BrowserUA, "https://aidemo.youdao.com/");
            Dictionary<string, object> dict = Json.Parse(body) as Dictionary<string, object>;
            if (dict != null && dict.ContainsKey("translation"))
            {
                object[] arr = dict["translation"] as object[];
                if (arr != null && arr.Length > 0)
                {
                    string r = Json.Str(arr[0]);
                    if (!string.IsNullOrWhiteSpace(r)) return Task.FromResult(r.Trim());
                }
            }
            throw new TranslationException("youdao: 响应异常");
        }

        static string MapTarget(string t)
        {
            if (t == "zh-CN") return "zh-CHS";
            if (t == "zh-TW") return "zh-CHT";
            return t;
        }
    }

    // ============ 谷歌翻译（需能访问 Google，如开了代理） ============
    public class GoogleProvider : TranslateProvider
    {
        public override string Name { get { return "google"; } }

        public override Task<string> TranslateAsync(string text, string target, CancellationToken ct)
        {
            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&dt=t&tl="
                + Uri.EscapeDataString(target) + "&q=" + Uri.EscapeDataString(text);
            string body = HttpGet(url, BrowserUA, null);
            object[] arr = Json.Parse(body) as object[];
            if (arr == null || arr.Length < 1) throw new TranslationException("google: 响应格式异常");
            object[] segs = arr[0] as object[];
            if (segs == null) throw new TranslationException("google: 响应格式异常");
            StringBuilder sb = new StringBuilder();
            foreach (object seg in segs)
            {
                object[] sa = seg as object[];
                if (sa != null && sa.Length > 0) sb.Append(Json.Str(sa[0]));
            }
            string r = sb.ToString().Trim();
            if (r.Length == 0) throw new TranslationException("google: 空结果");
            return Task.FromResult(r);
        }
    }

    // ============ MyMemory（免费，备用） ============
    public class MyMemoryProvider : TranslateProvider
    {
        public override string Name { get { return "mymemory"; } }

        public override Task<string> TranslateAsync(string text, string target, CancellationToken ct)
        {
            string src = LooksCjk(text) ? "zh-CN" : "en";
            string q = text.Length > 480 ? text.Substring(0, 480) : text;
            string url = "https://api.mymemory.translated.net/get?langpair="
                + Uri.EscapeDataString(src + "|" + target) + "&q=" + Uri.EscapeDataString(q);
            string body = HttpGet(url, BrowserUA, null);
            Dictionary<string, object> dict = Json.Parse(body) as Dictionary<string, object>;
            if (dict != null && dict.ContainsKey("responseData"))
            {
                Dictionary<string, object> rd = dict["responseData"] as Dictionary<string, object>;
                if (rd != null && rd.ContainsKey("translatedText"))
                {
                    string r = Json.Str(rd["translatedText"]);
                    if (!string.IsNullOrWhiteSpace(r)
                        && r.IndexOf("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase) < 0
                        && r != text)
                        return Task.FromResult(r.Trim());
                }
            }
            throw new TranslationException("mymemory: 无有效结果");
        }
    }

    // ============ 百度翻译（需自己在开放平台申请免费密钥） ============
    public class BaiduProvider : TranslateProvider
    {
        AppSettings _s;
        public BaiduProvider(AppSettings s) { _s = s; }

        public override string Name { get { return "baidu"; } }
        public override bool IsEnabled(AppSettings s)
        {
            return !string.IsNullOrEmpty(s.BaiduAppId) && !string.IsNullOrEmpty(s.BaiduSecret);
        }

        public override Task<string> TranslateAsync(string text, string target, CancellationToken ct)
        {
            string salt = (DateTime.UtcNow.Ticks % 100000000L).ToString(CultureInfo.InvariantCulture);
            string sign = Md5(_s.BaiduAppId + text + salt + _s.BaiduSecret);
            Dictionary<string, string> form = new Dictionary<string, string>();
            form["q"] = text;
            form["from"] = "auto";
            form["to"] = MapTarget(target);
            form["appid"] = _s.BaiduAppId;
            form["salt"] = salt;
            form["sign"] = sign;
            string body = HttpPostForm("https://fanyi-api.baidu.com/api/trans/vip/translate", form, BrowserUA, "https://fanyi-api.baidu.com/");
            Dictionary<string, object> dict = Json.Parse(body) as Dictionary<string, object>;
            if (dict != null && dict.ContainsKey("trans_result"))
            {
                object[] arr = dict["trans_result"] as object[];
                if (arr != null && arr.Length > 0)
                {
                    Dictionary<string, object> t0 = arr[0] as Dictionary<string, object>;
                    if (t0 != null && t0.ContainsKey("dst")) return Task.FromResult(Json.Str(t0["dst"]));
                }
            }
            if (dict != null && dict.ContainsKey("error_code"))
                throw new TranslationException("baidu: 错误 " + Json.Str(dict["error_code"]) + " " + (dict.ContainsKey("error_msg") ? Json.Str(dict["error_msg"]) : ""));
            throw new TranslationException("baidu: 响应异常");
        }

        static string MapTarget(string t)
        {
            if (t == "zh-CN") return "zh";
            if (t == "zh-TW") return "zt";
            if (t == "ja") return "jp";
            return t;
        }

        static string Md5(string s)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] b = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
                StringBuilder sb = new StringBuilder();
                foreach (byte x in b) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }

    // ============ DeepL（需免费注册 API Key） ============
    public class DeepLProvider : TranslateProvider
    {
        AppSettings _s;
        public DeepLProvider(AppSettings s) { _s = s; }

        public override string Name { get { return "deepl"; } }
        public override bool IsEnabled(AppSettings s) { return !string.IsNullOrEmpty(s.DeepLKey); }

        public override Task<string> TranslateAsync(string text, string target, CancellationToken ct)
        {
            string host = _s.DeepLKey.EndsWith(":fx", StringComparison.Ordinal) ? "https://api-free.deepl.com" : "https://api.deepl.com";
            string tl = target.ToUpperInvariant();
            if (tl == "ZH-CN" || tl == "ZH-TW") tl = "ZH";
            Dictionary<string, string> form = new Dictionary<string, string>();
            form["text"] = text;
            form["target_lang"] = tl;
            string body = HttpPostWithAuth(host + "/v2/translate", form, "DeepL-Auth-Key " + _s.DeepLKey);
            Dictionary<string, object> dict = Json.Parse(body) as Dictionary<string, object>;
            if (dict != null && dict.ContainsKey("translations"))
            {
                object[] arr = dict["translations"] as object[];
                if (arr != null && arr.Length > 0)
                {
                    Dictionary<string, object> t0 = arr[0] as Dictionary<string, object>;
                    if (t0 != null && t0.ContainsKey("text"))
                    {
                        string r = Json.Str(t0["text"]);
                        if (!string.IsNullOrWhiteSpace(r)) return Task.FromResult(r.Trim());
                    }
                }
            }
            throw new TranslationException("deepl: 响应异常");
        }

        string HttpPostWithAuth(string url, Dictionary<string, string> form, string auth)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in form)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value ?? ""));
            }
            HttpWebRequest req = BuildReq(url, "YiYiTranslator/1.0", null);
            req.Method = "POST";
            req.ContentType = "application/x-www-form-urlencoded";
            req.Headers.Add(HttpRequestHeader.Authorization, auth);
            byte[] data = Encoding.UTF8.GetBytes(sb.ToString());
            req.ContentLength = data.Length;
            try
            {
                using (Stream rs = req.GetRequestStream()) rs.Write(data, 0, data.Length);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse()) return ReadResp(resp);
            }
            catch (WebException ex)
            {
                throw new TranslationException(Name + ": " + DescribeWebException(ex));
            }
        }
    }

    // ============ 引擎调度：按顺序尝试，自动降级 ============
    public class EngineClient
    {
        AppSettings _s;
        readonly object _gate = new object();
        readonly Dictionary<string, string> _cache = new Dictionary<string, string>();
        DateTime _lastCall = DateTime.MinValue;

        public EngineClient(AppSettings s) { _s = s; }

        public void UpdateSettings(AppSettings s) { _s = s; }

        public static TranslateProvider Create(string name, AppSettings s)
        {
            switch (name)
            {
                case "bing": return new BingProvider();
                case "qq": return new QqProvider();
                case "youdao": return new YoudaoProvider();
                case "google": return new GoogleProvider();
                case "mymemory": return new MyMemoryProvider();
                case "baidu": return new BaiduProvider(s);
                case "deepl": return new DeepLProvider(s);
                default: return null;
            }
        }

        public List<TranslateProvider> ActiveProviders()
        {
            List<TranslateProvider> list = new List<TranslateProvider>();
            foreach (string name in _s.EngineOrder)
            {
                TranslateProvider p = Create(name, _s);
                if (p != null && p.IsEnabled(_s)) list.Add(p);
            }
            if (list.Count == 0)
                foreach (string name in new string[] { "bing", "qq", "youdao", "mymemory" })
                    list.Add(Create(name, _s));
            return list;
        }

        public async Task<string> TranslateAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            text = text.Trim();
            lock (_gate)
            {
                string hit;
                if (_cache.TryGetValue(text, out hit)) return hit;
            }
            TimeSpan since = DateTime.UtcNow - _lastCall;
            if (since < TimeSpan.FromMilliseconds(_s.RequestDelayMs))
                await Task.Delay(TimeSpan.FromMilliseconds(_s.RequestDelayMs) - since);
            string lastErr = "";
            foreach (TranslateProvider p in ActiveProviders())
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        string r = await p.TranslateAsync(text, _s.TargetLang, CancellationToken.None);
                        if (!string.IsNullOrWhiteSpace(r))
                        {
                            _lastCall = DateTime.UtcNow;
                            lock (_gate) { _cache[text] = r; }
                            return r;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastErr = p.Name + ": " + ex.Message;
                    }
                    await Task.Delay(600);
                }
            }
            _lastCall = DateTime.UtcNow;
            throw new TranslationException("所有翻译接口都失败了。最后错误 → " + lastErr);
        }
    }
}
