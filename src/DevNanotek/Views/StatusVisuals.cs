using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DevNanotek.Core;
using Drawing = System.Drawing;

namespace DevNanotek.Views
{
    /// <summary>Durum renkleri — her yerde aynı: yeşil çalışıyor, sarı durdu, kırmızı hata, gri kapalı.</summary>
    public static class StatusColors
    {
        public static readonly Color Ok = Color.FromRgb(0x22, 0xB5, 0x73);
        public static readonly Color Warn = Color.FromRgb(0xF5, 0xA5, 0x24);
        public static readonly Color Error = Color.FromRgb(0xE5, 0x48, 0x4D);
        public static readonly Color Off = Color.FromRgb(0x9A, 0x93, 0x86);

        // koyu kod/günlük zemini üzerinde okunaklı tonlar
        private static readonly Color LogOk = Color.FromRgb(0x3D, 0xD6, 0x8C);
        private static readonly Color LogWarn = Color.FromRgb(0xFF, 0xC1, 0x4D);
        private static readonly Color LogErr = Color.FromRgb(0xFF, 0x6B, 0x6B);
        private static readonly Color LogText = Color.FromRgb(0xE8, 0xE4, 0xDB);

        private static readonly Dictionary<string, SolidColorBrush> Brushes = new Dictionary<string, SolidColorBrush>();

        public static Color ColorOf(Level l)
        {
            switch (l) { case Level.Ok: return Ok; case Level.Warn: return Warn; case Level.Error: return Error; default: return Off; }
        }

        private static SolidColorBrush Get(string key, Color c)
        {
            lock (Brushes)
            {
                if (!Brushes.TryGetValue(key, out var b)) { b = new SolidColorBrush(c); b.Freeze(); Brushes[key] = b; }
                return b;
            }
        }

        public static SolidColorBrush BrushOf(Level l) => Get("d" + l, ColorOf(l));

        public static SolidColorBrush LogBrushOf(Level l)
        {
            switch (l)
            {
                case Level.Ok: return Get("lok", LogOk);
                case Level.Warn: return Get("lwarn", LogWarn);
                case Level.Error: return Get("lerr", LogErr);
                default: return Get("ltext", LogText);
            }
        }
    }

    /// <summary>XAML: Fill="{Binding Level, Converter={StaticResource LevelBrush}}"</summary>
    public class LevelToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Level l ? StatusColors.BrushOf(l) : StatusColors.BrushOf(Level.Off);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>
    /// Tepsi simgesi: DEVNANOTEK logosu (koyu kare + altın D) vektörden çizilir, sağ alt köşeye renkli durum noktası eklenir.
    /// </summary>
    public static class TrayIcons
    {
        private static readonly Dictionary<Level, Drawing.Icon> Cache = new Dictionary<Level, Drawing.Icon>();
        private static readonly List<IntPtr> Handles = new List<IntPtr>();
        private const string DPath = "M16 14h17c11 0 18 7 18 18S44 50 33 50H16V14zm10 9v18h6c6 0 9-3 9-9s-3-9-9-9h-6z";

        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);

        public static Drawing.Icon Get(Level level)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(level, out var ic)) return ic;
                ic = Create(level);
                Cache[level] = ic;
                return ic;
            }
        }

        /// <summary>Logo + nokta, WPF ile çizilir ve PNG üzerinden GDI simgesine dönüştürülür.</summary>
        private static byte[] RenderPng(int s, Level level)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double k = s / 64.0;
                dc.PushTransform(new ScaleTransform(k, k));
                var bg = new SolidColorBrush(Color.FromRgb(0x23, 0x24, 0x25));
                var gold = new SolidColorBrush(Color.FromRgb(0xFA, 0xA4, 0x1A));
                dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, 64, 64), 16, 16);
                dc.DrawGeometry(gold, null, Geometry.Parse(DPath));
                if (level != Level.Off)
                {
                    var c = new SolidColorBrush(StatusColors.ColorOf(level));
                    dc.DrawEllipse(Brushes.White, null, new Point(48, 48), 17, 17);
                    dc.DrawEllipse(c, null, new Point(48, 48), 13, 13);
                }
                dc.Pop();
            }
            var rtb = new RenderTargetBitmap(s, s, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var ms = new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
        }

        private static Drawing.Icon Create(Level level)
        {
            int s = Math.Max(16, System.Windows.Forms.SystemInformation.SmallIconSize.Width);
            using (var ms = new MemoryStream(RenderPng(s, level)))
            using (var bmp = new Drawing.Bitmap(ms))
            {
                var h = bmp.GetHicon();
                Handles.Add(h);
                return Drawing.Icon.FromHandle(h);
            }
        }

        /// <summary>Test/dokümantasyon: simgeyi PNG olarak kaydeder.</summary>
        public static void SavePng(Level level, string file, int size = 32) => File.WriteAllBytes(file, RenderPng(size, level));

        public static void DisposeAll()
        {
            lock (Cache)
            {
                foreach (var ic in Cache.Values) try { ic.Dispose(); } catch { }
                foreach (var h in Handles) try { DestroyIcon(h); } catch { }
                Cache.Clear(); Handles.Clear();
            }
        }
    }
}
