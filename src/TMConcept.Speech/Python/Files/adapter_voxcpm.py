"""VoxCPM2 (OpenBMB, pip voxcpm) for the TM Concept speech worker: VoxCPM2 itself (Apache-2.0, copies a reference clip), Trendyol-TTS
(VoxCPM2 with a merged Turkish LoRA, MIT) and Ahenk (VoxCPM2 + a Turkish LoRA, CC BY 4.0; its voices are emotions).

options.kind: "voxcpm2", "trendyol" or "ahenk". Ahenk's folder holds the LoRA and its helpers (instruction_pick.py, pause_clean.py,
onset.py, lora_config.json); options.base is the VoxCPM2 folder it is put on. Turkish text is written out as it is spoken with trnorm
(the form these Turkish models were trained on); VoxCPM's own normaliser is off (it reads Chinese and English only). The denoiser is
never loaded (it would download a model) and torch.compile is off (slow first start, fragile on Windows).
"""

import json
import os
import sys

from tmc_common import load_on, write_wav

EMOTIONS = ("neutral", "calm", "cheerful", "empathetic", "sad", "angry", "surprised")

# VoxCPM2 without a reference clip makes up a voice from a description in brackets before the text ("voice design"); the same description
# gives a similar, not always identical, voice in every scene (the user's own sample keeps it the same).
DESIGNS = {
    "narrator-female": "A calm adult woman narrating a documentary; warm, clear and steady, recorded close to the microphone in a quiet studio.",
    "narrator-male": "A calm adult man narrating a documentary; deep, clear and steady, recorded close to the microphone in a quiet studio.",
}


def _spoken(text, language):
    if (language or "").lower() != "tr":
        return text
    try:
        from trnorm import normalize

        return normalize(text)
    except ImportError:
        return text


class VoxCpmAdapter:
    def __init__(self, model, device, options):
        from voxcpm import VoxCPM

        self.kind = options.get("kind", "voxcpm2")
        self.steps = int(options.get("steps", 16 if self.kind != "voxcpm2" else 10))
        self.cfg = float(options.get("cfg", 2.0))
        lora = {}
        folder = model
        if self.kind == "ahenk":
            from voxcpm.model.voxcpm import LoRAConfig

            if model not in sys.path:
                sys.path.insert(0, model)
            from instruction_pick import compose, pick
            from pause_clean import clean

            self._compose, self._pick, self._clean = compose, pick, clean
            with open(os.path.join(model, "lora_config.json"), encoding="utf-8") as handle:
                config = json.load(handle)["lora_config"]
            lora = {"lora_config": LoRAConfig(**config), "lora_weights_path": model}
            folder = options["base"]

        self.tts, self.device, self.note = load_on(
            lambda dev: VoxCPM.from_pretrained(folder, load_denoiser=False, optimize=False, device=dev, local_files_only=True, **lora),
            device,
            "VoxCPM2",
        )
        self.sample_rate = int(self.tts.tts_model.sample_rate)

    def synth(self, text, language, voice, reference, speed, out):
        spoken = _spoken(text, language)
        arguments = {"cfg_value": self.cfg, "inference_timesteps": self.steps, "normalize": False}
        if self.kind == "ahenk":
            emotion = voice if voice in EMOTIONS and voice != "neutral" else ""
            instruction, model_text, _ = self._pick(emotion, spoken, "auto")
            audio = self.tts.generate(text=self._compose(instruction, model_text), **arguments)
            audio = self._clean(audio, self.sample_rate)[0]
        else:
            if reference and self.kind == "voxcpm2":
                arguments["reference_wav_path"] = reference
            elif self.kind == "voxcpm2":
                spoken = "(" + DESIGNS.get(voice, DESIGNS["narrator-female"]) + ")" + spoken
            audio = self.tts.generate(text=spoken, **arguments)
        return write_wav(out, audio, self.sample_rate)


def create(model, device, options):
    return VoxCpmAdapter(model, device, options)
