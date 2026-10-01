using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LimitLens
{
    internal static class Json
    {
        private static readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 4000000 };
        public static Dictionary<string, object> Parse(string text) { return serializer.DeserializeObject(text) as Dictionary<string, object>; }
        public static string Stringify(object value) { return serializer.Serialize(value); }
        public static Dictionary<string, object> Obj(object value) { return value as Dictionary<string, object>; }
        public static object Get(Dictionary<string, object> obj, string key) { object result; return obj != null && obj.TryGetValue(key, out result) ? result : null; }
        public static Dictionary<string, object> Child(Dictionary<string, object> obj, string key) { return Obj(Get(obj, key)); }
        public static string Str(object value) { return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture); }
        public static double? Number(object value)
        {
            if (value == null) return null;
            double number;
            return double.TryParse(Str(value), NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? (double?)number : null;
        }
        public static DateTimeOffset? Reset(object value)
        {
            if (value == null) return null;
            double? numeric = Number(value);
            if (numeric.HasValue && numeric.Value > 1000000000 && numeric.Value < 10000000000)
                return DateTimeOffset.FromUnixTimeSeconds((long)numeric.Value);
            DateTimeOffset parsed;
            return DateTimeOffset.TryParse(Str(value), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed) ? (DateTimeOffset?)parsed : null;
        }
    }

    internal interface IUsageProvider { ProviderSnapshot Fetch(); }

    internal sealed class CodexProvider : IUsageProvider
    {
        public ProviderSnapshot Fetch()
        {
            var snap = new ProviderSnapshot { Name = "Codex", Mark = "◎", Status = "Codex CLI nicht erreichbar", Source = "Codex App-Server", ObservedAt = DateTimeOffset.Now };
            Process process = null;
            try
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CODEX_HOME")))
                    Environment.SetEnvironmentVariable("CODEX_HOME", Path.Combine(AppPaths.Profile, ".codex"));
                var start = new ProcessStartInfo(ResolveCodexExe(), "app-server --stdio")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = false, StandardOutputEncoding = Encoding.UTF8
                };
                process = Process.Start(start);
                if (process == null) return snap;
                process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"limit_lens\",\"title\":\"Limit Lens\",\"version\":\"1.0.0\"}}}");
                process.StandardInput.Flush();
                if (ReadResponse(process, 1, 7000) == null) { snap.Status = "Codex antwortet nicht"; return snap; }
                process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"initialized\",\"params\":{}}");
                process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":{}}");
                process.StandardInput.Flush();
                Dictionary<string, object> response = ReadResponse(process, 2, 9000);
                Dictionary<string, object> result = Json.Child(response, "result");
                if (result == null) { snap.Status = "Keine Quotendaten von Codex"; return snap; }
                Dictionary<string, object> byId = Json.Child(result, "rateLimitsByLimitId");
                Dictionary<string, object> rates = Json.Child(byId, "codex") ?? Json.Child(result, "rateLimits");
                if (rates == null) { snap.Status = "Keine Codex-Limits gemeldet"; return snap; }
                AddWindow(snap, Json.Child(rates, "primary"));
                AddWindow(snap, Json.Child(rates, "secondary"));
                snap.Windows.Sort((a, b) => Rank(a.Label).CompareTo(Rank(b.Label)));
                snap.Status = snap.Windows.Count > 0 ? "Live · Codex App-Server" : "Codex meldet keine aktiven Fenster";
            }
            catch (Exception ex) { snap.Status = "Codex: " + Friendly(ex); }
            finally
            {
                if (process != null)
                {
                    try { if (!process.HasExited) process.Kill(); } catch { }
                    process.Dispose();
                }
            }
            return snap;
        }

        private static int Rank(string label) { return label == "5 Stunden" ? 0 : label == "Woche" ? 1 : 2; }
        private static void AddWindow(ProviderSnapshot snap, Dictionary<string, object> raw)
        {
            if (raw == null) return;
            double? percent = Json.Number(Json.Get(raw, "usedPercent"));
            if (!percent.HasValue || percent.Value < 0 || percent.Value > 100) return;
            double? minutes = Json.Number(Json.Get(raw, "windowDurationMins"));
            string label = !minutes.HasValue ? "Limit" : minutes.Value >= 10000 ? "Woche" : minutes.Value >= 290 && minutes.Value <= 310 ? "5 Stunden" : minutes.Value >= 60 ? (minutes.Value / 60).ToString("0.#", CultureInfo.GetCultureInfo("de-DE")) + " Stunden" : minutes.Value.ToString("0") + " Minuten";
            snap.Windows.Add(new UsageWindow { Label = label, UsedPercent = percent, ResetsAt = Json.Reset(Json.Get(raw, "resetsAt")) });
        }

        private static Dictionary<string, object> ReadResponse(Process process, int id, int timeoutMs)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                Task<string> read = process.StandardOutput.ReadLineAsync();
                int left = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                if (!read.Wait(left)) return null;
                string line = read.Result;
                if (line == null) return null;
                Dictionary<string, object> msg;
                try { msg = Json.Parse(line); } catch { continue; }
                if (Json.Number(Json.Get(msg, "id")) == id) return msg;
            }
            return null;
        }

        private static string Friendly(Exception ex)
        {
            if (ex is System.ComponentModel.Win32Exception) return "CLI nicht gefunden";
            return "Abfrage fehlgeschlagen";
        }

        private static string ResolveCodexExe()
        {
            string configured = Environment.GetEnvironmentVariable("LIMITLENS_CODEX_EXE");
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
            string appBin = Path.Combine(AppPaths.LocalData, "OpenAI", "Codex", "bin");
            if (Directory.Exists(appBin))
            {
                string[] candidates = Directory.GetFiles(appBin, "codex.exe", SearchOption.AllDirectories);
                if (candidates.Length > 0) return candidates.OrderByDescending(File.GetLastWriteTime).First();
            }
            return "codex.exe";
        }
    }

    internal sealed class ClaudeProvider : IUsageProvider
    {
        public ProviderSnapshot Fetch()
        {
            var snap = new ProviderSnapshot { Name = "Claude", Mark = "✳", Source = "Claude Code Status-Line", ObservedAt = DateTimeOffset.Now, Status = "Status-Line noch nicht verbunden" };
            try
            {
                if (!File.Exists(AppPaths.ClaudeCache)) return snap;
                DateTime modified = File.GetLastWriteTime(AppPaths.ClaudeCache);
                if (DateTime.Now - modified > TimeSpan.FromHours(12)) { snap.Status = "Status-Line-Daten veraltet"; return snap; }
                Dictionary<string, object> raw = Json.Parse(File.ReadAllText(AppPaths.ClaudeCache, Encoding.UTF8));
                AddWindow(snap, Json.Child(raw, "five_hour"), "5 Stunden");
                AddWindow(snap, Json.Child(raw, "seven_day"), "Woche");
                snap.ObservedAt = new DateTimeOffset(modified);
                snap.Status = snap.Windows.Count > 0 ? "Claude Code · zuletzt " + modified.ToString("HH:mm") : "Keine Abo-Limits in Status-Line";
            }
            catch { snap.Status = "Claude-Daten konnten nicht gelesen werden"; }
            return snap;
        }

        private static void AddWindow(ProviderSnapshot snap, Dictionary<string, object> raw, string label)
        {
            if (raw == null) return;
            double? percent = Json.Number(Json.Get(raw, "used_percentage"));
            if (!percent.HasValue || percent.Value < 0 || percent.Value > 100) return;
            snap.Windows.Add(new UsageWindow { Label = label, UsedPercent = percent, ResetsAt = Json.Reset(Json.Get(raw, "resets_at")) });
        }

        public static void CaptureStatusLine()
        {
            try
            {
                string input = Console.In.ReadToEnd();
                Dictionary<string, object> obj = Json.Parse(input);
                Dictionary<string, object> limits = Json.Child(obj, "rate_limits");
                if (limits == null) return;
                var safe = new Dictionary<string, object>();
                foreach (string key in new[] { "five_hour", "seven_day" })
                {
                    Dictionary<string, object> part = Json.Child(limits, key);
                    if (part == null) continue;
                    double? percent = Json.Number(Json.Get(part, "used_percentage"));
                    if (!percent.HasValue || percent.Value < 0 || percent.Value > 100) continue;
                    safe[key] = new Dictionary<string, object> { { "used_percentage", percent.Value }, { "resets_at", Json.Get(part, "resets_at") } };
                }
                if (safe.Count == 0) { Console.WriteLine("Claude · Quoten noch nicht verfügbar"); return; }
                Directory.CreateDirectory(AppPaths.Data);
                string temp = AppPaths.ClaudeCache + ".tmp." + Process.GetCurrentProcess().Id;
                File.WriteAllText(temp, Json.Stringify(safe), Encoding.UTF8);
                if (File.Exists(AppPaths.ClaudeCache)) File.Delete(AppPaths.ClaudeCache);
                File.Move(temp, AppPaths.ClaudeCache);
                Console.WriteLine("Claude · " + (safe.ContainsKey("five_hour") ? Formatters.Percent(Json.Number(Json.Get(Json.Obj(safe["five_hour"]), "used_percentage")), true) + " frei" : "aktiv"));
            }
            catch { }
        }
    }

    internal sealed class OpenCodeGoProvider : IUsageProvider
    {
        private const string DefaultUsageUrl = "https://opencode.ai/zen/go/v1/usage";
        private const int MaxResponseBytes = 1024 * 1024;
        private const int MaxAttempts = 2;
        private const int RetryDelayMs = 250;

        public ProviderSnapshot Fetch()
        {
            var snap = new ProviderSnapshot
            {
                Name = "OpenCode Go",
                Mark = "GO",
                Source = "OpenCode Go Usage API",
                ObservedAt = DateTimeOffset.Now,
                Status = "OpenCode Go nicht verbunden"
            };

            string apiKey = FindApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                snap.Status = "In OpenCode zuerst /connect ausführen";
                return snap;
            }

            try
            {
                string configuredUrl = Environment.GetEnvironmentVariable("LIMITLENS_OPENCODE_USAGE_URL");
                string url = string.IsNullOrWhiteSpace(configuredUrl) ? DefaultUsageUrl : configuredUrl;
                Uri endpoint;
                if (!TryGetAllowedEndpoint(url, out endpoint))
                {
                    snap.Status = "OpenCode-Endpoint nicht zugelassen";
                    return snap;
                }

                string body = FetchUsageBody(endpoint, apiKey);

                Dictionary<string, object> usage = Json.Child(Json.Parse(body), "usage");
                if (usage == null)
                {
                    snap.Status = "OpenCode meldet keine Abo-Limits";
                    return snap;
                }

                AddWindow(snap, Json.Child(usage, "rolling"), "5 Stunden");
                AddWindow(snap, Json.Child(usage, "weekly"), "Woche");
                AddWindow(snap, Json.Child(usage, "monthly"), "Monat");
                snap.Status = snap.Windows.Count > 0 ? "Live · offizielles Go-Limit" : "OpenCode meldet keine aktiven Fenster";
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                snap.Status = response != null && response.StatusCode == HttpStatusCode.Unauthorized
                    ? "OpenCode-Key nicht gültig"
                    : "OpenCode Go derzeit nicht erreichbar";
            }
            catch { snap.Status = "OpenCode-Daten konnten nicht gelesen werden"; }
            return snap;
        }

        private static string FetchUsageBody(Uri endpoint, string apiKey)
        {
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(endpoint);
                    request.Method = "GET";
                    request.Accept = "application/json";
                    request.UserAgent = AppInfo.UserAgent;
                    request.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiKey;
                    request.Timeout = 9000;
                    request.ReadWriteTimeout = 9000;
                    request.AllowAutoRedirect = false;

                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        return ReadLimited(reader, MaxResponseBytes);
                }
                catch (WebException ex)
                {
                    HttpWebResponse response = ex.Response as HttpWebResponse;
                    bool retry = response == null || IsTransient(response.StatusCode);
                    if (response != null) response.Dispose();
                    if (!retry || attempt == MaxAttempts) throw;
                    System.Threading.Thread.Sleep(RetryDelayMs);
                }
            }
            throw new InvalidOperationException("OpenCode-Abruf fehlgeschlagen.");
        }

        private static bool IsTransient(HttpStatusCode statusCode)
        {
            int code = (int)statusCode;
            return statusCode == HttpStatusCode.RequestTimeout || statusCode == (HttpStatusCode)429 || code >= 500;
        }

        private static bool TryGetAllowedEndpoint(string value, out Uri endpoint)
        {
            endpoint = null;
            Uri parsed;
            if (!Uri.TryCreate(value, UriKind.Absolute, out parsed)) return false;
            if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(parsed.Host, "opencode.ai", StringComparison.OrdinalIgnoreCase)) return false;
            if (!parsed.IsDefaultPort) return false;
            if (!string.IsNullOrEmpty(parsed.UserInfo)) return false;
            endpoint = parsed;
            return true;
        }

        private static string ReadLimited(StreamReader reader, int maxBytes)
        {
            var builder = new StringBuilder();
            char[] buffer = new char[4096];
            int bytes = 0;
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                bytes += Encoding.UTF8.GetByteCount(buffer, 0, read);
                if (bytes > maxBytes) throw new InvalidDataException("OpenCode-Antwort ist zu groß.");
                builder.Append(buffer, 0, read);
            }
            return builder.ToString();
        }

        private static void AddWindow(ProviderSnapshot snap, Dictionary<string, object> raw, string label)
        {
            if (raw == null) return;
            double? percent = Json.Number(Json.Get(raw, "percent"));
            if (!percent.HasValue || percent.Value < 0 || percent.Value > 100) return;
            snap.Windows.Add(new UsageWindow
            {
                Label = label,
                UsedPercent = percent,
                ResetsAt = Json.Reset(Json.Get(raw, "resetsAt"))
            });
        }

        private static string FindApiKey()
        {
            string configured = Environment.GetEnvironmentVariable("LIMITLENS_OPENCODE_API_KEY");
            if (string.IsNullOrWhiteSpace(configured)) configured = Environment.GetEnvironmentVariable("OPENCODE_API_KEY");
            if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();

            string databaseKey = FindV2DatabaseApiKey();
            if (!string.IsNullOrWhiteSpace(databaseKey)) return databaseKey;

            try
            {
                if (!File.Exists(AppPaths.OpenCodeAuth)) return null;
                Dictionary<string, object> auth = Json.Parse(File.ReadAllText(AppPaths.OpenCodeAuth, Encoding.UTF8));
                foreach (string provider in new[] { "opencode-go", "opencode" })
                {
                    Dictionary<string, object> entry = Json.Child(auth, provider);
                    if (entry == null || !string.Equals(Json.Str(Json.Get(entry, "type")), "api", StringComparison.OrdinalIgnoreCase)) continue;
                    string key = Json.Str(Json.Get(entry, "key"));
                    if (!string.IsNullOrWhiteSpace(key)) return key.Trim();
                }
            }
            catch { }
            return null;
        }

        private static string FindV2DatabaseApiKey()
        {
            if (!File.Exists(AppPaths.OpenCodeDatabase)) return null;
            Process process = null;
            try
            {
                const string script = "const{DatabaseSync}=require('node:sqlite');const d=new DatabaseSync(process.argv[1],{readOnly:true});try{const r=d.prepare(\"SELECT value FROM credential WHERE integration_id IN ('opencode-go','opencode') ORDER BY active DESC,time_updated DESC LIMIT 1\").get();if(r){const v=JSON.parse(r.value);if(v&&typeof v.key==='string')process.stdout.write(v.key)}}finally{d.close()}";
                var start = new ProcessStartInfo("node.exe", "--no-warnings -e \"" + script.Replace("\"", "\\\"") + "\" \"" + AppPaths.OpenCodeDatabase.Replace("\"", "\\\"") + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                process = Process.Start(start);
                if (process == null) return null;
                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(); } catch { }
                    return null;
                }
                string key = process.StandardOutput.ReadToEnd();
                return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(key) ? key.Trim() : null;
            }
            catch { return null; }
            finally { if (process != null) process.Dispose(); }
        }
    }

    internal sealed class GeminiProvider : IUsageProvider
    {
        public ProviderSnapshot Fetch()
        {
            var snap = new ProviderSnapshot { Name = "Gemini", Mark = "✦", Source = "Lokale Gemini-CLI-Sitzung", ObservedAt = DateTimeOffset.Now, Status = "Keine lokale Gemini-Sitzung" };
            try
            {
                if (!Directory.Exists(AppPaths.GeminiRoot)) return snap;
                string latest = null;
                DateTime stamp = DateTime.MinValue;
                foreach (string file in Directory.GetFiles(AppPaths.GeminiRoot, "session-*.json", SearchOption.AllDirectories))
                {
                    DateTime m = File.GetLastWriteTime(file);
                    if (m > stamp) { latest = file; stamp = m; }
                }
                if (latest == null) return snap;
                Dictionary<string, object> session = Json.Parse(File.ReadAllText(latest, Encoding.UTF8));
                object[] messages = Json.Get(session, "messages") as object[];
                long total = 0;
                if (messages != null)
                    foreach (object value in messages)
                    {
                        Dictionary<string, object> msg = Json.Obj(value);
                        double? count = Json.Number(Json.Get(Json.Child(msg, "tokens"), "total"));
                        if (count.HasValue && count.Value >= 0) total += (long)count.Value;
                    }
                snap.ObservedAt = new DateTimeOffset(stamp);
                snap.Windows.Add(new UsageWindow { Label = "Letzte Sitzung", Detail = Formatters.CompactNumber(total) + " Tokens lokal", UsedPercent = null });
                snap.Status = "Abo-Quote nicht verfügbar · " + stamp.ToString("dd.MM. HH:mm");
            }
            catch { snap.Status = "Gemini-Sitzung nicht lesbar"; }
            return snap;
        }
    }
}
