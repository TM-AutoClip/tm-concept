"""TM Concept: one YuE 1 piece (28 Nov 2026, run by YueGenerator with the app's yue environment).

Runs YuE-exllamav2's three stages from its pinned source folder (--code: src/yue) as child processes of this Python, in the piece's folder:
stage 1 (--stage1: the 7B or 0.5B model, or with --exl2 its 4.25-bit exllamav2 quantisation with a Q4 cache), stage 2 (--stage2: the 1B model,
or with --exl2 its 8-bit quantisation with a Q8 cache) and the post-processing with the codec's weights (--xcodec). Two segments. The prompt
is its genre tags; the words (--lyrics-file) become its sections ("[verse]", "[chorus]"...: the user's own tags are kept, plain paragraphs
alternate verse and chorus); an empty file gives an instrumental structure with "instrumental" added to the tags. The post-processing runs
through this script again (--post) so that its MP3 files are written and read as WAV with soundfile (no FFmpeg needed). The final mix is cut to
--seconds and written as a 16-bit WAV. Stages are printed as "TMC: ..." lines, a failure as one "TMC-E-<CODE>: ..." line before exit code 2.
"""
import argparse
import glob
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from tmc_music import blocks, fail, report, stage  # noqa: E402  (after the path above; the script runs with -I)


def sections(text):
    """The user's words as YuE's sections, one "[tag]" line before each block, ending with a newline."""
    found, _ = blocks(text)
    return "".join("[" + (re.sub(r"[^a-z0-9]+", "_", tag.lower()).strip("_") or "verse") + "]\n" + "\n".join(lines) + "\n\n" for tag, lines in found)


def run_stage(name, command, work):
    """Runs one stage, passing its output on (the log's tail); a card out of memory in it is a memory failure."""
    stage(name)
    process = subprocess.Popen(command, cwd=work, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
    memory = False
    last = ""
    for line in process.stdout:
        sys.stdout.write(line)
        sys.stdout.flush()
        if line.strip():
            last = line.strip()
        if "out of memory" in line.lower() or "OutOfMemoryError" in line:
            memory = True
    code = process.wait()
    if code != 0:
        if memory:
            fail("MEMORY", "the graphics card ran out of memory in " + name)
        fail("STAGE", name + " ended with " + str(code) + (": " + last[:300] if last else ""))


def post(script, rest):
    """The post-processing in this process, with torchaudio's save and load going through soundfile as WAV."""
    import runpy

    import soundfile as sf
    import torch
    import torchaudio

    def save(path, tensor, sample_rate, *args, **kwargs):
        data = tensor.detach().cpu().float()
        data = data.unsqueeze(0) if data.dim() == 1 else data
        sf.write(str(path), data.numpy().T, int(sample_rate), format="WAV", subtype="PCM_16")

    def load(path, *args, **kwargs):
        data, rate = sf.read(str(path), dtype="float32", always_2d=True)
        return torch.from_numpy(data.T.copy()), rate

    torchaudio.save = save
    torchaudio.load = load
    sys.path.insert(0, os.path.dirname(script))
    sys.argv = [script] + rest
    runpy.run_path(script, run_name="__main__")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--prompt-file", required=True)
    parser.add_argument("--lyrics-file", required=True)
    parser.add_argument("--seconds", type=float, required=True)
    parser.add_argument("--seed", type=int, required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--code", required=True)
    parser.add_argument("--stage1", required=True)
    parser.add_argument("--stage2", required=True)
    parser.add_argument("--xcodec", required=True)
    parser.add_argument("--exl2", action="store_true")
    args = parser.parse_args()

    with open(args.prompt_file, encoding="utf-8") as handle:
        genres = handle.read().strip()
    with open(args.lyrics_file, encoding="utf-8") as handle:
        words = handle.read().strip()
    if not genres:
        fail("PROMPT", "the prompt is empty")
    yue = os.path.join(args.code, "src", "yue")
    for need in (os.path.join(yue, "infer_stage1.py"), os.path.join(args.stage1, "tokenizer.model"), os.path.join(args.stage2, "tokenizer.model"),
                 os.path.join(args.xcodec, "final_ckpt", "ckpt_00360000.pth"), os.path.join(args.xcodec, "decoders", "decoder_151000.pth")):
        if not os.path.isfile(need):
            fail("MISSING", "a file of the model is missing: " + need)

    work = os.path.dirname(os.path.abspath(args.out))
    output = os.path.join(work, "yue")
    genre_file = os.path.join(work, "genre.txt")
    lyrics_file = os.path.join(work, "yue-lyrics.txt")
    with open(genre_file, "w", encoding="utf-8") as handle:
        handle.write(genres if words else genres + " instrumental")
    with open(lyrics_file, "w", encoding="utf-8") as handle:
        handle.write(sections(words) if words else "[verse]\n\n[chorus]\n\n")

    common = [
        "--stage1_model", args.stage1, "--stage2_model", args.stage2, "--genre_txt", genre_file, "--lyrics_txt", lyrics_file,
        "--run_n_segments", "2", "--max_new_tokens", "3000", "--output_dir", output, "--cuda_idx", "0", "--seed", str(args.seed % (2 ** 32)),
        "--basic_model_config", os.path.join(args.xcodec, "final_ckpt", "config.yaml"),
        "--resume_path", os.path.join(args.xcodec, "final_ckpt", "ckpt_00360000.pth"),
        "--config_path", os.path.join(args.xcodec, "decoders", "config.yaml"),
        "--vocal_decoder_path", os.path.join(args.xcodec, "decoders", "decoder_131000.pth"),
        "--inst_decoder_path", os.path.join(args.xcodec, "decoders", "decoder_151000.pth"),
        "--rescale",
    ]
    if args.exl2:
        common += ["--stage1_use_exl2", "--stage2_use_exl2", "--stage1_cache_mode", "Q4", "--stage2_cache_mode", "Q8", "--stage2_cache_size", "32768"]
    else:
        common += ["--stage2_batch_size", "4"]
    # -s: no user site packages; the stage scripts import their neighbours, so their own folder stays first on the path.
    python = [sys.executable, "-s", "-X", "utf8"]
    run_stage("stage 1 (%s)" % ("exllamav2" if args.exl2 else "16-bit"), python + [os.path.join(yue, "infer_stage1.py")] + common, work)
    run_stage("stage 2", python + [os.path.join(yue, "infer_stage2.py")] + common, work)
    run_stage("decoding", python + [os.path.abspath(__file__), "--post", os.path.join(yue, "infer_postprocess.py")] + common, work)

    mixes = sorted(glob.glob(os.path.join(output, "*.mp3")), key=os.path.getmtime)
    if not mixes:
        fail("OUTPUT", "YuE wrote no mix")
    stage("writing the piece")
    import numpy as np
    import soundfile as sf

    audio, rate = sf.read(mixes[-1], dtype="float32", always_2d=True)
    audio = audio[: max(1, int(args.seconds * rate))]
    sf.write(args.out, np.clip(audio, -1, 1), rate, subtype="PCM_16")
    stage("done")


if __name__ == "__main__":
    try:
        if len(sys.argv) > 2 and sys.argv[1] == "--post":
            post(sys.argv[2], sys.argv[3:])
        else:
            main()
    except SystemExit:
        raise
    except Exception as error:  # noqa: BLE001 - every failure is reported in one line
        report(error)
