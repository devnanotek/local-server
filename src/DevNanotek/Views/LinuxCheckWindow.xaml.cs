using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using DevNanotek.Core;

namespace DevNanotek.Views
{
    /// <summary>Bir proje klasörünü Linux sunucuya (Plesk / cPanel / VPS) taşımadan önce tarar.</summary>
    public partial class LinuxCheckWindow : Window
    {
        private readonly string _root;
        private CompatReport _report;
        private CancellationTokenSource _cts;

        public LinuxCheckWindow(string root)
        {
            InitializeComponent();
            _root = root;
            RootText.Text = root;
            Loaded += async (s, e) => await RunScan();
        }

        private async Task RunScan()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            Busy.Visibility = Visibility.Visible;
            EmptyText.Visibility = Visibility.Collapsed;
            List.ItemsSource = null;
            SumDot.Fill = StatusColors.BrushOf(Level.Off);
            SumTitle.Text = "Taranıyor…";
            SumText.Text = "";
            BtnAgain.IsEnabled = BtnCopy.IsEnabled = BtnSave.IsEnabled = false;
            try
            {
                _report = await Task.Run(() => LinuxCompat.Scan(_root, s => Dispatcher.BeginInvoke(new Action(() => SumText.Text = s)), ct), ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { SumTitle.Text = "Taranamadı"; SumText.Text = ex.Message; Busy.Visibility = Visibility.Collapsed; BtnAgain.IsEnabled = true; return; }
            Busy.Visibility = Visibility.Collapsed;
            BtnAgain.IsEnabled = BtnCopy.IsEnabled = BtnSave.IsEnabled = true;
            ShowReport();
        }

        private void ShowReport()
        {
            var r = _report;
            if (r == null) return;
            var level = r.Errors > 0 ? Level.Error : r.Warnings > 0 ? Level.Warn : Level.Ok;
            SumDot.Fill = StatusColors.BrushOf(level);
            SumTitle.Text = r.Errors > 0 ? $"{r.Errors} sorun Linux'ta hata verir"
                          : r.Warnings > 0 ? $"Hata yok · {r.Warnings} uyarıya göz atın"
                          : "Linux'a hazır görünüyor";
            SumText.Text = $"{r.Errors} hata · {r.Warnings} uyarı · {r.Infos} bilgi · {r.FilesScanned} dosya tarandı" + (r.Truncated ? " (çok büyük proje: ilk 25.000 dosya)" : "");
            var items = r.Issues.Where(i => CbShowInfo.IsChecked == true || i.Level != Level.Off).ToList();
            List.ItemsSource = items;
            EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Filter_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) ShowReport(); }
        private async void Again_Click(object sender, RoutedEventArgs e) => await RunScan();

        private void List_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var i = List.SelectedItem as CompatIssue;
            if (i == null) return;
            if (File.Exists(i.File))
            {
                var code = UI.FindVsCode();
                if (code != null) ProcessRunner.StartDetached(code, $"--goto \"{i.File}:{Math.Max(1, i.Line)}\"");
                else ProcessRunner.OpenInEditor(i.File);
            }
            else if (Directory.Exists(i.File)) ProcessRunner.OpenFolder(i.File);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (_report == null) return;
            try { Clipboard.SetText(_report.ToText()); UI.Done("Rapor panoya kopyalandı."); } catch (Exception ex) { UI.Err(ex.Message); }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_report == null) return;
            var d = new Microsoft.Win32.SaveFileDialog
            {
                FileName = L.Pick("linux-uyumluluk-", "linux-compat-") + Path.GetFileName(_root.TrimEnd('\\')) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt",
                Filter = L.T("Metin dosyası (*.txt)|*.txt"),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };
            if (d.ShowDialog(this) != true) return;
            File.WriteAllText(d.FileName, _report.ToText(), new UTF8Encoding(true));
            UI.Done("Rapor kaydedildi: " + d.FileName);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Window_Closing(object sender, CancelEventArgs e) => _cts?.Cancel();
    }
}
