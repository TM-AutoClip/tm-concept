"""TM Concept: one AudioLDM 2 piece (28 Nov 2026, run by AudioLdm2Generator with the app's audioldm2 environment).

Loads the diffusers AudioLDM2Pipeline from the model's folder in float16 (offline), on a card of 8 GB or more whole, on a smaller one with
accelerate's model offload (only the working part on the card), switches to the DPM-Solver++ scheduler, makes one piece and writes it as a
16-bit WAV (16 kHz, mono). Stages are printed as "TMC: ..." lines, a failure as one "TMC-E-<CODE>: ..." line before exit code 2.
"""
import argparse
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from tmc_music import fail, report, stage  # noqa: E402  (after the path above; the script runs with -I)

# A card below this much memory gets the model offload (AudioLDM 2 Large holds about 3 GB of weights in float16).
WHOLE_ON_CARD_BYTES = 7.5 * 1024 ** 3
NEGATIVE = "Low quality."


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--prompt-file", required=True)
    parser.add_argument("--seconds", type=float, required=True)
    parser.add_argument("--seed", type=int, required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--steps", type=int, default=50)
    parser.add_argument("--guidance", type=float, default=3.5)
    args = parser.parse_args()

    with open(args.prompt_file, encoding="utf-8") as handle:
        prompt = handle.read().strip()
    if not prompt:
        fail("PROMPT", "the prompt is empty")
    if not os.path.isfile(os.path.join(args.model, "model_index.json")):
        fail("MISSING", "the model folder has no model_index.json: " + args.model)

    stage("loading PyTorch")
    import numpy as np
    import soundfile as sf
    import torch
    from diffusers import AudioLDM2Pipeline, DPMSolverMultistepScheduler

    if not torch.cuda.is_available():
        fail("CUDA", "PyTorch sees no NVIDIA graphics card (CUDA)")

    stage("loading the model")
    pipe = AudioLDM2Pipeline.from_pretrained(args.model, torch_dtype=torch.float16, use_safetensors=True, local_files_only=True)
    pipe.scheduler = DPMSolverMultistepScheduler.from_config(pipe.scheduler.config)
    pipe.set_progress_bar_config(disable=True)
    total = torch.cuda.get_device_properties(0).total_memory
    if total < WHOLE_ON_CARD_BYTES:
        stage("model offload (a %.1f GB card)" % (total / 1024 ** 3))
        pipe.enable_model_cpu_offload()
    else:
        pipe.to("cuda")

    generator = torch.Generator("cuda").manual_seed(args.seed)
    stage("making %d s in %d steps" % (round(args.seconds), args.steps))
    with torch.inference_mode():
        audio = pipe(prompt, negative_prompt=NEGATIVE, num_inference_steps=args.steps, audio_length_in_s=args.seconds, guidance_scale=args.guidance,
                     num_waveforms_per_prompt=1, generator=generator).audios[0]

    stage("writing the piece")
    samples = np.asarray(audio, dtype=np.float32)
    peak = float(np.max(np.abs(samples))) if samples.size else 0.0
    if peak > 1.0:
        samples = samples / peak
    sf.write(args.out, samples, pipe.vocoder.config.sampling_rate, subtype="PCM_16")
    stage("done")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception as error:  # noqa: BLE001 - every failure is reported in one line
        report(error)
