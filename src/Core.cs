using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Beryll
{
    static class Native
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("shell32.dll")] public static extern void SHChangeNotify(int id, uint flags, IntPtr a, IntPtr b);

        [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int L, R, T, B; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct NCCALCSIZE_PARAMS { public RECT r0, r1, r2; public IntPtr pos; }
        [StructLayout(LayoutKind.Sequential)] public struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        public static void DarkTitle(IntPtr h, bool dark)
        {
            try { int v = dark ? 1 : 0; if (DwmSetWindowAttribute(h, 20, ref v, 4) != 0) DwmSetWindowAttribute(h, 19, ref v, 4); } catch { }
        }

        /// <summary>Windows 11 rounded corners (no-op on Windows 10).</summary>
        public static void RoundCorners(IntPtr h)
        {
            try { int v = 2; DwmSetWindowAttribute(h, 33, ref v, 4); } catch { }
        }

        public static long FreeMb()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            return GlobalMemoryStatusEx(ref m) ? (long)(m.ullAvailPhys >> 20) : 1024;
        }

        public static long TotalMb()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            return GlobalMemoryStatusEx(ref m) ? (long)(m.ullTotalPhys >> 20) : 4096;
        }
    }

    /// <summary>Light / dark palette (follows Windows by default).</summary>
    static class Theme
    {
        public static bool Dark;
        public static Color Strip, StripHover, Toolbar, Omni, OmniFocus, Text, TextDim, Border, Hover, Accent, Beryl, Danger, MenuBg, MenuSel, Page;

        public static bool SystemDark()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        public static void Apply(int mode)
        {
            Dark = mode == 1 || (mode == 0 && SystemDark());
            Beryl = Color.FromArgb(32, 178, 150);       // beryl / aquamarine green
            Accent = Color.FromArgb(24, 150, 126);
            Danger = Color.FromArgb(228, 86, 60);
            if (Dark)
            {
                Strip = Color.FromArgb(28, 29, 33); StripHover = Color.FromArgb(44, 46, 52);
                Toolbar = Color.FromArgb(48, 50, 56); Omni = Color.FromArgb(32, 33, 37); OmniFocus = Color.FromArgb(24, 25, 28);
                Text = Color.FromArgb(236, 238, 241); TextDim = Color.FromArgb(160, 164, 172);
                Border = Color.FromArgb(70, 72, 80); Hover = Color.FromArgb(40, 255, 255, 255);
                MenuBg = Color.FromArgb(40, 42, 47); MenuSel = Color.FromArgb(62, 65, 72); Page = Color.FromArgb(32, 33, 37);
            }
            else
            {
                Strip = Color.FromArgb(222, 226, 230); StripHover = Color.FromArgb(236, 239, 242);
                Toolbar = Color.FromArgb(255, 255, 255); Omni = Color.FromArgb(240, 242, 245); OmniFocus = Color.White;
                Text = Color.FromArgb(32, 33, 36); TextDim = Color.FromArgb(95, 99, 104);
                Border = Color.FromArgb(214, 218, 223); Hover = Color.FromArgb(22, 0, 0, 0);
                MenuBg = Color.FromArgb(252, 252, 253); MenuSel = Color.FromArgb(232, 236, 240); Page = Color.FromArgb(247, 248, 250);
            }
        }
    }

    sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.MenuBg; } }
        public override Color ImageMarginGradientBegin { get { return Theme.MenuBg; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.MenuBg; } }
        public override Color ImageMarginGradientEnd { get { return Theme.MenuBg; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.MenuSel; } }
        public override Color MenuItemSelected { get { return Theme.MenuSel; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.MenuSel; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.MenuSel; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.MenuSel; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.MenuSel; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.MenuBg; } }
        public override Color CheckBackground { get { return Theme.Accent; } }
        public override Color CheckSelectedBackground { get { return Theme.Accent; } }
        public override Color CheckPressedBackground { get { return Theme.Accent; } }
    }

    sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextDim;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Theme.TextDim; base.OnRenderArrow(e); }
    }

    /// <summary>Where Beryll keeps its data: %LOCALAPPDATA%\Beryll, or next to the exe in portable mode
    /// (a file named "portable" next to Beryll.exe).</summary>
    static class Paths
    {
        static string data;
        public static string Data
        {
            get
            {
                if (data == null)
                {
                    string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                    data = System.IO.File.Exists(Path.Combine(exeDir, "portable"))
                        ? Path.Combine(exeDir, "Data")
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Beryll");
                    Directory.CreateDirectory(data);
                }
                return data;
            }
        }
        public static string Profile { get { return Path.Combine(Data, "Profile"); } }
        public static string File(string name) { return Path.Combine(Data, name); }
    }

    sealed class Settings
    {
        public int X = int.MinValue, Y, W, H; public bool Max;
        public int ThemeMode;                 // 0 system, 1 dark, 2 light
        public int Search;                    // 0 DuckDuckGo, 1 DuckDuckGo Lite, 2 Google, 3 Bing
        public bool Protection = true, HttpsUpgrade = true, Gpc = true, CookiePopups = true, StripParams = true;
        public int LiteMode, ForceH264;       // 0 auto, 1 on, 2 off
        public int LiveTabs, SleepMinutes;    // 0 = auto by hardware
        public bool RestoreSession = true, BookmarksBar = true, Onboarded;
        public long TotalBlocked, TotalFreedMb;
        public HashSet<string> AllowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Fireproof = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static string IniPath { get { return Paths.File("settings.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!System.IO.File.Exists(IniPath)) return s;
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in System.IO.File.ReadAllLines(IniPath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
                s.X = I(d, "X", s.X); s.Y = I(d, "Y", 0); s.W = I(d, "W", 0); s.H = I(d, "H", 0); s.Max = B(d, "Max", false);
                s.ThemeMode = I(d, "Theme", 0); s.Search = I(d, "Search", 0);
                s.Protection = B(d, "Protection", true); s.HttpsUpgrade = B(d, "HttpsUpgrade", true); s.Gpc = B(d, "Gpc", true);
                s.CookiePopups = B(d, "CookiePopups", true); s.StripParams = B(d, "StripParams", true);
                s.LiteMode = I(d, "LiteMode", 0); s.ForceH264 = I(d, "ForceH264", 0);
                s.LiveTabs = I(d, "LiveTabs", 0); s.SleepMinutes = I(d, "SleepMinutes", 0);
                s.RestoreSession = B(d, "RestoreSession", true); s.BookmarksBar = B(d, "BookmarksBar", true); s.Onboarded = B(d, "Onboarded", false);
                long l;
                string v;
                if (d.TryGetValue("TotalBlocked", out v) && long.TryParse(v, out l)) s.TotalBlocked = l;
                if (d.TryGetValue("TotalFreedMb", out v) && long.TryParse(v, out l)) s.TotalFreedMb = l;
                if (d.TryGetValue("AllowList", out v)) foreach (string h in v.Split('|')) if (h.Length > 0) s.AllowList.Add(h);
                if (d.TryGetValue("Fireproof", out v)) foreach (string h in v.Split('|')) if (h.Length > 0) s.Fireproof.Add(h);
            }
            catch { }
            return s;
        }

        public void Save()
        {
            var sb = new StringBuilder("[Beryll]\r\n");
            Action<string, object> a = (k, v) => sb.Append(k).Append('=').Append(Convert.ToString(v, CultureInfo.InvariantCulture)).Append("\r\n");
            a("X", X); a("Y", Y); a("W", W); a("H", H); a("Max", Max ? 1 : 0);
            a("Theme", ThemeMode); a("Search", Search);
            a("Protection", Protection ? 1 : 0); a("HttpsUpgrade", HttpsUpgrade ? 1 : 0); a("Gpc", Gpc ? 1 : 0);
            a("CookiePopups", CookiePopups ? 1 : 0); a("StripParams", StripParams ? 1 : 0);
            a("LiteMode", LiteMode); a("ForceH264", ForceH264); a("LiveTabs", LiveTabs); a("SleepMinutes", SleepMinutes);
            a("RestoreSession", RestoreSession ? 1 : 0); a("BookmarksBar", BookmarksBar ? 1 : 0); a("Onboarded", Onboarded ? 1 : 0);
            a("TotalBlocked", TotalBlocked); a("TotalFreedMb", TotalFreedMb);
            a("AllowList", string.Join("|", AllowList)); a("Fireproof", string.Join("|", Fireproof));
            try { System.IO.File.WriteAllText(IniPath, sb.ToString(), Encoding.UTF8); } catch { }
        }

        static int I(Dictionary<string, string> d, string k, int def) { string v; int r; return d.TryGetValue(k, out v) && int.TryParse(v, out r) ? r : def; }
        static bool B(Dictionary<string, string> d, string k, bool def) { string v; return d.TryGetValue(k, out v) ? v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) : def; }

        public string SearchUrl(string q)
        {
            string e = Uri.EscapeDataString(q);
            switch (Search)
            {
                case 1: return "https://lite.duckduckgo.com/lite/?q=" + e;
                case 2: return "https://www.google.com/search?q=" + e;
                case 3: return "https://www.bing.com/search?q=" + e;
                default: return "https://duckduckgo.com/?q=" + e;
            }
        }

        public static readonly string[] SearchNames = { "DuckDuckGo", "DuckDuckGo Lite (найлегший)", "Google", "Bing" };
    }
}
