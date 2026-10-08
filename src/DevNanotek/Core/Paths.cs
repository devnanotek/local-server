using System;
using System.IO;
using System.Reflection;

namespace DevNanotek.Core
{
    /// <summary>
    /// Tüm klasör yapısı tek yerden yönetilir.
    /// Varsayılan kök: C:\devnanotek  (exe yanında "devnanotek.root" dosyası ile değiştirilebilir)
    /// </summary>
    public static class Paths
    {
        private static string _root;

        public static string Root
        {
            get
            {
                if (_root != null) return _root;
                var def = @"C:\devnanotek";
                try
                {
                    // 1) DEVNANOTEK_HOME ortam değişkeni (taşınabilir/test kurulumları için)
                    var env = Environment.GetEnvironmentVariable("DEVNANOTEK_HOME");
                    if (!string.IsNullOrWhiteSpace(env)) def = env.Trim().TrimEnd('\\', '/');
                    else
                    {
                        // 2) exe yanındaki devnanotek.root dosyası
                        var loc = Assembly.GetExecutingAssembly().Location;
                        if (!string.IsNullOrEmpty(loc))
                        {
                            var overrideFile = Path.Combine(Path.GetDirectoryName(loc) ?? "", "devnanotek.root");
                            if (File.Exists(overrideFile))
                            {
                                var v = File.ReadAllText(overrideFile).Trim();
                                if (!string.IsNullOrWhiteSpace(v)) def = v.TrimEnd('\\', '/');
                            }
                        }
                    }
                }
                catch { }
                _root = def;
                return _root;
            }
        }

        /// <summary>DevNanotek.exe'nin tam yolu. (Bellekten yüklenmişse boş döner — başka bir exe'yi asla göstermez.)</summary>
        public static string ExePath => Assembly.GetExecutingAssembly().Location ?? "";

        /// <summary>Program gerçek bir DevNanotek exe dosyasından mı çalışıyor?</summary>
        public static bool IsRealExe =>
            !string.IsNullOrEmpty(ExePath) && File.Exists(ExePath)
            && Path.GetFileName(ExePath).StartsWith("DevNanotek", StringComparison.OrdinalIgnoreCase)
            && ExePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        public static string ExeDir => string.IsNullOrEmpty(ExePath) ? Root : (Path.GetDirectoryName(ExePath) ?? Root);

        /// <summary>Programın kalıcı kurulum yeri (kısayollar ve zamanlanmış görev buraya işaret eder).</summary>
        public static string AppDir => Path.Combine(Root, "app");
        public static string AppExe => Path.Combine(AppDir, "DevNanotek.exe");
        /// <summary>Nginx modunda php-cgi havuzunu çalıştıran yardımcı kopya (ana exe güncellenirken kilitlenmesin diye ayrı).</summary>
        public static string FcgiHelperExe => Path.Combine(Bin, "fcgi", "DevNanotek-fcgi.exe");
        public static string Docs => Path.Combine(Root, "docs");
        public static string CaBundle => Path.Combine(EtcSsl, "ca-bundle.pem");

        public static string HttpDocs => Path.Combine(Root, "httpdocs");
        public static string Bin => Path.Combine(Root, "bin");
        public static string Data => Path.Combine(Root, "data");
        public static string Etc => Path.Combine(Root, "etc");
        public static string Logs => Path.Combine(Root, "logs");
        public static string Tmp => Path.Combine(Root, "tmp");
        public static string Downloads => Path.Combine(Root, "downloads");
        public static string Backups => Path.Combine(Root, "backups");
        public static string ConfigFile => Path.Combine(Root, "config.json");
        public static string AppLog => Path.Combine(Logs, "devnanotek.log");

        // bin alt klasörleri
        public static string BinPhp => Path.Combine(Bin, "php");
        public static string BinApache => Path.Combine(Bin, "apache");
        public static string BinNginx => Path.Combine(Bin, "nginx");
        public static string BinMariaDb => Path.Combine(Bin, "mariadb");
        public static string BinMySql => Path.Combine(Bin, "mysql");
        public static string BinPostgreSql => Path.Combine(Bin, "postgresql");
        public static string BinNode => Path.Combine(Bin, "nodejs");
        public static string BinPhpMyAdmin => Path.Combine(Bin, "phpmyadmin");
        public static string BinAdminer => Path.Combine(Bin, "adminer");
        public static string BinSqlSrv => Path.Combine(Bin, "sqlsrv");
        public static string BinMailpit => Path.Combine(Bin, "mailpit");
        public static string BinMkcert => Path.Combine(Bin, "mkcert");
        public static string BinWinSw => Path.Combine(Bin, "winsw");
        public static string BinComposer => Path.Combine(Bin, "composer");
        public static string BinCurrent => Path.Combine(Bin, "current");   // junction'lar: current\php, current\node ...
        public static string BinShims => Path.Combine(Bin, "shims");       // .cmd sarmalayıcılar

        // etc
        public static string EtcApache => Path.Combine(Etc, "apache");
        public static string EtcApacheVhosts => Path.Combine(EtcApache, "vhosts");
        public static string EtcNginx => Path.Combine(Etc, "nginx");
        public static string EtcNginxVhosts => Path.Combine(EtcNginx, "vhosts");
        public static string EtcDb => Path.Combine(Etc, "db");
        public static string EtcPostgreSql => Path.Combine(Etc, "postgresql");
        public static string EtcSsl => Path.Combine(Etc, "ssl");
        public static string EtcWinSw => Path.Combine(Etc, "winsw");
        public static string EtcMailpit => Path.Combine(Etc, "mailpit");

        public static string ApacheConf => Path.Combine(EtcApache, "httpd.conf");
        public static string ApacheCustomConf => Path.Combine(EtcApache, "custom.conf");
        public static string NginxConf => Path.Combine(EtcNginx, "nginx.conf");
        public static string NginxCustomConf => Path.Combine(EtcNginx, "custom.conf");
        public static string MyIni => Path.Combine(EtcDb, "my.ini");
        public static string CaCertPem => Path.Combine(EtcSsl, "cacert.pem");
        public static string SslCert => Path.Combine(EtcSsl, "localhost.pem");
        public static string SslKey => Path.Combine(EtcSsl, "localhost-key.pem");

        // logs
        public static string LogsApache => Path.Combine(Logs, "apache");
        public static string LogsNginx => Path.Combine(Logs, "nginx");
        public static string LogsDb => Path.Combine(Logs, "db");
        public static string LogsPostgreSql => Path.Combine(Logs, "postgresql");
        public static string LogsPhp => Path.Combine(Logs, "php");
        public static string LogsMailpit => Path.Combine(Logs, "mailpit");
        public static string LogsWinSw => Path.Combine(Logs, "winsw");

        public static string HostsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

        /// <summary>Tüm klasörleri oluşturur (varsa dokunmaz).</summary>
        public static void EnsureLayout()
        {
            foreach (var d in new[]
            {
                Root, HttpDocs, Bin, Data, Etc, Logs, Tmp, Downloads, Backups, Docs, Path.Combine(Bin, "fcgi"),
                BinPhp, BinApache, BinNginx, BinMariaDb, BinMySql, BinNode, BinMailpit, BinMkcert, BinWinSw, BinComposer, BinCurrent, BinShims,
                EtcApache, EtcApacheVhosts, EtcNginx, EtcNginxVhosts, EtcDb, EtcSsl, EtcWinSw, EtcMailpit,
                LogsApache, LogsNginx, LogsDb, LogsPhp, LogsMailpit, LogsWinSw,
                // PostgreSQL / Adminer / SQL Server klasörleri yalnız kurulunca oluşturulur
                Path.Combine(Tmp, "php-sessions"), Path.Combine(Tmp, "php-upload"), Path.Combine(Tmp, "phpmyadmin"), Path.Combine(Tmp, "db")
            })
            {
                try { Directory.CreateDirectory(d); } catch { }
            }
        }

        /// <summary>Apache / Nginx / PHP config dosyaları için ileri eğik çizgili yol.</summary>
        public static string Fwd(string p) => (p ?? "").Replace('\\', '/');
    }
}
