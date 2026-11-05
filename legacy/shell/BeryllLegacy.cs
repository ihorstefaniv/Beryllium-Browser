// Beryll Legacy shell for Windows 7 / 8 / 8.1 (later Vista / XP).
// Compiled against .NET 2.0 (CLR 2) so it runs on a clean Windows 7 (.NET 3.5.1 is built in) and also on CLR 4.
// Stage 1 (mode A): prepares the profile and flags for the hardware, starts the Supermium engine and exits,
// so it costs no memory while you browse.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("Beryll Legacy")]
[assembly: AssemblyDescription("Beryll Legacy — сучасний браузер для Windows 7 / 8 на слабких ПК")]
[assembly: AssemblyCompany("Ihor Stefaniv")]
[assembly: AssemblyProduct("Beryll Browser")]
[assembly: AssemblyCopyright("© 2026 Ihor Stefaniv with AI Claude · MIT")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

namespace BeryllLegacy
{
    static class Program
    {
        public const string Version = "1.0";

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("kernel32.dll")] static extern bool IsProcessorFeaturePresent(int feature);

        static string Root { get { return Path.GetDirectoryName(Application.ExecutablePath); } }

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            try { return Run(args); }
            catch (Exception ex)
            {
                MessageBox.Show("Не вдалося запустити Beryll:\n\n" + ex.Message, "Beryll", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        static int Run(string[] args)
        {
            string engine = Path.Combine(Path.Combine(Root, "engine"), "chrome.exe");
            if (!File.Exists(engine))
            {
                MessageBox.Show("Не знайдено рушій: " + engine + "\n\nРозпакуйте архів Beryll повністю, разом з папкою engine.", "Beryll", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
            // PF_XMMI64_INSTRUCTIONS_AVAILABLE (SSE2): the engine needs it (Pentium 4 / Athlon 64 and newer)
            if (!IsProcessorFeaturePresent(10))
            {
                MessageBox.Show("Процесор цього комп'ютера не підтримує SSE2, а сучасний рушій без нього не працює.", "Beryll", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 3;
            }

            string profile = ProfileDir();
            bool firstRun = !Directory.Exists(Path.Combine(profile, "Default"));
            if (firstRun) FirstRun(profile);

            var a = new List<string>();
            a.Add("--user-data-dir=" + Quote(profile));
            a.Add("--ungoogled-supermium");          // no Google services / pings (sites like Gmail and YouTube still work)
            a.Add("--disable-machine-id");
            a.Add("--no-first-run");
            a.Add("--no-default-browser-check");
            a.Add("--lang=uk");
            a.Add("--load-extension=" + Quote(ExtList()));
            a.Add("--disable-features=DisableLoadExtensionCommandLineSwitch,Translate,MediaRouter,OptimizationHints,OptimizationGuideModelDownloading,AutofillServerCommunication,DownloadBubble");
            a.AddRange(HardwareFlags());
            foreach (string s in args) a.Add(Quote(s));   // URLs / files from "Open with" or the default-browser link

            var psi = new ProcessStartInfo(engine, string.Join(" ", a.ToArray()));
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetDirectoryName(engine);
            Process.Start(psi);
            return 0;
        }

        /// <summary>Portable: profile next to the program. If that folder is read-only (Program Files), use %LOCALAPPDATA%.</summary>
        static string ProfileDir()
        {
            string p = Path.Combine(Root, "profile");
            try
            {
                Directory.CreateDirectory(p);
                string probe = Path.Combine(p, ".write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return p;
            }
            catch
            {
                string l = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"BeryllLegacy\profile");
                Directory.CreateDirectory(l);
                return l;
            }
        }

        static string ExtList()
        {
            var list = new List<string>();
            string ext = Path.Combine(Root, "ext");
            if (Directory.Exists(ext))
                foreach (string d in Directory.GetDirectories(ext))
                    if (File.Exists(Path.Combine(d, "manifest.json"))) list.Add(d);
            return string.Join(",", list.ToArray());
        }

        static ulong RamMb()
        {
            var m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            return GlobalMemoryStatusEx(ref m) ? m.ullTotalPhys / (1024 * 1024) : 4096;
        }

        /// <summary>Self-adaptation to the machine, the same idea as in Beryll for Windows 10/11.</summary>
        static List<string> HardwareFlags()
        {
            var f = new List<string>();
            ulong ram = RamMb();
            int cores = Environment.ProcessorCount;
            if (ram <= 2600)
            {
                // netbooks / old office PCs (≤ 2–2.5 GB): Chromium's own low-memory mode, fewer renderer processes,
                // small disk cache (less HDD seeking)
                f.Add("--enable-low-end-device-mode");
                f.Add("--renderer-process-limit=3");
                f.Add("--disk-cache-size=" + (64 * 1024 * 1024));
            }
            else if (ram <= 4200)
            {
                f.Add("--renderer-process-limit=6");
                f.Add("--disk-cache-size=" + (128 * 1024 * 1024));
            }
            if (cores <= 2) f.Add("--num-raster-threads=1");
            return f;
        }

        // ------------------------------------------------------------------ first run
        static void FirstRun(string profile)
        {
            string def = Path.Combine(profile, "Default");
            Directory.CreateDirectory(def);
            // privacy defaults (Beryll / DuckDuckGo-like). Written only once: later the user's own choices win.
            const string prefs = "{"
                + "\"browser\":{\"has_seen_welcome_page\":true,\"check_default_browser\":false},"
                + "\"enable_do_not_track\":true,"
                + "\"profile\":{\"cookie_controls_mode\":1,\"block_third_party_cookies\":true},"
                + "\"intl\":{\"accept_languages\":\"uk,en-US,en\",\"selected_languages\":\"uk,en-US,en\"},"
                + "\"translate\":{\"enabled\":false},"
                + "\"safebrowsing\":{\"enabled\":false},"
                + "\"search\":{\"suggest_enabled\":true},"
                + "\"alternate_error_pages\":{\"enabled\":false},"
                + "\"net\":{\"network_prediction_options\":2},"
                + "\"download\":{\"prompt_for_download\":false},"
                + "\"bookmark_bar\":{\"show_on_all_tabs\":true}"
                + "}";
            File.WriteAllText(Path.Combine(def, "Preferences"), prefs, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(profile, "First Run"), "");

            if (MessageBox.Show("Створити ярлик Beryll на робочому столі та в меню «Пуск»?", "Beryll — перший запуск",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try
                {
                    Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Beryll.lnk"));
                    string menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Beryll.lnk");
                    Shortcut(menu);
                }
                catch (Exception ex) { MessageBox.Show("Ярлик не створено: " + ex.Message, "Beryll"); }
            }
        }

        static void Shortcut(string lnk)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            object s = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
            Type st = s.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, s, new object[] { Application.ExecutablePath });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, s, new object[] { Root });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, s, new object[] { Application.ExecutablePath + ",0" });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, s, new object[] { "Beryll — швидкий приватний браузер" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, s, null);
            Marshal.ReleaseComObject(s);
            Marshal.ReleaseComObject(sh);
        }

        static string Quote(string s)
        {
            if (s.Length > 0 && s.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return s;
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }
    }
}
