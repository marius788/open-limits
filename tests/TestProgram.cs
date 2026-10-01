using System;
using System.IO;
using System.Reflection;
using LimitLens;

namespace LimitLensTests
{
    internal static class TestProgram
    {
        private static int failures;

        private static void Main()
        {
            string dataDir = Path.Combine(Path.GetTempPath(), "LimitLens-tests-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("LIMITLENS_DATA_DIR", dataDir);
            try
            {
                TestJsonParsing();
                TestFormatting();
                TestEndpointPolicy();
                TestLogRedaction();
            }
            finally
            {
                try { if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); } catch { }
            }

            if (failures > 0) throw new InvalidOperationException(failures + " Test(s) fehlgeschlagen.");
            Console.WriteLine("Alle Limit-Lens-Tests erfolgreich.");
        }

        private static void TestJsonParsing()
        {
            var root = Json.Parse("{\"usage\":{\"rolling\":{\"percent\":12.5,\"resetsAt\":1720000000}}}");
            Assert(Json.Number(Json.Get(Json.Child(Json.Child(root, "usage"), "rolling"), "percent")) == 12.5, "JSON Prozentwert");
            Assert(Json.Reset(1720000000).HasValue, "Unix-Resetzeit");
        }

        private static void TestFormatting()
        {
            Assert(Formatters.Percent(25, true) == "75%", "Rest-Prozentwert");
            Assert(Formatters.Percent(25, false) == "25%", "Verbrauch-Prozentwert");
            Assert(Formatters.CompactNumber(1200) == "1,2 Tsd.", "Kompakte Zahl");
        }

        private static void TestEndpointPolicy()
        {
            Environment.SetEnvironmentVariable("LIMITLENS_OPENCODE_API_KEY", "test-key");
            Environment.SetEnvironmentVariable("LIMITLENS_OPENCODE_USAGE_URL", "https://example.com/usage");
            ProviderSnapshot snapshot = new OpenCodeGoProvider().Fetch();
            Assert(snapshot.Status == "OpenCode-Endpoint nicht zugelassen", "Fremder OpenCode-Endpoint wird blockiert");
            Environment.SetEnvironmentVariable("LIMITLENS_OPENCODE_API_KEY", null);
            Environment.SetEnvironmentVariable("LIMITLENS_OPENCODE_USAGE_URL", null);
        }

        private static void TestLogRedaction()
        {
            string value = "super-secret-token";
            AppLog.Write("Authorization: Bearer " + value + " api_key=" + value);
            string log = File.ReadAllText(AppPaths.Log);
            Assert(!log.Contains(value), "Secrets werden aus Logs entfernt");
            Assert(log.Contains("[REDACTED]"), "Redaction-Markierung wird geschrieben");
        }

        private static void Assert(bool condition, string name)
        {
            if (condition) { Console.WriteLine("PASS " + name); return; }
            failures++;
            Console.WriteLine("FAIL " + name);
        }
    }
}
