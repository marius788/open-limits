using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LimitLens
{
    internal static class AppInfo
    {
        public const string Version = "1.2.0";
        public const string ProductName = "Limit Lens";
        public const string UserAgent = "LimitLens/" + Version;
    }

    internal sealed class UsageWindow
    {
        public string Label;
        public double? UsedPercent;
        public DateTimeOffset? ResetsAt;
        public string Detail;

        public int RemainingPercent
        {
            get { return UsedPercent.HasValue ? (int)Math.Round(Math.Max(0, Math.Min(100, 100 - UsedPercent.Value))) : -1; }
        }
    }

    internal sealed class ProviderSnapshot
    {
        public string Name;
        public string Mark;
        public string Status;
        public string Source;
        public DateTimeOffset ObservedAt;
        public readonly List<UsageWindow> Windows = new List<UsageWindow>();

        public UsageWindow Primary
        {
            get { return Windows.Count > 0 ? Windows[0] : null; }
        }
    }

    internal static class ProviderCatalog
    {
        public static IUsageProvider[] Create()
        {
            return new IUsageProvider[] { new ClaudeProvider(), new CodexProvider(), new OpenCodeGoProvider(), new GeminiProvider() };
        }
    }

    internal static class AppPaths
    {
        public static readonly string Profile = Environment.GetEnvironmentVariable("USERPROFILE") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public static readonly string LocalData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public static readonly string Data = Environment.GetEnvironmentVariable("LIMITLENS_DATA_DIR") ?? Path.Combine(LocalData, "LimitLens");
        public static readonly string ClaudeCache = Path.Combine(Data, "claude-status.json");
        public static readonly string History = Path.Combine(Data, "history.csv");
        public static readonly string Log = Path.Combine(Data, "limitlens.log");
        public static readonly string ClaudeSettings = Path.Combine(Profile, ".claude", "settings.json");
        public static readonly string GeminiRoot = Environment.GetEnvironmentVariable("LIMITLENS_GEMINI_ROOT") ?? Path.Combine(Profile, ".gemini", "tmp");
        public static readonly string OpenCodeAuth = Environment.GetEnvironmentVariable("LIMITLENS_OPENCODE_AUTH") ?? Path.Combine(Profile, ".local", "share", "opencode", "auth.json");
        public static readonly string OpenCodeDatabase = Environment.GetEnvironmentVariable("LIMITLENS_OPENCODE_DB") ?? Path.Combine(Profile, ".local", "share", "opencode", "opencode.db");
    }

    internal static class AppLog
    {
        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Data);
                string safeMessage = message ?? string.Empty;
                safeMessage = Regex.Replace(safeMessage, "(?i)(Bearer\\s+)[^\\s\"']+", "$1[REDACTED]");
                safeMessage = Regex.Replace(safeMessage, "(?i)((?:api[_-]?key|access[_-]?token|secret)\\s*[:=]\\s*)[^\\s,;\"']+", "$1[REDACTED]");
                File.AppendAllText(AppPaths.Log, DateTimeOffset.Now.ToString("o") + "  " + safeMessage + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }

    internal static class Formatters
    {
        public static string Percent(double? value, bool remaining)
        {
            if (!value.HasValue) return "—";
            double shown = remaining ? 100 - value.Value : value.Value;
            return Math.Round(Math.Max(0, Math.Min(100, shown))).ToString(CultureInfo.InvariantCulture) + "%";
        }

        public static string Until(DateTimeOffset? reset)
        {
            if (!reset.HasValue) return "Reset unbekannt";
            TimeSpan span = reset.Value - DateTimeOffset.Now;
            if (span.TotalSeconds <= 0) return "Reset fällig";
            if (span.TotalDays >= 1) return "Reset in " + (int)span.TotalDays + "T " + span.Hours + "h";
            if (span.TotalHours >= 1) return "Reset in " + (int)span.TotalHours + "h " + span.Minutes.ToString("00") + "m";
            return "Reset in " + Math.Max(1, (int)Math.Ceiling(span.TotalMinutes)) + "m";
        }

        public static string CompactNumber(long number)
        {
            if (number >= 1000000) return (number / 1000000.0).ToString("0.#", CultureInfo.GetCultureInfo("de-DE")) + " Mio.";
            if (number >= 1000) return (number / 1000.0).ToString("0.#", CultureInfo.GetCultureInfo("de-DE")) + " Tsd.";
            return number.ToString("N0", CultureInfo.GetCultureInfo("de-DE"));
        }
    }

    internal sealed class HistoryStore
    {
        private readonly List<Tuple<DateTimeOffset, string, double>> entries = new List<Tuple<DateTimeOffset, string, double>>();

        public HistoryStore()
        {
            try
            {
                if (!File.Exists(AppPaths.History)) return;
                foreach (string line in File.ReadAllLines(AppPaths.History, Encoding.UTF8))
                {
                    string[] parts = line.Split(',');
                    long unix;
                    double value;
                    if (parts.Length == 3 && long.TryParse(parts[0], out unix) && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                        entries.Add(Tuple.Create(DateTimeOffset.FromUnixTimeSeconds(unix), parts[1], value));
                }
            }
            catch { }
        }

        public void Add(IEnumerable<ProviderSnapshot> snapshots)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            foreach (ProviderSnapshot provider in snapshots)
            {
                UsageWindow window = provider.Primary;
                if (window == null || !window.UsedPercent.HasValue) continue;
                if (entries.Exists(e => e.Item2 == provider.Name && now - e.Item1 < TimeSpan.FromMinutes(4))) continue;
                entries.Add(Tuple.Create(now, provider.Name, window.UsedPercent.Value));
            }
            entries.RemoveAll(e => now - e.Item1 > TimeSpan.FromDays(7));
            try
            {
                Directory.CreateDirectory(AppPaths.Data);
                using (StreamWriter writer = new StreamWriter(AppPaths.History, false, Encoding.UTF8))
                    foreach (var e in entries)
                        writer.WriteLine(e.Item1.ToUnixTimeSeconds() + "," + e.Item2 + "," + e.Item3.ToString(CultureInfo.InvariantCulture));
            }
            catch { }
        }

        public double?[] LastDay()
        {
            double?[] hours = new double?[24];
            DateTimeOffset now = DateTimeOffset.Now;
            for (int hour = 0; hour < 24; hour++)
            {
                DateTimeOffset start = now.AddHours(hour - 24);
                DateTimeOffset end = start.AddHours(1);
                double sum = 0;
                int count = 0;
                foreach (var entry in entries)
                    if (entry.Item1 >= start && entry.Item1 < end) { sum += entry.Item3; count++; }
                if (count > 0) hours[hour] = sum / count;
            }
            return hours;
        }
    }
}
