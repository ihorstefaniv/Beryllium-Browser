using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Beryll
{
    sealed class BrowserForm : Form
    {
        public readonly Settings S;
        public HwProfile Hw;
        CoreWebView2Environment env;
        readonly List<Tab> tabs = new List<Tab>();
        readonly Stack<string> closed = new Stack<string>();
        readonly HashSet<string> httpOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Tab active;
        float k = 1;
        Font fUi, fSmall, fIcon, fIconSmall, fBold;

        readonly Panel content = new Panel();
        readonly NewTabPage ntp;
        readonly TextBox omni = new TextBox();
        readonly ListBox suggest = new ListBox();
        readonly Timer governor = new Timer(), spinner = new Timer(), toastTimer = new Timer();
        readonly ToolTip tips = new ToolTip();
        string toast;
        int spin;
        bool fullscreen, prevMax;
        Rectangle prevBounds;
        string startUrl;
        Font fToast;

        // layout (client coordinates)
        int stripH, toolH, barH;
        Rectangle stripR, toolR, barR, omniR, plusR, minR, maxR, closeR, backR, fwdR, reloadR, shieldR, fireR, menuR, starR;
        readonly List<Rectangle> tabR = new List<Rectangle>();
        readonly List<Rectangle> tabCloseR = new List<Rectangle>();
        readonly List<Rectangle> barItemR = new List<Rectangle>();
        string hover = ""; int hoverTab = -1, hoverClose = -1, hoverBar = -1;

        public BrowserForm(Settings s, string url)
        {
            S = s;
            startUrl = url;
            Theme.Apply(S.ThemeMode);
            Hw = HwProfile.Detect(S);
            Text = "Beryll";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Strip;
            KeyPreview = true;
            MinimumSize = new Size(480, 320);
            StartPosition = FormStartPosition.Manual;
            RestoreWindow();

            content.BackColor = Theme.Page;
            Controls.Add(content);
            ntp = new NewTabPage(this) { Dock = DockStyle.Fill };
            content.Controls.Add(ntp);

            omni.BorderStyle = BorderStyle.None;
            omni.KeyDown += OmniKeyDown;
            omni.TextChanged += delegate { if (omni.Focused) ShowSuggestions(); };
            omni.GotFocus += delegate { omni.BackColor = Theme.OmniFocus; BeginInvoke((MethodInvoker)omni.SelectAll); Invalidate(omniR); };
            omni.LostFocus += delegate { omni.BackColor = Theme.Omni; if (!suggest.Focused) { suggest.Visible = false; } Invalidate(omniR); };
            Controls.Add(omni);

            suggest.Visible = false;
            suggest.BorderStyle = BorderStyle.FixedSingle;
            suggest.DrawMode = DrawMode.OwnerDrawFixed;
            suggest.DrawItem += DrawSuggestion;
            suggest.MouseClick += delegate { GoSuggestion(); };
            suggest.IntegralHeight = false;
            Controls.Add(suggest);
            suggest.BringToFront();

            governor.Interval = 4000; governor.Tick += delegate { Govern(); };
            spinner.Interval = 60; spinner.Tick += delegate { spin = (spin + 30) % 360; Invalidate(stripR); };
            toastTimer.Tick += delegate { toastTimer.Stop(); toast = null; Invalidate(); };
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSysPrefs;
        }

        // ================================================================ window chrome (tabs in the title bar)

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.Style |= 0x00040000 | 0x00020000 | 0x00010000; return cp; } // THICKFRAME | MINIMIZEBOX | MAXIMIZEBOX
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var m = new Native.MARGINS { L = 0, R = 0, T = 1, B = 0 };
            Native.DwmExtendFrameIntoClientArea(Handle, ref m);
            Native.DarkTitle(Handle, Theme.Dark);
            Native.RoundCorners(Handle);
            uint dpi = 0;
            try { dpi = Native.GetDpiForWindow(Handle); } catch { }
            SetScale(dpi > 0 ? dpi / 96f : 1f);
            try { File.WriteAllText(Paths.File("instance"), Handle.ToInt64().ToString()); } catch { }
        }

        int FrameX { get { return Native.GetSystemMetrics(32) + Native.GetSystemMetrics(92); } } // SM_CXSIZEFRAME + SM_CXPADDEDBORDER
        int FrameY { get { return Native.GetSystemMetrics(33) + Native.GetSystemMetrics(92); } }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x0083: // WM_NCCALCSIZE: the whole window is client area (we draw our own title bar)
                    if (m.WParam != IntPtr.Zero)
                    {
                        if (WindowState == FormWindowState.Maximized && !fullscreen)
                        {
                            var p = (Native.NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(Native.NCCALCSIZE_PARAMS));
                            p.r0.Left += FrameX; p.r0.Right -= FrameX; p.r0.Top += FrameY; p.r0.Bottom -= FrameY;
                            Marshal.StructureToPtr(p, m.LParam, false);
                        }
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;
                case 0x0084: // WM_NCHITTEST
                    {
                        int x = (short)((long)m.LParam & 0xFFFF), y = (short)(((long)m.LParam >> 16) & 0xFFFF);
                        Point p = PointToClient(new Point(x, y));
                        m.Result = (IntPtr)HitTest(p);
                        return;
                    }
                case 0x004A: // WM_COPYDATA from a second Beryll instance
                    {
                        var cds = (Native.COPYDATASTRUCT)Marshal.PtrToStructure(m.LParam, typeof(Native.COPYDATASTRUCT));
                        string arg = cds.cbData > 0 ? Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2).TrimEnd('\0') : "";
                        BeginInvoke((MethodInvoker)delegate
                        {
                            if (Native.IsIconic(Handle)) Native.ShowWindow(Handle, 9);
                            Activate();
                            NewTab(arg.Length > 0 ? arg : null, true);
                        });
                        m.Result = (IntPtr)1;
                        return;
                    }
                case 0x02E0: // WM_DPICHANGED
                    {
                        SetScale(((int)((long)m.WParam & 0xFFFF)) / 96f);
                        var r = (Native.RECT)Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
                        Bounds = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                        m.Result = IntPtr.Zero;
                        return;
                    }
            }
            base.WndProc(ref m);
        }

        int HitTest(Point p)
        {
            if (fullscreen) return 1;
            int e = (int)(6 * k);
            if (WindowState != FormWindowState.Maximized)
            {
                bool l = p.X < e, r = p.X >= ClientSize.Width - e, t = p.Y < e, b = p.Y >= ClientSize.Height - e;
                if (t && l) return 13; if (t && r) return 14; if (b && l) return 16; if (b && r) return 17;
                if (l) return 10; if (r) return 11; if (t) return 12; if (b) return 15;
            }
            if (stripR.Contains(p))
            {
                if (minR.Contains(p) || maxR.Contains(p) || closeR.Contains(p) || plusR.Contains(p)) return 1;
                foreach (Rectangle r in tabR) if (r.Contains(p)) return 1;
                return 2; // HTCAPTION: drag, double-click maximize, Aero Snap
            }
            return 1;
        }

        // ================================================================ scale, fonts, layout

        void SetScale(float s)
        {
            k = Math.Max(1, s);
            if (fUi != null) { fUi.Dispose(); fSmall.Dispose(); fIcon.Dispose(); fIconSmall.Dispose(); fBold.Dispose(); fToast.Dispose(); }
            fUi = new Font("Segoe UI", 13.5f * k, FontStyle.Regular, GraphicsUnit.Pixel);
            fSmall = new Font("Segoe UI", 12f * k, FontStyle.Regular, GraphicsUnit.Pixel);
            fBold = new Font("Segoe UI Semibold", 12.5f * k, FontStyle.Regular, GraphicsUnit.Pixel);
            fToast = new Font("Segoe UI", 13f * k, FontStyle.Regular, GraphicsUnit.Pixel);
            string icons = IconFontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
            fIcon = new Font(icons, 15f * k, FontStyle.Regular, GraphicsUnit.Pixel);
            fIconSmall = new Font(icons, 10f * k, FontStyle.Regular, GraphicsUnit.Pixel);
            omni.Font = fUi;
            suggest.Font = fUi;
            suggest.ItemHeight = (int)(34 * k);
            if (ntp != null) ntp.SetScale(k);
            DoLayout();
        }

        static bool IconFontExists(string n) { using (var f = new Font(n, 10f)) return f.Name == n; }

        protected override void OnResize(EventArgs e) { base.OnResize(e); DoLayout(); }

        void DoLayout()
        {
            if (fUi == null) return;
            int w = ClientSize.Width;
            stripH = fullscreen ? 0 : (int)(40 * k);
            toolH = fullscreen ? 0 : (int)(46 * k);
            barH = fullscreen || !S.BookmarksBar ? 0 : (int)(32 * k);
            stripR = new Rectangle(0, 0, w, stripH);
            toolR = new Rectangle(0, stripH, w, toolH);
            barR = new Rectangle(0, stripH + toolH, w, barH);
            int cb = (int)(46 * k);
            closeR = new Rectangle(w - cb, 0, cb, stripH);
            maxR = new Rectangle(w - 2 * cb, 0, cb, stripH);
            minR = new Rectangle(w - 3 * cb, 0, cb, stripH);

            // tabs
            tabR.Clear(); tabCloseR.Clear();
            int x = (int)(8 * k), tabTop = (int)(7 * k), tabHh = stripH - tabTop;
            int avail = minR.Left - x - (int)(48 * k);
            int pinnedW = (int)(44 * k);
            int nPinned = tabs.Count(t => t.Pinned), nNormal = tabs.Count - nPinned;
            int tw = nNormal == 0 ? 0 : Math.Max((int)(56 * k), Math.Min((int)(232 * k), (avail - nPinned * pinnedW) / Math.Max(1, nNormal)));
            foreach (Tab t in tabs)
            {
                int ww = t.Pinned ? pinnedW : tw;
                tabR.Add(new Rectangle(x, tabTop, ww, tabHh));
                int cs = (int)(18 * k);
                tabCloseR.Add(t.Pinned || ww < 80 * k ? Rectangle.Empty : new Rectangle(x + ww - cs - (int)(8 * k), tabTop + (tabHh - cs) / 2, cs, cs));
                x += ww;
            }
            plusR = new Rectangle(x + (int)(6 * k), tabTop + (tabHh - (int)(28 * k)) / 2, (int)(28 * k), (int)(28 * k));

            // toolbar
            int bs = (int)(34 * k), ty = stripH + (toolH - bs) / 2, bx = (int)(8 * k);
            backR = new Rectangle(bx, ty, bs, bs); bx += bs + (int)(2 * k);
            fwdR = new Rectangle(bx, ty, bs, bs); bx += bs + (int)(2 * k);
            reloadR = new Rectangle(bx, ty, bs, bs); bx += bs + (int)(8 * k);
            menuR = new Rectangle(w - bs - (int)(8 * k), ty, bs, bs);
            fireR = new Rectangle(menuR.Left - bs - (int)(4 * k), ty, bs, bs);
            shieldR = new Rectangle(fireR.Left - bs - (int)(4 * k), ty, bs, bs);
            int oh = (int)(34 * k);
            omniR = new Rectangle(bx, stripH + (toolH - oh) / 2, shieldR.Left - (int)(10 * k) - bx, oh);
            starR = new Rectangle(omniR.Right - (int)(32 * k), omniR.Top + (oh - (int)(26 * k)) / 2, (int)(26 * k), (int)(26 * k));
            int textX = omniR.Left + (int)(36 * k);
            omni.Visible = !fullscreen;
            omni.SetBounds(textX, omniR.Top + (oh - omni.PreferredHeight) / 2, Math.Max(10, starR.Left - textX - (int)(6 * k)), omni.PreferredHeight);
            omni.BackColor = omni.Focused ? Theme.OmniFocus : Theme.Omni;
            omni.ForeColor = Theme.Text;

            // bookmarks bar (always visible like Chrome when enabled; icon + title per item)
            barItemR.Clear();
            if (barH > 0)
            {
                int bxx = (int)(10 * k);
                using (Graphics g = CreateGraphics())
                    foreach (Bookmark b in Store.Bookmarks)
                    {
                        string t = Short(b.Title, 22);
                        int bw = TextRenderer.MeasureText(g, t, fSmall).Width + (int)(40 * k);
                        if (bxx + bw > w - 10 * k) break;
                        barItemR.Add(new Rectangle(bxx, barR.Top + (int)(3 * k), bw, barH - (int)(6 * k)));
                        if (IconCache.Get(Tiles.Host(b.Url)) == null) IconCache.Fetch(Tiles.Host(b.Url), () => { try { BeginInvoke((MethodInvoker)delegate { Invalidate(barR); }); } catch { } });
                        bxx += bw + (int)(2 * k);
                    }
            }

            int top = stripH + toolH + barH;
            content.SetBounds(0, top, w, Math.Max(1, ClientSize.Height - top));
            if (suggest.Visible) PlaceSuggest();
            Invalidate();
        }

        static string Short(string s, int n) { s = s ?? ""; return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }

        // ================================================================ painting

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (fullscreen) { g.Clear(Color.Black); return; }
            using (var b = new SolidBrush(Theme.Strip)) g.FillRectangle(b, stripR);
            using (var b = new SolidBrush(Theme.Toolbar)) g.FillRectangle(b, toolR.Left, toolR.Top, toolR.Width, toolR.Height + barH);
            DrawTabs(g);
            DrawCaptionButtons(g);
            DrawToolbar(g);
            if (barH > 0) DrawBookmarksBar(g);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, content.Top - 1, ClientSize.Width, content.Top - 1);
            if (toast != null) DrawToast(g);
        }

        static GraphicsPath TabPath(Rectangle r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddLine(r.Left - rad, r.Bottom, r.Left, r.Bottom - rad);
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddLine(r.Right, r.Bottom - rad, r.Right + rad, r.Bottom);
            p.CloseFigure();
            return p;
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void DrawTabs(Graphics g)
        {
            for (int i = 0; i < tabs.Count && i < tabR.Count; i++)
            {
                Tab t = tabs[i];
                Rectangle r = tabR[i];
                bool act = t == active;
                if (act)
                    using (GraphicsPath p = TabPath(r, 8 * k)) using (var b = new SolidBrush(Theme.Toolbar)) g.FillPath(b, p);
                else if (i == hoverTab)
                    using (GraphicsPath p = Round(new RectangleF(r.X + 3 * k, r.Y + 2 * k, r.Width - 6 * k, r.Height - 7 * k), 7 * k)) using (var b = new SolidBrush(Theme.StripHover)) g.FillPath(b, p);
                else if (i + 1 < tabs.Count && tabs[i + 1] != active && i + 1 != hoverTab)
                    using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, r.Right, r.Top + 9 * k, r.Right, r.Bottom - 11 * k);

                int ic = (int)(16 * k);
                var iconR = new Rectangle(r.Left + (t.Pinned ? (r.Width - ic) / 2 : (int)(12 * k)), r.Top + (r.Height - ic) / 2 - (int)(1 * k), ic, ic);
                float alpha = t.Asleep ? 0.45f : 1f;
                if (t.Loading)
                    using (var pen = new Pen(Theme.Beryl, 2 * k)) g.DrawArc(pen, iconR.X + 1, iconR.Y + 1, iconR.Width - 2, iconR.Height - 2, spin, 270);
                else if (t.Icon != null)
                {
                    using (var ia = new System.Drawing.Imaging.ImageAttributes())
                    {
                        var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha };
                        ia.SetColorMatrix(cm);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(t.Icon, iconR, 0, 0, t.Icon.Width, t.Icon.Height, GraphicsUnit.Pixel, ia);
                    }
                }
                else if (t.IsNewTab) DrawElementTile(g, iconR, false);
                else TextRenderer.DrawText(g, "", fIconSmall, iconR, Fade(Theme.TextDim, alpha), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                if (t.Pinned) continue;
                int tx = iconR.Right + (int)(8 * k);
                int right = tabCloseR[i].IsEmpty ? r.Right - (int)(8 * k) : tabCloseR[i].Left - (int)(4 * k);
                if (t.Audible)
                {
                    var ar = new Rectangle(right - (int)(18 * k), r.Top, (int)(18 * k), r.Height - (int)(2 * k));
                    TextRenderer.DrawText(g, t.Muted ? "" : "", fIconSmall, ar, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    right = ar.Left - (int)(2 * k);
                }
                if (t.Asleep)
                {
                    var mr = new Rectangle(tx - (int)(2 * k), r.Top, (int)(16 * k), r.Height - (int)(2 * k));
                    TextRenderer.DrawText(g, "", fIconSmall, mr, Theme.Beryl, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    tx += (int)(16 * k);
                }
                string title = string.IsNullOrEmpty(t.Title) ? t.Url : t.Title;
                TextRenderer.DrawText(g, title, fSmall, new Rectangle(tx, r.Top, Math.Max(0, right - tx), r.Height - (int)(2 * k)),
                    Fade(act ? Theme.Text : Theme.TextDim, t.Asleep ? 0.7f : 1f),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                if (!tabCloseR[i].IsEmpty && (act || i == hoverTab))
                {
                    Rectangle c = tabCloseR[i];
                    if (i == hoverClose) using (var b = new SolidBrush(Theme.Hover)) g.FillEllipse(b, c);
                    TextRenderer.DrawText(g, "", fIconSmall, c, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            if (hover == "plus") using (var b = new SolidBrush(Theme.StripHover)) g.FillEllipse(b, plusR);
            TextRenderer.DrawText(g, "", fIconSmall, plusR, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        static Color Fade(Color c, float a) { return Color.FromArgb((int)(c.A * a), c); }

        void DrawCaptionButtons(Graphics g)
        {
            foreach (var kv in new[] { Tuple.Create("min", minR, ""), Tuple.Create("max", maxR, WindowState == FormWindowState.Maximized ? "" : ""), Tuple.Create("close", closeR, "") })
            {
                bool h = hover == kv.Item1;
                if (h) using (var b = new SolidBrush(kv.Item1 == "close" ? Color.FromArgb(232, 17, 35) : Theme.StripHover)) g.FillRectangle(b, kv.Item2);
                TextRenderer.DrawText(g, kv.Item3, fIconSmall, kv.Item2, h && kv.Item1 == "close" ? Color.White : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        void ToolButton(Graphics g, Rectangle r, string id, string glyph, bool enabled)
        {
            if (hover == id && enabled) using (GraphicsPath p = Round(r, r.Width / 2f)) using (var b = new SolidBrush(Theme.Hover)) g.FillPath(b, p);
            TextRenderer.DrawText(g, glyph, fIcon, r, enabled ? Theme.Text : Fade(Theme.TextDim, 0.5f), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        void DrawToolbar(Graphics g)
        {
            CoreWebView2 c = ActiveCore;
            ToolButton(g, backR, "back", "", c != null && c.CanGoBack);
            ToolButton(g, fwdR, "fwd", "", c != null && c.CanGoForward);
            ToolButton(g, reloadR, "reload", active != null && active.Loading ? "" : "", true);

            // omnibox pill
            using (GraphicsPath p = Round(omniR, omniR.Height / 2f))
            {
                using (var b = new SolidBrush(omni.Focused ? Theme.OmniFocus : Theme.Omni)) g.FillPath(b, p);
                if (omni.Focused) using (var pen = new Pen(Theme.Beryl, 1.6f * k)) g.DrawPath(pen, p);
            }
            var lockR = new Rectangle(omniR.Left + (int)(8 * k), omniR.Top, (int)(24 * k), omniR.Height);
            string scheme = SchemeOf(active);
            string lockGlyph = active == null || active.IsNewTab ? "" : scheme == "https" ? "" : "";
            Color lockColor = scheme == "http" ? Theme.Danger : Theme.TextDim;
            TextRenderer.DrawText(g, lockGlyph, fIconSmall, lockR, lockColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (active != null && !active.IsNewTab)
            {
                bool bm = Store.Bookmarks.Any(b => b.Url == active.Url);
                if (hover == "star") using (var b = new SolidBrush(Theme.Hover)) g.FillEllipse(b, starR);
                TextRenderer.DrawText(g, bm ? "" : "", fIconSmall, starR, bm ? Theme.Beryl : Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // shield
            if (hover == "shield") using (GraphicsPath p = Round(shieldR, shieldR.Width / 2f)) using (var b = new SolidBrush(Theme.Hover)) g.FillPath(b, p);
            bool on = S.Protection && active != null && !S.AllowList.Contains(Privacy.RegistrableDomain(active.Host));
            DrawShield(g, shieldR, on);
            if (on && active != null && active.Blocked > 0)
            {
                string n = active.Blocked > 99 ? "99+" : active.Blocked.ToString();
                Size ts = TextRenderer.MeasureText(n, fIconSmall.Size > 0 ? fSmall : fSmall);
                var br = new Rectangle(shieldR.Right - (int)(14 * k), shieldR.Top + (int)(1 * k), Math.Max((int)(16 * k), ts.Width - (int)(4 * k)), (int)(15 * k));
                using (GraphicsPath p = Round(br, br.Height / 2f)) using (var b = new SolidBrush(Theme.Beryl)) g.FillPath(b, p);
                using (var f = new Font("Segoe UI Semibold", 9.5f * k, FontStyle.Regular, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, n, f, br, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            // fire
            if (hover == "fire") using (GraphicsPath p = Round(fireR, fireR.Width / 2f)) using (var b = new SolidBrush(Theme.Hover)) g.FillPath(b, p);
            DrawFlame(g, fireR);
            ToolButton(g, menuR, "menu", "", true);
        }

        void DrawShield(Graphics g, Rectangle r, bool on)
        {
            float w = 15 * k, h = 18 * k, x = r.X + (r.Width - w) / 2, y = r.Y + (r.Height - h) / 2;
            var p = new GraphicsPath();
            p.AddLine(x + w / 2, y, x + w, y + h * 0.18f);
            p.AddBezier(x + w, y + h * 0.18f, x + w, y + h * 0.65f, x + w * 0.75f, y + h * 0.85f, x + w / 2, y + h);
            p.AddBezier(x + w / 2, y + h, x + w * 0.25f, y + h * 0.85f, x, y + h * 0.65f, x, y + h * 0.18f);
            p.CloseFigure();
            using (var b = new SolidBrush(on ? Theme.Beryl : Fade(Theme.TextDim, 0.35f))) g.FillPath(b, p);
            if (on)
                using (var pen = new Pen(Color.White, 1.8f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(pen, new[] { new PointF(x + w * 0.28f, y + h * 0.5f), new PointF(x + w * 0.45f, y + h * 0.66f), new PointF(x + w * 0.74f, y + h * 0.36f) });
            p.Dispose();
        }

        void DrawFlame(Graphics g, Rectangle r)
        {
            float w = 14 * k, h = 18 * k, x = r.X + (r.Width - w) / 2, y = r.Y + (r.Height - h) / 2;
            using (var p = new GraphicsPath())
            {
                p.AddBezier(x + w * 0.5f, y + h, x - w * 0.05f, y + h, x - w * 0.05f, y + h * 0.45f, x + w * 0.35f, y + h * 0.15f);
                p.AddBezier(x + w * 0.35f, y + h * 0.15f, x + w * 0.4f, y + h * 0.4f, x + w * 0.6f, y + h * 0.42f, x + w * 0.62f, y);
                p.AddBezier(x + w * 0.62f, y, x + w * 1.1f, y + h * 0.35f, x + w * 1.05f, y + h, x + w * 0.5f, y + h);
                p.CloseFigure();
                using (var b = new LinearGradientBrush(new RectangleF(x, y, w, h), Color.FromArgb(255, 196, 64), Color.FromArgb(232, 84, 40), 90f)) g.FillPath(b, p);
            }
        }

        public void DrawElementTile(Graphics g, Rectangle r, bool full)
        {
            using (GraphicsPath p = Round(r, r.Width * 0.18f))
            using (var b = new LinearGradientBrush(r, Color.FromArgb(52, 199, 166), Color.FromArgb(20, 120, 110), 45f))
                g.FillPath(b, p);
            float s = r.Width;
            using (var f = new Font("Segoe UI Semibold", s * (full ? 0.42f : 0.55f), FontStyle.Regular, GraphicsUnit.Pixel))
                TextRenderer.DrawText(g, "Be", f, new Rectangle(r.X, r.Y + (int)(full ? s * 0.06f : 0), r.Width, r.Height), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (full)
                using (var f = new Font("Segoe UI", s * 0.13f, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    TextRenderer.DrawText(g, "4", f, new Point(r.X + (int)(s * 0.08f), r.Y + (int)(s * 0.06f)), Color.White, TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, "9.012", f, new Rectangle(r.X, r.Bottom - (int)(s * 0.24f), r.Width, (int)(s * 0.18f)), Color.FromArgb(220, 255, 255, 255),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                }
        }

        void DrawBookmarksBar(Graphics g)
        {
            if (Store.Bookmarks.Count == 0)
            {
                TextRenderer.DrawText(g, "Щоб додати закладку, натисніть ☆ в адресному рядку або Ctrl+D. Імпорт з Chrome / Edge — у Налаштуваннях.", fSmall,
                    new Rectangle((int)(14 * k), barR.Top, ClientSize.Width - (int)(28 * k), barH), Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return;
            }
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            for (int i = 0; i < barItemR.Count && i < Store.Bookmarks.Count; i++)
            {
                Rectangle r = barItemR[i];
                Bookmark b = Store.Bookmarks[i];
                if (i == hoverBar) using (GraphicsPath p = Round(r, r.Height / 2f)) using (var br = new SolidBrush(Theme.Hover)) g.FillPath(br, p);
                int is2 = (int)(16 * k);
                var ir = new Rectangle(r.X + (int)(9 * k), r.Y + (r.Height - is2) / 2, is2, is2);
                Image icon = IconCache.Get(Tiles.Host(b.Url));
                if (icon != null) g.DrawImage(icon, ir);
                else TextRenderer.DrawText(g, "", fIconSmall, ir, Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, Short(b.Title, 22), fSmall, new Rectangle(ir.Right + (int)(6 * k), r.Y, r.Right - ir.Right - (int)(10 * k), r.Height), Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        }

        void DrawToast(Graphics g)
        {
            Size ts = TextRenderer.MeasureText(toast, fToast);
            var r = new Rectangle((ClientSize.Width - ts.Width) / 2 - (int)(18 * k), content.Top + (int)(14 * k), ts.Width + (int)(36 * k), (int)(38 * k));
            // drawn over the strip area only when there is no web content above — so we show it via the NTP or a tiny popup
        }

        public void ShowToast(string text, int ms = 2200)
        {
            toast = text;
            if (toastLabel == null)
            {
                toastLabel = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, BackColor = Color.FromArgb(40, 44, 50) };
                Controls.Add(toastLabel);
            }
            toastLabel.Font = fToast;
            toastLabel.Text = text;
            Size ts = TextRenderer.MeasureText(text, fToast);
            toastLabel.SetBounds((ClientSize.Width - ts.Width) / 2 - (int)(20 * k), content.Top + (int)(16 * k), ts.Width + (int)(40 * k), (int)(38 * k));
            toastLabel.Visible = true;
            toastLabel.BringToFront();
            toastTimer.Stop(); toastTimer.Interval = ms; toastTimer.Start();
            toastTimer.Tick -= HideToastLabel; toastTimer.Tick += HideToastLabel;
        }
        Label toastLabel;
        void HideToastLabel(object s, EventArgs e) { if (toastLabel != null) toastLabel.Visible = false; }

        static string SchemeOf(Tab t)
        {
            if (t == null) return "";
            Uri u; return Uri.TryCreate(t.Url, UriKind.Absolute, out u) ? u.Scheme : "";
        }

        // ================================================================ mouse

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string h = "";
            int ht = -1, hc = -1, hb = -1;
            Point p = e.Location;
            if (minR.Contains(p)) h = "min"; else if (maxR.Contains(p)) h = "max"; else if (closeR.Contains(p)) h = "close";
            else if (plusR.Contains(p)) h = "plus";
            else if (backR.Contains(p)) h = "back"; else if (fwdR.Contains(p)) h = "fwd"; else if (reloadR.Contains(p)) h = "reload";
            else if (shieldR.Contains(p)) h = "shield"; else if (fireR.Contains(p)) h = "fire"; else if (menuR.Contains(p)) h = "menu";
            else if (starR.Contains(p) && active != null && !active.IsNewTab) h = "star";
            for (int i = 0; i < tabR.Count; i++) if (tabR[i].Contains(p)) { ht = i; if (tabCloseR[i].Contains(p)) hc = i; }
            for (int i = 0; i < barItemR.Count; i++) if (barItemR[i].Contains(p)) hb = i;
            if (h != hover || ht != hoverTab || hc != hoverClose || hb != hoverBar)
            {
                string tip = null;
                switch (h)
                {
                    case "back": tip = "Назад (Alt+←)"; break; case "fwd": tip = "Вперед (Alt+→)"; break;
                    case "reload": tip = "Оновити (F5)"; break; case "shield": tip = "Захист приватності на цьому сайті"; break;
                    case "fire": tip = "Вогонь: закрити вкладки й стерти дані (Ctrl+Shift+Del)"; break; case "menu": tip = "Меню"; break;
                    case "plus": tip = "Нова вкладка (Ctrl+T)"; break; case "star": tip = "Закладка (Ctrl+D)"; break;
                }
                if (tip == null && ht >= 0) tip = tabs[ht].Title + (tabs[ht].Asleep ? "\nВкладка спить — пам'ять звільнено" : "");
                if (tip == null && hb >= 0) tip = Store.Bookmarks[hb].Url;
                tips.SetToolTip(this, tip ?? "");
                hover = h; hoverTab = ht; hoverClose = hc; hoverBar = hb;
                Invalidate(new Rectangle(0, 0, ClientSize.Width, stripH + toolH + barH));
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = ""; hoverTab = hoverClose = hoverBar = -1;
            Invalidate(new Rectangle(0, 0, ClientSize.Width, stripH + toolH + barH));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
                for (int i = 0; i < tabR.Count; i++)
                    if (tabR[i].Contains(e.Location) && !tabCloseR[i].Contains(e.Location)) { Activate(tabs[i]); return; }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Point p = e.Location;
            if (e.Button == MouseButtons.Middle)
            {
                for (int i = 0; i < tabR.Count; i++) if (tabR[i].Contains(p)) { CloseTab(tabs[i]); return; }
                for (int i = 0; i < barItemR.Count; i++) if (barItemR[i].Contains(p)) { NewTab(Store.Bookmarks[i].Url, false); return; }
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                for (int i = 0; i < tabR.Count; i++) if (tabR[i].Contains(p)) { TabMenu(tabs[i], p); return; }
                for (int i = 0; i < barItemR.Count; i++) if (barItemR[i].Contains(p)) { BookmarkMenu(i, p); return; }
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (minR.Contains(p)) { WindowState = FormWindowState.Minimized; return; }
            if (maxR.Contains(p)) { WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; return; }
            if (closeR.Contains(p)) { Close(); return; }
            if (plusR.Contains(p)) { NewTab(null, true); return; }
            for (int i = 0; i < tabCloseR.Count; i++) if (tabCloseR[i].Contains(p)) { CloseTab(tabs[i]); return; }
            CoreWebView2 c = ActiveCore;
            if (backR.Contains(p) && c != null && c.CanGoBack) { c.GoBack(); return; }
            if (fwdR.Contains(p) && c != null && c.CanGoForward) { c.GoForward(); return; }
            if (reloadR.Contains(p)) { ReloadOrStop(); return; }
            if (starR.Contains(p) && active != null && !active.IsNewTab) { ToggleBookmark(); return; }
            if (shieldR.Contains(p)) { ShowDashboard(); return; }
            if (fireR.Contains(p)) { Fire(); return; }
            if (menuR.Contains(p)) { ShowMenu(); return; }
            for (int i = 0; i < barItemR.Count; i++) if (barItemR[i].Contains(p)) { Navigate(Store.Bookmarks[i].Url); return; }
            if (omniR.Contains(p)) omni.Focus();
        }

        // ================================================================ startup / shutdown

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                var opts = new CoreWebView2EnvironmentOptions();
                var args = new List<string>();
                if (Hw.Lite) args.Add("--autoplay-policy=document-user-activation-required");
                opts.AdditionalBrowserArguments = string.Join(" ", args);
                opts.Language = "uk";
                env = await CoreWebView2Environment.CreateAsync(null, Paths.Profile, opts);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                MessageBox.Show(this, "Для роботи Beryll потрібен компонент Microsoft Edge WebView2 Runtime (у Windows 10/11 зазвичай уже є).\n\nЗараз відкриється сторінка завантаження.",
                    "Beryll", MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { System.Diagnostics.Process.Start("https://go.microsoft.com/fwlink/p/?LinkId=2124703"); } catch { }
                Close();
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не вдалося запустити рушій: " + ex.Message, "Beryll", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }

            int act = 0;
            List<string[]> session = S.RestoreSession ? Store.LoadSession(out act) : new List<string[]>();
            if (session.Count > 0 && string.IsNullOrEmpty(startUrl))
            {
                // restore all tabs asleep; only the active one is loaded (fast start, little RAM)
                foreach (string[] s in session) tabs.Add(new Tab { Pinned = s[0] == "1", Url = s[1], Title = s.Length > 2 ? s[2] : s[1] });
                act = Math.Max(0, Math.Min(tabs.Count - 1, act));
                DoLayout();
                Activate(tabs[act]);
            }
            else NewTab(string.IsNullOrEmpty(startUrl) ? null : startUrl, true);
            governor.Start();
            if (!S.Onboarded) BeginInvoke((MethodInvoker)delegate { Dialogs.Onboarding(this); });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            Store.SaveSession(tabs, Math.Max(0, tabs.IndexOf(active)));
            Store.CompactHistory();
            if (fullscreen) { S.Max = prevMax; SaveRect(prevBounds); }
            else { S.Max = WindowState == FormWindowState.Maximized; SaveRect(WindowState == FormWindowState.Normal ? Bounds : RestoreBounds); }
            S.Save();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSysPrefs;
            try { File.Delete(Paths.File("instance")); } catch { }
        }

        void SaveRect(Rectangle r) { S.X = r.X; S.Y = r.Y; S.W = r.Width; S.H = r.Height; }

        void RestoreWindow()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            var r = new Rectangle(S.X, S.Y, S.W, S.H);
            bool ok = S.X != int.MinValue && S.W > 400 && S.H > 300 && Screen.AllScreens.Any(sc => Rectangle.Intersect(sc.WorkingArea, r).Width > 150);
            if (!ok) { int w = (int)(wa.Width * 0.82), h = (int)(wa.Height * 0.86); r = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h); }
            Bounds = r;
            if (S.Max) WindowState = FormWindowState.Maximized;
        }

        void OnSysPrefs(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (S.ThemeMode != 0) return;
            try { BeginInvoke((MethodInvoker)ApplyTheme); } catch { }
        }

        public void ApplyTheme()
        {
            Theme.Apply(S.ThemeMode);
            BackColor = Theme.Strip;
            content.BackColor = Theme.Page;
            Native.DarkTitle(Handle, Theme.Dark);
            ntp.Invalidate();
            DoLayout();
        }

        // ================================================================ tabs

        CoreWebView2 ActiveCore { get { return active != null && active.View != null ? active.View.CoreWebView2 : null; } }

        public Tab NewTab(string url, bool activate, Tab after = null)
        {
            var t = new Tab();
            int idx = after != null ? tabs.IndexOf(after) + 1 : tabs.Count;
            tabs.Insert(Math.Max(0, Math.Min(tabs.Count, idx)), t);
            if (!string.IsNullOrEmpty(url)) { t.Url = ToUrl(url) ?? url; t.Title = t.Url; }
            DoLayout();
            if (activate) Activate(t);
            else if (!t.IsNewTab) Wake(t, t.Url, null, null);
            return t;
        }

        public void Activate(Tab t)
        {
            if (t == null) return;
            if (active != null && active != t)
            {
                active.LastActive = DateTime.Now;
                if (active.View != null) active.View.Visible = false;
            }
            active = t;
            t.LastActive = DateTime.Now;
            suggest.Visible = false;
            if (t.IsNewTab)
            {
                ntp.Visible = true; ntp.BringToFront(); ntp.RefreshData();
                omni.Text = "";
                omni.Focus();
            }
            else
            {
                ntp.Visible = false;
                if (t.View == null) Wake(t, t.Url, null, null);
                else
                {
                    t.View.Visible = true;
                    t.View.BringToFront();
                    if (t.Frozen && t.View.CoreWebView2 != null)
                    {
                        try
                        {
                            t.View.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                            if (t.View.CoreWebView2.IsSuspended) t.View.CoreWebView2.Resume();
                        }
                        catch { }
                        t.Frozen = false;
                    }
                    t.View.Focus();
                }
                omni.Text = t.Url;
            }
            Text = (t.IsNewTab ? "Нова вкладка" : t.Title) + " — Beryll";
            Invalidate();
        }

        public void CloseTab(Tab t)
        {
            int i = tabs.IndexOf(t);
            if (i < 0) return;
            if (!t.IsNewTab) closed.Push(t.Url);
            tabs.RemoveAt(i);
            KillView(t);
            if (tabs.Count == 0) { Close(); return; }
            if (t == active) { active = null; Activate(tabs[Math.Min(i, tabs.Count - 1)]); }
            DoLayout();
        }

        void KillView(Tab t)
        {
            if (t.View == null) return;
            WebView2 v = t.View;
            t.View = null;
            content.Controls.Remove(v);
            try { v.Dispose(); } catch { }
        }

        /// <summary>Creates the WebView for a tab (or adopts a window.open() request — keeps OAuth / KEP popups working).</summary>
        async void Wake(Tab t, string url, CoreWebView2NewWindowRequestedEventArgs pending, CoreWebView2Deferral deferral)
        {
            if (env == null || t.View != null) { if (deferral != null) deferral.Complete(); return; }
            var wv = new WebView2 { Dock = DockStyle.Fill, Visible = t == active, DefaultBackgroundColor = Theme.Page };
            t.View = wv;
            content.Controls.Add(wv);
            if (t == active) wv.BringToFront();
            try
            {
                await wv.EnsureCoreWebView2Async(env);
                if (t.View != wv) { if (deferral != null) deferral.Complete(); return; }
                Setup(t, wv);
                if (pending != null) { pending.NewWindow = wv.CoreWebView2; pending.Handled = true; }
                else if (!string.IsNullOrEmpty(url)) wv.CoreWebView2.Navigate(url);
            }
            catch (Exception ex) { if (t.View == wv) ShowToast("Помилка вкладки: " + ex.Message, 3500); }
            finally { if (deferral != null) deferral.Complete(); }
        }

        void Setup(Tab t, WebView2 wv)
        {
            CoreWebView2 c = wv.CoreWebView2;
            CoreWebView2Settings st = c.Settings;
            st.IsStatusBarEnabled = true;
            st.AreDevToolsEnabled = true;
            st.IsPasswordAutosaveEnabled = true;
            st.IsGeneralAutofillEnabled = true;
            st.AreBrowserAcceleratorKeysEnabled = true;
            st.IsZoomControlEnabled = true;
            try { c.Profile.PreferredTrackingPreventionLevel = S.Protection ? CoreWebView2TrackingPreventionLevel.Strict : CoreWebView2TrackingPreventionLevel.Balanced; } catch { }
            try { c.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Auto; } catch { }

            var script = Privacy.PageScript(S.Gpc, S.CookiePopups && S.Protection, Hw.PreferH264);
            var _ = c.AddScriptToExecuteOnDocumentCreatedAsync(script);
            if (S.Gpc) CdpGpc(c);
            if (Hw.Lite) { var __ = c.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", "{\"features\":[{\"name\":\"prefers-reduced-motion\",\"value\":\"reduce\"}]}"); }

            // tracker filters: matching runs natively in the engine; we are called only for listed domains
            foreach (string d in Privacy.Trackers)
            {
                c.AddWebResourceRequestedFilter("*://*." + d + "/*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
                c.AddWebResourceRequestedFilter("*://" + d + "/*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            }
            c.WebResourceRequested += (s, e) => OnRequest(t, e);
            c.NavigationStarting += (s, e) => OnNavStarting(t, c, e);
            c.NavigationCompleted += (s, e) => OnNavCompleted(t, c, e);
            c.SourceChanged += (s, e) => { t.Url = c.Source; if (t == active && !omni.Focused) omni.Text = t.Url; Invalidate(toolR); };
            c.DocumentTitleChanged += (s, e) =>
            {
                t.Title = string.IsNullOrEmpty(c.DocumentTitle) ? t.Url : c.DocumentTitle;
                Store.UpdateLastTitle(t.Url, t.Title);
                if (t == active) Text = t.Title + " — Beryll";
                Invalidate(stripR);
            };
            c.FaviconChanged += async (s, e) =>
            {
                try
                {
                    using (Stream st2 = await c.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png))
                    {
                        if (st2 == null || st2.Length == 0) { t.Icon = null; return; }
                        var ms = new MemoryStream(); st2.CopyTo(ms); ms.Position = 0;
                        Image old = t.Icon; t.Icon = Image.FromStream(ms); if (old != null) old.Dispose();
                        IconCache.Put(Tiles.Host(c.Source), t.Icon);
                    }
                    Invalidate(stripR);
                }
                catch { }
            };
            c.NewWindowRequested += (s, e) =>
            {
                CoreWebView2Deferral d = e.GetDeferral();
                var nt = new Tab { Url = e.Uri, Title = e.Uri };
                tabs.Insert(tabs.IndexOf(t) + 1, nt);
                DoLayout();
                bool foreground = e.IsUserInitiated || e.WindowFeatures.HasPosition || e.WindowFeatures.HasSize;
                if (foreground) { if (active != null && active.View != null) active.View.Visible = false; active = nt; ntp.Visible = false; }
                Wake(nt, null, e, d);
                if (foreground) { omni.Text = nt.Url; Invalidate(); }
            };
            c.WindowCloseRequested += (s, e) => CloseTab(t);
            c.IsDocumentPlayingAudioChanged += (s, e) => { t.Audible = c.IsDocumentPlayingAudio; Invalidate(stripR); };
            c.IsMutedChanged += (s, e) => { t.Muted = c.IsMuted; Invalidate(stripR); };
            c.ContainsFullScreenElementChanged += (s, e) => SetFullscreen(c.ContainsFullScreenElement);
            c.HistoryChanged += (s, e) => Invalidate(toolR);
            c.ProcessFailed += (s, e) =>
            {
                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                {
                    ShowToast("Сторінка аварійно завершилась — перезавантажую");
                    try { c.Reload(); } catch { }
                }
            };
            HookAccelerators(wv);
        }

        async void CdpGpc(CoreWebView2 c)
        {
            try
            {
                await c.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
                await c.CallDevToolsProtocolMethodAsync("Network.setExtraHTTPHeaders", "{\"headers\":{\"Sec-GPC\":\"1\"}}");
            }
            catch { }
        }

        /// <summary>Gets keyboard shortcuts even when the page has focus (the WinForms control keeps its controller private).</summary>
        void HookAccelerators(WebView2 wv)
        {
            try
            {
                FieldInfo f = typeof(WebView2).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).FirstOrDefault(x => x.FieldType == typeof(CoreWebView2Controller));
                var ctl = f != null ? f.GetValue(wv) as CoreWebView2Controller : null;
                if (ctl == null) return;
                ctl.AcceleratorKeyPressed += (s, e) =>
                {
                    if (e.KeyEventKind != CoreWebView2KeyEventKind.KeyDown && e.KeyEventKind != CoreWebView2KeyEventKind.SystemKeyDown) return;
                    Keys key = (Keys)e.VirtualKey | ModifierKeys;
                    if (IsOurShortcut(key)) { e.Handled = true; BeginInvoke((MethodInvoker)delegate { Shortcut(key); }); }
                };
            }
            catch { }
        }

        // ================================================================ navigation & privacy

        public void Navigate(string input)
        {
            string url = ToUrl(input);
            if (url == null) return;
            suggest.Visible = false;
            if (active == null || active.Pinned && active.View != null && !SameSite(active.Url, url)) { NewTab(url, true); return; }
            active.Url = url;
            active.Title = url;
            ntp.Visible = false;
            if (active.View == null) Wake(active, url, null, null);
            else { active.View.Visible = true; active.View.BringToFront(); active.View.CoreWebView2.Navigate(url); active.View.Focus(); }
            omni.Text = url;
            Invalidate();
        }

        static bool SameSite(string a, string b)
        {
            Uri x, y;
            return Uri.TryCreate(a, UriKind.Absolute, out x) && Uri.TryCreate(b, UriKind.Absolute, out y) && Privacy.RegistrableDomain(x.Host) == Privacy.RegistrableDomain(y.Host);
        }

        public string ToUrl(string input)
        {
            string s = (input ?? "").Trim();
            if (s.Length == 0) return null;
            if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || s.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("view-source:", StringComparison.OrdinalIgnoreCase) || s.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                return s;
            if (s.Length > 2 && s[1] == ':' && (s[2] == '\\' || s[2] == '/')) return new Uri(s).AbsoluteUri; // local file path
            bool hasSpace = s.IndexOf(' ') >= 0;
            string hostPart = s.Split('/')[0];
            bool looksHost = !hasSpace && (hostPart.Contains(".") && !hostPart.EndsWith(".") && !hostPart.StartsWith(".") || hostPart.StartsWith("localhost"));
            Uri tmp;
            if (looksHost && Uri.TryCreate("https://" + s, UriKind.Absolute, out tmp) && tmp.Host.Length > 0)
            {
                string tld = tmp.Host.Substring(tmp.Host.LastIndexOf('.') + 1);
                bool ip = System.Net.IPAddress.TryParse(tmp.Host, out _ip);
                if (ip || tmp.Host == "localhost" || tld.Length >= 2 && tld.All(char.IsLetter))
                    return (Privacy.IsLocalHost(tmp.Host) ? "http://" : "https://") + s;
            }
            return S.SearchUrl(s);
        }
        System.Net.IPAddress _ip;

        void OnNavStarting(Tab t, CoreWebView2 c, CoreWebView2NavigationStartingEventArgs e)
        {
            Uri u;
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out u)) return;
            if (S.Protection && S.StripParams && (u.Scheme == "http" || u.Scheme == "https"))
            {
                string clean = Privacy.StripTracking(e.Uri);
                if (clean != null && clean != e.Uri) { e.Cancel = true; c.Navigate(clean); return; }
            }
            if (S.Protection && S.HttpsUpgrade && u.Scheme == "http" && !Privacy.IsLocalHost(u.Host) && !httpOnly.Contains(u.Host))
            {
                e.Cancel = true;
                t.HttpsUpgraded = true;
                t.UpgradedFrom = e.Uri;
                var b = new UriBuilder(u) { Scheme = "https", Port = u.IsDefaultPort ? -1 : u.Port };
                c.Navigate(b.Uri.AbsoluteUri);
                return;
            }
            if (!e.IsRedirected) t.ResetStats();
            t.NavHost = u.Host;
            t.Loading = true;
            spinner.Start();
            Invalidate(stripR); Invalidate(toolR);
        }

        void OnNavCompleted(Tab t, CoreWebView2 c, CoreWebView2NavigationCompletedEventArgs e)
        {
            t.Loading = false;
            if (!tabs.Any(x => x.Loading)) spinner.Stop();
            if (!e.IsSuccess && t.HttpsUpgraded && t.UpgradedFrom != null && IsHttpsFailure(e.WebErrorStatus))
            {
                Uri u = new Uri(t.UpgradedFrom);
                httpOnly.Add(u.Host);                       // the site has no working HTTPS: fall back (session only)
                string back = t.UpgradedFrom;
                t.HttpsUpgraded = false; t.UpgradedFrom = null;
                c.Navigate(back);
                ShowToast("Сайт не підтримує HTTPS — з'єднання не захищене", 3000);
                return;
            }
            t.HttpsUpgraded = false; t.UpgradedFrom = null;
            if (e.IsSuccess) Store.AddHistory(c.Source, c.DocumentTitle);
            Invalidate(stripR); Invalidate(toolR);
        }

        static bool IsHttpsFailure(CoreWebView2WebErrorStatus s)
        {
            switch (s)
            {
                case CoreWebView2WebErrorStatus.CannotConnect: case CoreWebView2WebErrorStatus.ConnectionAborted:
                case CoreWebView2WebErrorStatus.ConnectionReset: case CoreWebView2WebErrorStatus.Disconnected:
                case CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect: case CoreWebView2WebErrorStatus.CertificateExpired:
                case CoreWebView2WebErrorStatus.ClientCertificateContainsErrors: case CoreWebView2WebErrorStatus.CertificateRevoked:
                case CoreWebView2WebErrorStatus.CertificateIsInvalid: case CoreWebView2WebErrorStatus.ServerUnreachable:
                case CoreWebView2WebErrorStatus.Timeout: case CoreWebView2WebErrorStatus.ErrorHttpInvalidServerResponse:
                    return true;
            }
            return false;
        }

        void OnRequest(Tab t, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (!S.Protection) return;
            Uri u;
            if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out u)) return;
            string page = t.NavHost;
            if (string.Equals(u.Host, page, StringComparison.OrdinalIgnoreCase)) return;          // the site itself
            if (S.AllowList.Contains(Privacy.RegistrableDomain(page))) return;                       // protection off for this site
            if (Privacy.SameParty(page, u.Host)) return;                                              // same company (DDG rule)
            string m = Privacy.MatchTracker(u.Host);
            if (m == null) return;
            e.Response = env.CreateWebResourceResponse(null, 403, "Blocked by Beryll", "");
            t.Blocked++;
            int n; t.BlockedDomains.TryGetValue(m, out n); t.BlockedDomains[m] = n + 1;
            S.TotalBlocked++;
            if (t == active) Invalidate(shieldR);
        }

        void ReloadOrStop()
        {
            CoreWebView2 c = ActiveCore;
            if (c == null) { if (active != null && active.Asleep) Activate(active); return; }
            if (active.Loading) c.Stop(); else c.Reload();
        }

        // ================================================================ memory governor (self-adaptive)

        void Govern()
        {
            try
            {
                DateTime now = DateTime.Now;
                var bg = tabs.Where(t => t != active && t.View != null && t.View.CoreWebView2 != null).OrderBy(t => t.LastActive).ToList();
                // 1) background tabs: low memory target + freeze after 30 s
                foreach (Tab t in bg)
                    if (!t.Frozen && (now - t.LastActive).TotalSeconds > 30 && !Exempt(t))
                    {
                        try
                        {
                            t.View.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                            var _ = t.View.CoreWebView2.TrySuspendAsync();
                            t.Frozen = true;
                        }
                        catch { }
                    }
                // 2) sleep by time, 3) by live-tab budget, 4) by memory pressure
                int live = tabs.Count(t => t.View != null);
                long free = Native.FreeMb();
                foreach (Tab t in bg)
                {
                    if (Exempt(t)) continue;
                    bool old = (now - t.LastActive).TotalMinutes >= Hw.SleepMinutes;
                    bool over = live > Hw.LiveTabs;
                    bool pressure = free < Hw.ReserveMb;
                    if (old || over || pressure)
                    {
                        SleepTab(t);
                        live--;
                        if (pressure) break; // re-measure next tick
                    }
                }
            }
            catch { }
        }

        bool Exempt(Tab t) { return t.Pinned || t.KeepAwake || t.Audible || t.Loading; }

        async void SleepTab(Tab t)
        {
            if (t.View == null || t.View.CoreWebView2 == null) return;
            try
            {
                // never lose what the user typed: tabs with unsaved input stay awake
                string dirty = await t.View.CoreWebView2.ExecuteScriptAsync("window.__beryllDirty===true");
                if (dirty == "true" || t.View == null || t == active) return;
            }
            catch { }
            long before = Native.FreeMb();
            KillView(t);
            t.Frozen = false;
            Invalidate(stripR);
            await Task.Delay(2500);
            long gain = Native.FreeMb() - before;
            if (gain > 0) { t.LastMemMb = gain; S.TotalFreedMb += gain; }
        }

        public void SleepAllBackground()
        {
            foreach (Tab t in tabs.Where(x => x != active && x.View != null && !Exempt(x)).ToList()) SleepTab(t);
            ShowToast("Фонові вкладки приспано — пам'ять звільняється");
        }

        // ================================================================ omnibox & suggestions

        void ShowSuggestions()
        {
            var items = Store.Suggest(omni.Text, 7).ToList();
            suggest.BeginUpdate();
            suggest.Items.Clear();
            foreach (Bookmark b in items) suggest.Items.Add(b);
            suggest.EndUpdate();
            if (items.Count == 0) { suggest.Visible = false; return; }
            PlaceSuggest();
            suggest.Visible = true;
            suggest.BringToFront();
        }

        void PlaceSuggest()
        {
            suggest.BackColor = Theme.MenuBg;
            suggest.ForeColor = Theme.Text;
            suggest.SetBounds(omniR.Left, omniR.Bottom + (int)(4 * k), omniR.Width, suggest.Items.Count * suggest.ItemHeight + 4);
        }

        void DrawSuggestion(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var b = (Bookmark)suggest.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var br = new SolidBrush(sel ? Theme.MenuSel : Theme.MenuBg)) e.Graphics.FillRectangle(br, e.Bounds);
            var r = new Rectangle(e.Bounds.X + (int)(14 * k), e.Bounds.Y, e.Bounds.Width - (int)(28 * k), e.Bounds.Height);
            int half = r.Width * 55 / 100;
            TextRenderer.DrawText(e.Graphics, b.Title, fUi, new Rectangle(r.X, r.Y, half, r.Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, b.Url, fSmall, new Rectangle(r.X + half + (int)(10 * k), r.Y, r.Width - half - (int)(10 * k), r.Height), Theme.Beryl, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        void GoSuggestion()
        {
            var b = suggest.SelectedItem as Bookmark;
            if (b != null) Navigate(b.Url);
        }

        void OmniKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (suggest.Visible && suggest.SelectedIndex >= 0) GoSuggestion();
                else if (e.Alt) NewTab(omni.Text, true);
                else Navigate(omni.Text);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                suggest.Visible = false;
                omni.Text = active != null && !active.IsNewTab ? active.Url : "";
                if (active != null && active.View != null) active.View.Focus();
            }
            else if (e.KeyCode == Keys.Down && suggest.Visible)
            {
                e.SuppressKeyPress = true;
                suggest.SelectedIndex = Math.Min(suggest.Items.Count - 1, suggest.SelectedIndex + 1);
            }
            else if (e.KeyCode == Keys.Up && suggest.Visible)
            {
                e.SuppressKeyPress = true;
                suggest.SelectedIndex = Math.Max(-1, suggest.SelectedIndex - 1);
            }
        }

        // ================================================================ keyboard

        static readonly Keys[] Ours = {
            Keys.Control | Keys.T, Keys.Control | Keys.N, Keys.Control | Keys.W, Keys.Control | Keys.F4, Keys.Control | Keys.Shift | Keys.T,
            Keys.Control | Keys.Tab, Keys.Control | Keys.Shift | Keys.Tab, Keys.Control | Keys.PageDown, Keys.Control | Keys.PageUp,
            Keys.Control | Keys.L, Keys.Alt | Keys.D, Keys.F6, Keys.Control | Keys.D, Keys.Control | Keys.Shift | Keys.B,
            Keys.Control | Keys.H, Keys.Control | Keys.J, Keys.Control | Keys.Shift | Keys.Delete, Keys.F11, Keys.Alt | Keys.Home,
            Keys.Control | Keys.D1, Keys.Control | Keys.D2, Keys.Control | Keys.D3, Keys.Control | Keys.D4, Keys.Control | Keys.D5,
            Keys.Control | Keys.D6, Keys.Control | Keys.D7, Keys.Control | Keys.D8, Keys.Control | Keys.D9 };

        static bool IsOurShortcut(Keys k) { return Array.IndexOf(Ours, k) >= 0; }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (IsOurShortcut(keyData)) { Shortcut(keyData); return true; }
            if (keyData == Keys.F5 || keyData == (Keys.Control | Keys.R)) { ReloadOrStop(); return true; }
            if (keyData == (Keys.Alt | Keys.Left)) { var c = ActiveCore; if (c != null && c.CanGoBack) c.GoBack(); return true; }
            if (keyData == (Keys.Alt | Keys.Right)) { var c = ActiveCore; if (c != null && c.CanGoForward) c.GoForward(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void Shortcut(Keys key)
        {
            int i = tabs.IndexOf(active);
            switch (key)
            {
                case Keys.Control | Keys.T: case Keys.Control | Keys.N: NewTab(null, true); break;
                case Keys.Control | Keys.W: case Keys.Control | Keys.F4: if (active != null) CloseTab(active); break;
                case Keys.Control | Keys.Shift | Keys.T: if (closed.Count > 0) NewTab(closed.Pop(), true); break;
                case Keys.Control | Keys.Tab: case Keys.Control | Keys.PageDown: if (tabs.Count > 1) Activate(tabs[(i + 1) % tabs.Count]); break;
                case Keys.Control | Keys.Shift | Keys.Tab: case Keys.Control | Keys.PageUp: if (tabs.Count > 1) Activate(tabs[(i - 1 + tabs.Count) % tabs.Count]); break;
                case Keys.Control | Keys.L: case Keys.Alt | Keys.D: case Keys.F6: omni.Focus(); omni.SelectAll(); break;
                case Keys.Control | Keys.D: ToggleBookmark(); break;
                case Keys.Control | Keys.Shift | Keys.B: S.BookmarksBar = !S.BookmarksBar; DoLayout(); break;
                case Keys.Control | Keys.H: Dialogs.History(this); break;
                case Keys.Control | Keys.J: { var c = ActiveCore; if (c != null) c.OpenDefaultDownloadDialog(); break; }
                case Keys.Control | Keys.Shift | Keys.Delete: Fire(); break;
                case Keys.F11: SetFullscreen(!fullscreen); break;
                case Keys.Alt | Keys.Home: Navigate("beryll://newtab"); break;
                case Keys.Control | Keys.D9: if (tabs.Count > 0) Activate(tabs[tabs.Count - 1]); break;
                default:
                    if ((key & Keys.Control) != 0)
                    {
                        int n = (int)(key & Keys.KeyCode) - (int)Keys.D1;
                        if (n >= 0 && n < 8 && n < tabs.Count) Activate(tabs[n]);
                    }
                    break;
            }
        }

        public void SetFullscreen(bool on)
        {
            if (on == fullscreen) return;
            fullscreen = on;
            if (on)
            {
                prevMax = WindowState == FormWindowState.Maximized;
                if (prevMax) WindowState = FormWindowState.Normal;
                prevBounds = Bounds;
                Bounds = Screen.FromControl(this).Bounds;
            }
            else
            {
                Bounds = prevBounds;
                if (prevMax) WindowState = FormWindowState.Maximized;
            }
            DoLayout();
        }

        // ================================================================ bookmarks, menus

        void ToggleBookmark()
        {
            if (active == null || active.IsNewTab) return;
            Bookmark b = Store.Bookmarks.FirstOrDefault(x => x.Url == active.Url);
            if (b != null) { Store.Bookmarks.Remove(b); ShowToast("Закладку видалено"); }
            else { Store.Bookmarks.Add(new Bookmark { Title = active.Title, Url = active.Url }); ShowToast("Додано в закладки"); }
            Store.SaveBookmarks();
            DoLayout();
        }

        ContextMenuStrip NewMenu() { return new ContextMenuStrip { Renderer = new MenuRenderer(), Font = fUi, ShowImageMargin = false, ShowCheckMargin = true }; }

        static ToolStripMenuItem Item(ToolStripItemCollection items, string text, string keys, Action a, bool check = false)
        {
            var it = new ToolStripMenuItem(text) { Checked = check };
            if (keys != null) it.ShortcutKeyDisplayString = keys;
            it.Click += delegate { a(); };
            items.Add(it);
            return it;
        }

        void TabMenu(Tab t, Point p)
        {
            var m = NewMenu();
            Item(m.Items, "Нова вкладка праворуч", null, () => NewTab(null, true, t));
            Item(m.Items, "Оновити", null, () => { if (t.View != null) t.View.CoreWebView2.Reload(); else Activate(t); });
            Item(m.Items, "Дублювати", null, () => NewTab(t.Url, true, t));
            Item(m.Items, t.Pinned ? "Відкріпити" : "Закріпити", null, () => { t.Pinned = !t.Pinned; tabs.Remove(t); tabs.Insert(t.Pinned ? tabs.Count(x => x.Pinned) : tabs.Count(x => x.Pinned), t); DoLayout(); });
            Item(m.Items, "Не присипляти цю вкладку", null, () => t.KeepAwake = !t.KeepAwake, t.KeepAwake);
            if (t.View != null) Item(m.Items, t.Muted ? "Увімкнути звук" : "Вимкнути звук", null, () => { t.View.CoreWebView2.IsMuted = !t.View.CoreWebView2.IsMuted; });
            if (t.View != null && t != active) Item(m.Items, "Приспати зараз", null, () => SleepTab(t));
            m.Items.Add(new ToolStripSeparator());
            Item(m.Items, "Закрити", "Ctrl+W", () => CloseTab(t));
            Item(m.Items, "Закрити інші", null, () => { foreach (Tab x in tabs.Where(x => x != t && !x.Pinned).ToList()) CloseTab(x); });
            m.Show(this, p);
        }

        void BookmarkMenu(int i, Point p)
        {
            Bookmark b = Store.Bookmarks[i];
            var m = NewMenu();
            Item(m.Items, "Відкрити в новій вкладці", null, () => NewTab(b.Url, true));
            Item(m.Items, "Перейменувати…", null, () =>
            {
                string n = Dialogs.Ask(this, "Закладка", "Назва:", b.Title);
                if (!string.IsNullOrEmpty(n)) { b.Title = n; Store.SaveBookmarks(); DoLayout(); }
            });
            Item(m.Items, "Видалити", null, () => { Store.Bookmarks.Remove(b); Store.SaveBookmarks(); DoLayout(); });
            m.Show(this, p);
        }

        void ShowMenu()
        {
            var m = NewMenu();
            CoreWebView2 c = ActiveCore;
            Item(m.Items, "Нова вкладка", "Ctrl+T", () => NewTab(null, true));
            Item(m.Items, "Відновити закриту вкладку", "Ctrl+Shift+T", () => { if (closed.Count > 0) NewTab(closed.Pop(), true); }).Enabled = closed.Count > 0;
            m.Items.Add(new ToolStripSeparator());
            Item(m.Items, "Історія", "Ctrl+H", () => Dialogs.History(this));
            Item(m.Items, "Завантаження", "Ctrl+J", () => { if (c != null) c.OpenDefaultDownloadDialog(); }).Enabled = c != null;
            Item(m.Items, "Панель закладок", "Ctrl+Shift+B", () => { S.BookmarksBar = !S.BookmarksBar; DoLayout(); }, S.BookmarksBar);
            m.Items.Add(new ToolStripSeparator());
            Item(m.Items, "Друк…", "Ctrl+P", () => { if (c != null) c.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); }).Enabled = c != null;
            Item(m.Items, "Знайти на сторінці…", "Ctrl+F", () => { if (active != null && active.View != null) { active.View.Focus(); SendKeys.Send("^f"); } }).Enabled = c != null;
            var zoom = new ToolStripMenuItem("Масштаб");
            foreach (double z in new[] { 0.67, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75 })
            {
                double zz = z;
                Item(zoom.DropDownItems, Math.Round(z * 100) + "%", null, () => { if (active != null && active.View != null) active.View.ZoomFactor = zz; },
                    active != null && active.View != null && Math.Abs(active.View.ZoomFactor - z) < 0.01);
            }
            zoom.Enabled = c != null;
            m.Items.Add(zoom);
            Item(m.Items, "На весь екран", "F11", () => SetFullscreen(!fullscreen), fullscreen);
            m.Items.Add(new ToolStripSeparator());
            var mem = new ToolStripMenuItem("Пам'ять і швидкість");
            Item(mem.DropDownItems, "Приспати фонові вкладки зараз", null, SleepAllBackground);
            Item(mem.DropDownItems, "Діагностика…", null, () => Dialogs.Diagnostics(this, tabs));
            m.Items.Add(mem);
            Item(m.Items, "Налаштування…", null, () => Dialogs.SettingsDialog(this));
            Item(m.Items, "Про Beryll", null, () => Dialogs.About(this, env));
            m.Show(this, new Point(menuR.Right - (int)(260 * k), menuR.Bottom + 2));
        }

        void ShowDashboard()
        {
            if (active == null || active.IsNewTab) { ShowToast("Захист активний: заблоковано трекерів усього — " + S.TotalBlocked); return; }
            Dialogs.Dashboard(this, active, PointToScreen(new Point(shieldR.Right, shieldR.Bottom + 4)), httpOnly.Contains(active.Host));
        }

        public void ReloadActive() { var c = ActiveCore; if (c != null) c.Reload(); }

        // ================================================================ Fire button (DuckDuckGo-style)

        public async void Fire()
        {
            int fp = S.Fireproof.Count;
            if (!Dialogs.Confirm(this, "Вогонь 🔥", "Закрити всі вкладки й стерти дані перегляду (cookies, кеш, історію)?" +
                (fp > 0 ? "\n\nСайти Fireproof (" + fp + ") залишаться залогіненими." : "") + "\nЗбережені паролі не видаляються.", "Стерти")) return;
            Cursor = Cursors.WaitCursor;
            try
            {
                Tab host = tabs.FirstOrDefault(t => t.View != null && t.View.CoreWebView2 != null);
                if (host == null) { host = NewTab("about:blank", false); await Task.Delay(1500); }
                CoreWebView2 core = host.View != null ? host.View.CoreWebView2 : null;
                if (core != null)
                {
                    CoreWebView2CookieManager cm = core.CookieManager;
                    var keep = new List<CoreWebView2Cookie>();
                    foreach (string site in S.Fireproof)
                        foreach (string pre in new[] { "https://", "https://www." })
                            try { keep.AddRange(await cm.GetCookiesAsync(pre + site)); } catch { }
                    var kinds = CoreWebView2BrowsingDataKinds.AllDomStorage | CoreWebView2BrowsingDataKinds.Cookies | CoreWebView2BrowsingDataKinds.DiskCache |
                                CoreWebView2BrowsingDataKinds.BrowsingHistory | CoreWebView2BrowsingDataKinds.DownloadHistory | CoreWebView2BrowsingDataKinds.GeneralAutofill;
                    await core.Profile.ClearBrowsingDataAsync(kinds);
                    foreach (CoreWebView2Cookie ck in keep) try { cm.AddOrUpdateCookie(cm.CopyCookie(ck)); } catch { }
                }
                foreach (Tab t in tabs.ToList()) { tabs.Remove(t); KillView(t); }
                closed.Clear();
                Store.ClearHistory();
                try { File.Delete(Paths.File("session.tsv")); } catch { }
                active = null;
                NewTab(null, true);
                ShowToast("🔥 Дані стерто" + (fp > 0 ? " · Fireproof-сайти збережено" : ""), 3000);
            }
            catch (Exception ex) { ShowToast("Помилка очищення: " + ex.Message, 3500); }
            finally { Cursor = Cursors.Default; }
        }

        public IList<Tab> Tabs { get { return tabs; } }
        public float UiScale { get { return k; } }
        public Font UiFont { get { return fUi; } }
        public string EngineVersion { get { return env != null ? env.BrowserVersionString : "—"; } }
    }
}
