"""F5-TTS (pip f5-tts) with the Turkish fine-tune marduk-ra/F5-TTS-Turkish (CC BY-NC 4.0) for the TM Concept speech worker.

The model folder holds the checkpoint, its vocab.txt and the Vocos vocoder (vocos/config.yaml, vocos/pytorch_model.bin): nothing is
downloaded. F5 copies the voice, accent and pace of a reference clip and needs the clip's exact words, so the voice is the clip that comes
with TM Concept (f5_ref_tr.wav next to this file, 6.7 s of Turkish, its words below); a clip without its words would make F5 download a
speech recogniser, so the user's own samples are not offered for this voice.
"""

import os

from tmc_common import load_on, wav_seconds

HERE = os.path.dirname(os.path.abspath(__file__))
REFERENCE = os.path.join(HERE, "f5_ref_tr.wav")
REFERENCE_TEXT = "Henüz yirmi bir yaşındaki İkinci Mehmed, dev Şahi toplarıyla surları haftalarca dövdü."


class F5Adapter:
    def __init__(self, model, device, options):
        from f5_tts.api import F5TTS

        checkpoint = os.path.join(model, options.get("checkpoint", "f5_tts_turkish_1000000.safetensors"))
        vocab = os.path.join(model, "vocab.txt")
        vocoder = os.path.join(model, "vocos")
        self.tts, self.device, self.note = load_on(
            lambda dev: F5TTS(model="F5TTS_Base", ckpt_file=checkpoint, vocab_file=vocab, vocoder_local_path=vocoder, device=dev),
            device,
            "F5-TTS",
        )
        self.sample_rate = 24000
        self.steps = int(options.get("steps", 32))

    def synth(self, text, language, voice, reference, speed, out):
        self.tts.infer(
            ref_file=REFERENCE,
            ref_text=REFERENCE_TEXT,
            gen_text=text,
            file_wave=out,
            speed=speed,
            nfe_step=self.steps,
            show_info=lambda *args, **kwargs: None,
            seed=0,
        )
        return wav_seconds(out)


def create(model, device, options):
    return F5Adapter(model, device, options)
