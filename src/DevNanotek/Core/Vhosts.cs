using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DevNanotek.Core
{
    /// <summary>Sanal host listesi: otomatik (httpdocs üst klasörleri) + elle eklenenler.</summary>
    public static class Vhosts
    {
        public static List<VhostEntry> Compute(AppConfig cfg)
        {
            var list = new List<VhostEntry>();
            var tld = cfg.VhostTld;
            if (cfg.AutoVirtualHosts && Directory.Exists(cfg.EffectiveDocRoot))
            {
                foreach (var d in Directory.GetDirectories(cfg.EffectiveDocRoot))
                {
                    var name = Path.GetFileName(d);
                    if (name.StartsWith(".") || name.StartsWith("_")) continue;
                    var slug = Slug(name);
                    if (string.IsNullOrEmpty(slug)) continue;
                    list.Add(new VhostEntry { Host = slug + "." + tld, DocRoot = ResolveDocRoot(d) });
                }
            }
            foreach (var v in cfg.Vhosts)
            {
                if (v == null || string.IsNullOrWhiteSpace(v.Host)) continue;
                var host = v.Host.Trim().ToLowerInvariant();
                list.RemoveAll(x => x.Host == host);
                list.Add(new VhostEntry { Host = host, DocRoot = v.DocRoot });
            }
            list.RemoveAll(x => x.Host == "localhost" || !Directory.Exists(x.DocRoot));
            return list.OrderBy(x => x.Host).ToList();
        }

        public static IEnumerable<string> HostNames(AppConfig cfg) => Compute(cfg).Select(v => v.Host);

        /// <summary>public\index.php varsa ve kökte index yoksa belge kökü public olur (Laravel vb.).</summary>
        public static string ResolveDocRoot(string dir)
        {
            bool hasIndex = File.Exists(Path.Combine(dir, "index.php")) || File.Exists(Path.Combine(dir, "index.html"));
            if (!hasIndex && File.Exists(Path.Combine(dir, "public", "index.php"))) return Path.Combine(dir, "public");
            return dir;
        }

        public static string Slug(string name)
        {
            var map = new Dictionary<char, string> { ['ç'] = "c", ['Ç'] = "c", ['ğ'] = "g", ['Ğ'] = "g", ['ı'] = "i", ['I'] = "i", ['İ'] = "i", ['ö'] = "o", ['Ö'] = "o", ['ş'] = "s", ['Ş'] = "s", ['ü'] = "u", ['Ü'] = "u" };
            var sb = new StringBuilder();
            foreach (var ch in name)
            {
                if (map.TryGetValue(ch, out var r)) sb.Append(r);
                else sb.Append(char.ToLowerInvariant(ch));
            }
            var s = Regex.Replace(sb.ToString(), @"[^a-z0-9\-]+", "-").Trim('-');
            return s;
        }

        /// <summary>Bir proje klasörü için önerilen host adı (klasör adından).</summary>
        public static string SuggestHost(string dir, AppConfig cfg) => Slug(Path.GetFileName(dir.TrimEnd('\\'))) + "." + cfg.VhostTld;
    }
}
