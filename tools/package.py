"""Packs a published folder for a release and prints SHA-256 values.

    python tools/package.py <publish folder> <rid> <version>

win-x64: copies the single exe to artifacts/release/TMConcept-<version>-win-x64.exe.
linux-*: writes artifacts/release/tm-concept-<version>-<rid>.tar.gz with a tm-concept/ folder: the program (executable), an icon,
a .desktop file, install-desktop-entry.sh (adds the menu entry for the current user), README, LICENSE, third-party notices, licenses/.
"""
import hashlib
import os
import shutil
import sys
import tarfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def main():
    folder, rid, version = sys.argv[1], sys.argv[2], sys.argv[3]
    out = os.path.join(ROOT, "artifacts", "release")
    os.makedirs(out, exist_ok=True)
    if rid.startswith("win"):
        target = os.path.join(out, f"TMConcept-{version}-{rid}.exe")
        shutil.copyfile(os.path.join(folder, "TMConcept.exe"), target)
    else:
        target = os.path.join(out, f"tm-concept-{version}-{rid}.tar.gz")
        with tarfile.open(target, "w:gz") as tar:
            def add(source, name, mode):
                info = tar.gettarinfo(source, arcname="tm-concept/" + name)
                info.mode, info.uid, info.gid, info.uname, info.gname = mode, 0, 0, "", ""
                with open(source, "rb") as f:
                    tar.addfile(info, f)

            add(os.path.join(folder, "TMConcept"), "tm-concept", 0o755)
            add(os.path.join(ROOT, "assets", "brand", "tm-concept-icon-256.png"), "tm-concept.png", 0o644)
            add(os.path.join(ROOT, "assets", "linux", "tm-concept.desktop"), "tm-concept.desktop", 0o644)
            add(os.path.join(ROOT, "assets", "linux", "install-desktop-entry.sh"), "install-desktop-entry.sh", 0o755)
            for name in ("README.md", "README.tr.md", "LICENSE", "THIRD-PARTY-NOTICES.md"):
                add(os.path.join(ROOT, name), name, 0o644)
            licenses = os.path.join(ROOT, "src", "TMConcept.Desktop", "Assets", "Licenses")
            for name in sorted(os.listdir(licenses)):
                add(os.path.join(licenses, name), "licenses/" + name, 0o644)
    print(f"{sha256(target)}  {os.path.basename(target)}  ({os.path.getsize(target) / (1024 * 1024):.1f} MiB)")


if __name__ == "__main__":
    main()
