"""MMS-TTS Turkish (Meta, facebook/mms-tts-tur, CC BY-NC 4.0; transformers VITS) for the TM Concept speech worker.

Small (145 MB): it always runs on the processor. Its alphabet is lower-case Turkish, so the text is lower-cased the Turkish way
(I -> ı, İ -> i) and spoken clause by clause with short pauses (a long input makes VITS rush).
"""

import re

from tmc_common import mono, silence, tr_lower, write_wav

_CLAUSE = re.compile(r"([^.!?;:,]+[.!?;:,]*)")
PAUSES = {",": 0.15, ";": 0.2, ":": 0.2, ".": 0.35, "!": 0.35, "?": 0.35}


class MmsAdapter:
    def __init__(self, model, device, options):
        import torch
        from transformers import AutoTokenizer, VitsModel

        self.torch = torch
        self.model = VitsModel.from_pretrained(model, local_files_only=True).eval()
        self.tokenizer = AutoTokenizer.from_pretrained(model, local_files_only=True)
        self.sample_rate = int(self.model.config.sampling_rate)
        self.device = "cpu"
        self.note = ""

    def synth(self, text, language, voice, reference, speed, out):
        self.model.speaking_rate = float(speed or 1.0)
        audio = []
        for clause in _CLAUSE.findall(tr_lower(text)):
            words = clause.strip()
            if not any(c.isalpha() for c in words):
                continue
            inputs = self.tokenizer(words, return_tensors="pt")
            with self.torch.no_grad():
                audio += mono(self.model(**inputs).waveform)
            audio += silence(PAUSES.get(words[-1], 0.1), self.sample_rate)
        return write_wav(out, audio, self.sample_rate)


def create(model, device, options):
    return MmsAdapter(model, device, options)
