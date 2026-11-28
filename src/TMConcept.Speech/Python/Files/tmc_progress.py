"""Progress lines of the TM Concept local music scripts (0.1.61; MIT, part of TM Concept).

A script prints its progress as "TMC-P: <stage>/<stages> <done>/<total> <unit>" lines, e.g. "TMC-P: 2/3 16/40 blocks" (the app's
MusicProgressLineParser reads them; LocalMusicRunRules names the mark). Units: tokens, blocks (YuE's 6-second blocks), steps, decoding; a stage
whose length is not known prints 0/0 (the screen shows the stage without a percentage). A line is printed when the stage starts, when the
stage's whole per cent changes or a second has passed since the last line (at most one line per per cent and one a second while the count
moves), and when the stage ends.

Nothing here edits a model's own code. The counters hook from the outside into what the libraries already offer: a callback a pipeline takes,
a logits processor list (one call per generated token), tqdm's update (the bars stay off screen: they write to os.devnull; TQDM_DISABLE=1 keeps
every other bar off, as before 0.1.61), a model's forward. A hook that cannot be installed (a library of another version) leaves the stage
without a count; the piece is still made.
"""

import os
import sys
import time

MARK = "TMC-P: "


class Reporter:
    """The progress of one run: its stage count, the running stage, its unit and length, and when the last line was printed."""

    def __init__(self, stages, out=None, clock=time.monotonic):
        self.stages = stages
        self.out = out
        self.clock = clock
        self.stage = 0
        self.unit = ""
        self.total = 0
        self.done = 0
        self._percent = -1
        self._printed = None

    def begin(self, stage, unit, total=0):
        """A stage starts (its first line, 0 done)."""
        self.stage, self.unit, self.total, self.done = stage, unit, max(0, int(total)), 0
        self._print()

    def length(self, total):
        """The running stage's length became known (or changed, e.g. a tqdm bar of a library that only now knows it)."""
        total = max(0, int(total))
        if total != self.total:
            self.total = total
            self._print()

    def update(self, done):
        """The running stage has <done> of its length."""
        done = max(0, int(done))
        if self.total > 0:
            done = min(done, self.total)
        if done == self.done:
            return
        self.done = done
        percent = done * 100 // self.total if self.total > 0 else 0
        now = self.clock()
        if done < self.total and percent == self._percent and self._printed is not None and now - self._printed < 1.0:
            return
        self._print()

    def finish(self):
        """The running stage is done (its last line shows the whole length)."""
        if self.total > 0 and self.done < self.total:
            self.done = self.total
            self._print()

    def _print(self):
        self._percent = self.done * 100 // self.total if self.total > 0 else 0
        self._printed = self.clock()
        out = self.out or sys.stdout
        out.write("%s%d/%d %d/%d %s\n" % (MARK, self.stage, self.stages, self.done, self.total, self.unit))
        out.flush()


def _hook(install):
    """Installs one hook; a library without the hooked part leaves the count out (the piece is still made)."""
    try:
        install()
        return True
    except Exception as error:  # noqa: BLE001 - a missing hook never fails the run
        print("TMC: warning: no progress count (%s: %s)" % (type(error).__name__, str(error)[:200]), flush=True)
        return False


def count_generation_steps(on_step):
    """Calls on_step() once per generated token of every transformers generate() in this process (its logits processor list runs once a
    step, in greedy and in sampling decoding, with or without classifier-free guidance)."""

    def install():
        from transformers.generation.logits_process import LogitsProcessorList

        real = LogitsProcessorList.__call__

        def call(self, input_ids, scores, **kwargs):
            on_step()
            return real(self, input_ids, scores, **kwargs)

        LogitsProcessorList.__call__ = call

    return _hook(install)


def follow_generate_calls(on_call):
    """Calls on_call(input_ids) before every transformers generate() in this process (YuE's stage 1 starts a segment with one call, its
    stage 2 makes one 6-second block frame per call)."""

    def install():
        from transformers.generation.utils import GenerationMixin

        real = GenerationMixin.generate

        def generate(self, *args, **kwargs):
            on_call(kwargs.get("input_ids", args[0] if args else None))
            return real(self, *args, **kwargs)

        GenerationMixin.generate = generate

    return _hook(install)


def follow_exllamav2(on_forward):
    """Calls on_forward(input_ids, last_id_only) before every exllamav2 forward in this process (YuE-exllamav2 forwards a prompt with
    last_id_only=True and each sampled token without it)."""

    def install():
        from exllamav2 import ExLlamaV2

        real = ExLlamaV2.forward

        def forward(self, input_ids, *args, **kwargs):
            on_forward(input_ids, bool(kwargs.get("last_id_only", False)))
            return real(self, input_ids, *args, **kwargs)

        ExLlamaV2.forward = forward

    return _hook(install)


def follow_tqdm(on_update):
    """Every tqdm bar made after this reports to on_update(bar, depth) when it is made, when it moves and when it closes (depth 0 = a bar made
    while no other is open). The bars are on (tqdm skips counting while disabled) but write to os.devnull: the app never sees their carriage
    returns."""

    def install():
        import tqdm as package
        import tqdm.auto
        import tqdm.std

        sink = open(os.devnull, "w", encoding="utf-8")
        opened = []

        class Followed(tqdm.std.tqdm):
            def __init__(self, *args, **kwargs):
                kwargs["disable"] = False
                kwargs["file"] = sink
                self._tmc_depth = len(opened)
                self._tmc_open = True
                super().__init__(*args, **kwargs)
                opened.append(self)
                on_update(self, self._tmc_depth)

            def update(self, n=1):
                moved = super().update(n)
                on_update(self, self._tmc_depth)
                return moved

            def close(self):
                if getattr(self, "_tmc_open", False):
                    self._tmc_open = False
                    opened[:] = [bar for bar in opened if bar is not self]
                    on_update(self, self._tmc_depth)
                super().close()

        package.tqdm = Followed
        tqdm.std.tqdm = Followed
        tqdm.auto.tqdm = Followed

    return _hook(install)
