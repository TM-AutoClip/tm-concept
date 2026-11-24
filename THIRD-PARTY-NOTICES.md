# Third-party notices

TM Concept is MIT licensed. It includes or uses the following third-party material.

## Included in this repository

| Component | License | Where |
|---|---|---|
| Manrope font | SIL Open Font License 1.1 | `src/TMConcept.Desktop/Assets/Fonts`, license text in `src/TMConcept.Desktop/Assets/Licenses/font-manrope.txt` |
| Space Grotesk font | SIL Open Font License 1.1 | `src/TMConcept.Desktop/Assets/Fonts`, license text in `src/TMConcept.Desktop/Assets/Licenses/font-space-grotesk.txt` |
| Video fonts of the templates: Fira Sans, Poppins, Bebas Neue, Anton, Crimson Text, Patrick Hand, Tinos (static TrueType files from github.com/google/fonts, `ofl/<name>`; Fira Sans, Poppins, Crimson Text and Tinos with their italic and bold italic files; Tinos is a serif with the widths of Times New Roman, by Steve Matteson, github.com/googlefonts/tinos) | SIL Open Font License 1.1 | `src/TMConcept.Core/Styles/Files/Fonts` (embedded in the program; copied into a video's work folder while it is made), license texts in `src/TMConcept.Desktop/Assets/Licenses/font-<name>.txt` |
| Engine package lists (`package.json`, `package-lock.json`) | — (lists of versions, not the packages) | `src/TMConcept.Engines/Packages` |
| Ahenk Cep text front end and sampling loop, ported to C# (from `site/js/front-native.js`, `site/js/engine.js` and `site/js/randn.js` of github.com/ozguradmin/ahenk-turkish-tts, which follow EMA Lightning's `frontend.py`, `chunker.py` and engine) | Apache-2.0 (Ahenk; EMA Lightning) | `src/TMConcept.Speech/AhenkCep`. Changed: rewritten in C#; the text arrives already normalized by TM Concept's own Turkish rules, so trnorm itself is not included. |
| PaddleOCR PP-OCRv5 Latin character list (`ppocrv5_latin_dict.txt` of github.com/PaddlePaddle/PaddleOCR, as published with the RapidOCR models, ModelScope RapidAI/RapidOCR tag v3.10.0; unchanged) | Apache-2.0 (PaddleOCR) | `src/TMConcept.Documents/Ocr/Files` (embedded in the program; the local OCR's recogniser reads its classes from it, 0.1.25) |
| World map of the scene drawer: Natural Earth 1:110m admin-0 countries (github.com/nvkelso/natural-earth-vector, `geojson/ne_110m_admin_0_countries.geojson`; "Made with Natural Earth") | Public domain | `src/TMConcept.Core/Plans/Files/world.json` (embedded in the program; a map scene's work folder gets it as `world.js`). Changed: names, codes, label points and outlines only, rounded to 0.1 degree and delta-encoded by `tools/world-map.py` (0.1.33). |
| Motion check thresholds of ReelMimic's video-clone `motion_check.py` (github.com/edenfunf/reelmimic, `.claude/skills/video-clone/scripts/motion_check.py`; the 160 × 96 grey frame, the 8 × 8 grid, the cut, freeze, black, white and hard-end-to-black numbers; the frame strip idea of `clip_strip.py`) | MIT (ReelMimic, notice below) | `src/TMConcept.Core/Videos/Constant/MotionCheckLimits.cs` (0.1.41). Changed: the numbers only; the check itself is written for TM Concept in C# (`MotionFrameReader`, `MotionCheck`), with its own rules for planned fades, scene starts and the scene drawer's beats. The 40 % background-variety rule of the scene plan (`PlanLimits.BackgroundShare`) is an idea from ReelMimic's storyboard format (Apache-2.0); no text or code of it is included. |
| The camera ease names of ReelMimic's video-clone `curves.py` (github.com/edenfunf/reelmimic, `.claude/skills/video-clone/scripts/perception/curves.py`: the GSAP ease names it fits camera moves to) | MIT (ReelMimic, notice below) | `src/TMConcept.Core/Plans/Constant/PlanEases.cs` (0.1.42). Changed: the list of names only (bounce and elastic left out). The scene transition numbers (zoom-through 1 → 1.2 / 0.75 → 1, at most 10 px of blur, 0.4–0.5 s at high energy) and the rule "the way a scene leaves is the way the next one enters, one direction in a film" are ideas from ReelMimic's transition registry and motion doctrine (Apache-2.0); no text, template or code of them is included (`SceneTransitions`, `plan.js`). |
| The finishing layer's numbers of ReelMimic's own engines (github.com/edenfunf/reelmimic, `.claude/skills/paper-cutout/template/src/cutout.js`: shadow by depth, opacity 0.2 + 0.09 s up to 0.6, offset 1.5 + 4.5 s, blur 2 + 6 s, light from (0.5, 0.86), 12 steps a second; `anime-cel/template/src/anime.js`: a soft glow and a vignette as a finishing grade; `crayon-storybook/template/src/crayon.js`: the paper tooth as light pits from two scales of noise; `video-clone/assets/engine-kit/kit.js`: a seeded random stream per key; `video-clone/styles/_TEMPLATE.md`: a "known pitfalls" list per style) | MIT (ReelMimic, notice below; the crayon-storybook runtime is derived from painted-animation / ClaudeAnimationBase, MIT) | `src/TMConcept.Core/Plans/Files/plan.js` (the finishing canvas), `src/TMConcept.Core/Styles/Concrete/DrawStyles.cs` (pitfalls and finishing per style), `src/TMConcept.Core/Render/Concrete/RenderFinishBuilder.cs` (0.1.43). Changed: the numbers and ideas only; the layer, its noise, its random stream (FNV-1a, mulberry32, SplitMix64) and every pitfall text are written for TM Concept. The watercolour and pastel engines (p5.js, p5.brush) are not used. |
| The ducking numbers of ReelMimic's media-use skill (github.com/edenfunf/reelmimic, `.claude/skills/media-use/scripts/lib/duck.mjs`: speech parts closer than 0.6 s are one, duck to 0.25×, 0.15 s down, 0.4 s back; `media-use/audio/scripts/lib/bgm-volume.mjs`: a music bed of 0.12× under a voice) and the idea of an asset record per project (`video-clone/scripts/fetch_assets.py`: name, source, licence, attribution) | Apache-2.0 (the ducking numbers) and MIT (the asset record idea), ReelMimic | `src/TMConcept.Core/Styles/Constant/MusicMixDefaults.cs`, `src/TMConcept.Core/Media/Concrete/MusicEnvelope.cs`, `src/TMConcept.Core/Videos/Concrete/AssetLedger.cs` (0.1.44). Changed: the numbers and ideas only; no text or code of them is included. The envelope is an ffmpeg `volume` expression written for TM Concept, the spoken parts come from TM Concept's own silence measure, and the asset record is its own JSON file. |
| The beat grid method of ReelMimic's painted-animation `beat_grid.py` (github.com/edenfunf/reelmimic, `.claude/skills/painted-animation/scripts/beat_grid.py`: spectral flux of 2048-sample Hann frames every 128 samples at 22 050 Hz, a 64-frame local mean taken off, the tempo between 70 and 170 bpm by autocorrelation, the phase whose comb of beats collects the most onset energy) and the entry idea of its video-clone `analyze.py` (`.claude/skills/video-clone/scripts/analyze.py`, `audio()`: the densest window as the start) | MIT (ReelMimic, notice below) | `src/TMConcept.Core/Music/Concrete/SpectrumFlux.cs`, `BeatTracker.cs`, `MusicBedPlanner.cs`, `src/TMConcept.Core/Music/Constant/MusicLibraryRules.cs` (0.1.45). Changed: the method is written again in C# (no Python, no numpy; ffmpeg reads the samples); the tempo is refined around the autocorrelation's peak by the comb itself, the phase search is smoothed and placed where a sound is heard; the entry compares an opening on a bar line with the 5-second parts after it (an idea of ReelMimic's background-music notes, Apache-2.0; no text or code of them is included). |
| The ready music library's catalog: titles, page and file addresses, tempos and lengths of Kevin MacLeod's tracks read from incompetech.com on 24 Nov 2026 | The tracks are CC BY 4.0 (Kevin MacLeod, incompetech.com); they are not in the app or in this repository and are downloaded by the user's choice | `catalog/music.json` (0.1.45). The tags are TM Concept's own words; every video that uses a track records its attribution in its `assets.json` and shows it on the video page. |
| Some entries of the built-in Turkish pronunciation list and the Turkish letter names of abbreviations, cross-checked with the word lists of trnorm (`site/js/trnorm/lexicon.js` of the same repository) | Apache-2.0 (trnorm) | `src/TMConcept.Core/Pronunciation/Files/tr.txt`, `src/TMConcept.Core/Pronunciation/Constant` |

The font license texts are shipped as files next to the program and in the Linux archive (`licenses/`). Fonts installed on the user's computer (for example Times New Roman) are never shipped: a template may use one, and a video made on that computer copies its files into the video's work folder only.

### ReelMimic (MIT)

```
MIT License

Copyright (c) 2026 ReelMimic contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Ideas read, not copied

The drawing styles of 0.1.32 (`src/TMConcept.Core/Styles/Concrete/DrawStyles.cs`) were written for TM Concept after reading the
documentation of these MIT-licensed projects as recipes; no code, prompt text or asset of theirs is included: ersinkoc/AI-Video-Factory
(entrance length limits, paper slide-in with a fixed shadow, isometric outline-then-fill, texture layers, a moving hook),
deenukhan/animation-styles and cth9191/animate (blueprint, hologram, terminal and pencil-sketch looks) and kaomei/stickman-video-director
(a stick-figure presenter, setup → action → reaction, a visual change every two to three seconds).

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
| ONNX Runtime (Microsoft.ML.OnnxRuntime with its native library; runs the documents' meaning model on the processor, 0.1.21, and the local OCR of scanned pages, 0.1.25) | MIT |
| Velopack (the installed edition's install, shortcuts and updates, 0.1.26; the portable program carries the library but does not use it) | MIT |

The installed edition (0.1.26) is installed by Velopack's Setup and updated by its `Update.exe` (both part of Velopack, MIT, github.com/velopack/velopack,
put into `TMConcept-win-x64-Setup.exe` by the `vpk` packing tool at the same version as the library). The Linux AppImage, when one is
published, starts with the AppImage type 2 runtime (MIT, github.com/AppImage/type2-runtime) that `vpk` adds; `mksquashfs`, which `vpk`
uses to build it, only runs on the build machine and is not distributed.

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
| yazgı-tts-1 (erkamk/yazgi-tts-1: weights, voices and the yazgi_tts package wheel, pinned to a commit) and normalizer-tr 0.7.0 | Apache-2.0 (both) | huggingface.co/erkamk/yazgi-tts-1 and PyPI, into the app's Python voice environment (0.1.35; the weights are plain tensors loaded with torch.load(weights_only=True)) |
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
| PaddleOCR PP-OCRv5 models in RapidOCR's ONNX form: the mobile text line detector (`onnx/PP-OCRv5/det/ch_PP-OCRv5_det_mobile.onnx`, 4.8 MB) and the Latin-script recogniser (`onnx/PP-OCRv5/rec/latin_PP-OCRv5_rec_mobile.onnx`, 7.9 MB), ModelScope RapidAI/RapidOCR tag v3.10.0 | Apache-2.0 (PaddleOCR; RapidOCR) | The local OCR of scanned PDF pages (optional, 0.1.25); www.modelscope.cn, each file with its SHA-256. Run in the program with the bundled ONNX Runtime, on the processor; the Latin recogniser reads Turkish (ç, ğ, ı, İ, ö, ş, ü) and the other Latin-script languages. |
| PDFium 159.0.8138.0 (bblanchon/pdfium-binaries release `chromium/8138`; `pdfium.dll` on Windows x64, `libpdfium.so` on Linux x64 and ARM64; built without JavaScript and XFA) | BSD-3-Clause (PDFium); MIT (the build scripts); the parts compiled in under their own permissive licences: FreeType (FTL), libjpeg-turbo (IJG, BSD-3-Clause, zlib), libpng, zlib, OpenJPEG (BSD-2-Clause), Little CMS (MIT), HarfBuzz (MIT), ICU (Unicode licence), Abseil (Apache-2.0), Highway (Apache-2.0 / BSD-3-Clause), simdutf and fast_float (MIT / Apache-2.0), dragonbox (Apache-2.0 with LLVM exception / Boost), LLVM libc (Apache-2.0 with LLVM exception), AGG 2.3 | Draws PDF pages as pictures for the local OCR and the vision models (optional, 0.1.27); github.com release archive with its SHA-256. The archive's `LICENSE` and `licenses/` texts are kept next to the library in the tools folder. Loaded by the program from that folder. |
| ONNX Runtime 1.24.4, DirectML build (`onnxruntime.dll` of NuGet `Microsoft.ML.OnnxRuntime.DirectML`) and DirectML 1.15.4 (`DirectML.dll` of NuGet `Microsoft.AI.DirectML`) | MIT (ONNX Runtime, with its third-party notices); **Microsoft DirectML licence terms** (DirectML: redistributable, not open source) | The local OCR on the graphics card, Windows x64 only (optional, 0.1.27; offered once when the local OCR first reads, "Text recognition" Automatic). Fetched from api.nuget.org entry by entry (only these files and their licence texts, each unpacked file checked by its SHA-256). Loaded by the program from the tools folder; the bundled ONNX Runtime 1.31.1 above stays the one used for everything else. DirectML is the one exception to the permissive-licence rule, taken in on purpose with the 0.1.27 plan (text recognition on any make of graphics card): it is never bundled, and its download question links Microsoft's terms (https://www.nuget.org/packages/Microsoft.AI.DirectML/1.15.4/License) and says that agreeing accepts them; a copy is kept next to it as `LICENSE-DirectML.txt`. |
| paraphrase-multilingual-MiniLM-L12-v2 (sentence-transformers), int8 ONNX file and tokenizer | Apache-2.0 | The local model of the duplicate check by meaning; huggingface.co, fixed commit with SHA-256. Run with the ONNX Runtime of the sherpa-onnx libraries above, in the worker process. |
| stable-diffusion.cpp master-1106-532d2c1 (Windows) and master-1043-3be2378 (Linux; the last release built on Ubuntu 24.04) (sd-cli) | MIT | github.com/leejet/stable-diffusion.cpp releases: the Windows CUDA 12 build with its CUDA runtime archive (NVIDIA CUDA runtime libraries under the NVIDIA CUDA EULA, as published in that release; TM Concept does not distribute them), the Windows Vulkan and processor builds, the Linux x64 Vulkan and processor builds (one is downloaded, by the Image acceleration setting). Runs the local image models as a separate process. |
| Z-Image Turbo (GGUF Q3_K / Q8_0 by leejet; BF16 by Comfy-Org) | Apache-2.0 | Tongyi-MAI. With its VAE (Comfy-Org, Apache-2.0) and Qwen3-4B-Instruct-2507 (Qwen, Apache-2.0; GGUF Q4_K_M / Q8_0 by unsloth; BF16 Qwen3-4B by Comfy-Org). huggingface.co |
| FLUX.2 klein 4B (GGUF Q4_0 / Q8_0 by leejet) | Apache-2.0 | Black Forest Labs. With the FLUX.2 VAE (the Apache-2.0 copy of Comfy-Org/Flux-2-Klein-4B, not the non-commercial FLUX.2 dev one) and Qwen3-4B (Qwen, Apache-2.0; GGUF by unsloth). huggingface.co |
| FLUX.2 klein 9B (GGUF Q4_0 / Q8_0 by leejet) | **FLUX Non-Commercial License** (Black Forest Labs) | **Not for commercial use.** Offered with an orange "non-commercial" badge; the app asks you to accept the license before the first download. With the FLUX.2 VAE above and Qwen3-8B (Qwen, Apache-2.0; GGUF by unsloth). huggingface.co (black-forest-labs/FLUX.2-klein-9B) |
| FLUX.3 Image Klein (GGUF Q4_K / Q8_0 by leejet) | **FLUX Kommunity License v1.0** (Black Forest Labs) | Commercial use under conditions: by Black Forest Labs' announcement free for individuals and companies under 5 million USD annual revenue (the license text on huggingface.co/black-forest-labs/flux-3-image-klein is the binding text). Asked before the first download. With the FLUX.2 VAE above and Qwen3-VL-4B-Instruct (Qwen, Apache-2.0; GGUF by Qwen). Needs stable-diffusion.cpp build 968 or newer. |
| Stable Diffusion 3.5 Medium (GGUF Q4_0 / Q8_0 with CLIP-L, CLIP-G and T5-XXL GGUF by second-state) | **Stability AI Community License** | Free, also commercially, for individuals and organizations under 1 million USD annual revenue; above that an enterprise license from Stability AI. Asked before the first download. huggingface.co (stabilityai/stable-diffusion-3.5-medium) |
| SDXL Lightning 4-step (GGUF q4_0 / q8_0 by mzwing) | **OpenRAIL++** (ByteDance) | Commercial use allowed with the license's use restrictions. Asked before the first download. With the SDXL fp16-fix VAE (madebyollin, MIT). huggingface.co |
| Qwen-Image (GGUF Q4_0 / Q6_K / Q8_0 by QuantStack) | Apache-2.0 | Qwen. With its VAE (Comfy-Org, Apache-2.0) and Qwen2.5-VL-7B-Instruct (Qwen, Apache-2.0; GGUF by mradermacher). huggingface.co |
| Qwen-Image 2.1 (GGUF Q8_0 by leejet) and Qwen-Image 2.1 Turbo (GGUF Q4_K / Q8_0 by AtomicChat) | **Qwen Research License** (Qwen) | **Not for commercial use** (research or evaluation only; commercial use needs a separate license from Qwen). Offered with an orange "non-commercial" badge; the app asks you to accept the license before the first download. With its own VAE (Comfy-Org copy, same license) and Qwen3-VL-8B-Instruct (Qwen, Apache-2.0; GGUF by Qwen). Needs stable-diffusion.cpp build 959 or newer. huggingface.co (Qwen/Qwen-Image-2.1) |
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
| rakkase (github.com/alexturkey/rakkase at commit 51b1375) and ACE-Step 1.5 (github.com/ACE-Step/ACE-Step-1.5 at commit ca1e85f) | MIT (both) | The local music model (0.1.46): GitHub's source archives of those commits, each with its SHA-256; ACE-Step's packages are installed by uv from its own `uv.lock` (PyTorch 2.7.1 for CUDA 12.8 and the rest, each under its own licence). Never part of the app. |
| ACE-Step 1.5 weights (ACE-Step/Ace-Step1.5 at revision 19671f4: the turbo DiT, the VAE) with the Qwen3-Embedding-0.6B text encoder | MIT (ACE-Step; its model card states that generated music may be used commercially), Apache-2.0 (Qwen3-Embedding) | huggingface.co, the files of rakkase's pinned list, each with its SHA-256. |
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
