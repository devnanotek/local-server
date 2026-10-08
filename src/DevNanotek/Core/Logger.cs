using System;
using System.IO;
using System.Text;

namespace DevNanotek.Core
{
    /// <summary>Basit dosya + olay tabanlı günlükleyici (logs\devnanotek.log).</summary>
    public static class Logger
    {
        private static readonly object Lock = new object();
        public static event Action<string> Message;

        // günlük, arayüz diliyle yazılır (İngilizce arayüzde iletiler L.T ile çevrilir)
        public static void Info(string msg) => Write(L.Pick("BILGI", "INFO "), L.T(msg));
        public static void Warn(string msg) => Write(L.Pick("UYARI", "WARN "), L.T(msg));
        public static void Error(string msg) => Write(L.Pick("HATA ", "ERROR"), L.T(msg));
        public static void Error(string msg, Exception ex) => Write(L.Pick("HATA ", "ERROR"), L.T(msg) + " :: " + ex.GetType().Name + ": " + L.T(ex.Message));

        private static void Write(string level, string msg)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level} {msg}";
            lock (Lock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Paths.AppLog));
                    // 5 MB üzerinde ise döndür
                    var fi = new FileInfo(Paths.AppLog);
                    if (fi.Exists && fi.Length > 5 * 1024 * 1024)
                    {
                        var old = Paths.AppLog + ".1";
                        try { if (File.Exists(old)) File.Delete(old); } catch { }
                        try { File.Move(Paths.AppLog, old); } catch { }
                    }
                    File.AppendAllText(Paths.AppLog, line + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
            try { Message?.Invoke(line); } catch { }
        }
    }
}
