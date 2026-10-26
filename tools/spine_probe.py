"""Inspect Spine assets inside skin packs to work out how to read animation names.

Godot's Spine importer emits, for each source asset:
  <name>.atlas  -> <hash>.spatlas   (JSON)
  <name>.skel   -> <hash>.spskel    (Spine binary)
  <name>.png    -> <hash>.ctex      (compressed texture)

Read-only.
"""
import json
import struct
import sys


def read_pack(path):
    blob = open(path, "rb").read()
    fb, do = struct.unpack_from("<QQ", blob, 24)
    pos = do
    n = struct.unpack_from("<I", blob, pos)[0]
    pos += 4
    out = {}
    for _ in range(n):
        pl = struct.unpack_from("<I", blob, pos)[0]
        pos += 4
        name = blob[pos:pos + pl].decode("utf-8", "replace").rstrip("\0")
        pos += pl
        off, size = struct.unpack_from("<QQ", blob, pos)
        pos += 32 + 4
        out[name] = blob[fb + off:fb + off + size]
    return out


pack = read_pack(sys.argv[1])
only = sys.argv[2] if len(sys.argv) > 2 else ""

for name, data in sorted(pack.items()):
    if only and only not in name:
        continue
    if name.endswith(".spatlas"):
        print("=" * 70)
        print("SPATLAS:", name)
        try:
            j = json.loads(data.decode("utf-8"))
            print(json.dumps(j, indent=2)[:1400])
        except Exception as e:
            print("  not JSON:", e, data[:200])
    elif name.endswith(".spskel"):
        print("=" * 70)
        print("SPSKEL:", name, len(data), "bytes")
        print("  first 96 bytes:", data[:96])
        # Spine binary: read version string if present (4.x starts with a hash then version)
        for probe_len in (10, 12):
            s = data[:probe_len]
            if s[:1].isascii() and s[:1].isalpha():
                print(f"  ascii probe ({probe_len}):", s)
        # Length-prefixed string scan: b"<len byte><chars>" for plausible names.
        found = []
        i = 0
        while i < min(len(data), 200000):
            ln = data[i]
            if 3 <= ln <= 40 and i + 1 + ln <= len(data):
                cand = data[i + 1:i + 1 + ln]
                if all(32 <= c < 127 for c in cand):
                    txt = cand.decode("ascii")
                    if txt.replace("_", "").replace("-", "").isalnum() and any(ch.isalpha() for ch in txt):
                        found.append((i, txt))
                        i += 1 + ln
                        continue
            i += 1
        print(f"  length-prefixed ascii candidates: {len(found)}")
        for off, txt in found[:60]:
            print(f"    @{off:<8} {txt}")
