"""XTTS-v2 (Coqui, pip coqui-tts) for the TM Concept speech worker. Weights: coqui/XTTS-v2, Coqui Public Model License (non-commercial).

The model folder holds config.json, model.pth, vocab.json and speakers_xtts.pth. A voice is one of the built-in speakers
(speakers_xtts.pth) or, with a reference clip (the user's own voice sample), a copy of that voice. Long text is spoken sentence by
sentence (XTTS reads at most about 250 characters well).
"""

import os

from tmc_common import load_on, mono, sentences, silence, write_wav

LIMIT = 220
LANGUAGES = {"zh": "zh-cn"}
PAUSE = 0.25


class XttsAdapter:
    def __init__(self, model, device, options):
        from TTS.tts.configs.xtts_config import XttsConfig
        from TTS.tts.models.xtts import Xtts

        config = XttsConfig()
        config.load_json(os.path.join(model, "config.json"))

        def build(dev):
            xtts = Xtts.init_from_config(config)
            xtts.load_checkpoint(config, checkpoint_dir=model, eval=True, use_deepspeed=False)
            return xtts.to(dev)

        self.model, self.device, self.note = load_on(build, device, "XTTS-v2")
        self.sample_rate = int(config.audio.output_sample_rate or 24000)
        self.default_speaker = options.get("speaker") or "Ana Florence"
        self._latents = {}

    def _voice(self, voice, reference):
        if reference:
            key = "file:" + reference
            if key not in self._latents:
                self._latents[key] = self.model.get_conditioning_latents(audio_path=[reference])
            return self._latents[key]
        speakers = self.model.speaker_manager.speakers if self.model.speaker_manager else {}
        name = voice if voice in speakers else self.default_speaker
        return tuple(speakers[name].values())

    def synth(self, text, language, voice, reference, speed, out):
        latent, embedding = self._voice(voice, reference)
        audio = []
        for piece in sentences(text, LIMIT):
            result = self.model.inference(piece, LANGUAGES.get(language, language) or "tr", latent, embedding, speed=speed)
            if audio:
                audio += silence(PAUSE, self.sample_rate)
            audio += mono(result["wav"])
        return write_wav(out, audio, self.sample_rate)


def create(model, device, options):
    return XttsAdapter(model, device, options)
