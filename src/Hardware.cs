using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Beryll
{
    /// <summary>Self-adaptive hardware profile: RAM, CPU, disk type, GPU and hardware video decoding.
    /// Detected once at start (a few ms) and turned into a strategy for the memory governor and the engine.</summary>
    sealed class HwProfile
    {
        public long TotalMb;
        public int Cores;
        public bool Hdd;
        public string Gpu = "";
        public bool IntegratedGpu = true;
        public bool H264Hw, Vp9Hw, Av1Hw;

        // strategy
        public int Tier;            // 1: <=2.5 GB, 2: <=4.5 GB, 3: <=8.5 GB, 4: more
        public int LiveTabs;        // tabs kept alive (others sleep)
        public int SleepMinutes;    // inactive tab falls asleep after
        public int ReserveMb;       // keep this much RAM free for Windows
        public bool Lite;           // lightweight page mode
        public bool PreferH264;     // ask sites for H.264 (no VP9/AV1 hardware decoding)

        public string Summary
        {
            get
            {
                return (TotalMb / 1024.0).ToString("0.#") + " ГБ RAM · " + Cores + " ядер · " + (Hdd ? "HDD" : "SSD") +
                       " · " + (Gpu.Length > 0 ? Gpu : "відео невідоме") +
                       " · апаратно: " + (H264Hw ? "H.264 " : "") + (Vp9Hw ? "VP9 " : "") + (Av1Hw ? "AV1" : "");
            }
        }

        public string TierName
        {
            get
            {
                switch (Tier)
                {
                    case 1: return "Слабкий ПК (≤ 2 ГБ)";
                    case 2: return "Економний (≤ 4 ГБ)";
                    case 3: return "Збалансований (≤ 8 ГБ)";
                    default: return "Потужний";
                }
            }
        }

        public static HwProfile Detect(Settings s)
        {
            var p = new HwProfile();
            p.TotalMb = Native.TotalMb();
            p.Cores = Environment.ProcessorCount;
            p.Hdd = DetectHdd();
            p.Gpu = DetectGpu();
            p.IntegratedGpu = p.Gpu.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0 && p.Gpu.IndexOf("Arc", StringComparison.OrdinalIgnoreCase) < 0
                              || p.Gpu.IndexOf("Vega", StringComparison.OrdinalIgnoreCase) >= 0 || p.Gpu.Length == 0;
            DetectDecoders(p);

            p.Tier = p.TotalMb <= 2600 ? 1 : p.TotalMb <= 4600 ? 2 : p.TotalMb <= 8700 ? 3 : 4;
            switch (p.Tier)
            {
                case 1: p.LiveTabs = 2; p.SleepMinutes = 2; p.ReserveMb = 300; break;
                case 2: p.LiveTabs = 4; p.SleepMinutes = 5; p.ReserveMb = 450; break;
                case 3: p.LiveTabs = p.Hdd ? 10 : 8; p.SleepMinutes = 15; p.ReserveMb = 700; break;   // HDD: keep more alive, reloading from disk is slow
                default: p.LiveTabs = 16; p.SleepMinutes = 30; p.ReserveMb = 1000; break;
            }
            if (p.Cores <= 2 && p.Tier > 1) p.SleepMinutes = Math.Max(2, p.SleepMinutes / 2);
            p.Lite = p.Tier == 1 || (p.Tier == 2 && p.Cores <= 2);
            p.PreferH264 = !p.Vp9Hw;

            // user overrides
            if (s.LiveTabs > 0) p.LiveTabs = s.LiveTabs;
            if (s.SleepMinutes > 0) p.SleepMinutes = s.SleepMinutes;
            if (s.LiteMode == 1) p.Lite = true; else if (s.LiteMode == 2) p.Lite = false;
            if (s.ForceH264 == 1) p.PreferH264 = true; else if (s.ForceH264 == 2) p.PreferH264 = false;
            return p;
        }

        // ---------------------------------------------------------------- disk: seek penalty = rotating HDD

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(IntPtr h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int ret, IntPtr ov);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        static bool DetectHdd()
        {
            try
            {
                string drive = Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\');
                IntPtr h = CreateFile(@"\\.\" + drive, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (h == new IntPtr(-1)) return false;
                try
                {
                    var q = new byte[12];              // STORAGE_PROPERTY_QUERY { PropertyId = 7 (seek penalty), QueryType = 0 }
                    BitConverter.GetBytes(7).CopyTo(q, 0);
                    var o = new byte[12];              // DEVICE_SEEK_PENALTY_DESCRIPTOR { Version, Size, IncursSeekPenalty }
                    int ret;
                    if (!DeviceIoControl(h, 0x2D1400, q, q.Length, o, o.Length, out ret, IntPtr.Zero) || ret < 9) return false;
                    return o[8] != 0;
                }
                finally { CloseHandle(h); }
            }
            catch { return false; }
        }

        // ---------------------------------------------------------------- GPU name

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevices(string dev, uint i, ref DISPLAY_DEVICE dd, uint flags);

        static string DetectGpu()
        {
            try
            {
                var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++)
                {
                    if ((dd.StateFlags & 4) != 0) return dd.DeviceString; // primary device
                    dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                }
            }
            catch { }
            return "";
        }

        // ---------------------------------------------------------------- hardware video decoders (Media Foundation)

        [StructLayout(LayoutKind.Sequential)] struct MFT_REGISTER_TYPE_INFO { public Guid Major, Sub; }
        [DllImport("mfplat.dll")] static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll")] static extern int MFShutdown();
        [DllImport("mfplat.dll")] static extern int MFTEnumEx(Guid category, int flags, ref MFT_REGISTER_TYPE_INFO input, IntPtr output, out IntPtr activates, out int count);
        [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr p);

        static readonly Guid DecoderCategory = new Guid("d6c02d4b-6833-45b4-971a-05a4b04bab91");
        static readonly Guid MediaVideo = new Guid("73646976-0000-0010-8000-00AA00389B71");

        static void DetectDecoders(HwProfile p)
        {
            try
            {
                if (MFStartup(0x00020070, 1) < 0) return;
                try
                {
                    p.H264Hw = HasHw("34363248-0000-0010-8000-00AA00389B71");
                    p.Vp9Hw = HasHw("30395056-0000-0010-8000-00AA00389B71");
                    p.Av1Hw = HasHw("31305641-0000-0010-8000-00AA00389B71");
                }
                finally { MFShutdown(); }
            }
            catch { }
        }

        static bool HasHw(string subtype)
        {
            var info = new MFT_REGISTER_TYPE_INFO { Major = MediaVideo, Sub = new Guid(subtype) };
            IntPtr arr; int n;
            if (MFTEnumEx(DecoderCategory, 0x4 | 0x40, ref info, IntPtr.Zero, out arr, out n) < 0) return false; // HARDWARE | SORTANDFILTER
            for (int i = 0; i < n; i++) Marshal.Release(Marshal.ReadIntPtr(arr, i * IntPtr.Size));
            if (arr != IntPtr.Zero) CoTaskMemFree(arr);
            return n > 0;
        }
    }
}
