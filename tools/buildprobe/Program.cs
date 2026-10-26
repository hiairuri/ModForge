using System.Buffers.Binary;
using ModForge.Core.Gfx;
using ModForge.Core.Pck;

// Builds the ArtProbe mod's PCK, containing the same image encoded three ways so the
// in-game probe can report which ones the exported engine actually accepts.
//
// Usage: buildprobe <gameDir> <outDir>

string gameDir = args.Length > 0
    ? args[0]
    : @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2";
string outDir = args.Length > 1 ? args[1] : @"D:\ModForge\out\ArtProbe";

string pckPath = Path.Combine(gameDir, "SlayTheSpire2.pck");
IReadOnlyList<PckFile.EntryInfo> dir = PckFile.ReadDirectory(pckPath);

// Take a real PNG and a real WebP out of the game so both encodings use genuine data.
PckFile.EntryInfo? pngEntry = dir.FirstOrDefault(e =>
    e.Path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && e.Size is > 3000 and < 120_000);

if (pngEntry is null)
{
    Console.WriteLine("no sample PNG found");
    return 1;
}

byte[] png = PckFile.ReadEntry(pckPath, pngEntry);
Console.WriteLine($"source PNG : {pngEntry.Path} ({png.Length} bytes)");

// Find an embedded WebP inside a shipped .ctex of the format used by card portraits.
byte[]? webp = null;
foreach (PckFile.EntryInfo e in dir.Where(x =>
             x.Path.EndsWith(".ctex", StringComparison.Ordinal) && x.Size is > 20_000 and < 400_000))
{
    byte[] data = PckFile.ReadEntry(pckPath, e);
    webp = ExtractWebp(data);
    if (webp is not null)
    {
        Console.WriteLine($"source WEBP: embedded in {e.Path} ({webp.Length} bytes)");
        break;
    }
}

if (webp is null)
{
    Console.WriteLine("no embedded WebP found");
    return 1;
}

var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

// 1. The same PNG wrapped in our own .ctex (data_format = 1).
files["ArtProbe/art/png_payload.ctex"] = CtexWriter.FromPng(png);

// 2. A real game WebP wrapped in our own .ctex (data_format = 2).
files["ArtProbe/art/webp_payload.ctex"] = CtexWriter.FromWebp(webp);

// 3. The import-remap recipe used by released mods: a .png.import stub naming a
//    .ctex, alongside that .ctex.
string stored = $"{Guid.NewGuid():N}";
string remapCtex = $"ArtProbe/.godot/imported/remapped.png-{stored}.ctex";
files[remapCtex] = CtexWriter.FromPng(png);
files["ArtProbe/art/remapped.png.import"] = System.Text.Encoding.UTF8.GetBytes(
    "[remap]\n\n" +
    "importer=\"texture\"\n" +
    "type=\"CompressedTexture2D\"\n" +
    $"path=\"res://{remapCtex}\"\n" +
    "metadata={\n\"vram_texture\": false\n}\n");

string modDir = Path.Combine(outDir, "mods", "ArtProbe");
Directory.CreateDirectory(modDir);

PckFile.Write(Path.Combine(modDir, "ArtProbe.pck"), files);

File.WriteAllText(
    Path.Combine(modDir, "ArtProbe.json"),
    """
    {
      "id": "ArtProbe",
      "name": "ArtProbe",
      "author": "ModForge",
      "description": "Diagnostic: determines which .ctex encodings the game can load.",
      "version": "1.0.0",
      "has_pck": true,
      "has_dll": true,
      "dependencies": [],
      "affects_gameplay": false,
      "min_game_version": "0.107.1"
    }
    """);

Console.WriteLine();
Console.WriteLine("packed for probe:");
foreach (string name in files.Keys)
{
    Console.WriteLine($"  {name} ({files[name].Length} bytes)");
}

Console.WriteLine();
Console.WriteLine($"staged at {modDir}");
return 0;

// Scans for the RIFF/WEBP container inside a .ctex payload.
static byte[]? ExtractWebp(byte[] data)
{
    for (int i = 0; i + 12 <= data.Length; i++)
    {
        if (data[i] != 'R' || data[i + 1] != 'I' || data[i + 2] != 'F' || data[i + 3] != 'F' ||
            data[i + 8] != 'W' || data[i + 9] != 'E' || data[i + 10] != 'B' || data[i + 11] != 'P')
        {
            continue;
        }

        int riffSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(i + 4, 4));
        int end = i + 8 + riffSize;
        if (end <= data.Length)
        {
            return data.AsSpan(i, end - i).ToArray();
        }
    }

    return null;
}
