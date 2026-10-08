using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Win32;

namespace DevNanotek.Core
{
    /// <summary>
    /// Denetim Masası > Programlar ve Özellikler kaydı
    /// (HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DevNanotek).
    /// "Kaldır" → DevNanotek.exe --uninstall, "Değiştir" → DevNanotek.exe --repair (Onarım ve Sıfırlama).
    /// </summary>
    public static class UninstallRegistry
    {
        public const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DevNanotek";

        public static bool IsRegistered()
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(KeyPath)) return k != null; } catch { return false; }
        }

        public static void Register()
        {
            if (!File.Exists(Paths.AppExe)) return;
            try
            {
                var exe = Paths.AppExe;
                var ver = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
                using (var k = Registry.LocalMachine.CreateSubKey(KeyPath))
                {
                    if (k == null) return;
                    k.SetValue("DisplayName", "DEVNANOTEK Local Server");
                    k.SetValue("DisplayVersion", ver);
                    k.SetValue("Publisher", "DEVNANOTEK");
                    k.SetValue("URLInfoAbout", "https://devnanotek.net/");
                    k.SetValue("HelpLink", "https://devnanotek.net/");
                    k.SetValue("Comments", "PHP, MariaDB/MySQL, Apache/Nginx, Node.js, phpMyAdmin yerel geliştirme ortamı");
                    k.SetValue("DisplayIcon", exe + ",0");
                    k.SetValue("InstallLocation", Paths.Root);
                    k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                    k.SetValue("QuietUninstallString", "\"" + exe + "\" --uninstall --quiet");
                    k.SetValue("ModifyPath", "\"" + exe + "\" --repair");
                    k.SetValue("NoModify", 0, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    if (k.GetValue("InstallDate") == null) k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                    k.SetValue("EstimatedSize", EstimateSizeKb(), RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { Logger.Warn("Programlar ve Özellikler kaydı yazılamadı: " + ex.Message); }
        }

        public static void Unregister()
        {
            try { Registry.LocalMachine.DeleteSubKeyTree(KeyPath, false); Logger.Info("Programlar ve Özellikler kaydı silindi"); }
            catch (Exception ex) { Logger.Warn("Programlar ve Özellikler kaydı silinemedi: " + ex.Message); }
        }

        /// <summary>app + bin klasörlerinin boyutu (KB). Veriler ve projeler dahil edilmez.</summary>
        private static int EstimateSizeKb()
        {
            long total = 0;
            foreach (var d in new[] { Paths.AppDir, Paths.Bin })
            {
                try
                {
                    if (!Directory.Exists(d)) continue;
                    foreach (var f in new DirectoryInfo(d).EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        if (f.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                        total += f.Length;
                    }
                }
                catch { }
            }
            return (int)Math.Min(int.MaxValue, total / 1024);
        }
    }

    /// <summary>DevNanotek'i bilgisayardan kaldırır.</summary>
    public static class Uninstaller
    {
        /// <param name="deleteEverything">true: C:\devnanotek tamamen silinir (projeler ve veritabanları dahil).
        /// false: httpdocs, data, backups ve config.json kalır; programlar ve sistem kayıtları silinir.</param>
        /// <param name="dumpToDesktop">Silmeden önce tüm veritabanlarının SQL yedeğini Masaüstüne al.</param>
        public static ApplyResult Run(bool deleteEverything, bool dumpToDesktop, Action<string> log)
        {
            log = log ?? (_ => { });
            var res = new ApplyResult();
            var cfg = AppConfig.Current;
            Logger.Info("Kaldırma başlıyor (her şeyi sil: " + deleteEverything + ")");

            if (deleteEverything && !IsSafeRoot(Paths.Root))
            {
                res.Errors.Add("Güvenlik nedeniyle bu klasör tamamen silinemez: " + Paths.Root + ". 'Verilerimi koru' seçeneğini kullanın.");
                return res;
            }

            // 1) Masaüstüne SQL yedeği
            if (dumpToDesktop && DbManager.IsInstalled(cfg.DbEngine, cfg.DbVersion))
            {
                try
                {
                    if (!WindowsServices.IsRunning(WindowsServices.Db) && WindowsServices.Exists(WindowsServices.Db))
                    {
                        log("Yedek için veritabanı başlatılıyor...");
                        WindowsServices.Start(WindowsServices.Db, 60);
                    }
                    if (WindowsServices.IsRunning(WindowsServices.Db) || PortUtil.IsListening(cfg.DbPort))
                    {
                        var f = DbManager.Backup(cfg, log);
                        var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        var target = Path.Combine(desk, "DevNanotek-veritabani-yedegi-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".sql");
                        File.Copy(f, target, true);
                        log("Veritabanı yedeği Masaüstüne kaydedildi: " + target);
                        res.Warnings.Add("Veritabanı yedeği: " + target);
                    }
                    else res.Warnings.Add("Veritabanı çalışmadığı için SQL yedeği alınamadı.");
                }
                catch (Exception ex) { res.Warnings.Add("Veritabanı yedeği alınamadı: " + ex.Message); }
            }
            if (dumpToDesktop)
            {
                var pf = PostgreSqlManager.BackupIfPossible(cfg, log, out var pw);
                if (pw != null) res.Warnings.Add(pw);
                if (pf != null)
                {
                    try
                    {
                        var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        var target = Path.Combine(desk, "DevNanotek-postgresql-yedegi-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".sql");
                        File.Copy(pf, target, true);
                        log("PostgreSQL yedeği Masaüstüne kaydedildi: " + target);
                        res.Warnings.Add("PostgreSQL yedeği: " + target);
                    }
                    catch (Exception ex) { res.Warnings.Add("PostgreSQL yedeği Masaüstüne kopyalanamadı: " + ex.Message); }
                }
            }

            // 2) Servisler, hosts, PATH, otomatik başlatma, SSL kök sertifikası, kısayollar
            Stack.RemoveAllServices(log, true);
            ResetManager.KillBinProcesses(log);
            foreach (var link in new[] { EnvPath.CurrentPhp, EnvPath.CurrentNode, EnvPath.CurrentDb, EnvPath.CurrentPg }) EnvPath.SetJunction(link, null);

            // 3) Denetim Masası kaydı, güvenlik duvarı kuralları, tepsi simgesi önbelleği, Defender istisnası
            log("Programlar ve Özellikler kaydı siliniyor...");
            UninstallRegistry.Unregister();
            log("Güvenlik duvarı kuralları temizleniyor...");
            var fw = FirewallManager.RemoveRulesUnder(Paths.Root);
            if (fw > 0) log($"  {fw} programa ait güvenlik duvarı kuralı silindi.");
            var ti = TrayIconRegistry.RemoveEntriesUnder(Paths.Root);
            if (ti > 0) log("  Tepsi simgesi kaydı silindi.");
            if (cfg.DefenderExcluded || DefenderManager.IsExcluded())
            {
                log("Windows Defender istisnası kaldırılıyor...");
                var de = DefenderManager.SetExcluded(false);
                if (de != null) res.Warnings.Add("Defender istisnası kaldırılamadı: " + de);
            }

            // 4) Dosyalar
            log("Program dosyaları siliniyor...");
            var toDelete = new[] { Paths.Bin, Paths.Etc, Paths.Tmp, Paths.Downloads, Paths.Docs }.ToList();
            if (deleteEverything) toDelete.AddRange(new[] { Paths.Data, Paths.Backups, Paths.HttpDocs });
            foreach (var d in toDelete)
            {
                Downloader.TryDeleteDir(d);
                if (Directory.Exists(d)) res.Warnings.Add("Silinemedi (kullanımda olabilir): " + d);
            }
            if (deleteEverything)
            {
                foreach (var f in new[] { Paths.ConfigFile, Paths.ConfigFile + ".bak", Paths.ConfigFile + ".tmp" })
                    try { if (File.Exists(f)) File.Delete(f); } catch { }
            }
            else
            {
                // yeniden kurulursa sihirbaz açılsın, ayarlar (portlar, root şifresi) kalsın
                try { cfg.SetupCompleted = false; cfg.Save(); } catch { }
                foreach (var f in new[] { Paths.ConfigFile + ".bak", Paths.ConfigFile + ".tmp" })
                    try { if (File.Exists(f)) File.Delete(f); } catch { }
                log("Korunanlar: " + Paths.HttpDocs + ", " + Paths.Data + ", " + Paths.Backups);
            }

            Logger.Info("Kaldırma tamamlandı");
            // Not: ScheduleFinalCleanup, program kapanmadan hemen önce çağıran tarafından çalıştırılır.
            return res;
        }

        /// <summary>
        /// Çalışan exe kendini silemez: program kapandıktan birkaç saniye sonra gizli bir komut
        /// app (ve logs) klasörünü — "her şeyi sil" seçildiyse tüm kök klasörü — siler.
        /// </summary>
        public static void ScheduleFinalCleanup(bool deleteEverything)
        {
            try
            {
                string q(string p) => "\"" + p + "\"";
                string cmd = deleteEverything
                    ? $"ping -n 6 127.0.0.1 >nul & rmdir /s /q {q(Paths.Root)}"
                    : $"ping -n 6 127.0.0.1 >nul & rmdir /s /q {q(Paths.AppDir)} & rmdir /s /q {q(Paths.Logs)} & rmdir {q(Paths.Root)} 2>nul";
                var psi = new ProcessStartInfo("cmd.exe", "/d /c " + cmd)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetTempPath()
                };
                Process.Start(psi);
            }
            catch (Exception ex) { Logger.Warn("Son temizlik zamanlanamadı: " + ex.Message); }
        }

        /// <summary>Kök klasör tamamen silinmeye uygun mu (sürücü kökü / sistem klasörü değil).</summary>
        public static bool IsSafeRoot(string root)
        {
            try
            {
                var full = Path.GetFullPath(root).TrimEnd('\\');
                if (full.Length <= 3) return false; // C:\
                var forbidden = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                };
                if (forbidden.Any(f => !string.IsNullOrEmpty(f) && string.Equals(f.TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase))) return false;
                // gerçekten bir DevNanotek klasörü mü?
                return Directory.Exists(Path.Combine(full, "httpdocs")) || File.Exists(Path.Combine(full, "config.json")) || Directory.Exists(Path.Combine(full, "app"));
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Çalışan DevNanotek'e ikinci bir kopyadan komut gönderme (göster / onar / çık).
    /// Komut tmp\ipc.cmd dosyasına yazılır, "DevNanotek-Show" olayı tetiklenir.
    /// </summary>
    public static class Ipc
    {
        public const string EventName = @"Local\DevNanotek-Show";
        public const string MutexName = @"Local\DevNanotek-GUI";
        private static string CmdFile => Path.Combine(Paths.Tmp, "ipc.cmd");

        public static bool IsGuiRunning()
        {
            try
            {
                using (var m = System.Threading.Mutex.OpenExisting(MutexName)) return true;
            }
            catch { return false; }
        }

        public static bool Send(string command)
        {
            try
            {
                Directory.CreateDirectory(Paths.Tmp);
                File.WriteAllText(CmdFile, command ?? "show");
                using (var ev = System.Threading.EventWaitHandle.OpenExisting(EventName)) ev.Set();
                return true;
            }
            catch { return false; }
        }

        public static string Take()
        {
            try
            {
                if (!File.Exists(CmdFile)) return "show";
                var c = File.ReadAllText(CmdFile).Trim();
                File.Delete(CmdFile);
                return string.IsNullOrEmpty(c) ? "show" : c;
            }
            catch { return "show"; }
        }

        /// <summary>Çalışan arayüzü kapatır ve kapanmasını bekler.</summary>
        public static void CloseRunningGui(int timeoutMs = 15000)
        {
            if (!IsGuiRunning()) return;
            Send("exit");
            var sw = Stopwatch.StartNew();
            while (IsGuiRunning() && sw.ElapsedMilliseconds < timeoutMs) System.Threading.Thread.Sleep(300);
        }
    }
}
