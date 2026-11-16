"""Helpers the TM Concept speech worker and its adapters share (MIT, part of TM Concept)."""

import math
import re
import struct
import sys
import wave

_SENTENCE = re.compile(r"(?<=[.!?…;:])\s+")


def warn(message):
    print("[tmc:warn] " + message, file=sys.stderr, flush=True)


def resolve_device(requested, need_mb):
    """(device, note): cuda only when torch sees the card and it has need_mb free; otherwise the processor, with the reason."""
    wanted = (requested or "cpu").lower()
    if not wanted.startswith("cuda"):
        return "cpu", ""
    try:
        import torch
    except ImportError:
        return "cpu", "torch is not installed"
    if not torch.cuda.is_available():
        if getattr(torch.version, "cuda", None) is None:
            return "cpu", "the processor build of torch is installed"
        return "cpu", "torch cannot see the graphics card (driver?)"
    if need_mb:
        try:
            free, _ = torch.cuda.mem_get_info()
            if free < need_mb * 1024 * 1024:
                return "cpu", "only %d MB of graphics memory is free, %d MB are needed" % (free // (1024 * 1024), need_mb)
        except Exception as exc:  # noqa: BLE001 - a failed reading never blocks the card
            warn("free graphics memory could not be read: %s" % exc)
    return wanted, ""


def load_on(factory, device, what):
    """factory(device); when that fails on the card (out of memory, driver), once more on the processor. (result, device, note)."""
    if not device.startswith("cuda"):
        return factory("cpu"), "cpu", ""
    try:
        return factory(device), device, ""
    except Exception as exc:  # noqa: BLE001 - any failure on the card falls back to the processor, loudly
        try:
            import torch

            torch.cuda.empty_cache()
        except Exception:  # noqa: BLE001
            pass
        note = "%s could not be loaded on the graphics card (%s: %s)" % (what, type(exc).__name__, exc)
        warn(note)
        return factory("cpu"), "cpu", note


def mono(audio):
    """A flat list of floats from a tensor, an array or a list ([T], [1, T] or [C, T]: the first channel)."""
    if hasattr(audio, "detach"):
        audio = audio.detach().cpu().float().numpy()
    if hasattr(audio, "ndim"):
        if audio.ndim > 1:
            audio = audio.reshape(-1, audio.shape[-1])[0]
        return [float(x) for x in audio.tolist()]
    return [float(x) for x in audio]


def silence(seconds, sample_rate):
    return [0.0] * int(seconds * sample_rate)


def write_wav(out, samples, sample_rate):
    """Mono 16-bit PCM WAV; the length in seconds."""
    samples = mono(samples)
    frames = bytearray()
    for value in samples:
        if value != value or math.isinf(value):
            value = 0.0
        frames += struct.pack("<h", int(round(max(-1.0, min(1.0, value)) * 32767)))
    with wave.open(out, "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(int(sample_rate))
        handle.writeframes(bytes(frames))
    return len(samples) / float(sample_rate)


def wav_seconds(path):
    with wave.open(path, "rb") as handle:
        return handle.getnframes() / float(handle.getframerate())


def sentences(text, limit):
    """The text in pieces of at most about limit characters, cut after sentence ends (a long sentence is cut at a comma or a space)."""
    pieces = []
    for sentence in _SENTENCE.split(text.strip()):
        sentence = sentence.strip()
        while len(sentence) > limit:
            cut = max(sentence.rfind(", ", 0, limit), sentence.rfind(" ", 0, limit))
            cut = cut if cut > limit // 3 else limit
            pieces.append(sentence[:cut].strip(" ,"))
            sentence = sentence[cut:].strip(" ,")
        if sentence:
            pieces.append(sentence)
    return pieces


def tr_lower(text):
    """Turkish lower case (I -> ı, İ -> i)."""
    return text.replace("I", "ı").replace("İ", "i").lower()
