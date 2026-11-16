# API anahtarı alma

[English](api-keys.md) · **Türkçe**

TM Concept online modelleri **kendi anahtarlarınla** kullanır. Ücreti sağlayıcıya doğrudan ödersin. Anahtarlar yalnız bilgisayarındaki
şifreli veritabanında durur, yalnız kendi sağlayıcısına gönderilir ve günlüğe yazılmaz. Yedeğe yalnız Ayarlar → Yedek altında
*API anahtarları*'nı işaretlersen girer (yedek dosyası yedek parolanla şifrelidir).

TM Concept'te: Ayarlar → **Anahtarlar** → sağlayıcıyı seç → anahtarı yapıştır → **Kaydet ve bağlantıyı dene**. Deneme ücretsiz bir
istektir (model ya da anahtar listesi). Bir sağlayıcıya birden çok anahtar eklenebilir (ör. "kişisel" ve "iş"). Anahtarı sayfa
istediğinde de ekleyebilirsin: anahtarı olmayan bir sağlayıcının modeliyle Üret'e ya da Kuyruğu üret'e basınca bir pencere açılır;
anahtar denenir ve yalnız çalışırsa saklanır.

API kullanımı sohbet aboneliklerinden (Claude Pro, ChatGPT Plus…) ayrı ücretlenir: abonelik API kredisi içermez.

## Anthropic (Claude)

1. **platform.claude.com**'u aç, hesap oluştur ya da giriş yap.
2. **Billing** → ödeme yöntemi ekle ve kredi al (başlamak için 5–10 $ gibi küçük bir tutar yeter).
3. **Settings → API keys → Create key**; bir ad ver (ör. "tm-concept").
4. Anahtarı hemen kopyala (bir kez gösterilir) ve TM Concept'e yapıştır.
5. Önerilen modeller: Claude Sonnet 5.5 (konsept ve video kodu), Claude Haiku 5.5 (ucuz adımlar). Veri: API verisi 30 gün tutulur,
   eğitimde kullanılmaz (sağlayıcı şartları).

## OpenAI

1. **platform.openai.com**'u aç ve giriş yap.
2. **Billing** → ödeme yöntemi ve kredi ekle.
3. **API keys → Create new secret key**.
4. Anahtarı kopyala ve TM Concept'e yapıştır.
5. Kullanıldığı yerler: sohbet modelleri, online seslendirme (gpt-4o-mini-tts), görseller, anlamsal tekrar denetimi için gömme.

## OpenRouter

1. **openrouter.ai**'yi aç ve giriş yap.
2. **Credits** → kredi al (alırken küçük bir ücret eklenir).
3. **Settings → Keys → Create key**.
4. Anahtarı kopyala ve TM Concept'e yapıştır.
5. Tek anahtarla birçok model ailesi. OpenRouter her çağrının kesin ücretini bildirir; TM Concept o değeri gösterir.

## Z.ai (GLM)

1. **z.ai**'yi aç ve giriş yap.
2. API hesabına bakiye ekle.
3. **API Keys** → anahtar oluştur.
4. Anahtarı kopyala ve TM Concept'e yapıştır.
5. Konsept ve görsel istemi için ucuz GLM modelleri; görsel için GLM-Image.

## Mistral

1. **console.mistral.ai**'yi aç ve giriş yap.
2. Bir plan seç ve ödeme yöntemi ekle.
3. **API Keys → Create new key**.
4. Anahtarı kopyala ve TM Concept'e yapıştır.

## DeepSeek

1. **platform.deepseek.com**'u aç ve giriş yap.
2. Bakiye yükle.
3. **API keys → Create API key**.
4. Anahtarı kopyala ve TM Concept'e yapıştır.
5. Not: şartlarda API verisinin eğitimde kullanılıp kullanılmadığı yazmıyor.

## fal (görseller)

1. **fal.ai**'yi aç ve giriş yap.
2. **Billing** → kredi ekle.
3. **Dashboard → Keys → Add key**.
4. Anahtarı kopyala ve TM Concept'e yapıştır.
5. fal'da ücretsiz deneme isteği yok: anahtar denenmeden kaydedilir, ilk görselde denetlenir. Fiyat görsel başınadır (FLUX 2 Flash ≈ 0,01 $).

## Özel sunucu (OpenAI uyumlu)

OpenAI API'siyle konuşan her sunucu her metin adımında kullanılabilir: konu önerileri, konseptler, bir konseptin yapay zekâ
düzenlemeleri, video kodu, görsel istemleri ve model görsel okuyorsa örnek görsel açıklama. Sunucu kendi bilgisayarında, ağındaki başka
bir bilgisayarda ya da internette (kendi sunucun ya da OpenAI uyumlu adres veren bir hizmet) olabilir. API anahtarı isteğe bağlıdır:
yalnız sunucu istiyorsa yaz. Program bu modellere ücret yazmaz; uzak bir hizmet ücret alıyorsa harcamayı o hizmetin panelinden izle.

1. Sunucuyu başlat ve bir model yükle. Sık kullanılan varsayılan adresler:

   | Sunucu | Adres |
   |---|---|
   | Lemonade Server (AMD; llama.cpp'yi ROCm ya da Vulkan ile çalıştırır) | `http://localhost:13305/api/v1` (eski sürümler: 8000 portu) |
   | Ollama | `http://localhost:11434/v1` |
   | LM Studio | `http://localhost:1234/v1` |
   | llama.cpp `llama-server` | `http://localhost:8080/v1` (yeni sürümler: 9931 portu) |
   | vLLM | `http://localhost:8000/v1` |
   | Ağındaki başka bir bilgisayar | `http://192.168.1.20:8080/v1` |
   | Uzak sunucu ya da hizmet | `https://sunucun.ornek.com/v1` + API anahtarı |

   AMD ekran kartları: Lemonade Server ya da llama.cpp'nin ROCm veya Vulkan sürümleri modelleri bu kartlarda çalıştırır (NVIDIA kart
   gerekmez).
2. Ayarlar → **Anahtarlar** → **Özel sunucu (OpenAI uyumlu)**'yu seç → bir ad ve adresi yaz (`/v1` ya da `/api/v1`'e kadar, http ya da
   https); API anahtarını yalnız sunucu isterse yaz.
3. **Bağlan ve modelleri listele** sunucudan modellerini ister. Kullanacaklarını işaretle (ya da model kimliğini yaz). Her biri için
   **Bağlam**'a (sunucunun modeli çalıştırdığı bağlam boyutu, llama-server'da `-c`; sunucu söylüyorsa oradan okunur) ve **En uzun
   yanıt**'a (daha fazlasını isteyen adım bunu alır) bak. Video kodu yanıtı on binlerce token tutar, bu yüzden modele geniş bir bağlam
   ver: En uzun yanıt, bağlamdan istem payı düşülerek başlar ve hiçbir çağrı isteminin yanında bağlamda kalandan fazlasını istemez. Görsel okuyan model için **Görsel okur**'u, **JSON şeması**'nı ise yalnız sunucu
   `response_format: json_schema` kabul ediyorsa işaretle (LM Studio, vLLM, yeni llama.cpp); yoksa JSON kipi istenir, ikisini de
   reddeden sunucuya istek onsuz gönderilir.
4. **Özel sunucuyu kaydet**. Modelleri artık model listelerinde "model · ad · özel sunucu, ücret yazılmaz" olarak görünür.
   Kayıtlı sunucudaki **Düzenle** onu yerinde değiştirir (modellerini kullanan adımlar onları korur). Adrese kullanıcı adı ya da parola
   yazma; gizli bilginin yeri API anahtarı alanı.

Model yüklendikten sonraki ilk istek dakikalar sürebilir: TM Concept ilk cevabı bir saate kadar bekler ve sunucu 503 (model yükleniyor)
dedikçe birkaç kez yeniden dener. Modelin düşünmesi (`reasoning_content`, `<think>…</think>`) "düşünüyor" olarak gösterilir, cevap
sayılmaz. Küçük yerel modeller büyük çevrim içi modellerden daha zayıf video kodu yazar; önce konu önerileri ve konseptlerle dene.

Sunucu listesi ayarlarla birlikte yedeğe girer; sunucunun jetonu yalnız *API anahtarları*'nı işaretlersen. Bir sunucuyu silmek, onu
kullanan adımları sen başka bir model seçene kadar modelsiz bırakır.

## Anahtarı ekledikten sonra

Ayarlar → **Modeller**: kullandığın her adım için bir model seç (konsept, video kodu, görsel istemi, görsel, online seslendirme, anlamsal
tekrar denetimi, örnek görsel açıklama). Hiçbiri kendiliğinden seçilmez; "Önerilen" rozeti iyi seçenekleri gösterir. Değişen bir fiyat
aynı sayfadan düzeltilir.
