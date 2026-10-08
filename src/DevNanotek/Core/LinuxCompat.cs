using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DevNanotek.Core
{
    /// <summary>Linux uyumluluk denetiminde bulunan tek bir sorun.</summary>
    public class CompatIssue
    {
        public Level Level { get; set; }          // Error = Linux'ta kesin hata, Warn = olası sorun, Off = bilgi
        public string Category { get; set; }
        public string File { get; set; }          // tam yol
        public string RelFile { get; set; }       // projeye göre
        public int Line { get; set; }
        public string Message { get; set; }
        public string Hint { get; set; }
        public string Where => Line > 0 ? $"{RelFile}:{Line}" : RelFile;
        public string LevelText => Level == Level.Error ? "HATA" : Level == Level.Warn ? "UYARI" : "BİLGİ";
    }

    public class CompatReport
    {
        public string Root { get; set; }
        public List<CompatIssue> Issues { get; } = new List<CompatIssue>();
        internal HashSet<string> Seen { get; } = new HashSet<string>();
        public int FilesScanned { get; set; }
        public bool Truncated { get; set; }
        public int Errors => Issues.Count(i => i.Level == Level.Error);
        public int Warnings => Issues.Count(i => i.Level == Level.Warn);
        public int Infos => Issues.Count(i => i.Level == Level.Off);

        public string ToText()
        {
            // rapor arayüz dilinde (panoya kopyalama / dosyaya kaydetme)
            var sb = new StringBuilder();
            sb.AppendLine(L.T("DEVNANOTEK — Linux uyumluluk raporu"));
            sb.AppendLine(L.F("Proje : {0}", Root));
            sb.AppendLine(L.F("Tarih : {0}", DateTime.Now.ToString("dd.MM.yyyy HH:mm")));
            sb.AppendLine(L.F("Sonuç : {0} hata, {1} uyarı, {2} bilgi ({3} dosya tarandı)", Errors, Warnings, Infos, FilesScanned));
            sb.AppendLine(new string('-', 70));
            foreach (var i in Issues)
            {
                sb.AppendLine($"[{L.T(i.LevelText)}] {i.Where}");
                sb.AppendLine("    " + L.T(i.Message));
                if (!string.IsNullOrEmpty(i.Hint)) sb.AppendLine("    → " + L.T(i.Hint));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Windows'ta çalışıp Linux sunucuda (Plesk / cPanel / VPS) bozulan şeyleri önceden bulur:
    /// büyük/küçük harf uyumsuz dosya yolları, ters bölü, Windows yolları, CRLF'li betikler,
    /// .htaccess'te php_value, karışık harfli tablo adları, Windows'a özel komutlar.
    /// </summary>
    public static class LinuxCompat
    {
        private static readonly HashSet<string> SkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "vendor", "node_modules", ".git", ".svn", ".hg", ".idea", ".vscode", "bower_components", ".next", ".nuxt", ".cache", "cache" };
        private static readonly HashSet<string> TextExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".php", ".phtml", ".inc", ".html", ".htm", ".tpl", ".twig", ".css", ".scss", ".js", ".mjs", ".vue", ".sh", ".env", ".htaccess", ".ini", ".json", ".xml", ".sql" };
        private const int MaxFiles = 25000;
        private const long MaxBytes = 2 * 1024 * 1024;

        private static readonly Regex RxInclude = new Regex(@"\b(?:require|require_once|include|include_once)\b\s*\(?\s*(?<dir>(?:__DIR__|dirname\s*\(\s*__FILE__\s*\))\s*\.\s*)?(?<q>['""])(?<p>[^'""$\{\}\r\n]+)\k<q>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RxAsset = new Regex(@"(?:\b(?:src|href|action)\s*=\s*|url\(\s*)(?<q>['""]?)(?<p>[^'""\s\)\(<>{}#?]+\.[A-Za-z0-9]{1,5})\k<q>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RxWinPath = new Regex(@"['""](?<p>[A-Za-z]:[\\/][^'""\r\n]{0,120})['""]", RegexOptions.Compiled);
        private static readonly Regex RxWinCmd = new Regex(@"\b(?:exec|shell_exec|system|passthru|proc_open|popen)\s*\(\s*['""](?<c>[^'""]*?)(?:['""]|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RxCom = new Regex(@"\bnew\s+\\?(?:COM|DOTNET|VARIANT)\s*\(", RegexOptions.Compiled);
        private static readonly Regex RxSqlTable = new Regex(@"\b(?:FROM|JOIN|INTO|UPDATE|TABLE(?:\s+IF\s+(?:NOT\s+)?EXISTS)?)\s+`?(?<t>[A-Za-z_][A-Za-z0-9_]*)`?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RxSqlStart = new Regex(@"\b(?:SELECT|INSERT|UPDATE|DELETE|CREATE|ALTER|DROP|REPLACE)\b", RegexOptions.Compiled);
        private static readonly Regex RxClass = new Regex(@"^\s*(?:(?:abstract|final|readonly)\s+)*(?:class|interface|trait|enum)\s+(?<n>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex RxTurkish = new Regex("[çğıöşüÇĞİÖŞÜ]", RegexOptions.Compiled);

        public static CompatReport Scan(string root, Action<string> progress = null, CancellationToken ct = default(CancellationToken))
        {
            root = Path.GetFullPath(root).TrimEnd('\\');
            var rep = new CompatReport { Root = root };
            var files = new List<string>();
            Walk(root, files, rep, ct);
            if (files.Count > MaxFiles) { rep.Truncated = true; files = files.Take(MaxFiles).ToList(); }

            // projede composer.json varsa web kökü public/ olabilir: kök-göreli (/css/a.css) yollar için adaylar
            var webRoots = new List<string> { root };
            foreach (var p in new[] { "public", "public_html", "httpdocs", "web", "www" })
                if (Directory.Exists(Path.Combine(root, p))) webRoots.Add(Path.Combine(root, p));

            int n = 0;
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                if (++n % 200 == 0) progress?.Invoke($"Taranıyor… {n}/{files.Count}");
                rep.FilesScanned++;
                CheckName(f, rep);
                string text;
                try
                {
                    if (new FileInfo(f).Length > MaxBytes) continue;
                    var bytes = File.ReadAllBytes(f);
                    if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF && IsPhp(f))
                        Add(rep, Level.Warn, "Kodlama", f, 1, "PHP dosyası UTF-8 BOM ile başlıyor.", "BOM, sayfa başına görünmez karakter ekler; header()/session_start() 'headers already sent' hatası verebilir. Editörde 'UTF-8 (BOM'suz)' olarak kaydedin.");
                    text = Encoding.UTF8.GetString(bytes);
                }
                catch { continue; }
                CheckText(f, text, rep, webRoots);
            }
            rep.Issues.Sort((a, b) =>
            {
                int r = Rank(a.Level).CompareTo(Rank(b.Level));
                if (r != 0) return r;
                r = string.Compare(a.RelFile, b.RelFile, StringComparison.OrdinalIgnoreCase);
                return r != 0 ? r : a.Line.CompareTo(b.Line);
            });
            progress?.Invoke($"Bitti: {rep.FilesScanned} dosya.");
            return rep;
        }

        private static int Rank(Level l) => l == Level.Error ? 0 : l == Level.Warn ? 1 : 2;

        private static void Walk(string dir, List<string> files, CompatReport rep, CancellationToken ct)
        {
            if (files.Count > MaxFiles) return;
            ct.ThrowIfCancellationRequested();
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    var name = Path.GetFileName(f);
                    var ext = Path.GetExtension(f);
                    if (TextExt.Contains(ext) || name.Equals(".htaccess", StringComparison.OrdinalIgnoreCase) || name.Equals(".user.ini", StringComparison.OrdinalIgnoreCase) || IsShebangCandidate(f, ext))
                        files.Add(f);
                    else CheckName(f, rep);
                }
                foreach (var d in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(d);
                    try { if (File.GetAttributes(d).HasFlag(FileAttributes.ReparsePoint)) continue; } catch { continue; }
                    if (SkipDirs.Contains(name)) continue;
                    CheckName(d, rep);
                    Walk(d, files, rep, ct);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        private static bool IsShebangCandidate(string f, string ext)
        {
            // uzantısız betikler (artisan, bin/console …): #! ile başlıyorsa
            if (!string.IsNullOrEmpty(ext)) return false;
            try
            {
                using (var fs = File.OpenRead(f)) { var b = new byte[2]; return fs.Read(b, 0, 2) == 2 && b[0] == (byte)'#' && b[1] == (byte)'!'; }
            }
            catch { return false; }
        }

        private static bool IsPhp(string f)
        {
            var e = Path.GetExtension(f).ToLowerInvariant();
            return e == ".php" || e == ".phtml" || e == ".inc";
        }

        private static void CheckName(string path, CompatReport rep)
        {
            var name = Path.GetFileName(path);
            if (RxTurkish.IsMatch(name))
                Add(rep, Level.Warn, "Dosya adı", path, 0, $"Adında Türkçe karakter var: \"{name}\".", "FTP/zip aktarımında ad bozulabilir ve adreslerde kodlama gerekir. Türkçe karakter kullanmayın (ç→c, ğ→g, ı→i, ö→o, ş→s, ü→u).");
            else if (name.Contains(" "))
                Add(rep, Level.Off, "Dosya adı", path, 0, $"Adında boşluk var: \"{name}\".", "Adreslerde %20 olur ve betiklerde tırnak gerektirir; boşluk yerine - veya _ kullanın.");
        }

        private static void CheckText(string f, string text, CompatReport rep, List<string> webRoots)
        {
            var name = Path.GetFileName(f);
            var ext = Path.GetExtension(f).ToLowerInvariant();
            bool php = IsPhp(f);
            bool isScript = ext == ".sh" || (string.IsNullOrEmpty(ext) && text.StartsWith("#!"));

            // 1) CRLF'li kabuk betikleri
            if (isScript && text.Contains("\r\n"))
                Add(rep, Level.Error, "Satır sonu", f, 1, "Kabuk betiği Windows satır sonu (CRLF) içeriyor.",
                    "Linux'ta \"/bin/bash^M: bad interpreter\" hatası verir. VS Code sağ altta CRLF → LF yapın veya .gitattributes'a \"*.sh text eol=lf\" ekleyin. Sunucuda çalıştırma izni de gerekir: chmod +x " + name);

            // 2) .htaccess / .user.ini
            if (name.Equals(".htaccess", StringComparison.OrdinalIgnoreCase))
            {
                int ln = 0;
                foreach (var line in Lines(text))
                {
                    ln++;
                    var t = line.Trim();
                    if (t.StartsWith("#")) continue;
                    if (Regex.IsMatch(t, @"^php_(value|flag|admin_value|admin_flag)\b", RegexOptions.IgnoreCase))
                        Add(rep, Level.Warn, ".htaccess", f, ln, "php_value / php_flag satırı: " + Short(t),
                            "Yerelde (mod_php) çalışır, ama sunucu PHP-FPM kullanıyorsa (cPanel/Plesk'te yaygın) 500 Internal Server Error verir. Bu ayarları .user.ini dosyasına (php_value olmadan, ayar = değer) ya da panelin PHP ayarlarına taşıyın.");
                }
            }

            if (!(php || ext == ".html" || ext == ".htm" || ext == ".tpl" || ext == ".twig" || ext == ".css" || ext == ".scss" || ext == ".vue" || ext == ".js" || ext == ".mjs")) return;

            var dir = Path.GetDirectoryName(f);
            int no = 0;
            foreach (var line in Lines(text))
            {
                no++;
                if (line.Length > 4000) continue; // sıkıştırılmış (minified) dosya satırları
                var trimmed = line.TrimStart();
                bool comment = trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("#") || trimmed.StartsWith("/*");

                if (php && !comment)
                {
                    // 3) include / require yolları
                    foreach (Match m in RxInclude.Matches(line))
                    {
                        var p = m.Groups["p"].Value;
                        if (p.IndexOf('\\') >= 0)
                            Add(rep, Level.Error, "Dosya yolu", f, no, $"Yolda ters bölü (\\) var: '{p}'", "Linux'ta \\ klasör ayırıcı değildir, dosya bulunamaz. / kullanın: '" + p.Replace('\\', '/') + "'");
                        if (Regex.IsMatch(p, @"^[A-Za-z]:")) continue; // aşağıda Windows yolu olarak raporlanır
                        bool withDir = m.Groups["dir"].Success && m.Groups["dir"].Value.Length > 0;
                        if (!withDir && p.StartsWith("/")) continue;      // Linux mutlak yolu: denetlenemez
                        var baseDirs = withDir ? new[] { dir } : new[] { dir, rep.Root };
                        CheckCase(rep, f, no, p.Replace('\\', '/').TrimStart('/'), baseDirs, "include/require");
                    }
                    // 4) Windows mutlak yolları
                    foreach (Match m in RxWinPath.Matches(line))
                    {
                        var p = m.Groups["p"].Value;
                        if (p.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                        Add(rep, Level.Error, "Windows yolu", f, no, "Windows'a özel mutlak yol: '" + Short(p) + "'",
                            "Sunucuda C:\\ diye bir sürücü yoktur. __DIR__ ile göreli yol kullanın (örn. __DIR__ . '/uploads') ya da yolu .env / config'den okuyun.");
                    }
                    // 5) Windows'a özel komutlar ve COM
                    foreach (Match m in RxWinCmd.Matches(line))
                    {
                        var c = m.Groups["c"].Value.Trim();
                        if (Regex.IsMatch(c, @"(\.exe|\.bat|\.cmd)\b|^(cmd|dir|del|copy|move|xcopy|robocopy|tasklist|taskkill|start|type|where|powershell)\b", RegexOptions.IgnoreCase))
                            Add(rep, Level.Error, "Komut", f, no, "Windows komutu çalıştırılıyor: " + Short(c),
                                "Linux'ta bu komut yoktur. Karşılıkları: dir→ls, del→rm, copy→cp, move→mv, tasklist→ps, taskkill→kill, type→cat, where→which. Mümkünse PHP fonksiyonlarını kullanın (scandir, unlink, copy, rename).");
                    }
                    if (RxCom.IsMatch(line))
                        Add(rep, Level.Error, "Windows API", f, no, "COM / DOTNET nesnesi kullanılıyor.", "Yalnız Windows'ta vardır; Linux sunucuda çalışmaz.");

                    // 6) karışık harfli tablo adları (yalnız SQL içeren satırlarda)
                    if ((line.IndexOf('"') >= 0 || line.IndexOf('\'') >= 0 || line.IndexOf('`') >= 0) && RxSqlStart.IsMatch(line))
                        foreach (Match m in RxSqlTable.Matches(line))
                        {
                            var t = m.Groups["t"].Value;
                            if (t.Any(char.IsUpper) && t.Any(char.IsLower) && !t.StartsWith("$"))
                                Add(rep, Level.Warn, "Tablo adı", f, no, $"Büyük harf içeren tablo adı: {t}",
                                    "Linux'ta MySQL/MariaDB tablo adları büyük/küçük harfe duyarlıdır; Windows'ta tablolar küçük harfle saklanır. Yedeği sunucuya aktarınca '" + t + "' bulunamaz. Tablo adlarını her yerde küçük harfle yazın (örn. '" + t.ToLowerInvariant() + "').");
                        }
                }

                // 7) HTML/CSS/JS'teki göreli dosya bağlantıları (resim, css, js)
                if (!comment || ext == ".css" || ext == ".scss")
                    foreach (Match m in RxAsset.Matches(line))
                    {
                        var p = m.Groups["p"].Value;
                        if (Regex.IsMatch(p, @"^(https?:|//|data:|mailto:|tel:|javascript:|\w+://)", RegexOptions.IgnoreCase) || p.Contains("<?") || p.Contains("{{")) continue;
                        if (p.IndexOf('\\') >= 0)
                        {
                            Add(rep, Level.Error, "Dosya bağlantısı", f, no, $"Bağlantıda ters bölü (\\) var: '{p}'", "Tarayıcılar ve Linux sunucular \\ işaretini klasör ayırıcı saymaz; / kullanın.");
                            continue;
                        }
                        if (p.StartsWith("/")) CheckCase(rep, f, no, p.TrimStart('/'), webRoots.ToArray(), "bağlantı");
                        else CheckCase(rep, f, no, p, new[] { dir }, "bağlantı");
                    }
            }

            // 8) PSR-4: dosya adı ile sınıf adı harfleri aynı olmalı (Composer autoload Linux'ta bulamaz)
            if (php)
            {
                var classes = RxClass.Matches(text).Cast<Match>().Select(m => m.Groups["n"].Value).Distinct().ToList();
                var baseName = Path.GetFileNameWithoutExtension(f);
                if (classes.Count == 1 && classes[0] != baseName && string.Equals(classes[0], baseName, StringComparison.OrdinalIgnoreCase))
                    Add(rep, Level.Error, "Sınıf adı", f, 0, $"Dosya adı '{baseName}.php' ama sınıf adı '{classes[0]}'.",
                        "Composer otomatik yükleyicisi Linux'ta '" + classes[0] + ".php' dosyasını arar ve bulamaz (Class not found). Dosyayı '" + classes[0] + ".php' olarak yeniden adlandırın.");
            }
        }

        /// <summary>
        /// Göreli yolu aday klasörlerde arar; Windows'ta bulunan ama harfleri farklı olan yol Linux'ta bulunamaz.
        /// Hiç bulunamayan yollar raporlanmaz (dinamik/üretilen dosyalar olabilir).
        /// </summary>
        private static void CheckCase(CompatReport rep, string file, int line, string rel, string[] bases, string what)
        {
            rel = rel.Split('?', '#')[0];
            if (rel.Length == 0 || rel.Contains("..\\")) return;
            foreach (var b in bases.Distinct())
            {
                string full;
                try { full = Path.GetFullPath(Path.Combine(b, rel.Replace('/', '\\'))); } catch { continue; }
                if (!File.Exists(full) && !Directory.Exists(full)) continue;
                var actual = ActualCase(full);
                if (actual == null) return;
                if (!string.Equals(actual, full, StringComparison.Ordinal))
                {
                    var wrong = DiffPart(full, actual);
                    Add(rep, Level.Error, "Büyük/küçük harf", file, line, $"{what}: '{rel}' — diskteki ad '{wrong.Item2}', kodda '{wrong.Item1}'.",
                        "Windows'ta çalışır ama Linux harf duyarlıdır: dosya bulunamaz (404 / failed to open stream). Koddaki adı diskteki adla birebir aynı yazın.");
                }
                return;
            }
        }

        /// <summary>Var olan bir yolun diskteki gerçek yazımı.</summary>
        private static string ActualCase(string path)
        {
            try
            {
                var parts = path.Split('\\');
                var cur = parts[0].ToUpperInvariant() + "\\";
                for (int i = 1; i < parts.Length; i++)
                {
                    if (parts[i].Length == 0) continue;
                    var match = Directory.EnumerateFileSystemEntries(cur, parts[i]).FirstOrDefault();
                    if (match == null) return null;
                    cur = Path.Combine(cur, Path.GetFileName(match));
                }
                return cur.TrimEnd('\\');
            }
            catch { return null; }
        }

        private static Tuple<string, string> DiffPart(string wanted, string actual)
        {
            var a = wanted.Split('\\'); var b = actual.Split('\\');
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return Tuple.Create(a[i], b[i]);
            return Tuple.Create(Path.GetFileName(wanted), Path.GetFileName(actual));
        }

        private static IEnumerable<string> Lines(string text) => text.Replace("\r\n", "\n").Split('\n');

        private static string Short(string s) => s.Length > 90 ? s.Substring(0, 90) + "…" : s;

        private static void Add(CompatReport rep, Level level, string cat, string file, int line, string msg, string hint)
        {
            var rel = file.StartsWith(rep.Root, StringComparison.OrdinalIgnoreCase) ? file.Substring(rep.Root.Length).TrimStart('\\') : file;
            if (rel.Length == 0) rel = Path.GetFileName(file);
            // aynı satırda aynı mesajı bir kez göster
            if (!rep.Seen.Add(file.ToLowerInvariant() + "|" + line + "|" + msg)) return;
            rep.Issues.Add(new CompatIssue { Level = level, Category = cat, File = file, RelFile = rel.Replace('\\', '/'), Line = line, Message = msg, Hint = hint });
        }
    }
}
