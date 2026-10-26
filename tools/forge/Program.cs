using ModForge.Core;
using ModForge.Core.Catalog;
using ModForge.Core.Export;
using ModForge.Core.Model;
using ModForge.Core.Validation;

// End-to-end check of the export pipeline: build a sample project, validate it,
// export it, then verify the produced assembly against the game's invariants.
//
// Usage: forge <gameDir> [outDir]

string gameDir = args.Length > 0
    ? args[0]
    : @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2";
string outDir = args.Length > 1 ? args[1] : @"D:\ModForge\out\sample";

Console.WriteLine("== sample project ==");
ModProject project = SampleProject.Create();
Console.WriteLine($"  id     : {project.Id}");
Console.WriteLine($"  cards  : {project.Cards.Count}");
Console.WriteLine($"  powers : {project.Powers.Count}");

Console.WriteLine();
Console.WriteLine("== vanilla catalog ==");
VanillaCatalog catalog = VanillaCatalog.Build(gameDir);
Console.WriteLine($"  {catalog.Entries.Count} entries, game {catalog.GameVersion}");

Console.WriteLine();
Console.WriteLine("== validation ==");
IReadOnlyList<ProjectIssue> issues = ProjectValidator.Validate(project, catalog);
if (issues.Count == 0)
{
    Console.WriteLine("  no issues");
}
else
{
    foreach (ProjectIssue issue in issues)
    {
        Console.WriteLine($"  [{issue.Severity}] {issue.Message}");
    }
}

if (issues.Any(i => i.Severity == "error"))
{
    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (validation errors)");
    return 1;
}

Console.WriteLine();
Console.WriteLine("== generated sources ==");
var generator = new ModForge.Core.Generate.CodeGenerator(project);
foreach ((string path, string source) in generator.GenerateAll())
{
    Console.WriteLine($"  {path}  ({source.Length} chars)");
}

Console.WriteLine();
Console.WriteLine("== export ==");
var exporter = new Exporter(gameDir);
ExportResult result = exporter.Export(project, outDir);
foreach (string message in result.Messages)
{
    Console.WriteLine("  " + message);
}

if (!result.Success)
{
    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (export)");
    return 1;
}

// Dump one generated file so the emitted code can be inspected.
string dllPath = result.ArtifactPaths.First(p => p.EndsWith(".dll", StringComparison.Ordinal));
Console.WriteLine();
Console.WriteLine("== sample generated source: first card ==");
string firstCard = generator.GenerateAll().Keys.First(k => k.StartsWith("Cards/", StringComparison.Ordinal));
Console.WriteLine(generator.GenerateAll()[firstCard]);

Console.WriteLine();
Console.WriteLine("== assembly validation ==");
var vanillaIds = catalog.Entries
    .Select(e => $"{e.Category}.{e.Entry}")
    .ToHashSet(StringComparer.Ordinal);

ModForge.Core.Validation.ValidationResult assemblyCheck =
    ModForge.Core.Validation.ModValidator.Validate(dllPath, gameDir, vanillaIds);

Console.WriteLine($"  found {assemblyCheck.CardCount} card(s), {assemblyCheck.PowerCount} power(s)");
foreach (ModForge.Core.Validation.ValidationIssue issue in assemblyCheck.Issues)
{
    Console.WriteLine($"  [{issue.Severity}] {issue.Message}");
}

Console.WriteLine();
Console.WriteLine(assemblyCheck.HasErrors ? "RESULT: FAILED" : "RESULT: OK");
return assemblyCheck.HasErrors ? 1 : 0;
