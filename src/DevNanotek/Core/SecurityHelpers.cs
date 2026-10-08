using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace DevNanotek.Core
{
    /// <summary>
    /// Windows Güvenlik Duvarı. Varsayılanda sunucular yalnızca 127.0.0.1/::1 dinler (kural gerekmez, Windows soru sormaz).
    /// "Yerel ağdan erişim" açılırsa yalnızca ÖZEL ağ profili için port kuralı eklenir. Kaldırırken DEVNANOTEK klasöründeki
    /// programlara ait tüm kurallar (Windows'un "erişime izin ver" sorusuyla oluşanlar dahil) silinir.
    /// </summary>
    public static class FirewallManager
    {
        public const string RuleWeb = "DEVNANOTEK Web (yerel ağ)";
        public const string RuleDb = "DEVNANOTEK Veritabanı (yerel ağ)";

        public static void Apply(AppConfig cfg)
        {
            Delete(RuleWeb);
            Delete(RuleDb);
            if (cfg.LanAccess) Add(RuleWeb, cfg.SslEnabled ? cfg.HttpPort + "," + cfg.HttpsPort : cfg.HttpPort.ToString());
            if (cfg.DbBindAddress == "0.0.0.0") Add(RuleDb, cfg.DbPort.ToString());
        }

        private static void Add(string name, string ports)
        {
            var r = ProcessRunner.Run("netsh.exe", $"advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol=TCP localport={ports} profile=private", null, 20000);
            if (r.Ok) Logger.Info($"Güvenlik duvarı kuralı eklendi: {name} ({ports}, yalnız özel ağ)");
            else Logger.Warn("Güvenlik duvarı kuralı eklenemedi: " + r.AllOutput);
        }

        private static void Delete(string name) => ProcessRunner.Run("netsh.exe", $"advfirewall firewall delete rule name=\"{name}\"", null, 20000);

        /// <summary>Kök klasör altındaki programlara ait tüm gelen/giden kuralları siler. Dönüş: silinen program sayısı.</summary>
        public static int RemoveRulesUnder(string root)
        {
            var prefix = root.TrimEnd('\\') + "\\";
            var programs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));
                foreach (dynamic rule in policy.Rules)
                {
                    string app = null;
                    try { app = rule.ApplicationName as string; } catch { }
                    if (!string.IsNullOrEmpty(app) && app.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) programs.Add(app);
                }
            }
            catch (Exception ex) { Logger.Warn("Güvenlik duvarı kuralları okunamadı: " + ex.Message); }
            foreach (var p in programs)
            {
                ProcessRunner.Run("netsh.exe", $"advfirewall firewall delete rule name=all program=\"{p}\"", null, 20000);
                Logger.Info("Güvenlik duvarı kuralları silindi: " + p);
            }
            Delete(RuleWeb);
            Delete(RuleDb);
            return programs.Count;
        }
    }

    /// <summary>
    /// İsteğe bağlı hızlandırma: DEVNANOTEK klasörünü Windows Defender taramasından hariç tutar.
    /// phpMyAdmin gibi binlerce PHP dosyası okuyan sayfaların ilk açılışı belirgin hızlanır.
    /// Güvenlik ödünleşimi olduğu için yalnızca kullanıcı açarsa uygulanır; kaldırırken geri alınır.
    /// </summary>
    public static class DefenderManager
    {
        private static ProcessResult Ps(string command)
            => ProcessRunner.Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + command.Replace("\"", "\\\"") + "\"", null, 60000);

        public static bool IsExcluded()
        {
            var r = Ps("(Get-MpPreference).ExclusionPath -join '|'");
            return r.Ok && r.StdOut.Split('|').Any(p => string.Equals(p.Trim().TrimEnd('\\'), Paths.Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        }

        public static string SetExcluded(bool on)
        {
            var r = Ps((on ? "Add-MpPreference" : "Remove-MpPreference") + " -ExclusionPath '" + Paths.Root.Replace("'", "''") + "'");
            if (!r.Ok) return string.IsNullOrWhiteSpace(r.AllOutput) ? "Windows Defender ayarı değiştirilemedi (Kurcalama Koruması veya kurum ilkesi engelliyor olabilir)." : r.AllOutput.Trim();
            var cfg = AppConfig.Current; cfg.DefenderExcluded = on; cfg.Save();
            Logger.Info("Defender istisnası " + (on ? "eklendi: " : "kaldırıldı: ") + Paths.Root);
            return null;
        }
    }

    /// <summary>Windows'un tepsi simgesi önbelleğindeki DEVNANOTEK kaydı (kaldırırken temizlenir).</summary>
    public static class TrayIconRegistry
    {
        public static int RemoveEntriesUnder(string root)
        {
            int n = 0;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", true))
                {
                    if (k == null) return 0;
                    foreach (var name in k.GetSubKeyNames())
                    {
                        string path = null;
                        try { using (var s = k.OpenSubKey(name)) path = s?.GetValue("ExecutablePath") as string; } catch { }
                        if (string.IsNullOrEmpty(path) || !path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                        try { k.DeleteSubKeyTree(name, false); n++; } catch { }
                    }
                }
            }
            catch { }
            return n;
        }
    }

    /// <summary>
    /// Servisler başladıktan sonra localhost ve phpMyAdmin sayfalarını arka planda bir kez açar:
    /// PHP derlemesi (OPcache) ve dosya önbellekleri ısınır, kullanıcı ilk açtığında beklemez.
    /// </summary>
    public static class WarmUp
    {
        private static DateTime _last = DateTime.MinValue;

        public static void Run(AppConfig cfg)
        {
            if ((DateTime.Now - _last).TotalSeconds < 20) return;
            _last = DateTime.Now;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1500).ConfigureAwait(false);
                    using (var h = new HttpClient { Timeout = TimeSpan.FromSeconds(120) })
                    {
                        var baseUrl = "http://127.0.0.1:" + cfg.HttpPort;
                        foreach (var path in new[] { "/", "/phpmyadmin/" })
                        {
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            try
                            {
                                using (var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + path))
                                {
                                    req.Headers.Host = "localhost";
                                    using (var resp = await h.SendAsync(req).ConfigureAwait(false))
                                        Logger.Info($"Isıtma: {path} → {(int)resp.StatusCode} ({sw.ElapsedMilliseconds} ms)");
                                }
                            }
                            catch (Exception ex) { Logger.Info($"Isıtma atlandı ({path}): {ex.Message}"); }
                        }
                    }
                }
                catch { }
            });
        }
    }
}
