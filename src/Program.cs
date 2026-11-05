using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Beryll Browser")]
[assembly: AssemblyDescription("Beryll — швидкий приватний браузер для старих ПК")]
[assembly: AssemblyCompany("Ihor Stefaniv")]
[assembly: AssemblyProduct("Beryll Browser")]
[assembly: AssemblyCopyright("© 2026 Ihor Stefaniv with AI Claude · MIT")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]
[assembly: AssemblyInformationalVersion("1.2.0")]
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Beryll
{
    static class Program
    {
        public const string Version = "1.2";

        [DllImport("kernel32.dll")] static extern bool SetDefaultDllDirectories(uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr AddDllDirectory(string dir);

        public static string NativeDir
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "runtimes", Environment.Is64BitProcess ? "win-x64" : "win-x86", "native"); }
        }

        [STAThread]
        static void Main(string[] args)
        {
            // DLL-planting protection: DLLs only from System32 and our own runtimes folder
            try { SetDefaultDllDirectories(0x800 | 0x400); AddDllDirectory(NativeDir); } catch { }
            try { Microsoft.Web.WebView2.Core.CoreWebView2Environment.SetLoaderDllFolderPath(NativeDir); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ToolStripManager.Renderer = new MenuRenderer();

            if (args.Length > 0 && args[0].Equals("/uninstall", StringComparison.OrdinalIgnoreCase)) { Shell.Uninstall(); return; }
            string url = args.FirstOrDefault(a => !a.StartsWith("--") && !a.StartsWith("/"));

            bool first;
            var mutex = new Mutex(true, @"Local\BeryllBrowser-" + Environment.UserName, out first);
            if (!first && SendToRunning(url ?? "")) return;

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => MessageBox.Show(e.Exception.Message, "Beryll", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Settings s2 = Settings.Load();
            Store.Load();
            Tiles.Load();
            Application.Run(new BrowserForm(s2, url));
            GC.KeepAlive(mutex);
        }

        /// <summary>Second launch (e.g. a link clicked in another program): open it as a tab in the running window.</summary>
        static bool SendToRunning(string url)
        {
            try
            {
                long h;
                if (!long.TryParse(File.ReadAllText(Paths.File("instance")).Trim(), out h)) return false;
                IntPtr data = Marshal.StringToHGlobalUni(url + "\0");
                try
                {
                    var cds = new Native.COPYDATASTRUCT { dwData = IntPtr.Zero, cbData = (url.Length + 1) * 2, lpData = data };
                    IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.COPYDATASTRUCT)));
                    try
                    {
                        Marshal.StructureToPtr(cds, p, false);
                        return Native.SendMessage(new IntPtr(h), 0x004A, IntPtr.Zero, p) != IntPtr.Zero;
                    }
                    finally { Marshal.FreeHGlobal(p); }
                }
                finally { Marshal.FreeHGlobal(data); }
            }
            catch { return false; }
        }
    }

    /// <summary>Default-browser registration and per-user install (no admin rights).</summary>
    static class Shell
    {
        const string Client = @"Software\Clients\StartMenuInternet\Beryll";

        static string Exe { get { return Application.ExecutablePath; } }

        public static void Register()
        {
            string cmd = "\"" + Exe + "\" \"%1\"", icon = "\"" + Exe + "\",0";
            foreach (string prog in new[] { "BeryllURL", "BeryllHTML" })
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + prog))
                {
                    k.SetValue("", prog == "BeryllURL" ? "Beryll URL" : "Beryll HTML Document");
                    if (prog == "BeryllURL") k.SetValue("URL Protocol", "");
                    using (RegistryKey i = k.CreateSubKey("DefaultIcon")) i.SetValue("", icon);
                    using (RegistryKey c = k.CreateSubKey(@"shell\open\command")) c.SetValue("", cmd);
                }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Client))
            {
                k.SetValue("", "Beryll");
                using (RegistryKey i = k.CreateSubKey("DefaultIcon")) i.SetValue("", icon);
                using (RegistryKey c = k.CreateSubKey(@"shell\open\command")) c.SetValue("", "\"" + Exe + "\"");
                using (RegistryKey cap = k.CreateSubKey("Capabilities"))
                {
                    cap.SetValue("ApplicationName", "Beryll");
                    cap.SetValue("ApplicationDescription", "Швидкий приватний браузер для старих ПК");
                    cap.SetValue("ApplicationIcon", icon);
                    using (RegistryKey u = cap.CreateSubKey("URLAssociations")) { u.SetValue("http", "BeryllURL"); u.SetValue("https", "BeryllURL"); }
                    using (RegistryKey fa = cap.CreateSubKey("FileAssociations"))
                        foreach (string e in new[] { ".htm", ".html", ".shtml", ".xhtml", ".svg", ".pdf" }) fa.SetValue(e, "BeryllHTML");
                    using (RegistryKey sm = cap.CreateSubKey("StartMenu")) sm.SetValue("StartMenuInternet", "Beryll");
                }
            }
            using (RegistryKey ra = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                ra.SetValue("Beryll", Client + @"\Capabilities");
            try { Native.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { }
        }

        public static void MakeDefault(IWin32Window owner)
        {
            try { Register(); }
            catch (Exception ex) { MessageBox.Show(owner, "Не вдалося зареєструвати: " + ex.Message, "Beryll"); return; }
            bool win11 = Environment.OSVersion.Version.Build >= 22000;
            MessageBox.Show(owner, win11
                ? "Зараз відкриються «Параметри» на сторінці Beryll. Натисніть угорі «Установити за замовчуванням»."
                : "Зараз відкриються «Параметри → Програми за замовчуванням». У розділі «Веб-браузер» виберіть Beryll.",
                "Браузер за замовчуванням", MessageBoxButtons.OK, MessageBoxIcon.Information);
            try { Process.Start(win11 ? "ms-settings:defaultapps?registeredAppUser=Beryll" : "ms-settings:defaultapps"); } catch { }
        }

        public static void Uninstall()
        {
            if (MessageBox.Show("Видалити Beryll з цього комп'ютера?\n\nЗакладки й історія залишаться в %LOCALAPPDATA%\\Beryll.", "Beryll",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Installer.RemoveAll();
            foreach (string k in new[] { @"Software\Classes\BeryllURL", @"Software\Classes\BeryllHTML", Client })
                try { Registry.CurrentUser.DeleteSubKeyTree(k, false); } catch { }
            try { using (RegistryKey ra = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true)) if (ra != null) ra.DeleteValue("Beryll", false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Beryll", false); } catch { }
            try { Native.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { }
            MessageBox.Show("Готово.", "Beryll");
        }
    }
}
