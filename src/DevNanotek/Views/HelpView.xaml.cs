using System.Windows;
using DevNanotek.Core;

namespace DevNanotek.Views
{
    public partial class HelpView : ViewBase
    {
        public HelpView()
        {
            InitializeComponent();
            Faq.ItemsSource = new[]
            {
                new { Glyph = "\uE8B7", Q = "Projemi nereye koyarım?", A = "C:\\devnanotek\\httpdocs içine istediğiniz klasörü açın. httpdocs\\GITHUB\\proje1 → http://localhost/GITHUB/proje1/ adresinden açılır. İç içe klasörler sınırsızdır, .htaccess çalışır." },
                new { Glyph = "\uE1D3", Q = "Veritabanına nasıl bağlanırım?", A = "Sunucu 127.0.0.1 · Port 3306 · Kullanıcı root · Şifre boş. phpMyAdmin: http://localhost/phpmyadmin. Navicat / HeidiSQL / DBeaver'da aynı bilgilerle MySQL bağlantısı oluşturun." },
                new { Glyph = "\uE896", Q = "Sürüm nasıl değiştirilir veya güncellenir?", A = "Sürümler sayfasında istediğiniz sürümü kurup 'Aktif yap'a tıklayın. Yeni sürümler 12 saatte bir otomatik denetlenir; güncelleme varsa menüde sayı rozeti çıkar ve tek tıkla güncellenir (php.ini ayarlarınız taşınır)." },
                new { Glyph = "\uE7E8", Q = "Bilgisayar açılınca çalışsın mı?", A = "Genel Bakış'taki 'Bilgisayar açılınca başlat' anahtarı: Açık → servisler Windows ile başlar. Kapalı → yalnız siz programı açınca çalışır, tepsiden Çıkış'ta durur." },
                new { Glyph = "\uE715", Q = "E-posta testi", A = "PHP mail() ve SMTP 127.0.0.1:1025 ile gönderilen e-postalar gerçek alıcıya gitmez; http://localhost:8025 adresindeki Mailpit kutusunda görünür. Şifreleme yok, kullanıcı/şifre gerekmez." },
                new { Glyph = "\uE943", Q = "Node.js / socket.io", A = "Genel Bakış → Terminal: node, npm, npx, php, composer hazırdır. Uygulamanızı kendi portunda (ör. 3000) çalıştırın. Apache üzerinden yayınlamak için Ayarlar → Web sunucu → custom.conf'taki ProxyPass örneğini kullanın." },
                new { Glyph = "\uE90F", Q = "Bir şey bozuldu, çalışmıyor", A = "1) Genel Bakış'ta kırmızı servisin günlük düğmesine bakın. 2) Ayarlar → Sistem → Port kontrolü. 3) Onar / Sıfırla → Hızlı onarım; olmazsa Tam onarım. Projeleriniz hiçbir durumda silinmez." },
                new { Glyph = "\uE774", Q = "phpMyAdmin ilk açılışta neden yavaş?", A = "İlk açılışta PHP binlerce dosyayı derleyip önbelleğe alır ve Windows Defender bu dosyaları tarar. DEVNANOTEK servisler başlayınca sayfayı arka planda önceden açar; ayrıca Ayarlar → Sistem → Hız ile Defender taramasından hariç tutabilirsiniz." },
                new { Glyph = "\uE943", Q = "gd, zip, intl gibi PHP eklentilerini nasıl açarım?", A = "Genel Bakış → Aktif sürümler → 'Eklentiler (gd, zip…)' ya da Ayarlar → PHP → Hızlı ayarlar. Anahtarı açıp 'Kaydet ve uygula'ya tıklayın; web sunucu kendiliğinden yeniden başlar." },
                new { Glyph = "\uE8F1", Q = "PostgreSQL, SQLite veya SQL Server kullanabilir miyim?", A = "Evet. PostgreSQL: Sürümler → Veritabanı → PostgreSQL → Kur (MariaDB/MySQL ile birlikte çalışır, kullanıcı postgres, şifresiz). SQLite: kurulum gerekmez, PHP'de hazır. SQL Server: Sürümler → Araçlar → SQL Server sürücüsü. Hepsini http://localhost/adminer ile yönetebilirsiniz." },
                new { Glyph = "\uE9D5", Q = "Projem Linux sunucuda (Plesk / cPanel) çalışacak mı?", A = "Projeler → klasörü seçin → Linux uyumluluğu → Denetle. Büyük/küçük harf uyumsuz dosya yolları, ters bölü, C:\\ yolları, karışık harfli tablo adları ve .htaccess'teki php_value gibi sunucuda hata verecek şeyleri satır satır gösterir." },
            };
        }

        public override void Refresh()
        {
            var app = AppUpdater.Available;
            var status = app != null ? " · " + L.F("YENİ SÜRÜM: {0} (Sürümler sayfasından güncelleyin)", app.Version.ToString(3))
                       : AppUpdater.CheckedAt != null ? " · " + L.T("güncel") : "";
            VersionText.Text = L.F("DEVNANOTEK Local Server v{0}{1} · MIT lisanslı açık kaynak · {2} · Kök: {3}", App.Version, status, SystemInfo.Summary, Paths.Root);
        }

        private void OpenGitHub_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenUrl(AppInfo.RepoUrl);
        private void OpenIssues_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenUrl(AppInfo.IssuesUrl);
        private void OpenPages_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenUrl(AppInfo.PagesUrl);

        private void OpenGuide_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenUrl(SelfInstall.WriteGuide());
        private void OpenDocsFolder_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenFolder(Paths.Docs);
        private void OpenLog_Click(object sender, RoutedEventArgs e) => ProcessRunner.StartDetached("notepad.exe", "\"" + Paths.AppLog + "\"");
        private void OpenSite_Click(object sender, RoutedEventArgs e) => ProcessRunner.OpenUrl("https://devnanotek.net/");

        private void Linux_Click(object sender, RoutedEventArgs e)
        {
            var dir = SelfInstall.WriteLinuxKit();
            if (dir == null) { UI.Err("Linux dosyaları yazılamadı (ayrıntı: program günlüğü)."); return; }
            ProcessRunner.StartDetached("explorer.exe", "/select,\"" + System.IO.Path.Combine(dir, "devnanotek.sh") + "\"");
        }
    }
}
