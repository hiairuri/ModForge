using ModForge.Core.Pck;

// Usage: pckgen <out.pck> <rootDir> [--prefix <packPrefix>]
//
// Packs every file under <rootDir> into a Godot 4 PCK v3 archive, using
// "<packPrefix>/<relative-path>" as the in-pack path. Paths always use forward
// slashes, as the engine expects.

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: pckgen <out.pck> <rootDir> [--prefix <packPrefix>]");
    return 2;
}

string outPath = args[0];
string rootDir = args[1];
string prefix = string.Empty;

for (int i = 2; i < args.Length; i++)
{
    if (args[i] == "--prefix" && i + 1 < args.Length)
    {
        prefix = args[++i].Trim('/');
    }
}

if (!Directory.Exists(rootDir))
{
    Console.Error.WriteLine($"error: source directory not found: {rootDir}");
    return 1;
}

var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
string fullRoot = Path.GetFullPath(rootDir);

foreach (string file in Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories))
{
    string rel = Path.GetRelativePath(fullRoot, file).Replace('\\', '/');
    string packPath = string.IsNullOrEmpty(prefix) ? rel : $"{prefix}/{rel}";

    if (!PckFile.IsSafePackPath(packPath))
    {
        Console.Error.WriteLine($"error: unsafe pack path '{packPath}'");
        return 1;
    }

    files[packPath] = File.ReadAllBytes(file);
}

PckFile.Write(outPath, files);

Console.WriteLine($"wrote {outPath} ({new FileInfo(outPath).Length} bytes, {files.Count} entries)");
foreach (string name in files.Keys)
{
    Console.WriteLine($"  {name}  ({files[name].Length} bytes)");
}

// Round-trip check so a bad archive is caught here rather than in-game.
var readBack = PckFile.Read(outPath);
if (readBack.Count != files.Count)
{
    Console.Error.WriteLine("error: round-trip entry count mismatch");
    return 1;
}

foreach ((string name, byte[] data) in files)
{
    if (!readBack.TryGetValue(name, out byte[]? back) || !back.AsSpan().SequenceEqual(data))
    {
        Console.Error.WriteLine($"error: round-trip mismatch for {name}");
        return 1;
    }
}

Console.WriteLine("round-trip OK");
return 0;
