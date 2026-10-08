using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace DevNanotek.Core
{
    /// <summary>
    /// Nginx modu: php-cgi.exe süreçlerini (N adet, ardışık portlarda) ayakta tutar.
    /// DevNanotek.exe --fcgi-spawner --php "php-cgi.exe" --host 127.0.0.1 --port 9000 --count 4 --ini "php klasörü"
    /// WinSW servisi (DevNanotek-PHP-FCGI) bu komutu çalıştırır. Job Object sayesinde ana süreç ölürse çocuklar da ölür.
    /// </summary>
    public static class FcgiSpawner
    {
        public static int Run(string[] args)
        {
            string php = null, host = "127.0.0.1", ini = null; int port = 9000, count = 4;
            for (int i = 0; i < args.Length; i++)
            {
                string Next() => i + 1 < args.Length ? args[++i] : null;
                switch (args[i])
                {
                    case "--php": php = Next(); break;
                    case "--host": host = Next(); break;
                    case "--port": int.TryParse(Next(), out port); break;
                    case "--count": int.TryParse(Next(), out count); break;
                    case "--ini": ini = Next(); break;
                }
            }
            if (string.IsNullOrEmpty(php) || !System.IO.File.Exists(php))
            {
                Console.Error.WriteLine("php-cgi.exe bulunamadı: " + php);
                return 2;
            }
            count = Math.Max(1, Math.Min(16, count));
            try { Console.OutputEncoding = new System.Text.UTF8Encoding(false); } catch { }
            Console.WriteLine($"[fcgi-spawner] {php}  {host}:{port}..{port + count - 1}  ({count} süreç)");

            var job = JobObject.Create();
            var procs = new Process[count];
            var stop = new ManualResetEvent(false);
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; stop.Set(); };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => stop.Set();

            try
            {
                while (!stop.WaitOne(1000))
                {
                    for (int i = 0; i < count; i++)
                    {
                        var p = procs[i];
                        if (p != null && !p.HasExited) continue;
                        if (p != null) { Console.WriteLine($"[fcgi-spawner] port {port + i} süreci çıktı (kod {SafeExit(p)}), yeniden başlatılıyor"); p.Dispose(); }
                        procs[i] = StartChild(php, host, port + i, ini, job);
                    }
                }
            }
            finally
            {
                Console.WriteLine("[fcgi-spawner] durduruluyor");
                foreach (var p in procs)
                {
                    try { if (p != null && !p.HasExited) { p.Kill(); p.WaitForExit(3000); } } catch { }
                }
                job?.Dispose();
            }
            return 0;
        }

        private static int SafeExit(Process p) { try { return p.ExitCode; } catch { return -1; } }

        private static Process StartChild(string php, string host, int port, string ini, JobObject job)
        {
            var psi = new ProcessStartInfo
            {
                FileName = php,
                Arguments = $"-b {host}:{port}" + (string.IsNullOrEmpty(ini) ? "" : $" -c \"{ini}\""),
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(php)
            };
            psi.EnvironmentVariables["PHP_FCGI_MAX_REQUESTS"] = "0";
            psi.EnvironmentVariables.Remove("PHP_FCGI_CHILDREN");
            var p = Process.Start(psi);
            try { job?.Assign(p); } catch { }
            Console.WriteLine($"[fcgi-spawner] başlatıldı: {host}:{port} pid={p.Id}");
            return p;
        }
    }

    /// <summary>Çocuk süreçlerin ana süreçle birlikte ölmesi için Windows Job Object.</summary>
    public sealed class JobObject : IDisposable
    {
        private IntPtr _handle;

        public static JobObject Create()
        {
            try
            {
                var j = new JobObject { _handle = CreateJobObject(IntPtr.Zero, null) };
                if (j._handle == IntPtr.Zero) return null;
                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                int len = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr ptr = Marshal.AllocHGlobal(len);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    SetInformationJobObject(j._handle, 9 /*JobObjectExtendedLimitInformation*/, ptr, (uint)len);
                }
                finally { Marshal.FreeHGlobal(ptr); }
                return j;
            }
            catch { return null; }
        }

        public void Assign(Process p) { if (_handle != IntPtr.Zero) AssignProcessToJobObject(_handle, p.Handle); }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero) { CloseHandle(_handle); _handle = IntPtr.Zero; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr a, string lpName);
        [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr hJob, int infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit; public long PerJobUserTimeLimit; public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize; public UIntPtr MaximumWorkingSetSize; public uint ActiveProcessLimit;
            public UIntPtr Affinity; public uint PriorityClass; public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation; public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit; public UIntPtr JobMemoryLimit; public UIntPtr PeakProcessMemoryUsed; public UIntPtr PeakJobMemoryUsed;
        }
    }
}
