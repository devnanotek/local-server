using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;

namespace DevNanotek.Core
{
    /// <summary>
    /// WinSW: kendi servis kurucusu olmayan programları (nginx, php-cgi, mailpit) Windows servisi yapar.
    /// Her servis için etc\winsw\{id}.exe (WinSW kopyası) + {id}.xml bulunur.
    /// </summary>
    public static class WinSwManager
    {
        public static string Exe => Path.Combine(Paths.BinWinSw, "WinSW.exe");
        public static bool IsInstalled => File.Exists(Exe);

        public static string ServiceExe(string id) => Path.Combine(Paths.EtcWinSw, id + ".exe");
        public static string ServiceXml(string id) => Path.Combine(Paths.EtcWinSw, id + ".xml");

        public static string BuildXml(string id, string displayName, string description, string executable, string arguments,
            string workingDir, IDictionary<string, string> env, string stopExecutable = null, string stopArguments = null, string logDir = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<!-- DevNanotek tarafından otomatik üretildi -->");
            sb.AppendLine("<service>");
            sb.AppendLine($"  <id>{E(id)}</id>");
            sb.AppendLine($"  <name>{E(displayName)}</name>");
            sb.AppendLine($"  <description>{E(description)}</description>");
            sb.AppendLine($"  <executable>{E(executable)}</executable>");
            if (!string.IsNullOrEmpty(arguments)) sb.AppendLine($"  <arguments>{E(arguments)}</arguments>");
            if (!string.IsNullOrEmpty(workingDir)) sb.AppendLine($"  <workingdirectory>{E(workingDir)}</workingdirectory>");
            if (!string.IsNullOrEmpty(stopExecutable))
            {
                sb.AppendLine($"  <stopexecutable>{E(stopExecutable)}</stopexecutable>");
                sb.AppendLine($"  <stoparguments>{E(stopArguments ?? "")}</stoparguments>");
            }
            sb.AppendLine("  <stoptimeout>20 sec</stoptimeout>");
            sb.AppendLine("  <startmode>Automatic</startmode>");
            sb.AppendLine("  <onfailure action=\"restart\" delay=\"5 sec\"/>");
            sb.AppendLine("  <onfailure action=\"restart\" delay=\"15 sec\"/>");
            sb.AppendLine("  <onfailure action=\"restart\" delay=\"60 sec\"/>");
            sb.AppendLine("  <resetfailure>1 day</resetfailure>");
            sb.AppendLine("  <priority>Normal</priority>");
            if (env != null)
                foreach (var kv in env) sb.AppendLine($"  <env name=\"{E(kv.Key)}\" value=\"{E(kv.Value)}\"/>");
            sb.AppendLine($"  <logpath>{E(logDir ?? Paths.LogsWinSw)}</logpath>");
            sb.AppendLine("  <log mode=\"roll-by-size\">");
            sb.AppendLine("    <sizeThreshold>5120</sizeThreshold>");
            sb.AppendLine("    <keepFiles>3</keepFiles>");
            sb.AppendLine("  </log>");
            sb.AppendLine("</service>");
            return sb.ToString();
        }

        private static string E(string s) => SecurityElement.Escape(s ?? "");

        /// <summary>
        /// Servisi XML'e göre kurar. Zaten kuruluysa ve XML aynıysa dokunmaz; farklıysa yeniden kurar.
        /// Dönüş: servis (yeniden) kuruldu mu.
        /// </summary>
        public static bool EnsureService(string id, string xml, Action<string> log = null)
        {
            if (!IsInstalled) throw new Exception("WinSW kurulu değil (Sürümler > Araçlar bölümünden kurun).");
            Directory.CreateDirectory(Paths.EtcWinSw);
            Directory.CreateDirectory(Paths.LogsWinSw);

            var exe = ServiceExe(id);
            var xmlPath = ServiceXml(id);
            bool exeOk = File.Exists(exe) && new FileInfo(exe).Length == new FileInfo(Exe).Length;
            bool xmlSame = File.Exists(xmlPath) && File.ReadAllText(xmlPath, Encoding.UTF8) == xml;
            bool exists = WindowsServices.Exists(id);

            if (exists && exeOk && xmlSame) return false;

            if (exists)
            {
                log?.Invoke($"{id} servisi yeniden kuruluyor...");
                Uninstall(id);
            }
            if (!exeOk) File.Copy(Exe, exe, true);
            File.WriteAllText(xmlPath, xml, new UTF8Encoding(false));

            var r = ProcessRunner.Run(exe, "install", Paths.EtcWinSw, 60000);
            if (!WindowsServices.Exists(id))
            {
                // WinSW bazı hatalarda 0 döndürür; servisin gerçekten oluştuğunu doğrula
                try { File.Delete(xmlPath); } catch { } // bir sonraki denemede yeniden kurulsun
                Logger.Error($"WinSW install başarısız ({id}): {r.AllOutput}");
                var outText = string.IsNullOrWhiteSpace(r.AllOutput) ? "(çıktı yok)" : r.AllOutput.Trim();
                if (SystemCheck.IsBlocked(r)) outText = "WinSW Windows tarafından engellendi (Akıllı Uygulama Denetimi).";
                throw new Exception($"{id} servisi kurulamadı. Programın yönetici olarak çalıştığından emin olun.\r\n{outText}");
            }
            WindowsServices.SetAutoRecovery(id);
            Logger.Info("Servis kuruldu (WinSW): " + id);
            return true;
        }

        public static void Uninstall(string id)
        {
            if (!WindowsServices.Exists(id)) return;
            WindowsServices.Stop(id, 60);
            var exe = ServiceExe(id);
            if (File.Exists(exe) && File.Exists(ServiceXml(id)))
            {
                var r = ProcessRunner.Run(exe, "uninstall", Paths.EtcWinSw, 60000);
                if (!r.Ok) Logger.Warn($"WinSW uninstall ({id}): {r.AllOutput}");
            }
            if (WindowsServices.Exists(id)) WindowsServices.Delete(id);
        }
    }
}
