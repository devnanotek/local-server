using System;
using System.IO;
using Microsoft.Win32;

namespace DevNanotek.Core
{
    /// <summary>
    /// Windows 11 yeni tepsi simgelerini "^" taşma alanına gizler. Simgemizi bir kez görev çubuğunda
    /// görünür yapar (HKCU\Control Panel\NotifyIconSettings\*\IsPromoted = 1). Kullanıcı sonra gizlerse
    /// tekrar zorlanmaz (config.TrayPromoted). Windows 10'da bu anahtar yoktur; işlem yapılmaz.
    /// </summary>
    public static class TrayVisibility
    {
        public static void PromoteOnce(AppConfig cfg)
        {
            if (cfg.TrayPromoted) return;
            try
            {
                var exe = Paths.ExePath;
                if (string.IsNullOrEmpty(exe)) return;
                using (var root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", true))
                {
                    if (root == null) return; // Windows 10
                    foreach (var name in root.GetSubKeyNames())
                    {
                        try
                        {
                            using (var k = root.OpenSubKey(name, true))
                            {
                                var path = k?.GetValue("ExecutablePath") as string;
                                if (string.IsNullOrEmpty(path) || !string.Equals(path.Trim(), exe, StringComparison.OrdinalIgnoreCase)) continue;
                                k.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                                cfg.TrayPromoted = true;
                                cfg.Save();
                                Logger.Info("Tepsi simgesi görev çubuğunda görünür yapıldı");
                                return;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { Logger.Warn("Tepsi simgesi görünür yapılamadı: " + ex.Message); }
        }
    }
}
