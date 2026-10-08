using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DevNanotek.Core
{
    /// <summary>
    /// - bin\current\php, bin\current\node, bin\current\db : aktif sürüme işaret eden junction'lar (PATH sabit kalır)
    /// - bin\shims : composer.cmd, mysql.cmd gibi sarmalayıcılar
    /// - Kullanıcı PATH değişkeni yönetimi
    /// </summary>
    public static class EnvPath
    {
        public static string CurrentPhp => Path.Combine(Paths.BinCurrent, "php");
        public static string CurrentNode => Path.Combine(Paths.BinCurrent, "node");
        public static string CurrentDb => Path.Combine(Paths.BinCurrent, "db");
        public static string CurrentPg => Path.Combine(Paths.BinCurrent, "pgsql");

        public static bool IsReparsePoint(string path)
        {
            try { return Directory.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
            catch { return false; }
        }

        /// <summary>
        /// link -> target junction'ı (yoksa oluşturur, farklıysa yeniler). target null ise link kaldırılır.
        /// Hedef, yanındaki "{link}.target" kayıt dosyasında tutulur (fsutil çıktısı dile göre değiştiği için).
        /// </summary>
        public static void SetJunction(string link, string target)
        {
            var record = link + ".target";
            try
            {
                var full = target == null ? null : Path.GetFullPath(target).TrimEnd('\\');
                if (Directory.Exists(link))
                {
                    if (IsReparsePoint(link))
                    {
                        string recorded = null;
                        try { if (File.Exists(record)) recorded = File.ReadAllText(record).Trim(); } catch { }
                        if (full != null && Directory.Exists(full) && string.Equals(recorded, full, StringComparison.OrdinalIgnoreCase)) return;
                        Directory.Delete(link, false); // junction'ı siler, hedefe dokunmaz
                    }
                    else
                    {
                        // gerçek bir klasör: kenara al
                        Directory.Move(link, link + ".old-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                    }
                }
                try { if (File.Exists(record)) File.Delete(record); } catch { }
                if (full == null || !Directory.Exists(full)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(link));
                var r = ProcessRunner.Cmd($"mklink /J \"{link}\" \"{full}\"");
                if (!r.Ok || !Directory.Exists(link)) { Logger.Warn("Junction oluşturulamadı: " + link + " -> " + r.AllOutput); return; }
                File.WriteAllText(record, full);
            }
            catch (Exception ex) { Logger.Warn("Junction hatası (" + link + "): " + ex.Message); }
        }

        /// <summary>bin\shims içine .cmd sarmalayıcıları yazar.</summary>
        public static void WriteShims(AppConfig cfg)
        {
            try
            {
                Directory.CreateDirectory(Paths.BinShims);
                var php = Path.Combine(CurrentPhp, "php.exe");
                var composer = Path.Combine(Paths.BinComposer, "composer.phar");
                var dbBin = Path.Combine(CurrentDb, "bin");

                void Shim(string name, string body)
                {
                    var file = Path.Combine(Paths.BinShims, name + ".cmd");
                    var txt = "@echo off\r\n" + body + "\r\n";
                    if (!File.Exists(file) || File.ReadAllText(file) != txt) File.WriteAllText(file, txt, Encoding.ASCII);
                }

                Shim("composer", $"\"{php}\" \"{composer}\" %*");
                foreach (var tool in new[] { "mysql", "mysqldump", "mysqladmin", "mysqlcheck", "mariadb", "mariadb-dump", "mariadb-admin" })
                    Shim(tool, $"if exist \"{dbBin}\\{tool}.exe\" (\"{dbBin}\\{tool}.exe\" %*) else (echo {tool}.exe aktif veritabani surumunde yok & exit /b 1)");
                // PostgreSQL araçları: varsayılan bağlantı 127.0.0.1 + postgres kullanıcısı (ortam değişkenleri ezilmez)
                var pgBin = Path.Combine(CurrentPg, "bin");
                foreach (var tool in new[] { "psql", "pg_dump", "pg_dumpall", "pg_restore", "createdb", "dropdb", "pg_isready" })
                    Shim(tool, $"setlocal\r\nif not defined PGHOST set PGHOST=127.0.0.1\r\nif not defined PGPORT set PGPORT={cfg.PgPort}\r\nif not defined PGUSER set PGUSER=postgres\r\n" +
                               $"if exist \"{pgBin}\\{tool}.exe\" (\"{pgBin}\\{tool}.exe\" %*) else (echo PostgreSQL kurulu degil - Surumler sayfasindan kurun & exit /b 1)");
                Shim("mailpit", $"\"{Path.Combine(Paths.BinMailpit, "mailpit.exe")}\" %*");
                Shim("mkcert", $"set CAROOT={Path.Combine(Paths.EtcSsl, "ca")}\r\n\"{Path.Combine(Paths.BinMkcert, "mkcert.exe")}\" %*");
                Shim("devnanotek-env", "echo DevNanotek ortami hazir. PHP: & php -v & echo. & node -v 2>nul");
            }
            catch (Exception ex) { Logger.Warn("Shim yazılamadı: " + ex.Message); }
        }

        public static IEnumerable<string> PathEntries() => new[] { CurrentPhp, CurrentNode, Paths.BinShims };

        /// <summary>Terminal açarken kullanılacak PATH (sistem PATH'in önüne eklenir).</summary>
        public static string BuildToolPath(AppConfig cfg)
        {
            var list = new List<string> { CurrentPhp, CurrentNode, Paths.BinShims, Path.Combine(CurrentDb, "bin"), Path.Combine(CurrentPg, "bin"), Paths.BinMailpit, Paths.BinMkcert };
            var sys = Environment.GetEnvironmentVariable("PATH") ?? "";
            return string.Join(";", list.Where(Directory.Exists).Concat(new[] { sys }));
        }

        public static Dictionary<string, string> ToolEnv(AppConfig cfg)
        {
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PATH"] = BuildToolPath(cfg),
                ["DEVNANOTEK_ROOT"] = Paths.Root,
                ["COMPOSER_HOME"] = Path.Combine(Paths.BinComposer, "home"),
                ["CAROOT"] = Path.Combine(Paths.EtcSsl, "ca"),
                ["PHP_INI_SCAN_DIR"] = ""
            };
            return env;
        }

        // ---------------- Kullanıcı PATH ----------------
        public static bool IsInUserPath()
        {
            var cur = ReadUserPath();
            return PathEntries().All(e => cur.Any(x => string.Equals(x.TrimEnd('\\'), e, StringComparison.OrdinalIgnoreCase)));
        }

        public static void ApplyUserPath(bool add)
        {
            try
            {
                var cur = ReadUserPath();
                var wanted = PathEntries().ToList();
                var changed = false;
                if (add)
                {
                    foreach (var e in wanted)
                        if (!cur.Any(x => string.Equals(x.TrimEnd('\\'), e, StringComparison.OrdinalIgnoreCase))) { cur.Insert(0, e); changed = true; }
                }
                else
                {
                    int before = cur.Count;
                    cur.RemoveAll(x => wanted.Any(w => string.Equals(x.TrimEnd('\\'), w, StringComparison.OrdinalIgnoreCase)));
                    changed = cur.Count != before;
                }
                if (!changed) return;
                using (var key = Registry.CurrentUser.OpenSubKey("Environment", true))
                    key.SetValue("Path", string.Join(";", cur), RegistryValueKind.ExpandString);
                BroadcastEnvironmentChange();
                Logger.Info("Kullanıcı PATH güncellendi (" + (add ? "eklendi" : "kaldırıldı") + ")");
            }
            catch (Exception ex) { Logger.Warn("PATH güncellenemedi: " + ex.Message); }
        }

        private static List<string> ReadUserPath()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("Environment"))
                {
                    var v = key?.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
                    return v.Split(';').Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                }
            }
            catch { return new List<string>(); }
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

        private static void BroadcastEnvironmentChange()
        {
            try
            {
                UIntPtr res;
                SendMessageTimeout((IntPtr)0xffff, 0x001A /*WM_SETTINGCHANGE*/, UIntPtr.Zero, "Environment", 0x0002 /*SMTO_ABORTIFHUNG*/, 3000, out res);
            }
            catch { }
        }
    }
}
