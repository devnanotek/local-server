using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using DevNanotek.Core;

namespace DevNanotek.Views
{
    public partial class ResetWindow : Window
    {
        private bool _running;
        private CancellationTokenSource _cts;

        /// <summary>Fabrika ayarlarına dönüldüyse true (çağıran program yeniden başlatılmalı).</summary>
        public bool FactoryResetDone { get; private set; }

        public ResetWindow()
        {
            InitializeComponent();
            BackupInfo.Text = "Yedekler: " + Paths.Backups + "\\sifirlama-TARİH klasörüne alınır.";
        }

        private ResetKind Selected =>
            RbFull.IsChecked == true ? ResetKind.Full :
            RbDb.IsChecked == true ? ResetKind.Database :
            RbFactory.IsChecked == true ? ResetKind.Factory : ResetKind.Quick;

        private void Log(string s)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LogBox.Append(s);
                StepText.Text = s;
            }));
        }

        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            var kind = Selected;
            string confirm;
            switch (kind)
            {
                case ResetKind.Quick: confirm = "Servisler kısa süre duracak, yeniden kurulup başlatılacak. Devam edilsin mi?"; break;
                case ResetKind.Full: confirm = "Kullandığınız tüm programlar silinip sıfırdan kurulacak (internetten indirme gerekebilir). php.ini ve custom ayar dosyaları varsayılana dönecek; eskileri yedeklenecek. Veritabanları ve projeler korunur.\n\nDevam edilsin mi?"; break;
                case ResetKind.Database: confirm = "VERİTABANI SIFIRLANACAK.\n\nTüm veritabanları yedeklenip kaldırılacak ve boş bir veritabanı oluşturulacak. Yedekten geri yükleyene kadar eski veritabanlarınız görünmez.\n\nDevam edilsin mi?"; break;
                default: confirm = "FABRİKA AYARLARINA DÖNÜLECEK.\n\nTüm servisler ve programlar kaldırılacak; veritabanları ve ayarlar yedek klasörüne taşınacak. Projeleriniz (httpdocs) korunur. Ardından program yeniden açılıp kurulum sihirbazını başlatacak.\n\nDevam edilsin mi?"; break;
            }
            if (!UI.Confirm(confirm)) return;
            if (kind == ResetKind.Factory && !UI.Confirm("Son onay: fabrika ayarlarına dönmek istediğinizden emin misiniz?")) return;

            _running = true;
            _cts = new CancellationTokenSource();
            ChoicePanel.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            BtnRun.IsEnabled = false;
            BtnClose.IsEnabled = false;
            StepTitle.Text = ResetManager.Title(kind) + " yapılıyor…";

            var progress = new Progress<DownloadProgress>(p =>
            {
                Bar.IsIndeterminate = p.Indeterminate;
                if (!p.Indeterminate) Bar.Value = p.Percent;
                if (!string.IsNullOrEmpty(p.Status)) StepText.Text = p.Status;
            });

            ApplyResult res = null;
            try
            {
                res = await ResetManager.RunAsync(kind, AppConfig.Current, Log, progress, _cts.Token, CbDeleteDownloads.IsChecked == true);
            }
            catch (Exception ex)
            {
                Logger.Error("Sıfırlama", ex);
                res = new ApplyResult();
                res.Errors.Add(ex.Message);
            }
            finally
            {
                _running = false;
                Bar.IsIndeterminate = false; Bar.Value = 100;
                BtnClose.IsEnabled = true;
                Stack.NotifyChanged();
            }

            foreach (var w in res.Warnings) Log("⚠ " + w);
            foreach (var er in res.Errors) Log("✖ " + er);
            StepTitle.Text = res.Ok ? "✔ " + ResetManager.Title(kind) + " tamamlandı" : ResetManager.Title(kind) + " tamamlandı, ancak hatalar var (ayrıntılar aşağıda)";

            if (kind == ResetKind.Factory)
            {
                FactoryResetDone = true;
                BtnClose.Content = "Programı yeniden başlat";
            }
        }

        private void Backups_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenFolder(Paths.Backups);
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_running) { UI.Msg("İşlem sürüyor, lütfen bitmesini bekleyin."); e.Cancel = true; }
        }
    }
}
