using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DevNanotek.Core
{
    /// <summary>
    /// Proje adresleri (GitHub deposu, site, sürümler). Depo adı değişirse yalnız buradaki iki satır değişir;
    /// site (GitHub Pages) depo adını kendi adresinden bulur.
    /// </summary>
    public static class AppInfo
    {
        public const string Owner = "devnanotek";
        public const string Repo = "local-server";

        public static string RepoUrl => $"https://github.com/{Owner}/{Repo}";
        public static string PagesUrl => $"https://{Owner}.github.io/{Repo}/";
        public static string ReleasesUrl => RepoUrl + "/releases";
        public static string IssuesUrl => RepoUrl + "/issues";
        public static string LatestReleaseApi => $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

        public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version;
        public static string CurrentText => CurrentVersion.ToString(3);
    }

    /// <summary>GitHub'da yayınlanmış bir DEVNANOTEK sürümü.</summary>
    public class AppRelease
    {
        public Version Version { get; set; }
        public string Tag { get; set; }
        public string PageUrl { get; set; }
        public string ExeUrl { get; set; }
        public DateTime? PublishedAt { get; set; }
        public string Text => "DEVNANOTEK " + Version.ToString(3);
    }

    /// <summary>
    /// Programın kendi güncellemesi: GitHub'daki son sürümü denetler; istenirse yeni exe'yi indirip
    /// bu program kapandıktan sonra çalıştırır (yeni exe kendini C:\devnanotek\app'e kopyalar). Servisler çalışmaya devam eder.
    /// </summary>
    public static class AppUpdater
    {
        /// <summary>Daha yeni bir sürüm varsa o; yoksa null.</summary>
        public static AppRelease Available { get; private set; }
        public static AppRelease Latest { get; private set; }
        public static DateTime? CheckedAt { get; private set; }

        public static async Task CheckAsync()
        {
            try
            {
                var json = await Downloader.GetStringAsync(AppInfo.LatestReleaseApi).ConfigureAwait(false);
                var root = JsonUtil.DeserializeObject(json) as Dictionary<string, object>;
                var tag = root != null && root.ContainsKey("tag_name") ? root["tag_name"]?.ToString() : null;
                var m = Regex.Match(tag ?? "", @"(\d+)\.(\d+)\.(\d+)");
                if (!m.Success) return;
                var rel = new AppRelease
                {
                    Version = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)),
                    Tag = tag,
                    PageUrl = root.ContainsKey("html_url") ? root["html_url"]?.ToString() : AppInfo.ReleasesUrl,
                };
                if (root.ContainsKey("published_at") && DateTime.TryParse(root["published_at"]?.ToString(), out var dt)) rel.PublishedAt = dt;
                var assets = root.ContainsKey("assets") ? root["assets"] as object[] : null;
                rel.ExeUrl = assets?.OfType<Dictionary<string, object>>()
                    .Where(a => string.Equals(a["name"]?.ToString(), "DevNanotek.exe", StringComparison.OrdinalIgnoreCase))
                    .Select(a => a["browser_download_url"]?.ToString()).FirstOrDefault();
                Latest = rel;
                var cur = AppInfo.CurrentVersion;
                Available = rel.ExeUrl != null && rel.Version > new Version(cur.Major, cur.Minor, Math.Max(0, cur.Build)) ? rel : null;
                CheckedAt = DateTime.Now;
                if (Available != null) Logger.Info("Yeni DEVNANOTEK sürümü var: " + Available.Version);
            }
            catch (Exception ex) { Logger.Warn("Program sürümü denetlenemedi: " + ex.Message); } // henüz sürüm yayınlanmamış olabilir (404)
        }

        /// <summary>Yeni exe'yi indirir ve doğrular. Dönüş: indirilen dosya.</summary>
        public static async Task<string> DownloadAsync(AppRelease rel, IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            var file = Path.Combine(Paths.Downloads, $"DevNanotek-{rel.Version.ToString(3)}.exe");
            try { if (File.Exists(file)) File.Delete(file); } catch { }
            await Downloader.DownloadAsync(new List<string> { rel.ExeUrl }, file, progress, ct);
            Version got;
            try { got = AssemblyName.GetAssemblyName(file).Version; }
            catch { throw new Exception("İndirilen dosya geçerli bir DEVNANOTEK programı değil."); }
            if (got < rel.Version) throw new Exception($"İndirilen dosyanın sürümü beklenenden eski ({got}).");
            return file;
        }

        /// <summary>
        /// Yeni exe'yi, bu süreç kapandıktan sonra başlatır (--wait-pid). Çağıran taraf ardından programı kapatmalıdır.
        /// </summary>
        public static void LaunchAfterExit(string exe)
        {
            var pid = Process.GetCurrentProcess().Id;
            Process.Start(new ProcessStartInfo(exe, "--wait-pid " + pid) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
            Logger.Info("Güncelleme başlatıldı: " + exe);
        }

        /// <summary>Güncellemede yeni exe, eski programın kapanmasını bekler (dosya kilidi kalksın).</summary>
        public static void WaitForPreviousInstance(string[] args)
        {
            int i = Array.IndexOf(args, "--wait-pid");
            if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out var pid)) return;
            try { using (var p = Process.GetProcessById(pid)) p.WaitForExit(30000); } catch { /* zaten kapanmış */ }
            Thread.Sleep(500);
        }
    }
}
