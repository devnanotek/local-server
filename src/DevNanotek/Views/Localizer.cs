using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using DevNanotek.Core;

namespace DevNanotek.Views
{
    /// <summary>
    /// İngilizce arayüz: XAML'deki ve koddan atanan Türkçe metinleri ekranda gösterilirken çevirir.
    /// Yazının çizildiği son öğede çalışır (TextBlock, Run, AccessText, pencere başlığı); düğme, sekme, ipucu ve
    /// liste içerikleri de sonunda bir TextBlock'ta gösterildiği için hepsi kapsanır. Kod içindeki değerler
    /// (Content, Tag, seçili öğe) Türkçe kalır, böylece program mantığı değişmez.
    /// Değer sonradan değişirse (durum yazısı, veri bağlama) öğeye bağlı "ayna" özelliği değişikliği yakalar ve yeniden çevirir.
    /// </summary>
    public static class Localizer
    {
        /// <summary>Bu öğe ve altındakiler çevrilmesin (ör. kullanıcı verisi, dosya içeriği).</summary>
        public static readonly DependencyProperty SkipProperty = DependencyProperty.RegisterAttached(
            "Skip", typeof(bool), typeof(Localizer), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
        public static void SetSkip(DependencyObject d, bool v) => d.SetValue(SkipProperty, v);
        public static bool GetSkip(DependencyObject d) => (bool)d.GetValue(SkipProperty);

        // Ayna: öğenin kendi özelliğine bağlanır, değer değişince geri çağırma çalışır (dış başvuru yok, sızıntı olmaz)
        private static readonly DependencyProperty MirrorProperty = DependencyProperty.RegisterAttached(
            "Mirror", typeof(object), typeof(Localizer), new PropertyMetadata(null, OnMirrorChanged));
        private static readonly DependencyProperty BusyProperty = DependencyProperty.RegisterAttached(
            "Busy", typeof(bool), typeof(Localizer), new PropertyMetadata(false));

        private static bool _installed;

        /// <summary>Uygulama açılışında bir kez çağrılır (yalnız İngilizce arayüzde bir şey yapar).</summary>
        public static void Install()
        {
            if (_installed || !L.En) return;
            _installed = true;
            // Not: Loaded olayı sınıf işleyicilerine her öğe için gönderilmez (WPF yalnız örnek işleyicisi olan öğelere yayınlar).
            // SizeChanged ise her öğe ilk kez yerleştirildiğinde (çizimden önce) koşulsuz gelir; gizli öğeler görününce çevrilir.
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(OnSized), true);
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded), true);
        }

        // öğe bir kez işlendi mi (her boyut değişiminde yeniden taranmasın)
        private static readonly DependencyProperty DoneProperty = DependencyProperty.RegisterAttached(
            "Done", typeof(bool), typeof(Localizer), new PropertyMetadata(false));

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window w && !GetSkip(w)) Hook(w, Window.TitleProperty);
        }

        private static void OnSized(object sender, SizeChangedEventArgs e)
        {
            if (sender is Window || !(sender is TextBlock || sender is AccessText || sender is ContentControl || sender is HeaderedItemsControl)) return;
            var d = (DependencyObject)sender;
            if ((bool)d.GetValue(DoneProperty)) return;
            d.SetValue(DoneProperty, true);
            Process(sender);
        }

        private static void Process(object sender)
        {
            switch (sender)
            {
                case TextBlock tb:
                    if (GetSkip(tb)) return;
                    // metin Text özelliğinden geliyorsa (yerel, bağlama, şablon) onu; <Run>/<Bold> gibi satır içi öğelerden geliyorsa onları çevir
                    // (satır içi öğeler korunur: kod x:Name ile bir Run'ı sonradan güncelleyebilir)
                    if (tb.Inlines.Count == 0 || DependencyPropertyHelper.GetValueSource(tb, TextBlock.TextProperty).BaseValueSource != BaseValueSource.Default)
                        Hook(tb, TextBlock.TextProperty);
                    else
                        HookInlines(tb.Inlines);
                    break;
                case AccessText at:
                    if (!GetSkip(at)) Hook(at, AccessText.TextProperty);
                    break;
                // düğme, sekme, onay kutusu, menü: ekranda görünen yazı yukarıdaki TextBlock'ta çevrilir; ekran okuyucunun
                // okuduğu ad (UI Automation) ise Content / Header değerinden gelir — onu da İngilizce yap (değerin kendisi değişmez)
                case HeaderedContentControl hcc:
                    if (!GetSkip(hcc)) HookName(hcc, HeaderedContentControl.HeaderProperty);
                    break;
                case ContentControl cc:
                    if (!GetSkip(cc)) HookName(cc, ContentControl.ContentProperty);
                    break;
                case HeaderedItemsControl hic:
                    if (!GetSkip(hic)) HookName(hic, HeaderedItemsControl.HeaderProperty);
                    break;
            }
        }

        private static readonly DependencyProperty NameModeProperty = DependencyProperty.RegisterAttached(
            "NameMode", typeof(bool), typeof(Localizer), new PropertyMetadata(false));

        private static void HookName(DependencyObject d, DependencyProperty dp)
        {
            if (d.ReadLocalValue(AutomationProperties.NameProperty) != DependencyProperty.UnsetValue) return; // XAML'de elle verilmiş ad
            d.SetValue(NameModeProperty, true);
            Hook(d, dp);
        }

        private static void HookInlines(InlineCollection inlines)
        {
            // önce kopyala: Run metni değişince satır içi koleksiyon da değişir
            foreach (var inline in new System.Collections.Generic.List<Inline>(inlines))
            {
                if (inline is Run run) Hook(run, Run.TextProperty);
                else if (inline is Span span) HookInlines(span.Inlines);
            }
        }

        private static void Hook(DependencyObject d, DependencyProperty dp)
        {
            Translate(d, dp);
            if (BindingOperations.GetBindingExpression(d, MirrorProperty) != null) return;
            BindingOperations.SetBinding(d, MirrorProperty, new Binding { Source = d, Path = new PropertyPath(dp), Mode = BindingMode.OneWay });
            d.SetValue(TargetProperty, dp);
        }

        private static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
            "Target", typeof(DependencyProperty), typeof(Localizer), new PropertyMetadata(null));

        private static void OnMirrorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d.GetValue(TargetProperty) is DependencyProperty dp) Translate(d, dp);
        }

        private static void Translate(DependencyObject d, DependencyProperty dp)
        {
            if (!L.En || (bool)d.GetValue(BusyProperty)) return;
            if ((bool)d.GetValue(NameModeProperty))
            {
                // yalnız erişilebilir ad: Content / Header Türkçe kalır, program mantığı etkilenmez
                if (d.GetValue(dp) is string v && v.Length > 0) { var tv = L.T(v); if (tv != v) AutomationProperties.SetName(d, tv); else d.ClearValue(AutomationProperties.NameProperty); }
                else d.ClearValue(AutomationProperties.NameProperty);
                return;
            }
            if (!(d.GetValue(dp) is string s) || s.Length == 0) return;
            var t = L.T(s);
            if (ReferenceEquals(t, s) || t == s) return;
            d.SetValue(BusyProperty, true);
            try { d.SetCurrentValue(dp, t); } // SetCurrentValue: veri bağlaması ve stil tetikleyicileri bozulmaz
            finally { d.SetValue(BusyProperty, false); }
        }
    }
}
