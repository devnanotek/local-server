using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DevNanotek.Core;

namespace DevNanotek.Views
{
    /// <summary>Genel Bakış satırı: servis + port + açıklama + eylem.</summary>
    public class DashRow
    {
        public ServiceRow Row { get; set; }
        public string Ports { get; set; }
        public string Info { get; set; }
        public string ActionText => !Row.Installed ? "Kur" : Row.Running ? "Durdur" : "Başlat";
    }

    public partial class DashboardView : ViewBase
    {
        private bool _loading;
        private StatusReport _report;

        public DashboardView() { InitializeComponent(); }

        public override void Refresh()
        {
            var cfg = Cfg;
            _loading = true;
            ModeSwitch.IsChecked = cfg.AutoStartServices;
            _loading = false;
            ModeTip.Text = "AÇIK: Servisler bilgisayar açılınca kendiliğinden başlar; program kapalıyken de siteler çalışır.\n\n" +
                           "KAPALI: Hiçbir şey kendiliğinden başlamaz. DEVNANOTEK'i açınca servisler başlar, tepsiden Çıkış yapınca durur.";
            SacBanner.Visibility = SystemCheck.SmartAppControlOn ? Visibility.Visible : Visibility.Collapsed;
            HttpsBtn.Visibility = cfg.SslEnabled ? Visibility.Visible : Visibility.Collapsed;
            MailBtn.Visibility = cfg.MailpitEnabled ? Visibility.Visible : Visibility.Collapsed;
            AdminerBtn.Visibility = AdminerManager.IsInstalled ? Visibility.Visible : Visibility.Collapsed;

            var php = PhpManager.InstalledVersions();
            PhpCombo.ItemsSource = php;
            PhpCombo.SelectedItem = php.Contains(cfg.PhpVersion) ? cfg.PhpVersion : php.FirstOrDefault();
            var node = NodeManager.Installed();
            NodeCombo.ItemsSource = node;
            NodeCombo.SelectedItem = node.Contains(cfg.NodeVersion) ? cfg.NodeVersion : node.FirstOrDefault();
            UpdateUpdatesChip();
        }

        private void UpdateUpdatesChip()
        {
            var n = UpdateChecker.Current.Count;
            UpdateChip.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateChip.Content = n + " güncelleme var →";
        }

        public override void OnStatus(StackStatus s)
        {
            var r = s.Report;
            if (r == null) return;
            _report = r;
            var cfg = Cfg;
            OverallDot.Fill = StatusColors.BrushOf(r.Overall);
            OverallText.Text = r.OverallText;
            VersionsLine.Text = string.IsNullOrEmpty(r.VersionsLine) ? "Henüz bileşen kurulmadı." : r.VersionsLine;

            ServiceRows.ItemsSource = r.Rows.Select(row => new DashRow { Row = row, Ports = PortsOf(row, cfg), Info = InfoOf(row, cfg) }).ToList();
            VersionChips.ItemsSource = r.Versions.Where(v => !string.IsNullOrEmpty(v.Version) && v.Name != "PHP" && v.Name != "Node.js" && !r.Rows.Any(x => x.Title == v.Name)).ToList();

            bool allOk = r.Overall == Level.Ok;
            MainToggleText.Text = allOk ? "Durdur" : "Başlat";
            MainToggleIcon.Text = allOk ? "" : "";
            Summary.Text = r.Overall == Level.Ok ? "Her şey çalışıyor. Projeleriniz http://localhost altında hazır."
                         : r.Overall == Level.Error ? "Bir serviste sorun var (kırmızı). Günlüğe bakın veya Onar / Sıfırla'yı deneyin."
                         : r.Overall == Level.Off ? "Henüz kurulum yapılmadı. Sürümler sayfasından Kurulum Sihirbazı'nı açın."
                         : "Servisler durdu. Başlatmak için sağ üstteki Başlat'a tıklayın.";
            UpdateUpdatesChip();
        }

        private static string PortsOf(ServiceRow r, AppConfig c)
        {
            switch (r.Key)
            {
                case "web": return "  ·  :" + c.HttpPort + (c.SslEnabled ? "  ·  :" + c.HttpsPort : "");
                case "fcgi": return "  ·  :" + c.FcgiPort + "–" + (c.FcgiPort + c.FcgiChildren - 1);
                case "db": return "  ·  :" + c.DbPort;
                case "pg": return "  ·  :" + c.PgPort;
                case "mail": return "  ·  SMTP :" + c.SmtpPort + "  ·  arayüz :" + c.MailpitUiPort;
                default: return "";
            }
        }

        private static string InfoOf(ServiceRow r, AppConfig c)
        {
            switch (r.Key)
            {
                case "web": return $"Adres: {UI.LocalhostUrl()}\nBelge kökü: {c.EffectiveDocRoot}\n" + (c.LanAccess ? "Yerel ağdan erişime AÇIK." : "Yalnızca bu bilgisayardan erişilebilir.");
                case "fcgi": return "Nginx için PHP'yi çalıştıran php-cgi havuzu.";
                case "db":
                    return $"Bağlantı (Navicat, HeidiSQL, PHP):\nSunucu 127.0.0.1 · Port {c.DbPort}\nKullanıcı root · Şifre {(string.IsNullOrEmpty(c.DbRootPassword) ? "boş" : "Ayarlar'da tanımlı")}\nVeri: {DbManager.DataDir(c.DbEngine, c.DbVersion)}";
                case "pg":
                    return $"Bağlantı (DBeaver, Navicat, pgAdmin, PHP):\nSunucu 127.0.0.1 · Port {c.PgPort}\nKullanıcı postgres · Şifre gerekmez (yalnız bu bilgisayardan)\nVeri: {PostgreSqlManager.DataDir(c.PgVersion)}";
                case "mail": return $"PHP mail() ve SMTP 127.0.0.1:{c.SmtpPort} ile gönderilen e-postalar gerçek alıcıya gitmez; {MailpitManager.UiUrl(c)} adresinde görünür.";
                default: return "";
            }
        }

        // ---- üst düğmeler ----
        private async void MainToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_report?.Overall == Level.Ok)
                await Main.RunBusyAsync("Servisler durduruluyor", async log =>
                {
                    var errs = await Stack.StopAllAsync(Cfg, log);
                    if (errs.Count > 0) throw new Exception(string.Join("\n", errs));
                }, false, "Servisler durduruldu.");
            else
                await Main.RunBusyAsync("Servisler başlatılıyor", async log =>
                {
                    var errs = await Stack.StartAllAsync(Cfg, log);
                    if (errs.Count > 0) throw new Exception(string.Join("\n\n", errs));
                }, false, "Servisler başlatıldı.");
        }

        private async void RestartAll_Click(object sender, RoutedEventArgs e)
            => await Main.RunBusyAsync("Servisler yeniden başlatılıyor", async log =>
            {
                await Stack.StopAllAsync(Cfg, log);
                var errs = await Stack.StartAllAsync(Cfg, log);
                if (errs.Count > 0) throw new Exception(string.Join("\n\n", errs));
            }, false, "Servisler yeniden başlatıldı.");

        private void Reset_Click(object sender, RoutedEventArgs e) => Main.OpenResetWindow();
        private void SacInfo_Click(object sender, RoutedEventArgs e) => UI.Warn(SystemCheck.SmartAppControlHelp);
        private void Updates_Click(object sender, RoutedEventArgs e) => Main.Navigate("versions");
        private void Versions_Click(object sender, RoutedEventArgs e) => Main.Navigate("versions");

        private async void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || !IsLoaded) return;
            bool always = ModeSwitch.IsChecked == true;
            Cfg.SetRunMode(always);
            Cfg.Save();
            await Main.RunBusyAsync("Çalışma modu uygulanıyor", async log => await Task.Run(() => Stack.ApplyStartMode(Cfg)),
                false, always ? "Servisler artık bilgisayar açılınca başlayacak." : "Servisler yalnızca siz DEVNANOTEK'i açınca çalışacak.");
        }

        // ---- servis satırı eylemleri ----
        private async void RowToggle_Click(object sender, RoutedEventArgs e)
        {
            var d = (sender as Button)?.Tag as DashRow;
            if (d == null) return;
            var row = d.Row;
            if (!row.Installed) { Main.Navigate("versions"); return; }
            var cfg = Cfg;
            if (!row.ServiceExists) { await Main.ApplyConfigAsync(true, row.Title + " kuruluyor"); return; }
            bool stop = row.Running;
            await Main.RunBusyAsync((stop ? "Durduruluyor: " : "Başlatılıyor: ") + row.TitleWithVersion, async log =>
            {
                var err = await Task.Run(() =>
                {
                    if (stop)
                    {
                        if (!WindowsServices.Stop(row.Service)) return row.Title + " durdurulamadı.";
                        if (row.Key == "web" && cfg.WebServer == "nginx") WindowsServices.Stop(WindowsServices.PhpFcgi);
                        return null;
                    }
                    if (row.Key == "web" && cfg.WebServer == "nginx") WindowsServices.Start(WindowsServices.PhpFcgi);
                    var ok = WindowsServices.Start(row.Service);
                    if (ok && row.Key == "web") WarmUp.Run(cfg);
                    return ok ? null : row.Title + " başlatılamadı.\n\n" + Stack.HintFor(row.Service) + SystemCheck.BlockedHint();
                });
                Stack.NotifyChanged();
                if (err != null) throw new Exception(err);
            }, false, row.Title + (stop ? " durduruldu." : " çalışıyor."));
        }

        private void RowLog_Click(object sender, RoutedEventArgs e)
        {
            var d = (sender as Button)?.Tag as DashRow;
            if (d == null) return;
            string file;
            switch (d.Row.Key)
            {
                case "web": file = Path.Combine(Cfg.WebServer == "nginx" ? Paths.LogsNginx : Paths.LogsApache, "error.log"); break;
                case "db": file = Path.Combine(Paths.LogsDb, "error.log"); break;
                case "pg": file = PostgreSqlManager.LatestLog() ?? Path.Combine(Paths.LogsPostgreSql, "postgresql.log"); break;
                case "mail": file = Path.Combine(Paths.LogsMailpit, WindowsServices.Mailpit + ".err.log"); break;
                default: file = Path.Combine(Paths.LogsWinSw, WindowsServices.PhpFcgi + ".out.log"); break;
            }
            LogsView.RequestPath = file;
            Main.Navigate("logs");
        }

        private void RowSettings_Click(object sender, RoutedEventArgs e)
        {
            var d = (sender as Button)?.Tag as DashRow;
            if (d == null) return;
            switch (d.Row.Key)
            {
                case "db":
                case "pg": SettingsView.RequestTab = "db"; break;
                case "mail": ProcessRunner.OpenUrl(MailpitManager.UiUrl(Cfg)); return;
                default: SettingsView.RequestTab = "web"; break;
            }
            Main.Navigate("settings");
        }

        // ---- PHP / Node ----
        private async void PhpSwitch_Click(object sender, RoutedEventArgs e)
        {
            var v = PhpCombo.SelectedItem as string;
            if (v == null) return;
            if (v == Cfg.PhpVersion) { Main.Toast(Level.Off, "PHP " + v + " zaten aktif."); return; }
            Cfg.PhpVersion = v; Cfg.Save();
            await Main.ApplyConfigAsync(true, $"PHP {v} sürümüne geçiliyor");
        }

        private async void NodeSwitch_Click(object sender, RoutedEventArgs e)
        {
            var v = NodeCombo.SelectedItem as string;
            if (v == null) return;
            Cfg.NodeVersion = v; Cfg.Save();
            await Main.RunBusyAsync($"Node.js {v} aktif ediliyor", async log =>
            {
                await Task.Run(() => { EnvPath.SetJunction(EnvPath.CurrentNode, NodeManager.Dir(v)); EnvPath.WriteShims(Cfg); });
            }, false, "Node.js " + v + " aktif (yeni açılan terminallerde geçerli).");
        }

        // ---- PHP eklentileri (hızlı erişim) ----
        private void ExtOpen_Click(object sender, RoutedEventArgs e)
        {
            var v = Cfg.PhpVersion;
            if (!PhpManager.IsInstalled(v)) { Main.Toast(Level.Warn, "PHP kurulu değil."); return; }
            ExtTitle.Text = "PHP " + v + " eklentileri";
            ExtSwitches.Load(v);
            ExtPopup.IsOpen = true;
        }

        private void ExtClose_Click(object sender, RoutedEventArgs e) => ExtPopup.IsOpen = false;
        private void ExtAll_Click(object sender, RoutedEventArgs e) { ExtPopup.IsOpen = false; SettingsView.RequestTab = "php"; Main.Navigate("settings"); }

        private async void ExtSave_Click(object sender, RoutedEventArgs e)
        {
            var changed = ExtSwitches.Changed;
            ExtPopup.IsOpen = false;
            if (changed.Count == 0) { Main.Toast(Level.Off, "Değişiklik yok."); return; }
            var v = ExtSwitches.Version;
            var names = string.Join(", ", changed.Select(x => (x.Enabled ? "+" : "−") + x.Name));
            ExtSwitches.Save();
            // yüklenemeyen eklenti olursa php.ini'de kapatılır (sayfalarda uyarı çıkmasın)
            var off = await Task.Run(() => PhpManager.SelfHeal(v));
            await Main.RunBusyAsync("PHP eklentileri uygulanıyor", async log =>
            {
                var errs = await Task.Run(() => Stack.RestartWeb(Cfg));
                if (errs.Count > 0) throw new Exception(string.Join("\n\n", errs));
            }, false, off.Count > 0 ? null : "Eklentiler uygulandı: " + names);
            if (off.Count > 0) Main.Toast(Level.Warn, "Yüklenemeyen eklenti kapatıldı: " + string.Join(", ", off), 8000);
        }

        private void PhpIni_Click(object sender, RoutedEventArgs e) { SettingsView.RequestTab = "php"; Main.Navigate("settings"); }
        private void PhpInfo_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenUrl(UI.LocalhostUrl() + "?phpinfo=1");

        // ---- bağlantılar ----
        private void OpenLocalhost_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenUrl(UI.LocalhostUrl());
        private void OpenHttps_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenUrl(UI.LocalhostHttpsUrl());
        private void Pma_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenUrl(UI.LocalhostUrl() + "phpmyadmin/");
        private void Adminer_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenUrl(UI.LocalhostUrl() + "adminer/");
        private void MailUi_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenUrl(MailpitManager.UiUrl(Cfg));
        private void OpenDocroot_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenFolder(Cfg.EffectiveDocRoot);
        private void Terminal_Click(object sender, RoutedEventArgs e) => UI.OpenTerminal(Cfg.EffectiveDocRoot);
    }
}
