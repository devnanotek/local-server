using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DevNanotek.Core;

namespace DevNanotek.Views
{
    public class VersionRow
    {
        public CatalogEntry Entry { get; set; }
        public bool Installed { get; set; }
        public bool Active { get; set; }
        public string Title => Entry.Title;
        public string Note => Entry.Recommended && (Entry.Note == "Önerilen") ? "" : (Entry.Note ?? "");
        public Visibility NoteVis => string.IsNullOrWhiteSpace(Note) ? Visibility.Collapsed : Visibility.Visible;
        public string Info
        {
            get
            {
                var urls = Entry.EffectiveUrls;
                var desc = Comp.Description(Entry.Component);
                return (string.IsNullOrEmpty(desc) ? "" : desc + "\n\n") + "Kaynak: " + (urls != null && urls.Count > 0 ? urls[0] : "—");
            }
        }
        public string Badge => Active ? "AKTİF" : Installed ? "KURULU" : "";
        public string BadgeKind => Active ? "active" : Installed ? "installed" : "";
        public Visibility BadgeVis => string.IsNullOrEmpty(Badge) ? Visibility.Collapsed : Visibility.Visible;
        /// <summary>Bileşenin birden çok seçeneği varsa (tek sürümlü araçlarda rozet kalabalık yapmasın).</summary>
        public bool MultiChoice { get; set; }
        public Visibility RecVis => Entry.Recommended && !Active && MultiChoice ? Visibility.Visible : Visibility.Collapsed;
        public Visibility InstallVis => Installed ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ReinstallVis => Installed && !Activatable ? Visibility.Visible : Visibility.Collapsed;
        public bool Activatable => new[] { Comp.Php, Comp.Apache, Comp.Nginx, Comp.MariaDb, Comp.MySql, Comp.PostgreSql, Comp.Node }.Contains(Entry.Component);
        public Visibility ActivateVis => Installed && !Active && Activatable ? Visibility.Visible : Visibility.Collapsed;
        /// <summary>Aktif PostgreSQL de silinebilir (isteğe bağlı bileşen; silinince servisi kaldırılır).</summary>
        public Visibility RemoveVis => Installed && (!Active || Entry.Component == Comp.PostgreSql)
            && (Activatable || Entry.Component == Comp.PhpMyAdmin || Entry.Component == Comp.Adminer || Entry.Component == Comp.SqlSrv) ? Visibility.Visible : Visibility.Collapsed;
    }

    public partial class ComponentListView : UserControl
    {
        public string[] Components { get; set; } = new string[0];
        public string Description { get; set; } = "";
        public bool AllowCustom { get; set; }

        public ComponentListView() { InitializeComponent(); }

        private MainWindow Main => MainWindow.Instance;
        private AppConfig Cfg => AppConfig.Current;

        public void Reload()
        {
            Desc.Text = Description;
            CustomRow.Visibility = AllowCustom ? Visibility.Visible : Visibility.Collapsed;
            if (AllowCustom)
            {
                var items = Components.Where(c => c == Comp.Php || c == Comp.MariaDb || c == Comp.MySql || c == Comp.PostgreSql || c == Comp.Node || c == Comp.Nginx).ToList();
                CustomComp.ItemsSource = items.Select(c => new ComboBoxItem { Content = Comp.Title(c), Tag = c }).ToList();
                if (CustomComp.Items.Count > 0 && CustomComp.SelectedIndex < 0) CustomComp.SelectedIndex = 0;
            }
            var rows = new List<VersionRow>();
            var cat = Catalog.Current;
            var unavailable = Components.Where(c => !cat.AvailableFor(c).Any() && cat.For(c).Any()).Select(Comp.Title).ToList();
            Unavailable.Visibility = unavailable.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Unavailable.Text = unavailable.Count > 0 ? string.Join(", ", unavailable) + " bu Windows (" + SystemInfo.ArchTitle + ") için yayınlanmıyor." : "";
            foreach (var comp in Components)
            {
                var entries = cat.AvailableFor(comp).ToList();
                // katalogda olmayan ama kurulu sürümleri de göster
                foreach (var v in InstalledVersions(comp))
                    if (!entries.Any(e => e.Version == v))
                        entries.Add(Catalog.Custom(comp, v) ?? new CatalogEntry { Component = comp, Version = v, Note = "Kurulu" });
                bool multi = entries.Count > 1;
                foreach (var e in entries.OrderByDescending(e => Catalog.VersionKey(e.Version)))
                    rows.Add(new VersionRow { Entry = e, Installed = Installer.IsInstalled(e), Active = Installer.IsActive(e, Cfg), MultiChoice = multi });
            }
            List.ItemsSource = new ObservableCollection<VersionRow>(rows);
        }

        private static IEnumerable<string> InstalledVersions(string comp)
        {
            switch (comp)
            {
                case Comp.Php: return PhpManager.InstalledVersions();
                case Comp.Apache: return ApacheManager.Installed();
                case Comp.Nginx: return NginxManager.Installed();
                case Comp.MariaDb: return DbManager.Installed(Comp.MariaDb);
                case Comp.MySql: return DbManager.Installed(Comp.MySql);
                case Comp.PostgreSql: return PostgreSqlManager.Installed();
                case Comp.Node: return NodeManager.Installed();
                default: return new string[0];
            }
        }

        private static VersionRow RowOf(object sender) => (sender as FrameworkElement)?.DataContext as VersionRow;

        // ------------------------------------------------------------------
        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            var row = RowOf(sender);
            if (row == null) return;
            if (row.Installed && !UI.Confirm(row.Title + " zaten kurulu. Yeniden indirip üzerine kurulsun mu? (php.ini korunmaz, veri dizinine dokunulmaz)")) return;
            await InstallEntries(new[] { row.Entry });
        }

        public async Task InstallEntries(IEnumerable<CatalogEntry> entries)
        {
            var list = entries.ToList();
            bool needVc = list.Any(x => x.Component == Comp.Php || x.Component == Comp.Apache || x.Component == Comp.MariaDb || x.Component == Comp.MySql || x.Component == Comp.PostgreSql || x.Component == Comp.SqlSrv) && !VcRedist.IsAdequate();
            if (needVc)
            {
                var vc = Catalog.Current.Recommended(Comp.VcRedist);
                if (vc != null && UI.Confirm("PHP/Apache/MariaDB için gerekli Visual C++ Runtime bulunamadı ya da eski. Önce otomatik kurulsun mu? (önerilir)")) list.Insert(0, vc);
            }
            if (list.Any(x => x.Component == Comp.SqlSrv) && !SqlSrvManager.OdbcInstalled)
            {
                SqlSrvManager.OdbcLicenseAccepted = UI.Confirm(
                    "PHP'nin SQL Server'a bağlanabilmesi için Microsoft ODBC Driver 18 de kurulacak.\n\n" +
                    "Bu sürücü Microsoft'un lisans koşullarıyla dağıtılır (https://aka.ms/odbc18eula).\n\n" +
                    "Lisans koşullarını kabul edip sürücüyü kurmak istiyor musunuz?\n(Hayır derseniz yalnız PHP eklentileri kopyalanır, bağlantı için sürücüyü sonra kendiniz kurmanız gerekir.)");
            }
            var activatedSomething = false;
            var blocked = new List<CatalogEntry>();
            var blockedMsgs = new List<string>();
            var ok = await Main.RunBusyAsync("Kurulum", async log =>
            {
                var cts = new CancellationTokenSource();
                foreach (var entry in list)
                {
                    log("İndiriliyor: " + entry.Title);
                    var progress = new Progress<DownloadProgress>(p =>
                    {
                        Main.SetBusyProgress(p.Percent, p.Indeterminate);
                        if (!string.IsNullOrEmpty(p.Status)) log(p.Status);
                    });
                    try { await Installer.InstallAsync(entry, Cfg, progress, cts.Token); }
                    catch (ComponentBlockedException bex) { blocked.Add(entry); blockedMsgs.Add(bex.Message); }
                    Main.SetBusyProgress(0, true);
                }
            });
            if (blockedMsgs.Count > 0)
                UI.Warn(string.Join("\n\n", blockedMsgs) + (SystemCheck.SmartAppControlOn ? "\n\n" + SystemCheck.SmartAppControlHelp : ""));
            if (!ok) { Reload(); return; }

            // yeni kurulan aktif edilebilir bir şeyse sor
            foreach (var entry in list)
            {
                if (blocked.Contains(entry)) continue;
                if (Installer.IsActive(entry, Cfg)) { activatedSomething = true; continue; }
                if (new[] { Comp.Php, Comp.MariaDb, Comp.MySql, Comp.PostgreSql, Comp.Node }.Contains(entry.Component) && Installer.IsInstalled(entry))
                {
                    if (UI.Confirm(entry.Title + " kuruldu. Şimdi aktif edilsin mi?")) { await Activate(entry, false); activatedSomething = true; }
                }
            }
            // Her kurulumdan sonra yapılandırmayı uygula: yeniden kurulumda durdurulan servisler tekrar başlar
            await Main.ApplyConfigAsync(true, activatedSomething ? "Yeni sürüm uygulanıyor" : "Yapılandırma uygulanıyor");
            Reload();
        }

        private async void Activate_Click(object sender, RoutedEventArgs e)
        {
            var row = RowOf(sender);
            if (row == null) return;
            await Activate(row.Entry, true);
            Reload();
        }

        public async Task Activate(CatalogEntry entry, bool apply)
        {
            var cfg = Cfg;
            switch (entry.Component)
            {
                case Comp.Php: cfg.PhpVersion = entry.Version; break;
                case Comp.Node: cfg.NodeVersion = entry.Version; break;
                case Comp.Apache: cfg.WebServer = "apache"; break;
                case Comp.Nginx: cfg.WebServer = "nginx"; break;
                case Comp.MariaDb:
                case Comp.MySql:
                    if (cfg.DbEngine != entry.Component || Catalog.MajorMinor(cfg.DbVersion) != Catalog.MajorMinor(entry.Version))
                    {
                        var msg = $"Her veritabanı sürümünün KENDİ veri dizini vardır:\n  {DbManager.DataDir(entry.Component, entry.Version)}\n\n" +
                                  "Mevcut veritabanlarınız bu yeni sürümde görünmez (silinmez, eski klasörde kalır). Taşımak için Ayarlar > Veritabanı > Yedek Al / Geri Yükle kullanın.\n\nDevam edilsin mi?";
                        if (!UI.Confirm(msg)) return;
                    }
                    cfg.DbEngine = entry.Component; cfg.DbVersion = entry.Version; break;
                case Comp.PostgreSql:
                    if (!string.IsNullOrEmpty(cfg.PgVersion) && PostgreSqlManager.Major(cfg.PgVersion) != PostgreSqlManager.Major(entry.Version))
                    {
                        var msg = $"Her PostgreSQL ana sürümünün KENDİ veri klasörü vardır:\n  {PostgreSqlManager.DataDir(entry.Version)}\n\n" +
                                  "Mevcut veritabanlarınız bu sürümde görünmez (silinmez, eski klasörde kalır). Taşımak için Ayarlar > Veritabanı > PostgreSQL > Yedek al / Geri yükle kullanın.\n\nDevam edilsin mi?";
                        if (!UI.Confirm(msg)) return;
                    }
                    cfg.PgVersion = entry.Version; break;
            }
            cfg.Save();
            if (apply) await Main.ApplyConfigAsync(true, entry.Title + " aktif ediliyor");
        }

        private async void Remove_Click(object sender, RoutedEventArgs e)
        {
            var row = RowOf(sender);
            if (row == null) return;
            if (!UI.Confirm(row.Title + " silinecek. Emin misiniz?")) return;
            var cfg = Cfg;
            if (row.Entry.Component == Comp.PostgreSql && row.Active)
            {
                // aktif PostgreSQL: servisi kaldır, varsa kurulu başka sürüme geç, yoksa PostgreSQL kapalı kalır
                await Task.Run(() => PostgreSqlManager.UninstallService());
                cfg.PgVersion = PostgreSqlManager.Installed().FirstOrDefault(v => v != row.Entry.Version) ?? "";
                cfg.Save();
            }
            var err = await Task.Run(() => Installer.Uninstall(row.Entry, cfg));
            if (err != null) UI.Err(err);
            bool isPg = row.Entry.Component == Comp.PostgreSql;
            if ((row.Entry.Component == Comp.MariaDb || row.Entry.Component == Comp.MySql || isPg) && err == null)
            {
                var data = isPg ? PostgreSqlManager.DataDir(row.Entry.Version) : DbManager.DataDir(row.Entry.Component, row.Entry.Version);
                // aynı ana sürümün başka bir yaması hâlâ kuruluysa (veri klasörü ortak) PostgreSQL verisi sorulmaz
                bool shared = isPg && PostgreSqlManager.Installed().Any(v => PostgreSqlManager.Major(v) == PostgreSqlManager.Major(row.Entry.Version));
                if (!shared && System.IO.Directory.Exists(data) && UI.Confirm("Bu sürümün veri dizini de silinsin mi?\n" + data + "\n\n(İçindeki tüm veritabanları kalıcı olarak silinir!)"))
                {
                    var e2 = Installer.DeleteDataDir(row.Entry.Component, row.Entry.Version);
                    if (e2 != null) UI.Err(e2);
                }
            }
            Reload();
            // aktif PostgreSQL silindiyse: kalan sürümle servisi yeniden kur (yoksa PostgreSQL kapalı kalır)
            if (isPg && row.Active && !string.IsNullOrEmpty(cfg.PgVersion)) await Main.ApplyConfigAsync(true, "PostgreSQL " + cfg.PgVersion + " aktif ediliyor");
            // web sunucu yapılandırmasında yeri olan araçlar silindiyse (Alias /phpmyadmin, /adminer, sqlsrv eklentisi) yapılandırmayı yenile
            else if (err == null && (row.Entry.Component == Comp.PhpMyAdmin || row.Entry.Component == Comp.Adminer || row.Entry.Component == Comp.SqlSrv))
                await Main.ApplyConfigAsync(true, "Yapılandırma güncelleniyor");
        }

        private async void Custom_Click(object sender, RoutedEventArgs e)
        {
            var comp = (CustomComp.SelectedItem as ComboBoxItem)?.Tag as string;
            var ver = (CustomVersion.Text ?? "").Trim();
            if (comp == null || string.IsNullOrEmpty(ver)) { UI.Warn("Bileşen seçip sürüm numarası yazın (örn. 8.3.30)."); return; }
            if (!System.Text.RegularExpressions.Regex.IsMatch(ver, @"^\d+\.\d+(\.\d+)?$")) { UI.Warn("Sürüm biçimi: 8.3.30"); return; }
            var entry = Catalog.Custom(comp, ver);
            if (entry == null) { UI.Warn("Bu bileşen için elle sürüm eklenemez."); return; }
            if (Catalog.Current.Find(comp, ver) == null) { Catalog.Current.Entries.Add(entry); Catalog.Current.Save(); }
            CustomVersion.Text = "";
            await InstallEntries(new[] { entry });
        }
    }
}
