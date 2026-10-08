using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DevNanotek.Core;

namespace DevNanotek.Views
{
    public partial class ProjectsView : ViewBase
    {
        private string _selected;

        public ProjectsView()
        {
            InitializeComponent();
            VsCodeBtn.Visibility = UI.FindVsCode() != null ? Visibility.Visible : Visibility.Collapsed;
        }

        public override void Refresh()
        {
            DocrootText.Text = Cfg.EffectiveDocRoot + "  ·  " + UI.LocalhostUrl();
            LoadTree();
            VhostList.ItemsSource = Vhosts.Compute(Cfg);
            UpdateDetail();
        }

        private void LoadTree()
        {
            Tree.Items.Clear();
            var root = Cfg.EffectiveDocRoot;
            Directory.CreateDirectory(root);
            var rootItem = MakeItem(root, "httpdocs");
            rootItem.IsExpanded = true;
            Tree.Items.Add(rootItem);
            if (_selected != null) SelectPath(rootItem, _selected);
        }

        private TreeViewItem MakeItem(string path, string label = null)
        {
            var item = new TreeViewItem { Header = MakeHeader(label ?? Path.GetFileName(path)), Tag = path };
            if (HasSubDirs(path)) item.Items.Add(new TreeViewItem { Header = "…" });
            item.Expanded += Item_Expanded;
            return item;
        }

        /// <summary>Klasör simgesi (altın) + ad.</summary>
        private static object MakeHeader(string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = new TextBlock { Text = "", FontSize = 13, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "FontIcon");
            icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            sp.Children.Add(icon);
            sp.Children.Add(t);
            return sp;
        }

        private static bool HasSubDirs(string path)
        {
            try { return Directory.EnumerateDirectories(path).Any(d => !IsHidden(d)); } catch { return false; }
        }

        private static bool IsHidden(string d)
        {
            var n = Path.GetFileName(d);
            return n.StartsWith(".") || n.Equals("node_modules", StringComparison.OrdinalIgnoreCase) || n.Equals("vendor", StringComparison.OrdinalIgnoreCase);
        }

        private void Item_Expanded(object sender, RoutedEventArgs e)
        {
            var item = (TreeViewItem)sender;
            if (item.Items.Count == 1 && (item.Items[0] as TreeViewItem)?.Tag == null)
            {
                item.Items.Clear();
                try
                {
                    foreach (var d in Directory.GetDirectories((string)item.Tag).Where(d => !IsHidden(d)).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                        item.Items.Add(MakeItem(d));
                }
                catch { }
            }
            e.Handled = true;
        }

        private void SelectPath(TreeViewItem node, string path)
        {
            var nodePath = (string)node.Tag;
            if (string.Equals(nodePath, path, StringComparison.OrdinalIgnoreCase)) { node.IsSelected = true; node.BringIntoView(); return; }
            if (!path.StartsWith(nodePath + "\\", StringComparison.OrdinalIgnoreCase)) return;
            node.IsExpanded = true;
            foreach (TreeViewItem child in node.Items.OfType<TreeViewItem>()) if (child.Tag != null) SelectPath(child, path);
        }

        private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _selected = (e.NewValue as TreeViewItem)?.Tag as string;
            UpdateDetail();
        }

        private string RelUrl(string path)
        {
            var root = Cfg.EffectiveDocRoot.TrimEnd('\\');
            if (string.Equals(path.TrimEnd('\\'), root, StringComparison.OrdinalIgnoreCase)) return UI.LocalhostUrl();
            var rel = path.Substring(root.Length).Trim('\\').Replace('\\', '/');
            return UI.LocalhostUrl() + Uri.EscapeUriString(rel) + "/";
        }

        private void UpdateDetail()
        {
            bool has = _selected != null && Directory.Exists(_selected);
            Detail.IsEnabled = has;
            if (!has) { SelName.Text = L.T("Bir klasör seçin"); SelPath.Text = ""; SelUrl.Text = ""; VhostInfo.Text = ""; return; }
            SelName.Text = Path.GetFileName(_selected.TrimEnd('\\'));
            if (string.IsNullOrEmpty(SelName.Text)) SelName.Text = "httpdocs";
            SelPath.Text = _selected;
            SelUrl.Text = RelUrl(_selected);
            var existing = Cfg.Vhosts.FirstOrDefault(v => string.Equals(v.DocRoot, Vhosts.ResolveDocRoot(_selected), StringComparison.OrdinalIgnoreCase)
                                                        || string.Equals(v.DocRoot, _selected, StringComparison.OrdinalIgnoreCase));
            VhostName.Text = existing?.Host ?? Vhosts.SuggestHost(_selected, Cfg);
            VhostRemoveBtn.Visibility = existing != null ? Visibility.Visible : Visibility.Collapsed;
            var docroot = Vhosts.ResolveDocRoot(_selected);
            VhostInfo.Text = existing != null
                ? $"Tanımlı: http://{existing.Host}/  →  {existing.DocRoot}"
                : "Belge kökü olacak klasör: " + docroot;
        }

        // ---- eylemler ----
        private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
        private void OpenDocroot_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenFolder(Cfg.EffectiveDocRoot);
        private void OpenBrowser_Click(object sender, RoutedEventArgs e) { if (_selected != null) ProcessRunner.OpenUrl(RelUrl(_selected)); }

        private void LinuxCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || !Directory.Exists(_selected)) { UI.Warn("Önce soldan bir proje klasörü seçin."); return; }
            new LinuxCheckWindow(_selected) { Owner = Window.GetWindow(this) }.Show();
        }
        private void OpenFolder_Click(object sender, RoutedEventArgs e) { if (_selected != null) ProcessRunner.OpenFolder(_selected); }
        private void OpenTerminal_Click(object sender, RoutedEventArgs e) { if (_selected != null) UI.OpenTerminal(_selected); }
        private void OpenVsCode_Click(object sender, RoutedEventArgs e)
        {
            var code = UI.FindVsCode();
            if (code != null && _selected != null) ProcessRunner.StartDetached(code, "\"" + _selected + "\"");
        }

        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            var name = (NewName.Text ?? "").Trim().Trim('\\', '/').Replace('/', '\\');
            if (string.IsNullOrEmpty(name)) { UI.Warn("Proje klasörü adı yazın. İç içe klasör için: GITHUB\\proje1"); return; }
            if (name.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || name.Contains("..")) { UI.Warn("Geçersiz ad."); return; }
            var baseDir = _selected != null && Directory.Exists(_selected) ? _selected : Cfg.EffectiveDocRoot;
            var dir = Path.Combine(baseDir, name);
            try
            {
                Directory.CreateDirectory(dir);
                var index = Path.Combine(dir, "index.php");
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    File.WriteAllText(index, "<?php\n// " + name + "\necho '<h1>" + name.Replace("\\", "/").Replace("'", "") + L.Pick(" çalışıyor!", " is working!") + "</h1><p>PHP ' . PHP_VERSION . '</p>';\n", new System.Text.UTF8Encoding(false));
                NewName.Text = "";
                _selected = dir;
                Refresh();
                Logger.Info("Proje oluşturuldu: " + dir);
                UI.Done("Proje oluşturuldu: " + name);
            }
            catch (Exception ex) { UI.Err("Oluşturulamadı: " + ex.Message); }
        }

        private void DeleteFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            if (string.Equals(_selected.TrimEnd('\\'), Cfg.EffectiveDocRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) { UI.Warn("Belge kökü silinemez."); return; }
            if (!UI.Confirm($"\"{_selected}\" klasörü GERİ DÖNÜŞÜM KUTUSUNA gönderilecek. Devam?")) return;
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(_selected, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                var parent = Path.GetDirectoryName(_selected);
                _selected = parent;
                Refresh();
            }
            catch (Exception ex) { UI.Err("Silinemedi: " + ex.Message); }
        }

        // ---- sanal host ----
        private async void VhostAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            var host = (VhostName.Text ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(host) || host.Contains(" ") || host.Contains("/")) { UI.Warn("Geçerli bir alan adı yazın (örn. proje1.test)."); return; }
            if (host == "localhost") { UI.Warn("localhost zaten belge köküne bağlı."); return; }
            if (!host.EndsWith("." + Cfg.VhostTld) && !host.Contains(".")) host += "." + Cfg.VhostTld;
            Cfg.Vhosts.RemoveAll(v => v.Host == host);
            Cfg.Vhosts.Add(new VhostEntry { Host = host, DocRoot = Vhosts.ResolveDocRoot(_selected) });
            Cfg.Save();
            await Main.ApplyConfigAsync(true, $"{host} sanal hostu oluşturuluyor");
            Refresh();
        }

        private async void VhostRemove_Click(object sender, RoutedEventArgs e)
        {
            var host = (VhostName.Text ?? "").Trim().ToLowerInvariant();
            await RemoveVhost(host);
        }

        private async void VhostListRemove_Click(object sender, RoutedEventArgs e) => await RemoveVhost((sender as Button)?.Tag as string);
        private void VhostListOpen_Click(object sender, RoutedEventArgs e)
        {
            var host = (sender as Button)?.Tag as string;
            if (host != null) ProcessRunner.OpenUrl((Cfg.HttpPort == 80 ? "http://" + host + "/" : $"http://{host}:{Cfg.HttpPort}/"));
        }

        private async System.Threading.Tasks.Task RemoveVhost(string host)
        {
            if (string.IsNullOrEmpty(host)) return;
            int n = Cfg.Vhosts.RemoveAll(v => v.Host == host);
            if (n == 0)
            {
                UI.Msg("Bu alan adı otomatik oluşturulmuş (Ayarlar > SSL ve alan adları > Otomatik alan adları). Elle silinemez; otomatik özelliği kapatabilirsiniz.");
                return;
            }
            Cfg.Save();
            await Main.ApplyConfigAsync(false, $"{host} kaldırılıyor");
            Refresh();
        }
    }
}
