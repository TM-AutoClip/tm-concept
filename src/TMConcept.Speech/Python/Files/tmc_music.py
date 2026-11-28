"""Helpers the TM Concept local music scripts share (28 Nov 2026; MIT, part of TM Concept).

Stages are printed as "TMC: ..." lines, a failure as one "TMC-E-<CODE>: ..." line before exit code 2 (the app shows that line and logs the
rest; LocalMusicRunRules names the marks). The scripts run with -I, so each puts its own folder on sys.path before it imports this.
"""

import re
import sys
import traceback

SECTION = re.compile(r"^\s*\[([A-Za-z][A-Za-z0-9 _-]*)\]\s*(.*)$")


def stage(text):
    print("TMC: " + text, flush=True)


def fail(code, text):
    print("TMC-E-" + code + ": " + text, flush=True)
    sys.exit(2)


def report(error):
    """Reports an unexpected failure in one line (the traceback goes to the log's tail)."""
    traceback.print_exc()
    name = type(error).__name__
    lines = str(error).strip().splitlines()
    first = lines[0][:300] if lines else ""
    if name == "OutOfMemoryError" or "out of memory" in str(error).lower():
        fail("MEMORY", "the graphics card ran out of memory" + (" (" + first + ")" if first else ""))
    fail("RUN", name + (": " + first if first else ""))


def blocks(text):
    """The user's words as (tag, lines) blocks and whether the user wrote tags.

    With "[tag]" lines: each tag as written (trimmed; text after it on its line is its first line) with the lines under it, lines before the
    first tag under "verse". Without: each paragraph, alternately "verse" and "chorus". Lines are trimmed, empty ones left out.
    """
    rows = text.splitlines()
    if not any(SECTION.match(row) for row in rows):
        paragraphs = [p for p in re.split(r"\n\s*\n", text.strip()) if p.strip()]
        return [("verse" if index % 2 == 0 else "chorus", [l.strip() for l in p.splitlines() if l.strip()]) for index, p in enumerate(paragraphs)], False
    found, tag, lines = [], None, []
    for row in rows:
        match = SECTION.match(row)
        if match:
            if tag is not None:
                found.append((tag, lines))
            tag, lines = match.group(1).strip(), [match.group(2).strip()] if match.group(2).strip() else []
        elif row.strip():
            if tag is None:
                tag = "verse"
            lines.append(row.strip())
    if tag is not None:
        found.append((tag, lines))
    return found, True
