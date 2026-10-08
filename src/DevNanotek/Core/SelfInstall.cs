using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace DevNanotek.Core
{
    /// <summary>
    /// Program nereden çalıştırılırsa çalıştırılsın kendini C:\devnanotek\app\DevNanotek.exe konumuna kopyalar,
    /// Masaüstü ve Başlat menüsü kısayollarını oluşturur. Böylece servisler ve zamanlanmış görev hep sabit bir yola bakar.
    /// </summary>
    public static class SelfInstall
    {
        public static bool IsRunningFromAppDir =>
            string.Equals(Path.GetFullPath(Paths.ExePath), Path.GetFullPath(Paths.AppExe), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Gerekirse kopyalar. Dönüş: başlatılması gereken exe yolu (null => mevcut süreçle devam et).
        /// </summary>
        public static string EnsureInstalled()
        {
            try
            {
                if (!Paths.IsRealExe) return null; // ör. test/bellek içi çalıştırma: hiçbir şey kopyalama
                if (IsRunningFromAppDir) { EnsureShortcuts(); return null; }
                Directory.CreateDirectory(Paths.AppDir);

                var cur = new FileInfo(Paths.ExePath);
                var dst = new FileInfo(Paths.AppExe);
                bool needCopy = !dst.Exists;
                if (dst.Exists)
                {
                    var curVer = AssemblyName.GetAssemblyName(cur.FullName).Version;
                    Version dstVer;
                    try { dstVer = AssemblyName.GetAssemblyName(dst.FullName).Version; } catch { dstVer = new Version(0, 0); }
                    needCopy = curVer > dstVer || (curVer == dstVer && (cur.Length != dst.Length || cur.LastWriteTimeUtc > dst.LastWriteTimeUtc));
                }
                if (needCopy)
                {
                    File.Copy(cur.FullName, dst.FullName, true);
                    // .config dosyası varsa onu da taşı
                    var cfg = cur.FullName + ".config";
                    if (File.Exists(cfg)) File.Copy(cfg, dst.FullName + ".config", true);
                    Logger.Info("Program kuruldu/güncellendi: " + dst.FullName);
                }
                EnsureShortcuts();
                return Paths.AppExe;
            }
            catch (Exception ex)
            {
                Logger.Warn("Program C:\\devnanotek\\app altına kopyalanamadı, bulunduğu yerden çalışacak: " + ex.Message);
                return null;
            }
        }

        public static void EnsureShortcuts()
        {
            var target = File.Exists(Paths.AppExe) ? Paths.AppExe : Paths.ExePath;
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DevNanotek");
                Directory.CreateDirectory(startMenu);
                CreateShortcut(Path.Combine(desktop, "DevNanotek.lnk"), target, "", L.T("DevNanotek yerel sunucu yöneticisi"));
                CreateShortcut(Path.Combine(startMenu, "DevNanotek.lnk"), target, "", L.T("DevNanotek yerel sunucu yöneticisi"));
                LanguageShortcut(startMenu, "httpdocs klasörü.lnk", "httpdocs folder.lnk", Paths.HttpDocs, L.T("Projeleriniz"));
                var guide = GuideFile;
                if (File.Exists(guide)) LanguageShortcut(startMenu, "DevNanotek Kılavuzu.lnk", "DevNanotek Guide.lnk", guide, L.T("Kullanım kılavuzu"));
            }
            catch (Exception ex) { Logger.Warn("Kısayol oluşturulamadı: " + ex.Message); }
        }

        /// <summary>Adı arayüz diline göre değişen kısayol: diğer dildeki eski kısayol silinir.</summary>
        private static void LanguageShortcut(string dir, string trName, string enName, string target, string description)
        {
            try { var old = Path.Combine(dir, L.Pick(enName, trName)); if (File.Exists(old)) File.Delete(old); } catch { }
            CreateShortcut(Path.Combine(dir, L.Pick(trName, enName)), target, "", description);
        }

        private static void CreateShortcut(string lnk, string target, string args, string description)
        {
            if (File.Exists(lnk))
            {
                // hedef aynıysa dokunma
                try
                {
                    dynamic sh0 = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                    dynamic s0 = sh0.CreateShortcut(lnk);
                    if (string.Equals((string)s0.TargetPath, target, StringComparison.OrdinalIgnoreCase)) return;
                }
                catch { }
            }
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            dynamic sc = shell.CreateShortcut(lnk);
            sc.TargetPath = target;
            sc.Arguments = args ?? "";
            sc.Description = description ?? "";
            sc.WorkingDirectory = Directory.Exists(target) ? target : Path.GetDirectoryName(target);
            if (File.Exists(target) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) sc.IconLocation = target + ",0";
            sc.Save();
        }

        public static void RemoveShortcuts()
        {
            try
            {
                var desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "DevNanotek.lnk");
                if (File.Exists(desktop)) File.Delete(desktop);
                var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DevNanotek");
                if (Directory.Exists(startMenu)) Directory.Delete(startMenu, true);
            }
            catch (Exception ex) { Logger.Warn("Kısayollar silinemedi: " + ex.Message); }
        }

        /// <summary>Arayüz dilindeki kılavuz: docs\Kilavuz.html (Türkçe) veya docs\Guide.html (İngilizce).</summary>
        public static string GuideFile => Path.Combine(Paths.Docs, L.Pick("Kilavuz.html", "Guide.html"));

        /// <summary>Gömülü kullanım kılavuzlarını (Türkçe ve İngilizce) docs klasörüne yazar; arayüz dilindekinin yolunu döndürür.</summary>
        public static string WriteGuide()
        {
            foreach (var name in new[] { "Kilavuz.html", "Guide.html" })
            {
                try { Templates.WriteIfChanged(Path.Combine(Paths.Docs, name), Templates.Load(name)); }
                catch (Exception ex) { Logger.Warn("Kılavuz yazılamadı: " + name + " — " + ex.Message); }
            }
            return GuideFile;
        }

        /// <summary>Linux sürümünü (devnanotek.sh + KURULUM.md) docs\linux klasörüne yazar. Satır sonları LF kalır.</summary>
        public static string WriteLinuxKit()
        {
            try
            {
                var dir = Path.Combine(Paths.Docs, "linux");
                Templates.WriteIfChanged(Path.Combine(dir, "devnanotek.sh"), Templates.Load("linux.devnanotek.sh"));
                Templates.WriteIfChanged(Path.Combine(dir, "KURULUM.md"), Templates.Load("linux.KURULUM.md"));
                return dir;
            }
            catch (Exception ex) { Logger.Warn("Linux dosyaları yazılamadı: " + ex.Message); return null; }
        }

        public static void Relaunch(string exe, string args)
        {
            Process.Start(new ProcessStartInfo(exe, args ?? "") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
        }
    }
}
