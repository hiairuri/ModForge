"""Probe Godot PCK v3 header/directory layout on real mod packs. Read-only."""
import struct
import sys
import os


def probe(path, limit=8):
    print("=" * 72)
    print("FILE:", path)
    print("  size:", os.path.getsize(path))
    with open(path, "rb") as f:
        magic = f.read(4)
        if magic != b"GDPC":
            print("  not a GDPC pack")
            return
        version, vmaj, vmin, vrev, flags = struct.unpack("<IIIII", f.read(20))
        file_base, dir_off = struct.unpack("<QQ", f.read(16))
        tail = f.read(64)
        print(f"  version={version} godot={vmaj}.{vmin}.{vrev} flags={flags}")
        print(f"  file_base={file_base}  dir_offset={dir_off}")
        print("  bytes after 40-byte header:", "all-zero" if tail == b"\0" * 64 else tail[:24].hex())

        f.seek(dir_off)
        n = struct.unpack("<I", f.read(4))[0]
        print(f"  [dir_off as ABSOLUTE] entry_count={n}")
        if not (0 < n < 300000):
            print("  -> implausible count; dir_offset is probably relative")
            return

        for i in range(min(limit, n)):
            pl = struct.unpack("<I", f.read(4))[0]
            if pl > 4096:
                print(f"    entry {i}: implausible path_len={pl}; aborting")
                break
            p = f.read(pl)
            off, size = struct.unpack("<QQ", f.read(16))
            _md5 = f.read(16)
            fl = struct.unpack("<I", f.read(4))[0]
            txt = p.decode("utf-8", "replace").rstrip("\0")
            abs_off = file_base + off
            here = f.tell()
            f.seek(abs_off)
            head = f.read(10)
            f.seek(here)
            print(f"    {txt[:64]:<64} off={off:<9} size={size:<8} fl={fl:<3} @{abs_off} {head!r}")


for arg in sys.argv[1:]:
    probe(arg)
