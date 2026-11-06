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
- [Tools: Node.js, ffmpeg, engines, voice](#tools-nodejs-ffmpeg-engines-voice)
- [API keys](#api-keys)
- [Make your first video](#make-your-first-video)
- [Update](#update)
- [Uninstall](#uninstall)
- [Where your data is](#where-your-data-is)
- [Licenses you should know](#licenses-you-should-know)
- [Known limits](#known-limits)
- [Build from source](#build-from-source)

## What it does

- **Profiles = channels.** Channel name, content language (Turkish or English), vertical short or horizontal video, target length
  (15–180 s), the channel's concept in your words, optional sample images and the narration voice. Four ready templates fill the fields
  (history: Byzantine, Ottoman; technology: ancient, modern).
- **Generate concepts.** 1–100 concepts per press, in batches of 20. Before you press, the app shows an estimated price range; afterwards
  the exact price from the provider's usage. Duplicates are prevented in four layers: the history of every concept in the prompt (cached),
  a free local similarity check, an optional semantic check with an embedding model, and a compressed history when the list grows.
  Approve with ✓ (key A) or delete with ✗ (key D). Deleted concepts stay in **Trash** forever (they still count against duplicates) until
  you delete them permanently.
- **Concept to video.** Queue approved concepts, choose the engine (HyperFrames by default, Remotion, Revideo), the code model, images
  (none, your sample images or an image model), narration, subtitles and a narration text file, then press **Produce the queue in order**.
  The model writes one code file; a safety check runs before anything is executed; a failed check or render gets one repair request.
  Videos are at most 3 minutes.
- **Produced videos.** Thumbnail, a built-in player (play/pause, seek bar, time, volume, full screen; nothing starts by itself), details
  (engine, models, cost per step, voice notes, the concept text), open in the system player, open the folder, produce again, delete the
  record and, if you want, move the files to the recycle bin.
- **Notifications** in the bottom-right corner (concepts ready, video ready, error). One minute by default; change or turn them off in
  Settings. A log panel at the bottom keeps everything (yellow warnings, red errors).
- **Encrypted.** The database (with your API keys) is encrypted with SQLite3 Multiple Ciphers. Its key is protected by Windows (DPAPI), the
  Linux keyring or a master password. Daily backups (last 10) are password-protected files (Argon2id + AES-256-GCM); API keys are never put
  in backups.
- **Two interface languages:** English and Turkish.

It does **not** read other channels, does not use reference videos and does not clone voices.

## What it costs

The app is free. You pay your AI providers directly, only for what you use; the app shows the estimate before and the exact cost after.
Rough numbers (prices of 5 Nov 2026; check Settings → Models):

| What | Typical cost |
|---|---|
| 20 concepts with Claude Sonnet 5.5 | about 0.15–0.25 $ |
| Video code for one 60–90 s video with Claude Sonnet 5.5 | about 0.10 $ |
| Narration with the local Supertonic 3 voice | free |
| 8 AI images per video (fal FLUX 2 Flash) | about 0.08 $ |

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
5. **Chrome's libraries.** The video engines run a headless Chrome. If libraries are missing, Settings → Tools shows the exact
   `sudo apt install …` command (the app never uses root). Typical list: `libnss3 libatk-bridge2.0-0 libcups2 libdrm2 libxkbcommon0
   libxcomposite1 libxdamage1 libxrandr2 libgbm1 libpango-1.0-0 libasound2`.
6. **ARM64:** Puppeteer has no ARM64 Chrome. Install Remotion (it brings its own ARM64 browser) or the system Chromium
   (`sudo apt install chromium`); HyperFrames and Revideo then use it.
7. Start `~/.local/opt/tm-concept/tm-concept` (or from the menu).

## First start

1. **Language:** two big buttons, Türkçe and English (your system language comes first). You can change it later in Settings.
2. **New start or restore:** shown only when there is no data yet. *Restore from backup* asks for a `.tmcbak` file and its password.
3. **Protection and backup password:** the database key is protected by Windows / the keyring, or by a master password. The **backup
   password** is required (at least 12 characters): daily backups are encrypted with it. Write it down; without it a backup cannot be opened.
4. **First profile:** choose a template or fill in your channel.

## Tools: Node.js, ffmpeg, engines, voice

Settings → **Tools**. Each item is downloaded from its official source only after you confirm a dialog that shows the source and size, and
is checked against a pinned SHA-256 (npm packages: the lock file's integrity values). Everything goes into the app's own data folder.

1. **Node.js** (≈ 35 MB) — runs the engines.
2. **ffmpeg** (≈ 130–200 MB, GPL build, a separate program) — joins voice and video, burns subtitles, makes thumbnails, plays videos in
   the app. On Linux the distribution's ffmpeg is used when present. If the pinned download is ever removed or does not match its
   SHA-256, the ffmpeg already on your PATH is used (with a warning); without one the app shows how to install it.
3. **An engine:** HyperFrames (default) and/or Remotion (asks you to accept its license first) and/or Revideo (experimental). The engine
   packages and Chrome Headless Shell take a few hundred MB. **Sample render** checks the engine without any model or network.
4. **Voice:** Supertonic 3 (≈ 130 MB; shows its OpenRAIL-M terms first; Turkish and English) with its libraries, sherpa-onnx (≈ 8–13 MB;
   they run in a separate process only while the voice speaks). Without a voice the videos are silent and a
   yellow warning appears in the log.

Total disk use with one engine and the voice: about 0.6–1 GB.

The window is drawn by the processor by default (same look, about 50–70 MB less memory). If scrolling or the player feels slow, turn on
Settings → **General** → **Draw the window with the graphics card** and restart.

## API keys

Settings → **Keys**: choose the provider, paste the key, press **Save and test the connection** (a free request; fal has no free test).
Keys stay in the encrypted database, are sent only to their own provider, are never written to logs and never put in backups.
Step-by-step guides for every provider: [docs/api-keys.md](docs/api-keys.md).

Then Settings → **Models**: choose a model for each step. Nothing is chosen for you; the badge shows the recommended ones.

## Make your first video

1. **Generate concepts:** choose the model, the count and an optional extra request; check the estimate; press **Generate**.
2. Open cards, approve the good ones (A), delete the rest (D).
3. **Concept to video:** click approved concepts to queue them, check the settings, press **Produce the queue in order**.
4. **Produced videos:** watch in the app (sound on Linux through PulseAudio or PipeWire), open the folder. The video folder has `video.mp4`, `subtitles.srt`, `narration.txt` and `thumbnail.jpg`.

## Update

Download the new release and replace the program file (Windows: the `.exe`; Linux: unpack over the old folder). Your data, tools and
videos stay where they are. Engine packages are reinstalled only when the new version pins other versions (Settings → Tools shows it).

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
| Tools (Node, ffmpeg, engines, voices) | `…\TM Concept\tools\` | `~/.local/share/tm-concept/tools/` | no |
| Videos and sample images | `Documents\TM Concept\` | `~/Documents/TM Concept/` (XDG documents folder) | no |

Portable mode: start the program with `--data-dir <folder>`; then everything, videos included, stays under that folder.

## Licenses you should know

- **Supertonic 3** (default voice): OpenRAIL-M. Say that the voice is AI-generated when you publish; no impersonation. The video details
  remind you.
- **Remotion**: not open source; free for individuals and companies of up to 3 people, larger companies need a Remotion license.
- **Sample images**: you must have the right to use them.
- **ffmpeg**: GPL, downloaded as a separate program.

Details: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Reporting a problem

TM Concept sends nothing by itself: no telemetry, no crash upload. If something goes wrong:

1. Click **Report a problem** at the bottom of the left menu.
2. **Prepare logs** makes `tm-concept-logs-<date>-<time>.zip` in the data folder's `reports` folder and shows it. It holds the log files
   of the last 7 days and `system.txt` (version, operating system, architecture, .NET, interface language, drawing mode, which tools are
   installed). Your home folder, the app's folders (library, data, documents), e-mail addresses and keys are masked, and while the app
   is unlocked every profile name and concept or video title it knows becomes `<name>` (the log lines of 0.1.4 leave them out already).
   The database, API keys, concept and narration texts and video files are never in it. The logs do name the settings you changed and
   their new values (interface language, drawing mode, notifications, each step's model and effort, price overrides). Masking is a
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
