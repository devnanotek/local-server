using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DevNanotek.Core
{
    public class PhpExtension
    {
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public bool IsZend { get; set; }
        /// <summary>Hızlı erişim listesinde gösterilen kısa açıklama.</summary>
        public string Description { get; set; } = "";
        /// <summary>php.ini'deki ilk durum (değişti mi karşılaştırması için).</summary>
        public bool Original { get; set; }
        public override string ToString() => Name;
    }

    /// <summary>PHP sürümleri, php.ini düzenleme, eklentiler.</summary>
    public static class PhpManager
    {
        public static string Dir(string version) => Path.Combine(Paths.BinPhp, "php-" + version);
        public static string Exe(string version) => Path.Combine(Dir(version), "php.exe");
        public static string CgiExe(string version) => Path.Combine(Dir(version), "php-cgi.exe");
        public static string IniPath(string version) => Path.Combine(Dir(version), "php.ini");
        public static string ExtDir(string version) => Path.Combine(Dir(version), "ext");
        public static bool IsInstalled(string version) => !string.IsNullOrEmpty(version) && File.Exists(Exe(version));

        public static List<string> InstalledVersions()
        {
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(Paths.BinPhp)) return list;
                foreach (var d in Directory.GetDirectories(Paths.BinPhp, "php-*"))
                {
                    var v = Path.GetFileName(d).Substring(4);
                    if (File.Exists(Path.Combine(d, "php.exe"))) list.Add(v);
                }
            }
            catch { }
            return list.OrderByDescending(Catalog.VersionKey).ToList();
        }

        public static int Major(string version)
        {
            var m = Regex.Match(version ?? "", @"^(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : 8;
        }

        public static string ApacheModuleDll(string version)
        {
            try { return Directory.GetFiles(Dir(version), "php*apache2_4.dll").Select(Path.GetFileName).FirstOrDefault(); }
            catch { return null; }
        }

        public static string ApacheModuleId(string version) => Major(version) >= 8 ? "php_module" : "php7_module";

        /// <summary>Apache'nin LoadFile ile önceden yüklemesi gereken bağımlı DLL'ler.</summary>
        public static List<string> DependencyDlls(string version)
        {
            var list = new List<string>();
            var dir = Dir(version);
            if (!Directory.Exists(dir)) return list;
            // libssl/libcrypto bilinçli olarak yok: Apache'nin mod_ssl'i aynı adlı OpenSSL 3 DLL'lerini zaten yükler,
            // ikinci kopyayı yüklemek çakışmaya yol açar. Diğerleri PATH'te de olduğu için güvenlidir.
            var patterns = new[] { "php*ts.dll", "libssh2.dll", "nghttp2.dll", "libsqlite3.dll", "libpq.dll", "libsasl.dll", "libsodium.dll", "icu*.dll" };
            foreach (var p in patterns)
                foreach (var f in Directory.GetFiles(dir, p))
                {
                    var name = Path.GetFileName(f);
                    if (name.IndexOf("apache", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (name.IndexOf("phpdbg", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (!list.Contains(name)) list.Add(name);
                }
            return list;
        }

        // ------------------------------------------------------------------
        //  php.ini
        // ------------------------------------------------------------------
        public static string ReadIni(string version) => File.Exists(IniPath(version)) ? File.ReadAllText(IniPath(version), Encoding.UTF8) : "";

        public static void WriteIni(string version, string content)
        {
            var path = IniPath(version);
            try { if (File.Exists(path)) File.Copy(path, path + ".bak", true); } catch { }
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        /// <summary>php.ini yoksa php.ini-development'tan oluşturur ve ilk-kurulum varsayılanlarını uygular.</summary>
        public static void EnsureIni(string version, AppConfig cfg)
        {
            var ini = IniPath(version);
            if (File.Exists(ini)) return;
            var src = Path.Combine(Dir(version), "php.ini-development");
            if (!File.Exists(src)) src = Path.Combine(Dir(version), "php.ini-production");
            var content = File.Exists(src) ? File.ReadAllText(src, Encoding.UTF8) : "[PHP]\r\n";
            content = ApplyFirstTimeDefaults(content, version);
            File.WriteAllText(ini, content, new UTF8Encoding(false));
            Logger.Info("php.ini oluşturuldu: " + ini);
        }

        private static string ApplyFirstTimeDefaults(string ini, string version)
        {
            var d = new Dictionary<string, string>
            {
                ["memory_limit"] = "512M",
                ["upload_max_filesize"] = "512M",
                ["post_max_size"] = "512M",
                ["max_file_uploads"] = "100",
                ["max_execution_time"] = "300",
                ["max_input_time"] = "300",
                ["max_input_vars"] = "10000",
                ["display_errors"] = "On",
                ["display_startup_errors"] = "On",
                ["log_errors"] = "On",
                ["error_reporting"] = "E_ALL & ~E_DEPRECATED",
                ["date.timezone"] = "Europe/Istanbul",
                ["output_buffering"] = "4096",
                ["realpath_cache_size"] = "4096k",
                ["realpath_cache_ttl"] = "600",
                ["opcache.enable"] = "1",
                ["opcache.enable_cli"] = "0",
                ["opcache.memory_consumption"] = "192",
                ["opcache.interned_strings_buffer"] = "16",
                ["opcache.max_accelerated_files"] = "20000",
                ["opcache.validate_timestamps"] = "1",
                ["opcache.revalidate_freq"] = "0",
                ["mysqli.allow_local_infile"] = "On",
                ["default_charset"] = "UTF-8",
            };
            foreach (var kv in d) ini = IniSetText(ini, kv.Key, kv.Value);

            // Varsayılan eklentiler (ext klasöründe varsa)
            var wanted = new[] { "bz2", "curl", "fileinfo", "gd", "gd2", "gettext", "gmp", "intl", "mbstring", "exif", "mysqli", "openssl", "pdo_mysql", "pdo_sqlite", "pdo_pgsql", "pgsql", "soap", "sockets", "sodium", "sqlite3", "tidy", "xsl", "zip", "ftp" };
            var available = AvailableExtensions(version);
            foreach (var e in wanted)
                if (available.Contains(e, StringComparer.OrdinalIgnoreCase)) ini = SetExtensionText(ini, e, true, false);
            if (HasOpcacheDll(version)) ini = SetExtensionText(ini, "opcache", true, true);
            return ini;
        }

        /// <summary>Her "Uygula"da yazılan altyapı anahtarları (yollar, SMTP, sertifika...).</summary>
        public static void ApplyInfrastructure(string version, AppConfig cfg)
        {
            if (!IsInstalled(version)) return;
            EnsureIni(version, cfg);
            var ini = ReadIni(version);
            var orig = ini;
            var dir = Paths.Fwd(Dir(version));
            var d = new Dictionary<string, string>
            {
                ["extension_dir"] = $"\"{dir}/ext\"",
                ["error_log"] = $"\"{Paths.Fwd(Paths.LogsPhp)}/php_error.log\"",
                ["session.save_path"] = $"\"{Paths.Fwd(Path.Combine(Paths.Tmp, "php-sessions"))}\"",
                ["upload_tmp_dir"] = $"\"{Paths.Fwd(Path.Combine(Paths.Tmp, "php-upload"))}\"",
                ["sys_temp_dir"] = $"\"{Paths.Fwd(Paths.Tmp)}\"",
                ["SMTP"] = "127.0.0.1",
                ["smtp_port"] = cfg.SmtpPort.ToString(),
                ["sendmail_from"] = "dev@localhost",
                ["cgi.force_redirect"] = "0",
            };
            // ca-bundle.pem = Mozilla kökleri + yerel mkcert kökü (PHP'den https://proje.test çağrıları da doğrulanır)
            var ca = File.Exists(Paths.CaBundle) ? Paths.CaBundle : File.Exists(Paths.CaCertPem) ? Paths.CaCertPem : null;
            if (ca != null)
            {
                d["curl.cainfo"] = $"\"{Paths.Fwd(ca)}\"";
                d["openssl.cafile"] = $"\"{Paths.Fwd(ca)}\"";
            }
            foreach (var kv in d) ini = IniSetText(ini, kv.Key, kv.Value);
            // PHP 8.5+ OPcache yerleşiktir (DLL yok) — zend_extension satırı orada hata verir
            if (!HasOpcacheDll(version) && IsExtensionEnabledText(ini, "opcache")) ini = SetExtensionText(ini, "opcache", false, true);
            if (ini != orig) WriteIni(version, ini);
        }

        public static bool HasOpcacheDll(string version) => File.Exists(Path.Combine(ExtDir(version), "php_opcache.dll"));

        /// <summary>
        /// Kendini onarma: php -v çalıştırır; "Unable to load dynamic library 'x'" veya
        /// "Failed loading Zend extension 'x'" uyarısı veren eklentileri php.ini'de kapatır.
        /// Böylece bozuk/engellenmiş bir DLL yüzünden her sayfanın başına uyarı basılmaz.
        /// Dönüş: kapatılan eklentiler.
        /// </summary>
        public static List<string> SelfHeal(string version, Action<string> log = null)
        {
            var disabled = new List<string>();
            if (!IsInstalled(version)) return disabled;
            for (int pass = 0; pass < 3; pass++)
            {
                var r = ProcessRunner.Run(Exe(version), "-v", Dir(version), 20000);
                var output = r.AllOutput;
                var names = new List<string>();
                foreach (Match m in Regex.Matches(output, @"Unable to load dynamic library '([^']+)'")) names.Add(m.Groups[1].Value);
                foreach (Match m in Regex.Matches(output, @"Failed loading Zend extension '([^']+)'")) names.Add(m.Groups[1].Value);
                names = names.Select(n => Regex.Replace(Path.GetFileNameWithoutExtension(n.Replace('/', '\\')), @"^php_", "")).Distinct().ToList();
                if (names.Count == 0) break;
                var ini = ReadIni(version);
                foreach (var n in names)
                {
                    ini = SetExtensionText(ini, n, false, n.Equals("opcache", StringComparison.OrdinalIgnoreCase));
                    if (!disabled.Contains(n)) disabled.Add(n);
                }
                WriteIni(version, ini);
            }
            if (disabled.Count > 0)
            {
                var msg = $"PHP {version}: yüklenemeyen eklentiler php.ini'de kapatıldı: {string.Join(", ", disabled)}";
                Logger.Warn(msg + SystemCheck.BlockedHint().Replace("\n", " "));
                log?.Invoke(msg);
            }
            return disabled;
        }

        public static string IniGet(string version, string key) => IniGetText(ReadIni(version), key);

        public static void IniSet(string version, string key, string value)
        {
            var ini = ReadIni(version);
            var n = IniSetText(ini, key, value);
            if (n != ini) WriteIni(version, n);
        }

        private static Regex KeyRegex(string key) =>
            new Regex(@"^(?<lead>[ \t]*)(?<c>;?)[ \t]*" + Regex.Escape(key) + @"[ \t]*=(?<val>[^\r\n]*)", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        public static string IniGetText(string ini, string key)
        {
            var rx = KeyRegex(key);
            foreach (Match m in rx.Matches(ini))
            {
                if (m.Groups["c"].Value == ";") continue;
                var v = m.Groups["val"].Value.Trim();
                // satır sonu yorumunu at (tırnak içinde değilse)
                if (!v.StartsWith("\"")) { var i = v.IndexOf(';'); if (i >= 0) v = v.Substring(0, i).Trim(); }
                return v.Trim('"');
            }
            return null;
        }

        /// <summary>
        /// Hedef satır seçimi: önce etkin satır, sonra ";anahtar =" biçimindeki gerçek yorumlu satır
        /// (";   anahtar = ..." gibi açıklama içindeki örnekler en son tercih edilir).
        /// </summary>
        private static Match PickTarget(List<Match> matches)
        {
            return matches.FirstOrDefault(m => m.Groups["c"].Value != ";")
                ?? matches.LastOrDefault(m => Regex.IsMatch(m.Value, @"^[ \t]*;[^ \t;]"))
                ?? matches.LastOrDefault();
        }

        public static string IniSetText(string ini, string key, string value)
        {
            var rx = KeyRegex(key);
            var matches = rx.Matches(ini).Cast<Match>().ToList();
            var target = PickTarget(matches);
            var line = key + " = " + value;
            if (target != null)
            {
                if (target.Value.Trim() == line) return ini;
                return ini.Substring(0, target.Index) + line + ini.Substring(target.Index + target.Length);
            }
            var nl = ini.Contains("\r\n") ? "\r\n" : "\n";
            if (!ini.EndsWith(nl)) ini += nl;
            if (!ini.Contains("; --- DevNanotek ---")) ini += nl + "; --- DevNanotek ---" + nl;
            return ini + line + nl;
        }

        // ------------------------------------------------------------------
        //  Eklentiler
        // ------------------------------------------------------------------
        public static List<string> AvailableExtensions(string version)
        {
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(ExtDir(version))) return list;
                foreach (var f in Directory.GetFiles(ExtDir(version), "php_*.dll"))
                {
                    var n = Path.GetFileNameWithoutExtension(f).Substring(4);
                    list.Add(n);
                }
            }
            catch { }
            return list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<PhpExtension> Extensions(string version)
        {
            var ini = ReadIni(version);
            return AvailableExtensions(version)
                .Select(n => new PhpExtension { Name = n, IsZend = n.Equals("opcache", StringComparison.OrdinalIgnoreCase), Enabled = IsExtensionEnabledText(ini, n) })
                .ToList();
        }

        private static Regex ExtRegex(string name) =>
            new Regex(@"^(?<lead>[ \t]*)(?<c>;?)[ \t]*(?<kind>zend_extension|extension)[ \t]*=[ \t]*""?(?:php_)?" + Regex.Escape(name) + @"(?:\.dll)?""?[ \t]*(?:;[^\r\n]*)?(?=\r?$)", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        public static bool IsExtensionEnabledText(string ini, string name)
            => ExtRegex(name).Matches(ini).Cast<Match>().Any(m => m.Groups["c"].Value != ";");

        public static void SetExtension(string version, string name, bool enabled)
        {
            var ini = ReadIni(version);
            var n = SetExtensionText(ini, name, enabled, name.Equals("opcache", StringComparison.OrdinalIgnoreCase));
            if (n != ini) WriteIni(version, n);
        }

        public static string SetExtensionText(string ini, string name, bool enabled, bool zend)
        {
            var rx = ExtRegex(name);
            var matches = rx.Matches(ini).Cast<Match>().ToList();
            var kind = zend ? "zend_extension" : "extension";
            var active = matches.Where(m => m.Groups["c"].Value != ";").ToList();
            var sb = new StringBuilder(ini);

            if (!enabled)
            {
                // etkin olan her satırı yorumla (sondan başa, indeksler kaymasın)
                foreach (var m in active.OrderByDescending(m => m.Index))
                    sb.Remove(m.Index, m.Length).Insert(m.Index, ";" + m.Value.TrimStart());
                return sb.ToString();
            }

            if (active.Count > 0)
            {
                // zaten etkin: fazladan kopyaları yorumla
                foreach (var m in active.Skip(1).OrderByDescending(m => m.Index))
                    sb.Remove(m.Index, m.Length).Insert(m.Index, ";" + m.Value.TrimStart());
                return sb.ToString();
            }

            var line = kind + "=" + name;
            var target = PickTarget(matches);
            if (target != null)
            {
                sb.Remove(target.Index, target.Length).Insert(target.Index, line);
                return sb.ToString();
            }
            var nl = ini.Contains("\r\n") ? "\r\n" : "\n";
            if (!ini.EndsWith(nl)) ini += nl;
            if (!ini.Contains("; --- DevNanotek ---")) ini += nl + "; --- DevNanotek ---" + nl;
            return ini + line + nl;
        }

        /// <summary>Arayüzdeki "hızlı ayarlar" listesi.</summary>
        /// <summary>Projelerde en sık gereken eklentiler (hızlı erişim). Sıra ekranda görünen sıradır.</summary>
        public static readonly Dictionary<string, string> PopularExtensions = new Dictionary<string, string>
        {
            ["gd"] = "Resim işleme: küçük resim, kırpma, captcha (WordPress, Laravel Intervention)",
            ["zip"] = "Zip dosyası açma/oluşturma (Composer, WordPress eklenti yükleme, Excel kütüphaneleri)",
            ["intl"] = "Çok dilli tarih, para ve sıralama (Laravel, Symfony, WooCommerce)",
            ["mbstring"] = "UTF-8 / Türkçe karakterli metin işlemleri",
            ["curl"] = "Dış adreslere HTTP istekleri (API, ödeme, kargo entegrasyonları)",
            ["openssl"] = "Şifreleme ve HTTPS bağlantıları",
            ["fileinfo"] = "Yüklenen dosyanın gerçek türünü algılama",
            ["exif"] = "Fotoğraf bilgileri (yön, çekim tarihi)",
            ["soap"] = "SOAP web servisleri (e-fatura, banka, kargo)",
            ["sodium"] = "Modern şifreleme (Argon2, libsodium)",
            ["mysqli"] = "MySQL / MariaDB (klasik bağlantı)",
            ["pdo_mysql"] = "MySQL / MariaDB (PDO — Laravel, WordPress)",
            ["pdo_pgsql"] = "PostgreSQL (PDO)",
            ["pgsql"] = "PostgreSQL (klasik bağlantı)",
            ["pdo_sqlite"] = "SQLite (PDO) — dosya tabanlı, sunucu gerekmez",
            ["sqlite3"] = "SQLite (klasik)",
            ["pdo_sqlsrv"] = "Microsoft SQL Server (PDO)",
            ["sqlsrv"] = "Microsoft SQL Server (klasik)",
            ["gmp"] = "Büyük sayılarla matematik (kripto, bazı ödeme kütüphaneleri)",
            ["xsl"] = "XML dönüşümleri (XSLT)",
            ["ftp"] = "FTP bağlantıları",
            ["sockets"] = "Soket programlama",
            ["imap"] = "E-posta kutusu okuma (IMAP) — PHP 8.3 ve öncesi",
            ["ldap"] = "LDAP / Active Directory girişi",
            ["bz2"] = "bzip2 sıkıştırma",
            ["gettext"] = "Çeviri dosyaları (.mo)",
            ["tidy"] = "HTML temizleme / düzeltme",
            ["opcache"] = "PHP hızlandırıcı (açık kalması önerilir)",
        };

        /// <summary>Bu PHP sürümünde bulunan sık kullanılan eklentiler, açıklamalarıyla.</summary>
        public static List<PhpExtension> PopularFor(string version)
        {
            var all = Extensions(version);
            var list = new List<PhpExtension>();
            foreach (var kv in PopularExtensions)
            {
                var x = all.FirstOrDefault(e => e.Name.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                if (x == null) continue;
                x.Description = kv.Value;
                x.Original = x.Enabled;
                list.Add(x);
            }
            return list;
        }

        public static readonly string[] QuickKeys =
        {
            "memory_limit", "upload_max_filesize", "post_max_size", "max_execution_time", "max_input_time", "max_input_vars",
            "display_errors", "error_reporting", "date.timezone", "short_open_tag", "opcache.enable", "max_file_uploads"
        };

        public static string RunPhp(string version, string args)
        {
            if (!IsInstalled(version)) return "";
            return ProcessRunner.Run(Exe(version), args, Dir(version), 20000).AllOutput;
        }
    }
}
