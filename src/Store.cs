using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Web.WebView2.WinForms;
using System.Threading;

namespace Beryll
{
    sealed class Bookmark { public string Title, Url; }
    sealed class HistoryItem { public string Title, Url; public DateTime Time; }

    /// <summary>Plain-text storage (tab-separated) — fast on HDD, easy to back up.</summary>
    static class Store
    {
        public static List<Bookmark> Bookmarks = new List<Bookmark>();
        public static List<HistoryItem> History = new List<HistoryItem>();
        const int MaxHistory = 20000;

        static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

        public static void Load()
        {
            try
            {
                string f = Paths.File("bookmarks.tsv");
                if (File.Exists(f))
                    foreach (string l in File.ReadAllLines(f, Encoding.UTF8))
                    {
                        string[] p = l.Split('\t');
                        if (p.Length >= 2 && p[1].Length > 0) Bookmarks.Add(new Bookmark { Title = p[0], Url = p[1] });
                    }
            }
            catch { }
            try
            {
                string f = Paths.File("history.tsv");
                if (File.Exists(f))
                {
                    string[] lines = File.ReadAllLines(f, Encoding.UTF8);
                    foreach (string l in lines.Skip(Math.Max(0, lines.Length - MaxHistory)))
                    {
                        string[] p = l.Split('\t');
                        long t;
                        if (p.Length >= 3 && long.TryParse(p[0], out t))
                            History.Add(new HistoryItem { Time = new DateTime(t), Url = p[1], Title = p[2] });
                    }
                }
            }
            catch { }
        }

        public static void SaveBookmarks()
        {
            try { File.WriteAllLines(Paths.File("bookmarks.tsv"), Bookmarks.Select(b => Clean(b.Title) + "\t" + Clean(b.Url)), Encoding.UTF8); } catch { }
        }

        public static void AddHistory(string url, string title)
        {
            if (string.IsNullOrEmpty(url) || url.StartsWith("about:") || url.StartsWith("data:") || url.StartsWith("edge:")) return;
            var h = new HistoryItem { Url = url, Title = title ?? "", Time = DateTime.Now };
            History.Add(h);
            try { File.AppendAllText(Paths.File("history.tsv"), h.Time.Ticks + "\t" + Clean(url) + "\t" + Clean(title) + "\r\n", Encoding.UTF8); } catch { }
        }

        public static void UpdateLastTitle(string url, string title)
        {
            for (int i = History.Count - 1; i >= 0 && i >= History.Count - 20; i--)
                if (History[i].Url == url) { History[i].Title = title; break; }
        }

        public static void ClearHistory()
        {
            History.Clear();
            try { File.Delete(Paths.File("history.tsv")); } catch { }
        }

        /// <summary>Compacts the history file occasionally (on exit).</summary>
        public static void CompactHistory()
        {
            try
            {
                string f = Paths.File("history.tsv");
                if (!File.Exists(f) || new FileInfo(f).Length < 4 << 20) return;
                File.WriteAllLines(f, History.Skip(Math.Max(0, History.Count - MaxHistory)).Select(h => h.Time.Ticks + "\t" + Clean(h.Url) + "\t" + Clean(h.Title)), Encoding.UTF8);
            }
            catch { }
        }

        public static IEnumerable<Bookmark> Suggest(string q, int max)
        {
            q = (q ?? "").Trim();
            if (q.Length == 0) return new Bookmark[0];
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var res = new List<Bookmark>();
            foreach (Bookmark b in Bookmarks)
                if ((b.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || b.Url.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) && seen.Add(b.Url))
                { res.Add(new Bookmark { Title = "★ " + b.Title, Url = b.Url }); if (res.Count >= max) return res; }
            // most visited first among matches
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = History.Count - 1; i >= 0 && i >= History.Count - 5000; i--)
            {
                HistoryItem h = History[i];
                if (h.Url.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && h.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                int c; counts.TryGetValue(h.Url, out c); counts[h.Url] = c + 1;
                if (!titles.ContainsKey(h.Url)) titles[h.Url] = h.Title;
            }
            foreach (var kv in counts.OrderByDescending(x => x.Value))
                if (seen.Add(kv.Key)) { res.Add(new Bookmark { Title = titles[kv.Key], Url = kv.Key }); if (res.Count >= max) break; }
            return res;
        }

        // ---------------------------------------------------------------- session

        public static void SaveSession(IList<Tab> tabs, int active)
        {
            try
            {
                var lines = new List<string> { active.ToString(CultureInfo.InvariantCulture) };
                foreach (Tab t in tabs) if (!t.IsNewTab) lines.Add((t.Pinned ? "1" : "0") + "\t" + Clean(t.Url) + "\t" + Clean(t.Title));
                File.WriteAllLines(Paths.File("session.tsv"), lines, Encoding.UTF8);
            }
            catch { }
        }

        public static List<string[]> LoadSession(out int active)
        {
            active = 0;
            var r = new List<string[]>();
            try
            {
                string f = Paths.File("session.tsv");
                if (!File.Exists(f)) return r;
                string[] lines = File.ReadAllLines(f, Encoding.UTF8);
                if (lines.Length > 0) int.TryParse(lines[0], out active);
                foreach (string l in lines.Skip(1)) { string[] p = l.Split('\t'); if (p.Length >= 3 && p[1].Length > 0) r.Add(p); }
            }
            catch { }
            return r;
        }

        // ---------------------------------------------------------------- import bookmarks from Chromium-based browsers

        public static int ImportChromiumBookmarks(out string from)
        {
            from = null;
            string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[][] sources = {
                new[] { "Google Chrome", Path.Combine(la, @"Google\Chrome\User Data\Default\Bookmarks") },
                new[] { "Microsoft Edge", Path.Combine(la, @"Microsoft\Edge\User Data\Default\Bookmarks") },
                new[] { "Brave", Path.Combine(la, @"BraveSoftware\Brave-Browser\User Data\Default\Bookmarks") },
                new[] { "Opera", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Opera Software\Opera Stable\Bookmarks") } };
            int added = 0;
            var have = new HashSet<string>(Bookmarks.Select(b => b.Url), StringComparer.OrdinalIgnoreCase);
            var js = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 64 << 20 };
            foreach (string[] s in sources)
            {
                if (!File.Exists(s[1])) continue;
                try
                {
                    var root = js.DeserializeObject(File.ReadAllText(s[1], Encoding.UTF8)) as Dictionary<string, object>;
                    var roots = root != null && root.ContainsKey("roots") ? root["roots"] as Dictionary<string, object> : null;
                    if (roots == null) continue;
                    foreach (var r in roots.Values) added += Walk(r as Dictionary<string, object>, have);
                    from = from == null ? s[0] : from + ", " + s[0];
                }
                catch { }
            }
            if (added > 0) SaveBookmarks();
            return added;
        }

        static int Walk(Dictionary<string, object> node, HashSet<string> have)
        {
            if (node == null) return 0;
            int n = 0;
            object type, url, name, children;
            node.TryGetValue("type", out type);
            if ("url".Equals(type) && node.TryGetValue("url", out url) && url is string && ((string)url).StartsWith("http"))
            {
                node.TryGetValue("name", out name);
                if (have.Add((string)url)) { Bookmarks.Add(new Bookmark { Title = name as string ?? (string)url, Url = (string)url }); n++; }
            }
            if (node.TryGetValue("children", out children) && children is IEnumerable)
                foreach (object c in (IEnumerable)children) n += Walk(c as Dictionary<string, object>, have);
            return n;
        }
    }

    /// <summary>One browser tab. The WebView2 control exists only while the tab is "alive";
    /// a sleeping tab keeps just its URL, title and icon (memory fully returned to Windows).</summary>
    sealed class Tab
    {
        public WebView2 View;
        public string Url = "", Title = "Нова вкладка";
        public Image Icon;
        public bool Pinned, Loading, KeepAwake, Frozen;
        public DateTime LastActive = DateTime.Now;
        public int Blocked;
        public readonly Dictionary<string, int> BlockedDomains = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public string NavHost = "";          // host of the current / pending top-level navigation
        public bool HttpsUpgraded;           // current navigation was upgraded to https
        public string UpgradedFrom;          // original http url (for fallback)
        public bool Audible, Muted;
        public long LastMemMb;

        public bool IsNewTab { get { return Url == "" || Url == "beryll://newtab"; } }
        public bool Asleep { get { return View == null && !IsNewTab; } }

        public string Host
        {
            get { Uri u; return Uri.TryCreate(Url, UriKind.Absolute, out u) ? u.Host : ""; }
        }

        public void ResetStats() { Blocked = 0; BlockedDomains.Clear(); }
    }
}

namespace Beryll
{
    /// <summary>Speed-dial tiles on the new tab page: user tiles (CRUD) + frequently visited sites (can be hidden).</summary>
    static class Tiles
    {
        public static List<Bookmark> User = new List<Bookmark>();
        public static HashSet<string> Hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public const int Max = 10;

        public static void Load()
        {
            try
            {
                string f = Paths.File("tiles.tsv");
                if (File.Exists(f))
                    foreach (string l in File.ReadAllLines(f, Encoding.UTF8))
                    {
                        string[] p = l.Split('\t');
                        if (p.Length >= 2 && p[1].Length > 0) User.Add(new Bookmark { Title = p[0], Url = p[1] });
                    }
                string h = Paths.File("tiles-hidden.txt");
                if (File.Exists(h)) foreach (string l in File.ReadAllLines(h)) if (l.Trim().Length > 0) Hidden.Add(l.Trim());
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllLines(Paths.File("tiles.tsv"), User.Select(b => (b.Title ?? "").Replace('\t', ' ') + "\t" + b.Url), Encoding.UTF8);
                File.WriteAllLines(Paths.File("tiles-hidden.txt"), Hidden);
            }
            catch { }
        }

        public static string Root(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) && (u.Scheme == "https" || u.Scheme == "http") ? u.Scheme + "://" + u.Host + "/" : null;
        }

        public static string Host(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Host.ToLowerInvariant() : "";
        }

        /// <summary>User tiles first, then the most visited sites (not hidden), up to Max.</summary>
        public static List<Tuple<Bookmark, bool>> Items()
        {
            var r = new List<Tuple<Bookmark, bool>>();
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Bookmark b in User) { r.Add(Tuple.Create(b, true)); have.Add(Host(b.Url)); }
            if (r.Count < Max)
            {
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = Store.History.Count - 1; i >= 0 && i >= Store.History.Count - 4000; i--)
                {
                    string root = Root(Store.History[i].Url);
                    if (root == null) continue;
                    int c; counts.TryGetValue(root, out c); counts[root] = c + 1;
                    if (!titles.ContainsKey(root)) titles[root] = Store.History[i].Title;
                }
                foreach (var kv in counts.OrderByDescending(x => x.Value))
                {
                    string host = Host(kv.Key);
                    if (Hidden.Contains(host) || !have.Add(host)) continue;
                    r.Add(Tuple.Create(new Bookmark { Url = kv.Key, Title = Pretty(host, titles[kv.Key]) }, false));
                    if (r.Count >= Max) break;
                }
            }
            return r;
        }

        static string Pretty(string host, string title)
        {
            string h = host.StartsWith("www.") ? host.Substring(4) : host;
            if (!string.IsNullOrEmpty(title) && title.Length <= 24 && title.IndexOf(" - ") < 0 && title.IndexOf(" | ") < 0) return title;
            int dot = h.IndexOf('.');
            string name = dot > 0 ? h.Substring(0, dot) : h;
            return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name.Substring(1) : h;
        }
    }

    /// <summary>Site icons: saved when a page shows its favicon; for new tiles fetched directly from the site
    /// (https://site/favicon.ico — no third-party icon services, nothing leaks elsewhere).</summary>
    static class IconCache
    {
        static readonly Dictionary<string, Image> mem = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> fetching = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static string Dir { get { string d = Paths.File("icons"); Directory.CreateDirectory(d); return d; } }
        static string FileOf(string host) { return Path.Combine(Dir, Uri.EscapeDataString(host.ToLowerInvariant()) + ".png"); }

        public static Image Get(string host)
        {
            if (string.IsNullOrEmpty(host)) return null;
            Image img;
            if (mem.TryGetValue(host, out img)) return img;
            string f = FileOf(host);
            if (File.Exists(f))
                try { using (var ms = new MemoryStream(File.ReadAllBytes(f))) img = new Bitmap(Image.FromStream(ms)); } catch { img = null; }
            mem[host] = img;
            return img;
        }

        public static void Put(string host, Image src)
        {
            if (string.IsNullOrEmpty(host) || src == null) return;
            try
            {
                var b = new Bitmap(src);
                mem[host] = b;
                b.Save(FileOf(host), System.Drawing.Imaging.ImageFormat.Png);
            }
            catch { }
        }

        /// <summary>Background fetch of https://host/favicon.ico (small, size-capped). Calls back on success.</summary>
        public static void Fetch(string host, Action done)
        {
            if (string.IsNullOrEmpty(host) || Get(host) != null || !fetching.Add(host)) return;
            var t = new System.Threading.Thread(() =>
            {
                System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072;
                foreach (string path in new[] { "/favicon.ico", "/apple-touch-icon.png", "/favicon.png" })
                try
                {
                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("https://" + host + path);
                    req.Timeout = 6000; req.ReadWriteTimeout = 6000; req.UserAgent = "Mozilla/5.0 Beryll";
                    using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                    using (Stream s = resp.GetResponseStream())
                    using (var ms = new MemoryStream())
                    {
                        var buf = new byte[16384]; int n; long total = 0;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0) { total += n; if (total > 256 * 1024) return; ms.Write(buf, 0, n); }
                        ms.Position = 0;
                        Image img;
                        try { img = new Icon(ms, 32, 32).ToBitmap(); } catch { ms.Position = 0; img = Image.FromStream(ms); }
                        Put(host, img);
                        if (done != null) done();
                        return;
                    }
                }
                catch { }
            });
            t.IsBackground = true;
            t.Priority = System.Threading.ThreadPriority.BelowNormal;
            t.Start();
        }
    }
}
