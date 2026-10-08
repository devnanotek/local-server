using System;
using System.Collections.Generic;
using System.Linq;

namespace DevNanotek.Core
{
    /// <summary>Durum seviyesi: yeşil / sarı / kırmızı / gri.</summary>
    public enum Level { Ok, Warn, Error, Off }

    /// <summary>Bir servis satırı (tepsi penceresi, sol menü ve Ana Sayfa aynı veriyi kullanır).</summary>
    public class ServiceRow
    {
        public string Key { get; set; }          // web | fcgi | db | pg | mail
        public string Title { get; set; }        // "Apache"
        public string Version { get; set; }      // "2.4.69"
        public string State { get; set; }        // "Çalışıyor", "Durduruldu", "Hata ile durdu (kod 1067)"
        public Level Level { get; set; }
        public string Service { get; set; }      // Windows servis adı
        public bool Installed { get; set; }      // bileşen kurulu mu
        public bool ServiceExists { get; set; }
        public bool Running => Level == Level.Ok;
        public string TitleWithVersion => string.IsNullOrEmpty(Version) ? Title : Title + " " + Version;
    }

    public class VersionItem
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Text => Name + " " + (string.IsNullOrEmpty(Version) ? "—" : Version);
    }

    public class StatusReport
    {
        public List<ServiceRow> Rows { get; } = new List<ServiceRow>();
        public List<VersionItem> Versions { get; } = new List<VersionItem>();
        public Level Overall { get; set; } = Level.Off;
        public bool AlwaysOn { get; set; }

        public string OverallText
        {
            get
            {
                switch (Overall)
                {
                    case Level.Ok: return "Tüm servisler çalışıyor";
                    case Level.Warn: return Rows.Any(r => r.Level == Level.Ok) ? "Bazı servisler durdu" : "Servisler durdu";
                    case Level.Error: return "Bir serviste hata var";
                    default: return "Kurulum bekleniyor";
                }
            }
        }

        public ServiceRow Row(string key) => Rows.FirstOrDefault(r => r.Key == key);

        /// <summary>Kısa sürüm özeti: "PHP 8.3.35 · Apache 2.4.69 · MariaDB 11.4.13"</summary>
        public string VersionsLine => string.Join("  ·  ", Versions.Where(v => !string.IsNullOrEmpty(v.Version)).Select(v => v.Text));

        // ------------------------------------------------------------------
        public static StatusReport Build(AppConfig cfg)
        {
            var r = new StatusReport { AlwaysOn = cfg.AutoStartServices };

            // ---- Web sunucu ----
            if (cfg.WebServer == "nginx")
            {
                var v = NginxManager.ActiveVersion();
                r.Rows.Add(MakeRow("web", "Nginx", v, WindowsServices.Nginx, v != null));
                r.Rows.Add(MakeRow("fcgi", "PHP FastCGI", cfg.PhpVersion, WindowsServices.PhpFcgi, PhpManager.IsInstalled(cfg.PhpVersion)));
            }
            else
            {
                var v = ApacheManager.ActiveVersion();
                r.Rows.Add(MakeRow("web", "Apache", v, WindowsServices.Apache, v != null));
            }

            // ---- Veritabanı ----
            var dbOk = DbManager.IsInstalled(cfg.DbEngine, cfg.DbVersion);
            r.Rows.Add(MakeRow("db", DbManager.EngineTitle(cfg.DbEngine), dbOk ? cfg.DbVersion : null, WindowsServices.Db, dbOk));

            // ---- PostgreSQL (yalnız kuruluysa satır olarak görünür) ----
            if (PostgreSqlManager.IsActive(cfg))
                r.Rows.Add(MakeRow("pg", "PostgreSQL", cfg.PgVersion, WindowsServices.PostgreSql, true));

            // ---- Mailpit ----
            if (cfg.MailpitEnabled)
            {
                var mv = MailpitManager.InstalledVersion();
                var row = MakeRow("mail", "Mailpit", mv, WindowsServices.Mailpit, MailpitManager.IsInstalled);
                if (!MailpitManager.IsInstalled) { row.Level = Level.Off; row.State = "Kurulu değil"; }
                r.Rows.Add(row);
            }

            // ---- Sürümler ----
            r.Versions.Add(new VersionItem { Name = "PHP", Version = PhpManager.IsInstalled(cfg.PhpVersion) ? cfg.PhpVersion : null });
            var web = r.Row("web");
            r.Versions.Add(new VersionItem { Name = web.Title, Version = web.Version });
            r.Versions.Add(new VersionItem { Name = DbManager.EngineTitle(cfg.DbEngine), Version = dbOk ? cfg.DbVersion : null });
            r.Versions.Add(new VersionItem { Name = "Node.js", Version = NodeManager.IsInstalled(cfg.NodeVersion) ? cfg.NodeVersion : null });
            if (PostgreSqlManager.IsActive(cfg)) r.Versions.Add(new VersionItem { Name = "PostgreSQL", Version = cfg.PgVersion });
            r.Versions.Add(new VersionItem { Name = "phpMyAdmin", Version = PhpMyAdminManager.IsInstalled ? PhpMyAdminManager.InstalledVersion() : null });
            if (AdminerManager.IsInstalled) r.Versions.Add(new VersionItem { Name = "Adminer", Version = AdminerManager.InstalledVersion() });
            if (cfg.MailpitEnabled) r.Versions.Add(new VersionItem { Name = "Mailpit", Version = MailpitManager.InstalledVersion() });

            // ---- Genel durum ----
            var active = r.Rows.Where(x => x.Level != Level.Off).ToList();
            if (active.Count == 0) r.Overall = Level.Off;
            else if (active.Any(x => x.Level == Level.Error)) r.Overall = Level.Error;
            else if (active.Any(x => x.Level == Level.Warn)) r.Overall = Level.Warn;
            else r.Overall = Level.Ok;
            return r;
        }

        private static ServiceRow MakeRow(string key, string title, string version, string service, bool installed)
        {
            var row = new ServiceRow { Key = key, Title = title, Version = version, Service = service, Installed = installed };
            if (!installed)
            {
                row.Level = Level.Error;
                row.State = "Kurulu değil";
                return row;
            }
            var st = WindowsServices.QueryStatus(service);
            row.ServiceExists = st != null;
            if (st == null)
            {
                row.Level = Level.Warn;
                row.State = "Servis kurulmadı";
                return row;
            }
            switch (st.State)
            {
                case 4: // RUNNING
                    row.Level = Level.Ok; row.State = "Çalışıyor"; break;
                case 2: // START_PENDING
                    row.Level = Level.Warn; row.State = "Başlatılıyor…"; break;
                case 3: // STOP_PENDING
                    row.Level = Level.Warn; row.State = "Durduruluyor…"; break;
                case 1: // STOPPED
                    if (st.HasError) { row.Level = Level.Error; row.State = "Hata ile durdu (kod " + st.ErrorCode + ")"; }
                    else { row.Level = Level.Warn; row.State = "Durduruldu"; }
                    break;
                default:
                    row.Level = Level.Warn; row.State = "Duraklatıldı"; break;
            }
            return row;
        }
    }
}
