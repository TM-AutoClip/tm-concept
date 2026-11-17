# Third-party notices

TM Concept is MIT licensed. It includes or uses the following third-party material.

## Included in this repository

| Component | License | Where |
|---|---|---|
| Manrope font | SIL Open Font License 1.1 | `src/TMConcept.Desktop/Assets/Fonts`, license text in `src/TMConcept.Desktop/Assets/Licenses/font-manrope.txt` |
| Space Grotesk font | SIL Open Font License 1.1 | `src/TMConcept.Desktop/Assets/Fonts`, license text in `src/TMConcept.Desktop/Assets/Licenses/font-space-grotesk.txt` |
| Video fonts of the templates: Fira Sans, Poppins, Bebas Neue, Anton, Crimson Text, Patrick Hand (static TrueType files from github.com/google/fonts, `ofl/<name>`) | SIL Open Font License 1.1 | `src/TMConcept.Core/Styles/Files/Fonts` (embedded in the program; copied into a video's work folder while it is made), license texts in `src/TMConcept.Desktop/Assets/Licenses/font-<name>.txt` |
| Engine package lists (`package.json`, `package-lock.json`) | — (lists of versions, not the packages) | `src/TMConcept.Engines/Packages` |
| Ahenk Cep text front end and sampling loop, ported to C# (from `site/js/front-native.js`, `site/js/engine.js` and `site/js/randn.js` of github.com/ozguradmin/ahenk-turkish-tts, which follow EMA Lightning's `frontend.py`, `chunker.py` and engine) | Apache-2.0 (Ahenk; EMA Lightning) | `src/TMConcept.Speech/AhenkCep`. Changed: rewritten in C#; the text arrives already normalized by TM Concept's own Turkish rules, so trnorm itself is not included. |
| Some entries of the built-in Turkish pronunciation list and the Turkish letter names of abbreviations, cross-checked with the word lists of trnorm (`site/js/trnorm/lexicon.js` of the same repository) | Apache-2.0 (trnorm) | `src/TMConcept.Core/Pronunciation/Files/tr.txt`, `src/TMConcept.Core/Pronunciation/Constant` |

The font license texts are shipped as files next to the program and in the Linux archive (`licenses/`).

## Libraries bundled into the program

The release files are self-contained: they carry the .NET runtime and these libraries. Each keeps its own license.

| Library | License |
|---|---|
| .NET runtime and Microsoft.Extensions.* | MIT |
| Avalonia (UI) with SkiaSharp and HarfBuzzSharp | MIT |
| CommunityToolkit.Mvvm | MIT |
| Entity Framework Core (SQLite) with Microsoft.Data.Sqlite | MIT |
| SQLite3 Multiple Ciphers (SQLite3MC.PCLRaw.bundle) and SQLitePCLRaw | MIT |
| SQLite | Public domain |
| Konscious.Security.Cryptography.Argon2 | MIT |
| NAudio (NAudio.Core, NAudio.WinMM; Windows sound output of the player) | MIT |
| System.Security.Cryptography.ProtectedData | MIT |
| ZstdSharp.Port (reads the Linux Ollama package, `.tar.zst`) | MIT |
| PdfPig (UglyToad.PdfPig; reads the text of PDF documents, 0.1.21) | Apache-2.0 |
| ONNX Runtime (Microsoft.ML.OnnxRuntime with its native library; runs the documents' meaning model on the processor, 0.1.21) | MIT |

Exact versions are in `Directory.Packages.props`. Since 0.1.3 the speech libraries (sherpa-onnx with its ONNX Runtime) are no longer bundled;
they are downloaded like the other tools (below). Since 0.1.21 the program carries ONNX Runtime again (Microsoft.ML.OnnxRuntime, for the
documents only; the speech worker still uses the downloaded sherpa-onnx copy). Word documents are read without a library (their XML
directly).

## Downloaded after your approval (not distributed with TM Concept)

These are downloaded from their official sources into the app's data folder only when a setting you save (Settings → Models, a profile's
models) or the first video needs them and you confirm the list of downloads. TM Concept checks each download against a pinned SHA-256
(npm packages: the integrity values of the lock file).

| Component | License | Notes |
|---|---|---|
| Node.js 26.13.0 | MIT (plus the licenses of its bundled parts) | nodejs.org |
| FFmpeg (BtbN FFmpeg-Builds, GPL build) | GPL-3.0-or-later | A separate program, run as a separate process. Source: github.com/FFmpeg/FFmpeg, build scripts: github.com/BtbN/FFmpeg-Builds |
| Chrome Headless Shell | BSD-3-Clause (Chromium) and the licenses of its third-party parts | Downloaded with @puppeteer/browsers or by Remotion |
| HyperFrames + GSAP | Apache-2.0 (HyperFrames), GSAP "no charge" standard license | npm |
| Remotion | Remotion License (source available, conditional) | Free for individuals, companies of up to 3 people and non-profits; other companies need a Remotion Company License. The license is between you and Remotion. |
| Revideo | MIT | npm (experimental engine) |
| sherpa-onnx 2.0.1 shared libraries with ONNX Runtime | Apache-2.0 (sherpa-onnx), MIT (ONNX Runtime) | The official "-lib" archives of the sherpa-onnx v2.0.1 GitHub release (Windows: the MT build). Loaded only by the app's speech worker process. 2.0.1 is used on purpose: the 1.13.8 library still contained espeak-ng (GPL-3.0); 2.0.1 does not. |
| Supertonic 3 voice model | Model weights: OpenRAIL-M; code: MIT | sherpa-onnx tts-models release. The OpenRAIL-M use restrictions apply to you (below). |
| Kokoro 82M v1.0 voice model (int8, sherpa-onnx tts-models release) | Apache-2.0 (hexgrad/Kokoro-82M) | English voices only (sherpa-onnx 2.x reads English from the model's dictionary). The archive also contains espeak-ng data (GPL-3.0), which the app deletes right after unpacking and never uses. |
| Pocket TTS voice model (int8, sherpa-onnx tts-models release `sherpa-onnx-pocket-tts-int8-2026-01-26`) | CC-BY-4.0 (weights: Kyutai, kyutai/pocket-tts; ONNX export: KevinAHM/pocket-tts-onnx) | **Attribution:** "Pocket TTS by Kyutai (https://huggingface.co/kyutai/pocket-tts), ONNX export by KevinAHM, licensed under CC-BY-4.0 (https://creativecommons.org/licenses/by/4.0/)". English only; speaks with a voice sample the user adds. The archive's README says "It is for non-commercial", which the license files of the weights (CC-BY-4.0) do not say; its test voices (recordings from kyutai-labs/delayed-streams-modeling, licenses not stated) are deleted right after unpacking and never used. Kyutai's terms forbid copying a person's voice without their consent. |
| Chatterbox Multilingual V3 voice model (ONNX: `speech_encoder`, `embed_tokens`, `language_model_q4`, `conditional_decoder`, `tokenizer.json`; Masterx/chatterbox-multilingual-v3-ONNX at commit 19b94afe41de738624b2f3afc75b4cd56221fbeb) | MIT ("Copyright (c) 2025 Resemble AI"; the export's LICENSE is downloaded with it) | Resemble AI's Chatterbox (`t3_mtl23ls_v3`, `s3gen`), converted to ONNX by Masterx (not affiliated with Resemble AI). huggingface.co, each file with its SHA-256. Run with the ONNX Runtime of the sherpa-onnx libraries above, in the worker process, on the processor; the app's own code does the tokenizer, the sampling and the key/value cache loop (no Python). The graphs carry no Perth watermark (Resemble's Python package adds one); Resemble AI asks to use the model responsibly. The export's bundled `default_voice.wav` (origin and license not stated) is not downloaded: the voice speaks only with the user's own samples. |
| Ahenk Cep voice model (ONNX int8 set: `text.int8`, `cond.int8`, `step.int8`, `decoder.f16`; ozgurguler/ahenk-cep-tr at commit 345c4cbb934815fa96ffd0d5061fd913237544bc) | CC-BY-4.0 (model; trained on the Alania datasets, CC-BY-4.0, with EMA Lightning, Apache-2.0) | **Attribution:** "Ahenk Cep, Ahenk (https://github.com/ozguradmin/ahenk-turkish-tts), licensed under CC-BY-4.0 (https://creativecommons.org/licenses/by/4.0/)". Turkish only; the model card asks that its output be labelled as synthetic speech. Run with the ONNX Runtime of the sherpa-onnx libraries above, in the worker process, on the processor. huggingface.co, SHA-256 of the model's own manifest. |
| multilingual-e5-small (intfloat), int8 ONNX file (`onnx/model_qint8_avx512_vnni.onnx`) and tokenizer, commit 614241f622f53c4eeff9890bdc4f31cfecc418b3 | MIT | The documents' meaning search (optional, 0.1.21); huggingface.co, each file with its SHA-256. Run in the program with the bundled ONNX Runtime, on the processor. |
| paraphrase-multilingual-MiniLM-L12-v2 (sentence-transformers), int8 ONNX file and tokenizer | Apache-2.0 | The local model of the duplicate check by meaning; huggingface.co, fixed commit with SHA-256. Run with the ONNX Runtime of the sherpa-onnx libraries above, in the worker process. |
| stable-diffusion.cpp master-1106-532d2c1 (Windows) and master-1043-3be2378 (Linux; the last release built on Ubuntu 24.04) (sd-cli) | MIT | github.com/leejet/stable-diffusion.cpp releases: the Windows CUDA 12 build with its CUDA runtime archive (NVIDIA CUDA runtime libraries under the NVIDIA CUDA EULA, as published in that release; TM Concept does not distribute them), the Windows Vulkan and processor builds, the Linux x64 Vulkan and processor builds (one is downloaded, by the Image acceleration setting). Runs the local image models as a separate process. |
| Z-Image Turbo (GGUF Q3_K / Q8_0 by leejet; BF16 by Comfy-Org) | Apache-2.0 | Tongyi-MAI. With its VAE (Comfy-Org, Apache-2.0) and Qwen3-4B-Instruct-2507 (Qwen, Apache-2.0; GGUF Q4_K_M / Q8_0 by unsloth; BF16 Qwen3-4B by Comfy-Org). huggingface.co |
| FLUX.2 klein 4B (GGUF Q4_0 / Q8_0 by leejet) | Apache-2.0 | Black Forest Labs. With the FLUX.2 VAE (the Apache-2.0 copy of Comfy-Org/Flux-2-Klein-4B, not the non-commercial FLUX.2 dev one) and Qwen3-4B (Qwen, Apache-2.0; GGUF by unsloth). huggingface.co |
| FLUX.2 klein 9B (GGUF Q4_0 / Q8_0 by leejet) | **FLUX Non-Commercial License** (Black Forest Labs) | **Not for commercial use.** Offered with a red "non-commercial" badge; the app asks you to accept the license before the first download. With the FLUX.2 VAE above and Qwen3-8B (Qwen, Apache-2.0; GGUF by unsloth). huggingface.co (black-forest-labs/FLUX.2-klein-9B) |
| FLUX.3 Image Klein (GGUF Q4_K / Q8_0 by leejet) | **FLUX Kommunity License v1.0** (Black Forest Labs) | Commercial use under conditions: by Black Forest Labs' announcement free for individuals and companies under 5 million USD annual revenue (the license text on huggingface.co/black-forest-labs/flux-3-image-klein is the binding text). Asked before the first download. With the FLUX.2 VAE above and Qwen3-VL-4B-Instruct (Qwen, Apache-2.0; GGUF by Qwen). Needs stable-diffusion.cpp build 968 or newer. |
| Stable Diffusion 3.5 Medium (GGUF Q4_0 / Q8_0 with CLIP-L, CLIP-G and T5-XXL GGUF by second-state) | **Stability AI Community License** | Free, also commercially, for individuals and organizations under 1 million USD annual revenue; above that an enterprise license from Stability AI. Asked before the first download. huggingface.co (stabilityai/stable-diffusion-3.5-medium) |
| SDXL Lightning 4-step (GGUF q4_0 / q8_0 by mzwing) | **OpenRAIL++** (ByteDance) | Commercial use allowed with the license's use restrictions. Asked before the first download. With the SDXL fp16-fix VAE (madebyollin, MIT). huggingface.co |
| Qwen-Image (GGUF Q4_0 / Q6_K / Q8_0 by QuantStack) | Apache-2.0 | Qwen. With its VAE (Comfy-Org, Apache-2.0) and Qwen2.5-VL-7B-Instruct (Qwen, Apache-2.0; GGUF by mradermacher). huggingface.co |
| Ollama 0.40.16 ("Ollama (TM Concept)", the app's own copy) | MIT (plus the licenses of its bundled parts: llama.cpp / ggml MIT; the CUDA builds carry NVIDIA CUDA runtime libraries under the NVIDIA CUDA EULA, as published in the release) | github.com/ollama/ollama releases, the official package checked against the release's SHA-256 digest; after unpacking only the graphics backend this computer needs is kept. Runs only as the app's own server (127.0.0.1, its own port and models folder); an Ollama you installed is never used or changed. |
| Qwen 3.5 (2B, 4B, 9B, 27B, 35B A3B), Gemma 4 (E2B, E4B, 12B, 26B A4B, 31B) and gpt-oss 20B, as Ollama library tags | Apache-2.0 (Qwen; Google; OpenAI) | registry.ollama.ai, pulled by the app's own Ollama server only when you download one in Settings → Local AI; Ollama checks each layer's SHA-256. |
| uv 0.13.6 | MIT or Apache-2.0 (Astral) | github.com/astral-sh/uv releases. Installs the Python voices' Python and packages into the tools folder (no launcher on the PATH, no registry entry, the user's uv/pip settings ignored). |
| CPython 3.11.18 (python-build-standalone, installed by uv) | PSF License 2.0 (plus the licences of its bundled parts: OpenSSL Apache-2.0, SQLite public domain, zlib, libffi MIT, Tcl/Tk BSD-style …) | github.com/astral-sh/python-build-standalone. Runs only the app's Python voice worker. |
| PyTorch 2.8.0 and torchaudio 2.8.0 (CUDA 12.8 build for NVIDIA cards, processor build otherwise) | BSD-3-Clause; the CUDA build pulls NVIDIA's CUDA libraries (NVIDIA software licence) | download.pytorch.org. The packages of each Python voice environment are pinned to exact versions by lock files in `src/TMConcept.Speech/Python/Files/Locks` (each package keeps its own licence: MIT, BSD, Apache-2.0, MPL-2.0, PSF, LGPL and others). |
| coqui-tts 0.28.0 (XTTS-v2 code) | MPL-2.0 | pypi.org (idiap's maintained fork of Coqui TTS). |
| f5-tts 1.1.22 | MIT | pypi.org (SWivid/F5-TTS). |
| voxcpm 2.0.3 | Apache-2.0 | pypi.org (OpenBMB/VoxCPM). |
| trnorm (Ahenk's Turkish text normaliser), commit 8981df9 | Apache-2.0 | github.com/ozguradmin/ahenk-turkish-tts (archive of that commit). |
| transformers 4.56.2 (MMS-TTS) | Apache-2.0 | pypi.org. |
| XTTS-v2 voice model (coqui/XTTS-v2 at commit 6c2b0d7: config, model, vocab, speakers, mel stats) | **Coqui Public Model License 1.0.0 — non-commercial** | huggingface.co, each file with its SHA-256. Offered only after the user accepted the licence in the app; the model may not be used commercially (a channel with ads or sponsors, paid work). Built-in speakers or the user's own sample. |
| F5-TTS Turkish (marduk-ra/F5-TTS-Turkish at commit 219efea, checkpoint 1 000 000) with the Vocos vocoder (charactr/vocos-mel-24khz at 0feb3fd) | **CC BY-NC 4.0 — non-commercial** (fine-tune); Vocos MIT | huggingface.co, each file with its SHA-256; offered only after the user accepted the licence. Speaks in the voice of `f5_ref_tr.wav`, a 6.7 s Turkish reference clip that comes with the app (spoken by the Supertonic 3 voice M1; the OpenRAIL-M restrictions apply to it). |
| VoxCPM2 (openbmb/VoxCPM2 at commit 7936080) | Apache-2.0 | OpenBMB. huggingface.co, each file with its SHA-256. OpenBMB forbids impersonation, fraud and disinformation and asks to label AI-generated content. |
| Trendyol-TTS (Trendyol/Trendyol-TTS at commit 66a8018) | MIT | Trendyol; VoxCPM2 with a merged Turkish LoRA. huggingface.co, each file with its SHA-256. |
| Ahenk (ozgurguler/ahenk-tts-tr at commit 6bcef95: LoRA weights and its helper scripts) | CC BY 4.0 (weights), Apache-2.0 (scripts) | **Attribution:** "Ahenk (https://github.com/ozguradmin/ahenk-turkish-tts), Özgür Güler, licensed under CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/)"; its training data (the Alania datasets) is CC BY 4.0 as well. A LoRA on VoxCPM2 (downloaded with it); one designed voice (f022), no real person. Label the output as AI-generated. |
| MMS-TTS Turkish (facebook/mms-tts-tur at commit 7e36447) | **CC BY-NC 4.0 — non-commercial** | Meta. huggingface.co, each file with its SHA-256; offered only after the user accepted the licence. |

### Supertonic 3 (OpenRAIL-M) use restrictions, short form

You agree not to use the model or its output: to break the law; to harm minors; to generate or spread false information meant to harm
others; to generate or spread personal data that can identify a person without permission; to impersonate a person (for example a voice
copy); to publish machine-generated content without clearly saying it is machine-generated; to discriminate against or harm people or
groups; for fully automated decisions that affect people's legal rights; or in ways that exploit the vulnerabilities of a group. This is a
summary; the license text of the model (Supertone Inc.) is the binding text. TM Concept therefore marks videos with an "AI voice — say so
when publishing" note.

### Not used

No reference voice other than the user's own voice samples (Pocket TTS, Chatterbox, XTTS-v2, VoxCPM2) and F5-TTS's reference clip above, no reading of other channels (no yt-dlp), no Piper GPL program. The optional Piper Turkish voice of
0.1.0 was removed in 0.1.1 (sherpa-onnx 2.x has no espeak-ng to turn text into its phonemes). On Linux the player uses the system's
libpulse-simple (PulseAudio / PipeWire, LGPL-2.1, a system library that is not distributed with TM Concept).
