using System;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace DevNanotek.Core
{
    /// <summary>
    /// Programın Windows oturumu açılınca (yönetici olarak, UAC sormadan) tepside başlaması için Zamanlanmış Görev.
    /// Not: Servislerin kendisi zaten Windows servisi olarak otomatik başlar; bu görev yalnızca arayüz/tepsi içindir.
    /// schtasks varsayılanları (3 gün sonra durdur, pilde başlatma) sorun çıkardığı için görev XML ile oluşturulur.
    /// </summary>
    public static class Autostart
    {
        public const string TaskName = "DevNanotek";

        public static string TargetExe => File.Exists(Paths.AppExe) ? Paths.AppExe : Paths.ExePath;

        public static bool IsEnabled()
        {
            var r = ProcessRunner.Run("schtasks.exe", $"/query /tn \"{TaskName}\"", null, 15000);
            return r.ExitCode == 0;
        }

        public static bool Enable()
        {
            var exe = TargetExe;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) { Logger.Warn("Otomatik başlatma: exe yolu belirlenemedi."); return false; }
            string user;
            try { user = WindowsIdentity.GetCurrent().Name; } catch { user = Environment.UserDomainName + "\\" + Environment.UserName; }

            var xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>DevNanotek yerel sunucu yöneticisini oturum açılışında tepside başlatır.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{SecurityElement.Escape(user)}</UserId>
      <Delay>PT10S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{SecurityElement.Escape(user)}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{SecurityElement.Escape(exe)}</Command>
      <Arguments>--autostart</Arguments>
      <WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe))}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
            var tmp = Path.Combine(Paths.Tmp, "devnanotek-task.xml");
            try
            {
                Directory.CreateDirectory(Paths.Tmp);
                File.WriteAllText(tmp, xml, Encoding.Unicode);
                var r = ProcessRunner.Run("schtasks.exe", $"/create /tn \"{TaskName}\" /xml \"{tmp}\" /f", null, 20000);
                if (!r.Ok) { Logger.Error("Zamanlanmış görev oluşturulamadı: " + r.AllOutput); return false; }
                Logger.Info("Windows ile başlat: açık (" + exe + ")");
                return true;
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        public static bool Disable()
        {
            if (!IsEnabled()) return true;
            var r = ProcessRunner.Run("schtasks.exe", $"/delete /tn \"{TaskName}\" /f", null, 15000);
            if (!r.Ok) { Logger.Error("Zamanlanmış görev silinemedi: " + r.AllOutput); return false; }
            Logger.Info("Windows ile başlat: kapalı");
            return true;
        }

        public static void Apply(bool enabled)
        {
            if (enabled) Enable(); else Disable();
        }
    }
}
