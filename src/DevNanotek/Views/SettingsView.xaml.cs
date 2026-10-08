using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DevNanotek.Core;

namespace DevNanotek.Views
{
    public class QuickItem : INotifyPropertyChanged
    {
        private string _value;
        public string Key { get; set; }
        public string Hint { get; set; }
        public string Original { get; set; }
        public string Value { get => _value; set { _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); } }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class SettingsView : ViewBase
    {
        /// <summary>Başka bir sayfadan belirli sekmeyi açmak için (ör. "php").</summary>
        public static string RequestTab { get; set; }

        private static readonly Dictionary<string, string> Hints = new Dictionary<string, string>
        {
            ["memory_limit"] = "Betik başına bellek (örn. 512M, -1 = sınırsız)",
            ["upload_max_filesize"] = "En büyük yükleme dosyası (post_max_size'dan küçük/eşit olmalı)",
            ["post_max_size"] = "Form gönderimi üst sınırı",
            ["max_execution_time"] = "Saniye (0 = sınırsız)",
            ["max_input_time"] = "Saniye",
            ["max_input_vars"] = "Form alan sayısı üst sınırı",
            ["display_errors"] = "On / Off — hatalar ekranda görünsün mü",
            ["error_reporting"] = "E_ALL veya E_ALL & ~E_DEPRECATED",
            ["date.timezone"] = "Europe/Istanbul",
            ["short_open_tag"] = "On / Off — <? kısa etiket (eski projeler için On)",
            ["opcache.enable"] = "1 / 0",
            ["max_file_uploads"] = "Tek istekte en fazla dosya sayısı"
        };

        private static readonly Dictionary<string, string> DevDefaults = new Dictionary<string, string>
        {
            ["memory_limit"] = "512M", ["upload_max_filesize"] = "512M", ["post_max_size"] = "512M",
            ["max_execution_time"] = "300", ["max_input_time"] = "300", ["max_input_vars"] = "10000",
            ["display_errors"] = "On", ["error_reporting"] = "E_ALL & ~E_DEPRECATED", ["date.timezone"] = "Europe/Istanbul",
            ["short_open_tag"] = "Off", ["opcache.enable"] = "1", ["max_file_uploads"] = "100"
        };

        private List<QuickItem> _quick = new List<QuickItem>();
        private List<PhpExtension> _ext = new List<PhpExtension>();
        private string _confLoadedFor;

        public SettingsView() { InitializeComponent(); }

        private string EditPhp => CbPhpVer.SelectedItem as string;

        // ==================================================================
        public override void Refresh()
        {
            var c = Cfg;
            RbApache.IsChecked = c.WebServer != "nginx";
            RbNginx.IsChecked = c.WebServer == "nginx";
            TbDocRoot.Text = c.DocumentRoot ?? "";
            TbHttp.Text = c.HttpPort.ToString(); TbHttps.Text = c.HttpsPort.ToString(); TbDb.Text = c.DbPort.ToString(); TbPg.Text = c.PgPort.ToString();
            TbSmtp.Text = c.SmtpPort.ToString(); TbMailUi.Text = c.MailpitUiPort.ToString();
            TbFcgi.Text = c.FcgiPort.ToString(); TbFcgiCount.Text = c.FcgiChildren.ToString();
            _loadingRunMode = true;
            RbRunAlways.IsChecked = c.AutoStartServices;
            RbRunManual.IsChecked = !c.AutoStartServices;
            _loadingRunMode = false;
            CbAutoServices.IsChecked = c.AutoStartServices;
            CbStartOnLaunch.IsChecked = c.StartServicesOnLaunch;
            CbAppWithWindows.IsChecked = c.AppStartWithWindows;
            CbMinToTray.IsChecked = c.MinimizeToTray;
            CbStopOnExit.IsChecked = c.StopServicesOnExit;
            CbMailpit.IsChecked = c.MailpitEnabled;
            CbPath.IsChecked = c.AddToPath;
            CbSsl.IsChecked = c.SslEnabled;
            CbAutoVhost.IsChecked = c.AutoVirtualHosts;
            TbTld.Text = c.VhostTld;
            CbDbNetwork.IsChecked = c.DbBindAddress == "0.0.0.0";
            CbLan.IsChecked = c.LanAccess;
            _loadingTheme = true;
            var tm = ThemeManager.Mode;
            RbThemeSystem.IsChecked = tm == "system"; RbThemeLight.IsChecked = tm == "light"; RbThemeDark.IsChecked = tm == "dark";
            _loadingTheme = false;
            _loadingLang = true;
            LangAuto.Content = L.F("Cihazın dili — {0}", L.DeviceLanguage == L.Turkish ? "Türkçe" : "English");
            CbLanguage.SelectedItem = CbLanguage.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string ?? "") == (c.Language ?? "")) ?? LangAuto;
            _loadingLang = false;
            RefreshDefender();

            // PHP
            var vers = PhpManager.InstalledVersions();
            var keep = EditPhp;
            CbPhpVer.ItemsSource = vers;
            CbPhpVer.SelectedItem = vers.Contains(keep) ? keep : vers.Contains(c.PhpVersion) ? c.PhpVersion : vers.FirstOrDefault();
            LoadPhp();

            // Web
            RbConfApache.IsChecked = c.WebServer != "nginx";
            RbConfNginx.IsChecked = c.WebServer == "nginx";
            LoadConf();

            // DB
            DbConnInfo.Text = $"Sunucu: 127.0.0.1    Port: {c.DbPort}    Kullanıcı: root    Şifre: {(string.IsNullOrEmpty(c.DbRootPassword) ? "(boş)" : "(tanımlı)")}\r\n" +
                              $"Motor: {DbManager.EngineTitle(c.DbEngine)} {c.DbVersion}    Veri: {DbManager.DataDir(c.DbEngine, c.DbVersion)}";
            try { CnfText.Text = File.Exists(DbManager.CustomCnf) ? File.ReadAllText(DbManager.CustomCnf, Encoding.UTF8) : ""; } catch { }
            RefreshOtherDbs();

            // SSL
            RefreshSslInfo();
            var hosts = HostsFile.GetManagedHosts();
            HostsList.Text = hosts.Count == 0 ? L.T("(kayıt yok)") : string.Join("\r\n", hosts.Select(h => "127.0.0.1  " + h));

            AboutText.Text = $"DEVNANOTEK Local Server v{App.Version} · MIT lisanslı açık kaynak · devnanotek.net\r\nKaynak kod: {AppInfo.RepoUrl}\r\n{SystemInfo.Summary}\r\nKök klasör: {Paths.Root}\r\nProgram: {Paths.ExePath}\r\nGünlük: {Paths.AppLog}";

            if (!string.IsNullOrEmpty(RequestTab))
            {
                foreach (TabItem t in Tabs.Items) if ((t.Tag as string) == RequestTab) { Tabs.SelectedItem = t; break; }
                RequestTab = null;
            }
        }

        private void RefreshSslInfo()
        {
            var sb = new StringBuilder();
            sb.Append("mkcert: ").Append(MkcertManager.IsInstalled ? "kurulu" : "kurulu değil (Sürümler > Araçlar)");
            sb.Append("   ·   Kök sertifika: ").Append(MkcertManager.CaExists ? (MkcertManager.CaTrusted ? "güvenilir ✔" : "Windows'a eklenmemiş") : "yok");
            sb.Append("   ·   Sunucu sertifikası: ").Append(MkcertManager.CertsExist ? "var" : "yok");
            sb.Append($"\r\nAdres: {UI.LocalhostHttpsUrl()}   (Firefox açıksa yeniden başlatın)");
            SslInfo.Text = sb.ToString();
        }

        // ==================================================================
        //  Genel: Kaydet ve uygula
        // ==================================================================
        private bool ReadPort(TextBox tb, string name, out int port)
        {
            if (!int.TryParse((tb.Text ?? "").Trim(), out port) || port < 1 || port > 65535)
            {
                UI.Warn($"{name} portu geçersiz (1-65535).");
                tb.Focus();
                return false;
            }
            return true;
        }

        private bool ReadForm()
        {
            var c = Cfg;
            if (!ReadPort(TbHttp, "HTTP", out var http) || !ReadPort(TbHttps, "HTTPS", out var https) || !ReadPort(TbDb, "Veritabanı", out var db)
                || !ReadPort(TbPg, "PostgreSQL", out var pg)
                || !ReadPort(TbSmtp, "SMTP", out var smtp) || !ReadPort(TbMailUi, "Mailpit arayüz", out var mui) || !ReadPort(TbFcgi, "FastCGI", out var fcgi))
                return false;
            if (!int.TryParse(TbFcgiCount.Text, out var cnt) || cnt < 1 || cnt > 16) { UI.Warn("php-cgi süreç sayısı 1-16 arası olmalı."); return false; }
            var ports = new[] { http, https, db, pg, smtp, mui };
            if (ports.Distinct().Count() != ports.Length) { UI.Warn("Portlar birbirinden farklı olmalı."); return false; }

            var docroot = (TbDocRoot.Text ?? "").Trim();
            if (docroot.Length > 0 && !Directory.Exists(docroot))
            {
                if (!UI.Confirm("Belge kökü klasörü yok:\n" + docroot + "\n\nOluşturulsun mu?")) return false;
                try { Directory.CreateDirectory(docroot); } catch (Exception ex) { UI.Err(ex.Message); return false; }
            }
            var tld = (TbTld.Text ?? "").Trim().TrimStart('.').ToLowerInvariant();
            if (!Regex.IsMatch(tld, @"^[a-z][a-z0-9\-]{0,20}$")) { UI.Warn("Geçersiz alan adı uzantısı. Örn: test"); return false; }
            if (tld == "dev" || tld == "app") { if (!UI.Confirm($".{tld} uzantısı tarayıcılarda her zaman HTTPS'e zorlanır (HSTS). Yine de kullanılsın mı?")) return false; }

            c.WebServer = RbNginx.IsChecked == true ? "nginx" : "apache";
            c.DocumentRoot = docroot;
            c.HttpPort = http; c.HttpsPort = https; c.DbPort = db; c.PgPort = pg; c.SmtpPort = smtp; c.MailpitUiPort = mui; c.FcgiPort = fcgi; c.FcgiChildren = cnt;
            c.AutoStartServices = CbAutoServices.IsChecked == true;
            c.StartServicesOnLaunch = CbStartOnLaunch.IsChecked == true;
            c.AppStartWithWindows = CbAppWithWindows.IsChecked == true;
            c.MinimizeToTray = CbMinToTray.IsChecked == true;
            c.StopServicesOnExit = CbStopOnExit.IsChecked == true;
            c.MailpitEnabled = CbMailpit.IsChecked == true;
            c.AddToPath = CbPath.IsChecked == true;
            c.SslEnabled = CbSsl.IsChecked == true;
            c.AutoVirtualHosts = CbAutoVhost.IsChecked == true;
            c.VhostTld = tld;
            c.DbBindAddress = CbDbNetwork.IsChecked == true ? "0.0.0.0" : "127.0.0.1";
            c.LanAccess = CbLan.IsChecked == true;
            c.Sanitize();
            c.Save();
            return true;
        }

        private async void SaveApply_Click(object sender, RoutedEventArgs e)
        {
            if (!ReadForm()) return;
            if (Cfg.WebServer == "nginx" && !NginxManager.IsInstalled) { UI.Warn("Nginx kurulu değil. Önce Sürümler > Web Sunucu bölümünden kurun."); return; }
            await Main.ApplyConfigAsync(true, "Ayarlar uygulanıyor");
            Refresh();
        }

        private void BrowseDocRoot_Click(object sender, RoutedEventArgs e)
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = L.T("Belge kökü (localhost) klasörünü seçin"), SelectedPath = Cfg.EffectiveDocRoot })
                if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) TbDocRoot.Text = d.SelectedPath;
        }

        private void DefaultDocRoot_Click(object sender, RoutedEventArgs e) => TbDocRoot.Text = "";

        // ==================================================================
        //  PHP
        // ==================================================================
        private void PhpVer_Changed(object sender, SelectionChangedEventArgs e) => LoadPhp();

        private void LoadPhp()
        {
            var v = EditPhp;
            if (v == null)
            {
                PhpActiveNote.Text = "PHP kurulu değil.";
                QuickList.ItemsSource = null; ExtList.ItemsSource = null; IniText.Text = ""; IniPath.Text = "";
                QuickExt.Load(null);
                return;
            }
            PhpActiveNote.Text = v == Cfg.PhpVersion ? "(aktif sürüm — değişiklikler web sunucusunu yeniden başlatır)" : "(aktif değil — değişiklikler bu sürüme geçince geçerli olur)";
            PhpManager.EnsureIni(v, Cfg);
            var ini = PhpManager.ReadIni(v);
            _quick = PhpManager.QuickKeys.Select(k =>
            {
                var val = PhpManager.IniGetText(ini, k) ?? "";
                return new QuickItem { Key = k, Value = val, Original = val, Hint = Hints.TryGetValue(k, out var h) ? h : "" };
            }).ToList();
            QuickList.ItemsSource = _quick;
            QuickExt.Load(v);
            _ext = PhpManager.Extensions(v);
            ExtList.ItemsSource = _ext;
            IniText.Text = ini;
            IniPath.Text = PhpManager.IniPath(v);
        }

        private async Task AfterPhpChange(string version, string title)
        {
            // php.ini sözdizimi/başlangıç hatası denetimi
            var output = await Task.Run(() => PhpManager.RunPhp(version, "-v"));
            var problems = output.Split('\n').Where(l => l.IndexOf("PHP Warning", StringComparison.OrdinalIgnoreCase) >= 0
                                                       || l.IndexOf("PHP Fatal", StringComparison.OrdinalIgnoreCase) >= 0
                                                       || l.IndexOf("PHP Startup", StringComparison.OrdinalIgnoreCase) >= 0
                                                       || l.IndexOf("Unable to load", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (problems.Count > 0)
            {
                bool extProblem = problems.Any(p => p.IndexOf("Unable to load dynamic library", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf("Failed loading Zend extension", StringComparison.OrdinalIgnoreCase) >= 0);
                var msg = "PHP başlangıçta uyarı verdi (php.ini'yi kontrol edin):\n\n" + string.Join("\n", problems.Take(8));
                if (extProblem && UI.Confirm(msg + "\n\nYüklenemeyen eklentiler php.ini'de otomatik kapatılsın mı? (önerilir — aksi halde uyarı her sayfada görünür)"))
                    await Task.Run(() => PhpManager.SelfHeal(version));
                else if (!extProblem) UI.Warn(msg);
            }
            if (version == Cfg.PhpVersion)
            {
                await Main.RunBusyAsync(title, async log =>
                {
                    log("Web sunucu yeniden başlatılıyor...");
                    var errs = await Task.Run(() => Stack.RestartWeb(Cfg));
                    if (errs.Count > 0) UI.Err(string.Join("\n\n", errs));
                });
            }
            LoadPhp();
        }

        private async void QuickSave_Click(object sender, RoutedEventArgs e)
        {
            var v = EditPhp; if (v == null) return;
            var changed = _quick.Where(q => (q.Value ?? "") != (q.Original ?? "")).ToList();
            if (changed.Count == 0 && QuickExt.Changed.Count == 0) { UI.Msg("Değişiklik yok."); return; }
            // değerler ve eklentiler tek seferde yazılır (php.ini.bak = kaydetmeden önceki hal)
            var ini = PhpManager.ReadIni(v);
            foreach (var q in changed) ini = PhpManager.IniSetText(ini, q.Key, (q.Value ?? "").Trim());
            ini = QuickExt.ApplyTo(ini);
            PhpManager.WriteIni(v, ini);
            if (changed.Count > 0) Logger.Info($"php.ini ({v}) güncellendi: " + string.Join(", ", changed.Select(q => q.Key + "=" + q.Value)));
            await AfterPhpChange(v, "PHP ayarları uygulanıyor");
        }

        private void QuickDefaults_Click(object sender, RoutedEventArgs e)
        {
            foreach (var q in _quick) if (DevDefaults.TryGetValue(q.Key, out var d)) q.Value = d;
            QuickList.ItemsSource = null; QuickList.ItemsSource = _quick;
        }

        private async void ExtSave_Click(object sender, RoutedEventArgs e)
        {
            var v = EditPhp; if (v == null) return;
            var ini = PhpManager.ReadIni(v);
            var orig = ini;
            foreach (var x in _ext) ini = PhpManager.SetExtensionText(ini, x.Name, x.Enabled, x.IsZend);
            if (ini == orig) { UI.Msg("Değişiklik yok."); return; }
            PhpManager.WriteIni(v, ini);
            await AfterPhpChange(v, "PHP eklentileri uygulanıyor");
        }

        private async void IniSave_Click(object sender, RoutedEventArgs e)
        {
            var v = EditPhp; if (v == null) return;
            PhpManager.WriteIni(v, IniText.Text);
            await AfterPhpChange(v, "php.ini uygulanıyor");
        }

        private void IniReload_Click(object sender, RoutedEventArgs e) => LoadPhp();
        private void IniOpen_Click(object sender, RoutedEventArgs e) { if (EditPhp != null) ProcessRunner.StartDetached("notepad.exe", "\"" + PhpManager.IniPath(EditPhp) + "\""); }

        private async void IniRestore_Click(object sender, RoutedEventArgs e)
        {
            var v = EditPhp; if (v == null) return;
            var bak = PhpManager.IniPath(v) + ".bak";
            if (!File.Exists(bak)) { UI.Msg("Yedek (.bak) bulunamadı."); return; }
            if (!UI.Confirm("php.ini son kaydetmeden önceki haline döndürülsün mü?")) return;
            File.Copy(bak, PhpManager.IniPath(v), true);
            await AfterPhpChange(v, "php.ini geri alınıyor");
        }

        // ==================================================================
        //  Web sunucu custom.conf
        // ==================================================================
        private bool EditingNginx => RbConfNginx.IsChecked == true;
        private string CustomConfPath => EditingNginx ? Paths.NginxCustomConf : Paths.ApacheCustomConf;

        private void ConfKind_Checked(object sender, RoutedEventArgs e) { if (IsLoaded) LoadConf(); }

        private void LoadConf()
        {
            var p = CustomConfPath;
            _confLoadedFor = p;
            try { ConfText.Text = File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : "# " + p + L.T(" (henüz oluşturulmadı — kaydedince oluşur)") + "\r\n"; }
            catch (Exception ex) { ConfText.Text = "# " + L.F("okunamadı: {0}", ex.Message); }
        }

        private async void ConfSave_Click(object sender, RoutedEventArgs e)
        {
            var p = _confLoadedFor ?? CustomConfPath;
            string old = File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : null;
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, ConfText.Text, new UTF8Encoding(false));
            bool isActive = EditingNginx == (Cfg.WebServer == "nginx");
            if (!isActive) { UI.Msg("Kaydedildi. Bu web sunucusu aktif olduğunda kullanılacak."); return; }

            var test = await Task.Run(() => EditingNginx ? NginxManager.TestConfig() : ApacheManager.TestConfig());
            bool ok = EditingNginx ? test.Ok : (test.Ok || test.AllOutput.IndexOf("Syntax OK", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!ok)
            {
                if (old != null) File.WriteAllText(p, old, new UTF8Encoding(false));
                UI.Err("Yapılandırma hatalı, değişiklik GERİ ALINDI (sunucu bozulmasın diye):\n\n" + test.AllOutput);
                LoadConf();
                return;
            }
            await Main.RunBusyAsync("Web sunucu yeniden başlatılıyor", async log =>
            {
                var errs = await Task.Run(() => Stack.RestartWeb(Cfg));
                if (errs.Count > 0) UI.Err(string.Join("\n\n", errs));
            });
        }

        private void ConfViewMain_Click(object sender, RoutedEventArgs e) => ProcessRunner.StartDetached("notepad.exe", "\"" + (EditingNginx ? Paths.NginxConf : Paths.ApacheConf) + "\"");

        private async void ConfTest_Click(object sender, RoutedEventArgs e)
        {
            if (EditingNginx ? !NginxManager.IsInstalled : !ApacheManager.IsInstalled) { UI.Warn("Bu web sunucusu kurulu değil."); return; }
            var r = await Task.Run(() => EditingNginx ? NginxManager.TestConfig() : ApacheManager.TestConfig());
            var ok = EditingNginx ? r.Ok : (r.Ok || r.AllOutput.IndexOf("Syntax OK", StringComparison.OrdinalIgnoreCase) >= 0);
            if (ok) UI.Msg("Yapılandırma geçerli.\n\n" + r.AllOutput); else UI.Err("Yapılandırma hatası:\n\n" + r.AllOutput);
        }

        private void ConfVhosts_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenFolder(EditingNginx ? Paths.EtcNginxVhosts : Paths.EtcApacheVhosts);

        // ==================================================================
        //  Veritabanı
        // ==================================================================
        private bool DbReady()
        {
            if (!WindowsServices.IsRunning(WindowsServices.Db)) { UI.Warn("Veritabanı çalışmıyor. Önce Genel Bakış'tan başlatın."); return false; }
            return true;
        }

        private async void SetRootPw_Click(object sender, RoutedEventArgs e)
        {
            if (!DbReady()) return;
            var pw = PbRoot.Password ?? "";
            if (!UI.Confirm(pw.Length == 0 ? "root şifresi KALDIRILACAK (boş). Devam?" : "root şifresi değiştirilecek. Devam?")) return;
            string err = null;
            await Main.RunBusyAsync("root şifresi ayarlanıyor", async log => { err = await Task.Run(() => DbManager.SetRootPassword(Cfg, pw)); });
            if (err != null) UI.Err(err); else { PbRoot.Password = ""; UI.Msg("root şifresi güncellendi. phpMyAdmin yapılandırması da güncellendi."); }
            Refresh();
        }

        private async void CreateDb_Click(object sender, RoutedEventArgs e)
        {
            if (!DbReady()) return;
            var name = (TbNewDb.Text ?? "").Trim();
            if (!Regex.IsMatch(name, @"^[A-Za-z0-9_\-]{1,64}$")) { UI.Warn("Veritabanı adı yalnızca harf, rakam, _ ve - içerebilir."); return; }
            var coll = Cfg.DbEngine == Comp.MySql ? "utf8mb4_0900_ai_ci" : "utf8mb4_unicode_ci";
            var r = await Task.Run(() => DbManager.Sql(Cfg, $"CREATE DATABASE IF NOT EXISTS `{name}` CHARACTER SET utf8mb4 COLLATE {coll}"));
            if (!r.Ok) UI.Err("Oluşturulamadı:\n" + r.AllOutput); else { TbNewDb.Text = ""; ListDb_Click(null, null); }
        }

        private async void ListDb_Click(object sender, RoutedEventArgs e)
        {
            if (!WindowsServices.IsRunning(WindowsServices.Db)) { DbList.Text = L.T("(veritabanı çalışmıyor)"); return; }
            var list = await Task.Run(() => DbManager.Databases(Cfg));
            DbList.Text = list.Count == 0 ? L.T("(bağlanılamadı)") : string.Join("   ", list);
        }

        private void Pma_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenUrl(UI.LocalhostUrl() + "phpmyadmin/");

        private async void Backup_Click(object sender, RoutedEventArgs e)
        {
            if (!DbReady()) return;
            string file = null;
            var ok = await Main.RunBusyAsync("Yedek alınıyor", async log => { file = await Task.Run(() => DbManager.Backup(Cfg, log)); });
            if (ok && file != null && UI.Confirm("Yedek alındı:\n" + file + "\n\nKlasör açılsın mı?")) ProcessRunner.StartDetached("explorer.exe", "/select,\"" + file + "\"");
        }

        private async void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (!DbReady()) return;
            var d = new Microsoft.Win32.OpenFileDialog { Filter = L.T("SQL dosyası (*.sql)|*.sql|Tüm dosyalar|*.*"), InitialDirectory = Paths.Backups };
            if (d.ShowDialog() != true) return;
            if (!UI.Confirm("Seçilen yedek geri yüklenecek. Aynı isimli veritabanlarındaki tablolar yedekteki haliyle DEĞİŞTİRİLİR. Devam?")) return;
            var ok = await Main.RunBusyAsync("Geri yükleniyor", async log => { await Task.Run(() => DbManager.Restore(Cfg, d.FileName, log)); });
            if (ok) UI.Msg("Geri yükleme tamamlandı.");
            ListDb_Click(null, null);
        }

        private void BackupFolder_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenFolder(Paths.Backups);

        // ---- PostgreSQL / SQLite / SQL Server ----
        private void RefreshOtherDbs()
        {
            var c = Cfg;
            bool pg = PostgreSqlManager.IsActive(c);
            PgActions.Visibility = pg ? Visibility.Visible : Visibility.Collapsed;
            PgInstallBtn.Visibility = pg ? Visibility.Collapsed : Visibility.Visible;
            PgInfo.Text = pg
                ? $"Sunucu: 127.0.0.1    Port: {c.PgPort}    Kullanıcı: postgres    Şifre: gerekmez\r\nSürüm: PostgreSQL {c.PgVersion}    Veri: {PostgreSqlManager.DataDir(c.PgVersion)}\r\nPHP: new PDO('pgsql:host=127.0.0.1;port={c.PgPort};dbname=postgres', 'postgres', '')"
                : "PostgreSQL kurulu değil. Sürümler > Veritabanı bölümünden kurabilirsiniz (MariaDB/MySQL ile birlikte çalışır).";
            var php = c.PhpVersion;
            string Ext(string n) => PhpManager.IsInstalled(php) && PhpManager.IsExtensionEnabledText(PhpManager.ReadIni(php), n) ? "açık" : "kapalı";
            OtherDbInfo.Text =
                $"SQLite      : sunucu gerekmez · pdo_sqlite {Ext("pdo_sqlite")} · sqlite3 {Ext("sqlite3")}\r\n" +
                $"PostgreSQL  : pdo_pgsql {Ext("pdo_pgsql")} · pgsql {Ext("pgsql")}\r\n" +
                $"SQL Server  : " + (SqlSrvManager.IsInstalled
                    ? $"pdo_sqlsrv {Ext("pdo_sqlsrv")} · sqlsrv {Ext("sqlsrv")} · ODBC Driver {(SqlSrvManager.OdbcInstalled ? "kurulu" : "YOK")}" + (PhpManager.IsInstalled(php) && !SqlSrvManager.Supports(php) ? $" · PHP {php} desteklenmiyor (8.1+)" : "")
                    : "sürücü kurulu değil (Sürümler > Araçlar > SQL Server sürücüsü)");
        }

        private bool PgReady()
        {
            if (!PostgreSqlManager.IsActive(Cfg)) { UI.Warn("PostgreSQL kurulu değil."); return false; }
            if (!WindowsServices.IsRunning(WindowsServices.PostgreSql)) { UI.Warn("PostgreSQL çalışmıyor. Önce Genel Bakış'tan başlatın."); return false; }
            return true;
        }

        private async void PgCreateDb_Click(object sender, RoutedEventArgs e)
        {
            if (!PgReady()) return;
            var name = (TbNewPgDb.Text ?? "").Trim();
            var err = await Task.Run(() => PostgreSqlManager.CreateDatabase(Cfg, name));
            if (err != null) UI.Err("Oluşturulamadı:\n" + err); else { TbNewPgDb.Text = ""; UI.Done("PostgreSQL veritabanı oluşturuldu: " + name); PgListDb_Click(null, null); }
        }

        private async void PgListDb_Click(object sender, RoutedEventArgs e)
        {
            if (!PostgreSqlManager.IsActive(Cfg) || !WindowsServices.IsRunning(WindowsServices.PostgreSql)) { PgDbList.Text = L.T("(PostgreSQL çalışmıyor)"); return; }
            var list = await Task.Run(() => PostgreSqlManager.Databases(Cfg));
            PgDbList.Text = list.Count == 0 ? L.T("(bağlanılamadı)") : string.Join("   ", list);
        }

        private void PgAdminer_Click(object sender, RoutedEventArgs e)
        {
            if (!AdminerManager.IsInstalled) { UI.Warn("Adminer kurulu değil. Sürümler > Araçlar > Adminer → Kur."); return; }
            ProcessRunner.OpenUrl(UI.LocalhostUrl() + "adminer/go.php?db=pgsql");
        }

        private async void PgBackup_Click(object sender, RoutedEventArgs e)
        {
            if (!PgReady()) return;
            string file = null;
            var ok = await Main.RunBusyAsync("PostgreSQL yedeği alınıyor", async log => { file = await Task.Run(() => PostgreSqlManager.Backup(Cfg, log)); });
            if (ok && file != null && UI.Confirm("Yedek alındı:\n" + file + "\n\nKlasör açılsın mı?")) ProcessRunner.StartDetached("explorer.exe", "/select,\"" + file + "\"");
        }

        private async void PgRestore_Click(object sender, RoutedEventArgs e)
        {
            if (!PgReady()) return;
            var d = new Microsoft.Win32.OpenFileDialog { Filter = L.T("SQL dosyası (*.sql)|*.sql|Tüm dosyalar|*.*"), InitialDirectory = Paths.Backups };
            if (d.ShowDialog() != true) return;
            if (!UI.Confirm("Seçilen yedek PostgreSQL'e geri yüklenecek. Yedekteki veritabanları silinip yedekteki haliyle yeniden oluşturulur. Devam?")) return;
            var ok = await Main.RunBusyAsync("PostgreSQL geri yükleniyor", async log => { await Task.Run(() => PostgreSqlManager.Restore(Cfg, d.FileName, log)); });
            if (ok) UI.Done("PostgreSQL geri yükleme tamamlandı.");
            PgListDb_Click(null, null);
        }

        private void PgCustom_Click(object sender, RoutedEventArgs e)
        {
            PostgreSqlManager.WriteConfig(Cfg); // custom.conf yoksa örnekle oluşturur
            ProcessRunner.OpenInEditor(PostgreSqlManager.CustomConf);
        }

        private void PgTerminal_Click(object sender, RoutedEventArgs e)
        {
            if (!PgReady()) return;
            UI.OpenTerminal(Cfg.EffectiveDocRoot, "psql");
        }

        private void GoVersions_Click(object sender, RoutedEventArgs e) => Main.Navigate("versions");

        private async void CnfSave_Click(object sender, RoutedEventArgs e)
        {
            var old = File.Exists(DbManager.CustomCnf) ? File.ReadAllText(DbManager.CustomCnf, Encoding.UTF8) : "";
            File.WriteAllText(DbManager.CustomCnf, CnfText.Text, new UTF8Encoding(false));
            ApplyResult res = null;
            await Main.RunBusyAsync("Veritabanı ayarları uygulanıyor", async log => { res = await Stack.ApplyAsync(Cfg, log, true); });
            if (res != null && !res.Ok)
            {
                if (UI.Confirm("Uygulama sırasında hata oluştu:\n\n" + string.Join("\n", res.Errors) + "\n\nÖnceki custom.cnf geri yüklensin mi? (önerilir)"))
                {
                    File.WriteAllText(DbManager.CustomCnf, old, new UTF8Encoding(false));
                    await Main.ApplyConfigAsync(true, "Önceki ayarlar geri yükleniyor");
                }
            }
            else if (res != null) Main.ShowApplyResult(res);
            Refresh();
        }

        private void MyIniView_Click(object sender, RoutedEventArgs e) => ProcessRunner.StartDetached("notepad.exe", "\"" + Paths.MyIni + "\"");

        // ==================================================================
        //  SSL
        // ==================================================================
        private async void TrustCa_Click(object sender, RoutedEventArgs e)
        {
            if (!MkcertManager.IsInstalled) { UI.Warn("mkcert kurulu değil (Sürümler > Araçlar)."); return; }
            bool ok = false;
            await Main.RunBusyAsync("Kök sertifika ekleniyor", async log => { ok = await Task.Run(() => MkcertManager.InstallCa(log)); });
            if (!ok) UI.Err("Kök sertifika eklenemedi. Ayrıntı: " + Paths.AppLog);
            RefreshSslInfo();
        }

        private async void RegenCert_Click(object sender, RoutedEventArgs e)
        {
            if (!MkcertManager.IsInstalled) { UI.Warn("mkcert kurulu değil (Sürümler > Araçlar)."); return; }
            await Main.RunBusyAsync("SSL sertifikası yeniden üretiliyor", async log =>
            {
                var ok = await Task.Run(() => MkcertManager.EnsureCerts(Cfg, log, true));
                if (!ok) { UI.Err("Sertifika üretilemedi. Ayrıntı: " + Paths.AppLog); return; }
                var errs = await Task.Run(() => Stack.RestartWeb(Cfg));
                if (errs.Count > 0) UI.Err(string.Join("\n\n", errs));
            });
            RefreshSslInfo();
        }

        private void UntrustCa_Click(object sender, RoutedEventArgs e)
        {
            if (!UI.Confirm("Yerel kök sertifika Windows'tan kaldırılacak; https://localhost tarayıcıda 'güvenli değil' uyarısı verir. Devam?")) return;
            if (!MkcertManager.UninstallCa()) UI.Err("Kaldırılamadı. Ayrıntı: " + Paths.AppLog);
            RefreshSslInfo();
        }

        private void SslFolder_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenFolder(Paths.EtcSsl);
        private void HostsOpen_Click(object sender, RoutedEventArgs e) => ProcessRunner.StartDetached("notepad.exe", "\"" + Paths.HostsFile + "\"");

        // ==================================================================
        //  Sistem
        // ==================================================================
        private void OpenRoot_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenFolder(Paths.Root);
        private void OpenDocs_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenFolder(Cfg.EffectiveDocRoot);
        private void OpenBin_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenFolder(Paths.Bin);
        private void OpenData_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenFolder(Paths.Data);
        private void OpenEtc_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenFolder(Paths.Etc);
        private void OpenLogs_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenFolder(Paths.Logs);
        private void OpenConfig_Click(object sender, RoutedEventArgs e) => ProcessRunner.StartDetached("notepad.exe", "\"" + Paths.ConfigFile + "\"");

        private void Reset_Click(object sender, RoutedEventArgs e) => Main.OpenResetWindow();

        // ---- tema ----
        private bool _loadingTheme;
        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_loadingTheme || !IsLoaded) return;
            var mode = RbThemeDark.IsChecked == true ? "dark" : RbThemeLight.IsChecked == true ? "light" : "system";
            ThemeManager.SetMode(mode);
        }

        // ---- dil ----
        private bool _loadingLang;
        private void Language_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingLang || !IsLoaded) return;
            var setting = (CbLanguage.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            var cfg = AppConfig.Current;
            if (setting == (cfg.Language ?? "")) return;
            if (L.Resolve(setting) == L.Code)
            {
                // görünen dil değişmiyor (ör. "cihazın dili" → aynı dil): yalnız kaydet
                cfg.Language = setting; cfg.Save();
                return;
            }
            // soru, seçim olayı bittikten sonra (açılır liste kapanınca) ve yeni dilde sorulur
            Dispatcher.BeginInvoke(new Action(() => AskLanguage(setting)), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void AskLanguage(string setting)
        {
            var cfg = AppConfig.Current;
            var en = L.Resolve(setting) == L.English;
            var msg = en
                ? "DEVNANOTEK will close and reopen in English.\n\nYour services and projects are not affected. Continue?"
                : "DEVNANOTEK kapanıp Türkçe olarak yeniden açılacak.\n\nServisleriniz ve projeleriniz etkilenmez. Devam edilsin mi?";
            if (!UI.ConfirmRaw(msg))
            {
                _loadingLang = true;
                CbLanguage.SelectedItem = CbLanguage.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string ?? "") == (cfg.Language ?? "")) ?? LangAuto;
                _loadingLang = false;
                return;
            }
            cfg.Language = setting; cfg.Save();
            Logger.Info("Arayüz dili değişti: " + (setting == "" ? "cihazın dili" : setting));
            Main.RestartForLanguage("settings");
        }

        // ---- Defender istisnası (isteğe bağlı hızlandırma) ----
        private bool _defenderOn;
        private async void RefreshDefender()
        {
            DefenderBtn.IsEnabled = false;
            DefenderBtn.Content = "Denetleniyor…";
            DefenderInfo.Text = "";
            _defenderOn = await Task.Run(() => DefenderManager.IsExcluded());
            DefenderBtn.Content = _defenderOn ? "Defender istisnasını kaldır" : "Defender taramasından hariç tut";
            DefenderInfo.Text = _defenderOn ? "Açık — C:\\devnanotek taranmıyor." : "Kapalı (varsayılan).";
            DefenderBtn.IsEnabled = true;
        }

        private async void Defender_Click(object sender, RoutedEventArgs e)
        {
            bool on = !_defenderOn;
            if (on && !UI.Confirm("C:\\devnanotek klasörü Windows Defender taramasından hariç tutulacak. phpMyAdmin ve büyük projelerin ilk açılışı hızlanır.\n\nYalnızca güvendiğiniz kodları bu klasöre koyun. Devam edilsin mi?")) return;
            string err = null;
            await Main.RunBusyAsync(on ? "Defender istisnası ekleniyor" : "Defender istisnası kaldırılıyor", async log => { err = await Task.Run(() => DefenderManager.SetExcluded(on)); });
            if (err != null) UI.Err(err); else UI.Done(on ? "C:\\devnanotek artık Defender taramasından hariç." : "Defender istisnası kaldırıldı.");
            RefreshDefender();
        }

        private bool _loadingRunMode;

        private void RunMode_Checked(object sender, RoutedEventArgs e)
        {
            if (_loadingRunMode) return;
            bool always = RbRunAlways.IsChecked == true;
            CbAutoServices.IsChecked = always;
            CbAppWithWindows.IsChecked = always;
            CbStartOnLaunch.IsChecked = true;
            CbStopOnExit.IsChecked = !always;
        }

        private void Landing_Click(object sender, RoutedEventArgs e)
        {
            var index = Path.Combine(Cfg.EffectiveDocRoot, "index.php");
            if (File.Exists(index) && !UI.Confirm(index + " dosyasının üzerine DEVNANOTEK açılış sayfası yazılacak. Devam?")) return;
            Stack.EnsureLandingPage(Cfg, true);
            ProcessRunner.OpenUrl(UI.LocalhostUrl());
        }

        private async void PortCheck_Click(object sender, RoutedEventArgs e)
        {
            var c = Cfg;
            var text = await Task.Run(() =>
            {
                var sb = new StringBuilder();
                var ports = new List<Tuple<string, int>>
                {
                    Tuple.Create("HTTP", c.HttpPort), Tuple.Create("HTTPS", c.HttpsPort), Tuple.Create(L.T("Veritabanı"), c.DbPort),
                    Tuple.Create("SMTP", c.SmtpPort), Tuple.Create("Mailpit", c.MailpitUiPort)
                };
                if (PostgreSqlManager.IsActive(c)) ports.Add(Tuple.Create("PostgreSQL", c.PgPort));
                if (c.WebServer == "nginx") for (int i = 0; i < c.FcgiChildren; i++) ports.Add(Tuple.Create("FastCGI", c.FcgiPort + i));
                foreach (var p in ports)
                {
                    var o = PortUtil.WhoListens(p.Item2);
                    sb.AppendLine($"{p.Item1,-12} {p.Item2,-6} {(o == null ? L.Pick("boş", "free") : o.ProcessName + " (PID " + o.Pid + ")" + (o.ProcessPath != null ? "  " + o.ProcessPath : ""))}");
                }
                var conflicts = PortUtil.Conflicts(c, new[] { "httpd", "nginx", "mysqld", "mariadbd", "postgres", "mailpit", "php-cgi", "DevNanotek", "DevNanotek-fcgi" });
                if (conflicts.Count > 0) { sb.AppendLine(); sb.AppendLine(L.T("ÇAKIŞMALAR:")); foreach (var m in conflicts) sb.AppendLine("• " + L.T(m)); }
                else { sb.AppendLine(); sb.AppendLine(L.T("Çakışma yok.")); }
                return sb.ToString();
            });
            UI.Msg(text);
        }

        private void Uninstall_Click(object sender, RoutedEventArgs e) => Main.OpenUninstallWindow();
    }
}
