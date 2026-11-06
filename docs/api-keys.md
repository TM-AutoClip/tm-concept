# Getting API keys

**English** · [Türkçe](api-keys.tr.md)

TM Concept uses online models with **your own keys**. You pay the provider directly. Keys are stored only in the encrypted database on
your computer, are sent only to their own provider and never written to logs. They go into a backup only when you tick *API keys* in
Settings → Backup (the backup file is encrypted with your backup password).

In TM Concept: Settings → **Keys** → choose the provider → paste the key → **Save and test the connection**. The test is a free request
(a model or key listing). A provider can have several keys (for example "personal" and "work"). You can also add a key when a page
needs it: pressing Generate or Produce with a model of a provider that has no key opens a window for it; the key is tested and kept only
when it works.

API use is billed separately from chat subscriptions (Claude Pro, ChatGPT Plus…): a subscription does not include API credit.

## Anthropic (Claude)

1. Open **platform.claude.com** and create an account or sign in.
2. **Billing** → add a payment method and buy credit (a small amount, for example 5–10 $, is enough to start).
3. **Settings → API keys → Create key**; give it a name (for example "tm-concept").
4. Copy the key right away (it is shown once) and paste it into TM Concept.
5. Recommended models: Claude Sonnet 5.5 (concepts and video code), Claude Haiku 5.5 (cheap steps). Data: API data is kept 30 days and not
   used for training (provider terms).

## OpenAI

1. Open **platform.openai.com** and sign in.
2. **Billing** → add a payment method and credit.
3. **API keys → Create new secret key**.
4. Copy the key and paste it into TM Concept.
5. Used for: chat models, online narration (gpt-4o-mini-tts), images, embeddings for the semantic duplicate check.

## OpenRouter

1. Open **openrouter.ai** and sign in.
2. **Credits** → buy credit (a small fee is added when buying).
3. **Settings → Keys → Create key**.
4. Copy the key and paste it into TM Concept.
5. One key for many model families. OpenRouter reports the exact cost of every call; TM Concept shows that value.

## Z.ai (GLM)

1. Open **z.ai** and sign in.
2. Add balance to the API account.
3. **API Keys** → create a key.
4. Copy the key and paste it into TM Concept.
5. Cheap GLM models for concepts and image prompts; GLM-Image for images.

## Mistral

1. Open **console.mistral.ai** and sign in.
2. Choose a plan and add a payment method.
3. **API Keys → Create new key**.
4. Copy the key and paste it into TM Concept.

## DeepSeek

1. Open **platform.deepseek.com** and sign in.
2. Top up the balance.
3. **API keys → Create API key**.
4. Copy the key and paste it into TM Concept.
5. Note: the terms do not say whether API data is used for training.

## fal (images)

1. Open **fal.ai** and sign in.
2. **Billing** → add credit.
3. **Dashboard → Keys → Add key**.
4. Copy the key and paste it into TM Concept.
5. fal has no free test request: the key is saved untested and checked at its first image. Prices are per image (FLUX 2 Flash ≈ 0.01 $).

## After adding a key

Settings → **Models**: choose a model for every step you use (concepts, video code, image prompt, image, online narration, semantic
duplicate check, describe a sample image). Nothing is chosen automatically; the "Recommended" badge shows good choices. A price that
changed can be corrected on the same page.
