# Third-party notices

TM Concept is MIT licensed. It includes or uses the following third-party material.

## Included in this repository

| Component | License | Where |
|---|---|---|
| Manrope font | SIL Open Font License 1.1 | `src/TMConcept.Desktop/Assets/Fonts`, license text in `src/TMConcept.Desktop/Assets/Licenses/font-manrope.txt` |
| Space Grotesk font | SIL Open Font License 1.1 | `src/TMConcept.Desktop/Assets/Fonts`, license text in `src/TMConcept.Desktop/Assets/Licenses/font-space-grotesk.txt` |
| Engine package lists (`package.json`, `package-lock.json`) | — (lists of versions, not the packages) | `src/TMConcept.Engines/Packages` |

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

Exact versions are in `Directory.Packages.props`. Since 0.1.3 the speech libraries (sherpa-onnx, ONNX Runtime) are no longer bundled; they
are downloaded like the other tools (below).

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
| paraphrase-multilingual-MiniLM-L12-v2 (sentence-transformers), int8 ONNX file and tokenizer | Apache-2.0 | The local model of the duplicate check by meaning; huggingface.co, fixed commit with SHA-256. Run with the ONNX Runtime of the sherpa-onnx libraries above, in the worker process. |
| stable-diffusion.cpp master-890-74988b2 (sd-cli) | MIT | github.com/leejet/stable-diffusion.cpp releases: the Windows CUDA 12 build with its CUDA runtime archive (NVIDIA CUDA runtime libraries under the NVIDIA CUDA EULA, as published in that release; TM Concept does not distribute them), the Linux x64 Vulkan build. Runs the local image models as a separate process. |
| FLUX.2 klein 4B (GGUF Q4_0 by leejet) | Apache-2.0 | Black Forest Labs. Only the 4B model is used: the 9B model has a non-commercial license. With the FLUX.2 VAE (Comfy-Org, Apache-2.0) and Qwen3-4B (Qwen, Apache-2.0; GGUF by unsloth). huggingface.co |
| Z-Image Turbo (GGUF Q8_0 by leejet) | Apache-2.0 | Tongyi-MAI. With its VAE (Comfy-Org, Apache-2.0) and Qwen3-4B-Instruct-2507 (Qwen, Apache-2.0; GGUF by unsloth). huggingface.co |

### Supertonic 3 (OpenRAIL-M) use restrictions, short form

You agree not to use the model or its output: to break the law; to harm minors; to generate or spread false information meant to harm
others; to generate or spread personal data that can identify a person without permission; to impersonate a person (for example a voice
copy); to publish machine-generated content without clearly saying it is machine-generated; to discriminate against or harm people or
groups; for fully automated decisions that affect people's legal rights; or in ways that exploit the vulnerabilities of a group. This is a
summary; the license text of the model (Supertone Inc.) is the binding text. TM Concept therefore marks videos with an "AI voice — say so
when publishing" note.

### Not used

No voice cloning, no reference voice, no reading of other channels (no yt-dlp), no Piper GPL program. The optional Piper Turkish voice of
0.1.0 was removed in 0.1.1 (sherpa-onnx 2.x has no espeak-ng to turn text into its phonemes). On Linux the player uses the system's
libpulse-simple (PulseAudio / PipeWire, LGPL-2.1, a system library that is not distributed with TM Concept).
