using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using DevNanotek.Core;

namespace DevNanotek.Views
{
    /// <summary>Kaldırma penceresi (Denetim Masası "Kaldır" veya Ayarlar > Sistem'den açılır).</summary>
    public partial class UninstallWindow : Window
    {
        private bool _running, _done, _deleteAll;

        public UninstallWindow()
        {
            InitializeComponent();
            var root = Paths.Root;
            KeepInfo.Text = $"Silinir: programlar (PHP, Apache, MariaDB…), ayar dosyaları, günlükler, indirilen dosyalar ve DevNanotek.exe.\n" +
                            $"Kalır: projeleriniz ({Paths.HttpDocs}), veritabanlarınız ({Paths.Data}) ve yedekler ({Paths.Backups}). " +
                            "DEVNANOTEK'i tekrar kurarsanız kaldığınız yerden devam edersiniz.";
            AllInfo.Text = $"{root} klasörü TAMAMEN silinir: projeler, veritabanları ve yedekler dahil. Bu işlem geri alınamaz." +
                           (string.IsNullOrWhiteSpace(AppConfig.Current.DocumentRoot) ? "" : $"\nNot: Ayrı seçtiğiniz belge kökü ({AppConfig.Current.DocumentRoot}) silinmez.");
            if (!Uninstaller.IsSafeRoot(root)) { RbAll.IsEnabled = false; AllInfo.Text = "Bu kök klasör (" + root + ") güvenlik nedeniyle otomatik silinemez."; }
        }

        private void RbAll_Changed(object sender, RoutedEventArgs e) { if (CbDump != null) CbDump.IsEnabled = RbAll.IsChecked == true; }

        private void Log(string s) => Dispatcher.BeginInvoke(new Action(() => LogBox.Append(s)));

        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            _deleteAll = RbAll.IsChecked == true;
            bool dump = _deleteAll && CbDump.IsChecked == true;
            var msg = _deleteAll
                ? $"{Paths.Root} klasörü projeleriniz ve veritabanlarınız DAHİL tamamen silinecek.\n\nBu işlem GERİ ALINAMAZ. Emin misiniz?"
                : "DEVNANOTEK kaldırılacak. Projeleriniz ve veritabanlarınız korunacak.\n\nDevam edilsin mi?";
            if (!UI.Confirm(msg)) return;
            if (_deleteAll && !UI.Confirm("Son onay: projeler ve veritabanları dahil HER ŞEY silinsin mi?")) return;

            _running = true;
            ChoicePanel.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            BtnRun.Visibility = Visibility.Collapsed;
            BtnCancel.Visibility = Visibility.Collapsed;

            ApplyResult res;
            try { res = await Task.Run(() => Uninstaller.Run(_deleteAll, dump, Log)); }
            catch (Exception ex) { res = new ApplyResult(); res.Errors.Add(ex.Message); Logger.Error("Kaldırma", ex); }
            _running = false;
            _done = res.Ok;
            Bar.IsIndeterminate = false; Bar.Value = 100;
            foreach (var w in res.Warnings) Log("⚠ " + w);
            foreach (var er in res.Errors) Log("✖ " + er);
            StepTitle.Text = res.Ok ? "✔ DEVNANOTEK kaldırıldı. Kapattığınızda son dosyalar da silinir." : "Kaldırma tamamlanamadı (ayrıntılar aşağıda)";
            BtnFinish.Visibility = Visibility.Visible;
            if (!res.Ok) BtnCancel.Visibility = Visibility.Visible;
        }

        private void Finish_Click(object sender, RoutedEventArgs e) => Close();
        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_running) { UI.Msg("Kaldırma sürüyor, lütfen bekleyin."); e.Cancel = true; return; }
            if (_done)
            {
                // program kapanırken app klasörü (ve gerekirse tüm kök) silinsin
                MainWindow.Instance?.PrepareForShutdown();
                Uninstaller.ScheduleFinalCleanup(_deleteAll);
                Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
            }
            else if (Owner == null)
            {
                // --uninstall ile tek başına açıldıysa ve vazgeçildiyse programı kapat
                Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
            }
        }
    }
}
