using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DevNanotek.Core
{
    /// <summary>
    /// İşletim sistemi ve işlemci mimarisi algılama.
    /// Arch: indirilecek bileşenlerin mimarisi — "x64" veya "x86".
    ///   x64 Windows → x64 · 32 bit Windows → x86 · ARM64 Windows 11 → x64 (emülasyon) · ARM64 Windows 10 → x86 (emülasyon)
    /// </summary>
    public static class SystemInfo
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);

        private static string _native;

        /// <summary>Donanımın gerçek mimarisi: x64, x86, arm64.</summary>
        public static string NativeArch
        {
            get
            {
                if (_native != null) return _native;
                try
                {
                    if (IsWow64Process2(Process.GetCurrentProcess().Handle, out _, out var native))
                    {
                        switch (native)
                        {
                            case 0xAA64: return _native = "arm64";
                            case 0x8664: return _native = "x64";
                            case 0x014C: return _native = "x86";
                        }
                    }
                }
                catch { /* Windows 10 1709 öncesi: API yok */ }
                return _native = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            }
        }

        public static int Build
        {
            get
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                        return int.TryParse(k?.GetValue("CurrentBuildNumber") as string, out var b) ? b : Environment.OSVersion.Version.Build;
                }
                catch { return Environment.OSVersion.Version.Build; }
            }
        }

        public static bool IsWindows11 => Build >= 22000;
        public static bool IsWindows10OrLater => Build >= 10240;

        /// <summary>Bileşenlerin indirileceği mimari.</summary>
        public static string Arch
        {
            get
            {
                var n = NativeArch;
                if (n == "arm64") return IsWindows11 ? "x64" : "x86";
                return n;
            }
        }

        public static bool IsX86 => Arch == "x86";

        public static string WindowsName
        {
            get
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    {
                        var name = (k?.GetValue("ProductName") as string) ?? "Windows";
                        if (IsWindows11) name = name.Replace("Windows 10", "Windows 11"); // ProductName Windows 11'de hâlâ "Windows 10" yazar
                        var disp = k?.GetValue("DisplayVersion") as string ?? k?.GetValue("ReleaseId") as string;
                        return name + (string.IsNullOrEmpty(disp) ? "" : " " + disp);
                    }
                }
                catch { return "Windows"; }
            }
        }

        public static string ArchTitle
        {
            get
            {
                switch (NativeArch)
                {
                    case "arm64": return L.F("ARM64 ({0} bileşenleri)", IsWindows11 ? "x64" : "x86");
                    case "x86": return "32 bit";
                    default: return "64 bit";
                }
            }
        }

        public static string Summary => L.F("{0} · {1} · derleme {2}", WindowsName, ArchTitle, Build);
    }
}
