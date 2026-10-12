#!/usr/bin/env python3
"""Apply macOS file fixes to the BlueArchive-NewWorld RimWorld mod (Steam Workshop 3560667190).

Safe to run repeatedly. Steam restores the original files whenever the mod updates,
so run this again (with RimWorld closed) after each update.
Each original is backed up before it is first patched.
"""
import os
import re
import shutil
import struct
import sys

MOD = os.path.expanduser(
    "~/Library/Application Support/Steam/steamapps/workshop/content/294100/3560667190")
BACKUP = os.path.expanduser("~/Library/Application Support/RimWorld/BANW-MacFix/backup")


def backup(path):
    """Only called right before patching, so the file is always the pristine original."""
    dst = os.path.join(BACKUP, os.path.relpath(path, MOD))
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copy2(path, dst)


def write_atomic(path, data):
    tmp = path + ".tmp-macfix"
    with open(tmp, "wb") as f:
        f.write(data)
    shutil.copymode(path, tmp)
    os.replace(tmp, path)


def fix_pawn_bundle():
    """RimWorld skips *_win asset bundles on macOS; give the character textures a _mac twin."""
    d = os.path.join(MOD, "1.6", "AssetBundles")
    src, dst = os.path.join(d, "banw_pawns_win"), os.path.join(d, "banw_pawns_mac")
    if not os.path.exists(src):
        return "pawn bundle: banw_pawns_win not found (skipped)"
    if os.path.exists(dst) and open(src, "rb").read() == open(dst, "rb").read():
        return "pawn bundle: already in place"
    shutil.copy2(src, dst)
    return "pawn bundle: copied banw_pawns_win -> banw_pawns_mac"


def iter_us_strings(b):
    """Yields (offset, utf16 bytes) for every string literal in a .NET assembly's #US heap."""
    i = b.find(b"BSJB")
    p = i + 16 + struct.unpack_from("<I", b, i + 12)[0]
    _, n = struct.unpack_from("<HH", b, p)
    p += 4
    heap = None
    for _ in range(n):
        off, size = struct.unpack_from("<II", b, p)
        e = b.index(b"\0", p + 8)
        if b[p + 8:e] == b"#US":
            heap = (i + off, size)
        p = (e + 4) & ~3
    q, end = heap[0] + 1, heap[0] + heap[1]
    while q < end:
        c = b[q]
        if c & 0x80 == 0:
            ln, q = c, q + 1
        elif c & 0xC0 == 0x80:
            ln, q = ((c & 0x3F) << 8) | b[q + 1], q + 2
        else:
            ln, q = ((c & 0x1F) << 24) | (b[q + 1] << 16) | (b[q + 2] << 8) | b[q + 3], q + 4
        if ln:
            yield q, b[q:q + ln - 1]
        q += ln


def fix_dll_paths():
    """Hard-coded Windows separators in BANWlLib.dll (gacha videos, avatars) -> '/'. Same length, so a byte swap."""
    path = os.path.join(MOD, "1.6", "Assemblies", "BANWlLib.dll")
    b = bytearray(open(path, "rb").read())
    wanted = {"\\": "/", "\\Common\\Textures\\": "/Common/Textures/"}
    done = []
    for pos, raw in list(iter_us_strings(b)):
        s = raw.decode("utf-16le", "replace")
        if s in wanted:
            new = wanted[s].encode("utf-16le")
            b[pos:pos + len(new)] = new
            done.append(s)
    if not done:
        return "dll: already fixed"
    backup(path)
    write_atomic(path, bytes(b))
    return f"dll: patched {len(done)} path literal(s)"


FIELD_RE = re.compile(r"(<(DraggableAudio|aronaAudioClipPath)>)([^<]*\\[^<]*)(</\2>)")


def fix_xml_paths():
    """Voice clip paths in Defs use backslashes. Only these path fields are touched ("\\n" text is left alone)."""
    files = values = 0
    for root, _, names in os.walk(os.path.join(MOD, "1.6", "Defs")):
        for fn in names:
            if not fn.lower().endswith(".xml"):
                continue
            path = os.path.join(root, fn)
            raw = open(path, "rb").read()
            bom = raw.startswith(b"\xef\xbb\xbf")
            text = raw[3:].decode("utf-8") if bom else raw.decode("utf-8")
            new, n = FIELD_RE.subn(lambda m: m.group(1) + m.group(3).replace("\\", "/") + m.group(4), text)
            if n:
                backup(path)
                write_atomic(path, (b"\xef\xbb\xbf" if bom else b"") + new.encode("utf-8"))
                files += 1
                values += n
    return f"xml: fixed {values} path value(s) in {files} file(s)"


def fix_about_dependency_url():
    """The ChezhouLib dependency lacks a download link, which RimWorld warns about at every launch."""
    path = os.path.join(MOD, "About", "About.xml")
    text = open(path, encoding="utf-8").read()
    m = re.search(r"(<li>\s*<packageId>Chezhou\.ChezhouLib\.lib</packageId>\s*<displayName>[^<]*</displayName>)(\s*)(</li>)", text)
    if not m:
        return "about: already has a link"
    link = "<steamWorkshopUrl>https://steamcommunity.com/sharedfiles/filedetails/?id=3595247479</steamWorkshopUrl>"
    new = text[:m.start()] + m.group(1) + m.group(2).rstrip(" ") + "      " + link + m.group(2) + m.group(3) + text[m.end():]
    backup(path)
    write_atomic(path, new.encode("utf-8"))
    return "about: added ChezhouLib workshop link"


def main():
    if not os.path.isdir(MOD):
        sys.exit(f"Mod folder not found: {MOD}")
    for step in (fix_pawn_bundle, fix_dll_paths, fix_xml_paths, fix_about_dependency_url):
        print(step())


if __name__ == "__main__":
    main()
