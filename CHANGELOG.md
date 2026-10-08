# Sürüm geçmişi

Bu dosyadaki her bölüm, GitHub'da aynı numaralı sürümün notları olarak yayınlanır.
Sürüm numaraları [Anlamsal Sürümleme](https://semver.org/lang/tr/) kurallarına uyar: `ANA.KÜÇÜK.YAMA`.

## [1.3.0] - 2026-10-08

### Eklenenler
- **İngilizce arayüz (English interface):** Program ilk açılışta Windows'un görüntü diline göre başlar: Windows Türkçe ise Türkçe, başka bir dildeyse İngilizce.
  - **Ayarlar → Genel → Dil / Language** ile istediğiniz zaman değişir (Cihazın dili / Türkçe / English). Program kapanıp yeni dille yeniden açılır, servisler durmaz.
  - Kurulum sihirbazının sağ üstünde de dil seçimi var.
  - Bildirimler, iletişim kutuları, tepsi menüsü, günlük kayıtları, komut satırı çıktısı ve Linux uyumluluk raporu da seçilen dilde.
  - `http://localhost` açılış sayfası programın diliyle açılır.
  - İngilizce kullanım kılavuzu: `docs\Guide.html` (Yardım → Tam kılavuz dile göre açılır).
- Yeni `custom.conf` / `custom.cnf` dosyalarının açıklama satırları ve kısayol adları da dile göre yazılır.

### Düzeltilenler
- Kurulum sihirbazının alt çubuğunda uzun metin düğmelerin üstüne binmiyor.
- Linux uyumluluk denetiminde "Bağlantı" kategorisinin adı "Dosya bağlantısı" oldu (veritabanı bağlantısıyla karışmasın).
- Otomatik alan adı uyarısındaki menü yolu düzeltildi (Ayarlar > SSL ve alan adları).

## [1.2.0] - 2026-10-08

### Eklenenler
- **PostgreSQL 14–18:** MariaDB/MySQL'in yanında ayrı servis olarak çalışır.
  - Kullanıcı `postgres`, bu bilgisayardan şifre gerekmez.
  - Yedek alma ve geri yükleme, `psql` terminali ve `custom.conf` desteği var.
- **SQLite:** PHP'de hazırdır.
- **SQL Server:** PHP sürücüsü (`sqlsrv`, `pdo_sqlsrv`) ve Microsoft ODBC Driver 18 kurulur.
- **Adminer:** tüm veritabanları için tek sayfalık yönetici (`/adminer`); tek tıkla, şifresiz yerel giriş.
- **PHP eklentilerine hızlı erişim:** gd, zip, intl ve 25 eklenti daha, Genel Bakış'tan ve Ayarlar → PHP → Hızlı ayarlar'dan anahtarla açılıp kapatılır.
- **Linux uyumluluk denetimi:** Proje Linux sunucuya (Plesk, cPanel, VPS) gitmeden önce taranır. Bulunanlar:
  - büyük/küçük harf uyumsuz yollar
  - ters bölü (`\`) ve `C:\` yolları
  - karışık harfli tablo adları
  - `.htaccess`'teki `php_value` satırları
  - CRLF satır sonlu betikler
- **Linux sürümü (`devnanotek.sh`):** Ubuntu, Debian, Linux Mint, AlmaLinux, Rocky, RHEL, CentOS Stream ve Fedora'ya aynı ortamı kurar.
- **Program güncellemesi:** Yeni DEVNANOTEK sürümü GitHub'dan denetlenir ve tek tıkla güncellenir.
- Açık kaynak (MIT); GitHub Actions ile otomatik derleme, sürüm yayınlama ve proje sitesi.

### Değişenler
- Yeni localhost açılış sayfası:
  - logo çizimi (yazı tipi gerekmez) ve tarayıcı sekmesi simgesi
  - veritabanı, PostgreSQL ve Mailpit durumu
- Program güncellenince eski DEVNANOTEK açılış sayfası yenisiyle değiştirilir; eskisi `.yedek` adıyla saklanır.
- Kılavuz yeni özelliklerle genişletildi; başlığı yeni logoyla düzeltildi.

### Düzeltilenler
- "Mailpit" başlığının Türkçe büyük harf kuralıyla "MAİLPİT" görünmesi düzeltildi.
- Veritabanı kapalıyken yanlış "root şifresi" uyarısı çıkması düzeltildi.
- Onarım/Sıfırlama ve kaldırma PostgreSQL'i de kapsıyor: servis, yedek ve kısayol bağlantısı.
- Araç silinince web sunucu yapılandırması yenileniyor.
- Port kontrolü PostgreSQL portunu da denetliyor.

## [1.1.0] - 2026-10-07

### Eklenenler
- DEVNANOTEK marka arayüzü: gündüz / gece / sistem teması, açıklamalar "?" simgelerinde, renkli bildirimler.
- Bileşen sürümleri resmi kaynaklardan otomatik denetlenir.
  - Her bileşende **ÖNERİLEN** sürüm işaretlenir.
  - Aynı daldaki yamalar tek tıkla güncellenir.
- 64 bit, 32 bit ve ARM64 Windows'u otomatik algılama.
- İsteğe bağlı Windows Defender istisnası; phpMyAdmin'in önceden ısıtılması.

### Değişenler
- Web sunucu varsayılan olarak yalnız bu bilgisayardan erişilebilir; yerel ağ erişimi isteğe bağlı.
- Kaldırma ek olarak güvenlik duvarı kurallarını, tepsi kaydını ve Defender istisnasını da temizler.

## [1.0.0] - 2026-10-07

### İlk sürüm
- Birden çok PHP sürümü (7.4–8.5), Apache veya Nginx, MariaDB veya MySQL.
- Diğer araçlar: phpMyAdmin, Mailpit, mkcert (yerel HTTPS), Node.js, Composer.
- Servisler gerçek Windows servisi olarak çalışır.
- Çalışma modu: "her zaman açık" veya "sadece ben açınca".
- Dört seviyeli onarım ve sıfırlama; Denetim Masası'ndan kaldırma; tepsi simgesi ve durum penceresi.
- `C:\devnanotek\httpdocs\GITHUB\proje1` → `http://localhost/GITHUB/proje1/`
