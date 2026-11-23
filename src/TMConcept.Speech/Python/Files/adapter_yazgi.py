"""yazgı-tts-1 (erkamk/yazgi-tts-1, Apache-2.0) for the TM Concept speech worker.

A small Turkish model (81M parameters for speech) that runs on the processor. Its package (the pinned wheel in the model folder) is put
on the import path; the weights are the pinned .pt files, which yazgi_tts loads with torch.load(weights_only=True): plain tensors, no
pickled code. Eight built-in voices (male-01..04, female-01..04); no speed of its own (the app stretches the clip).
"""

import glob
import os
import sys

from tmc_common import mono, write_wav

RATE = 24000


class YazgiAdapter:
    def __init__(self, model, device, options):
        wheels = sorted(glob.glob(os.path.join(model, "yazgi_tts-*.whl")))
        if not wheels:
            raise RuntimeError("the yazgi_tts package is missing from the model folder")
        if wheels[-1] not in sys.path:
            sys.path.insert(0, wheels[-1])
        import torch
        from yazgi_tts import YazgiTTS

        self.torch = torch
        self.tts = YazgiTTS(model, device="cpu")
        self.sample_rate = RATE
        self.device = "cpu"
        self.note = ""

    def synth(self, text, language, voice, reference, speed, out):
        name = voice if voice in self.tts.voices else "male-01"
        with self.torch.no_grad():
            audio = self.tts.say(text, voice=name, seed=1)
        return write_wav(out, mono(audio), self.sample_rate)


def create(model, device, options):
    return YazgiAdapter(model, device, options)
