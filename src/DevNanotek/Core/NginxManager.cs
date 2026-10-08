using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DevNanotek.Core
{
    /// <summary>Nginx + php-cgi (FastCGI) — WinSW servisleri olarak çalışır.</summary>
    public static class NginxManager
    {
        public const string Generated = "# DevNanotek-generated";

        public static string Dir(string version) => Path.Combine(Paths.BinNginx, "nginx-" + version);
        public static string Exe(string version) => Path.Combine(Dir(version), "nginx.exe");

        public static List<string> Installed()
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(Paths.BinNginx))
                    foreach (var d in Directory.GetDirectories(Paths.BinNginx, "nginx-*"))
                        if (File.Exists(Path.Combine(d, "nginx.exe"))) list.Add(Path.GetFileName(d).Substring(6));
            }
            catch { }
            return list.OrderByDescending(Catalog.VersionKey).ToList();
        }

        public static string ActiveVersion() => Installed().FirstOrDefault();
        public static bool IsInstalled => ActiveVersion() != null;

        private static string Prefix(string ver) => Paths.Fwd(Dir(ver)) + "/";
        public static string NginxArgs(string ver) => $"-p \"{Prefix(ver)}\" -c \"{Paths.Fwd(Paths.NginxConf)}\"";

        // ------------------------------------------------------------------
        public static bool WriteConfig(AppConfig cfg, bool sslReady)
        {
            var ver = ActiveVersion();
            if (ver == null) throw new Exception("Nginx kurulu değil.");
            bool changed = false;
            var logs = Paths.Fwd(Paths.LogsNginx);
            var docroot = Paths.Fwd(cfg.EffectiveDocRoot);
            var ndir = Paths.Fwd(Dir(ver));
            Directory.CreateDirectory(Path.Combine(Dir(ver), "logs"));
            Directory.CreateDirectory(Path.Combine(Dir(ver), "temp"));

            Templates.EnsureFile(Paths.NginxCustomConf,
                "# DevNanotek — Nginx için kendi eklemeleriniz (http {} bloğu içinde). Bu dosya asla üzerine yazılmaz.\r\n" +
                "# Örnek: ek bir server bloğu veya upstream tanımı buraya yazılabilir.\r\n");

            var fcgi = new StringBuilder();
            for (int i = 0; i < cfg.FcgiChildren; i++) fcgi.AppendLine($"        server 127.0.0.1:{cfg.FcgiPort + i};");

            var tokens = new Dictionary<string, string>
            {
                ["LOGS"] = logs,
                ["NGINX_DIR"] = ndir,
                ["FCGI_SERVERS"] = fcgi.ToString().TrimEnd(),
                ["VHOSTS_DIR"] = Paths.Fwd(Paths.EtcNginxVhosts),
                ["CUSTOM_CONF"] = Paths.Fwd(Paths.NginxCustomConf),
                ["ROOT"] = Paths.Fwd(Paths.Root)
            };
            changed |= Templates.WriteIfChanged(Paths.NginxConf, Templates.Render("nginx.conf.tpl", tokens));
            changed |= WriteVhosts(cfg, sslReady, docroot, logs, ndir);
            return changed;
        }

        private static string Server(AppConfig cfg, string names, string root, int port, bool isSsl, string logName, string ndir, string logs, bool frontController, bool withPma)
        {
            var sb = new StringBuilder();
            sb.AppendLine("server {");
            var sslSuffix = isSsl ? " ssl" : "";
            if (cfg.LanAccess) sb.AppendLine($"    listen {port}{sslSuffix};");
            else
            {
                // yalnızca bu bilgisayar (güvenlik duvarı sorusu ve dış erişim yok)
                sb.AppendLine($"    listen 127.0.0.1:{port}{sslSuffix};");
                sb.AppendLine($"    listen [::1]:{port}{sslSuffix};");
            }
            sb.AppendLine($"    server_name {names};");
            sb.AppendLine($"    root \"{root}\";");
            sb.AppendLine("    index index.php index.html index.htm;");
            sb.AppendLine("    autoindex on;");
            sb.AppendLine($"    access_log \"{logs}/{logName}-access.log\" main;");
            sb.AppendLine($"    error_log  \"{logs}/{logName}-error.log\" warn;");
            if (isSsl)
            {
                sb.AppendLine($"    ssl_certificate     \"{Paths.Fwd(Paths.SslCert)}\";");
                sb.AppendLine($"    ssl_certificate_key \"{Paths.Fwd(Paths.SslKey)}\";");
                sb.AppendLine("    ssl_protocols TLSv1.2 TLSv1.3;");
            }
            // Not: fastcgi_param dizileri üst bloktan miras alınmaz; bu yüzden her PHP konumuna ayrı yazılır.
            var dnParams =
                $"            fastcgi_param DEVNANOTEK_ROOT \"{Paths.Fwd(Paths.Root)}\";\r\n" +
                $"            fastcgi_param DEVNANOTEK_DB_PORT \"{cfg.DbPort}\";\r\n" +
                $"            fastcgi_param DEVNANOTEK_PG_PORT \"{cfg.PgPort}\";\r\n" +
                $"            fastcgi_param DEVNANOTEK_SMTP_PORT \"{cfg.SmtpPort}\";\r\n" +
                $"            fastcgi_param DEVNANOTEK_MAILPIT_PORT \"{cfg.MailpitUiPort}\";\r\n";
            if (isSsl) dnParams += "            fastcgi_param HTTPS on;\r\n";
            if (withPma && PhpMyAdminManager.IsInstalled)
            {
                var pdir = Paths.Fwd(PhpMyAdminManager.Dir);
                sb.AppendLine("    location ^~ /phpmyadmin {");
                sb.AppendLine($"        alias \"{pdir}\";");
                sb.AppendLine("        index index.php;");
                sb.AppendLine("        allow 127.0.0.1; allow ::1; deny all;");
                sb.AppendLine("        location ~ ^/phpmyadmin/(libraries|templates|setup/lib|vendor)/ { deny all; }");
                sb.AppendLine("        location ~ \\.php$ {");
                sb.AppendLine($"            include \"{ndir}/conf/fastcgi_params\";");
                sb.AppendLine("            fastcgi_param SCRIPT_FILENAME $request_filename;");
                sb.Append(dnParams);
                sb.AppendLine("            fastcgi_pass devnanotek_php;");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
            }
            if (withPma && AdminerManager.IsInstalled)
            {
                var adir = Paths.Fwd(AdminerManager.Dir);
                sb.AppendLine("    location ^~ /adminer {");
                sb.AppendLine($"        alias \"{adir}\";");
                sb.AppendLine("        index index.php;");
                sb.AppendLine("        allow 127.0.0.1; allow ::1; deny all;");
                sb.AppendLine("        location ~ \\.php$ {");
                sb.AppendLine($"            include \"{ndir}/conf/fastcgi_params\";");
                sb.AppendLine("            fastcgi_param SCRIPT_FILENAME $request_filename;");
                sb.Append(dnParams);
                sb.AppendLine("            fastcgi_pass devnanotek_php;");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
            }
            sb.AppendLine("    location / {");
            sb.AppendLine(frontController ? "        try_files $uri $uri/ /index.php?$query_string;" : "        try_files $uri $uri/ =404;");
            sb.AppendLine("    }");
            sb.AppendLine("    location ~ \\.php$ {");
            sb.AppendLine("        try_files $uri =404;");
            sb.AppendLine($"        include \"{ndir}/conf/fastcgi_params\";");
            sb.AppendLine("        fastcgi_param SCRIPT_FILENAME $document_root$fastcgi_script_name;");
            sb.Append(dnParams.Replace("            fastcgi_param", "        fastcgi_param"));
            sb.AppendLine("        fastcgi_pass devnanotek_php;");
            sb.AppendLine("    }");
            sb.AppendLine("    location ~ /\\.(ht|git|env) { deny all; }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static bool WriteVhosts(AppConfig cfg, bool sslReady, string docroot, string logs, string ndir)
        {
            bool changed = false;
            Directory.CreateDirectory(Paths.EtcNginxVhosts);
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var machine = Environment.MachineName.ToLowerInvariant();

            var lh = new StringBuilder();
            lh.AppendLine(Generated + " — localhost (otomatik, elle değiştirmeyin)");
            lh.Append(Server(cfg, $"localhost 127.0.0.1 {machine} *.localhost", docroot, cfg.HttpPort, false, "localhost", ndir, logs, false, true));
            if (sslReady) lh.Append(Server(cfg, $"localhost 127.0.0.1 {machine} *.localhost", docroot, cfg.HttpsPort, true, "localhost-ssl", ndir, logs, false, true));
            var lhFile = Path.Combine(Paths.EtcNginxVhosts, "00-localhost.conf");
            changed |= Templates.WriteIfChanged(lhFile, lh.ToString());
            keep.Add(lhFile);

            foreach (var v in Vhosts.Compute(cfg))
            {
                var root = Paths.Fwd(v.DocRoot);
                var sb = new StringBuilder();
                sb.AppendLine(Generated + $" — {v.Host} -> {root}");
                sb.Append(Server(cfg, $"{v.Host} www.{v.Host}", root, cfg.HttpPort, false, v.Host, ndir, logs, true, false));
                if (sslReady) sb.Append(Server(cfg, $"{v.Host} www.{v.Host}", root, cfg.HttpsPort, true, v.Host + "-ssl", ndir, logs, true, false));
                var file = Path.Combine(Paths.EtcNginxVhosts, "10-" + v.Host + ".conf");
                changed |= Templates.WriteIfChanged(file, sb.ToString());
                keep.Add(file);
            }

            foreach (var f in Directory.GetFiles(Paths.EtcNginxVhosts, "*.conf"))
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

        public static ProcessResult TestConfig()
        {
            var ver = ActiveVersion();
            return ProcessRunner.Run(Exe(ver), "-t " + NginxArgs(ver), Dir(ver), 30000);
        }

        // ------------------------------------------------------------------
        //  Servisler (WinSW)
        // ------------------------------------------------------------------
        public static bool InstallServices(AppConfig cfg, Action<string> log)
        {
            var ver = ActiveVersion();
            if (ver == null) throw new Exception("Nginx kurulu değil.");
            bool re = false;

            var nginxXml = WinSwManager.BuildXml(WindowsServices.Nginx, "DevNanotek Nginx", $"DevNanotek Nginx {ver} — {Paths.NginxConf}",
                Exe(ver), NginxArgs(ver), Dir(ver), new Dictionary<string, string> { ["DEVNANOTEK_ROOT"] = Paths.Root },
                Exe(ver), "-s stop " + NginxArgs(ver), Paths.LogsWinSw);
            re |= WinSwManager.EnsureService(WindowsServices.Nginx, nginxXml, log);

            if (PhpManager.IsInstalled(cfg.PhpVersion))
            {
                var phpDir = PhpManager.Dir(cfg.PhpVersion);
                var env = ApacheManager.ServiceEnv(cfg);
                env["PHP_FCGI_MAX_REQUESTS"] = "0";
                env["DEVNANOTEK_SMTP_PORT"] = cfg.SmtpPort.ToString();
                env["DEVNANOTEK_PG_PORT"] = cfg.PgPort.ToString();
                var helper = EnsureFcgiHelper(log);
                var args = $"--fcgi-spawner --php \"{PhpManager.CgiExe(cfg.PhpVersion)}\" --host 127.0.0.1 --port {cfg.FcgiPort} --count {cfg.FcgiChildren} --ini \"{phpDir}\"";
                var fcgiXml = WinSwManager.BuildXml(WindowsServices.PhpFcgi, "DevNanotek PHP FastCGI", $"DevNanotek PHP {cfg.PhpVersion} php-cgi havuzu ({cfg.FcgiChildren} süreç, port {cfg.FcgiPort}+)",
                    helper, args, phpDir, env, null, null, Paths.LogsWinSw);
                re |= WinSwManager.EnsureService(WindowsServices.PhpFcgi, fcgiXml, log);
            }
            else if (WindowsServices.Exists(WindowsServices.PhpFcgi)) WinSwManager.Uninstall(WindowsServices.PhpFcgi);

            foreach (var s in new[] { WindowsServices.Nginx, WindowsServices.PhpFcgi })
            {
                WindowsServices.SetStartType(s, cfg.AutoStartServices ? "auto" : "demand");
                WindowsServices.SetAutoRecovery(s);
            }
            return re;
        }

        /// <summary>
        /// php-cgi havuzunu çalıştıran yardımcı exe'yi bin\fcgi altına kopyalar.
        /// Ana program güncellenirken servis tarafından kilitlenmesin diye ayrı bir kopya kullanılır.
        /// </summary>
        public static string EnsureFcgiHelper(Action<string> log)
        {
            var src = File.Exists(Paths.AppExe) ? Paths.AppExe : Paths.ExePath;
            var dst = Paths.FcgiHelperExe;
            if (!Paths.IsRealExe && !File.Exists(Paths.AppExe)) throw new Exception("DevNanotek.exe yolu belirlenemedi (FastCGI yardımcısı kopyalanamaz).");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                bool same = File.Exists(dst) && new FileInfo(dst).Length == new FileInfo(src).Length
                            && File.GetLastWriteTimeUtc(dst) >= File.GetLastWriteTimeUtc(src);
                if (same) return dst;
                try { File.Copy(src, dst, true); }
                catch (IOException)
                {
                    log?.Invoke("PHP FastCGI yardımcısı güncelleniyor (servis kısa süre durdurulur)...");
                    WindowsServices.Stop(WindowsServices.PhpFcgi, 30);
                    WindowsServices.KillProcessesByPath(dst);
                    File.Copy(src, dst, true);
                }
            }
            catch (Exception ex) { Logger.Warn("FastCGI yardımcısı kopyalanamadı: " + ex.Message); if (!File.Exists(dst)) return src; }
            return dst;
        }

        public static void UninstallServices()
        {
            WinSwManager.Uninstall(WindowsServices.Nginx);
            WinSwManager.Uninstall(WindowsServices.PhpFcgi);
            // artakalan nginx süreçleri
            foreach (var v in Installed()) WindowsServices.KillProcessesByPath(Exe(v));
        }
    }
}
