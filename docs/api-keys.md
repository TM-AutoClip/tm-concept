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

## Custom server (OpenAI-compatible)

Any server that speaks the OpenAI API can be used in every text step: topic suggestions, concepts, AI edits of a concept, video code,
image prompts, and describing a sample image when the model reads images. It can run on your own computer, on another computer on your
network, or anywhere on the internet (your own server, or a service that offers an OpenAI-compatible address). The API key is optional:
type it only if the server asks for one. The app records no price for these models; if a remote service charges, watch the spending
in its own dashboard.

1. Start the server and load a model. Common default addresses:

   | Server | Address |
   |---|---|
   | Lemonade Server (AMD; runs llama.cpp on ROCm or Vulkan) | `http://localhost:13305/api/v1` (older versions: port 8000) |
   | Ollama | `http://localhost:11434` (found by itself, see below) |
   | LM Studio | `http://localhost:1234` (found by itself, see below) |
   | llama.cpp `llama-server` | `http://localhost:8080/v1` (new builds: port 9931) |
   | vLLM | `http://localhost:8000/v1` |
   | Another computer on your network | `http://192.168.1.20:8080/v1` |
   | A remote server or service | `https://your-server.example.com/v1` + its API key |

   AMD graphics cards: Lemonade Server, or the ROCm or Vulkan builds of llama.cpp, run models on them (no NVIDIA card needed).
2. Settings → **Keys** → choose **Custom server (OpenAI-compatible)** → type a name and the address (up to `/v1` or `/api/v1`, http or
   https); the API key only if the server asks for one.
3. **Connect and list models** asks the server for its models. Tick the ones to use (or type a model id). For each one check **Context**
   (the context size the server runs it with, llama-server's `-c`; read from the server when it says) and **Max answer** (a step that asks
   for more gets this). The video code answer runs to tens of thousands of tokens, so give the model a large context: Max answer starts
   at the context less room for the prompt, and no call asks for more than the context leaves next to its prompt.
   Tick **Reads images** for a vision model and **JSON schema** only if the server accepts `response_format: json_schema`
   (LM Studio, vLLM, recent llama.cpp); otherwise JSON mode is asked, and a server that rejects both gets the request without it.
4. **Save the custom server**. Its models now appear in the model lists as "model · name · custom server, no price recorded".
   **Edit** on a saved server changes it in place (the steps that use its models keep them). Do not put a user name or password in the
   address; the API key field is the place for a secret.

The first request after a model is loaded can take minutes: TM Concept waits up to an hour for the first answer and retries a few times
while the server answers 503 (model loading). A model's thinking (`reasoning_content`, `<think>…</think>`) is shown as "thinking" and not
used as the answer. Small local models write weaker video code than the large online ones; try topic suggestions and concepts first.

The server list goes into a backup with the settings; a server's token only when you tick *API keys*. Deleting a server leaves the steps
that used it with no model until you choose another.

### Ollama and LM Studio

When Settings → **Keys** opens, TM Concept looks for Ollama (`http://127.0.0.1:11434`) and LM Studio (`http://127.0.0.1:1234`) on this
computer. A running one shows under **Found on this computer** with its number of text models; **Add** fills the form and lists them. A
different address works too: type it (with or without `/v1`) and press **Connect and list models**; the app recognises Ollama and LM Studio
by their own API and keeps them by their own address. For LM Studio, start its server first (the *Developer* tab, or `lms server start`);
TM Concept never downloads models into them, so download one there first (`ollama pull qwen3:8b`, or LM Studio's model search).

What changes compared with a plain OpenAI-compatible server:

- **The list** shows only text models (embedding and cloud models are left out), each with its size on disk, whether it reads images and
  whether it can think. The proposed **Context** is the model's trained context, at most 16 384 tokens: it is what the app loads the model
  with, and a larger context takes more graphics memory. Raise it for long video code answers if your card has room.
- **Ollama** runs through its own `/api/chat`: the context goes with every request (Ollama's own default on cards under 24 GB is
  4 096 tokens, which silently cut long prompts), a JSON schema becomes a grammar so even small models write valid JSON, and **Think
  first** (for models that can think) turns the model's thinking on; it is off by default (faster).
- **LM Studio** loads the model with the chosen context before the first request (`/api/v1/models/load`); a model already loaded with a
  smaller context is loaded again. The requests go to its OpenAI-compatible API with a JSON schema.
- **One model on the graphics card:** when an image or voice model needs the card, the app asks Ollama or LM Studio to unload the
  language model and waits until it has left the server's list and the card's memory is free.
- **Too little graphics memory:** Ollama puts what does not fit on the card into main memory by itself (it works, but slower). **Test
  connection** on a saved Ollama shows each model in memory with how many GB sit on the card and how many in main memory, and the log
  says the same after every answer. LM Studio decides itself how much goes on the card; if a model does not fit, lower *GPU Offload* in
  the model's settings in LM Studio, or load it once with `lms load <model> --gpu 0.5` (a share between 0 and 1). A smaller model, a
  smaller quantization or a smaller context also helps.
- Weak local models that answer with broken JSON get the same request up to two more times before the step stops with a clear error.

### No server of your own: the app's own Ollama

Settings → **Local AI** installs *Ollama (TM Concept)*, a copy of Ollama that belongs to the app, and downloads recommended text models
for it (picked per graphics card class, each shown with its size and whether it fits your card). It is separate from everything on this
page: it never shows under **Found on this computer**, listens only on 127.0.0.1 on its own port (11534 or the next free one, never 11434),
keeps its models in the app's data folder and starts only when one of its models is used. Its models appear in the text steps by
themselves; there is nothing to add here. Remove them in Settings → **Storage**.

## After adding a key

Settings → **Models**: choose a model for every step you use (concepts, video code, image prompt, image, online narration, semantic
duplicate check, describe a sample image). Nothing is chosen automatically; the "Recommended" badge shows good choices. A price that
changed can be corrected on the same page.
