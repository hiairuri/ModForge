using ModForge.Core;
using ModForge.Core.Catalog;
using ModForge.Core.Export;
using ModForge.Core.Model;
using ModForge.Core.Pck;
using ModForge.Core.Validation;

// Verifies custom card art: builds a project whose card points at a real PNG, exports,
// then confirms the exported pack contains a well-formed .ctex at the route the
// generated card will request.
//
// Usage: arttest <gameDir> <artPng> [outDir]

string gameDir = args.Length > 0
    ? args[0]
    : @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2";
string outDir = args.Length > 2 ? args[2] : @"D:\ModForge\out\arttest";

// Pick a real PNG out of the game pack when the caller did not supply one.
string artPath = args.Length > 1 ? args[1] : "";

if (string.IsNullOrWhiteSpace(artPath))
{
    string pck = Path.Combine(gameDir, "SlayTheSpire2.pck");
    IReadOnlyList<PckFile.EntryInfo> dir = PckFile.ReadDirectory(pck);

    PckFile.EntryInfo? png = dir.FirstOrDefault(e =>
        e.Path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
        e.Size is > 3000 and < 200_000);

    if (png is null)
    {
        Console.WriteLine("no sample PNG available");
        return 1;
    }

    artPath = Path.Combine(Path.GetTempPath(), "modforge_art_sample.png");
    File.WriteAllBytes(artPath, PckFile.ReadEntry(pck, png));
    Console.WriteLine($"sample art: {png.Path} -> {artPath}");
}

ModProject project = SampleProject.Create();
project.Id = "EmberForgeArt";
project.SelfCheckOnStartup = false;

// Attach art to the first card.
project.Cards[0].ArtFile = artPath;

Console.WriteLine($"art file  : {Path.GetFileName(artPath)} ({new FileInfo(artPath).Length} bytes)");
Console.WriteLine($"card      : {project.Cards[0].Id}");

VanillaCatalog catalog = VanillaCatalog.Build(gameDir);
IReadOnlyList<ProjectIssue> issues = ProjectValidator.Validate(project, catalog);
if (issues.Any(i => i.Severity == "error"))
{
    foreach (ProjectIssue i in issues)
    {
        Console.WriteLine($"  [{i.Severity}] {i.Message}");
    }

    return 1;
}

Console.WriteLine();
Console.WriteLine("== export ==");
var exporter = new Exporter(gameDir)
{
    // Discover Godot so PNG art is converted to the WebP the game requires.
    WebpConverter = ModForge.Core.Gfx.WebpConverter.Discover(
        @"D:\Tools",
        @"D:\ModForge\tools\godot-helpers\convert_to_webp.gd"),
};

Console.WriteLine($"  webp converter available: {exporter.WebpConverter.IsAvailable}");

ExportResult result = exporter.Export(project, outDir);
foreach (string m in result.Messages)
{
    Console.WriteLine("  " + m);
}

if (!result.Success)
{
    Console.WriteLine("RESULT: FAILED (export)");
    return 1;
}

// Inspect the packed art.
string pckPath = result.ArtifactPaths.First(p => p.EndsWith(".pck", StringComparison.Ordinal));
Dictionary<string, byte[]> packed = PckFile.Read(pckPath);
string expected = Exporter.CardArtRoute(project, project.Cards[0]);

Console.WriteLine();
Console.WriteLine("== packed art ==");
Console.WriteLine($"  expected route: {expected}");

if (!packed.TryGetValue(expected, out byte[]? ctex))
{
    Console.WriteLine("  RESULT: FAILED (art not in pack)");
    return 1;
}

Console.WriteLine($"  found         : {ctex.Length} bytes");
string magic = System.Text.Encoding.ASCII.GetString(ctex, 0, 4);
int w = (int)BitConverter.ToUInt32(ctex, 8);
int h = (int)BitConverter.ToUInt32(ctex, 12);
uint dataFormat = BitConverter.ToUInt32(ctex, 36);
uint payloadSize = BitConverter.ToUInt32(ctex, 52);

Console.WriteLine($"  magic         : {magic}");
Console.WriteLine($"  size          : {w} x {h}");
Console.WriteLine($"  data format   : {dataFormat} (2=WEBP, the only kind the game loads)");
Console.WriteLine($"  payload bytes : {payloadSize}");
Console.WriteLine($"  56+payload    : {56 + payloadSize} (file {ctex.Length})");

byte[] embedded = ctex.AsSpan(56, (int)payloadSize).ToArray();
// The packed payload is the converted WebP, so compare against it rather than the PNG source.
string packedSource = Path.ChangeExtension(artPath, ".webp");
byte[] expectedPayload = File.Exists(packedSource)
    ? File.ReadAllBytes(packedSource)
    : File.ReadAllBytes(artPath);
bool sameAsOriginal = embedded.AsSpan().SequenceEqual(expectedPayload);

bool ok = magic == "GST2" && dataFormat == 2 &&
          56 + payloadSize == ctex.Length && sameAsOriginal;

Console.WriteLine($"  payload matches source image: {sameAsOriginal}");

Console.WriteLine();
Console.WriteLine(ok ? "RESULT: OK" : "RESULT: FAILED");
return ok ? 0 : 1;
