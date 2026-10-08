using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace DevNanotek.Core
{
    public class PortOwner
    {
        public int Port { get; set; }
        public int Pid { get; set; }
        public string ProcessName { get; set; }
        public string ProcessPath { get; set; }
        public override string ToString() => $"{ProcessName} (PID {Pid})";
    }

    /// <summary>
    /// Port kullanım denetimi. netstat çıktısı Türkçe Windows'ta yerelleştirildiği için
    /// doğrudan Win32 GetExtendedTcpTable API'si kullanılır.
    /// </summary>
    public static class PortUtil
    {
        private const int AF_INET = 2, AF_INET6 = 23;
        private const int TCP_TABLE_OWNER_PID_LISTENER = 3;

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tblClass, uint reserved);

        /// <summary>Dinlemedeki tüm TCP portları ve sahip PID'ler.</summary>
        public static Dictionary<int, int> ListeningPorts()
        {
            var map = new Dictionary<int, int>();
            foreach (var af in new[] { AF_INET, AF_INET6 })
            {
                int len = 0;
                GetExtendedTcpTable(IntPtr.Zero, ref len, false, af, TCP_TABLE_OWNER_PID_LISTENER, 0);
                if (len <= 0) continue;
                var buf = Marshal.AllocHGlobal(len);
                try
                {
                    if (GetExtendedTcpTable(buf, ref len, false, af, TCP_TABLE_OWNER_PID_LISTENER, 0) != 0) continue;
                    int count = Marshal.ReadInt32(buf);
                    // IPv4 satırı: state, localAddr, localPort, remoteAddr, remotePort, pid = 6 * 4 bayt
                    // IPv6 satırı: localAddr[16], localScope, localPort, remoteAddr[16], remoteScope, remotePort, state, pid = 56 bayt
                    int rowSize = af == AF_INET ? 24 : 56;
                    int portOffset = af == AF_INET ? 8 : 20;
                    int pidOffset = af == AF_INET ? 20 : 52;
                    var p = IntPtr.Add(buf, 4);
                    for (int i = 0; i < count; i++)
                    {
                        var row = IntPtr.Add(p, i * rowSize);
                        int rawPort = Marshal.ReadInt32(row, portOffset);
                        int port = ((rawPort & 0xFF) << 8) | ((rawPort >> 8) & 0xFF);
                        int pid = Marshal.ReadInt32(row, pidOffset);
                        if (!map.ContainsKey(port)) map[port] = pid;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return map;
        }

        /// <summary>Portta dinleyen süreci döndürür; boşsa null.</summary>
        public static PortOwner WhoListens(int port)
        {
            try
            {
                var map = ListeningPorts();
                if (!map.TryGetValue(port, out var pid)) return null;
                var o = new PortOwner { Port = port, Pid = pid, ProcessName = pid == 4 ? "System" : "?" };
                try
                {
                    using (var p = Process.GetProcessById(pid))
                    {
                        o.ProcessName = p.ProcessName;
                        try { o.ProcessPath = p.MainModule?.FileName; } catch { }
                    }
                }
                catch { }
                return o;
            }
            catch (Exception ex)
            {
                Logger.Warn("Port tablosu okunamadı: " + ex.Message);
                return null;
            }
        }

        public static bool IsFree(int port)
        {
            if (WhoListens(port) != null) return false;
            try
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                l.Start(); l.Stop();
                return true;
            }
            catch { return false; }
        }

        public static bool IsListening(int port, string host = "127.0.0.1", int timeoutMs = 700)
        {
            try
            {
                using (var c = new TcpClient())
                {
                    var t = c.BeginConnect(host, port, null, null);
                    if (!t.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                    c.EndConnect(t);
                    return true;
                }
            }
            catch { return false; }
        }

        /// <summary>Servisler başlamadan önce port çakışmalarını açıklayan mesajlar üretir.</summary>
        public static List<string> Conflicts(AppConfig cfg, IEnumerable<string> ourProcessNames)
        {
            var msgs = new List<string>();
            var ours = new HashSet<string>(ourProcessNames ?? new string[0], StringComparer.OrdinalIgnoreCase);
            Dictionary<int, int> map;
            try { map = ListeningPorts(); } catch { return msgs; }

            void Check(int port, string what)
            {
                if (!map.ContainsKey(port)) return;
                var o = WhoListens(port);
                if (o == null) return;
                if (ours.Contains(o.ProcessName)) return; // kendi servisimiz
                // kendi bin klasörümüzden çalışan herhangi bir süreç de bizimdir
                if (!string.IsNullOrEmpty(o.ProcessPath) && o.ProcessPath.StartsWith(Paths.Root, StringComparison.OrdinalIgnoreCase)) return;
                var hint = "";
                var n = (o.ProcessName ?? "").ToLowerInvariant();
                if (n == "system" && (port == 80 || port == 443)) hint = " — büyük ihtimalle IIS ('World Wide Web Publishing Service') veya http.sys kullanan bir hizmet. Hizmetler'den durdurup devre dışı bırakın ya da Ayarlar'dan farklı port seçin.";
                else if (n.Contains("skype")) hint = " — Skype 80/443 kullanıyor olabilir; Skype ayarlarından kapatın.";
                else if (n.Contains("httpd") || n.Contains("apache")) hint = " — başka bir Apache (XAMPP/WAMP/Laragon) çalışıyor; onu durdurun.";
                else if (n.Contains("mysqld") || n.Contains("mariadbd")) hint = " — başka bir MySQL/MariaDB çalışıyor (XAMPP/Laragon/MySQL Installer); onu durdurun veya DB portunu değiştirin.";
                else if (n.Contains("postgres")) hint = " — başka bir PostgreSQL çalışıyor (EDB kurulumu); Hizmetler'den durdurun veya Ayarlar'dan PostgreSQL portunu değiştirin.";
                else if (n.Contains("nginx")) hint = " — başka bir Nginx çalışıyor.";
                else if (n.Contains("vmware")) hint = " — VMware 443 portunu kullanıyor olabilir.";
                msgs.Add($"{what} portu {port} kullanımda: {o.ProcessName} (PID {o.Pid}){hint}");
            }
            Check(cfg.HttpPort, "HTTP");
            if (cfg.SslEnabled) Check(cfg.HttpsPort, "HTTPS");
            Check(cfg.DbPort, "Veritabanı");
            if (PostgreSqlManager.IsActive(cfg)) Check(cfg.PgPort, "PostgreSQL");
            if (cfg.MailpitEnabled) { Check(cfg.SmtpPort, "SMTP"); Check(cfg.MailpitUiPort, "Mailpit arayüz"); }
            if (cfg.WebServer == "nginx") for (int i = 0; i < cfg.FcgiChildren; i++) Check(cfg.FcgiPort + i, "PHP FastCGI");
            return msgs;
        }
    }
}
