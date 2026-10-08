using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using Microsoft.Win32;

namespace DevNanotek.Core
{
    /// <summary>Windows Servis Yöneticisi (SCM) işlemleri.</summary>
    public static class WindowsServices
    {
        public const string Apache = "DevNanotek-Apache";
        public const string Nginx = "DevNanotek-Nginx";
        public const string PhpFcgi = "DevNanotek-PHP-FCGI";
        public const string Db = "DevNanotek-DB";
        public const string Mailpit = "DevNanotek-Mailpit";
        public const string PostgreSql = "DevNanotek-PostgreSQL";

        public static readonly string[] All = { Apache, Nginx, PhpFcgi, Db, PostgreSql, Mailpit };

        public static bool Exists(string name)
        {
            try { using (var sc = new ServiceController(name)) { var _ = sc.Status; return true; } }
            catch { return false; }
        }

        public static ServiceControllerStatus? Status(string name)
        {
            try { using (var sc = new ServiceController(name)) return sc.Status; }
            catch { return null; }
        }

        public static bool IsRunning(string name) => Status(name) == ServiceControllerStatus.Running;

        public static string StatusText(string name)
        {
            var s = Status(name);
            if (s == null) return "Servis kurulmadı";
            switch (s.Value)
            {
                case ServiceControllerStatus.Running: return "Çalışıyor";
                case ServiceControllerStatus.Stopped: return "Durdu";
                case ServiceControllerStatus.StartPending: return "Başlatılıyor…";
                case ServiceControllerStatus.StopPending: return "Durduruluyor…";
                case ServiceControllerStatus.Paused: return "Duraklatıldı";
                default: return s.Value.ToString();
            }
        }

        public static bool Start(string name, int timeoutSec = 60)
        {
            try
            {
                using (var sc = new ServiceController(name))
                {
                    if (sc.Status == ServiceControllerStatus.Running) return true;
                    if (sc.Status == ServiceControllerStatus.StopPending) sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(timeoutSec));
                    if (sc.Status != ServiceControllerStatus.StartPending) sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(timeoutSec));
                    Logger.Info("Servis başlatıldı: " + name);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Servis başlatılamadı: " + name, ex);
                return false;
            }
        }

        public static bool Stop(string name, int timeoutSec = 90)
        {
            try
            {
                using (var sc = new ServiceController(name))
                {
                    if (sc.Status == ServiceControllerStatus.Stopped) return true;
                    if (sc.Status != ServiceControllerStatus.StopPending && sc.CanStop) sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(timeoutSec));
                    Logger.Info("Servis durduruldu: " + name);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Servis durdurulamadı: " + name, ex);
                return false;
            }
        }

        public static bool Restart(string name, int startTimeoutSec = 60)
        {
            Stop(name);
            return Start(name, startTimeoutSec);
        }

        /// <summary>sc.exe ile ham servis oluşturma (kendi kurucusu olmayan exe'ler için).</summary>
        public static bool Create(string name, string displayName, string binPath, string description = null)
        {
            var r = ProcessRunner.Run("sc.exe", $"create \"{name}\" binPath= \"{binPath.Replace("\"", "\\\"")}\" start= demand DisplayName= \"{displayName}\"");
            if (!r.Ok) { Logger.Error("sc create başarısız: " + name + " " + r.AllOutput); return false; }
            if (description != null) SetDescription(name, description);
            return true;
        }

        public static bool Delete(string name)
        {
            if (!Exists(name)) return true;
            Stop(name, 60);
            var r = ProcessRunner.Run("sc.exe", $"delete \"{name}\"");
            // Silme işlemi, açık tanıtıcılar varsa "işaretli" kalır; kısa süre bekle
            for (int i = 0; i < 20 && Exists(name); i++) System.Threading.Thread.Sleep(250);
            if (Exists(name)) Logger.Warn("Servis silinmek üzere işaretlendi: " + name);
            else Logger.Info("Servis silindi: " + name);
            return r.Ok || !Exists(name);
        }

        /// <summary>startType: auto | delayed-auto | demand | disabled</summary>
        public static void SetStartType(string name, string startType)
        {
            if (!Exists(name)) return;
            var r = ProcessRunner.Run("sc.exe", $"config \"{name}\" start= {startType}");
            if (!r.Ok) Logger.Warn($"sc config start= {startType} başarısız ({name}): {r.AllOutput}");
        }

        public static void SetDescription(string name, string description)
        {
            if (!Exists(name)) return;
            ProcessRunner.Run("sc.exe", $"description \"{name}\" \"{description.Replace("\"", "'")}\"");
        }

        /// <summary>Servis çökerse otomatik yeniden başlat.</summary>
        public static void SetAutoRecovery(string name)
        {
            if (!Exists(name)) return;
            ProcessRunner.Run("sc.exe", $"failure \"{name}\" reset= 86400 actions= restart/5000/restart/15000/restart/60000");
            ProcessRunner.Run("sc.exe", $"failureflag \"{name}\" 1");
        }

        // ---- Ön-kapatma (preshutdown) süresi: Windows kapanırken servise temiz kapanma için ek süre ----
        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(string machine, string db, uint access);
        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr OpenService(IntPtr scm, string name, uint access);
        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool ChangeServiceConfig2(IntPtr svc, uint infoLevel, ref uint info);
        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr h);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct SERVICE_STATUS
        {
            public int dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode, dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint;
        }
        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceStatus(IntPtr svc, out SERVICE_STATUS status);

        public class ServiceState
        {
            /// <summary>1=durdu 2=başlıyor 3=duruyor 4=çalışıyor 7=duraklatıldı</summary>
            public int State { get; set; }
            public int Win32ExitCode { get; set; }
            public int SpecificExitCode { get; set; }
            /// <summary>Servis hata koduyla durdu mu (normal durdurmada kod 0'dır).</summary>
            public bool HasError => State == 1 && Win32ExitCode != 0 && Win32ExitCode != 1077 /*hiç başlatılmadı*/;
            public int ErrorCode => Win32ExitCode == 1066 ? SpecificExitCode : Win32ExitCode;
        }

        /// <summary>Hızlı durum sorgusu (servis yoksa null).</summary>
        public static ServiceState QueryStatus(string name)
        {
            IntPtr scm = IntPtr.Zero, svc = IntPtr.Zero;
            try
            {
                scm = OpenSCManager(null, null, 0x0001 /*SC_MANAGER_CONNECT*/);
                if (scm == IntPtr.Zero) return null;
                svc = OpenService(scm, name, 0x0004 /*SERVICE_QUERY_STATUS*/);
                if (svc == IntPtr.Zero) return null;
                if (!QueryServiceStatus(svc, out var st)) return null;
                return new ServiceState { State = st.dwCurrentState, Win32ExitCode = st.dwWin32ExitCode, SpecificExitCode = st.dwServiceSpecificExitCode };
            }
            catch { return null; }
            finally
            {
                if (svc != IntPtr.Zero) CloseServiceHandle(svc);
                if (scm != IntPtr.Zero) CloseServiceHandle(scm);
            }
        }

        public static void SetPreshutdownTimeout(string name, uint milliseconds)
        {
            IntPtr scm = IntPtr.Zero, svc = IntPtr.Zero;
            try
            {
                scm = OpenSCManager(null, null, 0x0001 /*SC_MANAGER_CONNECT*/);
                if (scm == IntPtr.Zero) return;
                svc = OpenService(scm, name, 0x0002 /*SERVICE_CHANGE_CONFIG*/);
                if (svc == IntPtr.Zero) return;
                uint ms = milliseconds;
                ChangeServiceConfig2(svc, 7 /*SERVICE_CONFIG_PRESHUTDOWN_INFO*/, ref ms);
            }
            catch { }
            finally
            {
                if (svc != IntPtr.Zero) CloseServiceHandle(svc);
                if (scm != IntPtr.Zero) CloseServiceHandle(scm);
            }
        }

        /// <summary>Servis sürecine özel ortam değişkenleri (PATH gibi). Kayıt defteri: Services\name\Environment</summary>
        public static void SetEnvironment(string name, IDictionary<string, string> env)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name, true))
                {
                    if (key == null) return;
                    if (env == null || env.Count == 0) { try { key.DeleteValue("Environment", false); } catch { } return; }
                    key.SetValue("Environment", env.Select(kv => kv.Key + "=" + kv.Value).ToArray(), RegistryValueKind.MultiString);
                }
            }
            catch (Exception ex) { Logger.Warn("Servis ortamı yazılamadı (" + name + "): " + ex.Message); }
        }

        public static string GetImagePath(string name)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name))
                    return key?.GetValue("ImagePath") as string;
            }
            catch { return null; }
        }

        public static string GetStartType(string name)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name))
                {
                    var v = key?.GetValue("Start");
                    if (v == null) return null;
                    int i = Convert.ToInt32(v);
                    bool delayed = Convert.ToInt32(key.GetValue("DelayedAutostart", 0)) == 1;
                    switch (i) { case 2: return delayed ? "delayed-auto" : "auto"; case 3: return "demand"; case 4: return "disabled"; default: return i.ToString(); }
                }
            }
            catch { return null; }
        }

        /// <summary>Belirtilen exe yolundan çalışan süreçleri (örn. takılı kalmış mysqld) sonlandırır.</summary>
        public static int KillProcessesByPath(string exePath)
        {
            int n = 0;
            if (string.IsNullOrEmpty(exePath)) return 0;
            var name = Path.GetFileNameWithoutExtension(exePath);
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    string path = null;
                    try { path = p.MainModule?.FileName; } catch { }
                    if (path != null && !string.Equals(Path.GetFullPath(path), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase)) continue;
                    p.Kill(); p.WaitForExit(5000); n++;
                }
                catch { }
                finally { p.Dispose(); }
            }
            if (n > 0) Logger.Warn($"{n} süreç sonlandırıldı: {exePath}");
            return n;
        }

        /// <summary>Servis durumu için kısa özet listesi.</summary>
        public static Dictionary<string, string> Snapshot()
        {
            var d = new Dictionary<string, string>();
            foreach (var s in All) d[s] = StatusText(s);
            return d;
        }
    }
}
