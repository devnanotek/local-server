using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DevNanotek.Core
{
    public class ApplyResult
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool Ok => Errors.Count == 0;
    }

    public class StackStatus
    {
        public string WebTitle { get; set; } = "Web sunucu";
        public string WebService { get; set; }
        public string WebState { get; set; } = "-";
        public bool WebRunning { get; set; }
        public bool WebInstalled { get; set; }
        public string FcgiState { get; set; }

        public string DbTitle { get; set; } = "Veritabanı";
        public string DbState { get; set; } = "-";
        public bool DbRunning { get; set; }
        public bool DbInstalled { get; set; }

        public string MailState { get; set; } = "-";
        public bool MailRunning { get; set; }
        public bool MailInstalled { get; set; }

        public string PhpVersion { get; set; }
        public string NodeVersion { get; set; }
        /// <summary>Renkli durum satırları ve aktif sürümler (tepsi penceresi / sol menü / Ana Sayfa).</summary>
        public StatusReport Report { get; set; }
        public bool AllRunning => WebRunning && DbRunning && (!MailInstalled || MailRunning);
        public bool AnyRunning => WebRunning || DbRunning || MailRunning;
    }

    /// <summary>Tüm yığını yöneten orkestratör: yapılandırma üret, servisleri kur, başlat/durdur.</summary>
    public static class Stack
    {
        public static event Action StatusChanged;
        private static readonly object ApplyLock = new object();
        public static bool IsBusy { get; private set; }

        public static void NotifyChanged() { try { StatusChanged?.Invoke(); } catch { } }

        // ------------------------------------------------------------------
        public static StackStatus GetStatus(AppConfig cfg)
        {
            var s = new StackStatus { PhpVersion = cfg.PhpVersion, NodeVersion = cfg.NodeVersion };
            if (cfg.WebServer == "nginx")
            {
                var v = NginxManager.ActiveVersion();
                s.WebTitle = "Nginx" + (v != null ? " " + v : "");
                s.WebService = WindowsServices.Nginx;
                s.WebInstalled = v != null;
                s.WebRunning = WindowsServices.IsRunning(WindowsServices.Nginx);
                s.WebState = WindowsServices.StatusText(WindowsServices.Nginx);
                s.FcgiState = WindowsServices.StatusText(WindowsServices.PhpFcgi);
                if (s.WebRunning && !WindowsServices.IsRunning(WindowsServices.PhpFcgi)) s.WebState = "Çalışıyor (PHP FastCGI durdu!)";
            }
            else
            {
                var v = ApacheManager.ActiveVersion();
                s.WebTitle = "Apache" + (v != null ? " " + v : "");
                s.WebService = WindowsServices.Apache;
                s.WebInstalled = v != null;
                s.WebRunning = WindowsServices.IsRunning(WindowsServices.Apache);
                s.WebState = WindowsServices.StatusText(WindowsServices.Apache);
            }
            s.DbTitle = DbManager.EngineTitle(cfg.DbEngine) + (string.IsNullOrEmpty(cfg.DbVersion) ? "" : " " + cfg.DbVersion);
            s.DbInstalled = DbManager.IsInstalled(cfg.DbEngine, cfg.DbVersion);
            s.DbRunning = WindowsServices.IsRunning(WindowsServices.Db);
            s.DbState = WindowsServices.StatusText(WindowsServices.Db);
            s.MailInstalled = MailpitManager.IsInstalled && cfg.MailpitEnabled;
            s.MailRunning = WindowsServices.IsRunning(WindowsServices.Mailpit);
            s.MailState = WindowsServices.StatusText(WindowsServices.Mailpit);
            s.Report = StatusReport.Build(cfg);
            return s;
        }

        // ------------------------------------------------------------------
        public static Task<ApplyResult> ApplyAsync(AppConfig cfg, Action<string> log, bool startServices)
            => Task.Run(() => Apply(cfg, log, startServices));

        /// <summary>
        /// Yapılandırmayı diske yazar, servisleri kurar/günceller. startServices=true ise çalışmayanları da başlatır;
        /// değişiklik olan çalışan servisleri her durumda yeniden başlatır.
        /// configOnly=true: yalnızca dosyaları üretir (servis, hosts, PATH, zamanlanmış görev değişmez).
        /// </summary>
        public static ApplyResult Apply(AppConfig cfg, Action<string> log, bool startServices, bool configOnly = false)
        {
            var res = new ApplyResult();
            log = log ?? (_ => { });
            lock (ApplyLock)
            {
                IsBusy = true;
                try
                {
                    Paths.EnsureLayout();
                    cfg.Sanitize();
                    ResolveVersions(cfg, log, res);

                    // --- junction / shim / PATH ---
                    log("Yollar hazırlanıyor...");
                    EnvPath.SetJunction(EnvPath.CurrentPhp, PhpManager.IsInstalled(cfg.PhpVersion) ? PhpManager.Dir(cfg.PhpVersion) : null);
                    EnvPath.SetJunction(EnvPath.CurrentNode, NodeManager.IsInstalled(cfg.NodeVersion) ? NodeManager.Dir(cfg.NodeVersion) : null);
                    EnvPath.SetJunction(EnvPath.CurrentDb, DbManager.IsInstalled(cfg.DbEngine, cfg.DbVersion) ? DbManager.Dir(cfg.DbEngine, cfg.DbVersion) : null);
                    EnvPath.SetJunction(EnvPath.CurrentPg, PostgreSqlManager.IsActive(cfg) ? PostgreSqlManager.Dir(cfg.PgVersion) : null);
                    EnvPath.WriteShims(cfg);
                    if (!configOnly) EnvPath.ApplyUserPath(cfg.AddToPath);

                    // --- SSL ---
                    bool sslReady = false;
                    if (cfg.SslEnabled)
                    {
                        if (MkcertManager.IsInstalled)
                        {
                            try { sslReady = MkcertManager.EnsureCerts(cfg, log); }
                            catch (Exception ex) { res.Warnings.Add("SSL sertifikası üretilemedi: " + ex.Message); }
                            if (!sslReady) res.Warnings.Add("SSL sertifikası hazır değil; HTTPS kapalı kalacak.");
                        }
                        else res.Warnings.Add("mkcert kurulu değil; HTTPS kapalı. (Sürümler > Araçlar)");
                    }
                    else MkcertManager.WriteCaBundle();

                    // --- PHP (CA paketi hazır olduktan sonra) ---
                    if (PhpManager.IsInstalled(cfg.PhpVersion))
                    {
                        log($"PHP {cfg.PhpVersion} php.ini denetleniyor...");
                        PhpManager.ApplyInfrastructure(cfg.PhpVersion, cfg);
                        var off = PhpManager.SelfHeal(cfg.PhpVersion, log);
                        if (off.Count > 0) res.Warnings.Add($"PHP: yüklenemeyen eklentiler kapatıldı ({string.Join(", ", off)}). Ayarlar > PHP > Eklentiler'den tekrar açabilirsiniz." + (SystemCheck.SmartAppControlOn ? " Neden: Akıllı Uygulama Denetimi bu DLL'leri engelliyor." : ""));
                    }
                    else res.Warnings.Add("PHP kurulu değil — web sunucu PHP'siz çalışır.");

                    // --- hosts ---
                    if (!configOnly)
                    {
                        var hosts = Vhosts.HostNames(cfg).ToList();
                        if (!HostsFile.SetManagedHosts(hosts)) res.Warnings.Add("hosts dosyası güncellenemedi (antivirüs engelliyor olabilir).");
                    }

                    // --- phpMyAdmin / Adminer / SQL Server eklentisi ---
                    if (PhpMyAdminManager.IsInstalled) PhpMyAdminManager.WriteConfig(cfg);
                    if (AdminerManager.IsInstalled) AdminerManager.WriteWrapper();
                    if (SqlSrvManager.IsInstalled) SqlSrvManager.DeployTo(cfg.PhpVersion);

                    // --- açılış sayfası ---
                    EnsureLandingPage(cfg, false);

                    // --- Web sunucu ---
                    ApplyWeb(cfg, log, res, sslReady, startServices, configOnly);

                    // --- Veritabanı ---
                    ApplyDb(cfg, log, res, startServices, configOnly);

                    // --- PostgreSQL (isteğe bağlı, ayrı servis) ---
                    ApplyPg(cfg, log, res, startServices, configOnly);

                    // --- Mailpit ---
                    if (!configOnly) ApplyMail(cfg, log, res, startServices);

                    // --- Windows ile başlat + güvenlik duvarı (yalnız "yerel ağdan erişim" açıksa kural) ---
                    if (!configOnly)
                    {
                        try { Autostart.Apply(cfg.AppStartWithWindows); } catch (Exception ex) { res.Warnings.Add("Otomatik başlatma görevi: " + ex.Message); }
                        try { FirewallManager.Apply(cfg); } catch (Exception ex) { res.Warnings.Add("Güvenlik duvarı: " + ex.Message); }
                        if (res.Ok) WarmUp.Run(cfg);
                    }

                    cfg.Save();
                    log(res.Ok ? "Uygulandı." : "Uygulandı, ancak hatalar var.");
                }
                catch (Exception ex)
                {
                    Logger.Error("Apply", ex);
                    res.Errors.Add(ex.Message);
                }
                finally { IsBusy = false; }
            }
            NotifyChanged();
            return res;
        }

        private static void ResolveVersions(AppConfig cfg, Action<string> log, ApplyResult res)
        {
            if (!PhpManager.IsInstalled(cfg.PhpVersion))
            {
                var first = PhpManager.InstalledVersions().FirstOrDefault();
                if (first != null) { log($"PHP {cfg.PhpVersion} bulunamadı, {first} seçildi."); cfg.PhpVersion = first; }
            }
            if (!DbManager.IsInstalled(cfg.DbEngine, cfg.DbVersion))
            {
                var first = DbManager.Installed(cfg.DbEngine).FirstOrDefault();
                if (first == null)
                {
                    var other = cfg.DbEngine == Comp.MySql ? Comp.MariaDb : Comp.MySql;
                    first = DbManager.Installed(other).FirstOrDefault();
                    if (first != null) cfg.DbEngine = other;
                }
                if (first != null) { log($"Veritabanı {first} seçildi."); cfg.DbVersion = first; }
            }
            if (!NodeManager.IsInstalled(cfg.NodeVersion))
            {
                var first = NodeManager.Installed().FirstOrDefault();
                if (first != null) cfg.NodeVersion = first;
            }
            // PostgreSQL isteğe bağlıdır: seçili sürüm silindiyse kurulu başka sürüme geç, hiç yoksa kapalı kalır
            if (!string.IsNullOrEmpty(cfg.PgVersion) && !PostgreSqlManager.IsInstalled(cfg.PgVersion))
            {
                var first = PostgreSqlManager.Installed().FirstOrDefault();
                cfg.PgVersion = first ?? "";
                if (first != null) log($"PostgreSQL {first} seçildi.");
            }
            if (cfg.WebServer == "nginx" && !NginxManager.IsInstalled && ApacheManager.IsInstalled) { res.Warnings.Add("Nginx kurulu değil, Apache kullanılıyor."); cfg.WebServer = "apache"; }
            if (cfg.WebServer == "apache" && !ApacheManager.IsInstalled && NginxManager.IsInstalled) { res.Warnings.Add("Apache kurulu değil, Nginx kullanılıyor."); cfg.WebServer = "nginx"; }
        }

        private static void ApplyWeb(AppConfig cfg, Action<string> log, ApplyResult res, bool sslReady, bool start, bool configOnly)
        {
            try
            {
                if (cfg.WebServer == "nginx")
                {
                    if (!configOnly && WindowsServices.Exists(WindowsServices.Apache)) { log("Apache servisi kaldırılıyor (Nginx seçildi)..."); ApacheManager.UninstallService(); }
                    if (!NginxManager.IsInstalled) { res.Errors.Add("Nginx kurulu değil. Sürümler bölümünden kurun."); return; }
                    if (!configOnly && !WinSwManager.IsInstalled) { res.Errors.Add("WinSW kurulu değil (Nginx servisi için gerekir). Sürümler > Araçlar."); return; }
                    bool wasRunning = WindowsServices.IsRunning(WindowsServices.Nginx);
                    log("Nginx yapılandırması yazılıyor...");
                    bool changed = NginxManager.WriteConfig(cfg, sslReady);
                    var t = NginxManager.TestConfig();
                    if (!t.Ok) { res.Errors.Add("Nginx yapılandırma hatası:\r\n" + (string.IsNullOrWhiteSpace(t.AllOutput) ? "(nginx.exe hiç çıktı vermedi — program engellenmiş olabilir; çıkış kodu " + t.ExitCode + ")" : t.AllOutput) + SystemCheck.BlockedHint()); return; }
                    if (configOnly) { log("Nginx yapılandırması geçerli."); return; }
                    bool re = NginxManager.InstallServices(cfg, log);
                    if (changed || re || (start && !wasRunning))
                    {
                        CheckPorts(cfg, res, "nginx", "php-cgi", "DevNanotek");
                        log("Nginx + PHP FastCGI başlatılıyor...");
                        if (PhpManager.IsInstalled(cfg.PhpVersion)) WindowsServices.Restart(WindowsServices.PhpFcgi);
                        if (!WindowsServices.Restart(WindowsServices.Nginx)) res.Errors.Add("Nginx başlatılamadı. Günlükler: " + Paths.LogsNginx + SystemCheck.BlockedHint());
                    }
                }
                else
                {
                    if (!configOnly && (WindowsServices.Exists(WindowsServices.Nginx) || WindowsServices.Exists(WindowsServices.PhpFcgi))) { log("Nginx servisleri kaldırılıyor (Apache seçildi)..."); NginxManager.UninstallServices(); }
                    if (!ApacheManager.IsInstalled) { res.Errors.Add("Apache kurulu değil. Sürümler bölümünden kurun."); return; }
                    bool wasRunning = WindowsServices.IsRunning(WindowsServices.Apache);
                    log("Apache yapılandırması yazılıyor...");
                    bool changed = ApacheManager.WriteConfig(cfg, sslReady);
                    var t = ApacheManager.TestConfig();
                    if (!t.Ok && t.AllOutput.IndexOf("Syntax OK", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        res.Errors.Add("Apache yapılandırma hatası:\r\n" + (string.IsNullOrWhiteSpace(t.AllOutput) ? "(httpd.exe hiç çıktı vermedi — program engellenmiş olabilir; çıkış kodu " + t.ExitCode + ")" : t.AllOutput) + SystemCheck.BlockedHint());
                        return;
                    }
                    if (configOnly) { log("Apache yapılandırması geçerli (Syntax OK)."); return; }
                    bool re = ApacheManager.InstallService(cfg, log);
                    if (changed || re || (start && !wasRunning))
                    {
                        CheckPorts(cfg, res, "httpd");
                        log("Apache başlatılıyor...");
                        if (!WindowsServices.Restart(WindowsServices.Apache))
                            res.Errors.Add("Apache başlatılamadı. Hata günlüğü: " + Path.Combine(Paths.LogsApache, "error.log") + "\r\n" + TailFile(Path.Combine(Paths.LogsApache, "error.log"), 8) + SystemCheck.BlockedHint());
                    }
                }
            }
            catch (Exception ex) { Logger.Error("ApplyWeb", ex); res.Errors.Add("Web sunucu: " + ex.Message); }
        }

        private static void ApplyDb(AppConfig cfg, Action<string> log, ApplyResult res, bool start, bool configOnly)
        {
            try
            {
                if (!DbManager.IsInstalled(cfg.DbEngine, cfg.DbVersion))
                {
                    res.Warnings.Add("Veritabanı kurulu değil. Sürümler bölümünden MariaDB veya MySQL kurun.");
                    return;
                }
                bool wasRunning = WindowsServices.IsRunning(WindowsServices.Db);
                // sürüm değişiyorsa önce eski servisi durdur (veri dizini kilidi)
                var image = WindowsServices.GetImagePath(WindowsServices.Db) ?? "";
                var exe = DbManager.Mysqld(cfg.DbEngine, cfg.DbVersion);
                if (!configOnly && WindowsServices.Exists(WindowsServices.Db) && image.IndexOf(exe, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    log("Veritabanı sürümü değişiyor, eski servis durduruluyor...");
                    DbManager.UninstallService();
                    wasRunning = false;
                }
                log("my.ini yazılıyor...");
                bool changed = DbManager.WriteMyIni(cfg);
                DbManager.EnsureDataDir(cfg, log);
                if (configOnly) return;
                bool re = DbManager.InstallService(cfg, log);
                if (changed || re || (start && !wasRunning))
                {
                    CheckPorts(cfg, res, "mysqld", "mariadbd");
                    log($"{DbManager.EngineTitle(cfg.DbEngine)} başlatılıyor...");
                    if (!WindowsServices.Restart(WindowsServices.Db))
                        res.Errors.Add("Veritabanı başlatılamadı. Günlük: " + Path.Combine(Paths.LogsDb, "error.log") + "\r\n" + TailFile(Path.Combine(Paths.LogsDb, "error.log"), 10) + SystemCheck.BlockedHint());
                }
            }
            catch (Exception ex) { Logger.Error("ApplyDb", ex); res.Errors.Add("Veritabanı: " + ex.Message); }
        }

        private static void ApplyPg(AppConfig cfg, Action<string> log, ApplyResult res, bool start, bool configOnly)
        {
            try
            {
                if (!PostgreSqlManager.IsActive(cfg))
                {
                    if (!configOnly && WindowsServices.Exists(WindowsServices.PostgreSql)) { log("PostgreSQL servisi kaldırılıyor..."); PostgreSqlManager.UninstallService(); }
                    return;
                }
                bool wasRunning = WindowsServices.IsRunning(WindowsServices.PostgreSql);
                log("PostgreSQL yapılandırması yazılıyor...");
                bool changed = PostgreSqlManager.WriteConfig(cfg);
                PostgreSqlManager.EnsureDataDir(cfg, log);
                if (configOnly) return;
                bool re = PostgreSqlManager.InstallService(cfg, log);
                if (changed || re || (start && !wasRunning))
                {
                    CheckPorts(cfg, res, "postgres", "pg_ctl");
                    log($"PostgreSQL {cfg.PgVersion} başlatılıyor...");
                    if (!WindowsServices.Restart(WindowsServices.PostgreSql, 120))
                        res.Errors.Add("PostgreSQL başlatılamadı. " + PostgreSqlManager.HintText() + SystemCheck.BlockedHint());
                }
            }
            catch (Exception ex) { Logger.Error("ApplyPg", ex); res.Errors.Add("PostgreSQL: " + ex.Message); }
        }

        private static void ApplyMail(AppConfig cfg, Action<string> log, ApplyResult res, bool start)
        {
            try
            {
                if (!cfg.MailpitEnabled || !MailpitManager.IsInstalled)
                {
                    if (WindowsServices.Exists(WindowsServices.Mailpit)) { log("Mailpit servisi kaldırılıyor..."); MailpitManager.RemoveService(); }
                    return;
                }
                if (!WinSwManager.IsInstalled) { res.Warnings.Add("WinSW kurulu değil; Mailpit servisi kurulamadı."); return; }
                bool wasRunning = WindowsServices.IsRunning(WindowsServices.Mailpit);
                bool re = MailpitManager.EnsureService(cfg, log);
                if (re || (start && !wasRunning))
                {
                    log("Mailpit başlatılıyor...");
                    if (!WindowsServices.Restart(WindowsServices.Mailpit)) res.Warnings.Add("Mailpit başlatılamadı (port " + cfg.SmtpPort + "/" + cfg.MailpitUiPort + " dolu olabilir)." + SystemCheck.BlockedHint());
                }
            }
            catch (Exception ex) { Logger.Error("ApplyMail", ex); res.Warnings.Add("Mailpit: " + ex.Message); }
        }

        private static void CheckPorts(AppConfig cfg, ApplyResult res, params string[] ourExe)
        {
            foreach (var m in PortUtil.Conflicts(cfg, ourExe.Concat(new[] { "httpd", "nginx", "mysqld", "mariadbd", "postgres", "mailpit", "php-cgi", "DevNanotek" })))
                if (!res.Warnings.Contains(m)) res.Warnings.Add(m);
        }

        /// <summary>Son N satırı okur (servisin açık tuttuğu log dosyaları için paylaşımlı okuma).</summary>
        public static string TailFile(string path, int lines)
        {
            try
            {
                if (!File.Exists(path)) return "";
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    const int max = 256 * 1024;
                    if (fs.Length > max) fs.Seek(-max, SeekOrigin.End);
                    using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                    {
                        var all = sr.ReadToEnd().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
                        return string.Join("\r\n", all.Skip(Math.Max(0, all.Length - lines)));
                    }
                }
            }
            catch { return ""; }
        }

        public static void EnsureLandingPage(AppConfig cfg, bool force)
        {
            try
            {
                var root = cfg.EffectiveDocRoot;
                Directory.CreateDirectory(root);
                var index = Path.Combine(root, "index.php");
                var tpl = Templates.Load("index.php");
                bool any = Directory.EnumerateFiles(root).Any(f => Path.GetFileName(f).StartsWith("index.", StringComparison.OrdinalIgnoreCase));
                if (force || !any) { File.WriteAllText(index, tpl, new UTF8Encoding(false)); return; }
                // Önceki sürümlerin yazdığı açılış sayfası yenisiyle değiştirilir (eskisi yedeklenir); kullanıcının kendi index'ine dokunulmaz
                if (File.Exists(index))
                {
                    var cur = File.ReadAllText(index, Encoding.UTF8);
                    bool ours = cur.IndexOf("localhost açılış sayfası", StringComparison.Ordinal) >= 0 && cur.Contains("dn_dirs(");
                    if (ours && cur != tpl)
                    {
                        var bak = index + ".yedek-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                        File.Copy(index, bak, true);
                        File.WriteAllText(index, tpl, new UTF8Encoding(false));
                        Logger.Info("Açılış sayfası güncellendi (eski sayfa: " + bak + ")");
                    }
                }
            }
            catch (Exception ex) { Logger.Warn("Açılış sayfası yazılamadı: " + ex.Message); }
        }

        // ------------------------------------------------------------------
        //  Başlat / Durdur
        // ------------------------------------------------------------------
        public static IEnumerable<string> ActiveServices(AppConfig cfg)
        {
            if (cfg.WebServer == "nginx") { yield return WindowsServices.PhpFcgi; yield return WindowsServices.Nginx; }
            else yield return WindowsServices.Apache;
            yield return WindowsServices.Db;
            if (PostgreSqlManager.IsActive(cfg)) yield return WindowsServices.PostgreSql;
            if (cfg.MailpitEnabled) yield return WindowsServices.Mailpit;
        }

        public static Task<List<string>> StartAllAsync(AppConfig cfg, Action<string> log) => Task.Run(() => StartAll(cfg, log));
        public static Task<List<string>> StopAllAsync(AppConfig cfg, Action<string> log) => Task.Run(() => StopAll(cfg, log));

        public static List<string> StartAll(AppConfig cfg, Action<string> log)
        {
            var errors = new List<string>();
            IsBusy = true;
            try
            {
                var missing = ActiveServices(cfg).Where(s => !WindowsServices.Exists(s)).ToList();
                if (missing.Count > 0)
                {
                    log?.Invoke("Servisler kurulu değil, yapılandırma uygulanıyor...");
                    var r = Apply(cfg, log, true);
                    errors.AddRange(r.Errors);
                    return errors;
                }
                foreach (var m in PortUtil.Conflicts(cfg, new[] { "httpd", "nginx", "mysqld", "mariadbd", "postgres", "mailpit", "php-cgi", "DevNanotek" })) log?.Invoke("UYARI: " + m);
                foreach (var s in ActiveServices(cfg))
                {
                    if (WindowsServices.IsRunning(s)) continue;
                    log?.Invoke("Başlatılıyor: " + s);
                    if (!WindowsServices.Start(s)) errors.Add(s + " başlatılamadı. " + HintFor(s) + SystemCheck.BlockedHint());
                }
                if (errors.Count == 0) WarmUp.Run(cfg);
            }
            finally { IsBusy = false; NotifyChanged(); }
            return errors;
        }

        public static List<string> StopAll(AppConfig cfg, Action<string> log)
        {
            var errors = new List<string>();
            IsBusy = true;
            try
            {
                foreach (var s in WindowsServices.All)
                {
                    if (!WindowsServices.Exists(s) || !WindowsServices.IsRunning(s)) continue;
                    log?.Invoke("Durduruluyor: " + s);
                    if (!WindowsServices.Stop(s)) errors.Add(s + " durdurulamadı.");
                }
            }
            finally { IsBusy = false; NotifyChanged(); }
            return errors;
        }

        public static bool StartService(string name)
        {
            var ok = WindowsServices.Start(name);
            NotifyChanged();
            return ok;
        }

        public static bool StopService(string name)
        {
            var ok = WindowsServices.Stop(name);
            NotifyChanged();
            return ok;
        }

        public static bool RestartService(string name)
        {
            var ok = WindowsServices.Restart(name);
            NotifyChanged();
            return ok;
        }

        /// <summary>
        /// Çalışma modunu servislere ve zamanlanmış göreve hızlıca uygular (yeniden başlatma yapmaz):
        /// otomatik = servisler Windows açılışında başlar, elle = yalnızca program/kullanıcı başlatır.
        /// </summary>
        public static void ApplyStartMode(AppConfig cfg)
        {
            foreach (var s in WindowsServices.All)
                if (WindowsServices.Exists(s)) WindowsServices.SetStartType(s, cfg.AutoStartServices ? "auto" : "demand");
            try { Autostart.Apply(cfg.AppStartWithWindows); } catch (Exception ex) { Logger.Warn("Otomatik başlatma görevi: " + ex.Message); }
            Logger.Info("Çalışma modu: " + (cfg.AutoStartServices ? "her zaman açık" : "sadece program açıkken"));
        }

        /// <summary>php.ini veya custom.conf değişince web sunucuyu (ve Nginx modunda php-cgi havuzunu) yeniden başlatır.</summary>
        public static List<string> RestartWeb(AppConfig cfg)
        {
            var errors = new List<string>();
            if (cfg.WebServer == "nginx")
            {
                if (WindowsServices.Exists(WindowsServices.PhpFcgi) && !WindowsServices.Restart(WindowsServices.PhpFcgi)) errors.Add("PHP FastCGI yeniden başlatılamadı. " + HintFor(WindowsServices.PhpFcgi));
                if (WindowsServices.Exists(WindowsServices.Nginx) && !WindowsServices.Restart(WindowsServices.Nginx)) errors.Add("Nginx yeniden başlatılamadı. " + HintFor(WindowsServices.Nginx));
            }
            else if (WindowsServices.Exists(WindowsServices.Apache) && !WindowsServices.Restart(WindowsServices.Apache))
                errors.Add("Apache yeniden başlatılamadı. " + HintFor(WindowsServices.Apache));
            NotifyChanged();
            return errors;
        }

        public static string HintFor(string service)
        {
            if (service == WindowsServices.Apache) return "Hata günlüğü: " + Path.Combine(Paths.LogsApache, "error.log") + "\r\n" + TailFile(Path.Combine(Paths.LogsApache, "error.log"), 6);
            if (service == WindowsServices.Db) return "Hata günlüğü: " + Path.Combine(Paths.LogsDb, "error.log") + "\r\n" + TailFile(Path.Combine(Paths.LogsDb, "error.log"), 8);
            if (service == WindowsServices.Nginx) return "Hata günlüğü: " + Path.Combine(Paths.LogsNginx, "error.log") + "\r\n" + TailFile(Path.Combine(Paths.LogsNginx, "error.log"), 6);
            if (service == WindowsServices.PostgreSql) return PostgreSqlManager.HintText();
            return "Günlükler: " + Paths.LogsWinSw;
        }

        /// <summary>
        /// DevNanotek'in Windows'ta yaptığı tüm değişiklikleri geri alır (servisler, hosts, PATH, zamanlanmış görev,
        /// isteğe bağlı SSL kök sertifikası ve kısayollar). C:\devnanotek klasörüne ve verilere DOKUNMAZ.
        /// </summary>
        public static void RemoveAllServices(Action<string> log, bool removeCaAndShortcuts = false)
        {
            log?.Invoke("Servisler durdurulup kaldırılıyor...");
            ApacheManager.UninstallService();
            NginxManager.UninstallServices();
            DbManager.UninstallService();
            PostgreSqlManager.UninstallService();
            MailpitManager.RemoveService();
            log?.Invoke("hosts kayıtları, PATH ve otomatik başlatma temizleniyor...");
            HostsFile.SetManagedHosts(new string[0]);
            Autostart.Disable();
            EnvPath.ApplyUserPath(false);
            if (removeCaAndShortcuts)
            {
                log?.Invoke("SSL kök sertifikası ve kısayollar kaldırılıyor...");
                MkcertManager.UninstallCa();
                SelfInstall.RemoveShortcuts();
            }
            Logger.Info("Sistem değişiklikleri geri alındı");
            NotifyChanged();
        }
    }
}
