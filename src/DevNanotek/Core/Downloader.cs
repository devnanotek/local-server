using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DevNanotek.Core
{
    public class DownloadProgress
    {
        public long Received { get; set; }
        public long Total { get; set; }
        public string Status { get; set; } = "";
        public double Percent => Total > 0 ? Math.Min(100.0, Received * 100.0 / Total) : 0;
        public bool Indeterminate => Total <= 0;
    }

    /// <summary>HTTP indirme ve zip çıkarma yardımcıları.</summary>
    public static class Downloader
    {
        private static readonly Lazy<HttpClient> Http = new Lazy<HttpClient>(() =>
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; // + TLS 1.3
            ServicePointManager.DefaultConnectionLimit = 16;
            var h = new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            var c = new HttpClient(h) { Timeout = Timeout.InfiniteTimeSpan };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("DevNanotek/1.0 (Windows; +https://localhost)");
            c.DefaultRequestHeaders.Accept.ParseAdd("*/*");
            return c;
        });

        public static async Task<string> GetStringAsync(string url, int timeoutSec = 30)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec)))
            using (var resp = await Http.Value.GetAsync(url, cts.Token).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        /// <summary>HEAD ile boyut döndürür, erişilemiyorsa -1.</summary>
        public static async Task<long> HeadSizeAsync(string url, int timeoutSec = 20)
        {
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec)))
                using (var req = new HttpRequestMessage(HttpMethod.Head, url))
                using (var resp = await Http.Value.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode) return -1;
                    return resp.Content?.Headers?.ContentLength ?? 0;
                }
            }
            catch { return -1; }
        }

        /// <summary>
        /// URL listesini sırayla dener; başarılı olanı destFile'a yazar. downloads\ içinde aynı boyutta dosya varsa yeniden indirmez.
        /// </summary>
        public static async Task<string> DownloadAsync(IList<string> urls, string destFile, IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destFile));
            Exception last = null;
            foreach (var url in urls)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    progress?.Report(new DownloadProgress { Status = "Bağlanıyor: " + url });
                    using (var resp = await Http.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) { last = new Exception($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase} — {url}"); continue; }
                        var ctype = resp.Content.Headers.ContentType?.MediaType ?? "";
                        if (ctype.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                        {
                            last = new Exception("Sunucu dosya yerine HTML sayfa döndürdü — " + url);
                            continue;
                        }
                        long total = resp.Content.Headers.ContentLength ?? -1;

                        // önbellek: aynı boyutta dosya zaten varsa
                        if (File.Exists(destFile) && total > 0 && new FileInfo(destFile).Length == total)
                        {
                            progress?.Report(new DownloadProgress { Received = total, Total = total, Status = "Önceden indirilmiş dosya kullanılıyor: " + Path.GetFileName(destFile) });
                            return destFile;
                        }

                        var part = destFile + ".part";
                        using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                        {
                            var buf = new byte[1 << 16];
                            long recv = 0; int n;
                            var lastReport = DateTime.MinValue;
                            while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                            {
                                await dst.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                                recv += n;
                                if ((DateTime.Now - lastReport).TotalMilliseconds > 150)
                                {
                                    lastReport = DateTime.Now;
                                    progress?.Report(new DownloadProgress { Received = recv, Total = total, Status = $"İndiriliyor: {Path.GetFileName(destFile)}  {Fmt(recv)}{(total > 0 ? " / " + Fmt(total) : "")}" });
                                }
                            }
                            progress?.Report(new DownloadProgress { Received = recv, Total = total > 0 ? total : recv, Status = "İndirme tamamlandı: " + Path.GetFileName(destFile) });
                        }
                        if (File.Exists(destFile)) File.Delete(destFile);
                        File.Move(part, destFile);
                        return destFile;
                    }
                }
                catch (OperationCanceledException) { try { File.Delete(destFile + ".part"); } catch { } throw; }
                catch (Exception ex)
                {
                    last = ex;
                    Logger.Warn("İndirme başarısız: " + url + " -> " + ex.Message);
                }
            }
            throw new Exception("Hiçbir kaynaktan indirilemedi. " + (last?.Message ?? ""), last);
        }

        public static string Fmt(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.00") + " GB";
        }

        /// <summary>
        /// Zip'i geçici klasöre açar, "marker" dosyasını (ör. bin\httpd.exe) içeren klasörü bulur ve destDir'e taşır.
        /// destDir varsa önce .old olarak kenara alınır, başarı sonrası silinir.
        /// </summary>
        /// <param name="skip">Atlanacak girdiler (ör. PostgreSQL paketindeki pgAdmin 4) — zip içi göreli yol alır.</param>
        public static void ExtractZip(string zipFile, string destDir, IList<string> markers, IProgress<DownloadProgress> progress, CancellationToken ct, Func<string, bool> skip = null)
        {
            var tmp = Path.Combine(Paths.Tmp, "extract-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            try
            {
                using (var za = ZipFile.OpenRead(zipFile))
                {
                    long total = za.Entries.Where(e => skip == null || !skip(e.FullName.Replace('/', '\\'))).Sum(e => e.Length), done = 0; int count = 0;
                    var lastReport = DateTime.MinValue;
                    foreach (var entry in za.Entries)
                    {
                        ct.ThrowIfCancellationRequested();
                        var rel = entry.FullName.Replace('/', '\\');
                        if (rel.Contains("..")) continue; // zip-slip koruması
                        if (skip != null && skip(rel)) continue;
                        var target = Path.Combine(tmp, rel);
                        if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        entry.ExtractToFile(target, true);
                        done += entry.Length; count++;
                        if ((DateTime.Now - lastReport).TotalMilliseconds > 200)
                        {
                            lastReport = DateTime.Now;
                            progress?.Report(new DownloadProgress { Received = done, Total = total, Status = $"Çıkarılıyor: {Path.GetFileName(zipFile)} ({count} dosya)" });
                        }
                    }
                }

                var contentRoot = FindContentRoot(tmp, markers, 3);
                if (contentRoot == null) throw new Exception($"Zip içinde beklenen dosya bulunamadı: {string.Join(" | ", markers)}");

                progress?.Report(new DownloadProgress { Status = "Yerleştiriliyor: " + destDir, Received = 1, Total = 1 });
                string old = null;
                if (Directory.Exists(destDir))
                {
                    old = destDir + ".old-" + DateTime.Now.ToString("HHmmss");
                    try { Directory.Move(destDir, old); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        throw new Exception($"{destDir} klasörü kullanımda olduğu için değiştirilemedi. Bu klasörden çalışan bir program " +
                                            "(açık terminal, VS Code, çalışan servis, node süreci) olabilir; kapatıp tekrar deneyin.", ex);
                    }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destDir));
                Directory.Move(contentRoot, destDir);
                if (old != null) TryDeleteDir(old);
            }
            finally
            {
                TryDeleteDir(tmp);
            }
        }

        private static string FindContentRoot(string dir, IList<string> markers, int depth)
        {
            foreach (var m in markers)
                if (File.Exists(Path.Combine(dir, m))) return dir;
            if (depth <= 0) return null;
            foreach (var sub in Directory.GetDirectories(dir))
            {
                var r = FindContentRoot(sub, markers, depth - 1);
                if (r != null) return r;
            }
            return null;
        }

        public static void TryDeleteDir(string dir)
        {
            if (!Directory.Exists(dir)) return;
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                        try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                    Directory.Delete(dir, true);
                    return;
                }
                catch { Thread.Sleep(300); }
            }
            Logger.Warn("Klasör silinemedi (elle silinebilir): " + dir);
        }
    }
}
