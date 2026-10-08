using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DevNanotek.Core
{
    /// <summary>Elle tanımlanan sanal host: proje.test -> C:\devnanotek\httpdocs\GITHUB\proje</summary>
    public class VhostEntry
    {
        public string Host { get; set; }
        public string DocRoot { get; set; }
    }

    /// <summary>
    /// Uygulama yapılandırması: C:\devnanotek\config.json
    /// </summary>
    public class AppConfig
    {
        // ---- Aktif bileşenler ----
        public string WebServer { get; set; } = "apache";     // apache | nginx
        public string PhpVersion { get; set; } = "";          // ör. 8.3.35
        public string DbEngine { get; set; } = "mariadb";     // mariadb | mysql
        public string DbVersion { get; set; } = "";           // ör. 11.4.13
        public string NodeVersion { get; set; } = "";         // ör. 22.23.3
        public string PgVersion { get; set; } = "";           // PostgreSQL (isteğe bağlı, MariaDB/MySQL ile birlikte çalışır) ör. 17.11

        // ---- Portlar ----
        public int HttpPort { get; set; } = 80;
        public int HttpsPort { get; set; } = 443;
        public int DbPort { get; set; } = 3306;
        public int PgPort { get; set; } = 5432;
        public int SmtpPort { get; set; } = 1025;
        public int MailpitUiPort { get; set; } = 8025;
        public int FcgiPort { get; set; } = 9000;             // Nginx + php-cgi başlangıç portu
        public int FcgiChildren { get; set; } = 4;            // php-cgi süreç sayısı

        // ---- Davranış ----
        public bool SslEnabled { get; set; } = true;
        public bool MailpitEnabled { get; set; } = true;
        public bool AutoStartServices { get; set; } = true;   // Windows açılışında servisler otomatik başlasın
        public bool AppStartWithWindows { get; set; } = true; // Program da Windows ile başlasın (tepside) — "her zaman açık" modunun parçası
        public bool StopServicesOnExit { get; set; } = false; // Program kapanınca servisleri durdur
        public bool MinimizeToTray { get; set; } = true;      // Kapat düğmesi tepsiye küçültsün
        public bool AddToPath { get; set; } = true;           // php/node/composer'ı kullanıcı PATH'ine ekle
        public bool AutoVirtualHosts { get; set; } = false;   // httpdocs\proje -> proje.test
        public string VhostTld { get; set; } = "test";
        public bool StartServicesOnLaunch { get; set; } = true; // Program açılınca durmuş servisleri başlat
        public bool TrayPromoted { get; set; } = false;       // Windows 11: tepsi simgesi bir kez görev çubuğunda görünür yapıldı mı
        public bool LanAccess { get; set; } = false;          // web sunucu yerel ağdan erişilebilsin mi (varsayılan: yalnız bu bilgisayar)
        public bool DefenderExcluded { get; set; } = false;   // kullanıcı Defender istisnası ekledi mi (kaldırırken geri alınır)

        // ---- Veritabanı ----
        public string DbBindAddress { get; set; } = "127.0.0.1"; // 0.0.0.0 => ağdan erişim
        public string DbRootPassword { get; set; } = "";

        // ---- Diğer ----
        public string DocumentRoot { get; set; } = "";        // boş => C:\devnanotek\httpdocs
        public bool SetupCompleted { get; set; } = false;
        public string Theme { get; set; } = "system";         // system | light | dark
        public DateTime? CatalogUpdatedAt { get; set; }
        public List<string> PinnedProjects { get; set; } = new List<string>();
        public List<VhostEntry> Vhosts { get; set; } = new List<VhostEntry>();   // elle eklenen sanal hostlar

        [System.Web.Script.Serialization.ScriptIgnore]
        public string EffectiveDocRoot => string.IsNullOrWhiteSpace(DocumentRoot) ? Paths.HttpDocs : DocumentRoot.TrimEnd('\\', '/');

        // ------------------------------------------------------------------
        private static AppConfig _current;
        public static AppConfig Current
        {
            get
            {
                if (_current == null) _current = Load();
                return _current;
            }
        }

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(Paths.ConfigFile))
                {
                    var json = File.ReadAllText(Paths.ConfigFile, Encoding.UTF8);
                    var cfg = JsonUtil.Deserialize<AppConfig>(json);
                    if (cfg != null) return cfg.Sanitize();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("config.json okunamadı, varsayılanlar kullanılıyor", ex);
                try { File.Copy(Paths.ConfigFile, Paths.ConfigFile + ".bozuk-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch { }
            }
            return new AppConfig();
        }

        public AppConfig Sanitize()
        {
            if (WebServer != "nginx") WebServer = "apache";
            if (DbEngine != "mysql") DbEngine = "mariadb";
            if (HttpPort <= 0 || HttpPort > 65535) HttpPort = 80;
            if (HttpsPort <= 0 || HttpsPort > 65535) HttpsPort = 443;
            if (DbPort <= 0 || DbPort > 65535) DbPort = 3306;
            if (PgPort <= 0 || PgPort > 65535) PgPort = 5432;
            if (PgVersion == null) PgVersion = "";
            if (SmtpPort <= 0) SmtpPort = 1025;
            if (MailpitUiPort <= 0) MailpitUiPort = 8025;
            if (FcgiPort <= 0) FcgiPort = 9000;
            if (FcgiChildren < 1) FcgiChildren = 1;
            if (FcgiChildren > 16) FcgiChildren = 16;
            if (string.IsNullOrWhiteSpace(VhostTld)) VhostTld = "test";
            VhostTld = VhostTld.Trim().TrimStart('.').ToLowerInvariant();
            if (PinnedProjects == null) PinnedProjects = new List<string>();
            if (Theme != "light" && Theme != "dark") Theme = "system";
            if (Vhosts == null) Vhosts = new List<VhostEntry>();
            Vhosts.RemoveAll(v => v == null || string.IsNullOrWhiteSpace(v.Host) || string.IsNullOrWhiteSpace(v.DocRoot));
            return this;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Paths.ConfigFile));
                var json = JsonUtil.SerializePretty(this);
                var tmp = Paths.ConfigFile + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(Paths.ConfigFile)) File.Replace(tmp, Paths.ConfigFile, Paths.ConfigFile + ".bak");
                else File.Move(tmp, Paths.ConfigFile);
            }
            catch (Exception ex)
            {
                Logger.Error("config.json kaydedilemedi", ex);
            }
        }

        public AppConfig Clone() => JsonUtil.Deserialize<AppConfig>(JsonUtil.Serialize(this)).Sanitize();

        /// <summary>Fabrika ayarları: bellekteki ve diskteki yapılandırmayı varsayılana döndürür (kurulum sihirbazı yeniden açılır).</summary>
        public static void ResetToDefaults()
        {
            _current = new AppConfig();
            _current.Save();
        }

        /// <summary>
        /// Çalışma modu.
        /// true  = Her zaman açık: servisler bilgisayar açılınca başlar, program tepside Windows ile açılır.
        /// false = Sadece ben açınca: servisler elle başlar; program açılınca başlatılır, programdan Çıkış'ta durdurulur.
        /// </summary>
        public void SetRunMode(bool alwaysOn)
        {
            AutoStartServices = alwaysOn;
            AppStartWithWindows = alwaysOn;
            StartServicesOnLaunch = true;
            StopServicesOnExit = !alwaysOn;
        }

        [System.Web.Script.Serialization.ScriptIgnore]
        public bool AlwaysOn => AutoStartServices;
    }
}
