using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DevNanotek.Core;
using DevNanotek.Views;
using WinForms = System.Windows.Forms;
using ViewBase = DevNanotek.Views.ViewBase;

namespace DevNanotek
{
    public partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }

        private readonly Dictionary<string, ViewBase> _views = new Dictionary<string, ViewBase>();
        private ViewBase _current;
        private WinForms.NotifyIcon _tray;
        private readonly DispatcherTimer _timer;
        private bool _exiting;
        private bool _busy;
        private bool _trayHintShown;
        public StackStatus LastStatus { get; private set; }

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
            SideVersion.Text = "v" + App.Version;
            SideRoot.Text = Paths.Root;
            SideRoot.ToolTip = Paths.Root;

            SetupTray();
            // Windows simge kaydını birkaç saniye içinde oluşturur; sonra bir kez görünür yap
            if (Paths.IsRealExe)
                Task.Delay(8000).ContinueWith(_ => TrayVisibility.PromoteOnce(AppConfig.Current));
            Stack.StatusChanged += () => Dispatcher.BeginInvoke(new Action(RefreshStatus));
            Logger.Message += line => Dispatcher.BeginInvoke(new Action(() => { if (!_busy) StatusText.Text = line.Length > 23 ? line.Substring(22) : line; }));

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _timer.Tick += (s, e) => RefreshStatus();
            _timer.Start();
            UpdateThemeButton();
            ThemeManager.Changed += () => { UpdateThemeButton(); _lastIconLevel = null; };
            UpdateChecker.Changed += () => Dispatcher.BeginInvoke(new Action(OnUpdatesChanged));
            Loaded += (s, e) =>
            {
                RefreshStatus();
                if (_current == null) Navigate("dashboard");
                OnUpdatesChanged();
                // yeni sürümleri arka planda kontrol et: açılışta ve program tepside açık kaldıkça 6 saatte bir
                // (bileşen kataloğu en fazla 12 saatte bir yenilenir; DEVNANOTEK'in kendi sürümü her seferinde denetlenir)
                if (DemoReport == null)
                {
                    Task.Delay(6000).ContinueWith(_ => UpdateChecker.AutoCheckAsync());
                    var updTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
                    updTimer.Tick += (s2, e2) => _ = UpdateChecker.AutoCheckAsync();
                    updTimer.Start();
                }
            };
        }

        // ------------------------------------------------------------------
        //  Tema
        // ------------------------------------------------------------------
        private void UpdateThemeButton()
        {
            var m = ThemeManager.Mode;
            ThemeBtn.Content = ThemeManager.ModeGlyph(m);
            ThemeBtn.ToolTip = "Tema: " + ThemeManager.ModeTitle(m) + "  (değiştirmek için tıklayın)";
        }

        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            var m = ThemeManager.Cycle();
            Toast(Level.Off, "Tema: " + ThemeManager.ModeTitle(m), 2200);
        }

        private void SideStatus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => TogglePopup();

        // ------------------------------------------------------------------
        //  Güncellemeler
        // ------------------------------------------------------------------
        private int _lastUpdateCount = -1;
        private Version _announcedApp;

        private void OnUpdatesChanged()
        {
            var app = AppUpdater.Available;
            int n = UpdateChecker.Current.Count + (app != null ? 1 : 0);
            UpdateBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateBadgeText.Text = n.ToString();
            var lines = UpdateChecker.Current.Select(u => u.Text).ToList();
            if (app != null) lines.Insert(0, "DEVNANOTEK " + AppInfo.CurrentText + " → " + app.Version.ToString(3));
            UpdateBadge.ToolTip = n > 0 ? string.Join("\n", lines) : null;
            if (_tray != null && app != null && _announcedApp != app.Version)
            {
                _announcedApp = app.Version;
                try { _tray.ShowBalloonTip(5000, L.F("DEVNANOTEK {0} yayınlandı", app.Version.ToString(3)), L.T("Sürümler sayfasından tek tıkla güncelleyebilirsiniz."), WinForms.ToolTipIcon.Info); } catch { }
            }
            else if (n > 0 && _lastUpdateCount >= 0 && n > _lastUpdateCount && _tray != null)
            {
                try { _tray.ShowBalloonTip(4000, "DEVNANOTEK", L.F("{0} bileşen için yeni sürüm var. Sürümler sayfasından güncelleyebilirsiniz.", n), WinForms.ToolTipIcon.Info); } catch { }
            }
            _lastUpdateCount = n;
            if (_current is VersionsView || _current is HelpView) RefreshCurrentView();
        }

        /// <summary>DEVNANOTEK'in yeni sürümünü indirir, program kapanınca yenisini başlatır. Servisler çalışmaya devam eder.</summary>
        public async Task UpdateProgramAsync()
        {
            var rel = AppUpdater.Available;
            if (rel == null) { Toast(Level.Ok, "DEVNANOTEK güncel (v" + AppInfo.CurrentText + ")."); return; }
            if (!Paths.IsRealExe) { UI.Warn("Bu çalıştırma biçiminde güncellenemez."); return; }
            if (!UI.Confirm($"DEVNANOTEK {rel.Version.ToString(3)} indirilecek ve program yeniden açılacak.\n\nServisleriniz ve projeleriniz etkilenmez. Devam edilsin mi?")) return;
            string exe = null;
            var ok = await RunBusyAsync("DEVNANOTEK " + rel.Version.ToString(3) + " indiriliyor", async log =>
            {
                var progress = new Progress<DownloadProgress>(p => { SetBusyProgress(p.Percent, p.Indeterminate); if (!string.IsNullOrEmpty(p.Status)) log(p.Status); });
                exe = await AppUpdater.DownloadAsync(rel, progress, System.Threading.CancellationToken.None);
            }, true);
            if (!ok || exe == null) return;
            try
            {
                (Application.Current as App)?.ReleaseSingleInstance();
                AppUpdater.LaunchAfterExit(exe);
            }
            catch (Exception ex) { UI.Err("Yeni sürüm başlatılamadı: " + ex.Message + "\n\nDosya: " + exe); return; }
            PrepareForShutdown();
            Application.Current.Shutdown();
        }

        // ------------------------------------------------------------------
        //  Renkli bildirim (toast)
        // ------------------------------------------------------------------
        private DispatcherTimer _toastTimer;
        private string _toastDetails;

        /// <summary>Sağ altta renkli bildirim: Ok=yeşil, Error=kırmızı, Warn=sarı, Off=bilgi.</summary>
        public void Toast(Level level, string text, int ms = 4500, string details = null)
        {
            string bg, fg, glyph;
            switch (level)
            {
                case Level.Ok: bg = "OkBg"; fg = "Ok"; glyph = ""; break;
                case Level.Error: bg = "ErrBg"; fg = "Err"; glyph = ""; break;
                case Level.Warn: bg = "WarnBg"; fg = "Warn"; glyph = ""; break;
                default: bg = "InfoBg"; fg = "Info"; glyph = ""; break;
            }
            ToastBox.SetResourceReference(Border.BackgroundProperty, bg);
            ToastBox.SetResourceReference(Border.BorderBrushProperty, fg);
            ToastIcon.SetResourceReference(TextBlock.ForegroundProperty, fg);
            ToastText.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            ToastIcon.Text = glyph;
            ToastText.Text = text;
            _toastDetails = details;
            ToastMore.Visibility = string.IsNullOrEmpty(details) ? Visibility.Collapsed : Visibility.Visible;
            ToastBox.Visibility = Visibility.Visible;
            _toastTimer?.Stop();
            if (ms > 0)
            {
                _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                _toastTimer.Tick += (s, e) => { _toastTimer.Stop(); ToastBox.Visibility = Visibility.Collapsed; };
                _toastTimer.Start();
            }
        }

        private void ToastClose_Click(object sender, RoutedEventArgs e) { _toastTimer?.Stop(); ToastBox.Visibility = Visibility.Collapsed; }
        private void ToastMore_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrEmpty(_toastDetails)) UI.Msg(_toastDetails); }

        // ------------------------------------------------------------------
        //  Gezinme
        // ------------------------------------------------------------------
        private bool _navigating;

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (_navigating) return;
            var key = (sender as RadioButton)?.Tag as string;
            if (key != null && IsLoaded) Navigate(key);
        }

        public void Navigate(string key)
        {
            if (_navigating) return;
            _navigating = true;
            try { NavigateCore(key); }
            finally { _navigating = false; }
        }

        private void NavigateCore(string key)
        {
            if (!_views.TryGetValue(key, out var v))
            {
                switch (key)
                {
                    case "projects": v = new ProjectsView(); break;
                    case "versions": v = new VersionsView(); break;
                    case "settings": v = new SettingsView(); break;
                    case "logs": v = new LogsView(); break;
                    case "help": v = new HelpView(); break;
                    default: v = new DashboardView(); key = "dashboard"; break;
                }
                _views[key] = v;
            }
            _current = v;
            Host.Content = v;
            try { v.Refresh(); if (LastStatus != null) v.OnStatus(LastStatus); } catch (Exception ex) { Logger.Error("Görünüm yenilenemedi: " + key, ex); }
            // menüdeki seçimi eşitle
            foreach (var rb in FindVisualChildren<RadioButton>(this)) if ((rb.Tag as string) == key && rb.IsChecked != true) rb.IsChecked = true;
        }

        public void RefreshCurrentView() { try { _current?.Refresh(); } catch (Exception ex) { Logger.Error("Refresh", ex); } }

        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject d) where T : DependencyObject
        {
            if (d == null) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                if (c is T t) yield return t;
                foreach (var cc in FindVisualChildren<T>(c)) yield return cc;
            }
        }

        // ------------------------------------------------------------------
        //  Durum
        // ------------------------------------------------------------------
        private bool _refreshing;
        public async void RefreshStatus()
        {
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                var cfg = AppConfig.Current;
                var st = await Task.Run(() => Stack.GetStatus(cfg));
                if (DemoReport != null) st.Report = DemoReport;
                LastStatus = st;
                ApplyReport(st.Report);
                _current?.OnStatus(st);
            }
            catch (Exception ex) { Logger.Warn("Durum alınamadı: " + ex.Message); }
            finally { _refreshing = false; }
        }

        private Level? _lastIconLevel;

        /// <summary>Yalnızca görsel test (--ui-snapshot) için örnek durum.</summary>
        public static StatusReport DemoReport { get; set; }

        /// <summary>Sol menüdeki mini durum, tepsi simgesi rengi/ipucu ve tepsi penceresi aynı rapordan beslenir.</summary>
        public void ApplyReport(StatusReport r)
        {
            if (r == null) return;
            SideDot.Fill = StatusColors.BrushOf(r.Overall);
            SideStatus.Text = r.OverallText;
            SideRows.ItemsSource = r.Rows.ToList();
            var php = r.Versions.FirstOrDefault(v => v.Name == "PHP");
            var node = r.Versions.FirstOrDefault(v => v.Name == "Node.js");
            SidePhp.Text = string.Join("  ·  ", new[] { php, node }.Where(v => v != null && !string.IsNullOrEmpty(v.Version)).Select(v => v.Text));

            if (_tray != null)
            {
                if (_lastIconLevel != r.Overall)
                {
                    try { _tray.Icon = TrayIcons.Get(r.Overall); _lastIconLevel = r.Overall; } catch (Exception ex) { Logger.Warn("Tepsi simgesi: " + ex.Message); }
                }
                // NotifyIcon.Text en fazla 63 karakter olabilir
                var tip = "DEVNANOTEK — " + L.T(r.OverallText);
                var ver = php != null && !string.IsNullOrEmpty(php.Version) ? "\nPHP " + php.Version : "";
                tip += ver;
                _tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            }
            _popup?.Update(r);
        }

        // ------------------------------------------------------------------
        //  Meşgul çubuğu ile uzun işlem
        // ------------------------------------------------------------------
        /// <param name="doneMessage">İşlem hatasız biterse gösterilecek yeşil bildirim (null: gösterme).</param>
        public async Task<bool> RunBusyAsync(string title, Func<Action<string>, Task> work, bool showLog = false, string doneMessage = null)
        {
            if (_busy) { Toast(Level.Warn, "Başka bir işlem sürüyor, lütfen bitmesini bekleyin."); return false; }
            _busy = true;
            BusyBar.Visibility = Visibility.Visible;
            BusyTitle.Text = title;
            BusyText.Text = "";
            BusyLog.Clear();
            BusyLog.Visibility = showLog ? Visibility.Visible : Visibility.Collapsed;
            BusyProgress.IsIndeterminate = true;
            Host.IsEnabled = false;
            Action<string> log = s => Dispatcher.BeginInvoke(new Action(() =>
            {
                BusyText.Text = s;
                StatusText.Text = s;
                BusyLog.Append(s);
            }));
            bool ok = true;
            try { await work(log); }
            catch (Exception ex)
            {
                ok = false;
                Logger.Error(title, ex);
                Toast(Level.Error, title + " başarısız", 8000, ex.Message);
                UI.Err(title + " başarısız:\n\n" + ex.Message);
            }
            finally
            {
                _busy = false;
                Host.IsEnabled = true;
                BusyBar.Visibility = Visibility.Collapsed;
                RefreshStatus();
                RefreshCurrentView();
            }
            if (ok && doneMessage != null) Toast(Level.Ok, doneMessage);
            return ok;
        }

        public void SetBusyProgress(double percent, bool indeterminate)
        {
            BusyProgress.IsIndeterminate = indeterminate;
            if (!indeterminate) BusyProgress.Value = percent;
        }

        private void BusyLog_Click(object sender, RoutedEventArgs e)
            => BusyLog.Visibility = BusyLog.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Sonuç: hatasız → yeşil bildirim; yalnız uyarı → sarı bildirim (+ayrıntı); hata → kırmızı bildirim + açıklama penceresi.</summary>
        public void ShowApplyResult(ApplyResult r, string okMessage = null)
        {
            var msg = "";
            if (r.Errors.Count > 0) msg += "HATALAR:\n• " + string.Join("\n• ", r.Errors) + "\n\n";
            if (r.Warnings.Count > 0) msg += "Uyarılar:\n• " + string.Join("\n• ", r.Warnings);
            msg = msg.Trim();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (r.Ok && r.Warnings.Count == 0) { Toast(Level.Ok, okMessage ?? "İşlem başarıyla tamamlandı."); return; }
                if (r.Ok) { Toast(Level.Warn, (okMessage ?? "Tamamlandı") + " — " + r.Warnings.Count + " uyarı var", 9000, msg); return; }
                Toast(Level.Error, "İşlem hatalarla tamamlandı", 10000, msg);
                UI.Err(msg);
            }));
        }

        public Task ApplyConfigAsync(bool start, string title = "Yapılandırma uygulanıyor")
            => RunBusyAsync(title, async log =>
            {
                var r = await Stack.ApplyAsync(AppConfig.Current, log, start);
                ShowApplyResult(r, "Yapılandırma uygulandı.");
            });

        // ------------------------------------------------------------------
        //  Tepsi
        // ------------------------------------------------------------------
        private void SetupTray()
        {
            try
            {
                _tray = new WinForms.NotifyIcon { Text = "DEVNANOTEK Local Server", Visible = true };
                try { _tray.Icon = TrayIcons.Get(Level.Off); }
                catch
                {
                    var sri = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/devnanotek.ico"));
                    if (sri != null) _tray.Icon = new System.Drawing.Icon(sri.Stream, WinForms.SystemInformation.SmallIconSize);
                }
                var menu = new WinForms.ContextMenuStrip();
                menu.Items.Add(L.T("Durum penceresi"), null, (s, e) => TogglePopup());
                menu.Items.Add(L.T("DEVNANOTEK'i Göster"), null, (s, e) => ShowFromTray());
                menu.Items.Add(new WinForms.ToolStripSeparator());
                menu.Items.Add(L.T("▶ Tümünü Başlat"), null, async (s, e) => await StartAllFromTray());
                menu.Items.Add(L.T("■ Tümünü Durdur"), null, async (s, e) => await StopAllFromTray());
                menu.Items.Add(new WinForms.ToolStripSeparator());
                menu.Items.Add("localhost", null, (s, e) => ProcessRunner.OpenUrl(UI.LocalhostUrl()));
                menu.Items.Add("phpMyAdmin", null, (s, e) => ProcessRunner.OpenUrl(UI.LocalhostUrl() + "phpmyadmin/"));
                menu.Items.Add("Mailpit", null, (s, e) => ProcessRunner.OpenUrl(MailpitManager.UiUrl(AppConfig.Current)));
                menu.Items.Add(L.T("httpdocs klasörü"), null, (s, e) => ProcessRunner.OpenFolder(AppConfig.Current.EffectiveDocRoot));
                menu.Items.Add("Terminal", null, (s, e) => UI.OpenTerminal(AppConfig.Current.EffectiveDocRoot));
                menu.Items.Add(L.T("Onar / Sıfırla…"), null, (s, e) => OpenResetWindow());
                menu.Items.Add(new WinForms.ToolStripSeparator());
                menu.Items.Add(L.T("Çıkış"), null, (s, e) => ExitApp());
                _tray.ContextMenuStrip = menu;
                // tek sol tık: durum penceresi · çift tık: programı aç · sağ tık: menü
                _tray.MouseUp += (s, e) => { if (e.Button == WinForms.MouseButtons.Left) Dispatcher.BeginInvoke(new Action(TogglePopup)); };
                _tray.DoubleClick += (s, e) => Dispatcher.BeginInvoke(new Action(() => { _popup?.HidePopup(); ShowFromTray(); }));
            }
            catch (Exception ex) { Logger.Warn("Tepsi simgesi oluşturulamadı: " + ex.Message); }
        }

        private TrayPopup _popup;

        /// <summary>Saatin yanındaki simgeye tıklanınca açılan küçük durum penceresi.</summary>
        public void TogglePopup()
        {
            try
            {
                if (_popup == null) _popup = new TrayPopup();
                if (LastStatus?.Report != null) _popup.Update(LastStatus.Report);
                _popup.Toggle();
                RefreshStatus();
            }
            catch (Exception ex) { Logger.Error("Durum penceresi açılamadı", ex); }
        }

        private Task StartAllFromTray() => RunBusyAsync("Servisler başlatılıyor", async log =>
        {
            var errs = await Stack.StartAllAsync(AppConfig.Current, log);
            if (errs.Count > 0) UI.Err(string.Join("\n\n", errs));
        });

        private Task StopAllFromTray() => RunBusyAsync("Servisler durduruluyor", async log =>
        {
            var errs = await Stack.StopAllAsync(AppConfig.Current, log);
            if (errs.Count > 0) UI.Err(string.Join("\n", errs));
        });

        public void StartHidden()
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
            Show();
            Hide();
        }

        public void ShowFromTray()
        {
            Show();
            ShowInTaskbar = true;
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true; Topmost = false;
        }

        public void StartServicesInBackground()
        {
            Task.Run(() =>
            {
                try
                {
                    var cfg = AppConfig.Current;
                    if (Stack.ActiveServices(cfg).All(WindowsServices.Exists))
                    {
                        var errs = Stack.StartAll(cfg, s => Logger.Info(s));
                        if (errs.Count > 0) Logger.Warn("Açılışta başlatma: " + string.Join(" | ", errs));
                    }
                }
                catch (Exception ex) { Logger.Error("Açılışta başlatma", ex); }
            });
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && AppConfig.Current.MinimizeToTray)
            {
                Hide(); ShowInTaskbar = false;
                TrayHint();
            }
        }

        private void TrayHint()
        {
            if (_trayHintShown || _tray == null) return;
            _trayHintShown = true;
            try { _tray.ShowBalloonTip(2500, "DEVNANOTEK", L.T("Arka planda çalışmaya devam ediyor. Simgeye çift tıklayarak açabilirsiniz."), WinForms.ToolTipIcon.Info); } catch { }
        }

        /// <summary>Windows kapanıyor/oturum kapanıyor: kapanışı asla engelleme.</summary>
        public static bool SessionEnding { get; set; }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_exiting) return;
            if (SessionEnding)
            {
                PrepareForShutdown();
                return;
            }
            if (AppConfig.Current.MinimizeToTray)
            {
                e.Cancel = true;
                Hide(); ShowInTaskbar = false;
                TrayHint();
                return;
            }
            e.Cancel = true;
            ExitApp();
        }

        public async void ExitApp() => await ExitAppAsync(false);

        /// <summary>Arayüz dili değişti: program servislere dokunmadan kapanıp yeni dille yeniden açılır.</summary>
        /// <param name="page">Yeniden açılınca gösterilecek sayfa (ör. "settings"); null: Genel Bakış.</param>
        public async void RestartForLanguage(string page) => await ExitAppAsync(true, page == null ? null : "--open " + page);

        /// <param name="restart">true: servislere dokunmadan programı kapatıp yeniden açar (fabrika ayarlarından ve dil değişikliğinden sonra).</param>
        /// <param name="extraArgs">Yeniden açılan programa verilecek ek parametreler.</param>
        public async System.Threading.Tasks.Task ExitAppAsync(bool restart, string extraArgs = null)
        {
            if (_exiting) return;
            _exiting = true;
            try
            {
                if (!restart && AppConfig.Current.StopServicesOnExit && Stack.ActiveServices(AppConfig.Current).Any(WindowsServices.IsRunning))
                {
                    ShowFromTray();
                    await RunBusyAsync("Servisler durduruluyor", async log => { await Stack.StopAllAsync(AppConfig.Current, log); });
                }
            }
            catch { }
            PrepareForShutdown();
            if (restart && Paths.IsRealExe)
            {
                try
                {
                    (Application.Current as App)?.ReleaseSingleInstance();
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Paths.ExePath, "--no-self-install" + (string.IsNullOrEmpty(extraArgs) ? "" : " " + extraArgs)) { UseShellExecute = true });
                }
                catch (Exception ex) { Logger.Error("Yeniden başlatılamadı", ex); }
            }
            Application.Current.Shutdown();
        }

        /// <summary>Zamanlayıcıyı durdurur, tepsi simgesini ve durum penceresini kaldırır.</summary>
        public void PrepareForShutdown()
        {
            _exiting = true;
            try { _timer.Stop(); } catch { }
            try { _popup?.Close(); _popup = null; } catch { }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; } } catch { }
            TrayIcons.DisposeAll();
        }

        /// <summary>Kaldırma penceresini açar.</summary>
        public void OpenUninstallWindow()
        {
            ShowFromTray();
            var w = new UninstallWindow { Owner = this };
            w.ShowDialog();
        }

        /// <summary>Onarım ve Sıfırlama penceresini açar; fabrika ayarlarına dönüldüyse programı yeniden başlatır.</summary>
        public async void OpenResetWindow()
        {
            ShowFromTray();
            var w = new ResetWindow { Owner = this };
            w.ShowDialog();
            if (w.FactoryResetDone) { await ExitAppAsync(true); return; }
            RefreshStatus();
            RefreshCurrentView();
        }
    }

    /// <summary>Küçük arayüz yardımcıları.</summary>
    public static class UI
    {
        /// <summary>Otomatik ekran görüntüsü (test) modunda iletişim kutuları gösterilmez, günlüğe yazılır.</summary>
        public static bool Silent { get; set; }

        private const string Caption = "DEVNANOTEK";

        /// <summary>Kısa mesajlar ana pencere açıkken sağ altta renkli bildirim olur; uzunlar iletişim kutusunda gösterilir.</summary>
        private static bool TryToast(Level level, string text)
        {
            var m = MainWindow.Instance;
            if (Silent || m == null || !m.IsVisible || !m.IsActive && m.WindowState == WindowState.Minimized) return false;
            if (string.IsNullOrEmpty(text) || text.Length > 140 || text.Contains("\n")) return false;
            if (Application.Current.Windows.OfType<Window>().Any(w => w != m && w.IsVisible && w.IsActive)) return false; // başka pencere öndeyse
            m.Toast(level, text);
            return true;
        }

        // metinler Türkçe verilir; İngilizce arayüzde L.T ile çevrilir
        public static void Msg(string text) { text = L.T(text); if (Silent) { Logger.Info("[msg] " + text); return; } if (TryToast(Level.Off, text)) return; MessageBox.Show(text, Caption, MessageBoxButton.OK, MessageBoxImage.Information); }
        public static void Warn(string text) { text = L.T(text); if (Silent) { Logger.Warn("[warn] " + text); return; } if (TryToast(Level.Warn, text)) return; MessageBox.Show(text, Caption, MessageBoxButton.OK, MessageBoxImage.Warning); }
        public static void Err(string text) { text = L.T(text); if (Silent) { Logger.Error("[err] " + text); return; } MessageBox.Show(text, Caption, MessageBoxButton.OK, MessageBoxImage.Error); }
        /// <summary>Başarılı işlem: yeşil bildirim.</summary>
        public static void Done(string text) { text = L.T(text); if (Silent) { Logger.Info("[ok] " + text); return; } if (TryToast(Level.Ok, text)) return; MessageBox.Show(text, Caption, MessageBoxButton.OK, MessageBoxImage.Information); }
        public static bool Confirm(string text) => ConfirmRaw(L.T(text));
        /// <summary>Çevrilmeden sorulur (ör. dil değişikliği sorusu yeni dilde yazılır).</summary>
        public static bool ConfirmRaw(string text) { if (Silent) { Logger.Info("[confirm->hayır] " + text); return false; } return MessageBox.Show(text, Caption, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes; }

        public static string LocalhostUrl()
        {
            var cfg = AppConfig.Current;
            return cfg.HttpPort == 80 ? "http://localhost/" : $"http://localhost:{cfg.HttpPort}/";
        }

        public static string LocalhostHttpsUrl()
        {
            var cfg = AppConfig.Current;
            return cfg.HttpsPort == 443 ? "https://localhost/" : $"https://localhost:{cfg.HttpsPort}/";
        }

        /// <summary>PATH'i hazırlanmış bir komut istemi açar.</summary>
        /// <param name="run">Terminal açılınca çalıştırılacak komut (ör. "psql").</param>
        public static void OpenTerminal(string dir, string run = null)
        {
            var cfg = AppConfig.Current;
            var env = EnvPath.ToolEnv(cfg);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Paths.Root;
            var banner = "echo " + L.F("DEVNANOTEK terminali — PHP {0}  Node {1}  ({2})", cfg.PhpVersion, cfg.NodeVersion, dir) + " & echo " + L.T("Komutlar:") + " php, composer, node, npm, mysql, mysqldump" +
                         (PostgreSqlManager.IsActive(cfg) ? ", psql, pg_dump" : "") + ", mailpit, mkcert" + (string.IsNullOrEmpty(run) ? "" : " & " + run);
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/k " + banner) { UseShellExecute = false, WorkingDirectory = dir };
            foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
            try { System.Diagnostics.Process.Start(psi); } catch (Exception ex) { Err("Terminal açılamadı: " + ex.Message); }
        }

        public static string FindVsCode()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Microsoft VS Code\Code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft VS Code\Code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft VS Code\Code.exe")
            };
            return candidates.FirstOrDefault(File.Exists);
        }
    }
}
