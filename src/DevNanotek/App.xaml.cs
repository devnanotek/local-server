using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DevNanotek.Core;
using DevNanotek.Views;

namespace DevNanotek
{
    public partial class App : Application
    {
        private Mutex _mutex;
        private EventWaitHandle _showEvent;
        public static string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var args = Program.Args ?? new string[0];
            try { ThemeManager.Init(); } catch (Exception ex) { Logger.Warn("Tema yüklenemedi: " + ex.Message); }

            // ---- Kaldırma (Denetim Masası > Kaldır): çalışan arayüzü kapat, kaldırma penceresini aç ----
            if (args.Contains("--uninstall"))
            {
                DispatcherUnhandledException += (s, a) => { Logger.Error("Kaldırma penceresi hatası", a.Exception); UI.Err(a.Exception.Message); a.Handled = true; };
                Ipc.CloseRunningGui();
                var uw = new UninstallWindow();
                MainWindow = uw;
                uw.Show();
                return;
            }

            // Program güncellemesi: eski sürüm kapanana kadar bekle (exe kilidi kalksın, tek örnek kilidi serbest kalsın)
            AppUpdater.WaitForPreviousInstance(args);

            bool created;
            _mutex = new Mutex(true, Ipc.MutexName, out created);
            if (!created)
            {
                // zaten açık: ona komut gönder (göster / onar)
                Ipc.Send(args.Contains("--repair") ? "repair" : "show");
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (s, a) =>
            {
                Logger.Error("Beklenmeyen hata", a.Exception);
                Logger.Error("Ayrıntı: " + a.Exception);
                UI.Err("Beklenmeyen bir hata oluştu:\n\n" + a.Exception.Message + "\n\nAyrıntı: " + Paths.AppLog);
                a.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Logger.Error("Kritik hata", a.ExceptionObject as Exception ?? new Exception(a.ExceptionObject?.ToString()));
            TaskSchedulerUnobserved();

            Paths.EnsureLayout();
            Logger.Info($"DevNanotek {Version} başlatıldı — kök: {Paths.Root} — exe: {Paths.ExePath}");
            SelfInstall.WriteGuide();

            // Program C:\devnanotek\app dışından açıldıysa kendini oraya kopyalayıp oradan yeniden başlar
            if (!args.Contains("--no-self-install"))
            {
                var target = SelfInstall.EnsureInstalled();
                if (target != null)
                {
                    try { _mutex.ReleaseMutex(); _mutex.Dispose(); _mutex = null; } catch { }
                    try
                    {
                        SelfInstall.Relaunch(target, string.Join(" ", args.Select(a => a.Contains(" ") ? "\"" + a + "\"" : a)));
                        Shutdown();
                        return;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("Kurulu kopya başlatılamadı, mevcut konumdan devam ediliyor: " + ex.Message);
                        _mutex = new Mutex(true, Ipc.MutexName, out created);
                    }
                }
            }

            var cfg = AppConfig.Current;
            bool autostart = args.Contains("--autostart");

            int snap = Array.IndexOf(args, "--ui-snapshot");
            if (snap >= 0)
            {
                var dir = snap + 1 < args.Length ? args[snap + 1] : System.IO.Path.Combine(Paths.Tmp, "ui-snapshot");
                UiSnapshot.Run(dir);
                return;
            }

            var main = new MainWindow();
            MainWindow = main;

            if (!cfg.SetupCompleted && !autostart)
            {
                var setup = new SetupWindow();
                setup.ShowDialog();
                cfg = AppConfig.Current;
            }

            if (autostart) main.StartHidden(); else main.Show();

            // Denetim Masası kaydı (Programlar ve Özellikler) — yalnızca kalıcı kurulum yerinden çalışırken
            if (SelfInstall.IsRunningFromAppDir) System.Threading.Tasks.Task.Run(() => UninstallRegistry.Register());

            // ikinci kopyadan gelen komutlar: show / repair / uninstall / exit
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Ipc.EventName);
            var t = new Thread(() =>
            {
                while (true)
                {
                    try { _showEvent.WaitOne(); } catch { return; }
                    var cmd = Ipc.Take();
                    Dispatcher.BeginInvoke(new Action(() => HandleCommand(main, cmd)));
                }
            }) { IsBackground = true, Name = "ipc-listener" };
            t.Start();

            if (args.Contains("--repair")) Dispatcher.BeginInvoke(new Action(() => main.OpenResetWindow()), DispatcherPriority.ApplicationIdle);

            if (cfg.SetupCompleted && cfg.StartServicesOnLaunch) main.StartServicesInBackground();
        }

        private void HandleCommand(DevNanotek.MainWindow main, string cmd)
        {
            try
            {
                switch (cmd)
                {
                    case "exit":
                        Logger.Info("Başka bir kopyanın isteğiyle kapatılıyor (" + cmd + ")");
                        main.PrepareForShutdown();
                        Shutdown();
                        break;
                    case "repair": main.OpenResetWindow(); break;
                    case "uninstall": main.OpenUninstallWindow(); break;
                    default: main.ShowFromTray(); break;
                }
            }
            catch (Exception ex) { Logger.Error("Komut işlenemedi: " + cmd, ex); }
        }

        /// <summary>Program kendini yeniden başlatmadan önce tek-kopya kilidini bırakır.</summary>
        public void ReleaseSingleInstance()
        {
            try { _mutex?.ReleaseMutex(); } catch { }
            try { _mutex?.Dispose(); } catch { }
            _mutex = null;
        }

        private static void TaskSchedulerUnobserved()
        {
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, a) => { Logger.Error("Görev hatası", a.Exception); a.SetObserved(); };
        }

        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            // Bilgisayar kapanıyor: servisler Windows tarafından düzgün durdurulur, biz sadece çıkarız.
            Logger.Info("Windows oturumu kapanıyor (" + e.ReasonSessionEnding + ")");
            DevNanotek.MainWindow.SessionEnding = true; // pencere kapanışı tepsiye küçültmeyle iptal edilmesin
            try { AppConfig.Current.Save(); } catch { }
            base.OnSessionEnding(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Info("DevNanotek kapatıldı");
            try { _mutex?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }
    }
}
