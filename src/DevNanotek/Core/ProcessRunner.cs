using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DevNanotek.Core
{
    public class ProcessResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; } = "";
        public string StdErr { get; set; } = "";
        public bool TimedOut { get; set; }
        public bool Ok => ExitCode == 0 && !TimedOut;
        public string AllOutput => (StdOut + Environment.NewLine + StdErr).Trim();
        public override string ToString() => $"exit={ExitCode} timeout={TimedOut}\n{AllOutput}";
    }

    /// <summary>Konsol uygulamalarını penceresiz çalıştırır ve çıktısını yakalar.</summary>
    public static class ProcessRunner
    {
        public static ProcessResult Run(string exe, string args, string workDir = null, int timeoutMs = 60000, IDictionary<string, string> env = null, string stdin = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args ?? "",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null,
                WorkingDirectory = workDir ?? (File.Exists(exe) ? Path.GetDirectoryName(exe) : Paths.Root),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (env != null) foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;

            var res = new ProcessResult();
            var so = new StringBuilder(); var se = new StringBuilder();
            try
            {
                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (so) so.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (se) se.AppendLine(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (stdin != null)
                    {
                        try { p.StandardInput.Write(stdin); p.StandardInput.Close(); } catch { }
                    }
                    if (!p.WaitForExit(timeoutMs))
                    {
                        res.TimedOut = true;
                        try { p.Kill(); } catch { }
                        res.ExitCode = -1;
                    }
                    else
                    {
                        p.WaitForExit(); // async okuyucuların bitmesi için
                        res.ExitCode = p.ExitCode;
                    }
                }
            }
            catch (Exception ex)
            {
                res.ExitCode = -2;
                se.AppendLine(ex.Message);
            }
            res.StdOut = so.ToString();
            res.StdErr = se.ToString();
            return res;
        }

        public static Task<ProcessResult> RunAsync(string exe, string args, string workDir = null, int timeoutMs = 60000, IDictionary<string, string> env = null, string stdin = null)
            => Task.Run(() => Run(exe, args, workDir, timeoutMs, env, stdin));

        /// <summary>cmd.exe üzerinden bir komut satırı çalıştırır.</summary>
        public static ProcessResult Cmd(string commandLine, string workDir = null, int timeoutMs = 60000)
            => Run("cmd.exe", "/d /s /c \"" + commandLine + "\"", workDir ?? Paths.Root, timeoutMs);

        /// <summary>Beklemeden, kullanıcıya görünür şekilde başlatır (explorer, tarayıcı, terminal...).</summary>
        public static void StartDetached(string fileOrUrl, string args = null, string workDir = null, IDictionary<string, string> env = null)
        {
            try
            {
                var psi = new ProcessStartInfo { FileName = fileOrUrl, Arguments = args ?? "", UseShellExecute = env == null };
                if (!string.IsNullOrEmpty(workDir)) psi.WorkingDirectory = workDir;
                if (env != null) foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
                Process.Start(psi);
            }
            catch (Exception ex) { Logger.Error("Başlatılamadı: " + fileOrUrl, ex); }
        }

        public static void OpenUrl(string url) => StartDetached(url);
        public static void OpenFolder(string path)
        {
            try { Directory.CreateDirectory(path); } catch { }
            StartDetached("explorer.exe", "\"" + path + "\"");
        }
        public static void OpenInEditor(string file)
        {
            if (!File.Exists(file)) { try { File.WriteAllText(file, ""); } catch { } }
            // Varsayılan ilişkilendirme yoksa notepad
            try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); }
            catch { StartDetached("notepad.exe", "\"" + file + "\""); }
        }
    }
}
