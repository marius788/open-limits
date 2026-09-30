using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace LimitLens
{
    internal sealed class PopupForm : Form
    {
        public const int PanelWidth = 472;
        public const int PanelHeight = 704;
        private readonly Action refresh;
        private readonly Action showMenu;
        private List<ProviderSnapshot> snapshots = new List<ProviderSnapshot>();
        private double?[] history = new double?[24];
        private bool remaining = true;
        private bool loading;
        private bool startupPinned;
        private readonly float dpiScale;
        private DateTimeOffset? updated;
        private int hoveredAction;

        private readonly Font titleFont = new Font("Segoe UI Semibold", 18, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font nameFont = new Font("Segoe UI Semibold", 15, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font bodyFont = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font smallFont = new Font("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font microFont = new Font("Segoe UI", 10, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font valueFont = new Font("Segoe UI Semibold", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font markFont = new Font("Segoe UI Symbol", 25, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font badgeFont = new Font("Segoe UI Semibold", 12, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font commandIconFont = new Font("Segoe Fluent Icons", 17, FontStyle.Regular, GraphicsUnit.Pixel);

        public PopupForm(Action refreshAction, Action menuAction, Icon appIcon = null)
        {
            refresh = refreshAction;
            showMenu = menuAction;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = true;
            Text = "Limit Lens";
            if (appIcon != null) Icon = appIcon;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            KeyPreview = true;
            float requestedScale;
            using (Graphics desktop = Graphics.FromHwnd(IntPtr.Zero)) requestedScale = Math.Max(1f, desktop.DpiX / 96f);
            Rectangle workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            dpiScale = Math.Max(0.85f, Math.Min(requestedScale, (workingArea.Height - 16f) / PanelHeight));
            Size = new Size((int)Math.Round(PanelWidth * dpiScale), (int)Math.Round(PanelHeight * dpiScale));
            DoubleBuffered = true;
            BackColor = Color.FromArgb(8, 16, 30);
            using (GraphicsPath path = Rounded(new Rectangle(0, 0, Width, Height), (int)Math.Round(20 * dpiScale)))
                Region = new Region(path);
        }

        protected override bool ShowWithoutActivation { get { return false; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; }
        }

        public void SetData(List<ProviderSnapshot> data, double?[] bars, bool isLoading)
        {
            snapshots = data ?? new List<ProviderSnapshot>();
            history = bars ?? new double?[24];
            loading = isLoading;
            if (!isLoading) updated = DateTimeOffset.Now;
            Invalidate();
        }

        public void SetRemaining(bool value) { remaining = value; Invalidate(); }
        public bool IsRemaining { get { return remaining; } }

        public void ShowByTray(bool keepVisibleOnFirstDeactivate = false)
        {
            startupPinned = keepVisibleOnFirstDeactivate;
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int x = Math.Max(area.Left + 8, Math.Min(Cursor.Position.X - Width / 2, area.Right - Width - 10));
            int y = Math.Max(area.Top + 8, area.Bottom - Height - 9);
            Location = new Point(x, y);
            Show();
            Activate();
            Focus();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            AppLog.Write("Popup deaktiviert; Start-Pin=" + startupPinned);
            if (startupPinned) return;
            Hide();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            AppLog.Write("Popup sichtbar=" + Visible + "; Start-Pin=" + startupPinned);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            startupPinned = false;
            if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = true; }
            else if (e.KeyCode == Keys.R || e.KeyCode == Keys.F5) { refresh(); e.Handled = true; }
            else if (e.KeyCode == Keys.U) { SetRemaining(!remaining); e.Handled = true; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int next = HitAction(e.X / dpiScale, e.Y / dpiScale);
            if (next != hoveredAction) { hoveredAction = next; Invalidate(); }
            Cursor = next == 0 ? Cursors.Default : Cursors.Hand;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoveredAction = 0;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            startupPinned = false;
            if (e.Button != MouseButtons.Left) return;
            int action = HitAction(e.X / dpiScale, e.Y / dpiScale);
            if (action == 1) SetRemaining(true);
            else if (action == 2) SetRemaining(false);
            else if (action == 3) refresh();
            else if (action == 4) showMenu();
            else if (action == 5) Hide();
        }

        private static int HitAction(float x, float y)
        {
            if (y < 16 || y > 58) return 0;
            if (x >= 274 && x < 322) return 1;
            if (x >= 322 && x < 374) return 2;
            if (x >= 382 && x < 412) return 3;
            if (x >= 414 && x < 444) return 4;
            if (x >= 446) return 5;
            return 0;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.ScaleTransform(dpiScale, dpiScale);
            Draw(e.Graphics);
        }

        private void Draw(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Rectangle logicalBounds = new Rectangle(0, 0, PanelWidth, PanelHeight);
            using (var bg = new LinearGradientBrush(logicalBounds, Color.FromArgb(13, 27, 47), Color.FromArgb(7, 16, 30), 32f))
                g.FillRectangle(bg, logicalBounds);
            using (var glow = new PathGradientBrush(new[] { new Point(315, 0), new Point(472, 0), new Point(472, 205), new Point(250, 0) }))
            {
                glow.CenterColor = Color.FromArgb(54, 56, 105, 171);
                glow.SurroundColors = new[] { Color.Transparent, Color.Transparent, Color.Transparent, Color.Transparent };
                g.FillRectangle(glow, 245, 0, 227, 220);
            }
            StrokeRounded(g, new Rectangle(1, 1, PanelWidth - 3, PanelHeight - 3), 20, Color.FromArgb(74, 100, 137, 178), 1.1f);

            DrawText(g, "Limit Lens", titleFont, Color.FromArgb(245, 249, 255), 22, 15);
            int available = snapshots.Count(p => p.Windows.Any(w => w.UsedPercent.HasValue || !string.IsNullOrEmpty(w.Detail)));
            string summary = loading ? "Wird aktualisiert …" : snapshots.Count == 0 ? "Quoten auf einen Blick" : available + " von " + snapshots.Count + " Diensten mit Daten";
            DrawText(g, summary, microFont, loading ? Color.FromArgb(139, 187, 239) : Color.FromArgb(123, 151, 184), 23, 40);
            DrawModeToggle(g);
            DrawHeaderAction(g, 3, new Rectangle(382, 18, 28, 28), "\uE72C", loading ? Color.FromArgb(106, 137, 173) : Color.FromArgb(183, 210, 240));
            DrawHeaderAction(g, 4, new Rectangle(414, 18, 28, 28), "\uE713", Color.FromArgb(183, 210, 240));
            DrawHeaderAction(g, 5, new Rectangle(446, 18, 22, 28), "\uE711", Color.FromArgb(183, 210, 240));
            Line(g, 20, 66, 452, 66, Color.FromArgb(39, 102, 134, 169));

            DrawProvider(g, "Claude", Find("Claude"), 78, 108, Color.FromArgb(235, 139, 88));
            DrawProvider(g, "Codex", Find("Codex"), 194, 108, Color.FromArgb(229, 237, 248));
            DrawProvider(g, "OpenCode Go", Find("OpenCode Go"), 310, 134, Color.FromArgb(86, 220, 166));
            DrawProvider(g, "Gemini", Find("Gemini"), 452, 88, Color.FromArgb(143, 170, 255));
            DrawHistory(g);

            Line(g, 21, 672, 451, 672, Color.FromArgb(36, 101, 132, 165));
            string stamp = updated.HasValue ? "Aktualisiert " + updated.Value.ToString("HH:mm") : "Noch nicht aktualisiert";
            DrawText(g, stamp, microFont, Color.FromArgb(120, 149, 183), 23, 683);
            DrawTextRight(g, "R  Aktualisieren   ·   Esc  Schließen", microFont, Color.FromArgb(96, 125, 160), 450, 683);
        }

        private void DrawModeToggle(Graphics g)
        {
            Rectangle outer = new Rectangle(274, 17, 100, 32);
            FillRounded(g, outer, 10, Color.FromArgb(27, 46, 70));
            Rectangle selected = remaining ? new Rectangle(277, 20, 45, 26) : new Rectangle(322, 20, 49, 26);
            Color selectedColor = Color.FromArgb(61, 91, 128);
            if ((remaining && hoveredAction == 1) || (!remaining && hoveredAction == 2)) selectedColor = Color.FromArgb(72, 106, 147);
            FillRounded(g, selected, 8, selectedColor);
            DrawCentered(g, "Frei", microFont, remaining ? Color.White : Color.FromArgb(133, 159, 190), new Rectangle(276, 18, 47, 30));
            DrawCentered(g, "Genutzt", microFont, remaining ? Color.FromArgb(133, 159, 190) : Color.White, new Rectangle(322, 18, 50, 30));
        }

        private void DrawHeaderAction(Graphics g, int action, Rectangle rect, string glyph, Color color)
        {
            if (hoveredAction == action) FillRounded(g, rect, 8, Color.FromArgb(47, 70, 98));
            DrawCentered(g, glyph, commandIconFont, color, rect);
        }

        private ProviderSnapshot Find(string name) { return snapshots.FirstOrDefault(p => p.Name == name); }

        private void DrawProvider(Graphics g, string expectedName, ProviderSnapshot provider, int y, int height, Color accent)
        {
            Rectangle box = new Rectangle(18, y, 436, height);
            using (var fill = new LinearGradientBrush(box, Color.FromArgb(28, 47, 72), Color.FromArgb(18, 35, 57), 0f))
                FillRounded(g, box, 14, fill);
            StrokeRounded(g, box, 14, Color.FromArgb(43, 116, 149, 185), 1f);
            FillRounded(g, new Rectangle(18, y + 15, 3, height - 30), 2, Color.FromArgb(185, accent));

            string name = provider == null ? expectedName : provider.Name;
            string mark = provider == null ? DefaultMark(name) : provider.Mark;
            Rectangle badge = new Rectangle(31, y + 14, 43, 43);
            FillRounded(g, badge, 12, Color.FromArgb(38, accent));
            StrokeRounded(g, badge, 12, Color.FromArgb(92, accent), 1f);
            DrawCentered(g, mark, name == "OpenCode Go" ? badgeFont : markFont, accent, badge);

            DrawText(g, name, nameFont, Color.FromArgb(243, 248, 255), 88, y + 12);
            string state = provider == null ? "Wird geladen …" : provider.Status;
            bool hasData = provider != null && provider.Windows.Any(w => w.UsedPercent.HasValue || !string.IsNullOrEmpty(w.Detail));
            Color dot = provider == null ? Color.FromArgb(105, 138, 176) : hasData ? Color.FromArgb(88, 211, 151) : Color.FromArgb(225, 117, 112);
            using (var dotBrush = new SolidBrush(dot)) g.FillEllipse(dotBrush, 89, y + 36, 6, 6);
            DrawText(g, Truncate(g, state, microFont, 334), microFont, Color.FromArgb(139, 165, 196), 100, y + 32);

            if (provider == null || provider.Windows.Count == 0)
            {
                DrawText(g, provider == null ? "Daten werden geladen" : EmptyMessage(name), bodyFont, Color.FromArgb(177, 199, 224), 88, y + 65);
                return;
            }

            if (name == "Gemini")
            {
                UsageWindow local = provider.Windows[0];
                DrawText(g, local.Label, microFont, Color.FromArgb(157, 183, 214), 88, y + 59);
                DrawTextRight(g, local.Detail ?? "—", valueFont, Color.FromArgb(220, 231, 247), 430, y + 57);
                return;
            }

            int maxRows = name == "OpenCode Go" ? 3 : 2;
            for (int i = 0; i < Math.Min(maxRows, provider.Windows.Count); i++)
            {
                UsageWindow window = provider.Windows[i];
                int rowY = y + 55 + i * 24;
                DrawText(g, window.Label, microFont, Color.FromArgb(186, 207, 230), 88, rowY);
                DrawTextRight(g, Formatters.Until(window.ResetsAt), microFont, Color.FromArgb(125, 153, 187), 365, rowY);
                DrawTextRight(g, Formatters.Percent(window.UsedPercent, remaining), valueFont, Color.FromArgb(247, 250, 255), 431, rowY - 2);
                double shown = remaining ? 100 - window.UsedPercent.Value : window.UsedPercent.Value;
                DrawBar(g, new Rectangle(88, rowY + 15, 343, 5), shown, RiskColor(window.RemainingPercent, accent));
            }
        }

        private static string DefaultMark(string name)
        {
            if (name == "Claude") return "✳";
            if (name == "Codex") return "◎";
            if (name == "OpenCode Go") return "GO";
            return "✦";
        }

        private static string EmptyMessage(string name)
        {
            if (name == "OpenCode Go") return "Mit /connect bei OpenCode Go anmelden";
            if (name == "Claude") return "Claude im Menü verbinden";
            if (name == "Gemini") return "Noch keine lokale Gemini-Sitzung gefunden";
            return "Keine verlässliche Quote verfügbar";
        }

        private static Color RiskColor(int remainingPercent, Color normal)
        {
            if (remainingPercent >= 0 && remainingPercent <= 20) return Color.FromArgb(239, 105, 105);
            if (remainingPercent >= 0 && remainingPercent <= 40) return Color.FromArgb(241, 177, 78);
            return normal;
        }

        private void DrawHistory(Graphics g)
        {
            DrawText(g, "Nutzungstrend", nameFont, Color.FromArgb(237, 244, 252), 23, 554);
            DrawTextRight(g, "Ø AUSLASTUNG · 24 H", microFont, Color.FromArgb(128, 157, 191), 449, 558);
            Rectangle plot = new Rectangle(27, 584, 418, 67);
            using (var guide = new Pen(Color.FromArgb(29, 103, 135, 170)))
            {
                guide.DashStyle = DashStyle.Dot;
                g.DrawLine(guide, plot.Left, plot.Top + 31, plot.Right, plot.Top + 31);
            }
            Line(g, plot.Left, plot.Bottom, plot.Right, plot.Bottom, Color.FromArgb(52, 108, 139, 173));
            bool any = history.Any(v => v.HasValue);
            if (!any)
            {
                DrawText(g, "Der Trend erscheint nach den ersten Messungen.", smallFont, Color.FromArgb(130, 157, 190), 80, 610);
            }
            else
            {
                for (int i = 0; i < 24; i++)
                {
                    double? value = history[i];
                    if (!value.HasValue) continue;
                    float x = plot.Left + i * 17.45f;
                    float h = (float)Math.Max(3, value.Value / 100 * 63);
                    RectangleF bar = new RectangleF(x, plot.Bottom - h, 9, h);
                    Color top = value.Value >= 80 ? Color.FromArgb(239, 112, 112) : value.Value >= 60 ? Color.FromArgb(240, 181, 86) : Color.FromArgb(137, 193, 241);
                    using (var brush = new LinearGradientBrush(bar, top, Color.FromArgb(69, 111, 177), 90f)) g.FillRectangle(brush, bar);
                }
            }
            DrawText(g, "−24 h", microFont, Color.FromArgb(116, 145, 177), 27, 654);
            DrawTextRight(g, "jetzt", microFont, Color.FromArgb(116, 145, 177), 445, 654);
        }

        private static void DrawBar(Graphics g, Rectangle rect, double percent, Color color)
        {
            FillRounded(g, rect, 3, Color.FromArgb(47, 73, 102));
            int filled = (int)Math.Round(rect.Width * Math.Max(0, Math.Min(100, percent)) / 100);
            if (filled < 3) return;
            Rectangle valueRect = new Rectangle(rect.X, rect.Y, filled, rect.Height);
            using (var brush = new LinearGradientBrush(valueRect, color, ControlPaint.Dark(color, 0.13f), 0f))
                FillRounded(g, valueRect, 3, brush);
        }

        private static void FillRounded(Graphics g, Rectangle rect, int radius, Color color)
        {
            using (var brush = new SolidBrush(color)) FillRounded(g, rect, radius, brush);
        }

        private static void FillRounded(Graphics g, Rectangle rect, int radius, Brush brush)
        {
            using (GraphicsPath path = Rounded(rect, radius)) g.FillPath(brush, path);
        }

        private static void StrokeRounded(Graphics g, Rectangle rect, int radius, Color color, float width)
        {
            using (GraphicsPath path = Rounded(rect, radius))
            using (var pen = new Pen(color, width)) g.DrawPath(pen, path);
        }

        private static GraphicsPath Rounded(Rectangle rect, int radius)
        {
            int diameter = Math.Max(2, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawText(Graphics g, string value, Font font, Color color, float x, float y)
        {
            using (var brush = new SolidBrush(color)) g.DrawString(value ?? "", font, brush, x, y);
        }

        private static void DrawTextRight(Graphics g, string value, Font font, Color color, float right, float y)
        {
            float width = g.MeasureString(value ?? "", font).Width;
            DrawText(g, value, font, color, right - width, y);
        }

        private static void DrawCentered(Graphics g, string value, Font font, Color color, Rectangle rect)
        {
            SizeF size = g.MeasureString(value ?? "", font);
            DrawText(g, value, font, color, rect.X + (rect.Width - size.Width) / 2, rect.Y + (rect.Height - size.Height) / 2 - 1);
        }

        private static string Truncate(Graphics g, string value, Font font, int maxWidth)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string result = value;
            while (result.Length > 1 && g.MeasureString(result, font).Width > maxWidth)
                result = result.Substring(0, result.Length - 2) + "…";
            return result;
        }

        private static void Line(Graphics g, int x1, int y1, int x2, int y2, Color color)
        {
            using (var pen = new Pen(color)) g.DrawLine(pen, x1, y1, x2, y2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                titleFont.Dispose();
                nameFont.Dispose();
                bodyFont.Dispose();
                smallFont.Dispose();
                microFont.Dispose();
                valueFont.Dispose();
                markFont.Dispose();
                badgeFont.Dispose();
                commandIconFont.Dispose();
            }
            base.Dispose(disposing);
        }

        public static void RenderPreview(string path)
        {
            var form = new PopupForm(delegate { }, delegate { });
            var claude = new ProviderSnapshot { Name = "Claude", Mark = "✳", Status = "Claude Code · zuletzt 19:53" };
            claude.Windows.Add(new UsageWindow { Label = "5 Stunden", UsedPercent = 72, ResetsAt = DateTimeOffset.Now.AddHours(2).AddMinutes(14) });
            claude.Windows.Add(new UsageWindow { Label = "Woche", UsedPercent = 31, ResetsAt = DateTimeOffset.Now.AddDays(4) });
            var codex = new ProviderSnapshot { Name = "Codex", Mark = "◎", Status = "Live · Codex App-Server" };
            codex.Windows.Add(new UsageWindow { Label = "5 Stunden", UsedPercent = 48, ResetsAt = DateTimeOffset.Now.AddHours(3).AddMinutes(28) });
            codex.Windows.Add(new UsageWindow { Label = "Woche", UsedPercent = 61, ResetsAt = DateTimeOffset.Now.AddDays(2) });
            var openCode = new ProviderSnapshot { Name = "OpenCode Go", Mark = "GO", Status = "Live · offizielles Go-Limit" };
            openCode.Windows.Add(new UsageWindow { Label = "5 Stunden", UsedPercent = 18, ResetsAt = DateTimeOffset.Now.AddHours(1).AddMinutes(42) });
            openCode.Windows.Add(new UsageWindow { Label = "Woche", UsedPercent = 43, ResetsAt = DateTimeOffset.Now.AddDays(3).AddHours(8) });
            openCode.Windows.Add(new UsageWindow { Label = "Monat", UsedPercent = 67, ResetsAt = DateTimeOffset.Now.AddDays(12) });
            var gemini = new ProviderSnapshot { Name = "Gemini", Mark = "✦", Status = "Abo-Quote nicht verfügbar · 29.09. 19:53" };
            gemini.Windows.Add(new UsageWindow { Label = "Letzte Sitzung", Detail = "360 Tsd. Tokens lokal" });
            double?[] bars = new double?[24];
            double[] values = { 26, 30, 19, 24, 36, 42, 27, 31, 47, 52, 38, 49, 57, 44, 35, 41, 29, 26, 36, 48, 43, 55, 50, 62 };
            for (int i = 0; i < 24; i++) bars[i] = values[i];
            form.SetData(new List<ProviderSnapshot> { claude, codex, openCode, gemini }, bars, false);
            using (var bitmap = new Bitmap(PanelWidth, PanelHeight))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                form.Draw(graphics);
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            form.Dispose();
        }
    }
}
