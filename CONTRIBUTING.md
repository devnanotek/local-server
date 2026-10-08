# Katkıda bulunma

Katkılarınızı bekliyoruz: hata bildirimi, öneri, kod veya kılavuz düzeltmesi. Türkçe veya İngilizce yazabilirsiniz.

## Hata ve öneri
- [Issues](../../issues) sekmesinden **Hata bildirimi** veya **Öneri** şablonunu seçin.
- Hata bildirirken sürüm numarasını (sol alt köşe) ve program günlüğünü ekleyin: **Yardım → Program günlüğü**.

## Geliştirme

| Gerekli | Not |
|---|---|
| Windows 10/11 | Program WPF, .NET Framework 4.8 |
| [.NET SDK 8+](https://dotnet.microsoft.com/download) | `winget install Microsoft.DotNet.SDK.8` |
| Visual Studio 2022, Rider veya VS Code | isteğe bağlı |

```bash
# yönetici izni istemeyen test sürümü (servis kurmaz; arayüz ve yapılandırma üretimi denenebilir)
dotnet build src/DevNanotek/DevNanotek.csproj -c Release -p:AsInvoker=true

# dağıtım sürümü → dist\DevNanotek.exe
powershell -ExecutionPolicy Bypass -File build.ps1
```

Test kökünü değiştirmek için `DEVNANOTEK_HOME` ortam değişkenini kullanın. Böylece gerçek `C:\devnanotek` klasörüne dokunulmaz.

### Kod düzeni
- `src/DevNanotek/Core/`: servisler, yapılandırma, indirme, sürüm kataloğu.
- `src/DevNanotek/Views/`: WPF sayfaları.
- `src/DevNanotek/Resources/`: yapılandırma şablonları, açılış sayfası ve kılavuz.
- `linux/devnanotek.sh`: Linux sürümü. LF satır sonu zorunludur; `bash -n` ve `shellcheck` ile denetlenir.
- `site/`: GitHub Pages sitesi.

### Kurallar
- Arayüz metinleri kaynakta Türkçe yazılır, kod yorumları Türkçedir.
- **İngilizce arayüz:** Program Türkçe metni ekranda gösterirken `Core/Lang/En.*.cs` sözlüğünden çevirir (`L.T`). Yeni bir metin eklediğinizde İngilizcesini ilgili sözlük dosyasına `"Türkçe", "English",` çifti olarak ekleyin. Değişken içeren metinler için `{0}`, `{1}` kalıpları kullanılır: `"PHP {0} kuruldu.", "PHP {0} installed.",`.
  - Denetim: `dotnet run --project tools/i18n-check -- check src/DevNanotek tools/i18n-check/ignore.txt` — eksik çeviri varsa listeler; GitHub'daki derleme de bu denetimi yapar.
  - Kullanıcı verisi gösteren alanlar (klasör adı, veritabanı adı) çevrilmemeli: XAML'de `local:Localizer.Skip="True"`.
  - İngilizce kılavuz: `src/DevNanotek/Resources/Guide.html` (Türkçesi `Kilavuz.html`).
- Windows'ta değişiklik yapan her şey (servis, hosts, PATH, güvenlik duvarı) kaldırma sırasında geri alınmalıdır (`Uninstaller.cs`).

## Sürüm yayınlama (bakımcılar)
1. `CHANGELOG.md` dosyasına yeni bölümü ekleyin: `## [1.3.0] - YYYY-AA-GG`.
2. Etiketi gönderin: `git tag v1.3.0 && git push origin v1.3.0`. Alternatif: **Actions → Sürüm yayınla → Run workflow**.
3. GitHub Actions şunları yükler:
   - o sürüm numarasıyla derlenmiş `DevNanotek.exe`
   - `devnanotek.sh` ve kılavuz
   - SHA256 özetleri

   Kurulu programlar yeni sürümü kendiliğinden görür ve tek tıkla güncellenir.

Katkılarınız [MIT lisansı](LICENSE) ile yayınlanır.
