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

## Anahtarı ekledikten sonra

Ayarlar → **Modeller**: kullandığın her adım için bir model seç (konsept, video kodu, görsel istemi, görsel, online seslendirme, anlamsal
tekrar denetimi, örnek görsel açıklama). Hiçbiri kendiliğinden seçilmez; "Önerilen" rozeti iyi seçenekleri gösterir. Değişen bir fiyat
aynı sayfadan düzeltilir.
