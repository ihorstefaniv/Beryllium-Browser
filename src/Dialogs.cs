using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Beryll
{
    /// <summary>Native "new tab" page (no web engine, 0 MB): search, speed-dial tiles with icons (add / edit / remove / hide).</summary>
    sealed class NewTabPage : Control
    {
        sealed class TileSlot { public Rectangle R, Menu; public Bookmark B; public bool User; public bool Add; }

        readonly BrowserForm f;
        readonly TextBox search = new TextBox { BorderStyle = BorderStyle.None };
        readonly List<TileSlot> slots = new List<TileSlot>();
        float k = 1;
        Font fTitle, fUi, fSmall, fLetter, fIcons;
        Rectangle searchR;
        int hover = -1;
        bool hoverMenu;

        public NewTabPage(BrowserForm f)
        {
            this.f = f;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; f.Navigate(search.Text); search.Text = ""; } };
            search.GotFocus += delegate { Invalidate(); };
            search.LostFocus += delegate { Invalidate(); };
            search.TextChanged += delegate { Invalidate(searchR); };
            Controls.Add(search);
        }

        public void SetScale(float s)
        {
            k = s;
            if (fTitle != null) { fTitle.Dispose(); fUi.Dispose(); fSmall.Dispose(); fLetter.Dispose(); fIcons.Dispose(); }
            fTitle = new Font("Segoe UI Semilight", 30f * k, FontStyle.Regular, GraphicsUnit.Pixel);
            fUi = new Font("Segoe UI", 15f * k, FontStyle.Regular, GraphicsUnit.Pixel);
            fSmall = new Font("Segoe UI", 12.5f * k, FontStyle.Regular, GraphicsUnit.Pixel);
            fLetter = new Font("Segoe UI Semibold", 19f * k, FontStyle.Regular, GraphicsUnit.Pixel);
            fIcons = new Font("Segoe MDL2 Assets", 13f * k, FontStyle.Regular, GraphicsUnit.Pixel);
            search.Font = fUi;
            Relayout();
        }

        public void RefreshData() { Relayout(); Invalidate(); }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

        void Relayout()
        {
            if (fUi == null) return;
            int w = Width;
            slots.Clear();
            var items = Tiles.Items();
            int count = items.Count + (items.Count < Tiles.Max ? 1 : 0);
            int ts = (int)(112 * k), th = (int)(112 * k), gap = (int)(16 * k);
            int cols = Math.Max(1, Math.Min(5, Math.Min(count, (w - (int)(40 * k) + gap) / (ts + gap))));
            int rows = (count + cols - 1) / cols;
            int gridW = cols * ts + (cols - 1) * gap, gridH = rows * th + (rows - 1) * gap;

            int blockH = (int)(76 * k) + (int)(40 * k) + (int)(46 * k) + (int)(36 * k) + gridH;
            int top = Math.Max((int)(40 * k), (Height - blockH) / 2 - (int)(20 * k));
            int sw = Math.Min((int)(600 * k), w - (int)(40 * k));
            searchR = new Rectangle((w - sw) / 2, top + (int)(116 * k), sw, (int)(46 * k));
            search.SetBounds(searchR.Left + (int)(46 * k), searchR.Top + (searchR.Height - search.PreferredHeight) / 2, searchR.Width - (int)(62 * k), search.PreferredHeight);
            search.BackColor = Theme.Dark ? Color.FromArgb(48, 50, 56) : Color.White;
            search.ForeColor = Theme.Text;

            // one even grid: the whole block is centred, rows are left-aligned inside it
            int x0 = (w - gridW) / 2, y0 = searchR.Bottom + (int)(36 * k);
            for (int i = 0; i < count; i++)
            {
                var r = new Rectangle(x0 + (i % cols) * (ts + gap), y0 + (i / cols) * (th + gap), ts, th);
                var slot = new TileSlot { R = r, Menu = new Rectangle(r.Right - (int)(28 * k), r.Top + (int)(4 * k), (int)(24 * k), (int)(24 * k)) };
                if (i < items.Count) { slot.B = items[i].Item1; slot.User = items[i].Item2; } else slot.Add = true;
                slots.Add(slot);
                if (slot.B != null && IconCache.Get(Tiles.Host(slot.B.Url)) == null)
                    IconCache.Fetch(Tiles.Host(slot.B.Url), () => { try { BeginInvoke((MethodInvoker)Invalidate); } catch { } });
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Theme.Page);
            if (fUi == null) return;

            // logo
            int tile = (int)(76 * k);
            Size ts = TextRenderer.MeasureText("Beryll", fTitle);
            int lx = (Width - (tile + (int)(16 * k) + ts.Width)) / 2, ly = searchR.Top - (int)(116 * k);
            var tr = new Rectangle(lx, ly, tile, tile);
            f.DrawElementTile(g, tr, true);
            TextRenderer.DrawText(g, "Beryll", fTitle, new Rectangle(tr.Right + (int)(16 * k), tr.Top, ts.Width + 4, tile), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // search
            using (GraphicsPath p = Rr(searchR, searchR.Height / 2f))
            {
                using (var b = new SolidBrush(search.BackColor)) g.FillPath(b, p);
                using (var pen = new Pen(search.Focused ? Theme.Beryl : Theme.Border, 1.4f * k)) g.DrawPath(pen, p);
            }
            Glyph.Draw(g, "", fIcons, new Rectangle(searchR.Left + (int)(14 * k), searchR.Top, (int)(24 * k), searchR.Height), Theme.TextDim);
            if (search.Text.Length == 0 && !search.Focused)
                TextRenderer.DrawText(g, "Пошук у " + Settings.SearchNames[Math.Max(0, Math.Min(3, f.S.Search))].Split(' ')[0] + " або адреса сайту", fUi,
                    new Rectangle(search.Left, searchR.Top, search.Width, searchR.Height), Theme.TextDim, TextFormatFlags.VerticalCenter);

            // tiles
            for (int i = 0; i < slots.Count; i++)
            {
                TileSlot s = slots[i];
                bool h = i == hover;
                if (h)
                    using (GraphicsPath p = Rr(s.R, 12 * k)) using (var b = new SolidBrush(Theme.Dark ? Color.FromArgb(44, 46, 52) : Color.FromArgb(232, 236, 240))) g.FillPath(b, p);
                int cs = (int)(52 * k);
                var circle = new Rectangle(s.R.X + (s.R.Width - cs) / 2, s.R.Y + (int)(16 * k), cs, cs);
                using (var b = new SolidBrush(Theme.Dark ? Color.FromArgb(58, 60, 68) : Color.FromArgb(233, 238, 242))) g.FillEllipse(b, circle);
                string label;
                if (s.Add)
                {
                    Glyph.Draw(g, "", fIcons, circle, Theme.Text);
                    label = "Додати сайт";
                }
                else
                {
                    Image icon = IconCache.Get(Tiles.Host(s.B.Url));
                    if (icon != null)
                    {
                        int is2 = (int)(26 * k);
                        g.DrawImage(icon, new Rectangle(circle.X + (cs - is2) / 2, circle.Y + (cs - is2) / 2, is2, is2));
                    }
                    else
                    {
                        string t = (s.B.Title ?? "?").Trim();
                        TextRenderer.DrawText(g, t.Length > 0 ? t.Substring(0, 1).ToUpperInvariant() : "?", fLetter, circle, Theme.Beryl, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                    label = s.B.Title;
                    if (h)
                    {
                        if (hoverMenu) using (var b = new SolidBrush(Theme.Hover)) g.FillEllipse(b, s.Menu);
                        Glyph.Draw(g, "", fIcons, s.Menu, Theme.TextDim);
                    }
                }
                TextRenderer.DrawText(g, label, fSmall, new Rectangle(s.R.X + (int)(6 * k), circle.Bottom + (int)(10 * k), s.R.Width - (int)(12 * k), (int)(20 * k)), Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }

            string stats = "Заблоковано трекерів: " + f.S.TotalBlocked.ToString("N0") + "    ·    Звільнено пам'яті: ≈" + f.S.TotalFreedMb.ToString("N0") + " МБ    ·    " + f.Hw.TierName;
            TextRenderer.DrawText(g, stats, fSmall, new Rectangle(0, Height - (int)(44 * k), Width, (int)(30 * k)), Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        static GraphicsPath Rr(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1; bool hm = false;
            for (int i = 0; i < slots.Count; i++) if (slots[i].R.Contains(e.Location)) { h = i; hm = !slots[i].Add && slots[i].Menu.Contains(e.Location); }
            Cursor = h >= 0 || searchR.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            if (h != hover || hm != hoverMenu) { hover = h; hoverMenu = hm; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hover != -1) { hover = -1; Invalidate(); } }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (searchR.Contains(e.Location)) { search.Focus(); return; }
            foreach (TileSlot s in slots)
            {
                if (!s.R.Contains(e.Location)) continue;
                if (s.Add) { AddTile(); return; }
                if (s.Menu.Contains(e.Location) || e.Button == MouseButtons.Right) { TileMenu(s, e.Location); return; }
                if (e.Button == MouseButtons.Middle) f.NewTab(s.B.Url, false); else f.Navigate(s.B.Url);
                return;
            }
        }

        void AddTile()
        {
            string[] r = Dialogs.AskTile(f, "Додати сайт", "", "");
            if (r == null) return;
            Tiles.User.Add(new Bookmark { Title = r[0], Url = r[1] });
            Tiles.Hidden.Remove(Tiles.Host(r[1]));
            Tiles.Save();
            RefreshData();
        }

        void TileMenu(TileSlot s, Point p)
        {
            var m = new ContextMenuStrip { Renderer = new MenuRenderer(), Font = fUi, ShowImageMargin = false };
            m.Items.Add("Відкрити в новій вкладці", null, delegate { f.NewTab(s.B.Url, true); });
            m.Items.Add("Змінити…", null, delegate
            {
                string[] r = Dialogs.AskTile(f, "Змінити сайт", s.B.Title, s.B.Url);
                if (r == null) return;
                if (s.User) { s.B.Title = r[0]; s.B.Url = r[1]; }
                else { Tiles.Hidden.Add(Tiles.Host(s.B.Url)); Tiles.User.Add(new Bookmark { Title = r[0], Url = r[1] }); }
                Tiles.Save(); RefreshData();
            });
            m.Items.Add(s.User ? "Видалити" : "Прибрати з частих", null, delegate
            {
                if (s.User) Tiles.User.Remove(s.B); else Tiles.Hidden.Add(Tiles.Host(s.B.Url));
                Tiles.Save(); RefreshData();
            });
            m.Show(this, p);
        }
    }

    /// <summary>Simple on/off switch drawn in the theme colours.</summary>
    sealed class Switch : Control
    {
        bool on;
        public event EventHandler Changed;
        public Switch() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); Cursor = Cursors.Hand; }
        public bool On { get { return on; } set { if (on == value) return; on = value; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); } }
        protected override void OnClick(EventArgs e) { base.OnClick(e); On = !On; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.MenuBg);
            float h = Height - 4, w = Math.Min(Width - 2, h * 1.9f);
            var r = new RectangleF(Width - w - 1, 2, w, h);
            using (var p = new GraphicsPath())
            {
                p.AddArc(r.X, r.Y, h, h, 90, 180); p.AddArc(r.Right - h, r.Y, h, h, 270, 180); p.CloseFigure();
                using (var b = new SolidBrush(on ? Theme.Beryl : Theme.Border)) g.FillPath(b, p);
            }
            float d = h - 6;
            g.FillEllipse(Brushes.White, on ? r.Right - d - 3 : r.X + 3, r.Y + 3, d, d);
            var tr = new Rectangle(0, 0, (int)(Width - w - 8), Height);
            TextRenderer.DrawText(g, Text, Font, tr, Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    static class Dialogs
    {
        static Form Base(BrowserForm owner, string title, int w, int h)
        {
            float k = owner.UiScale;
            var f = new Form
            {
                Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent, BackColor = Theme.MenuBg, ForeColor = Theme.Text, AutoScaleMode = AutoScaleMode.None,
                ClientSize = new Size((int)(w * k), (int)(h * k)), Font = owner.UiFont
            };
            try { f.Icon = owner.Icon; } catch { }
            f.HandleCreated += delegate { Native.DarkTitle(f.Handle, Theme.Dark); };
            return f;
        }

        static Label Lbl(Form f, string text, int x, int y, int w, int h, float k, bool dim = false, Font font = null)
        {
            var l = new Label { Text = text, Location = new Point((int)(x * k), (int)(y * k)), Size = new Size((int)(w * k), (int)(h * k)), ForeColor = dim ? Theme.TextDim : Theme.Text, BackColor = Color.Transparent };
            if (font != null) l.Font = font;
            f.Controls.Add(l);
            return l;
        }

        static Button Btn(Form f, string text, int x, int y, int w, float k, bool accent = false)
        {
            var b = new Button { Text = text, Location = new Point((int)(x * k), (int)(y * k)), Size = new Size((int)(w * k), (int)(34 * k)), FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
            b.BackColor = accent ? Theme.Accent : Theme.Toolbar;
            b.ForeColor = accent ? Color.White : Theme.Text;
            b.FlatAppearance.BorderColor = accent ? Theme.Accent : Theme.Border;
            f.Controls.Add(b);
            return b;
        }

        static Switch Sw(Form f, string text, int x, int y, int w, float k, bool on)
        {
            var s = new Switch { Text = text, Location = new Point((int)(x * k), (int)(y * k)), Size = new Size((int)(w * k), (int)(28 * k)), Font = f.Font };
            s.On = on;
            f.Controls.Add(s);
            return s;
        }

        static ComboBox Combo(Form f, int x, int y, int w, float k, string[] items, int sel)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Location = new Point((int)(x * k), (int)(y * k)), Width = (int)(w * k), BackColor = Theme.Toolbar, ForeColor = Theme.Text };
            c.Items.AddRange(items);
            c.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, sel));
            f.Controls.Add(c);
            return c;
        }

        public static bool Confirm(BrowserForm owner, string title, string text, string yes)
        {
            float k = owner.UiScale;
            using (Form f = Base(owner, title, 440, 200))
            {
                var l = Lbl(f, text, 20, 18, 400, 120, k);
                var ok = Btn(f, yes, 440 - 20 - 240, 150, 120, k, true);
                var no = Btn(f, "Скасувати", 440 - 20 - 110, 150, 110, k);
                ok.DialogResult = DialogResult.OK; no.DialogResult = DialogResult.Cancel;
                f.AcceptButton = ok; f.CancelButton = no;
                return f.ShowDialog(owner) == DialogResult.OK;
            }
        }

        public static string Ask(BrowserForm owner, string title, string prompt, string value)
        {
            float k = owner.UiScale;
            using (Form f = Base(owner, title, 420, 140))
            {
                Lbl(f, prompt, 18, 14, 380, 24, k);
                var t = new TextBox { Text = value, Location = new Point((int)(18 * k), (int)(42 * k)), Width = (int)(384 * k), BackColor = Theme.Toolbar, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
                f.Controls.Add(t);
                var ok = Btn(f, "OK", 420 - 18 - 220, 92, 100, k, true);
                var no = Btn(f, "Скасувати", 420 - 18 - 110, 92, 110, k);
                ok.DialogResult = DialogResult.OK; no.DialogResult = DialogResult.Cancel;
                f.AcceptButton = ok; f.CancelButton = no;
                return f.ShowDialog(owner) == DialogResult.OK ? t.Text.Trim() : null;
            }
        }

        /// <summary>Name + address dialog for speed-dial tiles. Returns {title, url} or null.</summary>
        public static string[] AskTile(BrowserForm owner, string title, string name, string url)
        {
            float k = owner.UiScale;
            using (Form f = Base(owner, title, 440, 210))
            {
                Lbl(f, "Назва", 18, 14, 400, 22, k);
                var tn = new TextBox { Text = name, Location = new Point((int)(18 * k), (int)(38 * k)), Width = (int)(404 * k), BackColor = Theme.Toolbar, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
                Lbl(f, "Адреса сайту", 18, 76, 400, 22, k);
                var tu = new TextBox { Text = url, Location = new Point((int)(18 * k), (int)(100 * k)), Width = (int)(404 * k), BackColor = Theme.Toolbar, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle };
                f.Controls.Add(tn); f.Controls.Add(tu);
                var ok = Btn(f, "Зберегти", 440 - 18 - 240, 160, 120, k, true);
                var no = Btn(f, "Скасувати", 440 - 18 - 110, 160, 110, k);
                ok.DialogResult = DialogResult.OK; no.DialogResult = DialogResult.Cancel;
                f.AcceptButton = ok; f.CancelButton = no;
                f.Shown += delegate { (url.Length == 0 ? tu : tn).Focus(); };
                while (f.ShowDialog(owner) == DialogResult.OK)
                {
                    string u = owner.ToUrl(tu.Text);
                    Uri parsed;
                    if (u == null || !Uri.TryCreate(u, UriKind.Absolute, out parsed) || (parsed.Scheme != "https" && parsed.Scheme != "http") || u.Contains("/search?") || u.Contains("duckduckgo.com/?q=") && !tu.Text.Contains("duckduckgo"))
                    {
                        MessageBox.Show(f, "Вкажіть адресу сайту, наприклад: rozetka.com.ua", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        continue;
                    }
                    string n = tn.Text.Trim();
                    if (n.Length == 0) { n = parsed.Host.StartsWith("www.") ? parsed.Host.Substring(4) : parsed.Host; }
                    return new[] { n, u };
                }
                return null;
            }
        }

        // ---------------------------------------------------------------- privacy dashboard (shield)

        public static void Dashboard(BrowserForm owner, Tab t, Point anchorRightTop, bool httpFallback)
        {
            float k = owner.UiScale;
            var f = new Form
            {
                FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, BackColor = Theme.MenuBg, ForeColor = Theme.Text,
                Font = owner.UiFont, AutoScaleMode = AutoScaleMode.None, TopMost = false
            };
            int w = (int)(360 * k);
            string site = Privacy.RegistrableDomain(t.Host);
            var big = new Font("Segoe UI Semibold", 16f * k, FontStyle.Regular, GraphicsUnit.Pixel);
            int y = 16;
            Lbl(f, site.Length > 0 ? site : t.Host, 18, y, 324, 28, k, false, big); y += 34;
            bool https = t.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            Lbl(f, https ? "🔒 З'єднання зашифроване (HTTPS)" : httpFallback ? "⚠ Сайт не підтримує HTTPS — з'єднання не захищене" : "⚠ З'єднання не захищене", 18, y, 324, 24, k, !https); y += 30;
            bool on = owner.S.Protection && !owner.S.AllowList.Contains(site);
            var prot = Sw(f, "Захист на цьому сайті", 18, y, 324, k, on); y += 36;
            prot.Changed += delegate
            {
                if (prot.On) owner.S.AllowList.Remove(site); else owner.S.AllowList.Add(site);
                owner.S.Save();
                owner.ReloadActive();
            };
            prot.Enabled = owner.S.Protection;
            var fire = Sw(f, "Fireproof (вогонь не стирає вхід)", 18, y, 324, k, owner.S.Fireproof.Contains(site)); y += 40;
            fire.Changed += delegate { if (fire.On) owner.S.Fireproof.Add(site); else owner.S.Fireproof.Remove(site); owner.S.Save(); };

            Lbl(f, on ? "Заблоковано трекерів: " + t.Blocked : "Захист вимкнено для цього сайту", 18, y, 324, 24, k, false, new Font("Segoe UI Semibold", 13f * k, FontStyle.Regular, GraphicsUnit.Pixel)); y += 28;
            var list = string.Join("\n", t.BlockedDomains.OrderByDescending(x => x.Value).Take(12).Select(x => "•  " + x.Key + (x.Value > 1 ? "  ×" + x.Value : "")));
            if (list.Length == 0) list = on ? "На цій сторінці трекерів не знайдено (або їх зупинив вбудований захист рушія)." : "";
            int lines = Math.Max(1, list.Split('\n').Length);
            Lbl(f, list, 18, y, 324, Math.Min(12, lines) * 20 + 6, k, true); y += Math.Min(12, lines) * 20 + 12;
            Lbl(f, "Також: Global Privacy Control " + (owner.S.Gpc ? "увімкнено" : "вимкнено") + " · очищення посилань " + (owner.S.StripParams ? "увімкнено" : "вимкнено") +
                " · cookie-банери " + (owner.S.CookiePopups ? "відхиляються" : "без змін"), 18, y, 324, 62, k, true); y += 68;
            f.ClientSize = new Size(w, (int)(y * k));
            f.Location = new Point(anchorRightTop.X - w, anchorRightTop.Y);
            f.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, f.ClientSize.Width - 1, f.ClientSize.Height - 1); };
            f.Deactivate += delegate { f.Close(); };
            f.FormClosed += delegate { big.Dispose(); };
            f.HandleCreated += delegate { Native.RoundCorners(f.Handle); };
            f.Show(owner);
        }

        // ---------------------------------------------------------------- settings

        public static void SettingsDialog(BrowserForm owner)
        {
            Settings S = owner.S;
            float k = owner.UiScale;
            using (Form f = Base(owner, "Налаштування Beryll", 900, 560))
            {
                var h = new Font("Segoe UI Semibold", 14f * k, FontStyle.Regular, GraphicsUnit.Pixel);
                // ------------------------------------------------ left column
                int x = 20, w = 410, y = 14;
                Lbl(f, "Загальні", x, y, w, 26, k, false, h); y += 32;
                Lbl(f, "Пошук", x, y + 4, 120, 24, k, true); var cSearch = Combo(f, x + 130, y, w - 130, k, Settings.SearchNames, S.Search); y += 36;
                Lbl(f, "Тема", x, y + 4, 120, 24, k, true); var cTheme = Combo(f, x + 130, y, w - 130, k, new[] { "Як у Windows", "Темна", "Світла" }, S.ThemeMode); y += 36;
                var sRestore = Sw(f, "Відновлювати вкладки після запуску", x, y, w, k, S.RestoreSession); y += 32;
                var sBar = Sw(f, "Панель закладок", x, y, w, k, S.BookmarksBar); y += 40;

                Lbl(f, "Захист (як у DuckDuckGo)", x, y, w, 26, k, false, h); y += 32;
                var sProt = Sw(f, "Блокувати трекери (вбудований список)", x, y, w, k, S.Protection); y += 32;
                var sHttps = Sw(f, "Спершу HTTPS (безпечне з'єднання)", x, y, w, k, S.HttpsUpgrade); y += 32;
                var sStrip = Sw(f, "Прибирати трекінг із посилань (utm, fbclid…)", x, y, w, k, S.StripParams); y += 32;
                var sGpc = Sw(f, "Global Privacy Control", x, y, w, k, S.Gpc); y += 32;
                var sCook = Sw(f, "Автоматично відхиляти cookie-банери", x, y, w, k, S.CookiePopups); y += 40;

                Lbl(f, "Блокування реклами (open source)", x, y, w, 26, k, false, h); y += 32;
                var sMax = Sw(f, "Максимальне: uBlock Origin Lite (+9 МБ)", x, y, w, k, S.AdBlockMax); y += 30;
                Lbl(f, "Списки EasyList, EasyPrivacy, Peter Lowe, регіональні; працює всередині рушія.", x, y, w, 20, k, true); y += 28;
                var bDns = Btn(f, "DNS-фільтр для всього ПК…", x, y, 200, k); y += 36;
                bDns.Click += delegate
                {
                    MessageBox.Show(f, "Фільтр реклами на рівні DNS діє для всіх програм одразу і не забирає пам'яті.\n\n" +
                        "Windows 11: Параметри → Мережа та Інтернет → ваше підключення → Призначення DNS-сервера → Змінити → Вручну, IPv4:\n" +
                        "   94.140.14.14 і 94.140.15.15 (AdGuard DNS, без реклами)\n   Шифрування DNS: «Лише зашифровано (DNS через HTTPS)».\n\n" +
                        "Windows 10: те саме у властивостях мережевого адаптера (IPv4 → DNS-сервери).\n\n" +
                        "Потрібні права адміністратора. Зараз відкриються параметри мережі.", "DNS-фільтр", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    try { System.Diagnostics.Process.Start("ms-settings:network-status"); } catch { }
                };

                // ------------------------------------------------ right column
                x = 470; w = 410; y = 14;
                Lbl(f, "Швидкість і пам'ять", x, y, w, 26, k, false, h); y += 32;
                Lbl(f, "Профіль ПК: " + owner.Hw.TierName + "\n" + owner.Hw.Summary, x, y, w, 40, k, true); y += 48;
                string[] liveNames = { "Авто", "2", "4", "8", "16", "32" };
                int[] liveVals = { 0, 2, 4, 8, 16, 32 };
                Lbl(f, "Живих вкладок", x, y + 4, 150, 24, k, true); var cLive = Combo(f, x + 160, y, w - 160, k, liveNames, Array.IndexOf(liveVals, S.LiveTabs)); y += 36;
                string[] sleepNames = { "Авто", "1 хв", "2 хв", "5 хв", "15 хв", "30 хв", "60 хв" };
                int[] sleepVals = { 0, 1, 2, 5, 15, 30, 60 };
                Lbl(f, "Присипляти через", x, y + 4, 150, 24, k, true); var cSleep = Combo(f, x + 160, y, w - 160, k, sleepNames, Array.IndexOf(sleepVals, S.SleepMinutes)); y += 36;
                Lbl(f, "Легкий режим", x, y + 4, 150, 24, k, true); var cLite = Combo(f, x + 160, y, w - 160, k, new[] { "Авто", "Увімкнено", "Вимкнено" }, S.LiteMode); y += 36;
                Lbl(f, "Відео H.264", x, y + 4, 150, 24, k, true); var cH = Combo(f, x + 160, y, w - 160, k, new[] { "Авто", "Так", "Ні" }, S.ForceH264); y += 36;
                Lbl(f, "Музика й відео у фонових вкладках грають далі — такі вкладки не присипляються.", x, y, w, 40, k, true); y += 48;

                Lbl(f, "Windows і оновлення", x, y, w, 26, k, false, h); y += 32;
                var sUpd = Sw(f, "Автоматично перевіряти оновлення (раз на добу)", x, y, w, k, S.AutoUpdate); y += 38;
                var bInstall = Btn(f, Installer.RunningInstalled ? "Встановлено на цей ПК ✓" : "Встановити на ПК (Пуск, ярлик)", x, y, 200, k);
                bInstall.Enabled = !Installer.RunningInstalled;
                var bDefault = Btn(f, "Браузер за замовчуванням", x + 210, y, 200, k); y += 42;
                var bImport = Btn(f, "Імпорт закладок з Chrome / Edge", x, y, 200, k);
                var bHist = Btn(f, "Очистити історію", x + 210, y, 200, k);

                var ok = Btn(f, "Зберегти", 900 - 20 - 120, 560 - 52, 120, k, true);
                ok.DialogResult = DialogResult.OK;
                f.AcceptButton = ok;

                bImport.Click += delegate
                {
                    string from;
                    int n = Store.ImportChromiumBookmarks(out from);
                    MessageBox.Show(f, n > 0 ? "Імпортовано закладок: " + n + " (" + from + ")" : "Нових закладок не знайдено.", "Імпорт");
                };
                bDefault.Click += delegate { Shell.MakeDefault(f); };
                bHist.Click += delegate { Store.ClearHistory(); MessageBox.Show(f, "Історію очищено.", "Beryll"); };
                bInstall.Click += delegate { f.Close(); InstallDialog(owner); };

                if (f.ShowDialog(owner) != DialogResult.OK) return;
                bool restart = cLite.SelectedIndex != S.LiteMode;
                bool maxChanged = sMax.On != S.AdBlockMax;
                S.Search = cSearch.SelectedIndex; S.ThemeMode = cTheme.SelectedIndex; S.RestoreSession = sRestore.On; S.BookmarksBar = sBar.On;
                S.Protection = sProt.On; S.HttpsUpgrade = sHttps.On; S.StripParams = sStrip.On; S.Gpc = sGpc.On; S.CookiePopups = sCook.On;
                S.LiveTabs = liveVals[Math.Max(0, cLive.SelectedIndex)]; S.SleepMinutes = sleepVals[Math.Max(0, cSleep.SelectedIndex)];
                S.LiteMode = cLite.SelectedIndex; S.ForceH264 = cH.SelectedIndex; S.AutoUpdate = sUpd.On;
                S.Save();
                owner.Hw = HwProfile.Detect(S);
                owner.ApplyTheme();
                if (maxChanged) owner.SetAdBlockMax(sMax.On);
                owner.ShowToast(restart ? "Збережено. DNS / легкий режим застосуються після перезапуску Beryll" : "Налаштування збережено", 3000);
            }
        }

        // ---------------------------------------------------------------- install into Windows

        public static void InstallDialog(BrowserForm owner)
        {
            float k = owner.UiScale;
            using (Form f = Base(owner, "Встановити Beryll", 460, 300))
            {
                var h = new Font("Segoe UI Semibold", 16f * k, FontStyle.Regular, GraphicsUnit.Pixel);
                Lbl(f, "Встановити Beryll на цей ПК", 22, 16, 420, 30, k, false, h);
                Lbl(f, "Без прав адміністратора, як Chrome чи Edge: у %LOCALAPPDATA%\\Programs\\Beryll, із записом у «Програми й компоненти».", 22, 52, 420, 44, k, true);
                var sStart = Sw(f, "Меню «Пуск»", 22, 104, 416, k, true);
                sStart.Enabled = false;
                var sDesk = Sw(f, "Ярлик на робочому столі", 22, 138, 416, k, true);
                var sDef = Sw(f, "Зробити браузером за замовчуванням", 22, 172, 416, k, false);
                Lbl(f, "Закріпити на панелі завдань: після запуску клацніть правою кнопкою на значку Beryll → «Закріпити» (Windows не дозволяє програмам робити це самим).", 22, 206, 420, 44, k, true);
                var ok = Btn(f, "Встановити", 460 - 22 - 250, 300 - 50, 130, k, true);
                var no = Btn(f, "Скасувати", 460 - 22 - 110, 300 - 50, 110, k);
                ok.DialogResult = DialogResult.OK; no.DialogResult = DialogResult.Cancel;
                f.AcceptButton = ok; f.CancelButton = no;
                if (f.ShowDialog(owner) != DialogResult.OK) return;
                DoInstall(owner, sDesk.On, sDef.On);
            }
        }

        public static void DoInstall(BrowserForm owner, bool desktop, bool makeDefault)
        {
            try
            {
                owner.Cursor = Cursors.WaitCursor;
                string exe = Installer.Install(desktop);
                owner.Cursor = Cursors.Default;
                if (makeDefault) Shell.MakeDefault(owner);
                if (!Installer.RunningInstalled &&
                    Confirm(owner, "Beryll встановлено", "Beryll з'явився в меню «Пуск»" + (desktop ? " і на робочому столі" : "") + ".\n\nПерезапустити встановлену копію зараз?", "Перезапустити"))
                {
                    owner.S.Save();
                    owner.Close();
                    System.Diagnostics.Process.Start(exe);
                }
            }
            catch (Exception ex)
            {
                owner.Cursor = Cursors.Default;
                MessageBox.Show(owner, "Не вдалося встановити: " + ex.Message, "Beryll", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---------------------------------------------------------------- site permissions (camera, microphone, …)

        public static bool Permission(BrowserForm owner, string host, string what, string glyph, out bool remember)
        {
            float k = owner.UiScale;
            using (Form f = Base(owner, "Дозвіл для сайту", 440, 200))
            {
                var icon = new Label { Text = glyph, Font = new Font("Segoe MDL2 Assets", 26f * k, FontStyle.Regular, GraphicsUnit.Pixel), ForeColor = Theme.Beryl,
                    Location = new Point((int)(20 * k), (int)(20 * k)), Size = new Size((int)(46 * k), (int)(46 * k)), TextAlign = ContentAlignment.MiddleCenter };
                f.Controls.Add(icon);
                Lbl(f, host, 78, 18, 340, 26, k, false, new Font("Segoe UI Semibold", 15f * k, FontStyle.Regular, GraphicsUnit.Pixel));
                Lbl(f, "просить доступ до " + what, 78, 46, 340, 24, k);
                var sRem = Sw(f, "Запам'ятати для цього сайту", 20, 96, 400, k, true);
                var allow = Btn(f, "Дозволити", 440 - 20 - 250, 200 - 52, 130, k, true);
                var deny = Btn(f, "Заблокувати", 440 - 20 - 110, 200 - 52, 110, k);
                allow.DialogResult = DialogResult.OK; deny.DialogResult = DialogResult.Cancel;
                f.CancelButton = deny;
                bool ok = f.ShowDialog(owner) == DialogResult.OK;
                remember = sRem.On;
                return ok;
            }
        }

        // ---------------------------------------------------------------- history

        public static void History(BrowserForm owner)
        {
            float k = owner.UiScale;
            Form f = Base(owner, "Історія", 760, 560);
            f.FormBorderStyle = FormBorderStyle.Sizable;
            f.MinimizeBox = true;
            var search = new TextBox { Location = new Point((int)(14 * k), (int)(12 * k)), Width = (int)(560 * k), BackColor = Theme.Toolbar, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            f.Controls.Add(search);
            var clear = Btn(f, "Очистити", 590, 8, 156, k);
            clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var lv = new ListView
            {
                View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.None,
                Location = new Point((int)(14 * k), (int)(52 * k)), Size = new Size((int)(732 * k), (int)(494 * k)),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, BackColor = Theme.MenuBg, ForeColor = Theme.Text
            };
            lv.Columns.Add("Час", (int)(130 * k)); lv.Columns.Add("Назва", (int)(330 * k)); lv.Columns.Add("Адреса", (int)(250 * k));
            f.Controls.Add(lv);
            Action fill = () =>
            {
                string q = search.Text.Trim();
                lv.BeginUpdate(); lv.Items.Clear();
                int n = 0;
                for (int i = Store.History.Count - 1; i >= 0 && n < 1500; i--)
                {
                    HistoryItem h = Store.History[i];
                    if (q.Length > 0 && h.Url.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && h.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var it = new ListViewItem(new[] { h.Time.ToString("dd.MM HH:mm"), h.Title, h.Url }) { Tag = h.Url };
                    lv.Items.Add(it); n++;
                }
                lv.EndUpdate();
            };
            search.TextChanged += delegate { fill(); };
            lv.ItemActivate += delegate { if (lv.SelectedItems.Count > 0) owner.NewTab((string)lv.SelectedItems[0].Tag, true); };
            clear.Click += delegate { if (Confirm(owner, "Історія", "Очистити всю історію?", "Очистити")) { Store.ClearHistory(); fill(); } };
            fill();
            f.Show(owner);
        }

        // ---------------------------------------------------------------- diagnostics

        public static void Diagnostics(BrowserForm owner, IList<Tab> tabs)
        {
            HwProfile p = owner.Hw;
            int live = tabs.Count(t => t.View != null), sleeping = tabs.Count(t => t.Asleep);
            string advice = p.Tier == 1 ? "Порада: ще 2–4 ГБ RAM дозволять тримати живими 4–8 вкладок замість 2." :
                            p.Hdd ? "Порада: SSD замість HDD прискорить запуск браузера й Windows у кілька разів." : "Залізо в порядку.";
            string text =
                "Профіль: " + p.TierName + "\n" + p.Summary + "\n\n" +
                "Стратегія: живих вкладок до " + p.LiveTabs + ", присипляння через " + p.SleepMinutes + " хв, резерв RAM " + p.ReserveMb + " МБ\n" +
                "Легкий режим: " + (p.Lite ? "так" : "ні") + " · H.264 замість VP9/AV1: " + (p.PreferH264 ? "так (немає апаратного VP9)" : "ні") + "\n\n" +
                "Зараз: вкладок " + tabs.Count + ", живих " + live + ", сплять " + sleeping + "\n" +
                "Вільна RAM: " + Native.FreeMb() + " МБ з " + Native.TotalMb() + " МБ\n" +
                "Звільнено сплячими вкладками за весь час: ≈" + owner.S.TotalFreedMb + " МБ\n" +
                "Заблоковано трекерів за весь час: " + owner.S.TotalBlocked + "\n\n" + advice + "\n\nРушій: Microsoft Edge WebView2 " + owner.EngineVersion;
            MessageBox.Show(owner, text, "Діагностика Beryll", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void About(BrowserForm owner, CoreWebView2Environment env)
        {
            MessageBox.Show(owner,
                "Beryll Browser " + Program.Version + "\n\nШвидкий приватний браузер для старих і слабких ПК.\n" +
                "Рушій: Microsoft Edge WebView2 " + owner.EngineVersion + "\n\n" +
                "Захист у стилі DuckDuckGo: блокування трекерів, HTTPS, очищення посилань, Global Privacy Control, відхилення cookie-банерів, кнопка «Вогонь».\n\n" +
                "Open source · Ihor Stefaniv with AI Claude", "Про Beryll", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---------------------------------------------------------------- first run

        public static void Onboarding(BrowserForm owner)
        {
            Settings S = owner.S;
            float k = owner.UiScale;
            using (Form f = Base(owner, "Ласкаво просимо до Beryll", 520, 560))
            {
                var h = new Font("Segoe UI Semibold", 20f * k, FontStyle.Regular, GraphicsUnit.Pixel);
                Lbl(f, "Вітаємо в Beryll", 24, 16, 470, 36, k, false, h);
                Lbl(f, "Легкий приватний браузер. Ваш ПК: " + owner.Hw.TierName.ToLowerInvariant() + " — Beryll уже налаштувався під нього.", 24, 56, 470, 44, k, true);
                Lbl(f, "Пошукова система", 24, 110, 200, 24, k);
                var cs = Combo(f, 240, 106, 256, k, Settings.SearchNames, S.Search);
                Lbl(f, "Тема", 24, 146, 200, 24, k);
                var ct = Combo(f, 240, 142, 256, k, new[] { "Як у Windows", "Темна", "Світла" }, S.ThemeMode);
                int y = 188;
                bool inst = Installer.RunningInstalled;
                var sInst = Sw(f, inst ? "Встановлено на цей ПК ✓" : "Встановити на ПК (меню «Пуск», без адміна)", 24, y, 472, k, !inst); sInst.Enabled = !inst; y += 34;
                var sDesk = Sw(f, "Ярлик на робочому столі", 24, y, 472, k, true); y += 34;
                var sBar = Sw(f, "Показувати панель закладок", 24, y, 472, k, S.BookmarksBar); y += 34;
                var sImp = Sw(f, "Імпортувати закладки з Chrome / Edge", 24, y, 472, k, true); y += 34;
                var sMax = Sw(f, "Максимальне блокування реклами (uBlock Origin Lite, +9 МБ)", 24, y, 472, k, false); y += 34;
                var sDef = Sw(f, "Зробити Beryll браузером за замовчуванням", 24, y, 472, k, false); y += 42;
                Lbl(f, "Захист від стеження вже увімкнено. Кнопка-вогонь — стерти все одним кліком.", 24, y, 472, 40, k, true);
                var ok = Btn(f, "Почати", 520 - 24 - 130, 560 - 54, 130, k, true);
                ok.DialogResult = DialogResult.OK; f.AcceptButton = ok;
                f.ShowDialog(owner);
                S.Search = cs.SelectedIndex; S.ThemeMode = ct.SelectedIndex; S.BookmarksBar = sBar.On; S.Onboarded = true; S.Save();
                owner.ApplyTheme();
                if (sImp.On)
                {
                    string from; int n = Store.ImportChromiumBookmarks(out from);
                    if (n > 0) owner.ShowToast("Імпортовано закладок: " + n);
                }
                if (sMax.On) owner.SetAdBlockMax(true);
                if (!inst && sInst.On) DoInstall(owner, sDesk.On, sDef.On);
                else
                {
                    if (sDesk.On) Installer.AddDesktopShortcut();
                    if (sDef.On) Shell.MakeDefault(owner);
                }
            }
        }
    }
}
