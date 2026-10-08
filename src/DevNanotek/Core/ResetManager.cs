using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DevNanotek.Core
{
    public enum ResetKind
    {
        /// <summary>Servisleri kaldırıp yeniden kurar, yapılandırmayı yeniden üretir. Hiçbir şey silinmez.</summary>
        Quick,
        /// <summary>Aktif PHP, web sunucu, veritabanı programı ve araçlar yeniden kurulur; php.ini/custom.conf/custom.cnf varsayılana döner. Veriler korunur.</summary>
        Full,
        /// <summary>Veritabanları yedeklenir (SQL + veri klasörü), boş veritabanı oluşturulur, root şifresi boşalır.</summary>
        Database,
        /// <summary>httpdocs (projeler) hariç her şey yedeklenip temizlenir; kurulum sihirbazı yeniden açılır.</summary>
        Factory
    }

    /// <summary>Onarım ve sıfırlama işlemleri. Silinen her şey önce backups\sifirlama-TARİH klasörüne alınır.</summary>
    public static class ResetManager
    {
        public static string Title(ResetKind k)
        {
            switch (k)
            {
                case ResetKind.Quick: return "Hızlı onarım";
                case ResetKind.Full: return "Tam onarım";
                case ResetKind.Database: return "Veritabanını sıfırla";
                default: return "Fabrika ayarlarına dön";
            }
        }

        public static string NewBackupDir()
        {
            var d = Path.Combine(Paths.Backups, "sifirlama-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(d);
            return d;
        }

        public static async Task<ApplyResult> RunAsync(ResetKind kind, AppConfig cfg, Action<string> log, IProgress<DownloadProgress> progress,
            CancellationToken ct, bool deleteDownloads = false)
        {
            log = log ?? (_ => { });
            Logger.Info("Sıfırlama başlıyor: " + Title(kind));
            switch (kind)
            {
                case ResetKind.Quick: return await Task.Run(() => Quick(cfg, log), ct);
                case ResetKind.Full: return await Full(cfg, log, progress, ct);
                case ResetKind.Database: return await Task.Run(() => Database(cfg, log), ct);
                default: return await Task.Run(() => Factory(cfg, log, deleteDownloads), ct);
            }
        }

        // ------------------------------------------------------------------
        private static ApplyResult Quick(AppConfig cfg, Action<string> log)
        {
            RemoveServices(log);
            KillBinProcesses(log);
            log("Yapılandırma yeniden üretiliyor ve servisler kuruluyor...");
            return Stack.Apply(cfg, log, true);
        }

        // ------------------------------------------------------------------
        private static async Task<ApplyResult> Full(AppConfig cfg, Action<string> log, IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            var res = new ApplyResult();
            var bdir = NewBackupDir();
            await Task.Run(() =>
            {
                BackupConfigs(cfg, bdir, log);
                RemoveServices(log);
                KillBinProcesses(log);
                log("custom.conf / custom.cnf varsayılana döndürülüyor (eskileri yedeklendi)...");
                foreach (var f in new[] { Paths.ApacheCustomConf, Paths.NginxCustomConf, DbManager.CustomCnf, PostgreSqlManager.CustomConf })
                    try { if (File.Exists(f)) File.Delete(f); } catch { }
            }, ct);

            foreach (var e in ActiveEntries(cfg))
            {
                ct.ThrowIfCancellationRequested();
                log("Yeniden kuruluyor: " + e.Title);
                try { await Installer.InstallAsync(e, cfg, progress, ct); }
                catch (ComponentBlockedException bex) { res.Warnings.Add(bex.Message); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { res.Errors.Add(e.Title + ": " + ex.Message); Logger.Error("Tam onarım: " + e.Title, ex); }
            }

            log("Yapılandırma uygulanıyor ve servisler başlatılıyor...");
            var r = await Task.Run(() => Stack.Apply(cfg, log, true), ct);
            res.Errors.AddRange(r.Errors);
            res.Warnings.AddRange(r.Warnings);
            res.Warnings.Add("Eski ayar dosyalarınız yedeklendi: " + bdir);
            return res;
        }

        /// <summary>Şu an kullanılan bileşenlerin katalog girdileri (tam onarımda yeniden kurulacaklar).</summary>
        public static List<CatalogEntry> ActiveEntries(AppConfig cfg)
        {
            var cat = Catalog.Current;
            var list = new List<CatalogEntry>();
            void Add(CatalogEntry e) { if (e != null && !list.Any(x => x.Id == e.Id)) list.Add(e); }

            Add(cat.Recommended(Comp.WinSw));
            Add(cat.Recommended(Comp.CaCert));
            if (PhpManager.IsInstalled(cfg.PhpVersion)) Add(cat.Find(Comp.Php, cfg.PhpVersion) ?? Catalog.Custom(Comp.Php, cfg.PhpVersion));
            if (cfg.WebServer == "nginx")
            {
                var v = NginxManager.ActiveVersion();
                Add(v != null ? cat.Find(Comp.Nginx, v) ?? Catalog.Custom(Comp.Nginx, v) : cat.Recommended(Comp.Nginx));
            }
            else
            {
                var v = ApacheManager.ActiveVersion();
                Add(v != null ? cat.Find(Comp.Apache, v) ?? cat.Recommended(Comp.Apache) : cat.Recommended(Comp.Apache));
            }
            if (DbManager.IsInstalled(cfg.DbEngine, cfg.DbVersion)) Add(cat.Find(cfg.DbEngine, cfg.DbVersion) ?? Catalog.Custom(cfg.DbEngine, cfg.DbVersion));
            if (PostgreSqlManager.IsActive(cfg)) Add(cat.Find(Comp.PostgreSql, cfg.PgVersion) ?? Catalog.Custom(Comp.PostgreSql, cfg.PgVersion));
            if (PhpMyAdminManager.IsInstalled) Add(cat.Find(Comp.PhpMyAdmin, PhpMyAdminManager.InstalledVersion()) ?? cat.Recommended(Comp.PhpMyAdmin));
            if (AdminerManager.IsInstalled) Add(cat.Recommended(Comp.Adminer));
            if (SqlSrvManager.IsInstalled) Add(cat.Recommended(Comp.SqlSrv));
            if (MailpitManager.IsInstalled) Add(cat.Recommended(Comp.Mailpit));
            if (MkcertManager.IsInstalled) Add(cat.Recommended(Comp.Mkcert));
            if (ComposerManager.IsInstalled) Add(cat.Recommended(Comp.Composer));
            if (NodeManager.IsInstalled(cfg.NodeVersion)) Add(cat.Find(Comp.Node, cfg.NodeVersion) ?? Catalog.Custom(Comp.Node, cfg.NodeVersion));
            return list;
        }

        // ------------------------------------------------------------------
        private static ApplyResult Database(AppConfig cfg, Action<string> log)
        {
            var res = new ApplyResult();
            if (!DbManager.IsInstalled(cfg.DbEngine, cfg.DbVersion)) { res.Errors.Add("Veritabanı kurulu değil."); return res; }
            var bdir = NewBackupDir();
            DumpIfRunning(cfg, bdir, log, res);

            log("Veritabanı servisi kaldırılıyor...");
            DbManager.UninstallService();
            KillBinProcesses(log, p => p.IndexOf(Paths.BinMariaDb, StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf(Paths.BinMySql, StringComparison.OrdinalIgnoreCase) >= 0);

            var data = DbManager.DataDir(cfg.DbEngine, cfg.DbVersion);
            if (Directory.Exists(data))
            {
                var target = Path.Combine(bdir, Path.GetFileName(data));
                log("Eski veri klasörü yedeğe taşınıyor: " + target);
                MoveDir(data, target);
            }
            cfg.DbRootPassword = "";
            cfg.Save();

            log("Boş veritabanı oluşturuluyor...");
            var r = Stack.Apply(cfg, log, true);
            res.Errors.AddRange(r.Errors);
            res.Warnings.AddRange(r.Warnings);
            res.Warnings.Add("Eski veritabanlarınız yedeklendi: " + bdir + "  (geri yüklemek için Ayarlar > Veritabanı > Yedekten geri yükle ile oradaki .sql dosyasını seçin)");
            return res;
        }

        // ------------------------------------------------------------------
        private static ApplyResult Factory(AppConfig cfg, Action<string> log, bool deleteDownloads)
        {
            var res = new ApplyResult();
            var bdir = NewBackupDir();
            DumpIfRunning(cfg, bdir, log, res);
            // PostgreSQL de SQL olarak yedeklenir (veri klasörü ayrıca data ile birlikte taşınır)
            var pf = PostgreSqlManager.BackupIfPossible(cfg, log, out var pw);
            if (pw != null) res.Warnings.Add(pw);
            if (pf != null) try { File.Move(pf, Path.Combine(bdir, Path.GetFileName(pf))); log("PostgreSQL yedeği alındı."); } catch { }

            Stack.RemoveAllServices(log, false);
            log("Yerel SSL kök sertifikası kaldırılıyor...");
            MkcertManager.UninstallCa();
            KillBinProcesses(log);

            log("Kısayol bağlantıları (bin\\current) kaldırılıyor...");
            foreach (var link in new[] { EnvPath.CurrentPhp, EnvPath.CurrentNode, EnvPath.CurrentDb, EnvPath.CurrentPg }) EnvPath.SetJunction(link, null);

            log("Veritabanları ve ayarlar yedeğe taşınıyor: " + bdir);
            MoveDir(Paths.Data, Path.Combine(bdir, "data"));
            MoveDir(Paths.Etc, Path.Combine(bdir, "etc"));
            try { if (File.Exists(Paths.ConfigFile)) File.Move(Paths.ConfigFile, Path.Combine(bdir, "config.json")); } catch (Exception ex) { res.Warnings.Add("config.json taşınamadı: " + ex.Message); }
            foreach (var extra in new[] { Paths.ConfigFile + ".bak", Paths.ConfigFile + ".tmp" })
                try { if (File.Exists(extra)) File.Delete(extra); } catch { }

            log("Programlar, günlükler ve geçici dosyalar siliniyor...");
            foreach (var d in new[] { Paths.Bin, Paths.Tmp, Paths.Logs })
            {
                Downloader.TryDeleteDir(d);
                if (Directory.Exists(d)) res.Warnings.Add("Silinemedi (bir dosya kullanımda olabilir): " + d);
            }
            if (deleteDownloads)
            {
                Downloader.TryDeleteDir(Paths.Downloads);
                if (Directory.Exists(Paths.Downloads)) res.Warnings.Add("Silinemedi: " + Paths.Downloads);
            }

            AppConfig.ResetToDefaults();
            Paths.EnsureLayout();
            Logger.Info("Fabrika ayarlarına dönüldü. Yedek: " + bdir);
            log("Tamamlandı. Projeleriniz (httpdocs) olduğu gibi duruyor. Yedek: " + bdir);
            res.Warnings.Add("Projeleriniz (httpdocs) korundu. Eski veritabanları ve ayarlar yedeklendi: " + bdir);
            return res;
        }

        // ------------------------------------------------------------------
        //  Yardımcılar
        // ------------------------------------------------------------------
        private static void RemoveServices(Action<string> log)
        {
            log("Servisler durdurulup kaldırılıyor...");
            ApacheManager.UninstallService();
            NginxManager.UninstallServices();
            DbManager.UninstallService();
            PostgreSqlManager.UninstallService();
            MailpitManager.RemoveService();
        }

        /// <summary>bin klasöründen çalışan tüm süreçleri (httpd, mysqld, php-cgi, nginx, node…) kapatır.</summary>
        public static int KillBinProcesses(Action<string> log, Func<string, bool> filter = null)
        {
            int n = 0;
            var bin = Paths.Bin.TrimEnd('\\') + "\\";
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    string path = null;
                    try { path = p.MainModule?.FileName; } catch { }
                    if (path == null || !path.StartsWith(bin, StringComparison.OrdinalIgnoreCase)) continue;
                    if (filter != null && !filter(path)) continue;
                    log?.Invoke("Süreç kapatılıyor: " + p.ProcessName + " (PID " + p.Id + ")");
                    p.Kill();
                    p.WaitForExit(8000);
                    n++;
                }
                catch { }
                finally { p.Dispose(); }
            }
            return n;
        }

        private static void DumpIfRunning(AppConfig cfg, string bdir, Action<string> log, ApplyResult res)
        {
            if (!DbManager.IsInstalled(cfg.DbEngine, cfg.DbVersion)) return;
            if (!WindowsServices.IsRunning(WindowsServices.Db) && !PortUtil.IsListening(cfg.DbPort)) { log("Veritabanı çalışmıyor; SQL yedeği atlandı (veri klasörü yine de yedeklenecek)."); return; }
            try
            {
                var f = DbManager.Backup(cfg, log);
                var target = Path.Combine(bdir, Path.GetFileName(f));
                File.Move(f, target);
                log("SQL yedeği alındı: " + target);
            }
            catch (Exception ex)
            {
                res.Warnings.Add("SQL yedeği alınamadı (veri klasörü yine de yedeklendi): " + ex.Message);
            }
        }

        private static void BackupConfigs(AppConfig cfg, string bdir, Action<string> log)
        {
            log("Ayar dosyaları yedekleniyor: " + bdir);
            void Copy(string src, string name)
            {
                try { if (File.Exists(src)) File.Copy(src, Path.Combine(bdir, name), true); } catch { }
            }
            foreach (var v in PhpManager.InstalledVersions()) Copy(PhpManager.IniPath(v), "php-" + v + ".ini");
            Copy(Paths.ApacheCustomConf, "apache-custom.conf");
            Copy(Paths.NginxCustomConf, "nginx-custom.conf");
            Copy(DbManager.CustomCnf, "db-custom.cnf");
            Copy(Paths.MyIni, "my.ini");
            Copy(PostgreSqlManager.CustomConf, "postgresql-custom.conf");
            Copy(Paths.ConfigFile, "config.json");
        }

        /// <summary>Klasörü taşır (aynı sürücüde anında); olmazsa kopyalayıp siler.</summary>
        private static void MoveDir(string src, string dst)
        {
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            try { Directory.Move(src, dst); return; }
            catch (Exception ex)
            {
                Logger.Warn("Taşınamadı, kopyalanıyor: " + src + " :: " + ex.Message);
            }
            foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(src, dst));
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(src, dst), true);
            Downloader.TryDeleteDir(src);
        }
    }
}
