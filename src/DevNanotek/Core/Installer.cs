using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DevNanotek.Core
{
    /// <summary>Bileşen kuruldu ancak Windows çalıştırılmasına izin vermiyor.</summary>
    public class ComponentBlockedException : Exception
    {
        public ComponentBlockedException(string msg) : base(msg) { }
    }

    /// <summary>Katalogdaki bir bileşeni indirir, açar ve yerleştirir.</summary>
    public static class Installer
    {
        public static string TargetDir(CatalogEntry e)
        {
            switch (e.Component)
            {
                case Comp.Php: return PhpManager.Dir(e.Version);
                case Comp.Apache: return ApacheManager.Dir(e.Version);
                case Comp.Nginx: return NginxManager.Dir(e.Version);
                case Comp.MariaDb: return DbManager.Dir(Comp.MariaDb, e.Version);
                case Comp.MySql: return DbManager.Dir(Comp.MySql, e.Version);
                case Comp.PostgreSql: return PostgreSqlManager.Dir(e.Version);
                case Comp.Node: return NodeManager.Dir(e.Version);
                case Comp.PhpMyAdmin: return PhpMyAdminManager.Dir;
                case Comp.Adminer: return AdminerManager.Dir;
                case Comp.SqlSrv: return SqlSrvManager.Dir;
                case Comp.Mailpit: return Paths.BinMailpit;
                case Comp.Mkcert: return Paths.BinMkcert;
                case Comp.WinSw: return Paths.BinWinSw;
                case Comp.Composer: return Paths.BinComposer;
                case Comp.CaCert: return Paths.EtcSsl;
                default: return Paths.Downloads;
            }
        }

        private static string[] Markers(string component)
        {
            switch (component)
            {
                case Comp.Php: return new[] { "php.exe" };
                case Comp.Apache: return new[] { @"bin\httpd.exe" };
                case Comp.Nginx: return new[] { "nginx.exe" };
                case Comp.MariaDb: return new[] { @"bin\mariadbd.exe", @"bin\mysqld.exe" };
                case Comp.MySql: return new[] { @"bin\mysqld.exe" };
                case Comp.PostgreSql: return new[] { @"bin\postgres.exe" };
                case Comp.Node: return new[] { "node.exe" };
                case Comp.PhpMyAdmin: return new[] { "index.php" };
                case Comp.Mailpit: return new[] { "mailpit.exe" };
                default: return new string[0];
            }
        }

        public static bool IsInstalled(CatalogEntry e)
        {
            switch (e.Component)
            {
                case Comp.Php: return PhpManager.IsInstalled(e.Version);
                case Comp.Apache: return ApacheManager.Installed().Contains(e.Version);
                case Comp.Nginx: return NginxManager.Installed().Contains(e.Version);
                case Comp.MariaDb: return DbManager.IsInstalled(Comp.MariaDb, e.Version);
                case Comp.MySql: return DbManager.IsInstalled(Comp.MySql, e.Version);
                case Comp.PostgreSql: return PostgreSqlManager.IsInstalled(e.Version);
                case Comp.Node: return NodeManager.IsInstalled(e.Version);
                case Comp.PhpMyAdmin: return PhpMyAdminManager.IsInstalled && PhpMyAdminManager.InstalledVersion() == e.Version;
                case Comp.Adminer: return AdminerManager.IsInstalled && AdminerManager.InstalledVersion() == e.Version;
                case Comp.SqlSrv: return SqlSrvManager.IsInstalled && SqlSrvManager.InstalledVersion() == e.Version;
                case Comp.Mailpit: return MailpitManager.IsInstalled;
                case Comp.Mkcert: return MkcertManager.IsInstalled;
                case Comp.WinSw: return WinSwManager.IsInstalled;
                case Comp.Composer: return ComposerManager.IsInstalled;
                case Comp.CaCert: return CaCertManager.IsInstalled;
                case Comp.VcRedist: return VcRedist.IsAdequate();
                default: return false;
            }
        }

        /// <summary>Bileşen şu an yapılandırmada aktif mi (silmeden önce kontrol).</summary>
        public static bool IsActive(CatalogEntry e, AppConfig cfg)
        {
            switch (e.Component)
            {
                case Comp.Php: return cfg.PhpVersion == e.Version;
                case Comp.Apache: return cfg.WebServer == "apache" && ApacheManager.ActiveVersion() == e.Version;
                case Comp.Nginx: return cfg.WebServer == "nginx" && NginxManager.ActiveVersion() == e.Version;
                case Comp.MariaDb: return cfg.DbEngine == Comp.MariaDb && cfg.DbVersion == e.Version;
                case Comp.MySql: return cfg.DbEngine == Comp.MySql && cfg.DbVersion == e.Version;
                case Comp.PostgreSql: return cfg.PgVersion == e.Version;
                case Comp.Node: return cfg.NodeVersion == e.Version;
                default: return false;
            }
        }

        public static async Task InstallAsync(CatalogEntry e, AppConfig cfg, IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            Paths.EnsureLayout();
            Logger.Info($"Kurulum başlıyor: {e.Title}");
            var urls = e.EffectiveUrls;
            if (urls == null || urls.Count == 0) throw new Exception($"{e.Title} bu Windows ({SystemInfo.ArchTitle}) için yayınlanmıyor.");
            var fileName = Path.GetFileName(new Uri(urls[0]).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName) || !fileName.Contains(".")) fileName = e.Component + "-" + e.Version;

            switch (e.Component)
            {
                case Comp.Php:
                case Comp.Apache:
                case Comp.Nginx:
                case Comp.MariaDb:
                case Comp.MySql:
                case Comp.PostgreSql:
                case Comp.Node:
                case Comp.PhpMyAdmin:
                case Comp.Mailpit:
                {
                    var zipPath = Path.Combine(Paths.Downloads, fileName);
                    var zip = await Downloader.DownloadAsync(urls, zipPath, progress, ct);
                    var target = TargetDir(e);
                    if (Directory.Exists(target))
                    {
                        progress?.Report(new DownloadProgress { Status = "Kullanımdaki servis durduruluyor (yeniden kurulum)...", Received = 1, Total = 1 });
                        await Task.Run(() => ReleaseTarget(e, target), ct);
                    }
                    var skip = SkipFor(e.Component);
                    try
                    {
                        await Task.Run(() => Downloader.ExtractZip(zip, target, Markers(e.Component), progress, ct, skip), ct);
                    }
                    catch (InvalidDataException)
                    {
                        // önbellekteki zip bozuk: silip yeniden indir
                        Logger.Warn("Bozuk zip, yeniden indiriliyor: " + zip);
                        try { File.Delete(zip); } catch { }
                        zip = await Downloader.DownloadAsync(urls, zipPath, progress, ct);
                        await Task.Run(() => Downloader.ExtractZip(zip, target, Markers(e.Component), progress, ct, skip), ct);
                    }
                    break;
                }
                case Comp.Adminer:
                    Directory.CreateDirectory(AdminerManager.Dir);
                    await Downloader.DownloadAsync(urls, AdminerManager.Php, progress, ct);
                    break;
                case Comp.SqlSrv:
                {
                    var zip = await Downloader.DownloadAsync(urls, Path.Combine(Paths.Downloads, fileName), progress, ct);
                    progress?.Report(new DownloadProgress { Status = "SQL Server eklentileri çıkarılıyor...", Received = 1, Total = 1 });
                    // web sunucu ext\php_sqlsrv.dll'yi kilitler: kopyalamadan önce durdurulur, PostInstall sonrası Apply yeniden başlatır
                    await Task.Run(() => ReleaseTarget(e, SqlSrvManager.Dir), ct);
                    if (await Task.Run(() => SqlSrvManager.ExtractDlls(zip, true), ct) == 0) throw new Exception("Zip içinde SQL Server eklentisi bulunamadı.");
                    // PHP 8.1 / 8.2 için önceki sürümün DLL'leri (5.13 yalnız 8.3+ içerir)
                    try
                    {
                        var legacy = await Downloader.DownloadAsync(new List<string> { SqlSrvManager.LegacyZipUrl }, Path.Combine(Paths.Downloads, "Windows_5.12.0RTW.zip"), progress, ct);
                        await Task.Run(() => SqlSrvManager.ExtractDlls(legacy, false), ct);
                    }
                    catch (Exception ex) { Logger.Warn("PHP 8.1/8.2 için SQL Server eklentisi indirilemedi: " + ex.Message); }
                    if (!SqlSrvManager.OdbcInstalled && SqlSrvManager.OdbcLicenseAccepted)
                    {
                        var msi = await Downloader.DownloadAsync(SqlSrvManager.OdbcUrls, Path.Combine(Paths.Downloads, "msodbcsql18.msi"), progress, ct);
                        progress?.Report(new DownloadProgress { Status = "Microsoft ODBC Driver 18 kuruluyor (sessiz)...", Received = 1, Total = 1 });
                        var r = await Task.Run(() => SqlSrvManager.InstallOdbc(msi), ct);
                        if (!r.Ok && r.ExitCode != 3010) throw new Exception("ODBC Driver 18 kurulamadı (kod " + r.ExitCode + "). Ayrıntı: " + Path.Combine(Paths.Logs, "odbc-install.log"));
                    }
                    break;
                }
                case Comp.Mkcert:
                    await Downloader.DownloadAsync(urls, MkcertManager.Exe, progress, ct);
                    break;
                case Comp.WinSw:
                    await Downloader.DownloadAsync(urls, WinSwManager.Exe, progress, ct);
                    break;
                case Comp.Composer:
                {
                    await Downloader.DownloadAsync(urls, ComposerManager.Phar, progress, ct);
                    Directory.CreateDirectory(Path.Combine(Paths.BinComposer, "home"));
                    break;
                }
                case Comp.CaCert:
                    await Downloader.DownloadAsync(urls, Paths.CaCertPem, progress, ct);
                    break;
                case Comp.VcRedist:
                {
                    var exe = await Downloader.DownloadAsync(urls, Path.Combine(Paths.Downloads, "vc_redist.x64.exe"), progress, ct);
                    progress?.Report(new DownloadProgress { Status = "Visual C++ Runtime kuruluyor (sessiz)...", Received = 1, Total = 1 });
                    var r = await Task.Run(() => VcRedist.Install(exe), ct);
                    if (!r.Ok) throw new Exception("VC++ Runtime kurulumu başarısız (kod " + r.ExitCode + "): " + r.AllOutput);
                    break;
                }
                default:
                    throw new Exception("Bilinmeyen bileşen: " + e.Component);
            }

            // çalıştırma testi: Windows bu programı engelliyor mu? (engelliyorsa aktif sürüm yapılmaz)
            var warn = await Task.Run(() => Probe(e), ct);

            // kurulum sonrası
            progress?.Report(new DownloadProgress { Status = "Yapılandırılıyor...", Received = 1, Total = 1 });
            await Task.Run(() => PostInstall(e, cfg, warn == null), ct);
            Logger.Info($"Kurulum tamamlandı: {e.Title}");

            if (warn != null)
            {
                Logger.Warn(warn);
                progress?.Report(new DownloadProgress { Status = "UYARI: " + warn, Received = 1, Total = 1 });
                throw new ComponentBlockedException(warn);
            }
            progress?.Report(new DownloadProgress { Status = e.Title + " kuruldu ve çalıştırma testi geçti.", Received = 1, Total = 1 });
        }

        /// <summary>Zip'ten çıkarılmayacak kısımlar (indirme aynı, disk ve süre tasarrufu).</summary>
        private static Func<string, bool> SkipFor(string component)
        {
            if (component != Comp.PostgreSql) return null;
            // EDB paketi: pgAdmin 4 (~600 MB, çok uzun yollar), StackBuilder, hata ayıklama sembolleri ve belgeler gerekmez
            return rel =>
            {
                var r = rel.Replace('/', '\\');
                return r.IndexOf(@"\pgAdmin 4\", StringComparison.OrdinalIgnoreCase) >= 0
                    || r.IndexOf(@"\StackBuilder\", StringComparison.OrdinalIgnoreCase) >= 0
                    || r.IndexOf(@"\symbols\", StringComparison.OrdinalIgnoreCase) >= 0
                    || r.IndexOf(@"\doc\", StringComparison.OrdinalIgnoreCase) >= 0;
            };
        }

        /// <summary>Yeniden kurulacak klasörü kullanan servisleri durdurur ve oradan çalışan süreçleri kapatır.</summary>
        private static void ReleaseTarget(CatalogEntry e, string target)
        {
            var cfg = AppConfig.Current;
            void Stop(string svc) { if (WindowsServices.Exists(svc)) WindowsServices.Stop(svc, 60); }
            switch (e.Component)
            {
                case Comp.Php:
                    if (cfg.PhpVersion == e.Version) { Stop(WindowsServices.Apache); Stop(WindowsServices.PhpFcgi); }
                    break;
                case Comp.SqlSrv:
                    Stop(WindowsServices.Apache); Stop(WindowsServices.PhpFcgi);
                    break;
                case Comp.PostgreSql:
                    if (cfg.PgVersion == e.Version) Stop(WindowsServices.PostgreSql);
                    break;
                case Comp.Apache: Stop(WindowsServices.Apache); break;
                case Comp.Nginx: Stop(WindowsServices.Nginx); break;
                case Comp.MariaDb:
                case Comp.MySql:
                    if (cfg.DbEngine == e.Component && cfg.DbVersion == e.Version) Stop(WindowsServices.Db);
                    break;
                case Comp.Mailpit: Stop(WindowsServices.Mailpit); break;
            }
            var prefix = target.TrimEnd('\\') + "\\";
            ResetManager.KillBinProcesses(s => Logger.Info(s), p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Kurulan programı zararsız bir parametreyle (-v / --version) çalıştırır.
        /// Akıllı Uygulama Denetimi engelliyorsa açıklayıcı mesaj döndürür; sorun yoksa null.
        /// </summary>
        public static string Probe(CatalogEntry e)
        {
            string exe = null, args = "-v";
            switch (e.Component)
            {
                case Comp.Php: exe = PhpManager.Exe(e.Version); break;
                case Comp.Apache: exe = ApacheManager.Exe(e.Version); break;
                case Comp.Nginx: exe = NginxManager.Exe(e.Version); break;
                case Comp.MariaDb:
                case Comp.MySql: exe = DbManager.Mysqld(e.Component, e.Version); args = "--version"; break;
                case Comp.PostgreSql: exe = PostgreSqlManager.Tool(e.Version, "postgres"); args = "--version"; break;
                case Comp.Node: exe = NodeManager.Exe(e.Version); break;
                case Comp.Mailpit: exe = MailpitManager.Exe; args = "version"; break;
                case Comp.Mkcert: exe = MkcertManager.Exe; args = "-version"; break;
            }
            if (exe == null || !File.Exists(exe)) return null;
            var r = ProcessRunner.Run(exe, args, Path.GetDirectoryName(exe), 20000);
            if (SystemCheck.IsBlocked(r))
                return $"{e.Title} kuruldu ama Windows ÇALIŞTIRILMASINI ENGELLEDİ (Akıllı Uygulama Denetimi — imzasız/yeni sürüm). " +
                       "Akıllı Uygulama Denetimi'ni kapatın veya bu bileşenin daha eski/yaygın bir sürümünü seçin.";
            if (r.ExitCode != 0 && string.IsNullOrWhiteSpace(r.AllOutput) && e.Component != Comp.Mailpit)
                return $"{e.Title} çalıştırma testinde hata verdi (çıkış kodu {r.ExitCode}). Visual C++ Runtime eksik/eski olabilir (Sürümler > Araçlar).";
            return null;
        }

        /// <param name="usable">Çalıştırma testi geçti mi (geçmediyse hiçbir zaman otomatik aktif yapılmaz).</param>
        private static void PostInstall(CatalogEntry e, AppConfig cfg, bool usable)
        {
            switch (e.Component)
            {
                case Comp.Php:
                    PhpManager.EnsureIni(e.Version, cfg);
                    PhpManager.ApplyInfrastructure(e.Version, cfg);
                    // SQL Server eklentisi kuruluysa yeni PHP sürümüne de uygun DLL'i koy
                    if (SqlSrvManager.IsInstalled && SqlSrvManager.DeployTo(e.Version) && SqlSrvManager.OdbcInstalled)
                        foreach (var n in SqlSrvManager.ExtNames) PhpManager.SetExtension(e.Version, n, true);
                    if (usable) PhpManager.SelfHeal(e.Version);
                    if (usable && (string.IsNullOrEmpty(cfg.PhpVersion) || !PhpManager.IsInstalled(cfg.PhpVersion))) cfg.PhpVersion = e.Version;
                    break;
                case Comp.MariaDb:
                case Comp.MySql:
                    if (usable && (string.IsNullOrEmpty(cfg.DbVersion) || !DbManager.IsInstalled(cfg.DbEngine, cfg.DbVersion)))
                    {
                        cfg.DbEngine = e.Component; cfg.DbVersion = e.Version;
                    }
                    break;
                case Comp.PostgreSql:
                    if (usable && (string.IsNullOrEmpty(cfg.PgVersion) || !PostgreSqlManager.IsInstalled(cfg.PgVersion))) cfg.PgVersion = e.Version;
                    // PHP'nin PostgreSQL eklentilerini (PHP paketinde hazır gelir) aç
                    foreach (var pv in PhpManager.InstalledVersions())
                    {
                        var avail = PhpManager.AvailableExtensions(pv);
                        foreach (var n in new[] { "pgsql", "pdo_pgsql" })
                            if (avail.Contains(n, StringComparer.OrdinalIgnoreCase) && !PhpManager.IsExtensionEnabledText(PhpManager.ReadIni(pv), n)) PhpManager.SetExtension(pv, n, true);
                    }
                    break;
                case Comp.Node:
                    if (usable && (string.IsNullOrEmpty(cfg.NodeVersion) || !NodeManager.IsInstalled(cfg.NodeVersion))) cfg.NodeVersion = e.Version;
                    if (NodeManager.IsInstalled(cfg.NodeVersion)) EnvPath.SetJunction(EnvPath.CurrentNode, NodeManager.Dir(cfg.NodeVersion));
                    break;
                case Comp.PhpMyAdmin:
                    PhpMyAdminManager.WriteConfig(cfg);
                    break;
                case Comp.Adminer:
                    AdminerManager.SaveVersion(e.Version);
                    AdminerManager.WriteWrapper();
                    break;
                case Comp.SqlSrv:
                {
                    SqlSrvManager.SaveVersion(e.Version);
                    var done = SqlSrvManager.DeployAll(true);
                    Logger.Info("SQL Server eklentisi dağıtıldı: PHP " + (done.Count == 0 ? "(uygun sürüm yok)" : string.Join(", ", done)));
                    break;
                }
                case Comp.Mailpit:
                    MailpitManager.SaveVersion(e.Version);
                    break;
                case Comp.CaCert:
                    MkcertManager.WriteCaBundle();
                    foreach (var v in PhpManager.InstalledVersions()) PhpManager.ApplyInfrastructure(v, cfg);
                    break;
                case Comp.Composer:
                    EnvPath.WriteShims(cfg);
                    break;
            }
            cfg.Save();
        }

        /// <summary>Kurulu bir sürümü siler. Aktifse false döner (önce başka sürüme geçilmeli).</summary>
        public static string Uninstall(CatalogEntry e, AppConfig cfg)
        {
            if (IsActive(e, cfg)) return "Bu sürüm şu an aktif. Önce başka bir sürüme geçin (Ayarlar) sonra silin.";
            var dir = TargetDir(e);
            switch (e.Component)
            {
                case Comp.Php:
                case Comp.Apache:
                case Comp.Nginx:
                case Comp.MariaDb:
                case Comp.MySql:
                case Comp.PostgreSql:
                case Comp.Node:
                    if (!Directory.Exists(dir)) return null;
                    Downloader.TryDeleteDir(dir);
                    if (Directory.Exists(dir)) return "Klasör silinemedi (dosyalar kullanımda olabilir): " + dir;
                    Logger.Info("Silindi: " + dir);
                    return null;
                case Comp.PhpMyAdmin:
                case Comp.Adminer:
                    Downloader.TryDeleteDir(dir);
                    return Directory.Exists(dir) ? "Klasör silinemedi: " + dir : null;
                case Comp.SqlSrv:
                    return SqlSrvManager.Remove();
                default:
                    return "Bu bileşen buradan silinemez.";
            }
        }

        /// <summary>Önerilen başlangıç paketi (sihirbaz ve --install-defaults için).</summary>
        public static List<CatalogEntry> DefaultSet(bool includeNode = true, bool includeNginx = false)
        {
            var c = Catalog.Current;
            var comps = new List<string> { Comp.WinSw, Comp.CaCert, Comp.Php, Comp.Apache, Comp.MariaDb, Comp.PhpMyAdmin, Comp.Adminer, Comp.Mailpit, Comp.Mkcert, Comp.Composer };
            if (includeNginx) comps.Add(Comp.Nginx);
            if (includeNode) comps.Add(Comp.Node);
            var list = new List<CatalogEntry>();
            if (!VcRedist.IsAdequate()) { var vc = c.Recommended(Comp.VcRedist); if (vc != null) list.Add(vc); }
            foreach (var comp in comps)
            {
                var e = c.Recommended(comp);
                if (e != null && !IsInstalled(e)) list.Add(e);
            }
            return list;
        }

        /// <summary>"php:8.3.35" veya "php" (önerilen) biçimini çözümler.</summary>
        public static CatalogEntry Parse(string spec)
        {
            var parts = (spec ?? "").Split(new[] { ':', '@' }, 2);
            var comp = parts[0].Trim().ToLowerInvariant();
            if (comp == "node.js" || comp == "nodejs") comp = Comp.Node;
            if (comp == "pg" || comp == "postgres" || comp == "pgsql") comp = Comp.PostgreSql;
            if (comp == "mssql" || comp == "sqlserver") comp = Comp.SqlSrv;
            if (parts.Length == 1 || string.IsNullOrWhiteSpace(parts[1])) return Catalog.Current.Recommended(comp);
            var ver = parts[1].Trim();
            return Catalog.Current.Find(comp, ver) ?? Catalog.Custom(comp, ver);
        }

        /// <summary>Veri dizinini de silen tam temizlik (veritabanı sürümü için).</summary>
        public static string DeleteDataDir(string engine, string version)
        {
            if (engine == Comp.PostgreSql)
            {
                var pd = PostgreSqlManager.DataDir(version);
                if (!Directory.Exists(pd)) return null;
                if (WindowsServices.IsRunning(WindowsServices.PostgreSql) && (WindowsServices.GetImagePath(WindowsServices.PostgreSql) ?? "").IndexOf(pd, StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Bu veri klasörü şu an çalışan PostgreSQL tarafından kullanılıyor.";
                Downloader.TryDeleteDir(pd);
                return Directory.Exists(pd) ? "Silinemedi: " + pd : null;
            }
            var d = DbManager.DataDir(engine, version);
            if (!Directory.Exists(d)) return null;
            if (WindowsServices.IsRunning(WindowsServices.Db) && (WindowsServices.GetImagePath(WindowsServices.Db) ?? "").IndexOf(DbManager.Dir(engine, version), StringComparison.OrdinalIgnoreCase) >= 0)
                return "Bu veri dizini şu an çalışan veritabanı tarafından kullanılıyor.";
            Downloader.TryDeleteDir(d);
            return Directory.Exists(d) ? "Silinemedi: " + d : null;
        }
    }
}
