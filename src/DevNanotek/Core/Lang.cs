using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace DevNanotek.Core
{
    /// <summary>
    /// Arayüz dili (Türkçe / İngilizce).
    /// Metinler kaynakta Türkçe yazılır. İngilizce seçiliyse ekrana, iletişim kutularına ve günlüğe giden her metin
    /// L.T() ile sözlükten çevrilir (Core/Lang/En.*.cs). Sözlük anahtarı Türkçe metnin kendisidir.
    /// {0}, {1}… içeren anahtarlar kalıptır: "PHP {0} kuruldu." anahtarı "PHP 8.4.26 kuruldu." metnini de çevirir;
    /// yer tutuculara gelen parçalar da ayrıca çevrilir.
    /// Ayar: "" = cihazın dili (Windows görüntü dili Türkçe ise Türkçe, değilse İngilizce), "tr", "en".
    /// </summary>
    public static partial class L
    {
        public const string Turkish = "tr";
        public const string English = "en";

        /// <summary>Etkin dil: "tr" veya "en".</summary>
        public static string Code { get; private set; } = Turkish;
        public static bool En => Code == English;

        /// <summary>Windows görüntü dili Türkçe ise "tr", başka her dilde "en".</summary>
        public static string DeviceLanguage
        {
            get
            {
                try { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase) ? Turkish : English; }
                catch { return English; }
            }
        }

        /// <summary>Ayar değerini ("", "tr", "en") etkin dile çevirir.</summary>
        public static string Resolve(string setting) => setting == Turkish || setting == English ? setting : DeviceLanguage;

        public static void Init(string setting) => Code = Resolve(setting);

        /// <summary>Türkçe metni etkin dile çevirir. Türkçe seçiliyse veya karşılığı yoksa metni olduğu gibi döndürür.</summary>
        public static string T(string s) => En ? Translate(s, 0) : s;

        /// <summary>string.Format + çeviri: L.F("{0} kuruldu.", ad)</summary>
        public static string F(string format, params object[] args) => string.Format(T(format), args);

        /// <summary>Dile göre seçim: L.Pick("Türkçe", "English")</summary>
        public static string Pick(string tr, string en) => En ? en : tr;

        // ------------------------------------------------------------------
        //  Sözlük
        // ------------------------------------------------------------------
        private sealed class Pattern
        {
            public Regex Rx;
            public string Target;
            public string Hint;     // kalıptaki en uzun sabit parça: düzenli ifadeden önce hızlı eleme
            public int Weight;      // sabit metin uzunluğu: önce en belirgin kalıp denenir
            public int[] Slots;     // yakalama grubu → yer tutucu numarası
        }

        private static readonly object BuildLock = new object();
        private static Dictionary<string, string> _exact;
        private static Dictionary<string, string> _norm;
        private static List<Pattern> _patterns;
        private static readonly Regex Slot = new Regex(@"\{(\d+)\}", RegexOptions.CultureInvariant);
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.CultureInvariant);

        private static void EnsureBuilt()
        {
            if (_exact != null) return;
            lock (BuildLock)
            {
                if (_exact != null) return;
                var exact = new Dictionary<string, string>(StringComparer.Ordinal);
                var norm = new Dictionary<string, string>(StringComparer.Ordinal);
                var patterns = new List<Pattern>();
                // Core/Lang/En.*.cs dosyalarındaki "En" ile başlayan string[] alanları: { "türkçe", "english", … }
                foreach (var f in typeof(L).GetFields(BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (f.FieldType != typeof(string[]) || !f.Name.StartsWith("En", StringComparison.Ordinal)) continue;
                    var arr = (string[])f.GetValue(null);
                    for (int i = 0; i + 1 < arr.Length; i += 2)
                    {
                        var tr = arr[i]; var en = arr[i + 1];
                        if (string.IsNullOrEmpty(tr) || en == null) continue;
                        exact[tr] = en;
                        norm[Normalize(tr)] = en;
                        if (Slot.IsMatch(tr)) { var p = MakePattern(tr, en); if (p != null) patterns.Add(p); }
                    }
                }
                _patterns = patterns.OrderByDescending(p => p.Weight).ToList();
                _norm = norm;
                _exact = exact;
            }
        }

        private static Pattern MakePattern(string tr, string en)
        {
            var parts = Slot.Split(tr); // sabit, numara, sabit, numara, …
            var rx = new StringBuilder("^");
            var slots = new List<int>();
            string hint = "";
            int weight = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 0)
                {
                    rx.Append(Regex.Escape(parts[i]));
                    weight += parts[i].Trim().Length;
                    if (parts[i].Length > hint.Length) hint = parts[i];
                }
                else
                {
                    rx.Append("(.*?)"); // boş değer de olabilir (ör. bilinmeyen yol)
                    slots.Add(int.Parse(parts[i], CultureInfo.InvariantCulture));
                }
            }
            if (weight < 2) return null; // "{0} {1}" gibi yalnız yer tutucudan oluşan kalıplar her şeyi yakalar
            rx.Append("$");
            return new Pattern
            {
                Rx = new Regex(rx.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant),
                Target = en,
                Hint = hint,
                Weight = weight,
                Slots = slots.ToArray()
            };
        }

        private static string Normalize(string s) => Spaces.Replace(s.Trim(), " ");

        // ------------------------------------------------------------------
        //  Çeviri
        // ------------------------------------------------------------------
        private static string Translate(string s, int depth)
        {
            if (string.IsNullOrEmpty(s)) return s;
            EnsureBuilt();
            if (_exact.TryGetValue(s, out var hit)) return hit;

            // baştaki / sondaki boşluklar korunur
            int a = 0, b = s.Length;
            while (a < b && char.IsWhiteSpace(s[a])) a++;
            while (b > a && char.IsWhiteSpace(s[b - 1])) b--;
            if (a == b) return s;
            var core = s.Substring(a, b - a);
            var res = TranslateCore(core, depth);
            if (res == null) { NoteMissing(core, depth); return s; }
            return a == 0 && b == s.Length ? res : s.Substring(0, a) + res + s.Substring(b);
        }

        private static readonly string[] Prefixes = { "• ", "- ", "✔ ", "✖ ", "⚠ ", "▶ ", "■ ", "→ ", "· ", "— " };
        private static readonly string[] Suffixes = { "…", "...", ":", ".", " —" };

        private static string TranslateCore(string c, int depth)
        {
            if (_exact.TryGetValue(c, out var hit)) return hit;
            if (c.Length > 6000) return null; // uzun metinler (dosya içeriği, yığın izi) çevrilmez
            if (_norm.TryGetValue(Normalize(c), out hit)) return hit;

            // kalıplar: "PHP {0} kuruldu."
            if (depth < 4)
            {
                foreach (var p in _patterns)
                {
                    if (p.Hint.Length > 0 && c.IndexOf(p.Hint, StringComparison.Ordinal) < 0) continue;
                    var m = p.Rx.Match(c);
                    if (!m.Success) continue;
                    var r = p.Target;
                    for (int g = 0; g < p.Slots.Length; g++)
                        r = r.Replace("{" + p.Slots[g] + "}", Translate(m.Groups[g + 1].Value, depth + 1));
                    return r;
                }
            }

            // çok satırlı metin: satır satır
            if (c.IndexOf('\n') >= 0)
            {
                var lines = c.Split('\n');
                bool any = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    bool cr = line.EndsWith("\r");
                    if (cr) line = line.Substring(0, line.Length - 1);
                    var t = Translate(line, depth);
                    if (!ReferenceEquals(t, line) && t != line) any = true;
                    lines[i] = cr ? t + "\r" : t;
                }
                return any ? string.Join("\n", lines) : null;
            }

            // madde işaretleri ve sembollerle başlayan metin
            foreach (var pre in Prefixes)
                if (c.Length > pre.Length && c.StartsWith(pre, StringComparison.Ordinal))
                {
                    var rest = TranslateCore(c.Substring(pre.Length), depth);
                    return rest == null ? null : pre + rest;
                }

            // sondaki noktalama: "Kur…", "Sürüm:" → "Kur" / "Sürüm" anahtarı
            foreach (var suf in Suffixes)
                if (c.Length > suf.Length + 1 && c.EndsWith(suf, StringComparison.Ordinal))
                {
                    var head = c.Substring(0, c.Length - suf.Length);
                    if (_exact.TryGetValue(head, out hit)) return hit + suf;
                }
            return null;
        }

        // ------------------------------------------------------------------
        //  Eksik çeviri listesi (geliştirme): DN_I18N_MISSING=dosya yolu
        // ------------------------------------------------------------------
        private static readonly string MissingFile = Environment.GetEnvironmentVariable("DN_I18N_MISSING");
        private static readonly HashSet<string> Missing = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Regex Turkishish = new Regex(@"[çğıöşüÇĞİÖŞÜ]|\b(ve|bir|için|ile|yok|var|değil|olarak|sürüm|kur|aç|seç)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static void NoteMissing(string s, int depth)
        {
            if (MissingFile == null || !Turkishish.IsMatch(s)) return;
            lock (Missing)
            {
                if (!Missing.Add(s)) return;
                try { File.AppendAllText(MissingFile, s.Replace("\r", "").Replace("\n", "\\n") + Environment.NewLine, new UTF8Encoding(false)); } catch { }
            }
        }

        /// <summary>Test aracı: sözlükteki tüm anahtarlar ve karşılıkları.</summary>
        public static IEnumerable<KeyValuePair<string, string>> AllEntries()
        {
            EnsureBuilt();
            return _exact;
        }
    }
}
