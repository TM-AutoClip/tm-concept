# -*- coding: utf-8 -*-
"""Small, generic secret / personal-data scanner for TM Concept contributors.

Why: TM Concept is a public repository. Access tokens, private keys and paths that reveal a
user name must never be committed. The pre-commit hook (.githooks/pre-commit) runs this script on
the staged content; the pre-push hook runs it on commit messages.

Usage:
    python -I tools/scan-secrets.py --staged [--root <repo>]   scan staged files (pre-commit)
    python -I tools/scan-secrets.py --stdin                    scan text from standard input
    python -I tools/scan-secrets.py [--root <repo>]            scan every tracked file

Exit code: 0 = clean, 1 = findings, 2 = the scan could not run.
Matched values are never printed: only file:line and the finding group.
"""
import argparse
import os
import re
import subprocess
import sys

_B = r"(?<![A-Za-z0-9_-])"
_A = r"(?![A-Za-z0-9_-])"
PATTERNS = {
    "TOKEN_OPENAI_ANTHROPIC": re.compile(_B + r"sk-(?:ant-|proj-)?[A-Za-z0-9_-]{20,}"),
    "TOKEN_GITHUB": re.compile(_B + r"(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{22,})"),
    "TOKEN_GOOGLE": re.compile(_B + r"AIza[0-9A-Za-z_-]{35}"),
    "TOKEN_GOOGLE_OAUTH": re.compile(_B + r"(?:ya29\.[0-9A-Za-z_-]{20,}|1//0[0-9A-Za-z_-]{30,}|GOCSPX-[0-9A-Za-z_-]{20,})"),
    "TOKEN_SLACK": re.compile(_B + r"xox[abposr]-[A-Za-z0-9-]{10,}"),
    "TOKEN_META": re.compile(_B + r"EAA[A-Za-z0-9]{30,}"),
    "TOKEN_AWS": re.compile(_B + r"AKIA[0-9A-Z]{16}" + _A),
    "TOKEN_TELEGRAM_BOT": re.compile(_B + r"\d{8,10}:AA[0-9A-Za-z_-]{30,}"),
    "WEBHOOK_DISCORD": re.compile(r"discord(?:app)?\.com/api/webhooks/\d{10,}/[0-9A-Za-z_-]{30,}", re.IGNORECASE),
    "TOKEN_JWT": re.compile(_B + r"eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}"),
    "PRIVATE_KEY": re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH |DSA |PGP )?PRIVATE KEY-----"),
}
# Absolute home folder paths reveal a user name (one-letter example names are allowed).
HOME_PATH = re.compile(
    r"(?:[A-Za-z]:|/[a-z])(?:\\\\|\\|/)+Users(?:\\\\|\\|/)+(?!Public\b|Default\b)([A-Za-z0-9._-]+)"
    r"|(?<![\w.])/home/(?!runner\b|user\b|pi\b)([A-Za-z0-9._-]+)", re.IGNORECASE)
EMAIL = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}")
EMAIL_SAFE = re.compile(r"@(?:[\w-]+\.)*(?:example|test|localhost|invalid)(?:\.[a-z]{2,})*$"
                        r"|^noreply@anthropic\.com$|@users\.noreply\.github\.com$", re.IGNORECASE)
EMAIL_PERSONAL = re.compile(r"@(?:gmail|googlemail|outlook|hotmail|live|msn|yahoo|ymail|icloud|me|yandex|proton|"
                            r"protonmail|pm|gmx|aol|mail)\.[a-z.]{2,}$", re.IGNORECASE)
SKIP_EXT = {".png", ".jpg", ".jpeg", ".gif", ".ico", ".webp", ".ttf", ".otf", ".woff", ".woff2", ".zip", ".gz",
            ".exe", ".dll", ".pdb", ".db", ".nupkg", ".mp4"}


def scan_line(line):
    found = [name for name, pattern in PATTERNS.items() if pattern.search(line)]
    for m in HOME_PATH.finditer(line):
        name = m.group(1) or m.group(2) or ""
        if len(name) > 1:
            found.append("HOME_PATH")
    for m in EMAIL.finditer(line):
        address = m.group(0)
        if not EMAIL_SAFE.search(address) and EMAIL_PERSONAL.search(address):
            found.append("PERSONAL_EMAIL")
    return sorted(set(found))


def git(root, *args):
    return subprocess.run(["git", "-C", root, *args], capture_output=True, check=True).stdout


def staged(root):
    for raw in git(root, "diff", "--cached", "--name-only", "--diff-filter=ACMR", "-z").split(b"\0"):
        if raw:
            path = raw.decode("utf-8", errors="replace")
            yield path, lambda p=path: git(root, "show", ":" + p)


def tracked(root):
    for raw in git(root, "ls-files", "-z").split(b"\0"):
        if raw:
            path = raw.decode("utf-8", errors="replace")
            full = os.path.join(root, path)
            if os.path.isfile(full):
                yield path, lambda f=full: open(f, "rb").read()


def scan_text(label, data, out):
    if b"\0" in data[:8192]:
        return
    for number, line in enumerate(data.decode("utf-8", errors="replace").splitlines(), 1):
        for group in scan_line(line):
            out.append(f"{label}:{number}: [{group}]")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=os.getcwd())
    parser.add_argument("--staged", action="store_true")
    parser.add_argument("--stdin", action="store_true")
    args = parser.parse_args()
    findings = []
    try:
        if args.stdin:
            scan_text("<stdin>", sys.stdin.buffer.read(), findings)
        else:
            root = os.path.abspath(args.root)
            for path, read in (staged(root) if args.staged else tracked(root)):
                if os.path.splitext(path)[1].lower() in SKIP_EXT:
                    continue
                scan_text(path, read(), findings)
    except (OSError, subprocess.CalledProcessError) as error:
        print(f"scan-secrets: could not run: {error}", file=sys.stderr)
        return 2
    if findings:
        sys.stdout.write("\n".join(findings) + "\n")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
