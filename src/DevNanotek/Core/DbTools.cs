using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DevNanotek.Core
{
    /// <summary>
    /// Adminer: tek PHP dosyasından oluşan veritabanı yöneticisi (MySQL/MariaDB, PostgreSQL, SQLite, SQL Server).
    /// http://localhost/adminer — yalnız bu bilgisayardan; yerel geliştirme için şifresiz girişe izin verilir.
    /// </summary>
    public static class AdminerManager
    {
        public static string Dir => Paths.BinAdminer;
        public static string Php => Path.Combine(Dir, "adminer.php");
        public static string Index => Path.Combine(Dir, "index.php");
        private static string VersionFile => Path.Combine(Dir, "version.txt");

        public static bool IsInstalled => File.Exists(Php);

        public static string InstalledVersion()
        {
            try { return File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : (IsInstalled ? "?" : null); }
            catch { return null; }
        }

        public static void SaveVersion(string v) { try { File.WriteAllText(VersionFile, v ?? ""); } catch { } }

        /// <summary>index.php (şifresiz giriş eklentisi) ve go.php (tek tıkla giriş) dosyalarını yazar.</summary>
        public static void WriteWrapper()
        {
            if (!IsInstalled) return;
            Templates.WriteIfChanged(Index,
                "<?php\n" +
                "/**\n" +
                " * DEVNANOTEK — Adminer (MySQL/MariaDB, PostgreSQL, SQLite, SQL Server)\n" +
                " * BU DOSYA OTOMATİK ÜRETİLİR. Yerel geliştirme için şifresiz girişe izin verir; yalnız bu bilgisayardan açılır.\n" +
                " */\n" +
                "function adminer_object() {\n" +
                "    class DevnanotekAdminer extends Adminer\\Adminer {\n" +
                "        function name() { return 'DEVNANOTEK Adminer'; }\n" +
                "        function login($login, $password) { return true; }\n" +
                "    }\n" +
                "    return new DevnanotekAdminer;\n" +
                "}\n" +
                "include __DIR__ . '/adminer.php';\n");
            Templates.WriteIfChanged(Path.Combine(Dir, "go.php"), Templates.Load("adminer-go.php"));
        }
    }

    /// <summary>
    /// Microsoft SQL Server için PHP eklentileri (sqlsrv, pdo_sqlsrv) ve Microsoft ODBC Driver 18.
    /// DLL'ler bin\sqlsrv'de tutulur; her kurulu PHP sürümünün ext klasörüne uygun olanı kopyalanır.
    /// </summary>
    public static class SqlSrvManager
    {
        /// <summary>PHP 8.1 / 8.2 için son uyumlu sürüm (5.13 yalnız 8.3+ içerir).</summary>
        public const string LegacyZipUrl = "https://github.com/microsoft/msphpsql/releases/download/v5.12.0/Windows_5.12.0RTW.zip";

        public static string Dir => Paths.BinSqlSrv;
        private static string VersionFile => Path.Combine(Dir, "version.txt");
        public static readonly string[] ExtNames = { "sqlsrv", "pdo_sqlsrv" };

        /// <summary>Kullanıcı Microsoft ODBC lisansını bu oturumda kabul etti mi (arayüz sorar).</summary>
        public static bool OdbcLicenseAccepted { get; set; }

        public static bool IsInstalled => Directory.Exists(Dir) && Directory.EnumerateFiles(Dir, "php_*sqlsrv_*.dll").Any();

        public static string InstalledVersion()
        {
            try { return File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : (IsInstalled ? "?" : null); }
            catch { return null; }
        }

        public static void SaveVersion(string v) { try { File.WriteAllText(VersionFile, v ?? ""); } catch { } }

        /// <summary>Zip içindeki php_[pdo_]sqlsrv_83_ts_x64.dll dosyalarını bin\sqlsrv'ye çıkarır.</summary>
        public static int ExtractDlls(string zip, bool overwrite)
        {
            Directory.CreateDirectory(Dir);
            int n = 0;
            using (var za = ZipFile.OpenRead(zip))
                foreach (var e in za.Entries)
                {
                    if (!Regex.IsMatch(e.Name, @"^php_(pdo_)?sqlsrv_\d{2}_(ts|nts)_(x64|x86)\.dll$", RegexOptions.IgnoreCase)) continue;
                    var target = Path.Combine(Dir, e.Name);
                    if (!overwrite && File.Exists(target)) continue;
                    e.ExtractToFile(target, true);
                    n++;
                }
            return n;
        }

        private static string Tag(string phpVersion)
        {
            var m = Regex.Match(phpVersion ?? "", @"^(\d+)\.(\d+)");
            return m.Success ? m.Groups[1].Value + m.Groups[2].Value : "";
        }

        /// <summary>Bu PHP sürümü için uygun DLL var mı (PHP 8.1+ thread-safe).</summary>
        public static bool Supports(string phpVersion)
            => File.Exists(Path.Combine(Dir, $"php_sqlsrv_{Tag(phpVersion)}_ts_{(SystemInfo.IsX86 ? "x86" : "x64")}.dll"));

        /// <summary>DLL'leri PHP'nin ext klasörüne php_sqlsrv.dll / php_pdo_sqlsrv.dll adıyla kopyalar.</summary>
        public static bool DeployTo(string phpVersion)
        {
            if (!PhpManager.IsInstalled(phpVersion) || !Supports(phpVersion)) return false;
            var arch = SystemInfo.IsX86 ? "x86" : "x64";
            bool any = false;
            foreach (var name in ExtNames)
            {
                var src = Path.Combine(Dir, $"php_{name}_{Tag(phpVersion)}_ts_{arch}.dll");
                if (!File.Exists(src)) continue;
                var dst = Path.Combine(PhpManager.ExtDir(phpVersion), $"php_{name}.dll");
                try
                {
                    if (!File.Exists(dst) || new FileInfo(dst).Length != new FileInfo(src).Length) File.Copy(src, dst, true);
                    any = true;
                }
                catch (Exception ex) { Logger.Warn($"{name} kopyalanamadı (PHP {phpVersion}, dosya kullanımda olabilir): {ex.Message}"); }
            }
            return any;
        }

        /// <summary>Tüm kurulu PHP sürümlerine dağıtır; ODBC sürücüsü varsa eklentileri php.ini'de açar.</summary>
        public static List<string> DeployAll(bool enable)
        {
            var done = new List<string>();
            foreach (var v in PhpManager.InstalledVersions())
            {
                if (!DeployTo(v)) continue;
                done.Add(v);
                if (enable && OdbcInstalled)
                {
                    var ini = PhpManager.ReadIni(v);
                    var orig = ini;
                    foreach (var n in ExtNames) ini = PhpManager.SetExtensionText(ini, n, true, false);
                    if (ini != orig) PhpManager.WriteIni(v, ini);
                }
            }
            return done;
        }

        /// <summary>Eklentileri php.ini'de kapatır, ext klasöründen ve bin\sqlsrv'den siler. ODBC sürücüsüne dokunmaz.</summary>
        public static string Remove()
        {
            // web sunucu ext\php_sqlsrv.dll'yi kilitler: önce durdurulur (silindikten sonra yapılandırma uygulanınca yeniden başlar)
            foreach (var svc in new[] { WindowsServices.Apache, WindowsServices.PhpFcgi })
                if (WindowsServices.IsRunning(svc)) WindowsServices.Stop(svc, 60);
            foreach (var v in PhpManager.InstalledVersions())
            {
                var ini = PhpManager.ReadIni(v);
                var orig = ini;
                foreach (var n in ExtNames) ini = PhpManager.SetExtensionText(ini, n, false, false);
                if (ini != orig) PhpManager.WriteIni(v, ini);
                foreach (var n in ExtNames)
                {
                    var f = Path.Combine(PhpManager.ExtDir(v), $"php_{n}.dll");
                    try { if (File.Exists(f)) File.Delete(f); } catch { }
                }
            }
            Downloader.TryDeleteDir(Dir);
            return Directory.Exists(Dir) ? "Klasör silinemedi (web sunucuyu durdurup tekrar deneyin): " + Dir : null;
        }

        // ---------------- Microsoft ODBC Driver ----------------
        public static bool OdbcInstalled
        {
            get
            {
                try
                {
                    var view = Environment.Is64BitOperatingSystem && !SystemInfo.IsX86 ? RegistryView.Registry64 : RegistryView.Registry32;
                    using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var k = hklm.OpenSubKey(@"SOFTWARE\ODBC\ODBCINST.INI"))
                        return k != null && k.GetSubKeyNames().Any(n => n == "ODBC Driver 18 for SQL Server" || n == "ODBC Driver 17 for SQL Server");
                }
                catch { return false; }
            }
        }

        /// <summary>ODBC Driver 18 kurulum paketi (Microsoft'un kalıcı yönlendirme bağlantıları).</summary>
        public static List<string> OdbcUrls => SystemInfo.IsX86
            ? new List<string> { "https://go.microsoft.com/fwlink/?linkid=2378646" }
            : new List<string> { "https://go.microsoft.com/fwlink/?linkid=2378279" };

        public static ProcessResult InstallOdbc(string msi)
        {
            var log = Path.Combine(Paths.Logs, "odbc-install.log");
            return ProcessRunner.Run("msiexec.exe", $"/i \"{msi}\" /quiet /norestart IACCEPTMSODBCSQLLICENSETERMS=YES /l*v \"{log}\"", null, 600000);
        }
    }
}
