using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;

namespace DevNanotek.Core
{
    /// <summary>
    /// Windows ortamı denetimleri. En önemlisi Akıllı Uygulama Denetimi (Smart App Control):
    /// açıkken imzasız/yeni sürüm sunucu programlarını (Apache DLL'leri, Mailpit, Nginx…) ve imzasız
    /// DevNanotek.exe'yi engeller. XAMPP / Laragon'un "kendiliğinden bozulmasının" en yaygın nedenidir.
    /// </summary>
    public static class SystemCheck
    {
        /// <summary>0 = kapalı, 1 = açık (engelliyor), 2 = değerlendirme modu, -1 = bilinmiyor</summary>
        public static int SmartAppControlState()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy"))
                {
                    var v = k?.GetValue("VerifiedAndReputablePolicyState");
                    return v == null ? 0 : Convert.ToInt32(v);
                }
            }
            catch { return -1; }
        }

        public static bool SmartAppControlOn => SmartAppControlState() == 1;

        /// <summary>Kod bütünlüğü ilkesi bir exe/DLL'i engellediğinde sürecin döndürdüğü çıkış kodu (0xC0E90002).</summary>
        public const int BlockedExitCode = unchecked((int)0xC0E90002);

        public static bool IsBlocked(ProcessResult r) => r != null && r.ExitCode == BlockedExitCode;

        public const string SmartAppControlHelp =
            "Windows 'Akıllı Uygulama Denetimi' (Smart App Control) AÇIK.\n\n" +
            "Bu özellik imzasız veya yeni yayınlanmış programları engeller: Apache'nin DLL'leri, Mailpit, Nginx gibi " +
            "sunucu bileşenleri çalışamaz. XAMPP ve Laragon da aynı sebeple bozulur.\n\n" +
            "Kapatmak için: Ayarlar > Gizlilik ve güvenlik > Windows Güvenliği > Uygulama ve tarayıcı denetimi > " +
            "Akıllı Uygulama Denetimi ayarları > Kapalı.\n\n" +
            "Not: Bu bir güvenlik özelliğidir; Windows sürümünüze bağlı olarak kapattıktan sonra yeniden açmak için " +
            "Windows'u sıfırlamanız gerekebilir. Kararı siz verin. Antivirüsünüz (Windows Defender) açık kalmaya devam eder.";

        /// <summary>Code Integrity günlüğünden son engellenen dosyalar (son 'minutes' dakika).</summary>
        public static List<string> RecentBlockedFiles(int minutes = 10)
        {
            var list = new List<string>();
            try
            {
                var since = DateTime.Now.AddMinutes(-minutes).ToUniversalTime().ToString("o");
                var r = ProcessRunner.Run("wevtutil.exe",
                    $"qe Microsoft-Windows-CodeIntegrity/Operational /c:60 /rd:true /f:text /q:\"*[System[(EventID=3033 or EventID=3077) and TimeCreated[@SystemTime>='{since}']]]\"",
                    null, 15000);
                foreach (var line in r.StdOut.Split('\n'))
                {
                    var i = line.IndexOf("attempted to load ", StringComparison.OrdinalIgnoreCase);
                    if (i < 0) continue;
                    var rest = line.Substring(i + 18).Trim();
                    var sp = rest.IndexOf(" that ", StringComparison.OrdinalIgnoreCase);
                    var path = sp > 0 ? rest.Substring(0, sp) : rest;
                    path = System.Text.RegularExpressions.Regex.Replace(path, @"^\\Device\\HarddiskVolume\d+", "");
                    if (!list.Contains(path)) list.Add(path);
                }
            }
            catch { }
            return list;
        }

        /// <summary>DevNanotek klasöründen engellenen dosya varsa açıklayıcı ek mesaj.</summary>
        public static string BlockedHint()
        {
            if (!SmartAppControlOn) return "";
            var root = Paths.Root.Substring(2); // "C:" kısmını at
            var ours = RecentBlockedFiles(15).Where(p => p.IndexOf(root, StringComparison.OrdinalIgnoreCase) >= 0).Take(6).ToList();
            if (ours.Count == 0) return "\n\nAkıllı Uygulama Denetimi açık; bileşenleri engelliyor olabilir (Yardım sayfasına bakın).";
            return "\n\nAkıllı Uygulama Denetimi şu dosyaları ENGELLEDİ:\n  " + string.Join("\n  ", ours) + "\n\n" + SmartAppControlHelp;
        }
    }
}
