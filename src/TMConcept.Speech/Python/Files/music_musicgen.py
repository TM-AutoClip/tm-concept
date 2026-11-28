"""TM Concept: one MusicGen piece (28 Nov 2026, run by MusicGenGenerator with the app's musicgen environment).

Loads a MusicGen checkpoint from its folder with Hugging Face transformers (offline), makes one instrumental piece from the prompt and writes
it as a 16-bit WAV (32 kHz; two channels for the stereo models). Stages are printed as "TMC: ..." lines, a failure as one "TMC-E-<CODE>: ..."
line before exit code 2 (the app shows that line and logs the rest). MusicGen makes at most 30 s (1503 tokens) per call.
"""
import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from tmc_music import fail, report, stage  # noqa: E402  (after the path above; the script runs with -I)

MAX_TOKENS = 1503


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--prompt-file", required=True)
    parser.add_argument("--seconds", type=float, required=True)
    parser.add_argument("--seed", type=int, required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--guidance", type=float, default=3.0)
    args = parser.parse_args()

    with open(args.prompt_file, encoding="utf-8") as handle:
        prompt = handle.read().strip()
    if not prompt:
        fail("PROMPT", "the prompt is empty")
    if not os.path.isfile(os.path.join(args.model, "config.json")):
        fail("MISSING", "the model folder has no config.json: " + args.model)

    stage("loading PyTorch")
    import numpy as np
    import soundfile as sf
    import torch
    from transformers import AutoProcessor, MusicgenForConditionalGeneration, MusicgenMelodyForConditionalGeneration

    if not torch.cuda.is_available():
        fail("CUDA", "PyTorch sees no NVIDIA graphics card (CUDA)")
    torch.manual_seed(args.seed)
    with open(os.path.join(args.model, "config.json"), encoding="utf-8") as handle:
        kind = json.load(handle).get("model_type", "")
    model_class = MusicgenMelodyForConditionalGeneration if kind == "musicgen_melody" else MusicgenForConditionalGeneration
    # bfloat16 where the card has it (RTX 30 and newer); float16 otherwise, with the T5 text encoder kept in float32 (it overflows in float16).
    bf16 = torch.cuda.is_bf16_supported()
    dtype = torch.bfloat16 if bf16 else torch.float16

    stage("loading the model")
    processor = AutoProcessor.from_pretrained(args.model, local_files_only=True)
    model = model_class.from_pretrained(args.model, dtype=dtype, attn_implementation="sdpa", local_files_only=True)
    if not bf16:
        model.text_encoder.to(torch.float32)

        # The float32 T5 output goes on through enc_to_dec_proj (a float16 Linear: T5-base is 768 wide, every decoder wider), which takes
        # only the model's own dtype: cast it back as it leaves the encoder.
        def to_model_dtype(_module, _inputs, output):
            if isinstance(output, tuple):
                return (output[0].to(dtype),) + output[1:]
            output.last_hidden_state = output.last_hidden_state.to(dtype)
            return output

        model.text_encoder.register_forward_hook(to_model_dtype)
    model = model.to("cuda").eval()

    frame_rate = model.config.audio_encoder.frame_rate
    tokens = max(1, min(MAX_TOKENS, int(args.seconds * frame_rate) + 3))
    inputs = processor(text=[prompt], padding=True, return_tensors="pt").to("cuda")
    stage("making %d s (%d tokens)" % (round(args.seconds), tokens))
    with torch.inference_mode():
        audio = model.generate(**inputs, do_sample=True, guidance_scale=args.guidance, max_new_tokens=tokens)

    stage("writing the piece")
    samples = audio[0].float().cpu().numpy().T
    peak = float(np.max(np.abs(samples))) if samples.size else 0.0
    if peak > 1.0:
        samples = samples / peak
    sf.write(args.out, samples, model.config.audio_encoder.sampling_rate, subtype="PCM_16")
    stage("done")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception as error:  # noqa: BLE001 - every failure is reported in one line
        report(error)
