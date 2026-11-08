<p align="center"><img src="assets/brand/tm-concept-logo-horizontal-dark.svg" alt="TM Concept" width="420"></p>

**English** · [Türkçe](README.tr.md)

TM Concept is a free, open-source (MIT) desktop app that turns a channel idea into video concepts and short videos. It writes concepts
with **online AI models and your own API keys**, lets the model write the video code, renders the video **on your computer** (no GPU
needed) and narrates it with a **free local voice**. It runs on Windows and Linux and keeps its database encrypted.

- [What it does](#what-it-does)
- [What it costs](#what-it-costs)
- [Install on Windows](#install-on-windows)
- [Install on Linux (x64 and arm64)](#install-on-linux-x64-and-arm64)
- [First start](#first-start)
- [Models and what they install](#models-and-what-they-install)
- [API keys](#api-keys)
- [Make your first video](#make-your-first-video)
- [Update](#update)
- [Uninstall](#uninstall)
- [Where your data is](#where-your-data-is)
- [Licenses you should know](#licenses-you-should-know)
- [Known limits](#known-limits)
- [Build from source](#build-from-source)

## What it does

- **Profiles = channels.** Channel name, video language (Turkish, English, German, Spanish, French, Italian, Portuguese, Dutch, Polish,
  Russian, Swedish or Indonesian; your notes may stay in your own language, the video is written in this one), vertical short or horizontal video, target length
  (15–180 s), the channel's concept in your words, optional sample images, and its own models for any step (voice, images, engine…)
  where it should differ from the general setting. Four ready templates fill the fields
  (history: Byzantine, Ottoman; technology: ancient, modern).
- **Generate concepts.** 1–100 concepts per press, in batches of 20. Before you press, the app shows an estimated price range; afterwards
  the exact price from the provider's usage. Duplicates are prevented in four layers: the history of every concept in the prompt (cached),
  a free local similarity check, an optional semantic check with an embedding model, and a compressed history when the list grows.
  Approve with ✓ (key A) or delete with ✗ (key D). Deleted concepts stay in **Trash** forever (they still count against duplicates) until
  you delete them permanently.
- **Concept to video.** Queue approved concepts, choose images (as set in the profile's Images step, your sample images, or none),
  narration, subtitles and a narration text file, then press **Produce the queue in order**. The engine (HyperFrames by default, Remotion,
  Revideo), the code model, the image model and the voice come from the profile (or the general setting).
  The model writes one code file; a safety check runs before anything is executed; a failed check or render gets one repair request.
  Videos are at most 3 minutes.
- **Produced videos.** Thumbnail, a built-in player (play/pause, seek bar, time, volume, full screen; nothing starts by itself), the whole
  narration scene by scene and the whole concept text, details (engine, models, cost per step, voice notes), open in the system player,
  open the folder, produce again, delete a version and, if you want, move its files to the recycle bin.
- **Versions.** Edit the narration of each scene (more sentences, pause and speed markers) and choose a voice: the voice is made again on
  your computer and the same video code is rendered again — no AI cost (an online voice is paid). Or have new code written for another
  engine (the estimate is shown and confirmed first); images, voice clips and texts are used again. Every new version is a folder of its
  own (v2, v3…); the older ones stay.
- **Notifications** in the bottom-right corner (concepts ready, video ready, error). One minute by default; change or turn them off in
  Settings. A notification also says what failed and what was spent; everything the app does is written to the log file.
- **Encrypted.** The database (with your API keys) is encrypted with SQLite3 Multiple Ciphers. Its key is protected by Windows (DPAPI), the
  Linux keyring or a master password. Daily backups (last 10) are password-protected files (Argon2id + AES-256-GCM); you choose what goes
  in (API keys only when you tick them).
- **Two interface languages:** English and Turkish.

It does **not** read other channels and does not use reference videos. It can speak videos with **your own voice** (Pocket TTS for English,
Chatterbox Multilingual V3 for Turkish and nine more languages, from a short recording you add; whose voice you copy is your
responsibility).

## What it costs

The app is free. You pay your AI providers directly, only for what you use; the app shows the estimate before and the exact cost after.
Rough numbers (prices of 5 Nov 2026; the pages show the current ones next to each estimate — they update by themselves):

| What | Typical cost |
|---|---|
| 20 concepts with Claude Sonnet 5.5 | about 0.15–0.25 $ |
| Video code for one 60–90 s video with Claude Sonnet 5.5 | about 0.10 $ |
| Narration with a local voice (Supertonic 3, or Kokoro for English) | free |
| Duplicate check by meaning with the local model | free |
| 8 AI images per video (fal FLUX 2 Flash) | about 0.08 $ |
| 8 images with a local model (FLUX.2 klein 4B or Z-Image Turbo, your graphics card) | free |

## Install on Windows

Windows 10 or 11, 64-bit.

1. Open the [latest release](../../releases/latest) and download `TMConcept-<version>-win-x64.exe`.
2. Optional but recommended: check the file against the SHA-256 value on the release page. In PowerShell:
   ```powershell
   Get-FileHash .\TMConcept-0.1.4-win-x64.exe -Algorithm SHA256
   ```
3. Put the file where it can stay, for example `%LOCALAPPDATA%\Programs\TM Concept\TMConcept.exe`.
4. Double-click it. The file is not code-signed, so Windows shows **"Windows protected your PC"** (SmartScreen): click **More info**, then
   **Run anyway**. Windows usually asks only the first time.
5. Continue with [First start](#first-start).

No administrator rights are needed; nothing is installed system-wide and the system PATH is never changed.

**OneDrive note:** videos are saved in `Documents\TM Concept`. If OneDrive backs up your Documents folder, every video is uploaded to the
cloud too. If you do not want that, exclude the folder in OneDrive's settings.

## Install on Linux (x64 and arm64)

Use `linux-x64` on ordinary PCs and `linux-arm64` on 64-bit ARM computers (for example a Raspberry Pi 4 or 5 with a 64-bit desktop). A
desktop session is required.

1. Download `tm-concept-<version>-linux-x64.tar.gz` (or `-linux-arm64`) from the [latest release](../../releases/latest).
2. Optional: check the SHA-256 value: `sha256sum tm-concept-0.1.4-linux-x64.tar.gz`
3. Unpack it where it can stay:
   ```sh
   mkdir -p ~/.local/opt
   tar -xzf tm-concept-0.1.4-linux-x64.tar.gz -C ~/.local/opt
   ~/.local/opt/tm-concept/install-desktop-entry.sh   # optional: adds TM Concept to the application menu
   ```
4. **Keyring (recommended).** To open the database automatically, the key is stored in your desktop keyring with `secret-tool`:
   Debian / Ubuntu / Raspberry Pi OS: `sudo apt install libsecret-tools` · Fedora: `sudo dnf install libsecret` · Arch:
   `sudo pacman -S libsecret`. Without a keyring the app asks a master password at every start.
5. **Chrome's libraries.** The video engines run a headless Chrome. If libraries are missing, Settings → General (Installed components) shows the exact
   `sudo apt install …` command (the app never uses root). Typical list: `libnss3 libatk-bridge2.0-0 libcups2 libdrm2 libxkbcommon0
   libxcomposite1 libxdamage1 libxrandr2 libgbm1 libpango-1.0-0 libasound2`.
6. **ARM64:** Puppeteer has no ARM64 Chrome. Install Remotion (it brings its own ARM64 browser) or the system Chromium
   (`sudo apt install chromium`); HyperFrames and Revideo then use it.
7. Start `~/.local/opt/tm-concept/tm-concept` (or from the menu).

## First start

1. **Language:** two big buttons, Türkçe and English (your system language comes first). You can change it later in Settings.
2. **New start or restore:** shown only when there is no data yet. *Restore from backup* asks for a `.tmcbak` file and its password, then
   shows what the file holds (profiles, concepts, settings, API keys if they were backed up) and asks before restoring.
3. **Protection and backup password:** the database key is protected by Windows / the keyring, or by a master password. The **backup
   password** is required (at least 12 characters): daily backups are encrypted with it. Write it down; without it a backup cannot be opened.
4. **First profile:** choose a template or fill in your channel.

## Models and what they install

There is no model setting in Settings: every model is chosen on the page that uses it, and the last choice is kept **per profile** (a
new profile starts with nothing paid chosen; "Recommended" shows the suggested ones). **Generate concepts** has the concept model and the
duplicate check by meaning; **Concept → video** has the video code model with the engine, images and narration; the **Profile** page has
the model that describes a sample image. Each step says what it is for:

- **Concept generation** and **Video code** — the online models you pay for. **Effort** appears only for a model that takes one; *Automatic*
  sends no effort (the provider's default). **Video code** also has the **video engine**: HyperFrames (default), Remotion (asks you to
  accept its license: free for individuals and companies of up to 3 people), Revideo (experimental). The video code model also writes
  the image prompts.
- **Images** — *Off* (default: the video is drawn by its code only), an online image model, or a local one that runs on your graphics card:
  **FLUX.2 klein 4B** (fast) or **Z-Image Turbo** (better pictures), both with at least 6 GB of graphics memory (the card they were measured on). The page warns when
  your card looks too small; on Windows they need an NVIDIA card (CUDA), on Linux x64 a Vulkan driver; there is none for Linux ARM64.
- **Images** can also use the profile's own sample images (free).
- **Narration** — *Off* (silent video, yellow warning), **Supertonic 3** (local, free; every video language; the default
  voice of every language but English), **Kokoro 82M** (local, free; the default for English;
  English only, American and British voices; a dictionary voice: names and words it does not know are left out of the audio, with a
  warning), or an online voice. Only voices that speak the profile's video language are listed; when the language changes, a voice that
  does not speak the new one is replaced by the language's default voice, with a warning on the step. Voice, speed and ▶ Listen (a
  short sentence in the video language) are in the step.
- **Your own voice** — **Pocket TTS** (local, free, English only): Settings → Voice and pronunciation → add a clean recording of 3–15 s
  (WAV, MP3, M4A, OGG, FLAC; ffmpeg converts it), then choose it as the voice of the step. The recording stays in the encrypted
  database. Weights CC-BY-4.0: credit "Pocket TTS, Kyutai (CC-BY-4.0)" when you publish. No speed setting.
  **Chatterbox Multilingual V3** (Resemble AI, MIT; local, free) speaks Turkish, English, German, Spanish, French, Italian, Portuguese,
  Dutch, Polish and Swedish with the same recordings. It runs on the processor only (the graphics card is not used) and is slow: about
  6× the length of the speech on an 8-thread share of a 16-thread processor, plus about 1.5 minutes to load the model for the first scene.
  Long scenes are spoken sentence by sentence (at most 300 characters at a time). No speed setting, no watermark.
- **Pronunciation** — Settings → Voice and pronunciation: per video language, words the voice should read differently ("Viking" →
  "Vikink" in Turkish; Turkish comes with a small list, you add your own). Only the voice changes; subtitles keep the written word.
- **Duplicate check by meaning** — optional: the local **Multilingual MiniLM** (free, on this computer, 50+ languages including Turkish)
  or an online embedding model. **Describe a sample image** — optional, a cheap online helper.

When you press **Generate** or **Produce**, the choices are made ready first: a provider with no API key yet is named and you can paste a
key right there (it is tested with a free request and kept only when it works); then, when a choice needs something this computer does
not have, **one confirmation lists every download** (name, source, size). Nothing is downloaded without it. It installs in a window over
the app (one line per download with its progress; the app waits meanwhile, Cancel stops it), the choices are saved for the profile, and
the process goes on by itself. If you decline, cancel or an install fails, nothing
starts and nothing is paid for; the reason is shown. A kept choice whose component was removed later is installed again the next time.

| Component | From | About |
|---|---|---|
| Node.js 26.13.0 (only when no Node.js 22 or newer with npm is on your PATH) | nodejs.org | 35 MB |
| ffmpeg 8.1.3 (GPL build, a separate program; on Linux the distribution's copy is used when present) | github.com (BtbN builds) | 130–200 MB |
| Engine packages + Chrome Headless Shell | registry.npmjs.org, storage.googleapis.com | 200–350 MB |
| Supertonic 3 + sherpa-onnx 2.0.1 | github.com (k2-fsa) | 140 MB |
| Kokoro 82M v1.0 int8 (needs sherpa-onnx) | github.com (k2-fsa) | 130 MB |
| Pocket TTS int8 (needs sherpa-onnx) | github.com (k2-fsa) | 98 MB |
| Chatterbox Multilingual V3 ONNX (Masterx export, 4-bit language model; needs sherpa-onnx) | huggingface.co | 1.56 GB |
| Multilingual MiniLM (paraphrase-multilingual-MiniLM-L12-v2, int8 ONNX + tokenizer; needs sherpa-onnx) | huggingface.co | 127 MB |
| stable-diffusion.cpp master-890 (Windows: CUDA 12 build + CUDA runtime; Linux x64: Vulkan build) | github.com (leejet) | 900 MB / 40 MB |
| FLUX.2 klein 4B weights (model, Qwen3-4B text encoder, VAE) | huggingface.co | 5.3 GB |
| Z-Image Turbo weights (model, Qwen3-4B-Instruct text encoder, VAE) | huggingface.co | 9.4 GB |

Every pinned download is checked against its SHA-256 (npm packages: the lock file's integrity values; the image weights are fetched at a
fixed Hugging Face commit) and goes into the app's own data folder; the system PATH is never changed. Chrome Headless Shell comes over
HTTPS from Google, which publishes no checksum for it. Settings → **General** → *Installed components* lists what is on disk with its size
(an unfinished or no longer usable one is marked *incomplete*), can remove it, and can render an engine's sample without any model or network.

Prices update by themselves, at most once a day: the app reads `pricing/prices.json` of this repository and OpenRouter's public model
list (no key is sent), checks them and keeps the last good copy; offline, the built-in table is used. The estimate boxes show the date
and the source; the Produced videos page shows this month's spending.

The window is drawn by the processor by default (same look, about 50–70 MB less memory). If scrolling or the player feels slow, turn on
Settings → **General** → **Draw the window with the graphics card** and restart.

## API keys

Settings → **Keys**: choose the provider, paste the key, press **Save and test the connection** (a free request; fal has no free test).
Keys stay in the encrypted database, are sent only to their own provider and are never written to logs. They go into a backup only when
you tick *API keys* in Settings → Backup (the backup file is encrypted with your backup password). A page that needs a provider without a
key asks for one before it starts.
Step-by-step guides for every provider: [docs/api-keys.md](docs/api-keys.md).

Then choose the models on the pages that use them (see above). Nothing paid is chosen for you.

## Make your first video

1. **Generate concepts:** choose the model, the count and an optional extra request (markers, an optional fine-tuning of tone, audience and structure,
   and **Suggest topics** help with it); check the estimate; press **Generate**.
2. Open cards, approve the good ones (A), delete the rest (D).
3. **Concept to video:** click approved concepts to queue them, check the settings, press **Produce the queue in order**.
4. **Produced videos:** watch in the app (sound on Linux through PulseAudio or PipeWire), open the folder. Each video is a version folder (`v1/` inside the concept's folder) with `video.mp4`, `subtitles.srt`, `narration.txt` (always written) and `thumbnail.jpg`, plus `source/` (the engine project as it rendered: the model's code, `input.json`, the scene images) and `audio/` (the narration clip of each scene), kept for a later re-render.

## Update

Download the new release and replace the program file (Windows: the `.exe`; Linux: unpack over the old folder). Your data, tools and
videos stay where they are. Engine packages are reinstalled only when the new version pins other versions (you are asked before the next video).

## Uninstall

1. Delete the program file (Windows) or the `tm-concept` folder (Linux; and `~/.local/share/applications/tm-concept.desktop`).
2. To remove all data too, delete the data folder (below). Videos in `Documents/TM Concept` are yours; delete them separately if you want.
3. Linux: the database key may remain in your keyring as "tm-concept"; remove it with your keyring tool.

## Where your data is

| What | Windows | Linux | Encrypted |
|---|---|---|---|
| Database (profiles, concepts, keys) | `%LOCALAPPDATA%\TM Concept\concept.db` | `~/.local/share/tm-concept/concept.db` | yes |
| Backups (last 10, daily) | `…\TM Concept\backups\*.tmcbak` | `~/.local/share/tm-concept/backups/` | yes (password) |
| Logs (no keys; home and app folders masked; 7 days) | `…\TM Concept\logs\` | `~/.local/share/tm-concept/logs/` | no |
| Problem report zips (last 5) | `…\TM Concept\reports\` | `~/.local/share/tm-concept/reports/` | no |
| Tools (Node, ffmpeg, engines, voices, image models) | `…\TM Concept\tools\` | `~/.local/share/tm-concept/tools/` | no |
| Videos and sample images | `Documents\TM Concept\` | `~/Documents/TM Concept/` (XDG documents folder) | no |

Portable mode: start the program with `--data-dir <folder>`; then everything, videos included, stays under that folder.

## Licenses you should know

- **Supertonic 3** (default voice): OpenRAIL-M. Say that the voice is AI-generated when you publish; no impersonation. The video details
  remind you.
- **Kokoro 82M**: Apache-2.0. **Multilingual MiniLM** (sentence-transformers): Apache-2.0.
- **Remotion**: not open source; free for individuals and companies of up to 3 people, larger companies need a Remotion license.
- **Sample images**: you must have the right to use them.
- **ffmpeg**: GPL, downloaded as a separate program.
- **Local image models**: FLUX.2 klein 4B, Z-Image Turbo and their text encoders / VAEs are Apache-2.0; stable-diffusion.cpp is MIT.
  They are downloaded from Hugging Face / GitHub only after you confirm.

Details: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Reporting a problem

TM Concept sends nothing by itself: no telemetry, no crash upload. If something goes wrong:

1. Click **Report a problem** at the bottom of the left menu.
2. **Prepare logs** makes `tm-concept-logs-<date>-<time>.zip` in the data folder's `reports` folder and shows it. It holds the log files
   of the last 7 days and `system.txt` (version, operating system, architecture, .NET, interface language, drawing mode, which tools are
   installed). Your home folder, the app's folders (library, data, documents), e-mail addresses and keys are masked, and while the app
   is unlocked every profile name and concept or video title it knows becomes `<name>` (the log lines of 0.1.4 leave them out already).
   The database, API keys, concept and narration texts and video files are never in it. The logs do name the settings you changed and
   their new values (interface language, drawing mode, notifications, each step's model, effort and engine). Masking is a
   safety net, not a guarantee: open the zip and read it before you share it publicly.
3. **Report on GitHub** opens the issue form with the version and system filled in. Describe what happened and drag the zip into the
   form (a GitHub account is needed). **Copy details** copies the system information instead.

The logs are plain text files, one per day (`app-2026-11-06.log`, and `speech-worker-….log` for the local voice), in the `logs` folder
of the data folder (table above). Files older than 7 days are deleted automatically; one day's file stops at 20 MB.

## Known limits

- Videos up to 3 minutes; no long horizontal videos yet.
- The program is not code-signed (SmartScreen warning on Windows).
- Revideo is experimental. On Linux ARM64 an external Chromium is needed for HyperFrames and Revideo.
- Real provider requests were not part of the automated tests (they use fake HTTP); the first real use happens on your machine.

## Build from source

.NET 10 SDK. `dotnet build TMConcept.slnx` and `dotnet test TMConcept.slnx`. Release files: `tools/publish.ps1` (Windows) or
`tools/publish.sh`. Before the first commit run `tools/install-hooks.ps1` (or `.sh`): the pre-commit hook blocks keys, tokens and
personal data.
