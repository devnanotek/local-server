using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DevNanotek.Core;
using Microsoft.Win32;

namespace DevNanotek.Views
{
    /// <summary>
    /// Gece / gündüz teması. Mod: "system" (Windows ayarını izler), "light", "dark".
    /// Renk sözlüğü (Themes/Light.xaml veya Dark.xaml) uygulama kaynaklarında 0. sıradadır ve çalışırken değiştirilir.
    /// Pencere başlık çubukları da (Windows 10 20H1+ / 11) temaya uyar.
    /// </summary>
    public static class ThemeManager
    {
        public static bool IsDark { get; private set; }
        public static event Action Changed;
        private static bool _init;

        public static string Mode
        {
            get { var m = AppConfig.Current.Theme; return m == "light" || m == "dark" ? m : "system"; }
        }

        public static void Init()
        {
            if (_init) return;
            _init = true;
            Apply(Mode);
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (Mode == "system" && (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color))
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply("system")));
            };
            // her yeni pencere açıldığında başlık çubuğunu temaya uydur
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) => ApplyTitleBar(s as Window)));
        }

        public static void SetMode(string mode)
        {
            var cfg = AppConfig.Current;
            cfg.Theme = mode == "light" || mode == "dark" ? mode : "system";
            cfg.Save();
            Apply(cfg.Theme);
        }

        /// <summary>system → light → dark → system</summary>
        public static string Cycle()
        {
            var next = Mode == "system" ? "light" : Mode == "light" ? "dark" : "system";
            SetMode(next);
            return next;
        }

        public static string ModeTitle(string m) => m == "light" ? "Gündüz" : m == "dark" ? "Gece" : "Sistem (Windows'a göre)";
        public static string ModeGlyph(string m) => m == "light" ? "" : m == "dark" ? "" : "";

        public static bool SystemPrefersDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return Convert.ToInt32(k?.GetValue("AppsUseLightTheme", 1) ?? 1) == 0;
            }
            catch { return false; }
        }

        public static void Apply(string mode)
        {
            var app = Application.Current;
            if (app == null) return;
            bool dark = mode == "dark" || (mode == "system" && SystemPrefersDark());
            IsDark = dark;
            var dicts = app.Resources.MergedDictionaries;
            var uri = new Uri("pack://application:,,,/Themes/" + (dark ? "Dark" : "Light") + ".xaml", UriKind.Absolute);
            var rd = new ResourceDictionary { Source = uri };
            if (dicts.Count > 0) dicts[0] = rd; else dicts.Insert(0, rd);
            foreach (Window w in app.Windows) ApplyTitleBar(w);
            try { Changed?.Invoke(); } catch { }
        }

        // ---- başlık çubuğu (DWM) ----
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void ApplyTitleBar(Window w)
        {
            if (w == null) return;
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero) return;
                int on = IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4); // koyu başlık
                // Windows 11: başlık rengi = arka plan rengi (COLORREF 0x00BBGGRR)
                if (app() is Application a && a.TryFindResource("C.Bg") is Color c)
                {
                    int colorref = c.R | (c.G << 8) | (c.B << 16);
                    DwmSetWindowAttribute(hwnd, 35, ref colorref, 4);
                }
            }
            catch { }
        }

        private static Application app() => Application.Current;
    }

    /// <summary>"?" bilgi simgesi: üzerine gelince açıklama gösterir (kalabalık metinler yerine).</summary>
    public class InfoTip : Control
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(InfoTip), new PropertyMetadata(""));
        public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    }

    /// <summary>Renkli günlük satırı: başarılı yeşil, hata kırmızı, uyarı sarı.</summary>
    public class LogLine
    {
        public string Text { get; set; }
        public Level Level { get; set; }
        public Brush Brush => StatusColors.LogBrushOf(Level);
    }

    /// <summary>Kurulum / onarım / kaldırma pencerelerindeki renkli günlük.</summary>
    public class ColoredLog : ItemsControl
    {
        public ObservableCollection<LogLine> Lines { get; } = new ObservableCollection<LogLine>();
        private ScrollViewer _sv;

        public ColoredLog()
        {
            ItemsSource = Lines;
            var xaml = "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ItemsControl'>" +
                       "<Border Background='{DynamicResource CodeBg}' CornerRadius='8' Padding='10,8'>" +
                       "<ScrollViewer x:Name='sv' VerticalScrollBarVisibility='Auto'><ItemsPresenter/></ScrollViewer></Border></ControlTemplate>";
            Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
            var itemXaml = "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                           "<TextBlock Text='{Binding Text}' Foreground='{Binding Brush}' FontFamily='Cascadia Mono, Consolas' FontSize='12' TextWrapping='Wrap' Margin='0,1'/></DataTemplate>";
            ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse(itemXaml);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _sv = GetTemplateChild("sv") as ScrollViewer;
        }

        public void Clear() => Lines.Clear();

        public void Append(string text)
        {
            if (text == null) return;
            Lines.Add(new LogLine { Text = text, Level = Classify(text) });
            if (Lines.Count > 2000) Lines.RemoveAt(0);
            _sv?.ScrollToEnd();
        }

        public string AllText => string.Join(Environment.NewLine, Lines.Select(l => l.Text));

        public static Level Classify(string t)
        {
            var s = t.ToLowerInvariant();
            if (t.StartsWith("✖") || s.Contains("hata") || s.Contains("başarısız") || s.Contains("engelle") || s.Contains("kurulamadı") || s.Contains("başlatılamadı"))
                return Level.Error;
            if (t.StartsWith("⚠") || s.Contains("uyarı")) return Level.Warn;
            if (t.StartsWith("✔") || s.Contains("kuruldu") || s.Contains("tamamlandı") || s.Contains("uygulandı") || s.Contains("hazır") || s.Contains("geçti") || s.Contains("başarılı"))
                return Level.Ok;
            return Level.Off;
        }
    }
}
