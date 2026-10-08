# DEVNANOTEK Local Server — Linux sürümü

Windows sürümündeki ortamın aynısını Linux'ta kurar: çoklu PHP sürümü, Apache veya Nginx, MariaDB veya MySQL, isteğe bağlı PostgreSQL, phpMyAdmin, Adminer, Mailpit (test e-posta), Node.js, Composer ve yerel HTTPS (mkcert).

## Desteklenen dağıtımlar

| Aile | Dağıtımlar | PHP sürümleri nereden |
|---|---|---|
| Debian / Ubuntu | Ubuntu 22.04, 24.04, 26.04 · Debian 11, 12, 13 · Linux Mint 21–22 · Pop!_OS · Zorin | Ubuntu: ppa:ondrej/php · Debian: packages.sury.org |
| Red Hat | AlmaLinux, Rocky, RHEL, CentOS Stream, Oracle Linux, CloudLinux (8, 9, 10) · Fedora | Remi deposu |

64 bit Intel/AMD (x86_64) ve ARM64 (aarch64) desteklenir. Plesk ve cPanel'in çalıştığı dağıtımların tamamı listededir.

Arch, openSUSE ve Alpine henüz desteklenmiyor; betik bu sistemlerde açık bir mesajla durur.

## Kurulum

En kısa yol: normal kullanıcınızla bir terminal açıp şu komutu çalıştırın. Komut, betiğin en yeni sürümünü GitHub'dan indirir ve kurulumu başlatır:

```bash
curl -fsSLO https://github.com/devnanotek/local-server/releases/latest/download/devnanotek.sh && sudo bash devnanotek.sh install
```

`curl` yoksa `wget https://github.com/devnanotek/local-server/releases/latest/download/devnanotek.sh` ile indirebilirsiniz.

İnternetten indirmek istemezseniz:

1. `devnanotek.sh` dosyasını Linux bilgisayara kopyalayın. Windows'taki DEVNANOTEK'te **Yardım → Linux sürümünü al** ile alabilirsiniz.
2. Normal kullanıcınızla bir terminal açıp şunu çalıştırın:

   ```bash
   sudo bash devnanotek.sh install
   ```

Sihirbazda sırayla PHP sürümünü, web sunucuyu, veritabanını, araçları ve çalışma modunu seçin.

Soru sormadan kurmak için seçenekleri komutta verebilirsiniz:

```bash
sudo bash devnanotek.sh install --php 8.4 --web apache --db mariadb --pg 17 --node 24 -y
```

Kurulumdan sonra `devnanotek` komutu her yerden çalışır.

## Klasörler

| Yol | İçerik |
|---|---|
| `/opt/devnanotek/httpdocs` | **Projeleriniz.** `http://localhost/klasor` adresinden açılır. Ana klasörünüzde `~/httpdocs` kısayolu da oluşturulur. |
| `/opt/devnanotek/backups` | Veritabanı yedekleri |
| `/opt/devnanotek/etc` | DEVNANOTEK ayarları (`php-overrides.ini`, `apache-custom.conf`, `vhosts.list`) |
| `/opt/devnanotek/logs` | `php-error.log` ve DEVNANOTEK günlüğü |

PHP, projelerin sahibi olan sizin kullanıcınızla çalışır. Yüklenen dosyalar size ait olur, `chmod 777` gerekmez.

## Günlük kullanım

```bash
devnanotek                         # menü
devnanotek status                  # servisler: yeşil = çalışıyor, sarı = durdu, kırmızı = hata
sudo devnanotek start              # tümünü başlat
sudo devnanotek stop               # tümünü durdur
```

### PHP

```bash
devnanotek php                     # kurulu ve kurulabilir sürümler
sudo devnanotek php 8.3            # 8.3'ü kur ve geç (web + terminal)
sudo devnanotek ext enable gd      # eklenti aç (gd, zip, intl, imagick, redis…)
sudo devnanotek ext disable xdebug
sudo devnanotek ini memory_limit 1G
```

### Alan adı (proje.test)

```bash
sudo devnanotek vhost add proje1.test GITHUB/proje1
```

Laravel gibi `public/` klasörlü projelerde belge kökü kendiliğinden `public` olur. HTTPS açıksa `https://proje1.test` da çalışır.

### Veritabanı

| | MariaDB / MySQL | PostgreSQL |
|---|---|---|
| Sunucu | 127.0.0.1 | 127.0.0.1 |
| Port | 3306 | 5432 |
| Kullanıcı | root | postgres |
| Şifre | yok (yalnız bu bilgisayardan) | gerekmez (yalnız bu bilgisayardan) |

```bash
sudo devnanotek db backup                 # tüm veritabanları → /opt/devnanotek/backups
sudo devnanotek db restore yedek.sql
sudo devnanotek postgres install 17       # PostgreSQL ekle
```

- **phpMyAdmin:** http://localhost/phpmyadmin
- **Adminer** (tüm veritabanları): http://localhost/adminer

Bilgisayarda veritabanı **önceden kuruluysa** root şifresine dokunulmaz. phpMyAdmin'e kendi kullanıcınızla girersiniz.

`lower_case_table_names` Linux'ta 0'dır, yani tablo adları büyük/küçük harfe duyarlıdır. Bu, canlı sunucuyla birebir aynı davranıştır.

### Çalışma modu

```bash
sudo devnanotek mode always     # bilgisayar açılınca başlar
sudo devnanotek mode manual     # yalnız 'devnanotek start' deyince
```

## Kaldırma

```bash
sudo devnanotek uninstall          # projeler ve yedekler kalır
sudo devnanotek uninstall --all    # projeler dahil her şey (önce yedek önerilir)
```

Kaldırırken şunlar silinir:
- web sunucu yapılandırması
- PHP havuzları
- hosts kayıtları
- yerel HTTPS sertifikası
- Mailpit servisi
- kısayollar

DEVNANOTEK'in **kurduğu** paketler ve eklediği depolar ayrıca sorularak kaldırılır. Sizin önceden kurduğunuz paketlere dokunulmaz.

## Notlar

- **WSL (Windows'ta Ubuntu):** systemd açık olmalıdır. Güncel WSL Ubuntu'da varsayılan olarak açıktır; değilse `/etc/wsl.conf` dosyasına `[boot]` bölümü altında `systemd=true` ekleyin.
- **SELinux (AlmaLinux, Rocky, RHEL, Fedora):** gerekli etiketler ve izinler (`httpd_can_network_connect_db` vb.) kendiliğinden ayarlanır.
- **Güvenlik:** Siteler varsayılan olarak yalnızca bu bilgisayardan açılır (`Require local`). Bu bir geliştirme ortamıdır; internete açık bir sunucuya kurmayın.
- **Günlük:** Ayrıntılar `/opt/devnanotek/logs/devnanotek.log` dosyasındadır.
