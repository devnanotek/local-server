// Çeviri denetimi: DevNanotek kaynaklarındaki Türkçe metinleri çıkarır ve İngilizce sözlükle (Core/Lang/En*.cs) karşılaştırır.
//   dotnet run --project tools/i18n-check -- check src/DevNanotek tools/i18n-check/ignore.txt
//   extract <src> <out>            → tüm aday metinler (dosya:satır, tür, C# kaçışlı metin)
//   check   <src> <ignore> [<out>] → sözlükte olmayan metinler + sözlük sorunları; eksik varsa çıkış kodu 1
// Yeni bir Türkçe metin eklediyseniz İngilizcesini Core/Lang/En.*.cs dosyasına ekleyin; bilerek çevrilmeyen
// metinleri (yapılandırma içeriği, işaretçiler) ignore.txt dosyasına yazın.
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

static class Tool
{
    static int Main(string[] a)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var src = a[1];
        var items = Extract(src);
        if (a[0] == "extract")
        {
            File.WriteAllLines(a[2], items.Select(i => $"{i.File}:{i.Line}\t{i.Kind}\t{Esc(i.Text)}"), new UTF8Encoding(false));
            Console.WriteLine($"{items.Count} aday metin, {items.Select(i => i.Text).Distinct().Count()} farklı");
            return 0;
        }
        if (a[0] == "check")
        {
            var ignore = File.Exists(a[2]) ? File.ReadAllLines(a[2]).Where(l => l.Length > 0 && !l.StartsWith("#")).Select(Unesc).ToHashSet() : new HashSet<string>();
            var dict = LoadDict(src, out var problems);
            var norm = dict.Keys.GroupBy(Norm).ToDictionary(g => g.Key, g => g.First());
            var missing = new List<Item>();
            // sözlükteki anahtarların sabit parçaları ({0} dışında kalan metin): bir dize parçası bunlardan birinin içindeyse kalıpla karşılanmıştır
            var keyText = string.Join("\u0001", dict.Keys.Select(k => Regex.Replace(k, @"\{\d+\}", "\u0001")));
            var turkishWord = new Regex(@"[çğıöşüÇĞİÖŞÜ]|\b(ve|bir|ile|yok|var|kur|kurulu|durdur|kapat|gece|sistem|yenile|temizle|eski|aktif|tema|portlar|bitti|denetle|sil|ekle|kaydet|yedek|veri|sayfa|servis|servisi|sunucu|durum|adres|hata|olabilir|yeni|daha|bu|veya|ama|mi|bilgi|uyari|klasor|dosya|surum|program|ayar|ayarlar|baslat)\b", RegexOptions.IgnoreCase);
            foreach (var i in items)
            {
                var t = i.Text.Trim();
                if (ignore.Contains(i.Text) || ignore.Contains(t)) continue;
                if (dict.ContainsKey(i.Text) || dict.ContainsKey(t) || norm.ContainsKey(Norm(i.Text))) continue;
                if (!i.Kind.StartsWith("xaml") && !turkishWord.IsMatch(Regex.Replace(t, @"\{\d+\}", ""))) continue;              // Türkçe olmayan (teknik) metin
                if (i.Kind != "concat" && t.Length >= 3 && keyText.Contains(t)) continue;        // daha büyük bir kalıbın parçası
                if (i.Kind == "concat")
                {
                    // zincir kalıbın sabit parçalarının hepsi sözlükte geçiyorsa (ör. satır satır anahtarlar) karşılanmış say
                    var parts = Regex.Split(i.Text, @"\{\d+\}|\r?\n").Select(p => p.Trim()).Where(p => p.Length >= 3 && turkishWord.IsMatch(p)).ToList();
                    if (parts.Count > 0 && parts.All(p => keyText.Contains(p) || dict.ContainsKey(p))) continue;
                }
                missing.Add(i);
            }
            var lines = new List<string>();
            lines.Add($"# sözlük: {dict.Count} kayıt · eksik: {missing.Select(m => m.Text).Distinct().Count()} farklı metin · sorun: {problems.Count}");
            lines.AddRange(problems.Select(p => "! " + p));
            foreach (var g in missing.GroupBy(m => m.File))
            {
                lines.Add("");
                lines.Add("## " + g.Key);
                foreach (var m in g.GroupBy(x => x.Text)) lines.Add($"{m.First().Line}\t{m.First().Kind}\t{Esc(m.Key)}");
            }
            if (a.Length > 3) File.WriteAllLines(a[3], lines, new UTF8Encoding(false));
            foreach (var l in lines) Console.WriteLine(l);
            return missing.Count > 0 || problems.Count > 0 ? 1 : 0;
        }
        return 1;
    }

    record Item(string File, int Line, string Kind, string Text);

    static string Norm(string s) => Regex.Replace(s.Trim(), @"\s+", " ");

    // ------------------------------------------------------------------
    static List<Item> Extract(string src)
    {
        var list = new List<Item>();
        foreach (var f in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, f).Replace('\\', '/');
            if (rel.StartsWith("obj/") || rel.StartsWith("bin/") || rel.StartsWith("Core/Lang/")) continue;
            if (f.EndsWith(".cs")) list.AddRange(FromCs(rel, File.ReadAllText(f)));
            else if (f.EndsWith(".xaml")) list.AddRange(FromXaml(rel, File.ReadAllText(f)));
        }
        return list.Where(i => IsText(i.Text, i.Kind.StartsWith("xaml"))).ToList();
    }

    static readonly Regex TurkishChar = new(@"[çğıöşüÇĞİÖŞÜ]");

    static bool IsText(string s, bool xaml)
    {
        var t = s.Trim();
        if (t.Length < 2 || !t.Any(char.IsLetter)) return false;
        if (TurkishChar.IsMatch(t)) return true;
        if (Regex.IsMatch(t, @"^(https?|pack|file)://")) return false;
        var noHoles = Regex.Replace(t, @"\{\d+\}", "");
        if (!noHoles.Any(char.IsLetter)) return false;
        if (!t.Contains(' '))
        {
            if (t.Contains('\\') || t.Contains('/') || t.Contains('.') || t.Contains('_') || t.Contains('=') || t.Contains('*') || t.Contains('%') || t.Contains('$')) return false;
            if (Regex.IsMatch(t, @"^[a-z0-9\-:]+$")) return false;      // anahtar, servis, süreç adları
            if (!xaml && Regex.IsMatch(t, @"^[A-Z0-9_\-]+$") && t.Length <= 5) return false; // PHP, SSL, HTTP…
        }
        return true;
    }

    // ------------------------------------------------------------------
    //  C#
    // ------------------------------------------------------------------
    enum K { Str, Plus, Open, Close, Term, Other }
    record Tok(K Kind, string Text, int Line, int Holes);

    static IEnumerable<Item> FromCs(string file, string code)
    {
        var toks = Lex(code);
        foreach (var t in toks.Where(t => t.Kind == K.Str))
            yield return new Item(file, t.Line, t.Holes > 0 ? "interp" : "str", t.Text);
        foreach (var it in Chains(file, toks)) yield return it;
    }

    static List<Tok> Lex(string c)
    {
        var toks = new List<Tok>();
        int i = 0, line = 1;
        void NL(string s) { foreach (var ch in s) if (ch == '\n') line++; }
        while (i < c.Length)
        {
            char ch = c[i];
            if (ch == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(ch)) { i++; continue; }
            if (ch == '/' && i + 1 < c.Length && c[i + 1] == '/') { while (i < c.Length && c[i] != '\n') i++; continue; }
            if (ch == '/' && i + 1 < c.Length && c[i + 1] == '*') { int e = c.IndexOf("*/", i + 2); if (e < 0) e = c.Length - 2; NL(c.Substring(i, e + 2 - i)); i = e + 2; continue; }
            if (ch == '#' && (i == 0 || c[i - 1] == '\n' || string.IsNullOrWhiteSpace(c.Substring(c.LastIndexOf('\n', i - 1) + 1, i - c.LastIndexOf('\n', i - 1) - 1)))) { while (i < c.Length && c[i] != '\n') i++; continue; }
            if (ch == '\'') { i++; while (i < c.Length && c[i] != '\'') { if (c[i] == '\\') i++; i++; } i++; toks.Add(new Tok(K.Other, "'c'", line, 0)); continue; }
            bool interp = false, verbatim = false; int start = i;
            if (ch == '$' || ch == '@')
            {
                int j = i;
                while (j < c.Length && (c[j] == '$' || c[j] == '@')) { if (c[j] == '$') interp = true; else verbatim = true; j++; }
                if (j < c.Length && c[j] == '"') i = j; else { interp = verbatim = false; }
            }
            if (c[i] == '"')
            {
                int l0 = line;
                var (text, holes, end) = ReadString(c, i + 1, interp, verbatim);
                NL(c.Substring(start, end - start));
                toks.Add(new Tok(K.Str, text, l0, holes));
                i = end;
                continue;
            }
            if (ch == '+') { if (i + 1 < c.Length && (c[i + 1] == '=' || c[i + 1] == '+')) { toks.Add(new Tok(K.Term, "+=", line, 0)); i += 2; } else { toks.Add(new Tok(K.Plus, "+", line, 0)); i++; } continue; }
            if (ch == '(' || ch == '[') { toks.Add(new Tok(K.Open, ch.ToString(), line, 0)); i++; continue; }
            if (ch == ')' || ch == ']') { toks.Add(new Tok(K.Close, ch.ToString(), line, 0)); i++; continue; }
            if (ch == ',' || ch == ';' || ch == '{' || ch == '}' || ch == '?' || ch == ':') { toks.Add(new Tok(K.Term, ch.ToString(), line, 0)); i++; continue; }
            if (ch == '=' || ch == '&' || ch == '|' || ch == '!' || ch == '<' || ch == '>')
            {
                int j = i; while (j < c.Length && "=&|!<>".IndexOf(c[j]) >= 0) j++;
                var op = c.Substring(i, j - i);
                toks.Add(new Tok(op == "!" ? K.Other : K.Term, op, line, 0)); i = j; continue;
            }
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.')
            {
                int j = i; while (j < c.Length && (char.IsLetterOrDigit(c[j]) || c[j] == '_' || c[j] == '.')) j++;
                var w = c.Substring(i, j - i);
                toks.Add(new Tok(w == "return" || w == "new" ? K.Term : K.Other, w, line, 0)); i = j; continue;
            }
            toks.Add(new Tok(K.Other, ch.ToString(), line, 0)); i++;
        }
        return toks;
    }

    // dize içeriği → çalışma zamanı metni; enterpolasyon delikleri {0},{1}…
    static (string, int, int) ReadString(string c, int i, bool interp, bool verbatim)
    {
        var sb = new StringBuilder(); int holes = 0;
        while (i < c.Length)
        {
            char ch = c[i];
            if (ch == '"')
            {
                if (verbatim && i + 1 < c.Length && c[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                return (sb.ToString(), holes, i + 1);
            }
            if (!verbatim && ch == '\\')
            {
                char n = c[i + 1]; i += 2;
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '0': sb.Append('\0'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(c.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(n); break;
                }
                continue;
            }
            if (interp && ch == '{')
            {
                if (c[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
                // deliği atla (iç içe parantez ve dizeleri say)
                int depth = 0; i++;
                while (i < c.Length)
                {
                    char h = c[i];
                    if (h == '"') { var (_, _, e) = ReadString(c, i + 1, false, false); i = e; continue; }
                    if (h == '$' && i + 1 < c.Length && c[i + 1] == '"') { var (_, _, e) = ReadString(c, i + 2, true, false); i = e; continue; }
                    if (h == '\'') { i++; while (c[i] != '\'') { if (c[i] == '\\') i++; i++; } i++; continue; }
                    if (h == '(' || h == '[' || h == '{') depth++;
                    else if (h == ')' || h == ']') depth--;
                    else if (h == '}') { if (depth == 0) break; depth--; }
                    i++;
                }
                sb.Append('{').Append(holes++).Append('}');
                i++;
                continue;
            }
            if (interp && ch == '}' && i + 1 < c.Length && c[i + 1] == '}') { sb.Append('}'); i += 2; continue; }
            sb.Append(ch); i++;
        }
        return (sb.ToString(), holes, i);
    }

    // "a" + x + "b"  →  "a{0}b"
    static IEnumerable<Item> Chains(string file, List<Tok> toks)
    {
        var res = new List<Item>();
        // her parantez derinliği için açık zincir
        var stack = new Stack<List<List<Tok>>>();
        var ops = new List<List<Tok>> { new List<Tok>() };
        void Flush(List<List<Tok>> o)
        {
            var operands = o.Where(x => x.Count > 0).ToList();
            if (operands.Count < 2 || !operands.Any(x => x.Count == 1 && x[0].Kind == K.Str)) return;
            if (operands.Count != o.Count) return; // boş işlenen: tekli + vb.
            var sb = new StringBuilder(); int n = 0; int line = operands.First(x => x.Count == 1 && x[0].Kind == K.Str)[0].Line;
            foreach (var x in operands)
            {
                if (x.Count == 1 && x[0].Kind == K.Str)
                {
                    var t = x[0].Text;
                    if (x[0].Holes > 0) { int baseN = n; t = Regex.Replace(t, @"\{(\d+)\}", m => "{" + (baseN + int.Parse(m.Groups[1].Value)) + "}"); n += x[0].Holes; }
                    sb.Append(t);
                }
                else sb.Append('{').Append(n++).Append('}');
            }
            res.Add(new Item(file, line, "concat", sb.ToString()));
        }
        foreach (var t in toks)
        {
            switch (t.Kind)
            {
                case K.Open:
                    ops[^1].Add(t);
                    stack.Push(ops);
                    ops = new List<List<Tok>> { new List<Tok>() };
                    break;
                case K.Close:
                    Flush(ops);
                    ops = stack.Count > 0 ? stack.Pop() : new List<List<Tok>> { new List<Tok>() };
                    ops[^1].Add(t);
                    break;
                case K.Plus:
                    ops.Add(new List<Tok>());
                    break;
                case K.Term:
                    Flush(ops);
                    ops = new List<List<Tok>> { new List<Tok>() };
                    break;
                default:
                    ops[^1].Add(t);
                    break;
            }
        }
        return res;
    }

    // ------------------------------------------------------------------
    //  XAML
    // ------------------------------------------------------------------
    static readonly HashSet<string> TextAttrs = new() { "Text", "Content", "Header", "ToolTip", "Title", "Description", "Tag2" };
    static readonly HashSet<string> InlineHosts = new() { "TextBlock", "Run", "Bold", "Italic", "Span", "Hyperlink", "Underline", "Label", "Button", "CheckBox", "RadioButton", "ComboBoxItem", "ListBoxItem", "ToolTip" };

    static IEnumerable<Item> FromXaml(string file, string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        using (var r = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { IgnoreComments = true }))
        {
            var lineInfo = (IXmlLineInfo)r;
            var list = new List<Item>();
            var stack = new Stack<string>();
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element)
                {
                    var name = r.LocalName;
                    if (r.HasAttributes)
                        while (r.MoveToNextAttribute())
                        {
                            var an = r.LocalName;
                            if (!TextAttrs.Contains(an)) continue;
                            var v = r.Value;
                            if (v.StartsWith("{}")) v = v.Substring(2); else if (v.StartsWith("{")) continue;
                            list.Add(new Item(file, lineInfo.LineNumber, "xaml-" + an, v));
                        }
                    r.MoveToElement();
                    if (!r.IsEmptyElement) stack.Push(name);
                }
                else if (r.NodeType == XmlNodeType.EndElement) { if (stack.Count > 0) stack.Pop(); }
                else if (r.NodeType == XmlNodeType.Text)
                {
                    var host = stack.Count > 0 ? stack.Peek() : "";
                    if (InlineHosts.Contains(host)) list.Add(new Item(file, lineInfo.LineNumber, "xaml-text", Norm(r.Value)));
                }
            }
            return list;
        }
    }

    // ------------------------------------------------------------------
    //  Sözlük: Core/Lang/En*.cs → string[] çiftleri
    // ------------------------------------------------------------------
    static Dictionary<string, string> LoadDict(string src, out List<string> problems)
    {
        problems = new List<string>();
        var d = new Dictionary<string, string>();
        var dir = Path.Combine(src, "Core", "Lang");
        if (!Directory.Exists(dir)) return d;
        foreach (var f in Directory.GetFiles(dir, "En*.cs"))
        {
            var toks = Lex(File.ReadAllText(f));
            // dizi başlatıcılarındaki dizeler: { "tr", "en", ... }
            var strs = new List<Tok>();
            int depth = 0; bool inArr = false;
            for (int k = 0; k < toks.Count; k++)
            {
                var t = toks[k];
                if (t.Kind == K.Term && t.Text == "=" && k + 1 < toks.Count && toks[k + 1].Text == "{") { inArr = true; continue; }
                if (!inArr) continue;
                if (t.Text == "{") depth++;
                else if (t.Text == "}") { depth--; if (depth == 0) { inArr = false; Pairs(f, strs, d, problems); strs.Clear(); } }
                else if (t.Kind == K.Str) strs.Add(t);
            }
        }
        return d;
    }

    static void Pairs(string f, List<Tok> s, Dictionary<string, string> d, List<string> problems)
    {
        var name = Path.GetFileName(f);
        if (s.Count % 2 != 0) problems.Add($"{name}: tek sayıda dize ({s.Count}) — bir çift eksik");
        for (int i = 0; i + 1 < s.Count; i += 2)
        {
            var tr = s[i].Text; var en = s[i + 1].Text;
            var ph1 = Regex.Matches(tr, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x).ToList();
            var ph2 = Regex.Matches(en, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x).ToList();
            if (!ph1.SequenceEqual(ph2)) problems.Add($"{name}:{s[i].Line} yer tutucular uyuşmuyor: {Esc(tr)} → {Esc(en)}");
            if (en.Length == 0) problems.Add($"{name}:{s[i].Line} boş çeviri: {Esc(tr)}");
            if (TurkishChar.IsMatch(en)) problems.Add($"{name}:{s[i].Line} çeviride Türkçe harf: {Esc(en)}");
            if (d.TryGetValue(tr, out var old) && old != en) problems.Add($"{name}:{s[i].Line} farklı çeviriyle tekrar: {Esc(tr)} → {Esc(old)} / {Esc(en)}");
            d[tr] = en;
        }
    }

    static string Esc(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
    static string Unesc(string s)
    {
        s = s.Trim();
        if (s.StartsWith("\"") && s.EndsWith("\"")) s = s.Substring(1, s.Length - 2);
        return s.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\");
    }
}
