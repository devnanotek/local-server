using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DevNanotek.Core;

namespace DevNanotek.Views
{
    /// <summary>
    /// Test/dokümantasyon aracı: DevNanotek.exe --ui-snapshot "klasör"
    /// Her sayfayı ekran dışında açar, PNG olarak kaydeder ve kapanır. İletişim kutusu göstermez, hiçbir ayarı değiştirmez.
    /// </summary>
    public static class UiSnapshot
    {
        /// <summary>Görsellerde gösterilecek kök klasör (DN_SNAPSHOT_DISPLAY_ROOT), yoksa null.</summary>
        private static string DisplayRoot;

        public static async void Run(string dir)
        {
            UI.Silent = true;
            Directory.CreateDirectory(dir);
            Logger.Info("UI snapshot başlıyor: " + dir);
            MainWindow main = null;
            try
            {
                main = new MainWindow
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000, Top = -20000, Width = 1200, Height = 780,
                    ShowActivated = false, ShowInTaskbar = false
                };
                Application.Current.MainWindow = main;
                MainWindow.DemoReport = Demo();
                main.Show();
                // tanıtım görselleri için gösterilen kök yolu (ör. C:\devnanotek) — gerçek dosyalara dokunmaz
                var shown = Environment.GetEnvironmentVariable("DN_SNAPSHOT_DISPLAY_ROOT");
                if (!string.IsNullOrEmpty(shown)) { main.SideRoot.Text = shown; DisplayRoot = shown; }
                await Task.Delay(1500);
                main.RefreshStatus();
                await Task.Delay(800);

                foreach (var theme in new[] { "light", "dark" })
                {
                    ThemeManager.Apply(theme);
                    var d = Path.Combine(dir, theme);
                    Directory.CreateDirectory(d);
                    await Task.Delay(700);
                    await Pass(main, d);
                }
                ThemeManager.Apply(ThemeManager.Mode);
                Logger.Info("UI snapshot tamamlandı");
            }
            catch (Exception ex)
            {
                Logger.Error("UI snapshot hatası: " + ex);
            }
            finally
            {
                MainWindow.DemoReport = null;
                if (main != null) main.ExitApp(); else Application.Current.Shutdown();
            }
        }

        /// <summary>Bir temada tüm sayfaları ve pencereleri kaydeder.</summary>
        private static async Task Pass(MainWindow main, string dir)
        {
            {
                main.Toast(Level.Ok, "Servisler başlatıldı.", 0);
                foreach (var key in new[] { "dashboard", "projects", "versions", "settings", "logs", "help" })
                {
                    main.Navigate(key);
                    await Task.Delay(900);
                    if (DisplayRoot != null && main.Host.Content is ProjectsView pv) pv.DocrootText.Text = DisplayRoot + @"\httpdocs  ·  http://localhost/";
                    Save(main, Path.Combine(dir, key + ".png"));
                    if (key == "dashboard") main.Toast(Level.Off, "", 1);

                    if (key == "settings" && main.Host.Content is SettingsView sv)
                    {
                        for (int i = 0; i < sv.Tabs.Items.Count; i++)
                        {
                            sv.Tabs.SelectedIndex = i;
                            await Task.Delay(600);
                            var tag = ((sv.Tabs.Items[i] as TabItem)?.Tag as string) ?? i.ToString();
                            Save(main, Path.Combine(dir, "settings-" + tag + ".png"));
                            if (tag == "db" && (sv.Tabs.Items[i] as TabItem)?.Content is ScrollViewer dbScroll)
                            {
                                dbScroll.ScrollToEnd();
                                await Task.Delay(500);
                                Save(main, Path.Combine(dir, "settings-db-2.png"));
                                dbScroll.ScrollToHome();
                            }
                        }
                    }
                    if (key == "versions" && main.Host.Content is VersionsView vv)
                    {
                        for (int i = 1; i < vv.Tabs.Items.Count; i++)
                        {
                            vv.Tabs.SelectedIndex = i;
                            await Task.Delay(500);
                            Save(main, Path.Combine(dir, "versions-" + i + ".png"));
                        }
                    }
                }

                var setup = new SetupWindow
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false
                };
                setup.Show();
                await Task.Delay(1500);
                Save(setup, Path.Combine(dir, "setup.png"));
                setup.Close();

                // Linux uyumluluk penceresi (DN_SNAPSHOT_PROJECT ortam değişkeniyle örnek proje verilebilir)
                var proj = Environment.GetEnvironmentVariable("DN_SNAPSHOT_PROJECT");
                var lx = new LinuxCheckWindow(string.IsNullOrEmpty(proj) || !Directory.Exists(proj) ? AppConfig.Current.EffectiveDocRoot : proj)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false
                };
                lx.Show();
                await Task.Delay(2500);
                if (DisplayRoot != null) lx.RootText.Text = DisplayRoot + @"\httpdocs\GITHUB\proje1";
                await Task.Delay(200);
                Save(lx, Path.Combine(dir, "linux-check.png"));
                lx.Close();

                var reset = new ResetWindow
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false
                };
                reset.Show();
                await Task.Delay(1000);
                Save(reset, Path.Combine(dir, "reset.png"));
                reset.Close();

                var un = new UninstallWindow
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false, Owner = main
                };
                un.Show();
                await Task.Delay(1000);
                Save(un, Path.Combine(dir, "uninstall.png"));
                un.Close();

                // tepsi penceresi (örnek veriyle: yeşil / sarı / kırmızı)
                var pop = new TrayPopup { ShowActivated = false };
                pop.Update(Demo());
                pop.Left = -20000; pop.Top = -20000;
                pop.Show();
                await Task.Delay(1000);
                pop.Left = -20000; pop.Top = -20000;
                Save(pop, Path.Combine(dir, "tray-popup.png"));
                pop.Close();

                foreach (var lv in new[] { Level.Ok, Level.Warn, Level.Error, Level.Off })
                    TrayIcons.SavePng(lv, Path.Combine(dir, "tray-icon-" + lv.ToString().ToLowerInvariant() + ".png"));
            }
        }

        /// <summary>Görsel test için örnek durum: Apache çalışıyor, MariaDB hata ile durdu, Mailpit durduruldu.</summary>
        private static StatusReport Demo()
        {
            // DN_SNAPSHOT_ALL_OK=1: tanıtım görselleri için her şey çalışıyor (yeşil); yoksa üç renk birden (yeşil / sarı / kırmızı)
            bool allOk = Environment.GetEnvironmentVariable("DN_SNAPSHOT_ALL_OK") == "1";
            var r = new StatusReport { AlwaysOn = true, Overall = allOk ? Level.Ok : Level.Error };
            r.Rows.Add(new ServiceRow { Key = "web", Title = "Apache", Version = "2.4.69", State = "Çalışıyor", Level = Level.Ok, Installed = true, ServiceExists = true });
            r.Rows.Add(allOk
                ? new ServiceRow { Key = "db", Title = "MariaDB", Version = "11.4.13", State = "Çalışıyor", Level = Level.Ok, Installed = true, ServiceExists = true }
                : new ServiceRow { Key = "db", Title = "MariaDB", Version = "11.4.13", State = "Hata ile durdu (kod 1067)", Level = Level.Error, Installed = true, ServiceExists = true });
            r.Rows.Add(new ServiceRow { Key = "pg", Title = "PostgreSQL", Version = "17.11", State = "Çalışıyor", Level = Level.Ok, Installed = true, ServiceExists = true });
            r.Rows.Add(allOk
                ? new ServiceRow { Key = "mail", Title = "Mailpit", Version = "1.31.4", State = "Çalışıyor", Level = Level.Ok, Installed = true, ServiceExists = true }
                : new ServiceRow { Key = "mail", Title = "Mailpit", Version = "1.31.4", State = "Durduruldu", Level = Level.Warn, Installed = true, ServiceExists = true });
            r.Versions.Add(new VersionItem { Name = "PHP", Version = allOk ? "8.4.26" : "8.3.35" });
            r.Versions.Add(new VersionItem { Name = "Apache", Version = "2.4.69" });
            r.Versions.Add(new VersionItem { Name = "MariaDB", Version = "11.4.13" });
            r.Versions.Add(new VersionItem { Name = "PostgreSQL", Version = "17.11" });
            r.Versions.Add(new VersionItem { Name = "Node.js", Version = allOk ? "24.21.0" : "22.23.3" });
            r.Versions.Add(new VersionItem { Name = "phpMyAdmin", Version = "5.2.3" });
            r.Versions.Add(new VersionItem { Name = "Adminer", Version = "6.1.1" });
            r.Versions.Add(new VersionItem { Name = "Mailpit", Version = "1.31.4" });
            return r;
        }

        private static void Save(Window w, string file)
        {
            try
            {
                var el = w.Content as FrameworkElement;
                if (el == null) return;
                el.UpdateLayout();
                int width = Math.Max(1, (int)Math.Ceiling(el.ActualWidth)), height = Math.Max(1, (int)Math.Ceiling(el.ActualHeight));
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(w.Background ?? Brushes.White, null, new Rect(0, 0, width, height));
                    dc.DrawRectangle(new VisualBrush(el), null, new Rect(0, 0, width, height));
                }
                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = File.Create(file)) enc.Save(fs);
            }
            catch (Exception ex) { Logger.Error("Görüntü kaydedilemedi: " + file + " :: " + ex.Message); }
        }
    }
}
