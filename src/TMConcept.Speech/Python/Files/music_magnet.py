"""TM Concept: one MAGNeT piece (28 Nov 2026, run by MagnetGenerator with the app's magnet environment).

Runs Meta's audiocraft from its pinned source folder (--code; never installed as a package), with the checkpoint's own folder (--model:
state_dict.bin and compression_state_dict.bin) and the T5 text encoder folder audiocraft's T5 name points at (--t5-name t5-base or t5-large,
--t5 its folder), all offline. A checkpoint makes exactly its own length (10 or 30 s); the piece is cut to --seconds, its loudness normalised
and written as a 16-bit WAV. Stages are printed as "TMC: ..." lines, a failure as one "TMC-E-<CODE>: ..." line before exit code 2.
"""
import argparse
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from tmc_music import fail, report, stage  # noqa: E402  (after the path above; the script runs with -I)


class LocalPretrained:
    """Stands in for a transformers class in audiocraft's conditioners: from_pretrained of the T5 name audiocraft asks for loads the
    downloaded folder instead, offline (local_files_only); any other name is a failure, never a download."""

    def __init__(self, cls, name, folder):
        self.cls, self.name, self.folder = cls, name, folder

    def from_pretrained(self, name, *args, **kwargs):
        if name != self.name:
            fail("MISSING", "audiocraft asked for the text encoder " + str(name) + ", the app has " + self.name)
        kwargs["local_files_only"] = True
        return self.cls.from_pretrained(self.folder, *args, **kwargs)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--prompt-file", required=True)
    parser.add_argument("--seconds", type=float, required=True)
    parser.add_argument("--seed", type=int, required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--code", required=True)
    parser.add_argument("--t5-name", required=True)
    parser.add_argument("--t5", required=True)
    args = parser.parse_args()

    with open(args.prompt_file, encoding="utf-8") as handle:
        prompt = handle.read().strip()
    if not prompt:
        fail("PROMPT", "the prompt is empty")
    for need in (os.path.join(args.model, "state_dict.bin"), os.path.join(args.model, "compression_state_dict.bin"),
                 os.path.join(args.t5, "model.safetensors"), os.path.join(args.code, "audiocraft", "models", "magnet.py")):
        if not os.path.isfile(need):
            fail("MISSING", "a file of the model is missing: " + need)

    # audiocraft from its pinned source, ahead of anything else on the path (python -I left the script's folder out).
    sys.path.insert(0, args.code)
    stage("loading PyTorch and audiocraft")
    import soundfile as sf
    import torch
    import transformers

    if not torch.cuda.is_available():
        fail("CUDA", "PyTorch sees no NVIDIA graphics card (CUDA); MAGNeT runs only there (xformers has no processor version)")
    import audiocraft.modules.conditioners as conditioners
    from audiocraft.data.audio_utils import normalize_audio
    from audiocraft.models import MAGNeT

    conditioners.T5Tokenizer = LocalPretrained(transformers.T5Tokenizer, args.t5_name, args.t5)
    conditioners.T5EncoderModel = LocalPretrained(transformers.T5EncoderModel, args.t5_name, args.t5)
    torch.manual_seed(args.seed)

    stage("loading the model")
    model = MAGNeT.get_pretrained(args.model, device="cuda")
    # audiocraft's own defaults for MAGNeT (models/magnet.py), written out so a newer pin cannot change them unseen.
    model.set_generation_params(use_sampling=True, top_k=0, top_p=0.9, temperature=3.0, max_cfg_coef=10.0, min_cfg_coef=1.0,
                                decoding_steps=[20, 10, 10, 10], span_arrangement="nonoverlap")
    stage("making %d s" % round(model.duration))
    with torch.inference_mode():
        wav = model.generate([prompt])[0]

    stage("writing the piece")
    rate = model.sample_rate
    wav = wav[..., : max(1, int(args.seconds * rate))]
    wav = normalize_audio(wav.float().cpu(), strategy="loudness", sample_rate=rate)
    sf.write(args.out, wav.clamp(-1, 1).numpy().T, rate, subtype="PCM_16")
    stage("done")


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception as error:  # noqa: BLE001 - every failure is reported in one line
        report(error)
