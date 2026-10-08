using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DevNanotek.Core
{
    /// <summary>Kurulu bir bileşen için çıkmış yeni yama sürümü.</summary>
    public class UpdateInfo
    {
        public string Component { get; set; }
        public string Name { get; set; }
        public string Current { get; set; }
        public string New { get; set; }
        public bool IsActive { get; set; }
        public CatalogEntry Entry { get; set; }
        public string Text => $"{Name} {Current} → {New}";
    }

    /// <summary>
    /// Yeni sürüm denetimi: katalog en fazla 12 saatte bir internetten yenilenir; kurulu her bileşen için
    /// aynı daldaki yeni yama (ör. PHP 8.3.35 → 8.3.36) "güncelleme" olarak listelenir ve tek tıkla uygulanır.
    /// </summary>
    public static class UpdateChecker
    {
        public static List<UpdateInfo> Current { get; private set; } = new List<UpdateInfo>();
        public static event Action Changed;
        public static bool IsChecking { get; private set; }
        public static string LastError { get; private set; }

        public static async Task AutoCheckAsync(bool force = false)
        {
            if (IsChecking) return;
            IsChecking = true;
            try { Changed?.Invoke(); } catch { }
            try
            {
                var last = Catalog.Current.UpdatedAt?.ToLocalTime();
                if (force || last == null || (DateTime.Now - last.Value).TotalHours >= 12)
                {
                    await Catalog.Current.RefreshOnlineAsync(null).ConfigureAwait(false);
                    Logger.Info("Sürüm kataloğu internetten güncellendi");
                }
                LastError = null;
            }
            catch (Exception ex) { LastError = ex.Message; Logger.Warn("Sürüm denetimi yapılamadı: " + ex.Message); }
            // DEVNANOTEK'in kendi yeni sürümü (GitHub Releases) — hata olursa sessizce geçilir
            await AppUpdater.CheckAsync().ConfigureAwait(false);
            IsChecking = false;
            Recompute();
        }

        public static void Recompute()
        {
            try { Current = Compute(AppConfig.Current); } catch (Exception ex) { Logger.Warn("Güncelleme hesabı: " + ex.Message); }
            try { Changed?.Invoke(); } catch { }
        }

        public static List<UpdateInfo> Compute(AppConfig cfg)
        {
            var list = new List<UpdateInfo>();
            var cat = Catalog.Current;
            void Check(string comp, string cur, bool active)
            {
                if (string.IsNullOrEmpty(cur) || cur == "?" || cur == "latest") return;
                var newest = cat.AvailableFor(comp)
                    .Where(e => Catalog.SameBranch(comp, e.Version, cur) && Catalog.VersionKey(e.Version) > Catalog.VersionKey(cur))
                    .OrderByDescending(e => Catalog.VersionKey(e.Version)).FirstOrDefault();
                bool singleDir = comp == Comp.PhpMyAdmin || comp == Comp.Mailpit || comp == Comp.Adminer || comp == Comp.SqlSrv;
                if (newest == null || Installer.IsInstalled(newest) && !singleDir) return;
                if (list.Any(u => u.Component == comp && u.New == newest.Version)) return;
                list.Add(new UpdateInfo { Component = comp, Name = Comp.Title(comp), Current = cur, New = newest.Version, IsActive = active, Entry = newest });
            }

            foreach (var v in PhpManager.InstalledVersions()) Check(Comp.Php, v, v == cfg.PhpVersion);
            var ap = ApacheManager.ActiveVersion(); if (ap != null) Check(Comp.Apache, ap, cfg.WebServer != "nginx");
            var ng = NginxManager.ActiveVersion(); if (ng != null) Check(Comp.Nginx, ng, cfg.WebServer == "nginx");
            foreach (var engine in new[] { Comp.MariaDb, Comp.MySql })
                foreach (var v in DbManager.Installed(engine)) Check(engine, v, engine == cfg.DbEngine && v == cfg.DbVersion);
            foreach (var v in PostgreSqlManager.Installed()) Check(Comp.PostgreSql, v, v == cfg.PgVersion);
            foreach (var v in NodeManager.Installed()) Check(Comp.Node, v, v == cfg.NodeVersion);
            if (PhpMyAdminManager.IsInstalled) Check(Comp.PhpMyAdmin, PhpMyAdminManager.InstalledVersion(), true);
            if (AdminerManager.IsInstalled) Check(Comp.Adminer, AdminerManager.InstalledVersion(), true);
            if (SqlSrvManager.IsInstalled) Check(Comp.SqlSrv, SqlSrvManager.InstalledVersion(), true);
            if (MailpitManager.IsInstalled) Check(Comp.Mailpit, MailpitManager.InstalledVersion(), true);
            return list;
        }

        /// <summary>
        /// Güncellemeyi uygular: yeni sürümü kurar, ayarları (php.ini) taşır, aktif sürümü değiştirir,
        /// yapılandırmayı uygular ve başarılıysa eski yamayı kaldırır. Veritabanı verisi aynı dalda ortaktır.
        /// </summary>
        public static async Task<ApplyResult> ApplyAsync(UpdateInfo u, Action<string> log, IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            var res = new ApplyResult();
            var cfg = AppConfig.Current;
            log?.Invoke($"Güncelleniyor: {u.Text}");
            string oldIni = u.Component == Comp.Php ? PhpManager.ReadIni(u.Current) : null;

            await Installer.InstallAsync(u.Entry, cfg, progress, ct);

            switch (u.Component)
            {
                case Comp.Php:
                    if (!string.IsNullOrEmpty(oldIni))
                    {
                        log?.Invoke("php.ini ayarlarınız yeni sürüme taşınıyor...");
                        PhpManager.WriteIni(u.New, oldIni);
                        PhpManager.ApplyInfrastructure(u.New, cfg); // yollar (extension_dir vb.) yeni klasöre göre düzeltilir
                        PhpManager.SelfHeal(u.New, log);
                    }
                    if (cfg.PhpVersion == u.Current) cfg.PhpVersion = u.New;
                    break;
                case Comp.MariaDb:
                case Comp.MySql:
                    if (cfg.DbEngine == u.Component && cfg.DbVersion == u.Current) cfg.DbVersion = u.New;
                    break;
                case Comp.Node:
                    if (cfg.NodeVersion == u.Current) cfg.NodeVersion = u.New;
                    break;
                case Comp.PostgreSql:
                    // aynı ana sürüm: veri klasörü (data\postgresql-17) ortak, servis yeni pg_ctl ile yeniden kaydedilir
                    if (cfg.PgVersion == u.Current) cfg.PgVersion = u.New;
                    break;
            }
            cfg.Save();

            log?.Invoke("Yapılandırma uygulanıyor...");
            var r = await Task.Run(() => Stack.Apply(cfg, log, true), ct);
            res.Errors.AddRange(r.Errors);
            res.Warnings.AddRange(r.Warnings);

            if (r.Ok && u.Component == Comp.MariaDb && cfg.DbEngine == Comp.MariaDb && cfg.DbVersion == u.New)
            {
                var up = Path.Combine(DbManager.Dir(Comp.MariaDb, u.New), "bin", "mariadb-upgrade.exe");
                if (File.Exists(up))
                {
                    log?.Invoke("Veritabanı tabloları yeni sürüme uyarlanıyor (mariadb-upgrade)...");
                    var auth = $"-h 127.0.0.1 -P {cfg.DbPort} -u root" + (string.IsNullOrEmpty(cfg.DbRootPassword) ? "" : $" -p\"{cfg.DbRootPassword}\"");
                    var ur = ProcessRunner.Run(up, auth, null, 600000);
                    if (!ur.Ok) res.Warnings.Add("mariadb-upgrade uyarı verdi: " + ur.AllOutput.Trim());
                }
            }

            // eski yamayı kaldır (yalnız başarıyla geçildiyse; veri klasörleri dal başına olduğundan etkilenmez)
            if (r.Ok && new[] { Comp.Php, Comp.Apache, Comp.Nginx, Comp.MariaDb, Comp.MySql, Comp.PostgreSql, Comp.Node }.Contains(u.Component))
            {
                var old = Catalog.Current.Find(u.Component, u.Current) ?? Catalog.Custom(u.Component, u.Current)
                          ?? new CatalogEntry { Component = u.Component, Version = u.Current };
                if (!Installer.IsActive(old, cfg))
                {
                    var err = await Task.Run(() => Installer.Uninstall(old, cfg), ct);
                    if (err == null) log?.Invoke("Eski sürüm kaldırıldı: " + old.Title);
                    else res.Warnings.Add("Eski sürüm kaldırılamadı: " + err);
                }
            }
            Recompute();
            return res;
        }
    }
}
