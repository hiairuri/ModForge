using System.Buffers.Binary;
using ModForge.Core.Gfx;
using ModForge.Core.Pck;

// Verifies CtexWriter two ways:
//   1. Re-parsing a real shipped .ctex and confirming our reader matches the values
//      the engine's loader would compute (ground truth for the layout).
//   2. Writing our own .ctex from a real card PNG and validating it with the exact
//      field rules read from Godot's compressed_texture.cpp.
//
// Usage: ctextest <gameDir>

string gameDir = args.Length > 0
    ? args[0]
    : @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2";

string pck = Path.Combine(gameDir, "SlayTheSpire2.pck");
Console.WriteLine($"pack: {pck}");

// ---- Part 1: parse a real portrait ctex and check our field offsets ----

IReadOnlyList<PckFile.EntryInfo> directory = PckFile.ReadDirectory(pck);

// Locate a portrait by reading the import stub, which names the .ctex that holds it.
PckFile.EntryInfo? portrait = null;
foreach (PckFile.EntryInfo stub in directory.Where(e =>
             e.Path.Contains("card_portraits/ironclad/aggression.png", StringComparison.Ordinal) &&
             e.Path.EndsWith(".import", StringComparison.Ordinal)))
{
    string text = System.Text.Encoding.UTF8.GetString(PckFile.ReadEntry(pck, stub));
    int at = text.IndexOf("path=\"res://", StringComparison.Ordinal);
    if (at < 0)
    {
        continue;
    }

    int start = at + "path=\"res://".Length;
    int end = text.IndexOf('"', start);
    string imported = text[start..end];

    portrait = directory.FirstOrDefault(e => e.Path == imported);
    if (portrait is not null)
    {
        break;
    }
}

if (portrait is null)
{
    Console.WriteLine("could not find a sample portrait ctex");
    return 1;
}

byte[] real = PckFile.ReadEntry(pck, portrait);
Console.WriteLine();
Console.WriteLine("== real shipped .ctex ==");
Console.WriteLine($"  path         : {portrait.Path}");
Console.WriteLine($"  size         : {real.Length}");

uint version = BinaryPrimitives.ReadUInt32LittleEndian(real.AsSpan(4));
uint declaredW = BinaryPrimitives.ReadUInt32LittleEndian(real.AsSpan(8));
uint declaredH = BinaryPrimitives.ReadUInt32LittleEndian(real.AsSpan(12));
uint dataFormat = BinaryPrimitives.ReadUInt32LittleEndian(real.AsSpan(36));
ushort innerW = BinaryPrimitives.ReadUInt16LittleEndian(real.AsSpan(40));
ushort innerH = BinaryPrimitives.ReadUInt16LittleEndian(real.AsSpan(42));
uint mipmaps = BinaryPrimitives.ReadUInt32LittleEndian(real.AsSpan(44));
uint format = BinaryPrimitives.ReadUInt32LittleEndian(real.AsSpan(48));
uint payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(real.AsSpan(52));

Console.WriteLine($"  magic        : {System.Text.Encoding.ASCII.GetString(real, 0, 4)}");
Console.WriteLine($"  version      : {version}");
Console.WriteLine($"  outer w x h  : {declaredW} x {declaredH}");
Console.WriteLine($"  data format  : {dataFormat} (1=PNG, 2=WEBP, 0=raw)");
Console.WriteLine($"  inner w x h  : {innerW} x {innerH}");
Console.WriteLine($"  mipmaps      : {mipmaps}");
Console.WriteLine($"  pixel format : {format}");
Console.WriteLine($"  payload size : {payloadSize}");
Console.WriteLine($"  payload end  : {56 + payloadSize} (file size {real.Length})");
Console.WriteLine($"  payload magic: {System.Text.Encoding.ASCII.GetString(real, 56, 4)}");

bool layoutOk = 56 + payloadSize == real.Length && dataFormat == 2 &&
                innerW == declaredW && innerH == declaredH;
Console.WriteLine($"  layout check : {(layoutOk ? "OK" : "MISMATCH")}");

// ---- Part 2: build our own ctex from a real PNG found in the pack ----

Console.WriteLine();
Console.WriteLine("== writing our own .ctex from a shipped PNG ==");

PckFile.EntryInfo? pngEntry = directory.FirstOrDefault(e =>
    e.Path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
    e.Size > 2000 && e.Size < 400_000);

if (pngEntry is null)
{
    Console.WriteLine("  no suitable PNG found to test with");
    return layoutOk ? 0 : 1;
}

byte[] png = PckFile.ReadEntry(pck, pngEntry);
Console.WriteLine($"  source PNG   : {pngEntry.Path} ({png.Length} bytes)");

(int pw, int ph) = CtexWriter.ReadPngSize(png);
Console.WriteLine($"  png size     : {pw} x {ph}");

byte[] written = CtexWriter.FromPng(png);
Console.WriteLine($"  .ctex built  : {written.Length} bytes");

// Validate against the engine's own load routine, step by step.
Console.WriteLine();
Console.WriteLine("== validating with the engine's field rules ==");
string magic = System.Text.Encoding.ASCII.GetString(written, 0, 4);
uint wVersion = BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(4));
uint wOuterW = BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(8));
uint wOuterH = BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(12));
uint wDataFormat = BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(36));
ushort wInnerW = BinaryPrimitives.ReadUInt16LittleEndian(written.AsSpan(40));
ushort wInnerH = BinaryPrimitives.ReadUInt16LittleEndian(written.AsSpan(42));
uint wMipmaps = BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(44));
uint wPayloadSize = BinaryPrimitives.ReadUInt32LittleEndian(written.AsSpan(52));

Console.WriteLine($"  magic           : {magic} {(magic == "GST2" ? "OK" : "BAD")}");
Console.WriteLine($"  version         : {wVersion} {(wVersion == 1 ? "OK" : "BAD")}");
Console.WriteLine($"  outer w x h     : {wOuterW} x {wOuterH} {(wOuterW == pw && wOuterH == ph ? "OK" : "BAD")}");
Console.WriteLine($"  data format     : {wDataFormat} {(wDataFormat == 1 ? "OK (PNG)" : "BAD")}");
Console.WriteLine($"  inner w x h     : {wInnerW} x {wInnerH} {(wInnerW == pw && wInnerH == ph ? "OK" : "BAD")}");
Console.WriteLine($"  mipmaps         : {wMipmaps} {(wMipmaps == 0 ? "OK" : "BAD")}");

// The loader reads a size-prefixed payload for the PNG/WEBP containers.
int expectedEnd = 56 + (int)wPayloadSize;
bool endOk = expectedEnd == written.Length;
Console.WriteLine($"  payload size    : {wPayloadSize}");
Console.WriteLine($"  payload ends at : {expectedEnd} (file {written.Length}) {(endOk ? "OK" : "BAD")}");

byte[] embedded = written.AsSpan(56, (int)wPayloadSize).ToArray();
bool identical = embedded.AsSpan().SequenceEqual(png);
Console.WriteLine($"  embedded is the original PNG bytes: {(identical ? "OK" : "BAD")}");
Console.WriteLine($"  embedded magic  : {System.Text.Encoding.ASCII.GetString(embedded, 0, 4)}");

// Re-reading dimensions through our own PNG parser proves the header is self-consistent.
(int rw, int rh) = CtexWriter.ReadPngSize(embedded);
Console.WriteLine($"  dimensions parse back: {rw} x {rh} {(rw == pw && rh == ph ? "OK" : "BAD")}");

bool allOk = layoutOk && magic == "GST2" && wVersion == 1 && wDataFormat == 1 &&
             wOuterW == pw && wOuterH == ph && wInnerW == pw && wInnerH == ph &&
             wMipmaps == 0 && endOk && identical && rw == pw && rh == ph;

Console.WriteLine();
Console.WriteLine(allOk ? "RESULT: OK" : "RESULT: FAILED");
return allOk ? 0 : 1;
