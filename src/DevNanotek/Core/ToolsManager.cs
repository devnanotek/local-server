using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Win32;

namespace DevNanotek.Core
{
    // ======================================================================
    public static class PhpMyAdminManager
    {
        public static string Dir => Paths.BinPhpMyAdmin;
        public static bool IsInstalled => File.Exists(Path.Combine(Dir, "index.php"));
        public static string ConfigFile => Path.Combine(Dir, "config.inc.php");

        public static string InstalledVersion()
        {
            try
            {
                var f = Directory.GetFiles(Dir, "RELEASE-DATE-*").FirstOrDefault();
                if (f != null) return Path.GetFileName(f).Substring("RELEASE-DATE-".Length);
            }
            catch { }
            return IsInstalled ? "?" : null;
        }

        private static string Secret()
        {
            var f = Path.Combine(Paths.Etc, "pma-secret.txt");
            try
            {
                if (File.Exists(f)) { var s = File.ReadAllText(f).Trim(); if (s.Length >= 32) return s; }
                var bytes = new byte[24];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                var secret = Convert.ToBase64String(bytes);
                File.WriteAllText(f, secret);
                return secret;
            }
            catch { return "DevNanotek-" + Environment.MachineName.PadRight(32, 'x'); }
        }

        public static bool WriteConfig(AppConfig cfg)
        {
            if (!IsInstalled) return false;
            Directory.CreateDirectory(Path.Combine(Paths.Tmp, "phpmyadmin"));
            var tokens = new Dictionary<string, string>
            {
                ["SECRET"] = Secret().Replace("'", ""),
                ["SERVER_LABEL"] = $"{DbManager.EngineTitle(cfg.DbEngine)} {cfg.DbVersion} (DEVNANOTEK)",
                ["AUTH_TYPE"] = "config",
                ["PORT"] = cfg.DbPort.ToString(),
                ["USER"] = "root",
                ["PASS"] = (cfg.DbRootPassword ?? "").Replace("\\", "\\\\").Replace("'", "\\'"),
                ["TEMP_DIR"] = Paths.Fwd(Path.Combine(Paths.Tmp, "phpmyadmin"))
            };
            return Templates.WriteIfChanged(ConfigFile, Templates.Render("config.inc.php.tpl", tokens));
        }
    }

    // ======================================================================
    public static class MailpitManager
    {
        public static string Exe => Path.Combine(Paths.BinMailpit, "mailpit.exe");
        public static bool IsInstalled => File.Exists(Exe);
        private static string VersionFile => Path.Combine(Paths.BinMailpit, ".version");

        public static string InstalledVersion()
        {
            try { return File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : (IsInstalled ? "?" : null); } catch { return null; }
        }

        public static void SaveVersion(string v) { try { File.WriteAllText(VersionFile, v ?? ""); } catch { } }
        public static string DbFile => Path.Combine(Paths.Data, "mailpit", "mailpit.db");

        public static bool EnsureService(AppConfig cfg, Action<string> log)
        {
            if (!IsInstalled) throw new Exception("Mailpit kurulu değil.");
            Directory.CreateDirectory(Path.GetDirectoryName(DbFile));
            var args = MailpitArgs(cfg);
            var xml = WinSwManager.BuildXml(WindowsServices.Mailpit, "DevNanotek Mailpit", $"DevNanotek test e-posta sunucusu — SMTP {cfg.SmtpPort}, arayüz http://localhost:{cfg.MailpitUiPort}",
                Exe, args, Paths.BinMailpit, null, null, null, Paths.LogsMailpit);
            var re = WinSwManager.EnsureService(WindowsServices.Mailpit, xml, log);
            WindowsServices.SetStartType(WindowsServices.Mailpit, cfg.AutoStartServices ? "auto" : "demand");
            return re;
        }

        public static string MailpitArgs(AppConfig cfg)
            => $"--smtp 127.0.0.1:{cfg.SmtpPort} --listen 127.0.0.1:{cfg.MailpitUiPort} --database \"{DbFile}\" --max 5000 --smtp-auth-accept-any --smtp-auth-allow-insecure";

        public static void RemoveService() => WinSwManager.Uninstall(WindowsServices.Mailpit);
        public static string UiUrl(AppConfig cfg) => $"http://localhost:{cfg.MailpitUiPort}/";
    }

    // ======================================================================
    public static class MkcertManager
    {
        public static string Exe => Path.Combine(Paths.BinMkcert, "mkcert.exe");
        public static bool IsInstalled => File.Exists(Exe);
        public static string CaRoot => Path.Combine(Paths.EtcSsl, "ca");
        public static string RootCaPem => Path.Combine(CaRoot, "rootCA.pem");
        public static bool CaExists => File.Exists(RootCaPem) && File.Exists(Path.Combine(CaRoot, "rootCA-key.pem"));
        public static bool CertsExist => File.Exists(Paths.SslCert) && File.Exists(Paths.SslKey);
        private static string HostsFileRecord => Path.Combine(Paths.EtcSsl, "cert-hosts.txt");

        private static Dictionary<string, string> Env() => new Dictionary<string, string> { ["CAROOT"] = CaRoot };

        private static X509Certificate2 LoadRootCa()
        {
            if (!File.Exists(RootCaPem)) return null;
            var pem = File.ReadAllText(RootCaPem);
            var b64 = System.Text.RegularExpressions.Regex.Replace(pem, @"-----[^-]+-----|\s", "");
            return new X509Certificate2(Convert.FromBase64String(b64));
        }

        /// <summary>Kök sertifika Yerel Makine "Güvenilen Kök" deposunda mı?</summary>
        public static bool CaTrusted
        {
            get
            {
                try
                {
                    var ca = LoadRootCa();
                    if (ca == null) return false;
                    using (var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
                    {
                        store.Open(OpenFlags.ReadOnly);
                        return store.Certificates.Find(X509FindType.FindByThumbprint, ca.Thumbprint, false).Count > 0;
                    }
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// CA yoksa mkcert ile üretir, sonra Yerel Makine güvenilen kök deposuna ekler.
        /// (Chrome, Edge ve Firefox Windows deposunu kullanır; kullanıcıdan onay penceresi çıkmaz.)
        /// </summary>
        public static bool InstallCa(Action<string> log = null)
        {
            Directory.CreateDirectory(CaRoot);
            if (!CaExists)
            {
                // Herhangi bir sertifika üretimi, CAROOT'ta CA yoksa önce CA'yı oluşturur
                var tmpCert = Path.Combine(Paths.Tmp, "mkcert-init.pem");
                var tmpKey = Path.Combine(Paths.Tmp, "mkcert-init-key.pem");
                var r = ProcessRunner.Run(Exe, $"-cert-file \"{tmpCert}\" -key-file \"{tmpKey}\" localhost", Paths.EtcSsl, 120000, Env());
                try { File.Delete(tmpCert); File.Delete(tmpKey); } catch { }
                if (!CaExists) { Logger.Error("mkcert CA oluşturulamadı: " + r.AllOutput); return false; }
            }
            try
            {
                var ca = LoadRootCa();
                using (var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
                {
                    store.Open(OpenFlags.ReadWrite);
                    if (store.Certificates.Find(X509FindType.FindByThumbprint, ca.Thumbprint, false).Count == 0)
                    {
                        store.Add(ca);
                        log?.Invoke("Yerel SSL kök sertifikası Windows'a güvenilir olarak eklendi.");
                        Logger.Info("mkcert kök sertifikası LocalMachine\\Root deposuna eklendi: " + ca.Subject);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Kök sertifika depoya eklenemedi (yönetici hakkı gerekir)", ex);
                return false;
            }
        }

        public static bool UninstallCa()
        {
            try
            {
                var ca = LoadRootCa();
                if (ca == null) return true;
                using (var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
                {
                    store.Open(OpenFlags.ReadWrite);
                    foreach (var c in store.Certificates.Find(X509FindType.FindByThumbprint, ca.Thumbprint, false)) store.Remove(c);
                }
                Logger.Info("mkcert kök sertifikası kaldırıldı");
                return true;
            }
            catch (Exception ex) { Logger.Error("Kök sertifika kaldırılamadı", ex); return false; }
        }

        /// <summary>localhost + *.tld + sanal hostlar için sertifika üretir (host listesi değişmediyse üretmez).</summary>
        public static bool EnsureCerts(AppConfig cfg, Action<string> log, bool force = false)
        {
            if (!IsInstalled) return false;
            if (!CaExists || !CaTrusted) { log?.Invoke("Yerel SSL kök sertifikası hazırlanıyor..."); InstallCa(log); }
            if (!CaExists) return false;
            var hosts = new List<string> { "localhost", "127.0.0.1", "::1", Environment.MachineName.ToLowerInvariant(), "*." + cfg.VhostTld };
            foreach (var h in Vhosts.HostNames(cfg))
            {
                if (!hosts.Contains(h)) hosts.Add(h);
                if (!hosts.Contains("www." + h)) hosts.Add("www." + h);
            }
            var record = string.Join("\n", hosts);
            bool upToDate = CertsExist && File.Exists(HostsFileRecord) && File.ReadAllText(HostsFileRecord) == record;
            if (!upToDate || force)
            {
                log?.Invoke("SSL sertifikası üretiliyor: " + string.Join(", ", hosts));
                var args = $"-cert-file \"{Paths.SslCert}\" -key-file \"{Paths.SslKey}\" " + string.Join(" ", hosts.Select(h => "\"" + h + "\""));
                var r = ProcessRunner.Run(Exe, args, Paths.EtcSsl, 120000, Env());
                if (!r.Ok || !CertsExist) { Logger.Error("mkcert sertifika üretimi: " + r.AllOutput); return false; }
                File.WriteAllText(HostsFileRecord, record);
                Logger.Info("SSL sertifikası üretildi");
            }
            WriteCaBundle();
            return true;
        }

        /// <summary>cacert.pem (Mozilla kökleri) + mkcert kökü = ca-bundle.pem (PHP curl / openssl için).</summary>
        public static void WriteCaBundle()
        {
            try
            {
                var sb = new StringBuilder();
                if (File.Exists(Paths.CaCertPem)) sb.AppendLine(File.ReadAllText(Paths.CaCertPem).TrimEnd());
                if (File.Exists(RootCaPem))
                {
                    sb.AppendLine();
                    sb.AppendLine("DevNanotek yerel kok sertifikasi (mkcert)");
                    sb.AppendLine("=========================================");
                    sb.AppendLine(File.ReadAllText(RootCaPem).Trim());
                }
                if (sb.Length > 0) Templates.WriteIfChanged(Paths.CaBundle, sb.ToString());
            }
            catch (Exception ex) { Logger.Warn("ca-bundle.pem yazılamadı: " + ex.Message); }
        }
    }

    // ======================================================================
    public static class ComposerManager
    {
        public static string Phar => Path.Combine(Paths.BinComposer, "composer.phar");
        public static bool IsInstalled => File.Exists(Phar);
    }

    // ======================================================================
    public static class NodeManager
    {
        public static string Dir(string version) => Path.Combine(Paths.BinNode, "node-v" + version);
        public static string Exe(string version) => Path.Combine(Dir(version), "node.exe");
        public static bool IsInstalled(string version) => !string.IsNullOrEmpty(version) && File.Exists(Exe(version));

        public static List<string> Installed()
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(Paths.BinNode))
                    foreach (var d in Directory.GetDirectories(Paths.BinNode, "node-v*"))
                        if (File.Exists(Path.Combine(d, "node.exe"))) list.Add(Path.GetFileName(d).Substring(6));
            }
            catch { }
            return list.OrderByDescending(Catalog.VersionKey).ToList();
        }
    }

    // ======================================================================
    public static class CaCertManager
    {
        public static bool IsInstalled => File.Exists(Paths.CaCertPem);
    }

    // ======================================================================
    public static class VcRedist
    {
        /// <summary>Bileşen mimarisine uygun (x64 / x86) Visual C++ v14 çalışma zamanı sürümü.</summary>
        public static string InstalledVersion()
        {
            try
            {
                var arch = SystemInfo.IsX86 ? "x86" : "x64";
                var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                {
                    // 64 bit Windows'ta x86 çalışma zamanı WOW6432Node altında kayıtlıdır
                    var paths = new[] { $@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\{arch}", $@"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\{arch}" };
                    foreach (var p in paths)
                        using (var k = hklm.OpenSubKey(p))
                        {
                            if (k == null || Convert.ToInt32(k.GetValue("Installed", 0)) != 1) continue;
                            return k.GetValue("Version") as string;
                        }
                }
            }
            catch { }
            return null;
        }

        /// <summary>Apache Lounge VS18 derlemeleri 14.50+ ister (PHP VS16/VS17 için 14.30+ yeterli).</summary>
        public static bool IsAdequate()
        {
            var v = InstalledVersion();
            if (string.IsNullOrEmpty(v)) return false;
            var m = System.Text.RegularExpressions.Regex.Match(v, @"v?(\d+)\.(\d+)");
            if (!m.Success) return false;
            int major = int.Parse(m.Groups[1].Value), minor = int.Parse(m.Groups[2].Value);
            return major > 14 || (major == 14 && minor >= 50);
        }

        public static ProcessResult Install(string installerPath)
        {
            var r = ProcessRunner.Run(installerPath, "/install /quiet /norestart", Paths.Downloads, 600000);
            // 0 = ok, 1638 = daha yenisi kurulu, 3010 = yeniden başlatma gerekli
            if (r.ExitCode == 1638 || r.ExitCode == 3010) r.ExitCode = 0;
            return r;
        }
    }
}
