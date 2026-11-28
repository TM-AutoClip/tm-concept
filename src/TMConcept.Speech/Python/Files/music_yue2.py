"""TM Concept: one YuE2 piece (28 Nov 2026, run by Yue2Generator with the app's yue2 environment).

Runs YuE2 from its pinned source folder (--code: src/yue2, never installed as a package) with the model's and the VAE's folders (--weights:
model/ and vae/, their own code files pinned with them), offline, on the card with the given memory budget (--budget GiB; YuE2 keeps 2 GiB
of it in reserve) and, with --low-vram, its low-memory mode (only the active stage on the card). The prompt is its style, the words
(--lyrics-file) its lyrics; an empty file makes an instrumental piece. Full chain of thought, seeded with --seed. The 48 kHz stereo song is
cut to --seconds and written as a 16-bit WAV. Stages are printed as "TMC: ..." lines, a failure as one "TMC-E-<CODE>: ..." line before exit
code 2.
"""
import argparse
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from tmc_music import fail, report, stage  # noqa: E402  (after the path above; the script runs with -I)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--prompt-file", required=True)
    parser.add_argument("--lyrics-file", required=True)
    parser.add_argument("--seconds", type=float, required=True)
    parser.add_argument("--seed", type=int, required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--code", required=True)
    parser.add_argument("--weights", required=True)
    parser.add_argument("--budget", type=float, required=True)
    parser.add_argument("--low-vram", action="store_true")
    args = parser.parse_args()

    with open(args.prompt_file, encoding="utf-8") as handle:
        style = handle.read().strip()
    with open(args.lyrics_file, encoding="utf-8") as handle:
        lyrics = handle.read().strip()
    if not style:
        fail("PROMPT", "the prompt is empty")
    model = os.path.join(args.weights, "model")
    vae = os.path.join(args.weights, "vae")
    for need in (os.path.join(model, "model.safetensors"), os.path.join(model, "modeling_yue2.py"), os.path.join(vae, "model.safetensors"),
                 os.path.join(args.code, "src", "yue2", "pipeline.py")):
        if not os.path.isfile(need):
            fail("MISSING", "a file of the model is missing: " + need)

    sys.path.insert(0, os.path.join(args.code, "src"))
    stage("loading PyTorch and YuE2")
    import numpy as np
    import soundfile as sf
    import torch

    if not torch.cuda.is_available():
        fail("CUDA", "PyTorch sees no NVIDIA graphics card (CUDA); YuE2 runs only there in the app")
    from yue2.pipeline import YuE2Pipeline

    stage("loading the model" + (" (low-memory mode)" if args.low_vram else ""))
    pipe = YuE2Pipeline.from_pretrained(model, vae=vae, local_files_only=True, progress=False, device="cuda",
                                        memory_budget_gib=args.budget, low_vram=args.low_vram)
    try:
        stage("making the song (%s)" % ("with words" if lyrics else "instrumental"))
        song = pipe(style=style, lyrics=lyrics, cot="full", seed=args.seed % (2 ** 63))
    finally:
        close = getattr(pipe, "close", None)
        if close is not None:
            close()

    stage("writing the piece")
    audio = np.asarray(song.audio, dtype=np.float32)
    rate = int(song.sample_rate)
    audio = audio[: max(1, int(args.seconds * rate))]
    sf.write(args.out, np.clip(audio, -1, 1), rate, subtype="PCM_16")
    if getattr(song, "truncated", False):
        stage("warning: YuE2 stopped at its token limit")
    stage("done")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception as error:  # noqa: BLE001 - every failure is reported in one line
        report(error)
