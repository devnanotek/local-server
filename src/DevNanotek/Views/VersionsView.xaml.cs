using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DevNanotek.Core;

namespace DevNanotek.Views
{
    public partial class VersionsView : ViewBase
    {
        public VersionsView()
        {
            InitializeComponent();
            PhpList.Components = new[] { Comp.Php };
            PhpList.AllowCustom = true;
            PhpList.Description = "Hosting'inizdeki sürümü kurmanız önerilir.";
            WebList.Components = new[] { Comp.Apache, Comp.Nginx };
            WebList.AllowCustom = true;
            WebList.Description = "Apache önerilir (.htaccess). Aktif olanı 'Aktif yap' ile seçin.";
            DbList.Components = new[] { Comp.MariaDb, Comp.MySql, Comp.PostgreSql };
            DbList.AllowCustom = true;
            DbList.Description = "MariaDB veya MySQL'den biri aktif olur; PostgreSQL isteğe bağlıdır ve onlarla birlikte çalışır. Her dalın kendi veri klasörü vardır. SQLite için kurulum gerekmez (PHP'de hazır).";
            NodeList.Components = new[] { Comp.Node };
            NodeList.AllowCustom = true;
            NodeList.Description = "node, npm ve npx terminalde ve PATH'te hazır olur.";
            ToolsList.Components = new[] { Comp.PhpMyAdmin, Comp.Adminer, Comp.Mailpit, Comp.Mkcert, Comp.Composer, Comp.SqlSrv, Comp.WinSw, Comp.CaCert, Comp.VcRedist };
            ToolsList.Description = "Yardımcı araçlar. Ayrıntı için ? simgelerine bakın.";
        }

        public override void Refresh()
        {
            PhpList.Reload(); WebList.Reload(); DbList.Reload(); NodeList.Reload(); ToolsList.Reload();
            var up = Catalog.Current.UpdatedAt;
            var when = up.HasValue ? L.F("son denetim {0}", up.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm")) : L.T("henüz internetten denetlenmedi");
            CatalogInfo.Text = SystemInfo.Summary + "  ·  " + when + (UpdateChecker.LastError != null ? "  ·  ⚠ " + L.T(UpdateChecker.LastError) : "");
            CheckBtn.IsEnabled = !UpdateChecker.IsChecking;
            CheckBtnText.Text = UpdateChecker.IsChecking ? "Denetleniyor…" : "Şimdi denetle";

            var app = AppUpdater.Available;
            AppUpdateCard.Visibility = app != null ? Visibility.Visible : Visibility.Collapsed;
            if (app != null) AppUpdateTitle.Text = $"DEVNANOTEK {app.Version.ToString(3)} yayınlandı (kullandığınız: {AppInfo.CurrentText})";

            var ups = UpdateChecker.Current;
            UpdatesCard.Visibility = ups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdatesTitle.Text = ups.Count == 1 ? "1 güncelleme var" : ups.Count + " güncelleme var";
            UpdatesList.ItemsSource = ups.ToList();
        }

        private async void RefreshCatalog_Click(object sender, RoutedEventArgs e)
        {
            CheckBtn.IsEnabled = false;
            CheckBtnText.Text = "Denetleniyor…";
            await UpdateChecker.AutoCheckAsync(true);
            Refresh();
            var n = UpdateChecker.Current.Count + (AppUpdater.Available != null ? 1 : 0);
            if (UpdateChecker.LastError != null) Main.Toast(Level.Error, "Sürümler denetlenemedi (internet bağlantısını kontrol edin).", 8000, UpdateChecker.LastError);
            else Main.Toast(n > 0 ? Level.Warn : Level.Ok, n > 0 ? n + " güncelleme bulundu." : "Her şey güncel (DEVNANOTEK v" + AppInfo.CurrentText + ").");
        }

        private async void AppUpdate_Click(object sender, RoutedEventArgs e) => await Main.UpdateProgramAsync();
        private void AppNotes_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenUrl(AppUpdater.Available?.PageUrl ?? AppInfo.ReleasesUrl);

        private void Wizard_Click(object sender, RoutedEventArgs e)
        {
            var w = new SetupWindow { Owner = Window.GetWindow(this) };
            w.ShowDialog();
            Refresh();
        }

        private async void UpdateOne_Click(object sender, RoutedEventArgs e)
        {
            var u = (sender as Button)?.Tag as UpdateInfo;
            if (u == null) return;
            await RunUpdates(new[] { u });
        }

        private async void UpdateAll_Click(object sender, RoutedEventArgs e)
        {
            var list = UpdateChecker.Current.ToList();
            if (list.Count == 0) return;
            if (!UI.Confirm("Şu güncellemeler uygulanacak:\n\n• " + string.Join("\n• ", list.Select(u => u.Text)) + "\n\nServisler kısa süre yeniden başlatılacak. Devam edilsin mi?")) return;
            await RunUpdates(list);
        }

        private async Task RunUpdates(UpdateInfo[] list) => await RunUpdates(list.ToList());

        private async Task RunUpdates(System.Collections.Generic.List<UpdateInfo> list)
        {
            var warnings = new System.Collections.Generic.List<string>();
            var errors = new System.Collections.Generic.List<string>();
            await Main.RunBusyAsync("Güncelleniyor", async log =>
            {
                foreach (var u in list)
                {
                    var progress = new Progress<DownloadProgress>(p =>
                    {
                        Main.SetBusyProgress(p.Percent, p.Indeterminate);
                        if (!string.IsNullOrEmpty(p.Status)) log(p.Status);
                    });
                    try
                    {
                        var r = await UpdateChecker.ApplyAsync(u, log, progress, CancellationToken.None);
                        warnings.AddRange(r.Warnings);
                        errors.AddRange(r.Errors);
                        log((r.Ok ? "✔ " : "✖ ") + u.Text);
                    }
                    catch (Exception ex) { errors.Add(u.Text + ": " + ex.Message); log("✖ " + u.Text + ": " + ex.Message); }
                }
            }, true);
            var res = new ApplyResult();
            res.Errors.AddRange(errors); res.Warnings.AddRange(warnings);
            Main.ShowApplyResult(res, list.Count == 1 ? list[0].Text + " — güncellendi." : list.Count + " bileşen güncellendi.");
            Refresh();
        }
    }
}
