using ModForge.Core;
using ModForge.Core.Catalog;
using ModForge.Core.Export;
using ModForge.Core.Model;
using ModForge.Core.Skin;
using ModForge.Core.Validation;

// End-to-end check including a real skin: build a project with cards, buffs and a
// skin repacked from an installed skin mod, then export and validate.
//
// Usage: skinexport <gameDir> <skinPack.pck> [outDir]

string gameDir = args.Length > 0
    ? args[0]
    : @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2";
string skinPack = args.Length > 1
    ? args[1]
    : @"D:\Program Files (x86)\Steam\steamapps\workshop\content\2868840\3747589574\regentSkin.pck";
string outDir = args.Length > 2 ? args[2] : @"D:\ModForge\out\skintest";

Console.WriteLine("== reading skin pack ==");
SkinPack skin = SkinPackReader.Read(skinPack);
Console.WriteLine($"  pack     : {Path.GetFileName(skinPack)}");
Console.WriteLine($"  targets  : {string.Join(", ", skin.Targets)}");
Console.WriteLine($"  assets   : {skin.Assets.Count}");
foreach (SkinTargetKind kind in Enum.GetValues<SkinTargetKind>())
{
    int n = skin.Assets.Count(a => a.Kind == kind);
    if (n > 0)
    {
        Console.WriteLine($"    {kind,-16} {n}");
    }
}

IReadOnlyList<string> candidates = skin.AnimationCandidates;
Console.WriteLine($"  animations: {candidates.Count}");
Console.WriteLine($"    first 12: {string.Join(", ", candidates.Take(12))}");

ModProject project = SampleProject.Create();
project.Id = "EmberForgeSkin";

// Reuse the sample's cards and buffs, and add the skin with slot bindings chosen
// from the pack's own animation names.
var skinDef = new SkinDef
{
    SourcePack = Path.GetFullPath(skinPack),
    Name = skin.Name,
    Target = skin.Targets.FirstOrDefault() ?? "regent",
    AvailableAnimations = candidates.Take(200).ToList(),
    Routes = skin.Assets.Select(a => a.Route).Take(200).ToList(),
};

// Delegates slot suggestions to the shared helper, so the CLI and the editor agree.
foreach (var (slot, pick) in ModForge.Core.Skin.AnimationBinder.SuggestAll(candidates))
{
    skinDef.SlotBindings[slot] = pick;
}

project.Skins.Add(skinDef);

Console.WriteLine();
Console.WriteLine("== slot bindings chosen ==");
foreach ((string slot, string anim) in skinDef.SlotBindings)
{
    Console.WriteLine($"  {slot,-8} -> {(string.IsNullOrEmpty(anim) ? "(none found)" : anim)}");
}

Console.WriteLine();
Console.WriteLine("== validation ==");
VanillaCatalog catalog = VanillaCatalog.Build(gameDir);
IReadOnlyList<ProjectIssue> issues = ProjectValidator.Validate(project, catalog);
if (issues.Count == 0)
{
    Console.WriteLine("  no issues");
}
else
{
    foreach (ProjectIssue i in issues)
    {
        Console.WriteLine($"  [{i.Severity}] {i.Message}");
    }
}

if (issues.Any(i => i.Severity == "error"))
{
    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (validation)");
    return 1;
}

Console.WriteLine();
Console.WriteLine("== generated sources ==");
var gen = new ModForge.Core.Generate.CodeGenerator(project);
foreach ((string path, string src) in gen.GenerateAll())
{
    Console.WriteLine($"  {path}  ({src.Length} chars)");
}

Console.WriteLine();
Console.WriteLine("== export ==");
var exporter = new Exporter(gameDir);
ExportResult result = exporter.Export(project, outDir);
foreach (string m in result.Messages)
{
    Console.WriteLine("  " + m);
}

if (!result.Success)
{
    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (export)");
    return 1;
}

string dll = result.ArtifactPaths.First(p => p.EndsWith(".dll", StringComparison.Ordinal));
var vanillaIds = catalog.Entries.Select(e => $"{e.Category}.{e.Entry}")
    .ToHashSet(StringComparer.Ordinal);

Console.WriteLine();
Console.WriteLine("== assembly validation ==");
ModForge.Core.Validation.ValidationResult check = ModValidator.Validate(dll, gameDir, vanillaIds);
Console.WriteLine($"  cards {check.CardCount}, powers {check.PowerCount}");
foreach (ValidationIssue issue in check.Issues)
{
    Console.WriteLine($"  [{issue.Severity}] {issue.Message}");
}

// Confirm the skin assets and binding table landed in the output pack.
string pck = result.ArtifactPaths.First(p => p.EndsWith(".pck", StringComparison.Ordinal));
Dictionary<string, byte[]> packed = ModForge.Core.Pck.PckFile.Read(pck);
int skinAssets = packed.Keys.Count(k => k.StartsWith("res://animations/", StringComparison.Ordinal) ||
                                        k.StartsWith("animations/", StringComparison.Ordinal));
Console.WriteLine();
Console.WriteLine($"== output pack: {packed.Count} entries, {skinAssets} skin animation asset(s) ==");
foreach (string key in packed.Keys.Where(k => k.Contains("skins/")).OrderBy(k => k))
{
    Console.WriteLine($"  binding table: {key}");
    Console.WriteLine(System.Text.Encoding.UTF8.GetString(packed[key]));
}

Console.WriteLine();
Console.WriteLine(check.HasErrors ? "RESULT: FAILED" : "RESULT: OK");
return check.HasErrors ? 1 : 0;
