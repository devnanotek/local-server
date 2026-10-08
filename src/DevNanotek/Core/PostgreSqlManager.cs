using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DevNanotek.Core
{
    /// <summary>
    /// PostgreSQL: veri dizini (initdb), yapılandırma, Windows servisi (pg_ctl register) ve yedekleme.
    /// MariaDB/MySQL'in yerine değil yanında çalışır (ayrı servis, ayrı port).
    /// Yerel geliştirme için "trust" kimlik doğrulaması: postgres kullanıcısı şifresiz, yalnız bu bilgisayardan.
    /// </summary>
    public static class PostgreSqlManager
    {
        public const string SuperUser = "postgres";
        // PostgreSQL yapılandırma dosyaları ASCII tutulur (sunucu kodlamasından bağımsız okunsun)
        private const string Marker = "# --- DEVNANOTEK (bu satirlari silmeyin) ---";

        public static string Dir(string version) => Path.Combine(Paths.BinPostgreSql, "postgresql-" + version);
        public static string BinDir(string version) => Path.Combine(Dir(version), "bin");
        public static string Tool(string version, string name) => Path.Combine(BinDir(version), name + ".exe");
        public static string Major(string version) => (version ?? "").Split('.')[0];
        /// <summary>Veri klasörü ana sürüm başınadır: yama güncellemelerinde (17.10 → 17.11) veriler aynen kalır.</summary>
        public static string DataDir(string version) => Path.Combine(Paths.Data, "postgresql-" + Major(version));
        public static string ConfFile => Path.Combine(Paths.EtcPostgreSql, "devnanotek.conf");
        public static string CustomConf => Path.Combine(Paths.EtcPostgreSql, "custom.conf");

        public static List<string> Installed()
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(Paths.BinPostgreSql))
                    foreach (var d in Directory.GetDirectories(Paths.BinPostgreSql, "postgresql-*"))
                        if (File.Exists(Path.Combine(d, "bin", "postgres.exe"))) list.Add(Path.GetFileName(d).Substring("postgresql-".Length));
            }
            catch { }
            return list.OrderByDescending(Catalog.VersionKey).ToList();
        }

        public static bool IsInstalled(string version) => !string.IsNullOrEmpty(version) && File.Exists(Tool(version, "postgres"));
        /// <summary>PostgreSQL bu yapılandırmada kullanılıyor mu (kurulu ve seçili).</summary>
        public static bool IsActive(AppConfig cfg) => IsInstalled(cfg.PgVersion);
        public static bool IsDataInitialized(string version) => File.Exists(Path.Combine(DataDir(version), "PG_VERSION"));

        // ------------------------------------------------------------------
        /// <summary>etc\postgresql\devnanotek.conf yazar. Dönüş: değişti mi (çalışan servis yeniden başlatılmalı).</summary>
        public static bool WriteConfig(AppConfig cfg)
        {
            Directory.CreateDirectory(Paths.EtcPostgreSql);
            Directory.CreateDirectory(Paths.LogsPostgreSql);
            Templates.EnsureFile(CustomConf,
                L.Pick("# DEVNANOTEK - PostgreSQL icin kendi ayarlariniz (postgresql.conf bicimi). Bu dosya asla uzerine yazilmaz.\r\n# Ornek:\r\n",
                       "# DEVNANOTEK - your own PostgreSQL settings (postgresql.conf format). This file is never overwritten.\r\n# Example:\r\n") +
                "# shared_buffers = 512MB\r\n# work_mem = 16MB\r\n# max_connections = 200\r\n");
            var sb = new StringBuilder();
            sb.AppendLine("# DEVNANOTEK - PostgreSQL ayarlari. BU DOSYA OTOMATIK URETILIR; kendi ayarlariniz icin custom.conf");
            sb.AppendLine($"port = {cfg.PgPort}");
            sb.AppendLine("listen_addresses = 'localhost'          # yalniz bu bilgisayar");
            sb.AppendLine("max_connections = 100");
            sb.AppendLine("logging_collector = on");
            sb.AppendLine($"log_directory = '{Paths.Fwd(Paths.LogsPostgreSql)}'");
            sb.AppendLine("log_filename = 'postgresql-%a.log'       # haftanin gunu: 7 dosya doner");
            sb.AppendLine("log_truncate_on_rotation = on");
            sb.AppendLine("log_rotation_age = 1d");
            sb.AppendLine("log_rotation_size = 0");
            sb.AppendLine("log_line_prefix = '%m [%p] %q%u@%d '");
            return Templates.WriteIfChanged(ConfFile, sb.ToString());
        }

        /// <summary>Veri klasörü yoksa initdb ile oluşturur (UTF8, postgres kullanıcısı, yerel bağlantılar şifresiz).</summary>
        public static void EnsureDataDir(AppConfig cfg, Action<string> log)
        {
            var v = cfg.PgVersion;
            var data = DataDir(v);
            if (!IsDataInitialized(v))
            {
                Directory.CreateDirectory(Paths.Data);
                if (Directory.Exists(data) && Directory.EnumerateFileSystemEntries(data).Any())
                    Directory.Move(data, data + ".bozuk-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                log?.Invoke($"PostgreSQL {Major(v)} veri klasörü oluşturuluyor: {data}");
                // initdb yönetici hesabıyla çalıştırılınca kendini kısıtlı haklarla yeniden başlatır (Windows'ta desteklenir)
                var r = ProcessRunner.Run(Tool(v, "initdb"), $"-D \"{data}\" -U {SuperUser} -E UTF8 --no-locale -A trust", BinDir(v), 600000);
                if (!IsDataInitialized(v))
                    throw new Exception("PostgreSQL veri klasörü oluşturulamadı:\r\n" + r.AllOutput);
                Logger.Info("PostgreSQL veri klasörü oluşturuldu: " + data);
                log?.Invoke("Veri klasörü hazır. Kullanıcı postgres, şifre gerekmez.");
            }
            EnsureIncludes(data);
        }

        /// <summary>postgresql.conf'un sonuna bizim ve kullanıcının dosyalarını dahil eden satırları ekler (kök değişirse düzeltir).</summary>
        private static void EnsureIncludes(string data)
        {
            var conf = Path.Combine(data, "postgresql.conf");
            if (!File.Exists(conf)) return;
            var text = File.ReadAllText(conf, Encoding.UTF8);
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            lines.RemoveAll(l => l.StartsWith(Marker) || (l.StartsWith("include_if_exists") && (l.Contains("/postgresql/devnanotek.conf'") || l.Contains("/postgresql/custom.conf'"))));
            while (lines.Count > 0 && lines[lines.Count - 1].Trim() == "") lines.RemoveAt(lines.Count - 1);
            lines.Add("");
            lines.Add(Marker);
            lines.Add($"include_if_exists = '{Paths.Fwd(ConfFile)}'");
            lines.Add($"include_if_exists = '{Paths.Fwd(CustomConf)}'");
            var updated = string.Join("\n", lines) + "\n";
            if (updated != text.Replace("\r\n", "\n")) File.WriteAllText(conf, updated, new UTF8Encoding(false));
        }

        /// <summary>Servis "NT AUTHORITY\NetworkService" ile çalışır: veri ve günlük klasörlerine yazabilmeli (SID: dil bağımsız).</summary>
        private static void GrantServiceAccess(params string[] dirs)
        {
            foreach (var d in dirs)
            {
                if (!Directory.Exists(d)) continue;
                var r = ProcessRunner.Run("icacls.exe", $"\"{d}\" /grant *S-1-5-20:(OI)(CI)F /T /Q", null, 120000);
                if (!r.Ok) Logger.Warn("İzin verilemedi (" + d + "): " + r.AllOutput);
            }
        }

        // ------------------------------------------------------------------
        public static bool InstallService(AppConfig cfg, Action<string> log)
        {
            var v = cfg.PgVersion;
            var pgctl = Tool(v, "pg_ctl");
            if (!File.Exists(pgctl)) throw new Exception("PostgreSQL kurulu değil.");
            var svc = WindowsServices.PostgreSql;
            bool reinstalled = false;
            bool exists = WindowsServices.Exists(svc);
            var image = WindowsServices.GetImagePath(svc) ?? "";
            bool same = image.IndexOf(pgctl, StringComparison.OrdinalIgnoreCase) >= 0 && image.IndexOf(DataDir(v), StringComparison.OrdinalIgnoreCase) >= 0;
            if (exists && !same)
            {
                log?.Invoke("PostgreSQL servisi farklı sürüme işaret ediyor, yeniden kuruluyor...");
                UninstallService();
                exists = false;
            }
            if (!exists)
            {
                // izinler yalnız servis kaydedilirken verilir (her "Uygula"da büyük veri klasörünü yeniden taramamak için)
                GrantServiceAccess(DataDir(v), Paths.LogsPostgreSql);
                log?.Invoke($"PostgreSQL {v} servisi kuruluyor...");
                var r = ProcessRunner.Run(pgctl, $"register -N \"{svc}\" -U \"NT AUTHORITY\\NetworkService\" -D \"{DataDir(v)}\" -S {(cfg.AutoStartServices ? "auto" : "demand")} -w -t 120", BinDir(v), 60000);
                if (!WindowsServices.Exists(svc)) throw new Exception("PostgreSQL servisi kurulamadı:\r\n" + r.AllOutput);
                reinstalled = true;
                Logger.Info("PostgreSQL servisi kuruldu");
            }
            WindowsServices.SetDescription(svc, $"DevNanotek PostgreSQL {v} — veri: {DataDir(v)}");
            WindowsServices.SetAutoRecovery(svc);
            WindowsServices.SetStartType(svc, cfg.AutoStartServices ? "auto" : "demand");
            WindowsServices.SetPreshutdownTimeout(svc, 60000);
            return reinstalled;
        }

        public static void UninstallService()
        {
            var svc = WindowsServices.PostgreSql;
            if (!WindowsServices.Exists(svc)) return;
            WindowsServices.Stop(svc, 120);
            var exe = ApacheManager.ExtractExe(WindowsServices.GetImagePath(svc) ?? "");
            if (exe != null && File.Exists(exe)) ProcessRunner.Run(exe, $"unregister -N \"{svc}\"", Path.GetDirectoryName(exe), 60000);
            if (WindowsServices.Exists(svc)) WindowsServices.Delete(svc);
            Logger.Info("PostgreSQL servisi kaldırıldı");
        }

        // ------------------------------------------------------------------
        //  SQL / yedek
        // ------------------------------------------------------------------
        private static string Conn(AppConfig cfg) => $"-h 127.0.0.1 -p {cfg.PgPort} -U {SuperUser}";
        private static Dictionary<string, string> Env => new Dictionary<string, string> { ["PGCLIENTENCODING"] = "UTF8" };

        public static ProcessResult Sql(AppConfig cfg, string sql, string database = "postgres", int timeoutMs = 60000)
        {
            var psql = Tool(cfg.PgVersion, "psql");
            if (!File.Exists(psql)) return new ProcessResult { ExitCode = -1, StdErr = "psql.exe bulunamadı" };
            return ProcessRunner.Run(psql, $"{Conn(cfg)} -d \"{database}\" -At -v ON_ERROR_STOP=1 -c \"{sql.Replace("\"", "\\\"")}\"", null, timeoutMs, Env);
        }

        public static bool Ping(AppConfig cfg)
        {
            var ready = Tool(cfg.PgVersion, "pg_isready");
            if (!File.Exists(ready)) return PortUtil.IsListening(cfg.PgPort);
            return ProcessRunner.Run(ready, $"-h 127.0.0.1 -p {cfg.PgPort} -t 5", null, 10000).Ok;
        }

        public static List<string> Databases(AppConfig cfg)
        {
            var r = Sql(cfg, "SELECT datname FROM pg_database WHERE NOT datistemplate ORDER BY datname");
            if (!r.Ok) return new List<string>();
            return r.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
        }

        public static string CreateDatabase(AppConfig cfg, string name)
        {
            if (!Regex.IsMatch(name ?? "", @"^[A-Za-z_][A-Za-z0-9_\-]{0,62}$")) return "Geçersiz ad. Harf, rakam, _ ve - kullanın.";
            var r = Sql(cfg, $"CREATE DATABASE \"{name}\" ENCODING 'UTF8' TEMPLATE template0");
            return r.Ok ? null : r.AllOutput;
        }

        public static string Backup(AppConfig cfg, Action<string> log)
        {
            var dump = Tool(cfg.PgVersion, "pg_dumpall");
            if (!File.Exists(dump)) throw new Exception("pg_dumpall.exe bulunamadı.");
            Directory.CreateDirectory(Paths.Backups);
            var file = Path.Combine(Paths.Backups, $"postgresql-{cfg.PgVersion}-{DateTime.Now:yyyyMMdd-HHmmss}.sql");
            log?.Invoke("PostgreSQL yedeği alınıyor: " + file);
            var r = ProcessRunner.Run(dump, $"{Conn(cfg)} --clean --if-exists -f \"{file}\"", null, 3600000, Env);
            if (!r.Ok) throw new Exception("Yedek alınamadı: " + r.AllOutput);
            return file;
        }

        /// <summary>
        /// PostgreSQL kuruluysa (gerekirse servisi başlatıp) tüm veritabanlarını yedekler.
        /// Dönüş: yedek dosyası; PostgreSQL yoksa null. Hata olursa uyarı metni <paramref name="warning"/> ile döner.
        /// </summary>
        public static string BackupIfPossible(AppConfig cfg, Action<string> log, out string warning)
        {
            warning = null;
            if (!IsActive(cfg)) return null;
            try
            {
                if (!WindowsServices.IsRunning(WindowsServices.PostgreSql) && WindowsServices.Exists(WindowsServices.PostgreSql))
                {
                    log?.Invoke("Yedek için PostgreSQL başlatılıyor...");
                    WindowsServices.Start(WindowsServices.PostgreSql, 90);
                }
                if (!WindowsServices.IsRunning(WindowsServices.PostgreSql) && !PortUtil.IsListening(cfg.PgPort))
                {
                    warning = "PostgreSQL çalışmadığı için SQL yedeği alınamadı (veri klasörü korunuyor).";
                    return null;
                }
                return Backup(cfg, log);
            }
            catch (Exception ex) { warning = "PostgreSQL yedeği alınamadı: " + ex.Message; return null; }
        }

        public static void Restore(AppConfig cfg, string file, Action<string> log)
        {
            var psql = Tool(cfg.PgVersion, "psql");
            if (!File.Exists(psql)) throw new Exception("psql.exe bulunamadı.");
            log?.Invoke("Geri yükleniyor: " + file);
            var r = ProcessRunner.Run(psql, $"{Conn(cfg)} -d postgres -f \"{file}\"", null, 3600000, Env);
            if (!r.Ok) throw new Exception("Geri yükleme hatası: " + r.AllOutput);
        }

        public static string LatestLog()
        {
            try
            {
                if (!Directory.Exists(Paths.LogsPostgreSql)) return null;
                return new DirectoryInfo(Paths.LogsPostgreSql).GetFiles("*.log").OrderByDescending(f => f.LastWriteTime).FirstOrDefault()?.FullName;
            }
            catch { return null; }
        }

        public static string HintText()
        {
            var f = LatestLog();
            return f == null ? "Günlükler: " + Paths.LogsPostgreSql : "Hata günlüğü: " + f + "\r\n" + Stack.TailFile(f, 8);
        }
    }
}
