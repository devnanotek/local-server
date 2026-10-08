using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using DevNanotek.Core;

namespace DevNanotek
{
    /// <summary>
    /// Giriş noktası. Parametresiz çalıştırılınca arayüz açılır. Komut satırı modları:
    ///   --autostart            Tepsiye küçük başlar (Windows oturum açılışı)
    ///   --install-defaults     Önerilen paketi indirip kurar (PHP, Apache, MariaDB, phpMyAdmin, Mailpit, mkcert, Composer, Node)
    ///   --install php:8.3.35 mariadb:11.4.13 ...   Belirli bileşenleri kurar ("php" tek başına = önerilen sürüm)
    ///   --apply                Yapılandırmayı uygula, servisleri kur/güncelle ve başlat
    ///   --config-only          Yalnızca yapılandırma dosyalarını üret (servis/hosts/PATH dokunulmaz)
    ///   --start-all / --stop-all / --status
    ///   --uninstall-system     Servisleri, hosts kayıtlarını, PATH ve otomatik başlatmayı kaldırır (dosyalar kalır)
    ///   --fcgi-spawner ...     (iç kullanım) Nginx modu için php-cgi havuzu
    /// </summary>
    public static class Program
    {
        [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);

        private static readonly string[] CliFlags = { "--install-defaults", "--install", "--apply", "--config-only", "--start-all", "--stop-all", "--status", "--uninstall-system", "--reset", "--mode", "--check-updates", "--linux-check", "--help", "-h", "/?" };

        [STAThread]
        public static int Main(string[] args)
        {
            args = args ?? new string[0];
            if (args.Contains("--fcgi-spawner")) return FcgiSpawner.Run(args);

            bool quietUninstall = args.Contains("--uninstall") && args.Contains("--quiet");
            if (args.Any(a => CliFlags.Contains(a)) || quietUninstall)
            {
                try { AttachConsole(-1); } catch { }
                L.Init(AppConfig.Current.Language);
                Console.WriteLine();
                try { return RunCli(args); }
                catch (Exception ex) { Err("HATA: " + ex.Message); Logger.Error("CLI", ex); return 1; }
            }

            Args = args;
            try { System.Windows.Application.ResourceAssembly = typeof(App).Assembly; } catch { }
            var app = new App();
            app.InitializeComponent();
            return app.Run();
        }

        /// <summary>Main'e gelen parametreler (arayüz bunları kullanır).</summary>
        public static string[] Args { get; private set; } = new string[0];

        // konsol çıktısı arayüz dilinde
        private static void Out(string s) => Console.WriteLine(L.T(s));
        private static void Err(string s) => Console.Error.WriteLine(L.T(s));
        private static void Help(string option, string text) => Console.WriteLine("  " + option.PadRight(53) + L.T(text));

        private static int RunCli(string[] args)
        {
            Paths.EnsureLayout();
            var cfg = AppConfig.Current;
            Action<string> log = s => Out(s);
            int rc = 0;

            if (args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
            {
                Out("DevNanotek " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
                Out("  --install-defaults | --install php:8.3.35 apache mariadb:11.4.13 node ...");
                Out("  --apply | --config-only | --start-all | --stop-all | --status | --uninstall-system");
                Help("--reset quick|full|db|factory [--delete-downloads]", "onarım / sıfırlama");
                Help("--mode always|manual", "her zaman açık / sadece ben açınca");
                Help("--uninstall [--quiet [--all]]", "kaldır (--all: projeler/veritabanları dahil)");
                Help("--repair", "Onarım ve Sıfırlama penceresini aç");
                Help("--check-updates", "yeni sürümleri denetle, önerilenleri listele");
                Help(L.Pick("--linux-check <klasör>", "--linux-check <folder>"), "projeyi Linux sunucu uyumluluğu için tara");
                return 0;
            }

            // ---- Linux uyumluluk denetimi (yalnız okur, hiçbir şeyi değiştirmez) ----
            int lc = Array.IndexOf(args, "--linux-check");
            if (lc >= 0)
            {
                var dir = lc + 1 < args.Length && !args[lc + 1].StartsWith("--") ? args[lc + 1] : cfg.EffectiveDocRoot;
                if (!System.IO.Directory.Exists(dir)) { Err("Klasör yok: " + dir); return 1; }
                var rep = LinuxCompat.Scan(dir, s => Out("  " + s));
                Out(rep.ToText());
                return rep.Errors > 0 ? 2 : 0;
            }

            // ---- yeni sürüm denetimi ----
            if (args.Contains("--check-updates"))
            {
                Out("Sürümler internetten denetleniyor (" + SystemInfo.Summary + ")...");
                var rep = Catalog.Current.RefreshOnlineAsync(s => Out("  " + s)).GetAwaiter().GetResult();
                UpdateChecker.Recompute();
                foreach (var comp in new[] { Comp.Php, Comp.Apache, Comp.Nginx, Comp.MariaDb, Comp.MySql, Comp.PostgreSql, Comp.Node, Comp.PhpMyAdmin, Comp.Adminer, Comp.Mailpit, Comp.Mkcert, Comp.SqlSrv })
                {
                    var list = Catalog.Current.AvailableFor(comp).Select(e => e.Version + (e.Recommended ? "*" : "")).ToList();
                    Console.WriteLine($"  {Comp.Title(comp),-20} {(list.Count == 0 ? L.T("(bu mimaride yok)") : string.Join("  ", list))}");
                }
                Out("  (* = önerilen)");
                Out(UpdateChecker.Current.Count == 0 ? "Güncelleme yok." : "Güncellemeler: " + string.Join(", ", UpdateChecker.Current.Select(u => u.Text)));
            }

            // ---- sessiz kaldırma (Denetim Masası QuietUninstallString) ----
            if (args.Contains("--uninstall") && args.Contains("--quiet"))
            {
                Ipc.CloseRunningGui();
                var ur = Uninstaller.Run(args.Contains("--all"), false, log);
                foreach (var w in ur.Warnings) Out("UYARI: " + w);
                foreach (var e in ur.Errors) Err("HATA: " + e);
                if (ur.Ok) Uninstaller.ScheduleFinalCleanup(args.Contains("--all"));
                return ur.Ok ? 0 : 1;
            }

            // ---- onarım / sıfırlama ----
            int ri = Array.IndexOf(args, "--reset");
            if (ri >= 0)
            {
                var kindArg = ri + 1 < args.Length ? args[ri + 1].ToLowerInvariant() : "quick";
                ResetKind kind = kindArg == "full" ? ResetKind.Full : kindArg == "db" ? ResetKind.Database : kindArg == "factory" ? ResetKind.Factory : ResetKind.Quick;
                Out("==> " + ResetManager.Title(kind));
                var prog = new SyncProgress<DownloadProgress>(p => { if (!p.Indeterminate && (int)p.Percent % 25 == 0 && !string.IsNullOrEmpty(p.Status)) Out("    " + p.Status); });
                var rr = ResetManager.RunAsync(kind, cfg, log, prog, CancellationToken.None, args.Contains("--delete-downloads")).GetAwaiter().GetResult();
                foreach (var w in rr.Warnings) Out("UYARI: " + w);
                foreach (var e in rr.Errors) Err("HATA: " + e);
                if (!rr.Ok) rc = 1;
                cfg = AppConfig.Current; // fabrika ayarlarından sonra yeni nesne
            }

            // ---- çalışma modu ----
            int mi = Array.IndexOf(args, "--mode");
            if (mi >= 0 && mi + 1 < args.Length)
            {
                bool always = !args[mi + 1].Equals("manual", StringComparison.OrdinalIgnoreCase);
                cfg.SetRunMode(always);
                cfg.Save();
                Stack.ApplyStartMode(cfg);
                Out("Çalışma modu: " + (always ? "her zaman açık" : "sadece ben açınca"));
            }

            // ---- kurulum ----
            var toInstall = new List<CatalogEntry>();
            if (args.Contains("--install-defaults")) toInstall.AddRange(Installer.DefaultSet(true));
            int idx = Array.IndexOf(args, "--install");
            if (idx >= 0)
                for (int i = idx + 1; i < args.Length && !args[i].StartsWith("--"); i++)
                {
                    var e = Installer.Parse(args[i]);
                    if (e == null) { Err("Bilinmeyen bileşen: " + args[i]); rc = 1; continue; }
                    toInstall.Add(e);
                }
            foreach (var e in toInstall)
            {
                Out("==> " + e.Title);
                int lastPct = -1;
                var progress = new SyncProgress<DownloadProgress>(p =>
                {
                    if (p.Indeterminate) return;
                    var pct = (int)p.Percent;
                    if (pct / 10 != lastPct / 10) { lastPct = pct; Console.WriteLine($"    {L.T(p.Status)} ({pct}%)"); }
                });
                try { Installer.InstallAsync(e, cfg, progress, CancellationToken.None).GetAwaiter().GetResult(); Out("    kuruldu."); }
                catch (Exception ex) { Err("    HATA: " + ex.Message); rc = 1; }
            }
            if (toInstall.Count > 0) { cfg.SetupCompleted = true; cfg.Save(); }

            // ---- uygulama / servisler ----
            if (args.Contains("--config-only"))
            {
                var r = Stack.Apply(cfg, log, false, true);
                foreach (var w in r.Warnings) Out("UYARI: " + w);
                foreach (var e in r.Errors) Err("HATA: " + e);
                if (!r.Ok) rc = 1;
            }
            if (args.Contains("--apply"))
            {
                var r = Stack.Apply(cfg, log, true);
                foreach (var w in r.Warnings) Out("UYARI: " + w);
                foreach (var e in r.Errors) Err("HATA: " + e);
                if (!r.Ok) rc = 1;
            }
            if (args.Contains("--start-all")) { var e = Stack.StartAll(cfg, log); foreach (var x in e) Err("HATA: " + x); if (e.Count > 0) rc = 1; }
            if (args.Contains("--stop-all")) { var e = Stack.StopAll(cfg, log); foreach (var x in e) Err("HATA: " + x); if (e.Count > 0) rc = 1; }
            if (args.Contains("--uninstall-system")) Stack.RemoveAllServices(log, false);
            if (args.Contains("--status"))
            {
                Out($"Kök: {Paths.Root}");
                Out($"Web: {cfg.WebServer}  PHP: {cfg.PhpVersion}  DB: {cfg.DbEngine} {cfg.DbVersion}  Node: {cfg.NodeVersion}");
                Out("Çalışma modu: " + (cfg.AutoStartServices ? "her zaman açık" : "sadece ben açınca") + "   Kurulum tamam: " + cfg.SetupCompleted);
                Out("Akıllı Uygulama Denetimi: " + (SystemCheck.SmartAppControlOn ? "AÇIK (bileşenleri engelleyebilir)" : "kapalı"));
                foreach (var kv in WindowsServices.Snapshot()) Console.WriteLine($"  {kv.Key,-22} {L.T(kv.Value)}");
            }
            return rc;
        }
    }

    /// <summary>Konsol modunda ilerlemeyi aynı iş parçacığında raporlar (Progress&lt;T&gt; SynchronizationContext ister).</summary>
    public class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _a;
        public SyncProgress(Action<T> a) { _a = a; }
        public void Report(T value) { try { _a(value); } catch { } }
    }
}
