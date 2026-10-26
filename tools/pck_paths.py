"""Check the res:// prefix convention across Godot 4 PCKs. Read-only."""
import struct
import sys
import os


def entries(path, want_prefixes=("/localization/", "cards.json", "powers.json")):
    with open(path, "rb") as f:
        if f.read(4) != b"GDPC":
            print(path, "-> not GDPC")
            return
        f.read(20)
        file_base, dir_off = struct.unpack("<QQ", f.read(16))
        f.seek(dir_off)
        n = struct.unpack("<I", f.read(4))[0]
        names = []
        for _ in range(n):
            pl = struct.unpack("<I", f.read(4))[0]
            if pl > 4096:
                break
            p = f.read(pl).decode("utf-8", "replace").rstrip("\0")
            f.read(16 + 16 + 4)
            names.append(p)
        prefixed = sum(1 for p in names if p.startswith("res://"))
        print(f"{os.path.basename(path):<34} n={n:<6} with_res://={prefixed:<6} without={n - prefixed}")
        for p in names[:3]:
            print("      sample:", p[:80])
        hits = [p for p in names if p.endswith("cards.json") or p.endswith("powers.json")]
        for h in hits[:6]:
            print("      loc   :", h[:100])


for arg in sys.argv[1:]:
    entries(arg)
