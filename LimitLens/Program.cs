using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LimitLens
{
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon tray;
        private readonly PopupForm popup;
        private readonly ContextMenuStrip menu;
        private readonly Icon appIcon;
        private readonly ToolStripMenuItem remainingItem;
        private readonly ToolStripMenuItem startupItem;
        private readonly Timer timer;
        private readonly Timer startupTimer;
        private readonly HistoryStore history = new HistoryStore();
        private List<ProviderSnapshot> snapshots = new List<ProviderSnapshot>();
        private bool fetching;
        private Dictionary<string, int> lastRemaining = new Dictionary<string, int>();

        public TrayApp()
        {
            appIcon = MakeIcon();
            popup = new PopupForm(Refresh, ShowMenu, appIcon);
            menu = new ContextMenuStrip();
            menu.BackColor = Color.FromArgb(25, 39, 59);
            menu.ForeColor = Color.White;
            menu.RenderMode = ToolStripRenderMode.System;
            menu.Items.Add("Aktualisieren", null, delegate { Refresh(); });
            remainingItem = new ToolStripMenuItem("Verbleibend anzeigen", null, delegate { ToggleRemaining(); }) { Checked = true };
            menu.Items.Add(remainingItem);
            startupItem = new ToolStripMenuItem("Mit Windows starten", null, delegate { ToggleStartup(); }) { Checked = IsStartupEnabled() };
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Claude verbinden", null, delegate { ConnectClaude(); });
            menu.Items.Add("Über Limit Lens", null, delegate { ShowAbout(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Beenden", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = appIcon, Text = "Limit Lens · lädt …", Visible = true, ContextMenuStrip = menu };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) TogglePopup(); };
            timer = new Timer { Interval = 5 * 60 * 1000 };
            timer.Tick += delegate { Refresh(); };
            timer.Start();
            startupTimer = new Timer { Interval = 500 };
            startupTimer.Tick += delegate
            {
                startupTimer.Stop();
                popup.ShowByTray(true);
                AppLog.Write("Start-Popup geöffnet");
            };
            startupTimer.Start();
            AppLog.Write("Limit Lens gestartet");
            Refresh();
        }

        private void TogglePopup()
        {
            if (popup.Visible) popup.Hide(); else popup.ShowByTray();
        }

        private void ShowMenu()
        {
            menu.Show(popup, new Point(popup.Width - 230, 50));
        }

        private void ToggleRemaining()
        {
            remainingItem.Checked = !remainingItem.Checked;
            popup.SetRemaining(remainingItem.Checked);
            UpdateTooltip();
        }

        private void ToggleStartup()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) throw new InvalidOperationException("Autostart-Schlüssel nicht verfügbar");
                    if (startupItem.Checked) key.DeleteValue("LimitLens", false);
                    else key.SetValue("LimitLens", "\"" + Application.ExecutablePath + "\"");
                    startupItem.Checked = !startupItem.Checked;
                }
            }
            catch (Exception ex) { MessageBox.Show("Autostart konnte nicht geändert werden:\n" + ex.Message, "Limit Lens", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private static bool IsStartupEnabled()
        {
            try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return key != null && key.GetValue("LimitLens") != null; }
            catch { return false; }
        }

        private void ConnectClaude()
        {
            // Claude Code may route status-line commands through Git Bash on Windows.
            // Forward slashes keep the executable path valid in both Git Bash and PowerShell.
            string portablePath = Application.ExecutablePath.Replace('\\', '/');
            string command = "\"" + portablePath + "\" --claude-statusline";
            try
            {
                Dictionary<string, object> settings = File.Exists(AppPaths.ClaudeSettings) ? Json.Parse(File.ReadAllText(AppPaths.ClaudeSettings)) : new Dictionary<string, object>();
                Dictionary<string, object> existing = Json.Child(settings, "statusLine");
                if (existing != null && Json.Get(existing, "command") != null)
                {
                    Clipboard.SetText(command);
                    MessageBox.Show("Claude Code nutzt bereits eine Status-Line. Der Limit-Lens-Befehl wurde in die Zwischenablage kopiert. Binde ihn in deine vorhandene Status-Line ein, damit deren Ausgabe erhalten bleibt.\n\n" + command,
                        "Claude verbinden", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult answer = MessageBox.Show("Limit Lens kann die Claude-Code-Status-Line einrichten. Claude übergibt dann seine 5h-/7d-Werte lokal an Limit Lens. Die aktuelle settings.json wird vorher gesichert.\n\nJetzt verbinden?",
                    "Claude verbinden", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;
                Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.ClaudeSettings));
                if (File.Exists(AppPaths.ClaudeSettings)) File.Copy(AppPaths.ClaudeSettings, AppPaths.ClaudeSettings + ".limitlens-backup-" + DateTime.Now.ToString("yyyyMMddHHmmss"), false);
                settings["statusLine"] = new Dictionary<string, object> { { "type", "command" }, { "command", command } };
                File.WriteAllText(AppPaths.ClaudeSettings, Json.Stringify(settings));
                MessageBox.Show("Claude ist verbunden. Die Werte erscheinen, sobald Claude Code seine Status-Line erneut aktualisiert.", "Limit Lens", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show("Claude konnte nicht verbunden werden:\n" + ex.Message, "Limit Lens", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void ShowAbout()
        {
            MessageBox.Show("Limit Lens 1.1\n\nCodex: Live-Quoten über den lokalen App-Server.\nClaude: Werte aus der Claude-Code-Status-Line.\nOpenCode Go: offizielle 5h-, Wochen- und Monatslimits.\nGemini: lokale Sitzungstokens; die Abo-Quote ist nicht verfügbar.\n\nDer lokale OpenCode-API-Key wird nur für die direkte Abfrage bei opencode.ai verwendet und nie gespeichert oder protokolliert.",
                "Über Limit Lens", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async void Refresh()
        {
            if (fetching) return;
            fetching = true;
            popup.SetData(snapshots, history.LastDay(), true);
            try
            {
                IUsageProvider[] providers = { new ClaudeProvider(), new CodexProvider(), new OpenCodeGoProvider(), new GeminiProvider() };
                Task<ProviderSnapshot>[] tasks = providers.Select(p => Task.Run(() => p.Fetch())).ToArray();
                ProviderSnapshot[] result = await Task.WhenAll(tasks);
                snapshots = result.ToList();
                history.Add(snapshots);
                popup.SetData(snapshots, history.LastDay(), false);
                UpdateTooltip();
                CheckNotifications();
            }
            catch { }
            finally { fetching = false; }
        }

        private void UpdateTooltip()
        {
            string[] values = snapshots.Select(s => s.Name + " " + Formatters.Percent(s.Primary == null ? null : s.Primary.UsedPercent, popup.IsRemaining)).ToArray();
            string line = values.Length == 0 ? "Limit Lens · lädt …" : string.Join(" · ", values);
            if (line.Length > 63) line = line.Substring(0, 63);
            tray.Text = line;
        }

        private void CheckNotifications()
        {
            foreach (ProviderSnapshot provider in snapshots)
            {
                UsageWindow primary = provider.Primary;
                if (primary == null || !primary.UsedPercent.HasValue) continue;
                int current = primary.RemainingPercent;
                int previous;
                if (lastRemaining.TryGetValue(provider.Name, out previous) && previous > 20 && current <= 20)
                {
                    tray.BalloonTipTitle = provider.Name + ": wenig Kontingent";
                    tray.BalloonTipText = current + "% verbleibend · " + Formatters.Until(primary.ResetsAt);
                    tray.ShowBalloonTip(5000);
                }
                lastRemaining[provider.Name] = current;
            }
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            startupTimer.Stop();
            tray.Visible = false;
            tray.Dispose();
            popup.Dispose();
            menu.Dispose();
            appIcon.Dispose();
            base.ExitThreadCore();
        }

        private static Icon MakeIcon()
        {
            try
            {
                using (Icon associated = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    if (associated != null) return (Icon)associated.Clone();
            }
            catch { }

            using (var bitmap = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var brush = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(89, 141, 242), Color.FromArgb(36, 68, 139), 45f)) g.FillEllipse(brush, 2, 2, 28, 28);
                using (var p = new Pen(Color.White, 3f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawArc(p, 8, 8, 16, 16, 215, 250);
                    g.DrawLine(p, 16, 16, 21, 11);
                }
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon borrowed = Icon.FromHandle(handle))
                        return (Icon)borrowed.Clone();
                }
                finally { DestroyIcon(handle); }
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);
    }

    internal static class Program
    {
        private static System.Threading.Mutex instanceMutex;

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--claude-statusline") { ClaudeProvider.CaptureStatusLine(); return; }
            if (args.Length > 1 && args[0] == "--preview") { PopupForm.RenderPreview(args[1]); return; }
            if (args.Length > 1 && args[0] == "--diagnostics") { WriteDiagnostics(args[1]); return; }
            bool firstInstance;
            instanceMutex = new System.Threading.Mutex(true, "Local\\LimitLens.Singleton", out firstInstance);
            if (!firstInstance)
            {
                MessageBox.Show("Limit Lens läuft bereits. Öffne rechts unten den Infobereich der Taskleiste und klicke auf das AI-Symbol.", "Limit Lens", MessageBoxButtons.OK, MessageBoxIcon.Information);
                instanceMutex.Dispose();
                return;
            }
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
            catch (Exception ex)
            {
                AppLog.Write("Schwerer Startfehler: " + ex);
                MessageBox.Show("Limit Lens konnte nicht gestartet werden.\n\n" + ex.Message + "\n\nProtokoll: " + AppPaths.Log, "Limit Lens", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                try { instanceMutex.ReleaseMutex(); } catch { }
                instanceMutex.Dispose();
            }
        }

        private static void WriteDiagnostics(string path)
        {
            var result = new List<Dictionary<string, object>>();
            IUsageProvider[] providers = { new ClaudeProvider(), new CodexProvider(), new OpenCodeGoProvider(), new GeminiProvider() };
            foreach (IUsageProvider provider in providers)
            {
                ProviderSnapshot snap = provider.Fetch();
                var windows = new List<Dictionary<string, object>>();
                foreach (UsageWindow w in snap.Windows)
                    windows.Add(new Dictionary<string, object> { { "label", w.Label }, { "usedPercent", w.UsedPercent }, { "reset", w.ResetsAt.HasValue ? w.ResetsAt.Value.ToString("o") : null }, { "detail", w.Detail } });
                result.Add(new Dictionary<string, object> { { "provider", snap.Name }, { "status", snap.Status }, { "windows", windows } });
            }
            File.WriteAllText(path, Json.Stringify(new Dictionary<string, object> { { "providers", result }, { "geminiRoot", AppPaths.GeminiRoot }, { "geminiRootExists", Directory.Exists(AppPaths.GeminiRoot) } }));
        }
    }
}
