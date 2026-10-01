using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
                TestResponseLimit();
                TestProviderCatalog();
                TestLogRedaction();
                TestDiagnosticsRedaction(dataDir);
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
            MethodInfo policy = typeof(OpenCodeGoProvider).GetMethod("TryGetAllowedEndpoint", BindingFlags.NonPublic | BindingFlags.Static);
            string[] blocked =
            {
                "http://opencode.ai/zen/go/v1/usage",
                "https://example.com/usage",
                "https://evil.opencode.ai/usage",
                "https://opencode.ai:8443/usage",
                "https://user:password@opencode.ai/usage"
            };
            foreach (string value in blocked)
            {
                object[] arguments = { value, null };
                Assert(!(bool)policy.Invoke(null, arguments), "Endpoint blockiert: " + value);
            }
            object[] allowedArguments = { "https://opencode.ai/zen/go/v1/usage", null };
            Assert((bool)policy.Invoke(null, allowedArguments), "Offizieller Endpoint erlaubt");

            Environment.SetEnvironmentVariable("LIMITLENS_OPENCODE_API_KEY", "test-key");
            Environment.SetEnvironmentVariable("LIMITLENS_OPENCODE_USAGE_URL", "https://example.com/usage");
            ProviderSnapshot snapshot = new OpenCodeGoProvider().Fetch();
            Assert(snapshot.Status == "OpenCode-Endpoint nicht zugelassen", "Fremder OpenCode-Endpoint wird blockiert");
            Environment.SetEnvironmentVariable("LIMITLENS_OPENCODE_API_KEY", null);
            Environment.SetEnvironmentVariable("LIMITLENS_OPENCODE_USAGE_URL", null);
        }

        private static void TestResponseLimit()
        {
            MethodInfo readerMethod = typeof(OpenCodeGoProvider).GetMethod("ReadLimited", BindingFlags.NonPublic | BindingFlags.Static);
            string oversized = new string('x', 1024 * 1024 + 1);
            bool rejected = false;
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(oversized)))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    readerMethod.Invoke(null, new object[] { reader, 1024 * 1024 });
            }
            catch (TargetInvocationException ex)
            {
                rejected = ex.InnerException is InvalidDataException;
            }
            Assert(rejected, "Übergroße API-Antwort wird abgewiesen");
        }

        private static void TestProviderCatalog()
        {
            IUsageProvider[] providers = ProviderCatalog.Create();
            Assert(providers.Length == 4, "Provider-Katalog enthält alle Adapter");
            Assert(providers.Select(p => p.GetType().Name).Distinct().Count() == 4, "Provider-Katalog enthält keine Duplikate");
        }

        private static void TestLogRedaction()
        {
            string value = "super-secret-token";
            AppLog.Write("Authorization: Bearer " + value + " api_key=" + value);
            string log = File.ReadAllText(AppPaths.Log);
            Assert(!log.Contains(value), "Secrets werden aus Logs entfernt");
            Assert(log.Contains("[REDACTED]"), "Redaction-Markierung wird geschrieben");
            AppLog.Write(new string('z', 5000));
            log = File.ReadAllText(AppPaths.Log);
            Assert(log.Contains("[TRUNCATED]"), "Überlange Logs werden gekürzt");
        }

        private static void TestDiagnosticsRedaction(string dataDir)
        {
            string secret = "diagnostic-secret-value";
            string output = Path.Combine(dataDir, "diagnostics.json");
            Environment.SetEnvironmentVariable("LIMITLENS_OPENCODE_API_KEY", secret);
            Environment.SetEnvironmentVariable("LIMITLENS_OPENCODE_USAGE_URL", "https://example.com/usage");
            MethodInfo writer = typeof(Program).GetMethod("WriteDiagnostics", BindingFlags.NonPublic | BindingFlags.Static);
            writer.Invoke(null, new object[] { output });
            string diagnostics = File.ReadAllText(output, Encoding.UTF8);
            Assert(!diagnostics.Contains(secret), "Diagnoseausgabe enthält keinen OpenCode-Key");
            Assert(!diagnostics.Contains("Bearer"), "Diagnoseausgabe enthält keinen Authorization-Header");
            Environment.SetEnvironmentVariable("LIMITLENS_OPENCODE_API_KEY", null);
            Environment.SetEnvironmentVariable("LIMITLENS_OPENCODE_USAGE_URL", null);
        }

        private static void Assert(bool condition, string name)
        {
            if (condition) { Console.WriteLine("PASS " + name); return; }
            failures++;
            Console.WriteLine("FAIL " + name);
        }
    }
}
