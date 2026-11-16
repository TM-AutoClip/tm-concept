"""TM Concept speech worker for the Python voices (MIT, part of TM Concept).

One long-lived process per voice, started by the app (PythonWorkerClient) with the voice's own Python environment, one request at a time.
Every request is one JSON object on one line of standard input; every answer is one JSON object on one line of standard output:

  {"cmd": "load", "adapter": "xtts" (the module adapter_xtts.py), "model": "<folder>", "device": "cuda", "need_mb": 3072, "threads": 4, "options": {...}}
      -> {"ok": true, "device": "cuda", "note": "", "sample_rate": 24000}
  {"cmd": "synth", "text": "...", "language": "tr", "voice": "", "reference": "", "speed": 1.0, "out": "<file>.wav"}
      -> {"ok": true, "duration": 3.21, "sample_rate": 24000}
  {"cmd": "quit"} -> {"ok": true}
  a failure -> {"ok": false, "code": "model" | "engine" | "request", "error": "..."}

The model files are read only from the given folder (the app downloaded them, pinned by commit and SHA-256): HF_HUB_OFFLINE is set, so
nothing is downloaded here. Whatever the libraries print goes to standard error; the real standard output carries only answers.
"""

import importlib
import json
import os
import re
import sys
import traceback

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

_ANSWERS = sys.stdout
sys.stdout = sys.stderr

from tmc_common import resolve_device, warn  # noqa: E402  (after the path above)

_ADAPTER = re.compile(r"^[a-z0-9_]+$")


def send(answer):
    _ANSWERS.write(json.dumps(answer, ensure_ascii=False) + "\n")
    _ANSWERS.flush()


def _load(request, state):
    name = (request.get("adapter") or "").strip().lower()
    if not _ADAPTER.match(name):
        raise ValueError("invalid adapter name: %r" % name)
    model = request.get("model") or ""
    if not os.path.isdir(model):
        raise FileNotFoundError("the model folder is missing: %s" % model)
    threads = int(request.get("threads") or 0)
    device, note = resolve_device(request.get("device"), int(request.get("need_mb") or 0))
    if note:
        warn("the processor is used: " + note)
    try:
        import torch

        if threads > 0:
            torch.set_num_threads(threads)
    except ImportError:
        pass
    module = importlib.import_module("adapter_" + name)
    adapter = module.create(model, device, request.get("options") or {})
    state["adapter"] = adapter
    used = str(getattr(adapter, "device", device))
    note = getattr(adapter, "note", "") or note
    return {"ok": True, "device": used, "note": note, "sample_rate": int(getattr(adapter, "sample_rate", 0) or 0)}


def _synth(request, state):
    adapter = state.get("adapter")
    if adapter is None:
        return {"ok": False, "code": "request", "error": "no model is loaded"}
    out = request.get("out") or ""
    text = (request.get("text") or "").strip()
    if not out or not text:
        return {"ok": False, "code": "request", "error": "text and out are needed"}
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    seconds = adapter.synth(
        text=text,
        language=request.get("language") or "",
        voice=request.get("voice") or "",
        reference=request.get("reference") or "",
        speed=float(request.get("speed") or 1.0),
        out=out,
    )
    return {"ok": True, "duration": float(seconds), "sample_rate": int(getattr(adapter, "sample_rate", 0) or 0)}


def main():
    state = {}
    for raw in sys.stdin:
        line = raw.strip()
        if not line:
            continue
        command = ""
        try:
            request = json.loads(line)
            command = request.get("cmd") or ""
            if command == "load":
                send(_load(request, state))
            elif command == "synth":
                send(_synth(request, state))
            elif command == "quit":
                send({"ok": True})
                break
            else:
                send({"ok": False, "code": "request", "error": "unknown command: %r" % command})
        except Exception as exc:  # noqa: BLE001 - every failure becomes an answer line
            traceback.print_exc(file=sys.stderr)
            code = "model" if command == "load" else "engine"
            send({"ok": False, "code": code, "error": "%s: %s" % (type(exc).__name__, exc)})


if __name__ == "__main__":
    try:
        sys.stdin.reconfigure(encoding="utf-8")
        _ANSWERS.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except AttributeError:
        pass
    main()
