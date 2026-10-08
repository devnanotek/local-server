using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DevNanotek.Core;

namespace DevNanotek.Views
{
    public class LogFileItem
    {
        public string Title { get; set; }
        public string Path { get; set; }
    }

    public partial class LogsView : ViewBase
    {
        private readonly DispatcherTimer _timer;

        public LogsView()
        {
            InitializeComponent();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _timer.Tick += (s, e) => { if (AutoRefresh.IsChecked == true && IsVisible) LoadSelected(); };
            _timer.Start();
        }

        /// <summary>Başka sayfadan belirli bir günlüğü açmak için.</summary>
        public static string RequestPath { get; set; }

        public override void Refresh()
        {
            var keep = RequestPath ?? (LogCombo.SelectedItem as LogFileItem)?.Path;
            RequestPath = null;
            var items = new List<LogFileItem>
            {
                new LogFileItem { Title = "DEVNANOTEK (program)", Path = Paths.AppLog },
                new LogFileItem { Title = "Apache — hata", Path = System.IO.Path.Combine(Paths.LogsApache, "error.log") },
                new LogFileItem { Title = "Apache — erişim", Path = System.IO.Path.Combine(Paths.LogsApache, "access.log") },
                new LogFileItem { Title = "Nginx — hata", Path = System.IO.Path.Combine(Paths.LogsNginx, "error.log") },
                new LogFileItem { Title = "Nginx — erişim", Path = System.IO.Path.Combine(Paths.LogsNginx, "access.log") },
                new LogFileItem { Title = "PHP — hata (php_error.log)", Path = System.IO.Path.Combine(Paths.LogsPhp, "php_error.log") },
                new LogFileItem { Title = "Veritabanı — hata", Path = System.IO.Path.Combine(Paths.LogsDb, "error.log") },
            };
            var pgLog = PostgreSqlManager.LatestLog();
            if (pgLog != null) items.Add(new LogFileItem { Title = "PostgreSQL — son günlük", Path = pgLog });
            // ek dosyalar: sanal host logları, WinSW, mailpit, PostgreSQL (haftanın günlerine göre)
            foreach (var dir in new[] { Paths.LogsApache, Paths.LogsNginx, Paths.LogsWinSw, Paths.LogsMailpit, Paths.LogsPostgreSql })
            {
                try
                {
                    foreach (var f in Directory.GetFiles(dir, "*.log").OrderBy(x => x))
                        if (!items.Any(i => string.Equals(i.Path, f, StringComparison.OrdinalIgnoreCase)))
                            items.Add(new LogFileItem { Title = System.IO.Path.GetFileName(dir) + " — " + System.IO.Path.GetFileName(f), Path = f });
                }
                catch { }
            }
            if (keep != null && !items.Any(i => string.Equals(i.Path, keep, StringComparison.OrdinalIgnoreCase)))
                items.Add(new LogFileItem { Title = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(keep)) + " — " + System.IO.Path.GetFileName(keep), Path = keep });
            LogCombo.ItemsSource = items;
            LogCombo.SelectedItem = items.FirstOrDefault(i => string.Equals(i.Path, keep, StringComparison.OrdinalIgnoreCase)) ?? items[0];
        }

        private void LogCombo_Changed(object sender, SelectionChangedEventArgs e) => LoadSelected();
        private void Reload_Click(object sender, RoutedEventArgs e) => LoadSelected();

        private void LoadSelected()
        {
            var item = LogCombo.SelectedItem as LogFileItem;
            if (item == null) return;
            LogText.Text = Tail(item.Path, 400);
            LogText.ScrollToEnd();
        }

        public static string Tail(string path, int lines)
        {
            if (!File.Exists(path)) return "(dosya henüz yok: " + path + ")";
            var text = Stack.TailFile(path, lines);
            return string.IsNullOrWhiteSpace(text) ? "(boş)" : text;
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            var item = LogCombo.SelectedItem as LogFileItem;
            if (item != null && File.Exists(item.Path)) ProcessRunner.StartDetached("notepad.exe", "\"" + item.Path + "\"");
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            var item = LogCombo.SelectedItem as LogFileItem;
            if (item == null || !File.Exists(item.Path)) return;
            if (!UI.Confirm(item.Path + " içeriği silinecek. Devam?")) return;
            try
            {
                using (var fs = new FileStream(item.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) fs.SetLength(0);
            }
            catch (Exception ex) { UI.Err("Temizlenemedi (dosya kullanımda olabilir): " + ex.Message); }
            LoadSelected();
        }

        private void Folder_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenFolder(Paths.Logs);
    }
}
