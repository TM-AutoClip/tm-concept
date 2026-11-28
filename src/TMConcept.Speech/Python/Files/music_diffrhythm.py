"""TM Concept: one DiffRhythm piece (28 Nov 2026, run by DiffRhythmGenerator / DiffRhythmLegacyGenerator with the app's diffrhythm environment).

Runs ASLP-lab's DiffRhythm from its pinned source folder (--code; 1.2 or 1.0 code, --code-version) with the model's own folder (--model:
cfm_model.pt) and the shared files (--shared: vae/, muq-mulan/, muq-msd/, xlm-roberta-base/), offline. Instrumental only: no words are
given, so DiffRhythm's phonemizer (which needs espeak-ng) is never built. To fit a small card, MuQ-MuLan encodes the prompt on the processor
and is freed, the transformer is cast to 16 bits on the processor before it moves to the card, and it is freed before the VAE decodes in
chunks. A base model (--frames 2048) makes exactly 95 s; a full one (--frames 6144) 96-285 s with the 1.2 code, exactly 285 s with the 1.0
code; the piece is cut to --seconds, peak-normalised and written as a 16-bit 44.1 kHz stereo WAV. Stages are printed as "TMC: ..." lines, a
failure as one "TMC-E-<CODE>: ..." line before exit code 2. Progress (0.1.61): stage 1 the sampler's steps (each call of the flow function
torchdiffeq's Euler solver makes between two of the 32 time points: 31), counted by wrapping the odeint DiffRhythm's model code calls, from
outside its file; stage 2 the VAE's decoding, without a count (tmc_progress).
"""
import argparse
import gc
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from tmc_music import fail, report, stage  # noqa: E402  (after the path above; the script runs with -I)
from tmc_progress import Reporter  # noqa: E402

SAMPLE_RATE = 44100
DOWNSAMPLE = 2048


def load_mulan(shared):
    """MuQ-MuLan built from its config with its two inner models pointed at the downloaded folders (the names must still contain "muq" and
    "xlm-roberta", which its code checks), then its own weights loaded over them, on the processor."""
    import torch
    from muq import MuQMuLan

    with open(os.path.join(shared, "muq-mulan", "config.json"), encoding="utf-8") as handle:
        config = json.load(handle)
    config["audio_model"]["name"] = os.path.join(shared, "muq-msd")
    config["text_model"]["name"] = os.path.join(shared, "xlm-roberta-base")
    model = MuQMuLan(config)
    state = torch.load(os.path.join(shared, "muq-mulan", "pytorch_model.bin"), map_location="cpu", weights_only=True)
    model.load_state_dict(state, strict=False)
    return model.eval()


def follow_steps(progress):
    """The sampler's steps: DiffRhythm's model code calls torchdiffeq's odeint by the name it imported, so that name is wrapped (its file is
    not changed); the flow function is counted at each call."""
    import model.cfm as code

    real = code.odeint

    def odeint(func, y0, t, **kwargs):
        progress.begin(1, "steps", max(1, len(t) - 1))
        calls = {"n": 0}

        def counted(*args, **inner):
            out = func(*args, **inner)
            calls["n"] += 1
            progress.update(calls["n"])
            return out

        return real(counted, y0, t, **kwargs)

    code.odeint = odeint


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--prompt-file", required=True)
    parser.add_argument("--seconds", type=float, required=True)
    parser.add_argument("--seed", type=int, required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--code", required=True)
    parser.add_argument("--shared", required=True)
    parser.add_argument("--frames", type=int, choices=(2048, 6144), required=True)
    parser.add_argument("--code-version", choices=("1.2", "1.0"), required=True)
    args = parser.parse_args()

    with open(args.prompt_file, encoding="utf-8") as handle:
        prompt = handle.read().strip()
    if not prompt:
        fail("PROMPT", "the prompt is empty")
    for need in (os.path.join(args.model, "cfm_model.pt"), os.path.join(args.shared, "vae", "vae_model.pt"),
                 os.path.join(args.shared, "muq-mulan", "pytorch_model.bin"), os.path.join(args.shared, "muq-msd", "model.safetensors"),
                 os.path.join(args.shared, "xlm-roberta-base", "model.safetensors"), os.path.join(args.code, "infer", "infer_utils.py")):
        if not os.path.isfile(need):
            fail("MISSING", "a file of the model is missing: " + need)

    # DiffRhythm reads infer/example/vocal.npy and config/diffrhythm-1b.json by relative path and imports "model" from its folder.
    os.chdir(args.code)
    sys.path.insert(0, args.code)
    sys.path.insert(0, os.path.join(args.code, "infer"))
    stage("loading PyTorch and DiffRhythm")
    import numpy as np
    import soundfile as sf
    import torch

    if not torch.cuda.is_available():
        fail("CUDA", "PyTorch sees no NVIDIA graphics card (CUDA); DiffRhythm runs only there in the app")
    from infer_utils import decode_audio, get_lrc_token
    from model import CFM, DiT

    progress = Reporter(2)
    follow_steps(progress)

    torch.manual_seed(args.seed)
    np.random.seed(args.seed % (2 ** 32))
    device = "cuda"
    full = args.frames == 6144
    if not full:
        length = 95
    elif args.code_version == "1.0":
        length = 285
    else:
        length = int(min(285, max(96, round(args.seconds))))

    stage("encoding the prompt with MuQ-MuLan (processor)")
    mulan = load_mulan(args.shared)
    with torch.no_grad():
        style = mulan(texts=[prompt]).half().to(device)
    del mulan
    gc.collect()
    negative = torch.from_numpy(np.load(os.path.join("infer", "example", "vocal.npy"))).half().to(device)

    stage("loading the transformer")
    with open(os.path.join("config", "diffrhythm-1b.json"), encoding="utf-8") as handle:
        config = json.load(handle)
    cfm = CFM(transformer=DiT(**config["model"], max_frames=args.frames), num_channels=config["model"]["mel_dim"], max_frames=args.frames)
    state = torch.load(os.path.join(args.model, "cfm_model.pt"), map_location="cpu", weights_only=True)
    state = state.get("model_state_dict", state)
    cfm = cfm.half()
    missing, _ = cfm.load_state_dict(state, strict=False)
    if len(missing) * 2 > len(state):
        fail("WEIGHTS", "the checkpoint does not fit this code (%d missing keys, e.g. %s)" % (len(missing), missing[0]))
    if missing:
        stage("warning: %d keys not in the checkpoint (e.g. %s)" % (len(missing), missing[0]))
    del state
    cfm = cfm.to(device).eval()

    stage("making %d s" % length)
    cond = torch.zeros(1, args.frames, 64, device=device)
    with torch.inference_mode():
        if args.code_version == "1.2":
            lrc, start_time, end_frame, song_duration = get_lrc_token(args.frames, "", None, length, device)
            latents, _ = cfm.sample(cond=cond, text=lrc, duration=end_frame, style_prompt=style, max_duration=end_frame,
                                    song_duration=song_duration, negative_style_prompt=negative, steps=32, cfg_strength=4.0,
                                    start_time=start_time, latent_pred_segments=[(0, args.frames)], batch_infer_num=1, seed=args.seed)
            latent = latents[0]
        else:
            lrc, start_time = get_lrc_token(args.frames, "", None, device)
            latent, _ = cfm.sample(cond=cond, text=lrc, duration=args.frames, style_prompt=style, negative_style_prompt=negative,
                                   steps=32, cfg_strength=4.0, start_time=start_time, seed=args.seed)
    latent = latent.to(torch.float32).transpose(1, 2)
    del cfm
    gc.collect()
    torch.cuda.empty_cache()

    progress.finish()
    stage("decoding")
    progress.begin(2, "decoding")
    vae = torch.jit.load(os.path.join(args.shared, "vae", "vae_model.pt"), map_location="cpu").to(device)
    with torch.inference_mode():
        audio = decode_audio(latent, vae, chunked=True)
    audio = audio[0].float().cpu()
    peak = torch.max(torch.abs(audio))
    audio = (audio / peak if peak > 0 else audio).clamp(-1, 1)

    stage("writing the piece")
    audio = audio[:, : max(1, int(min(args.seconds, length) * SAMPLE_RATE))]
    sf.write(args.out, audio.numpy().T, SAMPLE_RATE, subtype="PCM_16")
    stage("done")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception as error:  # noqa: BLE001 - every failure is reported in one line
        report(error)
