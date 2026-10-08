using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace DevNanotek.Core
{
    /// <summary>
    /// hosts dosyasında yalnızca kendi bloğumuzu yönetiriz:
    ///   # >>> DevNanotek BEGIN
    ///   127.0.0.1 proje.test
    ///   # <<< DevNanotek END
    /// </summary>
    public static class HostsFile
    {
        private const string Begin = "# >>> DevNanotek BEGIN (bu blok otomatik yönetilir)";
        private const string End = "# <<< DevNanotek END";

        public static List<string> GetManagedHosts()
        {
            var list = new List<string>();
            try
            {
                if (!File.Exists(Paths.HostsFile)) return list;
                bool inside = false;
                foreach (var raw in File.ReadAllLines(Paths.HostsFile))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("# >>> DevNanotek BEGIN")) { inside = true; continue; }
                    if (line.StartsWith(End)) { inside = false; continue; }
                    if (!inside || line.StartsWith("#") || line.Length == 0) continue;
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && !list.Contains(parts[1])) list.Add(parts[1]);
                }
            }
            catch (Exception ex) { Logger.Warn("hosts okunamadı: " + ex.Message); }
            return list;
        }

        public static bool SetManagedHosts(IEnumerable<string> hosts)
        {
            var wanted = hosts.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim().ToLowerInvariant()).Distinct().OrderBy(h => h).ToList();
            try
            {
                var lines = File.Exists(Paths.HostsFile) ? File.ReadAllLines(Paths.HostsFile).ToList() : new List<string>();
                // eski bloğu çıkar
                var outLines = new List<string>();
                bool inside = false;
                foreach (var l in lines)
                {
                    var t = l.Trim();
                    if (t.StartsWith("# >>> DevNanotek BEGIN")) { inside = true; continue; }
                    if (t.StartsWith(End)) { inside = false; continue; }
                    if (!inside) outLines.Add(l);
                }
                while (outLines.Count > 0 && string.IsNullOrWhiteSpace(outLines[outLines.Count - 1])) outLines.RemoveAt(outLines.Count - 1);

                if (wanted.Count > 0)
                {
                    outLines.Add("");
                    outLines.Add(Begin);
                    foreach (var h in wanted)
                    {
                        outLines.Add("127.0.0.1\t" + h);
                        outLines.Add("::1\t\t" + h);
                    }
                    outLines.Add(End);
                }
                outLines.Add("");

                var current = File.Exists(Paths.HostsFile) ? File.ReadAllText(Paths.HostsFile) : "";
                var text = string.Join(Environment.NewLine, outLines);
                if (current.Replace("\r\n", "\n") == text.Replace("\r\n", "\n")) return true; // değişiklik yok

                // salt-okunur özniteliğini kaldır, yeniden dene
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        if (File.Exists(Paths.HostsFile)) File.SetAttributes(Paths.HostsFile, FileAttributes.Normal);
                        File.WriteAllText(Paths.HostsFile, text, new UTF8Encoding(false));
                        break;
                    }
                    catch (IOException) when (i < 4) { Thread.Sleep(300); }
                }
                ProcessRunner.Run("ipconfig.exe", "/flushdns", null, 15000);
                Logger.Info("hosts güncellendi: " + string.Join(", ", wanted));
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("hosts yazılamadı", ex);
                return false;
            }
        }

        public static bool Add(string host)
        {
            var l = GetManagedHosts();
            if (!l.Contains(host, StringComparer.OrdinalIgnoreCase)) l.Add(host);
            return SetManagedHosts(l);
        }

        public static bool Remove(string host)
        {
            var l = GetManagedHosts();
            l.RemoveAll(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase));
            return SetManagedHosts(l);
        }
    }
}
