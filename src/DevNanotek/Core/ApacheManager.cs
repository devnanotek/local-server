using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DevNanotek.Core
{
    /// <summary>Apache HTTP Server: yapılandırma üretimi ve Windows servisi.</summary>
    public static class ApacheManager
    {
        public const string Generated = "# DevNanotek-generated";

        public static string Dir(string version) => Path.Combine(Paths.BinApache, "httpd-" + version);
        public static string Exe(string version) => Path.Combine(Dir(version), "bin", "httpd.exe");

        public static List<string> Installed()
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(Paths.BinApache))
                    foreach (var d in Directory.GetDirectories(Paths.BinApache, "httpd-*"))
                        if (File.Exists(Path.Combine(d, "bin", "httpd.exe"))) list.Add(Path.GetFileName(d).Substring(6));
            }
            catch { }
            return list.OrderByDescending(Catalog.VersionKey).ToList();
        }

        public static string ActiveVersion() => Installed().FirstOrDefault();
        public static bool IsInstalled => ActiveVersion() != null;

        // ------------------------------------------------------------------
        /// <summary>httpd.conf + vhosts yazar. Dönüş: herhangi bir dosya değişti mi.</summary>
        public static bool WriteConfig(AppConfig cfg, bool sslReady)
        {
            var ver = ActiveVersion();
            if (ver == null) throw new Exception("Apache kurulu değil.");
            bool changed = false;
            var logs = Paths.Fwd(Paths.LogsApache);
            var docroot = Paths.Fwd(cfg.EffectiveDocRoot);

            Templates.EnsureFile(Paths.ApacheCustomConf,
                L.Pick("# DevNanotek — Apache için kendi eklemeleriniz. Bu dosya asla üzerine yazılmaz.\r\n# Örnek: Node/socket.io uygulamasını /app altında yayınla\r\n",
                       "# DevNanotek — your own Apache additions. This file is never overwritten.\r\n# Example: serve a Node/socket.io app under /app\r\n") +
                "#   ProxyPass \"/app/\" \"http://127.0.0.1:3000/\"\r\n" +
                "#   ProxyPassReverse \"/app/\" \"http://127.0.0.1:3000/\"\r\n" +
                "#   ProxyPass \"/socket.io/\" \"ws://127.0.0.1:3000/socket.io/\"\r\n");

            // PHP bloğu
            var php = new StringBuilder();
            if (PhpManager.IsInstalled(cfg.PhpVersion))
            {
                var pdir = Paths.Fwd(PhpManager.Dir(cfg.PhpVersion));
                var dll = PhpManager.ApacheModuleDll(cfg.PhpVersion);
                if (dll == null) throw new Exception($"PHP {cfg.PhpVersion} içinde Apache modülü (php*apache2_4.dll) yok. Thread-Safe (TS) sürüm gerekir.");
                php.AppendLine($"# ---- PHP {cfg.PhpVersion} (mod_php) ------------------------------------------------");
                foreach (var d in PhpManager.DependencyDlls(cfg.PhpVersion)) php.AppendLine($"LoadFile \"{pdir}/{d}\"");
                php.AppendLine($"LoadModule {PhpManager.ApacheModuleId(cfg.PhpVersion)} \"{pdir}/{dll}\"");
                php.AppendLine($"PHPIniDir \"{pdir}\"");
                php.AppendLine("<FilesMatch \"\\.(php|phtml|php[0-9])$\">");
                php.AppendLine("    SetHandler application/x-httpd-php");
                php.AppendLine("</FilesMatch>");
                php.AppendLine("<FilesMatch \"\\.phps$\">");
                php.AppendLine("    SetHandler application/x-httpd-php-source");
                php.AppendLine("</FilesMatch>");
            }
            else php.AppendLine("# PHP kurulu değil / seçilmedi");

            // phpMyAdmin bloğu
            var pma = new StringBuilder();
            if (PhpMyAdminManager.IsInstalled)
            {
                var pdir = Paths.Fwd(PhpMyAdminManager.Dir);
                pma.AppendLine($"Alias /phpmyadmin \"{pdir}\"");
                pma.AppendLine($"<Directory \"{pdir}\">");
                pma.AppendLine("    Options FollowSymLinks");
                pma.AppendLine("    AllowOverride All");
                pma.AppendLine("    Require local");
                if (PhpManager.IsInstalled(cfg.PhpVersion))
                {
                    pma.AppendLine($"    <IfModule {PhpManager.ApacheModuleId(cfg.PhpVersion)}>");
                    pma.AppendLine("        php_admin_value display_errors Off");
                    pma.AppendLine("        php_admin_value error_reporting 0");
                    pma.AppendLine("    </IfModule>");
                }
                pma.AppendLine("</Directory>");
                foreach (var sub in new[] { "libraries", "templates", "setup/lib" })
                    pma.AppendLine($"<Directory \"{pdir}/{sub}\">\r\n    Require all denied\r\n</Directory>");
            }
            else pma.AppendLine("# phpMyAdmin kurulu değil");

            // Adminer (http://localhost/adminer) — tüm veritabanları için
            if (AdminerManager.IsInstalled)
            {
                var adir = Paths.Fwd(AdminerManager.Dir);
                pma.AppendLine($"Alias /adminer \"{adir}\"");
                pma.AppendLine($"<Directory \"{adir}\">");
                pma.AppendLine("    Options None");
                pma.AppendLine("    AllowOverride None");
                pma.AppendLine("    DirectoryIndex index.php");
                pma.AppendLine("    Require local");
                pma.AppendLine("</Directory>");
            }

            // SSL genel
            var ssl = new StringBuilder();
            if (sslReady)
            {
                ssl.AppendLine(ListenLines(cfg, cfg.HttpsPort));
                ssl.AppendLine("SSLCipherSuite HIGH:MEDIUM:!MD5:!RC4:!3DES");
                ssl.AppendLine("SSLProxyCipherSuite HIGH:MEDIUM:!MD5:!RC4:!3DES");
                ssl.AppendLine("SSLHonorCipherOrder on");
                ssl.AppendLine("SSLProtocol all -SSLv3 -TLSv1 -TLSv1.1");
                ssl.AppendLine("SSLProxyProtocol all -SSLv3 -TLSv1 -TLSv1.1");
                ssl.AppendLine("SSLPassPhraseDialog builtin");
            }
            else ssl.AppendLine("# SSL kapalı (Ayarlar > SSL'den açabilirsiniz)");
            // mod_ssl her zaman yüklü: oturum önbelleği tanımlı olmazsa günlüğe gereksiz uyarı düşer
            ssl.AppendLine($"SSLSessionCache \"shmcb:{logs}/ssl_scache(512000)\"");
            ssl.AppendLine("SSLSessionCacheTimeout 300");

            var tokens = new Dictionary<string, string>
            {
                ["SRVROOT"] = Paths.Fwd(Dir(ver)),
                ["HTTP_PORT"] = cfg.HttpPort.ToString(),
                ["LISTEN_HTTP"] = ListenLines(cfg, cfg.HttpPort),
                ["DOCROOT"] = docroot,
                ["LOGS"] = logs,
                ["PHP_BLOCK"] = php.ToString().TrimEnd(),
                ["PMA_BLOCK"] = pma.ToString().TrimEnd(),
                ["SSL_BLOCK"] = ssl.ToString().TrimEnd(),
                ["VHOSTS_DIR"] = Paths.Fwd(Paths.EtcApacheVhosts),
                ["CUSTOM_CONF"] = Paths.Fwd(Paths.ApacheCustomConf),
                ["ROOT"] = Paths.Fwd(Paths.Root),
                ["DB_PORT"] = cfg.DbPort.ToString(),
                ["PG_PORT"] = cfg.PgPort.ToString(),
                ["MAILPIT_PORT"] = cfg.MailpitUiPort.ToString(),
                ["SMTP_PORT"] = cfg.SmtpPort.ToString(),
                ["PHP_VERSION"] = cfg.PhpVersion ?? ""
            };
            changed |= Templates.WriteIfChanged(Paths.ApacheConf, Templates.Render("httpd.conf.tpl", tokens));
            changed |= WriteVhosts(cfg, sslReady, docroot, logs);
            return changed;
        }

        /// <summary>Yalnız bu bilgisayar: 127.0.0.1 + [::1] (Windows güvenlik duvarı soru sormaz). Yerel ağ açıksa tüm arayüzler.</summary>
        private static string ListenLines(AppConfig cfg, int port)
            => cfg.LanAccess ? $"Listen {port}" : $"Listen 127.0.0.1:{port}\r\nListen [::1]:{port}";

        private static bool WriteVhosts(AppConfig cfg, bool sslReady, string docroot, string logs)
        {
            bool changed = false;
            Directory.CreateDirectory(Paths.EtcApacheVhosts);
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string Block(string host, string aliases, string root, int port, bool isSsl, string logName)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"<VirtualHost *:{port}>");
                sb.AppendLine($"    ServerName {host}");
                if (!string.IsNullOrEmpty(aliases)) sb.AppendLine($"    ServerAlias {aliases}");
                sb.AppendLine($"    DocumentRoot \"{root}\"");
                sb.AppendLine($"    <Directory \"{root}\">");
                sb.AppendLine("        Options Indexes FollowSymLinks Includes ExecCGI");
                sb.AppendLine("        AllowOverride All");
                sb.AppendLine("        Require all granted");
                sb.AppendLine("    </Directory>");
                if (isSsl)
                {
                    sb.AppendLine("    SSLEngine on");
                    sb.AppendLine($"    SSLCertificateFile \"{Paths.Fwd(Paths.SslCert)}\"");
                    sb.AppendLine($"    SSLCertificateKeyFile \"{Paths.Fwd(Paths.SslKey)}\"");
                    sb.AppendLine("    <FilesMatch \"\\.(cgi|shtml|phtml|php)$\">");
                    sb.AppendLine("        SSLOptions +StdEnvVars");
                    sb.AppendLine("    </FilesMatch>");
                }
                sb.AppendLine($"    ErrorLog \"{logs}/{logName}-error.log\"");
                sb.AppendLine($"    CustomLog \"{logs}/{logName}-access.log\" common");
                sb.AppendLine("</VirtualHost>");
                return sb.ToString();
            }

            // 00-localhost: her zaman ilk — ad eşleşmeyen istekler buraya düşer
            var machine = Environment.MachineName.ToLowerInvariant();
            var lh = new StringBuilder();
            lh.AppendLine(Generated + " — localhost (otomatik, elle değiştirmeyin)");
            lh.Append(Block("localhost", "127.0.0.1 ::1 " + machine + " *.localhost", docroot, cfg.HttpPort, false, "localhost"));
            if (sslReady) lh.Append(Block("localhost", "127.0.0.1 ::1 " + machine + " *.localhost", docroot, cfg.HttpsPort, true, "localhost-ssl"));
            var lhFile = Path.Combine(Paths.EtcApacheVhosts, "00-localhost.conf");
            changed |= Templates.WriteIfChanged(lhFile, lh.ToString());
            keep.Add(lhFile);

            foreach (var v in Vhosts.Compute(cfg))
            {
                var root = Paths.Fwd(v.DocRoot);
                var sb = new StringBuilder();
                sb.AppendLine(Generated + $" — {v.Host} -> {root}");
                sb.Append(Block(v.Host, "www." + v.Host, root, cfg.HttpPort, false, v.Host));
                if (sslReady) sb.Append(Block(v.Host, "www." + v.Host, root, cfg.HttpsPort, true, v.Host + "-ssl"));
                var file = Path.Combine(Paths.EtcApacheVhosts, "10-" + v.Host + ".conf");
                changed |= Templates.WriteIfChanged(file, sb.ToString());
                keep.Add(file);
            }

            // bizim ürettiğimiz eski vhost dosyalarını temizle (kullanıcı dosyalarına dokunma)
            foreach (var f in Directory.GetFiles(Paths.EtcApacheVhosts, "*.conf"))
            {
                if (keep.Contains(f)) continue;
                try
                {
                    var first = File.ReadLines(f).FirstOrDefault() ?? "";
                    if (first.StartsWith(Generated)) { File.Delete(f); changed = true; }
                }
                catch { }
            }
            return changed;
        }

        // ------------------------------------------------------------------
        public static ProcessResult TestConfig()
        {
            var ver = ActiveVersion();
            var env = ServiceEnv(AppConfig.Current);
            return ProcessRunner.Run(Exe(ver), $"-t -f \"{Paths.ApacheConf}\"", Path.Combine(Dir(ver), "bin"), 30000, env);
        }

        public static Dictionary<string, string> ServiceEnv(AppConfig cfg)
        {
            var parts = new List<string>();
            if (PhpManager.IsInstalled(cfg.PhpVersion)) parts.Add(PhpManager.Dir(cfg.PhpVersion));
            var ver = ActiveVersion();
            if (ver != null) parts.Add(Path.Combine(Dir(ver), "bin"));
            if (Directory.Exists(EnvPath.CurrentNode)) parts.Add(EnvPath.CurrentNode);
            if (Directory.Exists(Paths.BinShims)) parts.Add(Paths.BinShims);
            var dbBin = Path.Combine(EnvPath.CurrentDb, "bin");
            if (Directory.Exists(dbBin)) parts.Add(dbBin);
            var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
            parts.Add(machinePath);
            return new Dictionary<string, string>
            {
                ["PATH"] = string.Join(";", parts),
                ["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows",
                ["TEMP"] = Paths.Tmp,
                ["TMP"] = Paths.Tmp,
                ["DEVNANOTEK_ROOT"] = Paths.Root
            };
        }

        /// <summary>Servisi kurar (sürüm değiştiyse yeniden kurar). Dönüş: yeniden kuruldu mu.</summary>
        public static bool InstallService(AppConfig cfg, Action<string> log)
        {
            var ver = ActiveVersion();
            if (ver == null) throw new Exception("Apache kurulu değil.");
            var exe = Exe(ver);
            bool reinstalled = false;
            var image = WindowsServices.GetImagePath(WindowsServices.Apache) ?? "";
            bool exists = WindowsServices.Exists(WindowsServices.Apache);
            // Apache, -f yolunu ImagePath'e değil Parameters\ConfigArgs değerine yazar
            var confArgs = ReadConfigArgs();
            bool sameExe = image.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0
                           && (confArgs == null || confArgs.IndexOf(Paths.ApacheConf, StringComparison.OrdinalIgnoreCase) >= 0
                               || confArgs.IndexOf(Paths.Fwd(Paths.ApacheConf), StringComparison.OrdinalIgnoreCase) >= 0);
            if (exists && !sameExe)
            {
                log?.Invoke("Apache servisi farklı sürüme işaret ediyor, yeniden kuruluyor...");
                UninstallService();
                exists = false;
            }
            if (!exists)
            {
                log?.Invoke("Apache servisi kuruluyor...");
                var r = ProcessRunner.Run(exe, $"-k install -n \"{WindowsServices.Apache}\" -f \"{Paths.ApacheConf}\"", Path.Combine(Dir(ver), "bin"), 60000, ServiceEnv(cfg));
                if (!WindowsServices.Exists(WindowsServices.Apache))
                    throw new Exception("Apache servisi kurulamadı:\r\n" + r.AllOutput);
                reinstalled = true;
                Logger.Info("Apache servisi kuruldu: " + r.AllOutput.Trim());
            }
            WindowsServices.SetEnvironment(WindowsServices.Apache, ServiceEnv(cfg));
            WindowsServices.SetDescription(WindowsServices.Apache, $"DevNanotek Apache {ver} + PHP {cfg.PhpVersion} — {Paths.ApacheConf}");
            WindowsServices.SetAutoRecovery(WindowsServices.Apache);
            WindowsServices.SetStartType(WindowsServices.Apache, cfg.AutoStartServices ? "auto" : "demand");
            return reinstalled;
        }

        public static void UninstallService()
        {
            if (!WindowsServices.Exists(WindowsServices.Apache)) return;
            WindowsServices.Stop(WindowsServices.Apache, 60);
            var image = WindowsServices.GetImagePath(WindowsServices.Apache) ?? "";
            var exe = ExtractExe(image);
            if (exe != null && File.Exists(exe))
                ProcessRunner.Run(exe, $"-k uninstall -n \"{WindowsServices.Apache}\"", Path.GetDirectoryName(exe), 60000);
            if (WindowsServices.Exists(WindowsServices.Apache)) WindowsServices.Delete(WindowsServices.Apache);
            Logger.Info("Apache servisi kaldırıldı");
        }

        private static string ReadConfigArgs()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + WindowsServices.Apache + @"\Parameters"))
                {
                    var v = k?.GetValue("ConfigArgs");
                    if (v is string[] arr) return string.Join(" ", arr);
                    return v?.ToString();
                }
            }
            catch { return null; }
        }

        public static string ExtractExe(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath)) return null;
            imagePath = imagePath.Trim();
            if (imagePath.StartsWith("\""))
            {
                var end = imagePath.IndexOf('"', 1);
                return end > 0 ? imagePath.Substring(1, end - 1) : null;
            }
            var sp = imagePath.IndexOf(" -");
            return sp > 0 ? imagePath.Substring(0, sp) : imagePath;
        }
    }
}
