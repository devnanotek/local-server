using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DevNanotek.Core
{
    /// <summary>Bileşen kimlikleri.</summary>
    public static class Comp
    {
        public const string Php = "php";
        public const string Apache = "apache";
        public const string Nginx = "nginx";
        public const string MariaDb = "mariadb";
        public const string MySql = "mysql";
        public const string PostgreSql = "postgresql";
        public const string Node = "node";
        public const string PhpMyAdmin = "phpmyadmin";
        public const string Adminer = "adminer";
        public const string SqlSrv = "sqlsrv";
        public const string Mailpit = "mailpit";
        public const string Mkcert = "mkcert";
        public const string WinSw = "winsw";
        public const string Composer = "composer";
        public const string CaCert = "cacert";
        public const string VcRedist = "vcredist";

        public static string Title(string c)
        {
            switch (c)
            {
                case Php: return "PHP";
                case Apache: return "Apache";
                case Nginx: return "Nginx";
                case MariaDb: return "MariaDB";
                case MySql: return "MySQL";
                case PostgreSql: return "PostgreSQL";
                case Node: return "Node.js";
                case PhpMyAdmin: return "phpMyAdmin";
                case Adminer: return "Adminer";
                case SqlSrv: return "SQL Server sürücüsü";
                case Mailpit: return "Mailpit";
                case Mkcert: return "mkcert";
                case WinSw: return "WinSW";
                case Composer: return "Composer";
                case CaCert: return "CA sertifika paketi";
                case VcRedist: return "Visual C++ Runtime";
                default: return c;
            }
        }

        public static string Description(string c)
        {
            switch (c)
            {
                case Php: return "PHP çalıştırıcısı. Birden çok sürüm kurup tek tıkla geçebilirsiniz.";
                case Apache: return "Web sunucu. .htaccess ve mod_rewrite destekler (cPanel/Plesk ile aynı).";
                case Nginx: return "Hafif web sunucu. PHP, php-cgi havuzuyla çalışır; .htaccess okunmaz.";
                case MariaDb: return "MySQL uyumlu veritabanı. Plesk/cPanel'de en yaygın olanı.";
                case MySql: return "Oracle MySQL veritabanı.";
                case PostgreSql: return "Gelişmiş açık kaynak SQL veritabanı (cPanel ve Plesk'te de var). MariaDB/MySQL ile aynı anda çalışır, port 5432. Kullanıcı postgres, şifre gerekmez (yalnız bu bilgisayardan).";
                case Node: return "node, npm, npx — socket.io, React, Vue derlemeleri için.";
                case PhpMyAdmin: return "Tarayıcıdan veritabanı yönetimi: http://localhost/phpmyadmin";
                case Adminer: return "Tek sayfalık veritabanı yöneticisi: MySQL/MariaDB, PostgreSQL, SQLite ve SQL Server. http://localhost/adminer";
                case SqlSrv: return "PHP'nin Microsoft SQL Server'a bağlanması için sqlsrv ve pdo_sqlsrv eklentileri. Microsoft ODBC Driver 18 de kurulur. PHP 8.1 ve üstü.";
                case Mailpit: return "Test e-posta kutusu. PHP'nin gönderdiği e-postalar gerçek alıcıya gitmez, burada görünür.";
                case Mkcert: return "Tarayıcının güvendiği yerel HTTPS sertifikası üretir.";
                case WinSw: return "Nginx, php-cgi ve Mailpit'i Windows servisi olarak çalıştırır.";
                case Composer: return "PHP paket yöneticisi.";
                case CaCert: return "PHP curl/openssl için güncel kök sertifikalar.";
                case VcRedist: return "PHP, Apache ve MariaDB'nin çalışması için gereken Microsoft kitaplıkları.";
                default: return "";
            }
        }

        /// <summary>Mimariden bağımsız bileşenler (tek indirme her yerde çalışır).</summary>
        public static bool ArchNeutral(string c) => c == PhpMyAdmin || c == Composer || c == CaCert || c == WinSw || c == Nginx || c == Adminer || c == SqlSrv;

        /// <summary>Dalı ana sürümle belirlenen bileşenler (Node 24.x, PostgreSQL 18.x).</summary>
        public static bool MajorBranch(string c) => c == Node || c == PostgreSql;
    }

    public class CatalogEntry
    {
        public string Component { get; set; }
        public string Version { get; set; }
        public string Branch { get; set; }
        public List<string> Urls { get; set; } = new List<string>();      // x64 (veya mimariden bağımsız)
        public List<string> UrlsX86 { get; set; } = new List<string>();   // 32 bit
        public string Note { get; set; } = "";
        public bool Recommended { get; set; }
        public bool Lts { get; set; }
        public long SizeBytes { get; set; }

        [ScriptIgnore] public string Title => Comp.Title(Component) + (Version == "latest" ? "" : " " + Version);
        [ScriptIgnore] public string Id => Component + ":" + Version;

        /// <summary>Bu bilgisayarın mimarisine uygun indirme bağlantıları.</summary>
        [ScriptIgnore]
        public List<string> EffectiveUrls => SystemInfo.IsX86 && !Comp.ArchNeutral(Component) ? (UrlsX86 ?? new List<string>()) : Urls;

        /// <summary>Bu bilgisayarda kurulabilir mi (32 bit Windows'ta bazı bileşenler yayınlanmıyor).</summary>
        [ScriptIgnore] public bool IsAvailable => EffectiveUrls != null && EffectiveUrls.Count > 0;

        public override string ToString() => Title;
    }

    /// <summary>
    /// İndirilebilir sürümler. Yerleşik liste + internetten otomatik güncelleme (UpdateChecker, 12 saatte bir).
    /// etc\catalog.json içine kaydedilir.
    /// </summary>
    public class Catalog
    {
        public List<CatalogEntry> Entries { get; set; } = new List<CatalogEntry>();
        public DateTime? UpdatedAt { get; set; }

        public static string FilePath => Path.Combine(Paths.Etc, "catalog.json");

        private static Catalog _current;
        public static Catalog Current
        {
            get
            {
                if (_current == null) _current = Load();
                return _current;
            }
        }

        // ------------------------------------------------------------------
        //  URL üreticileri
        // ------------------------------------------------------------------
        public static string PhpCompiler(string version)
        {
            var m = Regex.Match(version ?? "", @"^(\d+)\.(\d+)");
            if (!m.Success) return "vs17";
            int major = int.Parse(m.Groups[1].Value), minor = int.Parse(m.Groups[2].Value);
            if (major < 8) return "vc15";
            if (major == 8 && minor <= 3) return "vs16";
            return "vs17";
        }

        public static List<string> PhpUrls(string version, string arch = "x64")
        {
            var file = $"php-{version}-Win32-{PhpCompiler(version)}-{arch}.zip";
            return new List<string>
            {
                "https://windows.php.net/downloads/releases/" + file,
                "https://windows.php.net/downloads/releases/archives/" + file
            };
        }

        public static List<string> MariaDbUrls(string version) => new List<string>
        {
            $"https://downloads.mariadb.org/rest-api/mariadb/{version}/mariadb-{version}-winx64.zip",
            $"https://archive.mariadb.org/mariadb-{version}/winx64-packages/mariadb-{version}-winx64.zip"
        };

        public static List<string> MySqlUrls(string version)
        {
            var branch = Regex.Match(version, @"^\d+\.\d+").Value;
            return new List<string>
            {
                $"https://cdn.mysql.com/Downloads/MySQL-{branch}/mysql-{version}-winx64.zip",
                $"https://cdn.mysql.com/archives/mysql-{branch}/mysql-{version}-winx64.zip"
            };
        }

        public static List<string> NodeUrls(string version, string arch = "x64") => new List<string> { $"https://nodejs.org/dist/v{version}/node-v{version}-win-{arch}.zip" };
        public static List<string> NginxUrls(string version) => new List<string> { $"https://nginx.org/download/nginx-{version}.zip" };

        /// <summary>EnterpriseDB "binaries" zip'i (yalnız 64 bit). Paket numarası (-1, -2…) yeniden paketlemede artar.</summary>
        public static List<string> PostgreSqlUrls(string version)
            => Enumerable.Range(1, 5).Select(n => $"https://get.enterprisedb.com/postgresql/postgresql-{version}-{n}-windows-x64-binaries.zip").ToList();

        public static List<string> AdminerUrls(string version) => new List<string> { $"https://github.com/vrana/adminer/releases/download/v{version}/adminer-{version}.php" };
        public static List<string> SqlSrvUrls(string version) => new List<string> { $"https://github.com/microsoft/msphpsql/releases/download/v{version}/Windows_{version}RTW.zip" };

        private static bool NodeHasX86(string version)
        {
            int major; return int.TryParse((version ?? "").Split('.')[0], out major) && major <= 22;
        }

        // ------------------------------------------------------------------
        //  Yerleşik katalog (2026-10 itibarıyla doğrulanmış bağlantılar)
        // ------------------------------------------------------------------
        public static Catalog BuiltIn()
        {
            var c = new Catalog();
            CatalogEntry Add(string comp, string ver, List<string> urls, List<string> x86 = null, string branch = null, bool rec = false, string note = "", bool lts = false)
            {
                var e = new CatalogEntry { Component = comp, Version = ver, Urls = urls, UrlsX86 = x86 ?? new List<string>(), Branch = branch ?? MajorMinor(ver), Recommended = rec, Note = note, Lts = lts };
                c.Entries.Add(e);
                return e;
            }
            void Php(string v, bool rec = false, string note = "") => Add(Comp.Php, v, PhpUrls(v), PhpUrls(v, "x86"), null, rec, note);

            Php("8.5.11", note: "En yeni");
            Php("8.4.26", rec: true, note: "Önerilen");
            Php("8.3.35");
            Php("8.2.34");
            Php("8.1.34", note: "Güvenlik desteği bitiyor");
            Php("8.0.30", note: "Desteği bitti");
            Php("7.4.33", note: "Eski projeler için");

            Add(Comp.Apache, "2.4.69", new List<string> { "https://www.apachelounge.com/download/VS18/binaries/httpd-2.4.69-261002-Win64-VS18.zip" },
                new List<string> { "https://www.apachelounge.com/download/VS18/binaries/httpd-2.4.69-261002-win32-vs18.zip" }, "2.4", true, "Apache Lounge");

            Add(Comp.Nginx, "1.30.5", NginxUrls("1.30.5"), null, "1.30", true, "Kararlı");
            Add(Comp.Nginx, "1.31.6", NginxUrls("1.31.6"), null, "1.31", false, "Mainline");

            Add(Comp.MariaDb, "12.3.3", MariaDbUrls("12.3.3"), null, "12.3", true, "LTS", true);
            Add(Comp.MariaDb, "11.8.9", MariaDbUrls("11.8.9"), null, "11.8", false, "LTS", true);
            Add(Comp.MariaDb, "11.4.13", MariaDbUrls("11.4.13"), null, "11.4", false, "LTS", true);
            Add(Comp.MariaDb, "10.11.19", MariaDbUrls("10.11.19"), null, "10.11", false, "LTS · Plesk/cPanel'de yaygın", true);
            Add(Comp.MariaDb, "10.6.28", MariaDbUrls("10.6.28"), null, "10.6", false, "LTS (eski)", true);
            Add(Comp.MariaDb, "13.0.2", MariaDbUrls("13.0.2"), null, "13.0", false, "Rolling");

            Add(Comp.MySql, "8.4.11", MySqlUrls("8.4.11"), null, "8.4", true, "LTS", true);
            Add(Comp.MySql, "8.0.46", MySqlUrls("8.0.46"), null, "8.0", false, "Eski");

            // PostgreSQL (postgresql.org/versions.json, 2026-10): desteklenen dallar 14–18
            void Pg(string v, bool rec, string note) => Add(Comp.PostgreSql, v, PostgreSqlUrls(v), null, v.Split('.')[0], rec, note);
            Pg("18.6", false, "En yeni");
            Pg("17.11", true, "Önerilen");
            Pg("16.15", false, "Ubuntu 24.04'te varsayılan");
            Pg("15.19", false, "Debian 12'de varsayılan");
            Pg("14.24", false, "Desteği Kasım 2026'da bitiyor");

            void Node(string v, bool rec, string note) => Add(Comp.Node, v, NodeUrls(v), NodeHasX86(v) ? NodeUrls(v, "x86") : null, v.Split('.')[0], rec, note, note.StartsWith("LTS"));
            Node("24.21.0", true, "LTS Krypton");
            Node("22.23.3", false, "LTS Jod");
            Node("20.20.2", false, "LTS Iron");

            Add(Comp.PhpMyAdmin, "5.2.3", new List<string> { "https://files.phpmyadmin.net/phpMyAdmin/5.2.3/phpMyAdmin-5.2.3-all-languages.zip" }, null, "5.2", true);
            Add(Comp.Adminer, "6.1.1", AdminerUrls("6.1.1"), null, "6", true);
            Add(Comp.SqlSrv, "5.13.3", SqlSrvUrls("5.13.3"), null, "5", true, "PHP 8.1 – 8.5");
            Add(Comp.Mailpit, "1.31.4", new List<string> { "https://github.com/axllent/mailpit/releases/download/v1.31.4/mailpit-windows-amd64.zip" }, null, "1", true);
            Add(Comp.Mkcert, "1.4.4", new List<string> { "https://github.com/FiloSottile/mkcert/releases/download/v1.4.4/mkcert-v1.4.4-windows-amd64.exe" }, null, "1", true);
            Add(Comp.WinSw, "2.12.0", new List<string> { "https://github.com/winsw/winsw/releases/download/v2.12.0/WinSW.NET461.exe" }, null, "2", true);
            Add(Comp.Composer, "latest", new List<string> { "https://getcomposer.org/download/latest-stable/composer.phar" }, null, "2", true);
            Add(Comp.CaCert, "latest", new List<string> { "https://curl.se/ca/cacert.pem" }, null, "1", true);
            // aka.ms/vc14 her zaman en güncel v14'ü verir (Apache VS18 için 14.50+ gerekir)
            Add(Comp.VcRedist, "14", new List<string> { "https://aka.ms/vc14/vc_redist.x64.exe", "https://aka.ms/vs/17/release/vc_redist.x64.exe" },
                new List<string> { "https://aka.ms/vc14/vc_redist.x86.exe", "https://aka.ms/vs/17/release/vc_redist.x86.exe" }, "14", true, "Güncel v14");
            return c;
        }

        public static string MajorMinor(string v)
        {
            var m = Regex.Match(v ?? "", @"^(\d+)\.(\d+)");
            return m.Success ? m.Value : (v ?? "");
        }

        /// <summary>Aynı "dal" mı (yama güncellemesi): Node için ana sürüm, diğerleri için ana.alt.</summary>
        public static bool SameBranch(string comp, string a, string b)
        {
            if (Comp.MajorBranch(comp)) return (a ?? "").Split('.')[0] == (b ?? "").Split('.')[0];
            if (comp == Comp.PhpMyAdmin || comp == Comp.Mailpit || comp == Comp.Mkcert || comp == Comp.Adminer || comp == Comp.SqlSrv) return true;
            return MajorMinor(a) == MajorMinor(b);
        }

        // ------------------------------------------------------------------
        public static Catalog Load()
        {
            var builtIn = BuiltIn();
            try
            {
                if (File.Exists(FilePath))
                {
                    var saved = JsonUtil.Deserialize<Catalog>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (saved?.Entries != null && saved.Entries.Count > 0)
                    {
                        // araç girişleri (bağlantıları sabit) her zaman programdaki güncel listeden gelir
                        var fixedComps = new[] { Comp.VcRedist, Comp.WinSw, Comp.CaCert, Comp.Composer };
                        saved.Entries.RemoveAll(x => fixedComps.Contains(x.Component));
                        foreach (var e in saved.Entries) if (e.UrlsX86 == null) e.UrlsX86 = new List<string>();
                        foreach (var e in builtIn.Entries)
                            if (!saved.Entries.Any(x => x.Component == e.Component && x.Version == e.Version))
                                saved.Entries.Add(e);
                        saved.Normalize();
                        return saved;
                    }
                }
            }
            catch (Exception ex) { Logger.Warn("catalog.json okunamadı: " + ex.Message); }
            builtIn.Normalize();
            return builtIn;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtil.SerializePretty(this), new UTF8Encoding(false));
            }
            catch (Exception ex) { Logger.Warn("catalog.json yazılamadı: " + ex.Message); }
        }

        /// <summary>Mimariye uygun girdiler (yeniden eskiye).</summary>
        public IEnumerable<CatalogEntry> For(string component)
            => Entries.Where(e => e.Component == component).OrderByDescending(e => VersionKey(e.Version));

        public IEnumerable<CatalogEntry> AvailableFor(string component) => For(component).Where(e => e.IsAvailable);

        public CatalogEntry Find(string component, string version)
            => Entries.FirstOrDefault(e => e.Component == component && e.Version == version);

        public CatalogEntry Recommended(string component)
            => AvailableFor(component).FirstOrDefault(e => e.Recommended) ?? AvailableFor(component).FirstOrDefault();

        public static CatalogEntry Custom(string component, string version)
        {
            version = (version ?? "").Trim().TrimStart('v', 'V');
            var e = new CatalogEntry { Component = component, Version = version, Branch = MajorMinor(version), Note = "Elle eklendi" };
            switch (component)
            {
                case Comp.Php: e.Urls = PhpUrls(version); e.UrlsX86 = PhpUrls(version, "x86"); break;
                case Comp.MariaDb: e.Urls = MariaDbUrls(version); break;
                case Comp.MySql: e.Urls = MySqlUrls(version); break;
                case Comp.Node: e.Urls = NodeUrls(version); if (NodeHasX86(version)) e.UrlsX86 = NodeUrls(version, "x86"); e.Branch = version.Split('.')[0]; break;
                case Comp.Nginx: e.Urls = NginxUrls(version); break;
                case Comp.PostgreSql: e.Urls = PostgreSqlUrls(version); e.Branch = version.Split('.')[0]; break;
                default: return null;
            }
            return e;
        }

        public static long VersionKey(string v)
        {
            long key = 0; int shift = 3;
            foreach (var part in (v ?? "").Split('.', '-'))
            {
                int n; if (!int.TryParse(part, out n)) n = 0;
                key += (long)n * (long)Math.Pow(10000, shift);
                if (--shift < 0) break;
            }
            return key;
        }

        // ------------------------------------------------------------------
        //  Düzenleme: eski yamaları ayıkla, önerilenleri yeniden hesapla
        // ------------------------------------------------------------------
        public void Normalize()
        {
            // 1) Her bileşende aynı dalın yalnızca en yeni yamasını tut (kurulu eski sürümler Sürümler sayfasında ayrıca listelenir)
            var keep = new List<CatalogEntry>();
            foreach (var g in Entries.GroupBy(e => e.Component))
            {
                if (Comp.ArchNeutral(g.Key) && g.Key != Comp.Nginx && g.Key != Comp.PhpMyAdmin && g.Key != Comp.Adminer && g.Key != Comp.SqlSrv) { keep.AddRange(g); continue; }
                bool single = g.Key == Comp.PhpMyAdmin || g.Key == Comp.Mailpit || g.Key == Comp.Mkcert || g.Key == Comp.Adminer || g.Key == Comp.SqlSrv;
                foreach (var b in g.GroupBy(e => Comp.MajorBranch(g.Key) ? e.Version.Split('.')[0] : (single ? "x" : MajorMinor(e.Version))))
                    keep.Add(b.OrderByDescending(e => VersionKey(e.Version)).First());
            }
            Entries = keep;

            // 2) Önerilen sürümler
            void Mark(string comp, Func<List<CatalogEntry>, CatalogEntry> pick)
            {
                var list = AvailableFor(comp).ToList();
                if (list.Count == 0) return;
                foreach (var e in For(comp)) e.Recommended = false;
                var r = pick(list) ?? list[0];
                r.Recommended = true;
            }
            // PHP: en yeni dalın bir altı (hosting'lerde en yaygın, phpMyAdmin ile uyumlu)
            Mark(Comp.Php, l => { var branches = l.Select(e => MajorMinor(e.Version)).Distinct().ToList(); return branches.Count > 1 ? l.First(e => MajorMinor(e.Version) == branches[1]) : l[0]; });
            // MariaDB / MySQL: en yeni uzun destekli (LTS) dal
            Mark(Comp.MariaDb, l => l.FirstOrDefault(e => e.Lts || (e.Note ?? "").Contains("LTS")));
            Mark(Comp.MySql, l => l.FirstOrDefault(e => e.Lts || (e.Note ?? "").Contains("LTS")));
            // Node.js: bu mimaride bulunan en yeni LTS
            Mark(Comp.Node, l => l.FirstOrDefault(e => e.Lts || (e.Note ?? "").StartsWith("LTS")));
            // Nginx: kararlı dal
            Mark(Comp.Nginx, l => l.FirstOrDefault(e => (e.Note ?? "").StartsWith("Kararlı")));
            // PostgreSQL: en yeni ana sürümün bir altı (ilk yamalarını almış, eklentileri olgun)
            Mark(Comp.PostgreSql, l => { var majors = l.Select(e => e.Version.Split('.')[0]).Distinct().ToList(); return majors.Count > 1 ? l.First(e => e.Version.Split('.')[0] == majors[1]) : l[0]; });
            foreach (var c in new[] { Comp.Apache, Comp.PhpMyAdmin, Comp.Mailpit, Comp.Mkcert, Comp.Adminer, Comp.SqlSrv }) Mark(c, l => l[0]);
            foreach (var e in Entries.Where(e => e.Recommended && string.IsNullOrEmpty(e.Note))) e.Note = "Önerilen";
        }

        private void Upsert(string comp, string ver, List<string> urls, List<string> x86, string branch, string note = null, bool? lts = null)
        {
            var e = Find(comp, ver);
            if (e == null)
            {
                e = new CatalogEntry { Component = comp, Version = ver, Branch = branch ?? MajorMinor(ver), Note = note ?? "" };
                Entries.Add(e);
            }
            e.Urls = urls;
            e.UrlsX86 = x86 ?? new List<string>();
            if (note != null) e.Note = note;
            if (lts.HasValue) e.Lts = lts.Value;
        }

        // ------------------------------------------------------------------
        //  İnternetten güncelleme
        // ------------------------------------------------------------------
        public async Task<string> RefreshOnlineAsync(Action<string> status = null)
        {
            var report = new StringBuilder();
            void Say(string s) { status?.Invoke(s); report.AppendLine(s); }

            // PHP (windows.php.net releases.json — desteklenen tüm dallar, x64 + x86)
            try
            {
                Say("PHP sürümleri sorgulanıyor...");
                var json = await Downloader.GetStringAsync("https://windows.php.net/downloads/releases/releases.json");
                var root = JsonUtil.DeserializeObject(json) as Dictionary<string, object>;
                int n = 0;
                if (root != null)
                    foreach (var kv in root)
                    {
                        var b = kv.Value as Dictionary<string, object>;
                        var ver = b != null && b.ContainsKey("version") ? b["version"]?.ToString() : null;
                        if (string.IsNullOrEmpty(ver)) continue;
                        string Zip(string suffix)
                        {
                            var key = b.Keys.FirstOrDefault(k => k.StartsWith("ts-") && k.EndsWith(suffix));
                            var o = key != null ? b[key] as Dictionary<string, object> : null;
                            var z = o != null && o.ContainsKey("zip") ? o["zip"] as Dictionary<string, object> : null;
                            return z != null && z.ContainsKey("path") ? "https://windows.php.net/downloads/releases/" + z["path"] : null;
                        }
                        var x64 = PhpUrls(ver); var x86 = PhpUrls(ver, "x86");
                        var z64 = Zip("-x64"); var z86 = Zip("-x86");
                        if (z64 != null) x64.Insert(0, z64);
                        if (z86 != null) x86.Insert(0, z86);
                        Upsert(Comp.Php, ver, x64.Distinct().ToList(), x86.Distinct().ToList(), kv.Key, "");
                        n++;
                    }
                Say($"  PHP: {n} dal.");
            }
            catch (Exception ex) { Say("  PHP sorgusu başarısız: " + ex.Message); }

            // MariaDB (REST API — kararlı dallar, LTS bilgisiyle)
            try
            {
                Say("MariaDB sürümleri sorgulanıyor...");
                var json = await Downloader.GetStringAsync("https://downloads.mariadb.org/rest-api/mariadb/");
                var root = JsonUtil.DeserializeObject(json) as Dictionary<string, object>;
                var majors = root != null && root.ContainsKey("major_releases") ? root["major_releases"] as object[] : null;
                int n = 0;
                if (majors != null)
                    foreach (var m in majors.OfType<Dictionary<string, object>>())
                    {
                        var st = m.ContainsKey("release_status") ? m["release_status"]?.ToString() : "";
                        var id = m.ContainsKey("release_id") ? m["release_id"]?.ToString() : null;
                        if (id == null || st != "Stable") continue;
                        var support = m.ContainsKey("release_support_type") ? m["release_support_type"]?.ToString() ?? "" : "";
                        bool lts = support.IndexOf("Long Term", StringComparison.OrdinalIgnoreCase) >= 0;
                        try
                        {
                            var bj = await Downloader.GetStringAsync($"https://downloads.mariadb.org/rest-api/mariadb/{id}/");
                            var br = JsonUtil.DeserializeObject(bj) as Dictionary<string, object>;
                            var rels = br != null && br.ContainsKey("releases") ? br["releases"] as Dictionary<string, object> : null;
                            if (rels == null || rels.Count == 0) continue;
                            var latest = rels.Keys.OrderByDescending(VersionKey).First();
                            Upsert(Comp.MariaDb, latest, MariaDbUrls(latest), null, id, lts ? "LTS" : (support.Length > 0 ? support : "Kararlı"), lts);
                            n++;
                        }
                        catch { }
                    }
                Say($"  MariaDB: {n} dal.");
            }
            catch (Exception ex) { Say("  MariaDB sorgusu başarısız: " + ex.Message); }

            // MySQL (resmi API yok: bilinen dallarda yeni yama dener)
            try
            {
                Say("MySQL sürümleri deneniyor...");
                int n = 0;
                foreach (var branch in new[] { "8.4", "8.0" })
                {
                    var known = For(Comp.MySql).FirstOrDefault(e => e.Branch == branch);
                    if (known == null) continue;
                    var parts = known.Version.Split('.');
                    int patch = int.Parse(parts[2]);
                    string found = null;
                    for (int i = 1; i <= 6; i++)
                    {
                        var cand = $"{parts[0]}.{parts[1]}.{patch + i}";
                        if (await Downloader.HeadSizeAsync(MySqlUrls(cand)[0]) > 0) found = cand; else break;
                    }
                    if (found != null) { Upsert(Comp.MySql, found, MySqlUrls(found), null, branch, known.Note, known.Lts); n++; }
                }
                Say($"  MySQL: {n} yeni yama.");
            }
            catch (Exception ex) { Say("  MySQL sorgusu başarısız: " + ex.Message); }

            // Node.js (index.json — en yeni Current + LTS dallar, x86 varlığıyla)
            try
            {
                Say("Node.js sürümleri sorgulanıyor...");
                var json = await Downloader.GetStringAsync("https://nodejs.org/dist/index.json");
                var arr = JsonUtil.DeserializeObject(json) as object[];
                int n = 0;
                if (arr != null)
                {
                    var seen = new HashSet<string>();
                    foreach (var o in arr.OfType<Dictionary<string, object>>())
                    {
                        var v = o["version"]?.ToString()?.TrimStart('v');
                        var ltsObj = o.ContainsKey("lts") ? o["lts"] : null;
                        bool isLts = ltsObj != null && !(ltsObj is bool);
                        var major = v?.Split('.')[0];
                        if (v == null || major == null || seen.Contains(major)) continue;
                        if (!isLts && seen.Count > 0) continue;
                        seen.Add(major);
                        var files = (o.ContainsKey("files") ? o["files"] as object[] : null)?.Select(x => x?.ToString()).ToList() ?? new List<string>();
                        var x86 = files.Contains("win-x86-zip") ? NodeUrls(v, "x86") : null;
                        Upsert(Comp.Node, v, NodeUrls(v), x86, major, isLts ? "LTS " + ltsObj : "Current", isLts);
                        n++;
                        if (seen.Count >= 5) break;
                    }
                }
                Say($"  Node.js: {n} dal.");
            }
            catch (Exception ex) { Say("  Node.js sorgusu başarısız: " + ex.Message); }

            // PostgreSQL (versions.json — desteklenen ana sürümlerin son yaması; EDB paket numarası HEAD ile bulunur)
            try
            {
                Say("PostgreSQL sürümleri sorgulanıyor...");
                var json = await Downloader.GetStringAsync("https://www.postgresql.org/versions.json");
                var arr = JsonUtil.DeserializeObject(json) as object[];
                int n = 0;
                if (arr != null)
                    foreach (var o in arr.OfType<Dictionary<string, object>>())
                    {
                        bool supported = o.ContainsKey("supported") && o["supported"] is bool sb && sb;
                        var major = o.ContainsKey("major") ? o["major"]?.ToString() : null;
                        var minor = o.ContainsKey("latestMinor") ? o["latestMinor"]?.ToString() : null;
                        if (!supported || string.IsNullOrEmpty(major) || string.IsNullOrEmpty(minor)) continue;
                        var v = major + "." + minor;
                        var known = For(Comp.PostgreSql).FirstOrDefault(e => e.Version.Split('.')[0] == major);
                        if (known != null && known.Version == v) { n++; continue; }
                        // EDB bazen ilk paketi -1 yerine -2 ile yayınlar: çalışan bağlantıyı öne al
                        var urls = PostgreSqlUrls(v);
                        string ok = null;
                        foreach (var u in urls.Take(3)) if (await Downloader.HeadSizeAsync(u) > 0) { ok = u; break; }
                        if (ok == null) continue; // Windows paketi henüz yayınlanmamış
                        urls.Remove(ok); urls.Insert(0, ok);
                        Upsert(Comp.PostgreSql, v, urls, null, major, known?.Note ?? "");
                        n++;
                    }
                Say($"  PostgreSQL: {n} dal.");
            }
            catch (Exception ex) { Say("  PostgreSQL sorgusu başarısız: " + ex.Message); }

            // phpMyAdmin
            try
            {
                Say("phpMyAdmin sürümü sorgulanıyor...");
                var json = await Downloader.GetStringAsync("https://www.phpmyadmin.net/home_page/version.json");
                var root = JsonUtil.DeserializeObject(json) as Dictionary<string, object>;
                var v = root != null && root.ContainsKey("version") ? root["version"]?.ToString() : null;
                if (!string.IsNullOrEmpty(v))
                {
                    Upsert(Comp.PhpMyAdmin, v, new List<string> { $"https://files.phpmyadmin.net/phpMyAdmin/{v}/phpMyAdmin-{v}-all-languages.zip" }, null, MajorMinor(v));
                    Say("  phpMyAdmin: " + v);
                }
            }
            catch (Exception ex) { Say("  phpMyAdmin sorgusu başarısız: " + ex.Message); }

            // Mailpit & mkcert (GitHub; yalnız 64 bit / ARM64 Windows sürümü var)
            // Adminer (adminer-6.1.1.php) ve SQL Server sürücüsü (Windows_5.13.3RTW.zip) de GitHub'dan
            foreach (var gh in new[]
            {
                new { comp = Comp.Mailpit, repo = "axllent/mailpit", pattern = @"windows-amd64\.zip$" },
                new { comp = Comp.Mkcert, repo = "FiloSottile/mkcert", pattern = @"windows-amd64\.exe$" },
                new { comp = Comp.Adminer, repo = "vrana/adminer", pattern = @"^adminer-[\d.]+\.php$" },
                new { comp = Comp.SqlSrv, repo = "microsoft/msphpsql", pattern = @"^Windows_[\d.]+RTW\.zip$" }
            })
            {
                try
                {
                    Say(Comp.Title(gh.comp) + " sorgulanıyor...");
                    var json = await Downloader.GetStringAsync($"https://api.github.com/repos/{gh.repo}/releases/latest");
                    var root = JsonUtil.DeserializeObject(json) as Dictionary<string, object>;
                    var tag = root != null && root.ContainsKey("tag_name") ? root["tag_name"]?.ToString() : null;
                    var assets = root != null && root.ContainsKey("assets") ? root["assets"] as object[] : null;
                    var asset = assets?.OfType<Dictionary<string, object>>().FirstOrDefault(a => Regex.IsMatch(a["name"]?.ToString() ?? "", gh.pattern));
                    if (tag != null && asset != null)
                    {
                        var v = tag.TrimStart('v');
                        Upsert(gh.comp, v, new List<string> { asset["browser_download_url"].ToString() }, null, v.Split('.')[0]);
                        Say("  " + Comp.Title(gh.comp) + ": " + v);
                    }
                }
                catch (Exception ex) { Say("  " + Comp.Title(gh.comp) + " sorgusu başarısız: " + ex.Message); }
            }

            // Apache Lounge (64 + 32 bit bağlantıları)
            try
            {
                Say("Apache sorgulanıyor...");
                var html = await Downloader.GetStringAsync("https://www.apachelounge.com/download/");
                var links = Regex.Matches(html, @"href=""(/download/VS\d+/binaries/httpd-(2\.4\.\d+)-\d+-(win64|win32)-vs\d+\.zip)""", RegexOptions.IgnoreCase)
                                 .Cast<Match>().Select(m => new { url = "https://www.apachelounge.com" + m.Groups[1].Value, ver = m.Groups[2].Value, arch = m.Groups[3].Value.ToLowerInvariant() }).ToList();
                foreach (var g in links.GroupBy(l => l.ver))
                {
                    var x64 = g.Where(l => l.arch == "win64").Select(l => l.url).ToList();
                    var x86 = g.Where(l => l.arch == "win32").Select(l => l.url).ToList();
                    if (x64.Count > 0) Upsert(Comp.Apache, g.Key, x64, x86, "2.4", "Apache Lounge");
                }
                Say($"  Apache: {links.Select(l => l.ver).Distinct().Count()} sürüm.");
            }
            catch (Exception ex) { Say("  Apache sorgusu başarısız: " + ex.Message); }

            // Nginx (ilk bağlantı mainline, ikinci kararlı)
            try
            {
                Say("Nginx sorgulanıyor...");
                var html = await Downloader.GetStringAsync("https://nginx.org/en/download.html");
                var vers = Regex.Matches(html, @"/download/nginx-(\d+\.\d+\.\d+)\.zip""").Cast<Match>().Select(x => x.Groups[1].Value).Distinct().Take(2).ToList();
                for (int i = 0; i < vers.Count; i++) Upsert(Comp.Nginx, vers[i], NginxUrls(vers[i]), null, MajorMinor(vers[i]), i == 0 && vers.Count > 1 ? "Mainline" : "Kararlı");
                Say($"  Nginx: {vers.Count} sürüm.");
            }
            catch (Exception ex) { Say("  Nginx sorgusu başarısız: " + ex.Message); }

            Normalize();
            UpdatedAt = DateTime.Now;
            Save();
            Say("Katalog güncellendi.");
            return report.ToString();
        }
    }
}
