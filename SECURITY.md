# Güvenlik

DEVNANOTEK bir **yerel geliştirme ortamıdır**; internete açık sunucularda kullanılmak için tasarlanmamıştır.

- Siteler, phpMyAdmin ve Adminer varsayılan olarak **yalnız bu bilgisayardan** açılır.
- Veritabanı `root` / `postgres` kullanıcıları yerel kullanımda şifresizdir.
- Yerel ağ erişimini açarsanız (Ayarlar → Genel) mutlaka bir veritabanı şifresi belirleyin.

## Güvenlik açığı bildirme
Bir güvenlik açığı bulduysanız lütfen herkese açık bir issue açmayın. GitHub'daki **Security → Report a vulnerability** (özel bildirim) bağlantısını kullanın ya da [devnanotek.net](https://devnanotek.net) üzerinden ulaşın.

## İndirmelerin doğrulanması
Her sürümde `SHA256SUMS.txt` yayınlanır. Windows'ta indirdiğiniz dosyayı şöyle doğrulayabilirsiniz:

```bash
certutil -hashfile DevNanotek.exe SHA256
```
