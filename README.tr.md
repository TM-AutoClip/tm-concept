<p align="center"><img src="assets/brand/tm-concept-logo-horizontal-dark.svg" alt="TM Concept" width="420"></p>

[English](README.md) · **Türkçe**

TM Concept, bir kanal fikrini video konseptlerine ve kısa videolara dönüştüren ücretsiz, açık kaynak (MIT) bir masaüstü programıdır.
Konseptleri **online yapay zekâ modelleri ve kendi API anahtarlarınla** yazar, video kodunu modele yazdırır, videoyu **kendi
bilgisayarında** render eder (ekran kartı gerekmez) ve **ücretsiz yerel bir sesle** seslendirir. Windows ve Linux'ta çalışır,
veritabanını şifreli tutar.

- [Ne yapar](#ne-yapar)
- [Ne kadar tutar](#ne-kadar-tutar)
- [Windows'a kurulum](#windowsa-kurulum)
- [Linux'a kurulum (x64 ve arm64)](#linuxa-kurulum-x64-ve-arm64)
- [İlk açılış](#ilk-açılış)
- [Araçlar: Node.js, ffmpeg, motorlar, ses](#araçlar-nodejs-ffmpeg-motorlar-ses)
- [API anahtarları](#api-anahtarları)
- [İlk videonu üret](#ilk-videonu-üret)
- [Güncelleme](#güncelleme)
- [Kaldırma](#kaldırma)
- [Verin nerede](#verin-nerede)
- [Bilmen gereken lisanslar](#bilmen-gereken-lisanslar)
- [Bilinen kısıtlar](#bilinen-kısıtlar)
- [Kaynaktan derleme](#kaynaktan-derleme)

## Ne yapar

- **Profil = kanal.** Kanal adı, içerik dili (Türkçe ya da İngilizce), dikey kısa ya da yatay video, hedef süre (15–180 sn), kanalın
  konsepti, isteğe bağlı örnek görseller ve seslendirme sesi. Dört hazır şablon alanları doldurur (tarih: Bizans, Osmanlı; teknoloji:
  antik, modern).
- **Konsept üret.** Tek basışta 1–100 konsept, 20'şerlik turlarla. Basmadan önce tahmini ücret aralığı, sonra sağlayıcının bildirdiği
  kullanımdan kesin ücret görünür. Tekrar dört katmanda önlenir: istemde bütün konseptlerin geçmişi (önbellekli), ücretsiz yerel benzerlik
  denetimi, gömme modeliyle isteğe bağlı anlamsal denetim ve liste büyüyünce sıkıştırılmış geçmiş. ✓ (A tuşu) onaylar, ✗ (D tuşu) siler.
  Silinenler **Silinenler** sayfasında süresiz durur (tekrar denetiminde sayılmaya devam eder); oradan kalıcı silinebilir.
- **Konseptten video.** Onaylı konseptleri sıraya al, motoru (varsayılan HyperFrames, Remotion, Revideo), kod modelini, görselleri (yok,
  örnek görsellerin ya da bir görsel modeli), seslendirmeyi, altyazıyı ve konuşma metni dosyasını seç, **Sıradakileri sırayla üret**'e bas.
  Model tek bir kod dosyası yazar; hiçbir şey çalıştırılmadan önce güvenlik denetimi yapılır; denetim ya da render hatasında tek onarım
  istenir. Videolar en çok 3 dakikadır.
- **Üretilen videolar.** Küçük resim, program içi oynatıcı (oynat/duraklat, sarma çubuğu, süre, ses düzeyi, tam ekran; kendiliğinden
  başlamaz), ayrıntılar (motor, modeller, adım adım ücret, ses notları, konsept metni), sistem oynatıcısında aç, yeniden üret, klasörü aç, kaydı sil ve
  istersen dosyaları geri dönüşüm kutusuna taşı.
- **Bildirimler** sağ altta (konseptler hazır, video hazır, hata). Varsayılan 1 dakika; Ayarlar'dan süresi değişir ya da kapatılır.
  Alttaki işlem günlüğü her şeyi tutar (sarı uyarı, kırmızı hata).
- **Şifreli.** Veritabanı (API anahtarların dahil) SQLite3 Multiple Ciphers ile şifrelidir. Anahtarı Windows (DPAPI), Linux anahtarlığı ya
  da bir ana parola korur. Günlük yedekler (son 10) parolalı dosyalardır (Argon2id + AES-256-GCM); API anahtarları yedeğe asla girmez.
- **İki arayüz dili:** Türkçe ve İngilizce.

Başka kanalları **okumaz**, referans video kullanmaz, ses klonlamaz.

## Ne kadar tutar

Program ücretsizdir. Yapay zekâ sağlayıcılarına yalnız kullandığın kadar doğrudan ödersin; program önce tahmini, sonra kesin ücreti
gösterir. Kabaca (5 Kas 2026 fiyatları; Ayarlar → Modeller'de güncelini gör):

| Ne | Yaklaşık ücret |
|---|---|
| Claude Sonnet 5.5 ile 20 konsept | 0,15–0,25 $ |
| Claude Sonnet 5.5 ile 60–90 sn'lik bir videonun kodu | 0,10 $ |
| Yerel Supertonic 3 sesiyle seslendirme | ücretsiz |
| Video başına 8 YZ görseli (fal FLUX 2 Flash) | 0,08 $ |

## Windows'a kurulum

Windows 10 ya da 11, 64 bit.

1. [Son sürümü](../../releases/latest) aç ve `TMConcept-<sürüm>-win-x64.exe` dosyasını indir.
2. İsteğe bağlı ama önerilir: dosyayı sürüm sayfasındaki SHA-256 değeriyle karşılaştır. PowerShell'de:
   ```powershell
   Get-FileHash .\TMConcept-0.1.3-win-x64.exe -Algorithm SHA256
   ```
3. Dosyayı kalacağı yere koy, örneğin `%LOCALAPPDATA%\Programs\TM Concept\TMConcept.exe`.
4. Çift tıkla. Dosya kod imzalı olmadığı için Windows **"Windows bilgisayarınızı korudu"** (SmartScreen) der: **Ek bilgi**'ye, sonra
   **Yine de çalıştır**'a tıkla. Windows genelde yalnız ilk seferde sorar.
5. [İlk açılış](#ilk-açılış) ile devam et.

Yönetici yetkisi gerekmez; sisteme bir şey kurulmaz, sistem PATH'i değişmez.

**OneDrive notu:** videolar `Belgeler\TM Concept` altına kaydedilir. OneDrive Belgeler klasörünü yedekliyorsa her video buluta da
yüklenir. İstemiyorsan klasörü OneDrive ayarlarından hariç tut.

## Linux'a kurulum (x64 ve arm64)

Sıradan bilgisayarlarda `linux-x64`, 64 bit ARM bilgisayarlarda (ör. 64 bit masaüstlü Raspberry Pi 4 ya da 5) `linux-arm64` kullan.
Masaüstü oturumu gerekir.

1. [Son sürümden](../../releases/latest) `tm-concept-<sürüm>-linux-x64.tar.gz` (ya da `-linux-arm64`) indir.
2. İsteğe bağlı: SHA-256 değerini denetle: `sha256sum tm-concept-0.1.3-linux-x64.tar.gz`
3. Kalacağı yere aç:
   ```sh
   mkdir -p ~/.local/opt
   tar -xzf tm-concept-0.1.3-linux-x64.tar.gz -C ~/.local/opt
   ~/.local/opt/tm-concept/install-desktop-entry.sh   # isteğe bağlı: TM Concept'i uygulama menüsüne ekler
   ```
4. **Anahtarlık (önerilir).** Veritabanının kendiliğinden açılması için anahtar `secret-tool` ile masaüstü anahtarlığına konur:
   Debian / Ubuntu / Raspberry Pi OS: `sudo apt install libsecret-tools` · Fedora: `sudo dnf install libsecret` · Arch:
   `sudo pacman -S libsecret`. Anahtarlık yoksa program her açılışta ana parola sorar.
5. **Chrome kitaplıkları.** Video motorları başsız bir Chrome çalıştırır. Eksik kitaplık varsa Ayarlar → Araçlar tam
   `sudo apt install …` komutunu gösterir (program kök yetkisi kullanmaz). Tipik liste: `libnss3 libatk-bridge2.0-0 libcups2 libdrm2
   libxkbcommon0 libxcomposite1 libxdamage1 libxrandr2 libgbm1 libpango-1.0-0 libasound2`.
6. **ARM64:** Puppeteer'ın ARM64 Chrome'u yok. Remotion'u kur (kendi ARM64 tarayıcısını getirir) ya da sistem Chromium'unu kur
   (`sudo apt install chromium`); HyperFrames ve Revideo onu kullanır.
7. `~/.local/opt/tm-concept/tm-concept` ile (ya da menüden) başlat.

## İlk açılış

1. **Dil:** iki büyük düğme, Türkçe ve English (sistem dilin önde). Sonra Ayarlar'dan değişir.
2. **Yeni başla ya da yedekten yükle:** yalnız veri yokken görünür. *Yedekten yükle* bir `.tmcbak` dosyası ve parolasını ister.
3. **Koruma ve yedek parolası:** veritabanı anahtarını Windows / anahtarlık ya da bir ana parola korur. **Yedek parolası** zorunludur
   (en az 12 karakter): günlük yedekler onunla şifrelenir. Bir yere yaz; parolasız yedek açılamaz.
4. **İlk profil:** bir şablon seç ya da kanalını doldur.

## Araçlar: Node.js, ffmpeg, motorlar, ses

Ayarlar → **Araçlar**. Her kalem, kaynağını ve boyutunu gösteren onay penceresinden sonra resmî kaynağından indirilir ve sabitlenmiş
SHA-256 ile denetlenir (npm paketleri: kilit dosyasının bütünlük değerleri). Hepsi programın kendi veri klasörüne gider.

1. **Node.js** (≈ 35 MB) — motorları çalıştırır.
2. **ffmpeg** (≈ 130–200 MB, GPL derlemesi, ayrı bir program) — sesi ve videoyu birleştirir, altyazıyı yakar, küçük resim çıkarır,
   videoları program içinde oynatır. Linux'ta dağıtımın ffmpeg'i varsa o kullanılır. Sabitlenen indirme bir gün kaldırılırsa ya da SHA-256'sı
   tutmazsa PATH'teki ffmpeg kullanılır (uyarıyla); o da yoksa program nasıl kurulacağını gösterir.
3. **Bir motor:** HyperFrames (varsayılan) ve/veya Remotion (önce lisansını kabul etmeni ister) ve/veya Revideo (deneysel). Motor paketleri
   ve Chrome Headless Shell birkaç yüz MB tutar. **Örnek render** motoru modelsiz ve ağsız dener.
4. **Ses:** Supertonic 3 (≈ 130 MB; önce OpenRAIL-M şartlarını gösterir; Türkçe ve İngilizce), kitaplıklarıyla birlikte: sherpa-onnx
   (≈ 8–13 MB; yalnız ses konuşurken ayrı bir süreçte çalışır). Ses yoksa videolar sessiz üretilir ve
   günlüğe sarı uyarı düşer.

Bir motor ve sesle toplam disk: yaklaşık 0,6–1 GB.

Pencereyi varsayılan olarak işlemci çizer (görünüm aynı, yaklaşık 50–70 MB daha az bellek). Kaydırma ya da oynatıcı yavaş gelirse
Ayarlar → **Genel** → **Pencereyi ekran kartıyla çiz** seçeneğini aç ve programı yeniden başlat.

## API anahtarları

Ayarlar → **Anahtarlar**: sağlayıcıyı seç, anahtarı yapıştır, **Kaydet ve bağlantıyı dene**'ye bas (ücretsiz bir istek; fal'da ücretsiz
deneme yok). Anahtarlar şifreli veritabanında durur, yalnız kendi sağlayıcısına gider, günlüğe yazılmaz, yedeğe girmez. Her sağlayıcı
için adım adım rehber: [docs/api-keys.tr.md](docs/api-keys.tr.md).

Sonra Ayarlar → **Modeller**: her adım için bir model seç. Senin yerine seçilmez; rozet önerilenleri gösterir.

## İlk videonu üret

1. **Konsept Üret:** modeli, adedi ve isteğe bağlı ek istemi seç; tahmini gör; **Üret**'e bas.
2. Kartları aç, iyileri onayla (A), kalanı sil (D).
3. **Konseptten Video:** onaylı konseptlere tıklayıp sıraya al, ayarlara bak, **Sıradakileri sırayla üret**'e bas.
4. **Üretilen Videolar:** program içinde izle (Linux'ta ses PulseAudio ya da PipeWire ile), klasörü aç. Video klasöründe `video.mp4`, `subtitles.srt`, `narration.txt` ve `thumbnail.jpg` bulunur.

## Güncelleme

Yeni sürümü indir, program dosyasını değiştir (Windows: `.exe`; Linux: eski klasörün üstüne aç). Verin, araçların ve videoların yerinde
kalır. Motor paketleri yalnız yeni sürüm başka sürümleri sabitlediyse yeniden kurulur (Ayarlar → Araçlar gösterir).

## Kaldırma

1. Program dosyasını (Windows) ya da `tm-concept` klasörünü (Linux; ve `~/.local/share/applications/tm-concept.desktop`) sil.
2. Bütün veriyi de silmek için veri klasörünü (aşağıda) sil. `Belgeler/TM Concept` altındaki videolar senindir; istersen ayrıca sil.
3. Linux: veritabanı anahtarı anahtarlığında "tm-concept" adıyla kalabilir; anahtarlık aracınla sil.

## Verin nerede

| Ne | Windows | Linux | Şifreli |
|---|---|---|---|
| Veritabanı (profiller, konseptler, anahtarlar) | `%LOCALAPPDATA%\TM Concept\concept.db` | `~/.local/share/tm-concept/concept.db` | evet |
| Yedekler (son 10, günlük) | `…\TM Concept\backups\*.tmcbak` | `~/.local/share/tm-concept/backups/` | evet (parola) |
| Günlükler (anahtarsız, kişisel yolsuz) | `…\TM Concept\logs\` | `~/.local/share/tm-concept/logs/` | hayır |
| Araçlar (Node, ffmpeg, motorlar, sesler) | `…\TM Concept\tools\` | `~/.local/share/tm-concept/tools/` | hayır |
| Videolar ve örnek görseller | `Belgeler\TM Concept\` | `~/Documents/TM Concept/` (XDG belgeler klasörü) | hayır |

Taşınabilir kip: programı `--data-dir <klasör>` ile başlat; videolar dahil her şey o klasörün altında kalır.

## Bilmen gereken lisanslar

- **Supertonic 3** (varsayılan ses): OpenRAIL-M. Yayınlarken sesin yapay zekâ ile üretildiğini belirt; kimseyi taklit etme. Video
  ayrıntısı bunu hatırlatır.
- **Remotion**: açık kaynak değil; bireyler ve en çok 3 kişilik şirketler ücretsiz, daha büyük şirketler Remotion lisansı almalı.
- **Örnek görseller**: kullanım hakkı sende olmalı.
- **ffmpeg**: GPL, ayrı bir program olarak indirilir.

Ayrıntı: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Bilinen kısıtlar

- Videolar en çok 3 dakika; uzun yatay video henüz yok.
- Program kod imzalı değil (Windows'ta SmartScreen uyarısı).
- Revideo deneysel. Linux ARM64'te HyperFrames ve Revideo için harici Chromium gerekir.
- Otomatik testlerde gerçek sağlayıcı isteği yoktur (sahte HTTP); ilk gerçek kullanım senin bilgisayarında olur.

## Kaynaktan derleme

.NET 10 SDK. `dotnet build TMConcept.slnx` ve `dotnet test TMConcept.slnx`. Yayın dosyaları: `tools/publish.ps1` (Windows) ya da
`tools/publish.sh`. İlk commit'ten önce `tools/install-hooks.ps1` (ya da `.sh`) çalıştır: pre-commit kancası anahtarları, jetonları ve
kişisel verileri durdurur.
