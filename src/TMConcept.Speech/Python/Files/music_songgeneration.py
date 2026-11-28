"""TM Concept: one SongGeneration piece (28 Nov 2026, run by SongGenerationGenerator with the app's songgeneration environment).

Runs Tencent's SongGeneration from its pinned source folder (--code) in the shared runtime folder (--runtime: ckpt/ and third_party/, which
the code reads by relative path) with the model's own folder (--model: model.pt and config.yaml, named songgeneration_<model>), offline,
always in its low-memory mode and without flash-attn. The prompt is its description (no prompt audio); the words (--lyrics-file) become
its sections ("[verse] line one. line two ; [chorus] ..."): the user's own [section] tags are kept, plain paragraphs alternate verse and
chorus between a short intro and outro; an empty file makes pure music (its "bgm" type). Every random source is seeded with --seed (upstream
seeds from the clock). The 48 kHz piece is cut to --seconds and written as a 16-bit WAV. Stages are printed as "TMC: ..." lines, a failure
as one "TMC-E-<CODE>: ..." line before exit code 2. Progress (0.1.61): stage 1 the generated tokens, from the tqdm bar of SongGeneration's
token loop (followed off screen, tmc_progress), stage 2 the decoding, without a count; the stages are told apart by wrapping CodecLM's generate
and generate_audio from outside its file.
"""
import argparse
import json
import os
import random
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from tmc_music import blocks, fail, report, stage  # noqa: E402  (after the path above; the script runs with -I)
from tmc_progress import Reporter, follow_tqdm  # noqa: E402


def sentence(line):
    """One sung line as SongGeneration wants it: half-width punctuation, ending with a full stop."""
    line = line.strip().replace("，", ",").replace("。", ".").replace(";", ",")
    return line.rstrip(".,!?") + "." if line else ""


def sections(text):
    """The user's words as SongGeneration's sections, joined with " ; " (plain paragraphs between a short intro and outro)."""
    found, tagged = blocks(text)
    parts = []
    for tag, lines in found:
        sung = " ".join(s for s in (sentence(l) for l in lines) if s)
        parts.append("[" + tag.lower().replace(" ", "-") + "]" + (" " + sung if sung else ""))
    return " ; ".join(parts if tagged else ["[intro-short]"] + parts + ["[outro-short]"])


def count_tokens(progress, bar):
    """The token loop's bar (only while the tokens are made: the decoding's own bars, if any, are not counted)."""
    if progress.stage == 1 and progress.unit == "tokens" and bar.total:
        progress.length(bar.total)
        progress.update(bar.n)


def follow_stages(progress, codec):
    """Stage 1 starts with CodecLM.generate (the tokens), stage 2 with the first CodecLM.generate_audio (the decoding)."""
    generate, decode = codec.generate, codec.generate_audio

    def tokens(self, *args, **kwargs):
        progress.begin(1, "tokens")
        made = generate(self, *args, **kwargs)
        progress.finish()
        return made

    def decoding(self, *args, **kwargs):
        if progress.stage != 2:
            progress.begin(2, "decoding")
        return decode(self, *args, **kwargs)

    codec.generate = tokens
    codec.generate_audio = decoding


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--prompt-file", required=True)
    parser.add_argument("--lyrics-file", required=True)
    parser.add_argument("--seconds", type=float, required=True)
    parser.add_argument("--seed", type=int, required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--code", required=True)
    parser.add_argument("--runtime", required=True)
    parser.add_argument("--version", choices=("v1", "v2"), required=True)
    args = parser.parse_args()

    with open(args.prompt_file, encoding="utf-8") as handle:
        prompt = handle.read().strip()
    with open(args.lyrics_file, encoding="utf-8") as handle:
        words = handle.read().strip()
    if not prompt:
        fail("PROMPT", "the prompt is empty")
    for need in (os.path.join(args.model, "model.pt"), os.path.join(args.model, "config.yaml"), os.path.join(args.code, "generate.py"),
                 os.path.join(args.code, "tools", "new_auto_prompt.pt"), os.path.join(args.runtime, "ckpt", "encode-s12k.pt"),
                 os.path.join(args.runtime, "third_party", "Qwen2-7B", "tokenizer.json")):
        if not os.path.isfile(need):
            fail("MISSING", "a file of the model is missing: " + need)

    code = os.path.abspath(args.code)
    runtime = os.path.abspath(args.runtime)
    work = os.path.dirname(os.path.abspath(args.out))
    # What upstream's generate.sh sets, without bash: the paths it puts first, its transformers cache, one thread for OpenMP and MKL.
    os.environ["TRANSFORMERS_CACHE"] = os.path.join(runtime, "third_party", "hub")
    os.environ["OMP_NUM_THREADS"] = "1"
    os.environ["MKL_NUM_THREADS"] = "1"
    for folder in (runtime, os.path.join(code, "codeclm", "tokenizer", "Flow1dVAE"), code, os.path.join(code, "codeclm", "tokenizer")):
        sys.path.insert(0, folder)
    os.chdir(runtime)

    stage("loading PyTorch and SongGeneration")
    import numpy as np
    import soundfile as sf
    import torch
    from omegaconf import OmegaConf

    if not torch.cuda.is_available():
        fail("CUDA", "PyTorch sees no NVIDIA graphics card (CUDA); SongGeneration runs only there")

    def from_code(path):
        """A relative path upstream reads from its own folder (conf/, tools/) when the runtime folder has no such file."""
        if isinstance(path, str) and not os.path.isabs(path) and not os.path.exists(path) and os.path.exists(os.path.join(code, path)):
            return os.path.join(code, path)
        return path

    real_load = torch.load
    torch.load = lambda path, *a, **k: real_load(from_code(path), *a, **k)
    OmegaConf.register_new_resolver("eval", lambda x: eval(x))  # noqa: S307 - upstream's own resolver, for its own config files
    OmegaConf.register_new_resolver("concat", lambda *x: [xxx for xx in x for xxx in xx])
    OmegaConf.register_new_resolver("get_fname", lambda: "generate")
    OmegaConf.register_new_resolver("load_yaml", lambda x: list(OmegaConf.load(from_code(x))))
    torch.backends.cudnn.enabled = False

    progress = Reporter(2)
    # Before upstream's modules import tqdm (its token loop's bar is followed from then on).
    follow_tqdm(lambda bar, depth: count_tokens(progress, bar))
    import generate as upstream
    from codeclm.models import CodecLM
    from codeclm.utils.offload_profiler import OffloadParamParse, OffloadProfiler

    follow_stages(progress, CodecLM)

    upstream.OffloadProfiler = OffloadProfiler
    upstream.OffloadParamParse = OffloadParamParse
    random.seed(args.seed)
    np.random.seed(args.seed % (2 ** 32))
    torch.manual_seed(args.seed)
    torch.cuda.manual_seed_all(args.seed)

    kind = "mixed" if words else "bgm"
    item = {"idx": "piece", "gt_lyric": sections(words) if words else "[intro-medium] ; [inst-medium] ; [outro-medium]", "descriptions": prompt}
    tasks = os.path.join(work, "songgeneration.jsonl")
    with open(tasks, "w", encoding="utf-8") as handle:
        handle.write(json.dumps(item, ensure_ascii=False) + "\n")
    save = os.path.join(work, "songgeneration")
    # generate.py picks nothing by name here (the low-memory function is called directly), but its paths are joined with "/".
    options = argparse.Namespace(ckpt_path=os.path.abspath(args.model).replace("\\", "/"), input_jsonl=tasks, save_dir=save.replace("\\", "/"),
                                 generate_type=kind, use_flash_attn=False, low_mem=True)
    stage("making the song (%s, %s)" % (args.version, "with words" if words else "pure music"))
    upstream.generate_lowmem(options, version=args.version)

    progress.finish()
    made = os.path.join(save, "audios", "piece.flac")
    if not os.path.isfile(made):
        fail("OUTPUT", "SongGeneration wrote no piece")
    stage("writing the piece")
    audio, rate = sf.read(made, dtype="float32", always_2d=True)
    audio = audio[: max(1, int(args.seconds * rate))]
    sf.write(args.out, np.clip(audio, -1, 1), rate, subtype="PCM_16")
    stage("done")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception as error:  # noqa: BLE001 - every failure is reported in one line
        report(error)
