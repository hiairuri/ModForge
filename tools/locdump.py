"""Read a localization table straight out of a Godot PCK. Read-only.

PCK v3: dir_offset is absolute; entry payloads are at file_base + entry.offset.
Payloads in these packs are stored uncompressed.
"""
import json
import struct
import sys


def load_table(pck_path, table_path):
    with open(pck_path, "rb") as f:
        blob = f.read()
    if blob[:4] != b"GDPC":
        raise ValueError("not a GDPC pack")
    # Header: "GDPC"(4) + five u32 (20) = 24 bytes, then file_base, dir_offset.
    file_base, dir_offset = struct.unpack_from("<QQ", blob, 24)
    pos = dir_offset
    count = struct.unpack_from("<I", blob, pos)[0]
    pos += 4
    for _ in range(count):
        plen = struct.unpack_from("<I", blob, pos)[0]
        pos += 4
        name = blob[pos:pos + plen].decode("utf-8", "replace").rstrip("\0")
        pos += plen
        off, size = struct.unpack_from("<QQ", blob, pos)
        pos += 32
        pos += 4
        if name == table_path or name == "res://" + table_path:
            raw = blob[file_base + off:file_base + off + size]
            return json.loads(raw.decode("utf-8"))
    raise KeyError(table_path)


if __name__ == "__main__":
    pck, table = sys.argv[1], sys.argv[2]
    keys = sys.argv[3:]
    data = load_table(pck, table)
    print(f"{table}: {len(data)} keys")
    if not keys:
        for k in list(data)[:12]:
            print(f"  {k!r}: {data[k]!r}")
    for k in keys:
        print(f"  {k!r}: {data.get(k, '<MISSING>')!r}")
