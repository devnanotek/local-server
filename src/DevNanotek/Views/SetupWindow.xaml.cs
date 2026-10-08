using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DevNanotek.Core;

namespace DevNanotek.Views
{
    public class SetupChoice
    {
        public CatalogEntry Entry { get; set; }
        public string Display { get; set; }
    }

    public partial class SetupWindow : Window
    {
        private CancellationTokenSource _cts;
        private bool _running;

        public SetupWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => Init();
        }

        private static string Label(CatalogEntry e, bool installed)
            => e.Title + (installed ? "   · kurulu" : e.Recommended ? "   · önerilen" : "") + (string.IsNullOrEmpty(e.Note) || e.Recommended || e.Note == "Önerilen" ? "" : "   · " + e.Note);

        private void Init()
        {
            var cat = Catalog.Current;
            var cfg = AppConfig.Current;
            SysText.Text = "Yerel sunucu kurulumu  ·  " + SystemInfo.Summary;

            List<SetupChoice> Choices(params string[] comps) => comps.SelectMany(c => cat.AvailableFor(c))
                .Select(e => new SetupChoice { Entry = e, Display = Label(e, Installer.IsInstalled(e)) }).ToList();

            // bu bilgisayarda yayınlanmayan bileşenler (32 bit Windows)
            var missing = new List<string>();
            if (!cat.AvailableFor(Comp.MariaDb).Any() && !cat.AvailableFor(Comp.MySql).Any()) missing.Add("MariaDB/MySQL");
            if (!cat.AvailableFor(Comp.Mailpit).Any()) { CbMail.IsChecked = false; CbMail.IsEnabled = false; missing.Add("Mailpit"); }
            if (!cat.AvailableFor(Comp.Mkcert).Any()) { CbSsl.IsChecked = false; CbSsl.IsEnabled = false; missing.Add("mkcert (HTTPS)"); }
            if (!cat.AvailableFor(Comp.PostgreSql).Any()) { CbPg.IsChecked = false; CbPg.IsEnabled = false; missing.Add("PostgreSQL"); }
            var notes = new List<string>();
            if (missing.Count > 0)
                notes.Add(string.Join(", ", missing) + " bu Windows (" + SystemInfo.ArchTitle + ") için yayınlanmıyor; diğer bileşenler kurulur.");
            if (!SystemInfo.IsWindows10OrLater)
                notes.Add("Bu Windows sürümü eski: güncel PHP, Apache ve MariaDB/MySQL sürümleri Windows 10 veya 11 ister, bazı bileşenler çalışmayabilir.");
            if (notes.Count > 0)
            {
                ArchNote.Text = string.Join("\n", notes);
                ArchNote.Visibility = Visibility.Visible;
            }

            var php = Choices(Comp.Php);
            CbPhp.ItemsSource = php;
            CbPhp.SelectedItem = php.FirstOrDefault(x => x.Entry.Version == cfg.PhpVersion) ?? php.FirstOrDefault(x => x.Entry.Recommended) ?? php.FirstOrDefault();

            var db = Choices(Comp.MariaDb, Comp.MySql);
            CbDb.ItemsSource = db;
            CbDb.SelectedItem = db.FirstOrDefault(x => x.Entry.Component == cfg.DbEngine && x.Entry.Version == cfg.DbVersion)
                                ?? db.FirstOrDefault(x => x.Entry.Component == Comp.MariaDb && x.Entry.Recommended) ?? db.FirstOrDefault();

            var node = Choices(Comp.Node);
            CbNodeVer.ItemsSource = node;
            CbNodeVer.SelectedItem = node.FirstOrDefault(x => x.Entry.Version == cfg.NodeVersion) ?? node.FirstOrDefault(x => x.Entry.Recommended) ?? node.FirstOrDefault();

            RbNginx.IsChecked = cfg.WebServer == "nginx";
            RbApache.IsChecked = cfg.WebServer != "nginx";
            RbAlways.IsChecked = cfg.AutoStartServices;
            RbManual.IsChecked = !cfg.AutoStartServices;
            CbPath.IsChecked = cfg.AddToPath;
            SizeInfo.Text = "İndirme boyutu yaklaşık 200 MB (MySQL seçilirse ~450 MB). İnternet bağlantısı gerekir.";
            if (SystemCheck.SmartAppControlOn)
            {
                SacText.Text = SystemCheck.SmartAppControlHelp;
                SacBox.Visibility = Visibility.Visible;
            }

            Task.Run(() => PortUtil.Conflicts(cfg, new[] { "httpd", "nginx", "mysqld", "mariadbd", "mailpit", "php-cgi" }))
                .ContinueWith(t =>
                {
                    if (t.Status != TaskStatus.RanToCompletion || t.Result.Count == 0) return;
                    PortWarn.Text = string.Join("\n", t.Result) + "\n\nKuruluma devam edebilirsiniz; çakışan programı kapatın ya da kurulumdan sonra Ayarlar > Genel'den port değiştirin.";
                    PortWarnBox.Visibility = Visibility.Visible;
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void Log(string s)
        {
            LogBox.Append(s);
            StepText.Text = s;
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            var php = (CbPhp.SelectedItem as SetupChoice)?.Entry;
            var db = (CbDb.SelectedItem as SetupChoice)?.Entry;
            var node = CbNode.IsChecked == true ? (CbNodeVer.SelectedItem as SetupChoice)?.Entry : null;
            if (php == null) { UI.Warn("PHP sürümü seçin."); return; }
            if (db == null && (Catalog.Current.AvailableFor(Comp.MariaDb).Any() || Catalog.Current.AvailableFor(Comp.MySql).Any())) { UI.Warn("Veritabanı sürümü seçin."); return; }
            bool nginx = RbNginx.IsChecked == true;

            var cat = Catalog.Current;
            var list = new List<CatalogEntry>();
            void Add(CatalogEntry x) { if (x != null && !list.Contains(x)) list.Add(x); }
            if (!VcRedist.IsAdequate()) Add(cat.Recommended(Comp.VcRedist));
            Add(cat.Recommended(Comp.WinSw));
            Add(cat.Recommended(Comp.CaCert));
            Add(php);
            Add(nginx ? cat.Recommended(Comp.Nginx) : cat.Recommended(Comp.Apache));
            Add(db);
            if (CbPma.IsChecked == true) Add(cat.Recommended(Comp.PhpMyAdmin));
            if (CbAdminer.IsChecked == true) Add(cat.Recommended(Comp.Adminer));
            if (CbPg.IsChecked == true) Add(cat.Recommended(Comp.PostgreSql));
            if (CbMail.IsChecked == true) Add(cat.Recommended(Comp.Mailpit));
            if (CbSsl.IsChecked == true) Add(cat.Recommended(Comp.Mkcert));
            if (CbComposer.IsChecked == true) Add(cat.Recommended(Comp.Composer));
            Add(node);
            var todo = list.Where(x => !Installer.IsInstalled(x)).ToList();

            _running = true;
            _cts = new CancellationTokenSource();
            ChoicePanel.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            BtnInstall.Visibility = Visibility.Collapsed;
            BtnSkip.Visibility = Visibility.Collapsed;
            BtnCancel.Visibility = Visibility.Visible;
            SizeInfo.Text = "";

            var cfg = AppConfig.Current;
            var failed = new List<string>();
            var failedEntries = new List<CatalogEntry>();
            try
            {
                for (int i = 0; i < todo.Count; i++)
                {
                    var entry = todo[i];
                    StepTitle.Text = $"({i + 1}/{todo.Count}) {entry.Title}";
                    TotalBar.Value = i * 100.0 / Math.Max(1, todo.Count);
                    TotalText.Text = $"Toplam: {i}/{todo.Count} bileşen tamamlandı";
                    Log("▶ " + entry.Title);
                    var progress = new Progress<DownloadProgress>(p =>
                    {
                        Bar.IsIndeterminate = p.Indeterminate;
                        if (!p.Indeterminate) Bar.Value = p.Percent;
                        if (!string.IsNullOrEmpty(p.Status)) StepText.Text = p.Status;
                    });
                    try
                    {
                        await Installer.InstallAsync(entry, cfg, progress, _cts.Token);
                        Log("  ✔ kuruldu");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        failedEntries.Add(entry);
                        failed.Add(entry.Title + ": " + ex.Message);
                        Log("  ✖ HATA: " + ex.Message);
                        Logger.Error("Kurulum: " + entry.Title, ex);
                    }
                }
                TotalBar.Value = 100;
                TotalText.Text = $"Toplam: {todo.Count}/{todo.Count} bileşen";

                // seçimleri yapılandırmaya yaz (kurulamayan/engellenen sürümler aktif yapılmaz)
                bool Usable(CatalogEntry x) => x != null && !failedEntries.Contains(x) && Installer.IsInstalled(x) && Installer.Probe(x) == null;
                if (Usable(php)) cfg.PhpVersion = php.Version;
                if (Usable(db)) { cfg.DbEngine = db.Component; cfg.DbVersion = db.Version; }
                if (Usable(node)) cfg.NodeVersion = node.Version;
                cfg.WebServer = nginx ? "nginx" : "apache";
                cfg.MailpitEnabled = CbMail.IsChecked == true;
                cfg.SslEnabled = CbSsl.IsChecked == true;
                cfg.SetRunMode(RbAlways.IsChecked == true);
                cfg.AddToPath = CbPath.IsChecked == true;
                cfg.SetupCompleted = true;
                cfg.Save();

                StepTitle.Text = "Servisler yapılandırılıp başlatılıyor…";
                Bar.IsIndeterminate = true;
                BtnCancel.IsEnabled = false;
                var res = await Stack.ApplyAsync(cfg, s => Dispatcher.BeginInvoke(new Action(() => Log(s))), true);
                Bar.IsIndeterminate = false; Bar.Value = 100;
                foreach (var w in res.Warnings) Log("⚠ " + w);
                foreach (var er in res.Errors) Log("✖ " + er);

                if (failed.Count == 0 && res.Ok)
                {
                    StepTitle.Text = "✔ Kurulum tamamlandı! Siteleriniz http://localhost adresinde.";
                    Log("");
                    Log("Projelerinizi C:\\devnanotek\\httpdocs içine koyun.  phpMyAdmin: http://localhost/phpmyadmin  (root / şifresiz)");
                }
                else
                {
                    StepTitle.Text = "Kurulum tamamlandı, ancak bazı sorunlar var (ayrıntılar aşağıda)";
                    foreach (var f in failed) Log("✖ " + f);
                    Log("Sorunlu bileşenleri daha sonra Sürümler sayfasından yeniden kurabilirsiniz.");
                }
            }
            catch (OperationCanceledException)
            {
                StepTitle.Text = "İptal edildi";
                Log("Kurulum iptal edildi. Daha sonra Sürümler sayfasından devam edebilirsiniz.");
            }
            finally
            {
                _running = false;
                BtnCancel.Visibility = Visibility.Collapsed;
                BtnDone.Visibility = Visibility.Visible;
                BtnOpen.Visibility = WindowsServices.IsRunning(WindowsServices.Apache) || WindowsServices.IsRunning(WindowsServices.Nginx) ? Visibility.Visible : Visibility.Collapsed;
                Stack.NotifyChanged();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_cts != null && UI.Confirm("Kurulum iptal edilsin mi?")) _cts.Cancel();
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            if (!AppConfig.Current.SetupCompleted)
                UI.Msg("Kurulumu daha sonra 'Sürümler' sayfasındaki 'Kurulum sihirbazı' düğmesiyle başlatabilirsiniz.");
            var cfg = AppConfig.Current;
            cfg.SetupCompleted = true;
            cfg.Save();
            Close();
        }

        private void Open_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenUrl(UI.LocalhostUrl());
        private void Done_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_running)
            {
                if (!UI.Confirm("Kurulum sürüyor. İptal edip kapatılsın mı?")) { e.Cancel = true; return; }
                _cts?.Cancel();
            }
        }
    }
}
