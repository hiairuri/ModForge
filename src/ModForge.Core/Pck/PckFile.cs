using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ModForge.Core.Pck;

/// <summary>
/// Reads and writes Godot 4 PCK v3 packs.
///
/// Layout (verified byte-for-byte against SlayTheSpire2.pck and several mod packs):
///   header  : "GDPC" | u32 format_version | u32 major | u32 minor | u32 patch
///             | u32 flags | u64 file_base | u64 dir_offset | 64 reserved bytes  (104 bytes)
///   payload : file bytes, 16-byte aligned; absolute position = file_base + entry.offset
///   dir     : at ABSOLUTE dir_offset: u32 entry_count, then entries
///   entry   : u32 path_len | path bytes | u64 offset | u64 size
///             | 16-byte digest | u32 flags
///
/// Observed variation between packs: the engine pads path names with NULs to a
/// 4-byte boundary, while exported mod packs do not; the directory may sit before
/// or after the payload area. Readers here tolerate both. Writers here emit the
/// layout the engine itself produces (file_base 16-aligned after the header,
/// directory last, unpadded paths).
///
/// Note on the digest: the entry record reserves exactly 16 bytes for a digest of
/// the payload. That field is part of the container format, so the algorithm is
/// fixed at MD5. It is an integrity check for a local asset archive, not a
/// security boundary, and changing the width would shift the record layout and
/// make the pack unreadable by the engine.
/// </summary>
public static class PckFile
{
    private const int ReservedSize = 16 * 4;
    private const int HeaderBytes = 4 + (5 * 4) + 8 + 8 + ReservedSize; // 104
    private const int Alignment = 16;
    private const uint FormatVersion = 3;
    private const uint GodotMajor = 4;
    private const uint GodotMinor = 5;
    private const uint GodotPatch = 1;
    private const uint PackFlags = 2;

    /// <summary>
    /// Rejects pack paths that could escape the archive root once extracted.
    /// Mod packs come from third parties, so a stored path is never trusted.
    /// </summary>
    public static bool IsSafePackPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith("res://", StringComparison.Ordinal))
        {
            path = path.Substring("res://".Length);
        }

        if (path.StartsWith('/') || path.StartsWith('\\') || path.Contains('\\'))
        {
            return false;
        }

        // Windows drive-absolute form, e.g. "C:/foo".
        if (path.Length >= 2 && path[1] == ':')
        {
            return false;
        }

        foreach (string segment in path.Split('/'))
        {
            if (segment == "..")
            {
                return false;
            }
        }

        foreach (char c in path)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    public static void Write(string outPath, IReadOnlyDictionary<string, byte[]> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var blobs = new List<(string Name, byte[] Data, long Offset)>();
        long offset = 0;

        foreach (string name in files.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!IsSafePackPath(name))
            {
                throw new ArgumentException($"Unsafe pack path: '{name}'", nameof(files));
            }

            string normalized = name.StartsWith("res://", StringComparison.Ordinal)
                ? name.Substring("res://".Length)
                : name;

            byte[] data = files[name] ?? Array.Empty<byte>();
            offset += PadTo(offset);
            blobs.Add((normalized, data, offset));
            offset += data.Length;
        }

        // Payload offsets are relative to file_base, which the engine places at the
        // first 16-byte boundary after the header.
        long fileBase = AlignUp(HeaderBytes, Alignment);
        long dirOffset = fileBase + offset;

        using var stream = File.Create(outPath);
        using var writer = new BinaryWriter(stream, Encoding.UTF8);

        writer.Write(Encoding.ASCII.GetBytes("GDPC"));
        writer.Write(FormatVersion);
        writer.Write(GodotMajor);
        writer.Write(GodotMinor);
        writer.Write(GodotPatch);
        writer.Write(PackFlags);
        writer.Write((ulong)fileBase);
        writer.Write((ulong)dirOffset);
        writer.Write(new byte[ReservedSize]);
        writer.Write(new byte[fileBase - HeaderBytes]);

        // `written` tracks position within the payload area (0-based), matching the
        // offsets stored in the directory.
        long written = 0;
        foreach ((string _, byte[] data, long pos) in blobs)
        {
            long padding = pos - written;
            if (padding > 0)
            {
                writer.Write(new byte[padding]);
                written += padding;
            }

            writer.Write(data);
            written += data.Length;
        }

        writer.Write((uint)blobs.Count);
        foreach ((string name, byte[] data, long pos) in blobs)
        {
            byte[] raw = Encoding.UTF8.GetBytes(name);
            writer.Write((uint)raw.Length);
            writer.Write(raw);
            writer.Write((ulong)pos);
            writer.Write((ulong)data.Length);
            writer.Write(Digest(data));
            writer.Write(0u);
        }
    }

    /// <summary>One directory entry, without its payload.</summary>
    public sealed record EntryInfo(string Path, long Offset, long Size);

    /// <summary>
    /// Reads only the directory table, without materializing any payload.
    ///
    /// This matters when scanning many large packs: a workshop folder can hold
    /// hundreds of megabytes per pack, and callers that only need to inspect a few
    /// small entries should not pay to read all of it.
    /// </summary>
    public static IReadOnlyList<EntryInfo> ReadDirectory(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        if (stream.Length < HeaderBytes)
        {
            throw new InvalidDataException("Not a GDPC pack.");
        }

        byte[] magic = reader.ReadBytes(4);
        if (Encoding.ASCII.GetString(magic) != "GDPC")
        {
            throw new InvalidDataException("Not a GDPC pack.");
        }

        stream.Position = 24;
        long fileBase = reader.ReadInt64();
        long dirOffset = reader.ReadInt64();

        if (dirOffset < 0 || dirOffset + 4 > stream.Length)
        {
            throw new InvalidDataException("Pack directory offset is out of range.");
        }

        stream.Position = dirOffset;
        uint count = reader.ReadUInt32();

        var result = new List<EntryInfo>((int)Math.Min(count, 65536));
        for (uint i = 0; i < count; i++)
        {
            if (stream.Position + 4 > stream.Length)
            {
                break;
            }

            uint pathLen = reader.ReadUInt32();
            if (pathLen > 4096 || stream.Position + pathLen > stream.Length)
            {
                throw new InvalidDataException("Corrupt pack entry path.");
            }

            string name = Encoding.UTF8.GetString(reader.ReadBytes((int)pathLen)).TrimEnd('\0');

            ulong entryOffset = reader.ReadUInt64();
            ulong size = reader.ReadUInt64();
            reader.ReadBytes(16); // digest
            reader.ReadUInt32();  // flags

            if (!IsSafePackPath(name))
            {
                continue;
            }

            result.Add(new EntryInfo(name, fileBase + (long)entryOffset, (long)size));
        }

        return result;
    }

    /// <summary>Reads a single entry's payload using a previously read directory.</summary>
    public static byte[] ReadEntry(string path, EntryInfo entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var stream = File.OpenRead(path);
        if (entry.Offset < 0 || entry.Offset + entry.Size > stream.Length)
        {
            throw new InvalidDataException($"Pack entry '{entry.Path}' is out of range.");
        }

        stream.Position = entry.Offset;
        byte[] data = new byte[entry.Size];
        stream.ReadExactly(data);
        return data;
    }

    public static Dictionary<string, byte[]> Read(string path)
    {
        byte[] blob = File.ReadAllBytes(path);
        if (blob.Length < HeaderBytes || Encoding.ASCII.GetString(blob, 0, 4) != "GDPC")
        {
            throw new InvalidDataException("Not a GDPC pack.");
        }

        // Header: "GDPC"(4) + five u32 (20) = 24 bytes, then file_base and dir_offset.
        long fileBase = BitConverter.ToInt64(blob, 24);
        long dirOffset = BitConverter.ToInt64(blob, 32);

        if (dirOffset < 0 || dirOffset + 4 > blob.Length)
        {
            throw new InvalidDataException("Pack directory offset is out of range.");
        }

        int pos = (int)dirOffset;
        uint count = BitConverter.ToUInt32(blob, pos);
        pos += 4;

        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (uint i = 0; i < count; i++)
        {
            uint pathLen = BitConverter.ToUInt32(blob, pos);
            pos += 4;

            if (pathLen > 4096 || pos + pathLen > blob.Length)
            {
                throw new InvalidDataException("Corrupt pack entry path.");
            }

            // The engine NUL-pads paths to a 4-byte boundary; exported packs do not.
            string name = Encoding.UTF8.GetString(blob, pos, (int)pathLen).TrimEnd('\0');
            pos += (int)pathLen;

            ulong entryOffset = BitConverter.ToUInt64(blob, pos);
            ulong size = BitConverter.ToUInt64(blob, pos + 8);
            pos += 16;
            pos += 16; // digest
            pos += 4;  // flags

            if (!IsSafePackPath(name))
            {
                // Never hand an escaping path to a caller that extracts.
                continue;
            }

            long start = fileBase + (long)entryOffset;
            if (start < 0 || start + (long)size > blob.Length)
            {
                throw new InvalidDataException($"Pack entry '{name}' is out of range.");
            }

            byte[] data = new byte[(int)size];
            Array.Copy(blob, start, data, 0, (int)size);
            result[name] = data;
        }

        return result;
    }

    private static long PadTo(long offset)
    {
        long rem = offset % Alignment;
        return rem == 0 ? 0 : Alignment - rem;
    }

    private static long AlignUp(long value, long alignment)
    {
        return value + PadTo(value);
    }

    private static byte[] Digest(byte[] data)
    {
        // Fixed 16-byte field required by the PCK v3 entry record.
        return MD5.HashData(data);
    }
}
