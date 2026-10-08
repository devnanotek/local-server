using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DevNanotek.Core
{
    /// <summary>MariaDB / MySQL: veri dizini, my.ini, Windows servisi, yedekleme.</summary>
    public static class DbManager
    {
        public static string Root(string engine) => engine == Comp.MySql ? Paths.BinMySql : Paths.BinMariaDb;
        public static string Dir(string engine, string version) => Path.Combine(Root(engine), engine + "-" + version);
        public static string DataDir(string engine, string version) => Path.Combine(Paths.Data, engine + "-" + Catalog.MajorMinor(version));
        public static string EngineTitle(string engine) => engine == Comp.MySql ? "MySQL" : "MariaDB";

        public static List<string> Installed(string engine)
        {
            var list = new List<string>();
            try
            {
                var root = Root(engine);
                if (Directory.Exists(root))
                    foreach (var d in Directory.GetDirectories(root, engine + "-*"))
                        if (Mysqld(engine, Path.GetFileName(d).Substring(engine.Length + 1)) != null) list.Add(Path.GetFileName(d).Substring(engine.Length + 1));
            }
            catch { }
            return list.OrderByDescending(Catalog.VersionKey).ToList();
        }

        public static bool IsInstalled(string engine, string version) => !string.IsNullOrEmpty(version) && Mysqld(engine, version) != null;

        private static string FirstExisting(string dir, params string[] names)
        {
            foreach (var n in names) { var p = Path.Combine(dir, "bin", n); if (File.Exists(p)) return p; }
            return null;
        }

        public static string Mysqld(string engine, string v) => FirstExisting(Dir(engine, v), "mariadbd.exe", "mysqld.exe");
        public static string Client(string engine, string v) => FirstExisting(Dir(engine, v), "mariadb.exe", "mysql.exe");
        public static string Dump(string engine, string v) => FirstExisting(Dir(engine, v), "mariadb-dump.exe", "mysqldump.exe");
        public static string Admin(string engine, string v) => FirstExisting(Dir(engine, v), "mariadb-admin.exe", "mysqladmin.exe");
        public static string InstallDb(string engine, string v) => FirstExisting(Dir(engine, v), "mariadb-install-db.exe", "mysql_install_db.exe");

        public static bool IsDataInitialized(string engine, string version)
            => Directory.Exists(Path.Combine(DataDir(engine, version), "mysql"));

        // ------------------------------------------------------------------
        public static string CustomCnf => Path.Combine(Paths.EtcDb, "custom.cnf");

        public static bool WriteMyIni(AppConfig cfg)
        {
            var engine = cfg.DbEngine; var v = cfg.DbVersion;
            if (!IsInstalled(engine, v)) throw new Exception(EngineTitle(engine) + " kurulu değil.");
            Templates.EnsureFile(CustomCnf,
                "# DevNanotek — veritabanı için kendi eklemeleriniz ([mysqld] bölümüne eklenir). Bu dosya asla üzerine yazılmaz.\r\n" +
                "# Örnek:\r\n# sql_mode=\"\"\r\n# innodb_buffer_pool_size=1G\r\n# lower_case_table_names=2\r\n");

            var extra = new StringBuilder();
            if (engine == Comp.MySql)
            {
                extra.AppendLine("# MySQL'e özel");
                extra.AppendLine("skip-log-bin");
                extra.AppendLine("mysqlx=OFF");
                var mm = Catalog.MajorMinor(v);
                if (Catalog.VersionKey(mm) >= Catalog.VersionKey("8.4") && Catalog.VersionKey(mm) < Catalog.VersionKey("9.0"))
                {
                    extra.AppendLine("# eski istemciler için (PHP < 7.4, eski Navicat sürümleri)");
                    extra.AppendLine("mysql_native_password=ON");
                }
            }
            else
            {
                extra.AppendLine("# MariaDB'ye özel ayar gerekmiyor (varsayılanlar geliştirme için uygundur)");
            }

            string custom = "";
            try { custom = File.ReadAllText(CustomCnf, Encoding.UTF8); } catch { }
            custom = string.Join("\r\n", custom.Replace("\r\n", "\n").Split('\n').Where(l => !l.TrimStart().StartsWith("[")));

            var tokens = new Dictionary<string, string>
            {
                ["ENGINE_TITLE"] = EngineTitle(engine),
                ["VERSION"] = v,
                ["CUSTOM_CNF"] = CustomCnf,
                ["PORT"] = cfg.DbPort.ToString(),
                ["BASEDIR"] = Paths.Fwd(Dir(engine, v)),
                ["DATADIR"] = Paths.Fwd(DataDir(engine, v)),
                ["TMPDIR"] = Paths.Fwd(Path.Combine(Paths.Tmp, "db")),
                ["BIND"] = string.IsNullOrWhiteSpace(cfg.DbBindAddress) ? "127.0.0.1" : cfg.DbBindAddress.Trim(),
                ["COLLATION"] = engine == Comp.MySql ? "utf8mb4_0900_ai_ci" : "utf8mb4_unicode_ci",
                ["LOGS"] = Paths.Fwd(Paths.LogsDb),
                ["EXTRA"] = extra.ToString().TrimEnd(),
                ["CUSTOM"] = custom.Trim()
            };
            return Templates.WriteIfChanged(Paths.MyIni, Templates.Render("my.ini.tpl", tokens));
        }

        /// <summary>Veri dizini yoksa oluşturur (root şifresiz).</summary>
        public static void EnsureDataDir(AppConfig cfg, Action<string> log)
        {
            var engine = cfg.DbEngine; var v = cfg.DbVersion;
            var data = DataDir(engine, v);
            if (IsDataInitialized(engine, v)) return;
            Directory.CreateDirectory(Paths.Data);
            Directory.CreateDirectory(Path.Combine(Paths.Tmp, "db"));
            log?.Invoke($"{EngineTitle(engine)} {v} veri dizini oluşturuluyor: {data}");

            if (Directory.Exists(data) && Directory.EnumerateFileSystemEntries(data).Any())
            {
                // yarım kalmış bir dizin: kenara al
                Directory.Move(data, data + ".bozuk-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            }

            ProcessResult r;
            if (engine == Comp.MySql)
            {
                Directory.CreateDirectory(data);
                r = ProcessRunner.Run(Mysqld(engine, v), $"--defaults-file=\"{Paths.MyIni}\" --initialize-insecure --console", Path.Combine(Dir(engine, v), "bin"), 600000);
            }
            else
            {
                var inst = InstallDb(engine, v);
                if (inst == null) throw new Exception("mysql_install_db.exe bulunamadı.");
                r = ProcessRunner.Run(inst, $"--datadir=\"{data}\" --port={cfg.DbPort}", Path.Combine(Dir(engine, v), "bin"), 600000);
                // kurulum aracı datadir içine my.ini yazar; biz kendi my.ini'mizi kullanıyoruz
                try { var stray = Path.Combine(data, "my.ini"); if (File.Exists(stray)) File.Move(stray, Path.Combine(data, "my.ini.install-db")); } catch { }
            }
            if (!IsDataInitialized(engine, v))
                throw new Exception($"{EngineTitle(engine)} veri dizini oluşturulamadı:\r\n{r.AllOutput}");
            Logger.Info($"Veri dizini oluşturuldu: {data}");
            log?.Invoke("Veri dizini hazır. root kullanıcısı şifresizdir.");
        }

        // ------------------------------------------------------------------
        public static bool InstallService(AppConfig cfg, Action<string> log)
        {
            var engine = cfg.DbEngine; var v = cfg.DbVersion;
            var exe = Mysqld(engine, v);
            if (exe == null) throw new Exception(EngineTitle(engine) + " kurulu değil.");
            bool reinstalled = false;
            bool exists = WindowsServices.Exists(WindowsServices.Db);
            var image = WindowsServices.GetImagePath(WindowsServices.Db) ?? "";
            bool same = image.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0 && image.IndexOf(Paths.MyIni, StringComparison.OrdinalIgnoreCase) >= 0;
            if (exists && !same)
            {
                log?.Invoke("Veritabanı servisi farklı sürüme işaret ediyor, yeniden kuruluyor...");
                UninstallService();
                exists = false;
            }
            if (!exists)
            {
                log?.Invoke($"{EngineTitle(engine)} servisi kuruluyor...");
                var r = ProcessRunner.Run(exe, $"--install {WindowsServices.Db} --defaults-file=\"{Paths.MyIni}\"", Path.Combine(Dir(engine, v), "bin"), 60000);
                if (!WindowsServices.Exists(WindowsServices.Db))
                    throw new Exception("Veritabanı servisi kurulamadı:\r\n" + r.AllOutput);
                reinstalled = true;
                Logger.Info("DB servisi kuruldu: " + r.AllOutput.Trim());
            }
            WindowsServices.SetDescription(WindowsServices.Db, $"DevNanotek {EngineTitle(engine)} {v} — veri: {DataDir(engine, v)}");
            WindowsServices.SetAutoRecovery(WindowsServices.Db);
            WindowsServices.SetStartType(WindowsServices.Db, cfg.AutoStartServices ? "auto" : "demand");
            // Bilgisayar kapanırken veritabanının temiz kapanması için ön-kapatma süresi (60 sn)
            WindowsServices.SetPreshutdownTimeout(WindowsServices.Db, 60000);
            return reinstalled;
        }

        public static void UninstallService()
        {
            if (!WindowsServices.Exists(WindowsServices.Db)) return;
            WindowsServices.Stop(WindowsServices.Db, 180);
            var image = WindowsServices.GetImagePath(WindowsServices.Db) ?? "";
            var exe = ApacheManager.ExtractExe(image);
            if (exe != null && File.Exists(exe))
                ProcessRunner.Run(exe, $"--remove {WindowsServices.Db}", Path.GetDirectoryName(exe), 60000);
            if (WindowsServices.Exists(WindowsServices.Db)) WindowsServices.Delete(WindowsServices.Db);
            Logger.Info("DB servisi kaldırıldı");
        }

        // ------------------------------------------------------------------
        //  SQL / şifre / yedek
        // ------------------------------------------------------------------
        private static string Auth(AppConfig cfg) => $"-h 127.0.0.1 -P {cfg.DbPort} -u root" + (string.IsNullOrEmpty(cfg.DbRootPassword) ? "" : $" -p\"{cfg.DbRootPassword}\"");

        public static ProcessResult Sql(AppConfig cfg, string sql, string database = null, int timeoutMs = 60000)
        {
            var client = Client(cfg.DbEngine, cfg.DbVersion);
            if (client == null) return new ProcessResult { ExitCode = -1, StdErr = "İstemci (mysql.exe) bulunamadı" };
            var args = Auth(cfg) + " --default-character-set=utf8mb4 -N -B " + (database != null ? $"\"{database}\" " : "") + $"-e \"{sql.Replace("\"", "\\\"")}\"";
            return ProcessRunner.Run(client, args, null, timeoutMs);
        }

        public static bool Ping(AppConfig cfg)
        {
            var admin = Admin(cfg.DbEngine, cfg.DbVersion);
            if (admin == null) return PortUtil.IsListening(cfg.DbPort);
            return ProcessRunner.Run(admin, Auth(cfg) + " ping", null, 8000).Ok;
        }

        public static string SetRootPassword(AppConfig cfg, string newPassword)
        {
            var hosts = Sql(cfg, "SELECT host FROM mysql.user WHERE user='root'");
            if (!hosts.Ok) return "Bağlanılamadı: " + hosts.AllOutput;
            var list = hosts.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(h => h.Trim()).Distinct().ToList();
            if (list.Count == 0) list.Add("localhost");
            var sb = new StringBuilder();
            foreach (var h in list) sb.Append($"ALTER USER 'root'@'{h}' IDENTIFIED BY '{newPassword.Replace("'", "''")}'; ");
            sb.Append("FLUSH PRIVILEGES;");
            var r = Sql(cfg, sb.ToString());
            if (!r.Ok) return "Şifre değiştirilemedi: " + r.AllOutput;
            cfg.DbRootPassword = newPassword;
            cfg.Save();
            try { PhpMyAdminManager.WriteConfig(cfg); } catch { }
            Logger.Info("root şifresi güncellendi");
            return null;
        }

        public static string Backup(AppConfig cfg, Action<string> log)
        {
            var dump = Dump(cfg.DbEngine, cfg.DbVersion);
            if (dump == null) throw new Exception("mysqldump bulunamadı.");
            Directory.CreateDirectory(Paths.Backups);
            var file = Path.Combine(Paths.Backups, $"{cfg.DbEngine}-{cfg.DbVersion}-{DateTime.Now:yyyyMMdd-HHmmss}.sql");
            log?.Invoke("Yedek alınıyor: " + file);
            var args = Auth(cfg) + " --all-databases --routines --events --triggers --single-transaction --skip-lock-tables --default-character-set=utf8mb4 --result-file=\"" + file + "\"";
            var r = ProcessRunner.Run(dump, args, null, 3600000);
            if (!r.Ok) throw new Exception("Yedek alınamadı: " + r.AllOutput);
            return file;
        }

        public static void Restore(AppConfig cfg, string file, Action<string> log)
        {
            var client = Client(cfg.DbEngine, cfg.DbVersion);
            if (client == null) throw new Exception("mysql istemcisi bulunamadı.");
            log?.Invoke("Geri yükleniyor: " + file);
            var r = ProcessRunner.Cmd($"\"{client}\" {Auth(cfg)} --default-character-set=utf8mb4 < \"{file}\"", null, 3600000);
            if (!r.Ok) throw new Exception("Geri yükleme hatası: " + r.AllOutput);
        }

        public static List<string> Databases(AppConfig cfg)
        {
            var r = Sql(cfg, "SHOW DATABASES");
            if (!r.Ok) return new List<string>();
            return r.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
        }
    }
}
