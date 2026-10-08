namespace DevNanotek.Core
{
    // İngilizce sözlük: Yardım (sık sorulanlar) ve Günlükler sayfaları.
    public static partial class L
    {
        private static readonly string[] EnHelp =
        {
            // ---- Yardım ----
            "Tam kılavuz", "Full guide",
            "En sık sorulanlar. Ayrıntılı anlatım için Tam kılavuz.", "The most frequently asked questions. See the Full guide for details.",
            "Linux sürümünü al", "Get the Linux version",
            "Linux'ta da kullanmak ister misiniz?", "Want to use it on Linux too?",
            "Ubuntu, Debian, Linux Mint, AlmaLinux, Rocky, RHEL ve Fedora için aynı ortamı kuran devnanotek.sh betiği. Dosyayı Linux bilgisayara kopyalayıp 'sudo bash devnanotek.sh install' çalıştırın.",
                "The devnanotek.sh script sets up the same stack on Ubuntu, Debian, Linux Mint, AlmaLinux, Rocky, RHEL and Fedora. Copy the file to the Linux computer and run 'sudo bash devnanotek.sh install'.",
            "Kılavuz klasörü", "Guide folder",
            "Program günlüğü", "App log",
            "GitHub (açık kaynak)", "GitHub (open source)",
            "Kaynak kod, sürümler ve hata bildirimi", "Source code, releases and bug reports",
            "Hata bildir / öneri", "Report a bug / suggest",
            "Proje sitesi", "Project site",
            "YENİ SÜRÜM: {0} (Sürümler sayfasından güncelleyin)", "NEW VERSION: {0} (update it from the Versions page)",
            "güncel", "up to date",
            "DEVNANOTEK Local Server v{0}{1} · MIT lisanslı açık kaynak · {2} · Kök: {3}", "DEVNANOTEK Local Server v{0}{1} · open source under the MIT license · {2} · Root: {3}",
            "Linux dosyaları yazılamadı (ayrıntı: program günlüğü).", "Couldn't write the Linux files (details: app log).",

            // ---- sık sorulanlar ----
            "Projemi nereye koyarım?", "Where do I put my project?",
            "C:\\devnanotek\\httpdocs içine istediğiniz klasörü açın. httpdocs\\GITHUB\\proje1 → http://localhost/GITHUB/proje1/ adresinden açılır. İç içe klasörler sınırsızdır, .htaccess çalışır.",
                "Create any folder inside C:\\devnanotek\\httpdocs. httpdocs\\GITHUB\\project1 opens at http://localhost/GITHUB/project1/. You can nest folders as deep as you like, and .htaccess works.",
            "Veritabanına nasıl bağlanırım?", "How do I connect to the database?",
            "Sunucu 127.0.0.1 · Port 3306 · Kullanıcı root · Şifre boş. phpMyAdmin: http://localhost/phpmyadmin. Navicat / HeidiSQL / DBeaver'da aynı bilgilerle MySQL bağlantısı oluşturun.",
                "Host 127.0.0.1 · Port 3306 · User root · Empty password. phpMyAdmin: http://localhost/phpmyadmin. In Navicat / HeidiSQL / DBeaver, create a MySQL connection with the same details.",
            "Sürüm nasıl değiştirilir veya güncellenir?", "How do I change or update a version?",
            "Sürümler sayfasında istediğiniz sürümü kurup 'Aktif yap'a tıklayın. Yeni sürümler 12 saatte bir otomatik denetlenir; güncelleme varsa menüde sayı rozeti çıkar ve tek tıkla güncellenir (php.ini ayarlarınız taşınır).",
                "Install the version you want on the Versions page and click 'Activate'. New versions are checked automatically every 12 hours; when an update is available, a number badge appears in the menu and you can update with one click (your php.ini settings are carried over).",
            "Bilgisayar açılınca çalışsın mı?", "Should it run when the computer starts?",
            "Genel Bakış'taki 'Bilgisayar açılınca başlat' anahtarı: Açık → servisler Windows ile başlar. Kapalı → yalnız siz programı açınca çalışır, tepsiden Çıkış'ta durur.",
                "Use the 'Start with the computer' switch on the Overview page: On → services start with Windows. Off → they run only when you open the app and stop when you choose Exit from the tray.",
            "E-posta testi", "Testing email",
            "PHP mail() ve SMTP 127.0.0.1:1025 ile gönderilen e-postalar gerçek alıcıya gitmez; http://localhost:8025 adresindeki Mailpit kutusunda görünür. Şifreleme yok, kullanıcı/şifre gerekmez.",
                "Emails sent with PHP mail() or SMTP 127.0.0.1:1025 never reach real recipients; they appear in the Mailpit inbox at http://localhost:8025. No encryption, no user name or password needed.",
            "Genel Bakış → Terminal: node, npm, npx, php, composer hazırdır. Uygulamanızı kendi portunda (ör. 3000) çalıştırın. Apache üzerinden yayınlamak için Ayarlar → Web sunucu → custom.conf'taki ProxyPass örneğini kullanın.",
                "Overview → Terminal: node, npm, npx, php and composer are ready. Run your app on its own port (e.g. 3000). To serve it through Apache, use the ProxyPass example in Settings → Web server → custom.conf.",
            "Bir şey bozuldu, çalışmıyor", "Something broke and doesn't work",
            "1) Genel Bakış'ta kırmızı servisin günlük düğmesine bakın. 2) Ayarlar → Sistem → Port kontrolü. 3) Onar / Sıfırla → Hızlı onarım; olmazsa Tam onarım. Projeleriniz hiçbir durumda silinmez.",
                "1) On the Overview page, check the log button of the red service. 2) Settings → System → Port check. 3) Repair / Reset → Quick repair; if that doesn't help, Full repair. Your projects are never deleted.",
            "phpMyAdmin ilk açılışta neden yavaş?", "Why is phpMyAdmin slow the first time?",
            "İlk açılışta PHP binlerce dosyayı derleyip önbelleğe alır ve Windows Defender bu dosyaları tarar. DEVNANOTEK servisler başlayınca sayfayı arka planda önceden açar; ayrıca Ayarlar → Sistem → Hız ile Defender taramasından hariç tutabilirsiniz.",
                "On first load PHP compiles and caches thousands of files, and Windows Defender scans them. DEVNANOTEK warms the page up in the background when services start; you can also exclude it from Defender scanning in Settings → System → Speed.",
            "gd, zip, intl gibi PHP eklentilerini nasıl açarım?", "How do I turn on PHP extensions such as gd, zip or intl?",
            "Genel Bakış → Aktif sürümler → 'Eklentiler (gd, zip…)' ya da Ayarlar → PHP → Hızlı ayarlar. Anahtarı açıp 'Kaydet ve uygula'ya tıklayın; web sunucu kendiliğinden yeniden başlar.",
                "Overview → Active versions → 'Extensions (gd, zip…)', or Settings → PHP → Quick settings. Turn the switch on and click 'Save and apply'; the web server restarts by itself.",
            "PostgreSQL, SQLite veya SQL Server kullanabilir miyim?", "Can I use PostgreSQL, SQLite or SQL Server?",
            "Evet. PostgreSQL: Sürümler → Veritabanı → PostgreSQL → Kur (MariaDB/MySQL ile birlikte çalışır, kullanıcı postgres, şifresiz). SQLite: kurulum gerekmez, PHP'de hazır. SQL Server: Sürümler → Araçlar → SQL Server sürücüsü. Hepsini http://localhost/adminer ile yönetebilirsiniz.",
                "Yes. PostgreSQL: Versions → Database → PostgreSQL → Install (runs alongside MariaDB/MySQL, user postgres, no password). SQLite: nothing to install, it's built into PHP. SQL Server: Versions → Tools → SQL Server driver. You can manage them all at http://localhost/adminer.",
            "Projem Linux sunucuda (Plesk / cPanel) çalışacak mı?", "Will my project work on a Linux server (Plesk / cPanel)?",
            "Projeler → klasörü seçin → Linux uyumluluğu → Denetle. Büyük/küçük harf uyumsuz dosya yolları, ters bölü, C:\\ yolları, karışık harfli tablo adları ve .htaccess'teki php_value gibi sunucuda hata verecek şeyleri satır satır gösterir.",
                "Projects → select the folder → Linux compatibility → Check. It lists, line by line, what would fail on the server: file paths with the wrong letter case, backslashes, C:\\ paths, mixed-case table names, php_value in .htaccess and more.",

            // ---- Günlükler ----
            "Bir şey çalışmıyorsa önce buraya bakın. Seçilen dosyanın son 400 satırı gösterilir.", "If something isn't working, look here first. The last 400 lines of the selected file are shown.",
            "Canlı", "Live",
            "3 saniyede bir yenile", "Refresh every 3 seconds",
            "Temizle", "Clear",
            "Günlük klasörü", "Log folder",
            "DEVNANOTEK (program)", "DEVNANOTEK (app)",
            "Apache — hata", "Apache — errors",
            "Apache — erişim", "Apache — access",
            "Nginx — hata", "Nginx — errors",
            "Nginx — erişim", "Nginx — access",
            "PHP — hata (php_error.log)", "PHP — errors (php_error.log)",
            "Veritabanı — hata", "Database — errors",
            "PostgreSQL — son günlük", "PostgreSQL — latest log",
            "(dosya henüz yok: {0})", "(file doesn't exist yet: {0})",
            "{0} içeriği silinecek. Devam?", "The contents of {0} will be deleted. Continue?",
            "Temizlenemedi (dosya kullanımda olabilir): {0}", "Couldn't clear it (the file may be in use): {0}",
        };
    }
}
