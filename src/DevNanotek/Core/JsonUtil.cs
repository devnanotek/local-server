using System;
using System.Text;
using System.Web.Script.Serialization;

namespace DevNanotek.Core
{
    /// <summary>
    /// .NET Framework'te ek paket gerektirmeyen JSON yardımcıları (System.Web.Extensions).
    /// </summary>
    public static class JsonUtil
    {
        private static JavaScriptSerializer NewSerializer() => new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };

        public static T Deserialize<T>(string json) => NewSerializer().Deserialize<T>(json);
        public static object DeserializeObject(string json) => NewSerializer().DeserializeObject(json);
        public static string Serialize(object o) => NewSerializer().Serialize(o);

        /// <summary>İnsan tarafından okunabilir (girintili) JSON üretir.</summary>
        public static string SerializePretty(object o) => Indent(Serialize(o));

        public static string Indent(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;
            var sb = new StringBuilder(json.Length * 2);
            int depth = 0; bool inStr = false; bool esc = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') inStr = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inStr = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        // boş obje/dizi
                        if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']')) { sb.Append(json[i + 1]); i++; break; }
                        depth++; sb.AppendLine(); sb.Append(' ', depth * 2); break;
                    case '}':
                    case ']':
                        depth--; sb.AppendLine(); sb.Append(' ', Math.Max(0, depth) * 2); sb.Append(c); break;
                    case ',': sb.Append(c); sb.AppendLine(); sb.Append(' ', depth * 2); break;
                    case ':': sb.Append(": "); break;
                    default:
                        if (!char.IsWhiteSpace(c)) sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
