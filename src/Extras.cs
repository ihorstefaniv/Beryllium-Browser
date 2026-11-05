using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Beryll
{
    static class Net
    {
        /// <summary>HTTPS-only download with a size cap.</summary>
        public static byte[] Get(string url, long max)
        {
            if (!url.StartsWith("https://", StringComparison.Ordinal)) throw new SecurityException("Лише HTTPS");
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Beryll/" + Program.Version;
            req.Timeout = 20000; req.ReadWriteTimeout = 20000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if (!resp.ResponseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) throw new SecurityException("Перенаправлення не на HTTPS");
                if (resp.ContentLength > max) throw new SecurityException("Завеликий файл");
                using (Stream s = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    var buf = new byte[65536]; int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) { if (ms.Length + n > max) throw new SecurityException("Завеликий файл"); ms.Write(buf, 0, n); }
                    return ms.ToArray();
                }
            }
        }

        public static string Sha256(byte[] data)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }
    }

    // ==================================================================== ad blocking

    /// <summary>Optional "maximum" ad blocking: uBlock Origin Lite (GPL-3, open source; EasyList, EasyPrivacy,
    /// Peter Lowe's list, regional lists) runs inside the engine via declarativeNetRequest — native speed, no
    /// per-request work in Beryll. Downloaded on demand (9 MB) so the base browser stays tiny; integrity pinned by SHA-256.</summary>
    static class AdBlock
    {
        public const string UbolVersion = "2026.1103.1743";
        const string UbolUrl = "https://github.com/uBlockOrigin/uBOL-home/releases/download/" + UbolVersion + "/uBOLite_" + UbolVersion + ".chromium.zip";
        const string UbolSha256 = "79236476535b63706749e088e0ce99bb31d3d2ff15fabb4d7e990be23d6e4613";

        public static string Dir { get { return Paths.File(@"extensions\ubol"); } }
        public static bool Installed { get { return File.Exists(Path.Combine(Dir, "manifest.json")); } }

        public static void Install()
        {
            byte[] zip = Net.Get(UbolUrl, 40L << 20);
            if (Net.Sha256(zip) != UbolSha256) throw new SecurityException("Контрольна сума uBlock Origin Lite не збігається — завантаження відхилено");
            string tmp = Dir + ".tmp";
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            using (var za = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
                foreach (ZipArchiveEntry e in za.Entries)
                {
                    if (e.FullName.EndsWith("/")) continue;
                    string target = Path.GetFullPath(Path.Combine(tmp, e.FullName));
                    if (!target.StartsWith(Path.GetFullPath(tmp) + "\\", StringComparison.OrdinalIgnoreCase)) continue; // zip-slip
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    e.ExtractToFile(target, true);
                }
            if (Directory.Exists(Dir)) Directory.Delete(Dir, true);
            Directory.Move(tmp, Dir);
        }

        public static void Remove() { try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch { } }
    }

    // ==================================================================== install into Windows (per user, no admin)

    static class Installer
    {
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Beryll"); } }
        public static string Exe { get { return Path.Combine(Dir, "Beryll.exe"); } }
        public static bool IsInstalled { get { return File.Exists(Exe); } }
        public static bool RunningInstalled { get { return string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase); } }
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Beryll";
        static string StartLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Beryll.lnk"); } }
        static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Beryll.lnk"); } }

        /// <summary>Copies the program to %LOCALAPPDATA%\Programs\Beryll (like Chrome / Edge per-user installs),
        /// creates shortcuts and an entry in "Installed apps". Returns the installed exe.</summary>
        public static string Install(bool desktop)
        {
            string src = Path.GetDirectoryName(Application.ExecutablePath);
            if (!RunningInstalled)
            {
                Directory.CreateDirectory(Dir);
                foreach (string f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(src.Length + 1);
                    if (rel.StartsWith("Data\\", StringComparison.OrdinalIgnoreCase) || rel.Equals("portable", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".old")) continue;
                    string dst = Path.Combine(Dir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.WriteAllBytes(dst, File.ReadAllBytes(f));
                }
            }
            Shortcut(StartLink);
            if (desktop) Shortcut(DesktopLink);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Beryll Browser");
                k.SetValue("DisplayVersion", Program.Version);
                k.SetValue("Publisher", "Ihor Stefaniv");
                k.SetValue("DisplayIcon", "\"" + Exe + "\",0");
                k.SetValue("InstallLocation", Dir);
                k.SetValue("UninstallString", "\"" + Exe + "\" /uninstall");
                k.SetValue("URLInfoAbout", "https://github.com/ihorstefaniv/Beryllium-Browser");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long size = 0;
                try { size = Directory.GetFiles(Dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); } catch { }
                k.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
            }
            return Exe;
        }

        public static void Shortcut(string lnk)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object sh = Activator.CreateInstance(t);
                object l = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                Type lt = l.GetType();
                string target = IsInstalled ? Exe : Application.ExecutablePath;
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, l, new object[] { target });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, l, new object[] { Path.GetDirectoryName(target) });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, l, new object[] { target + ",0" });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, l, new object[] { "Beryll — швидкий приватний браузер" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, l, null);
            }
            catch { }
        }

        public static void AddDesktopShortcut() { Shortcut(DesktopLink); }

        public static void RemoveAll()
        {
            try { File.Delete(StartLink); } catch { }
            try { File.Delete(DesktopLink); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            if (RunningInstalled && Directory.Exists(Dir))
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + Dir + "\"") { CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false };
                try { Process.Start(psi); } catch { }
            }
        }
    }

    // ==================================================================== signed updates

    /// <summary>Offline-first signed updates. Once a day (20 s after start, low priority) Beryll reads
    /// download/latest.txt from the official repository. The release zip must carry an RSA-3072/SHA-256 signature
    /// made with the developer's offline key (public key compiled in, UpdateKey.cs). Only whitelisted files are extracted,
    /// the new exe must be newer (no downgrade), all downloads are HTTPS and size-capped.</summary>
    static class Updater
    {
        const string Base = "https://raw.githubusercontent.com/ihorstefaniv/Beryllium-Browser/main/download/";
        static readonly Regex Allowed = new Regex(@"^(Beryll\.exe|Microsoft\.Web\.WebView2\.(Core|WinForms)\.dll|WebView2-LICENSE\.txt|runtimes/win-(x64|x86|arm64)/native/WebView2Loader\.dll)$", RegexOptions.IgnoreCase);

        public sealed class Release { public Version Version; public string Notes; }

        public static Version Current { get { return Norm(Assembly.GetExecutingAssembly().GetName().Version); } }
        static Version Norm(Version v) { return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision)); }

        public static void Cleanup()
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            try { foreach (string f in Directory.GetFiles(dir, "*.old", SearchOption.AllDirectories)) File.Delete(f); } catch { }
        }

        /// <summary>latest.txt: first line = version, rest = release notes.</summary>
        public static Release Check()
        {
            string txt = System.Text.Encoding.UTF8.GetString(Net.Get(Base + "latest.txt", 64 * 1024));
            string[] lines = txt.Replace("\r", "").Split('\n');
            Version v;
            if (lines.Length == 0 || !Version.TryParse(lines[0].Trim(), out v)) return null;
            return new Release { Version = Norm(v), Notes = string.Join("\n", lines.Skip(1)).Trim() };
        }

        public static bool Verify(byte[] data, byte[] sig)
        {
            if (string.IsNullOrEmpty(UpdateKey.PublicKeyXml) || sig == null || sig.Length == 0 || sig.Length > 4096) return false;
            try
            {
                using (var rsa = new RSACryptoServiceProvider(new CspParameters(24)))
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(UpdateKey.PublicKeyXml);
                    if (rsa.KeySize < 3072) return false;
                    byte[] hash; using (var sha = SHA256.Create()) hash = sha.ComputeHash(data);
                    return rsa.VerifyHash(hash, CryptoConfig.MapNameToOID("SHA256"), sig);
                }
            }
            catch { return false; }
        }

        /// <summary>Downloads, verifies and installs next to the running exe; restarts Beryll.</summary>
        public static void Apply()
        {
            byte[] zip = Net.Get(Base + "Beryll-portable.zip", 100L << 20);
            byte[] sig = Net.Get(Base + "Beryll-portable.zip.sig", 4096);
            if (!Verify(zip, sig)) throw new SecurityException("Підпис оновлення недійсний — оновлення відхилено");
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            string stage = Path.Combine(Path.GetTempPath(), "beryll-update-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(stage);
                using (var za = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
                    foreach (ZipArchiveEntry e in za.Entries)
                    {
                        string name = e.FullName.Replace('\\', '/');
                        if (name.StartsWith("Beryll/")) name = name.Substring(7);
                        if (!Allowed.IsMatch(name) || e.Length > (60L << 20)) continue;
                        string target = Path.GetFullPath(Path.Combine(stage, name.Replace('/', '\\')));
                        if (!target.StartsWith(stage + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        e.ExtractToFile(target, true);
                    }
                string newExe = Path.Combine(stage, "Beryll.exe");
                Version fv;
                if (!File.Exists(newExe) || !Version.TryParse(FileVersionInfo.GetVersionInfo(newExe).FileVersion ?? "", out fv) || Norm(fv) <= Current)
                    throw new SecurityException("Версія в архіві не новіша — оновлення відхилено");
                foreach (string f in Directory.GetFiles(stage, "*", SearchOption.AllDirectories))
                {
                    string dst = Path.Combine(dir, f.Substring(stage.Length + 1));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    if (File.Exists(dst))
                    {
                        string old = dst + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(dst, old);          // running exe / loaded DLLs can be renamed, not overwritten
                    }
                    File.Copy(f, dst);
                }
            }
            finally { try { Directory.Delete(stage, true); } catch { } }
            Process.Start(Application.ExecutablePath);
        }
    }
}
