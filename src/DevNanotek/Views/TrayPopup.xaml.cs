using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DevNanotek.Core;

namespace DevNanotek.Views
{
    /// <summary>
    /// Saatin yanındaki tepsi simgesine tıklayınca açılan küçük durum penceresi:
    /// servisler (yeşil/sarı/kırmızı), aktif sürümler, hızlı eylemler.
    /// </summary>
    public partial class TrayPopup : Window
    {
        private DateTime _hiddenAt = DateTime.MinValue;
        private bool _busy;

        public TrayPopup() { InitializeComponent(); }

        /// <summary>Simgeye tıklandı: açıksa kapat, kapalıysa göster.</summary>
        public void Toggle()
        {
            if (IsVisible) { HidePopup(); return; }
            // simgeye tıklama pencereyi önce Deactivated ile kapatır; hemen tekrar açılmasın
            if ((DateTime.Now - _hiddenAt).TotalMilliseconds < 350) return;
            ShowPopup();
        }

        public void ShowPopup()
        {
            Show();
            UpdateLayout();
            PlaceNearTray();
            Activate();
            Focus();
        }

        public void HidePopup()
        {
            _hiddenAt = DateTime.Now;
            Hide();
        }

        /// <summary>Görev çubuğunun konumuna göre ekranın sağ alt (veya sağ üst) köşesine yerleştir.</summary>
        private void PlaceNearTray()
        {
            var wa = SystemParameters.WorkArea;
            var ps = SystemParameters.PrimaryScreenHeight;
            double h = ActualHeight > 0 ? ActualHeight : 520, w = ActualWidth > 0 ? ActualWidth : Width;
            Left = wa.Right - w + 4;
            Top = wa.Top > 0 && wa.Bottom >= ps - 1 ? wa.Top - 4 : wa.Bottom - h + 4; // görev çubuğu üstteyse üste
        }

        public void Update(StatusReport r)
        {
            if (r == null) return;
            OverallDot.Fill = StatusColors.BrushOf(r.Overall);
            OverallText.Text = r.OverallText;
            RowsList.ItemsSource = r.Rows.ToList();
            VersionsList.ItemsSource = r.Versions.Where(v => !string.IsNullOrEmpty(v.Version)).ToList();
            ModeText.Text = r.AlwaysOn ? "Mod: Her zaman açık (bilgisayar açılınca başlar)" : "Mod: Sadece ben açınca";
            if (IsVisible) Dispatcher.BeginInvoke(new Action(PlaceNearTray), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void RowButton_Loaded(object sender, RoutedEventArgs e)
        {
            var b = (Button)sender;
            var row = b.Tag as ServiceRow;
            if (row == null) return;
            if (!row.Installed) { b.Content = "Kur"; b.ToolTip = "Sürümler sayfasını açar"; return; }
            b.Content = row.Running ? "Durdur" : "Başlat";
            b.ToolTip = row.Running ? row.Title + " servisini durdur" : row.Title + " servisini başlat";
        }

        private async void RowToggle_Click(object sender, RoutedEventArgs e)
        {
            var row = (sender as Button)?.Tag as ServiceRow;
            if (row == null || _busy) return;
            if (!row.Installed) { OpenMain("versions"); return; }
            var cfg = AppConfig.Current;
            await RunAsync((row.Running ? "Durduruluyor: " : "Başlatılıyor: ") + row.TitleWithVersion, () =>
            {
                if (!row.ServiceExists)
                {
                    var res = Stack.Apply(cfg, null, true);
                    return res.Ok ? null : string.Join("\n\n", res.Errors);
                }
                if (row.Running)
                {
                    if (!WindowsServices.Stop(row.Service)) return row.Title + " durdurulamadı.";
                    if (row.Key == "web" && cfg.WebServer == "nginx") WindowsServices.Stop(WindowsServices.PhpFcgi);
                    return null;
                }
                if (row.Key == "web" && cfg.WebServer == "nginx") WindowsServices.Start(WindowsServices.PhpFcgi);
                return WindowsServices.Start(row.Service) ? null : row.Title + " başlatılamadı. " + Stack.HintFor(row.Service) + SystemCheck.BlockedHint();
            });
        }

        private async void StartAll_Click(object sender, RoutedEventArgs e)
            => await RunAsync("Servisler başlatılıyor…", () =>
            {
                var errs = Stack.StartAll(AppConfig.Current, null);
                return errs.Count == 0 ? null : string.Join("\n\n", errs);
            });

        private async void StopAll_Click(object sender, RoutedEventArgs e)
            => await RunAsync("Servisler durduruluyor…", () =>
            {
                var errs = Stack.StopAll(AppConfig.Current, null);
                return errs.Count == 0 ? null : string.Join("\n", errs);
            });

        private async Task RunAsync(string title, Func<string> work)
        {
            _busy = true;
            BusyText.Text = title;
            BtnStartAll.IsEnabled = BtnStopAll.IsEnabled = RowsList.IsEnabled = false;
            string err = null;
            try { err = await Task.Run(work); }
            catch (Exception ex) { err = ex.Message; Logger.Error(title, ex); }
            finally
            {
                _busy = false;
                BusyText.Text = "";
                BtnStartAll.IsEnabled = BtnStopAll.IsEnabled = RowsList.IsEnabled = true;
                Stack.NotifyChanged();
                MainWindow.Instance?.RefreshStatus();
            }
            if (err != null) { HidePopup(); UI.Err(err); }
        }

        private void OpenMain(string page = null)
        {
            HidePopup();
            var m = MainWindow.Instance;
            if (m == null) return;
            m.ShowFromTray();
            if (page != null) m.Navigate(page);
        }

        private void Localhost_Click(object sender, RoutedEventArgs e) { HidePopup(); ProcessRunner.OpenUrl(UI.LocalhostUrl()); }
        private void Pma_Click(object sender, RoutedEventArgs e) { HidePopup(); ProcessRunner.OpenUrl(UI.LocalhostUrl() + "phpmyadmin/"); }
        private void Docs_Click(object sender, RoutedEventArgs e) { HidePopup(); ProcessRunner.OpenFolder(AppConfig.Current.EffectiveDocRoot); }
        private void Terminal_Click(object sender, RoutedEventArgs e) { HidePopup(); UI.OpenTerminal(AppConfig.Current.EffectiveDocRoot); }
        private void Open_Click(object sender, RoutedEventArgs e) => OpenMain("dashboard");
        private void Close_Click(object sender, RoutedEventArgs e) => HidePopup();

        private void Window_Deactivated(object sender, EventArgs e) { if (!_busy) HidePopup(); }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) HidePopup(); }
    }
}
