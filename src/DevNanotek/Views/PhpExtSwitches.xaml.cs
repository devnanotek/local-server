using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DevNanotek.Core;

namespace DevNanotek.Views
{
    /// <summary>Sık kullanılan PHP eklentileri için açma/kapama anahtarları (gd, zip, intl…).</summary>
    public partial class PhpExtSwitches : UserControl
    {
        public static readonly DependencyProperty ItemWidthProperty =
            DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(PhpExtSwitches), new PropertyMetadata(190.0));

        public double ItemWidth { get => (double)GetValue(ItemWidthProperty); set => SetValue(ItemWidthProperty, value); }

        public string Version { get; private set; }
        public List<PhpExtension> List { get; private set; } = new List<PhpExtension>();

        public PhpExtSwitches() { InitializeComponent(); }

        public void Load(string phpVersion)
        {
            Version = phpVersion;
            List = PhpManager.IsInstalled(phpVersion) ? PhpManager.PopularFor(phpVersion) : new List<PhpExtension>();
            Items.ItemsSource = null;
            Items.ItemsSource = List;
        }

        public List<PhpExtension> Changed => List.Where(x => x.Enabled != x.Original).ToList();

        /// <summary>Değişen eklentileri verilen php.ini metnine uygular (yazmaz).</summary>
        public string ApplyTo(string ini)
        {
            var changed = Changed;
            foreach (var x in changed) ini = PhpManager.SetExtensionText(ini, x.Name, x.Enabled, x.IsZend);
            if (changed.Count > 0) Logger.Info($"PHP {Version} eklentileri: " + string.Join(", ", changed.Select(x => (x.Enabled ? "+" : "-") + x.Name)));
            foreach (var x in changed) x.Original = x.Enabled;
            return ini;
        }

        /// <summary>Değişiklikleri php.ini'ye yazar (yedekli). Dönüş: değişen eklenti sayısı.</summary>
        public int Save()
        {
            int n = Changed.Count;
            if (n == 0 || !PhpManager.IsInstalled(Version)) return 0;
            PhpManager.WriteIni(Version, ApplyTo(PhpManager.ReadIni(Version)));
            return n;
        }
    }
}
