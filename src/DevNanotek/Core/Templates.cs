using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DevNanotek.Core
{
    /// <summary>Gömülü şablon dosyalarını (Resources\*) okur ve {{TOKEN}} değerlerini yerleştirir.</summary>
    public static class Templates
    {
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string Load(string name)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(name, out var cached)) return cached;
                var asm = Assembly.GetExecutingAssembly();
                var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + name, StringComparison.OrdinalIgnoreCase));
                if (res == null) throw new FileNotFoundException("Gömülü şablon bulunamadı: " + name);
                using (var s = asm.GetManifestResourceStream(res))
                using (var r = new StreamReader(s, Encoding.UTF8))
                {
                    var txt = r.ReadToEnd();
                    Cache[name] = txt;
                    return txt;
                }
            }
        }

        public static string Render(string templateName, IDictionary<string, string> tokens)
        {
            var txt = Load(templateName);
            foreach (var kv in tokens) txt = txt.Replace("{{" + kv.Key + "}}", kv.Value ?? "");
            return txt;
        }

        /// <summary>Dosyayı yalnızca içerik değiştiyse yazar; döndürdüğü değer "değişti mi".</summary>
        public static bool WriteIfChanged(string path, string content, Encoding enc = null)
        {
            enc = enc ?? new UTF8Encoding(false);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (File.Exists(path))
                {
                    var cur = File.ReadAllText(path, enc);
                    if (cur == content) return false;
                }
                File.WriteAllText(path, content, enc);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Dosya yazılamadı: " + path, ex);
                throw;
            }
        }

        public static void EnsureFile(string path, string defaultContent)
        {
            if (File.Exists(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, defaultContent, new UTF8Encoding(false));
        }
    }
}
