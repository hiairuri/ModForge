using ModForge.Core.Catalog;
using ModForge.Core.Validation;

// Verifies the P0 mod offline: builds the vanilla catalog, then checks the
// generated assembly against the invariants the game enforces at startup.
//
// Usage: verify <gameDir> <modAssembly>

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: verify <gameDir> <modAssembly>");
    return 2;
}

string gameDir = args[0];
string modAssembly = args[1];

Console.WriteLine("building vanilla catalog (offline)...");
var sw = System.Diagnostics.Stopwatch.StartNew();
VanillaCatalog catalog = VanillaCatalog.Build(gameDir);
sw.Stop();

Console.WriteLine($"  game version : {catalog.GameVersion}");
Console.WriteLine($"  entries      : {catalog.Entries.Count}  ({sw.ElapsedMilliseconds} ms)");

foreach (string category in new[] { "CARD", "POWER", "RELIC", "CHARACTER", "ORB" })
{
    var slice = catalog.ByCategory(category).ToList();
    var named = slice.Count(e => e.NameEn is not null);
    var namedZh = slice.Count(e => e.NameZh is not null);
    Console.WriteLine($"  {category,-12} {slice.Count,5} entries, {named,5} with EN name, {namedZh,5} with ZHS name");
}

Console.WriteLine();
Console.WriteLine("sample lookup (id -> EN / ZHS):");
foreach (string wanted in new[] { "STRIKE_IRONCLAD", "BASH", "STRENGTH_POWER", "VULNERABLE_POWER" })
{
    var hit = catalog.Entries.FirstOrDefault(e => e.Entry == wanted);
    if (hit is not null)
    {
        Console.WriteLine($"  {hit.Category}.{hit.Entry,-22} {hit.NameEn ?? "?",-20} {hit.NameZh ?? "?"}");
    }
    else
    {
        Console.WriteLine($"  {wanted}: NOT FOUND");
    }
}

var vanillaIds = catalog.Entries.Select(e => $"{e.Category}.{e.Entry}").ToHashSet(StringComparer.Ordinal);

Console.WriteLine();
Console.WriteLine($"validating {Path.GetFileName(modAssembly)} ...");
ValidationResult result = ModValidator.Validate(modAssembly, gameDir, vanillaIds);

Console.WriteLine($"  discovered   : {result.CardCount} card(s), {result.PowerCount} power(s)");
if (result.Issues.Count == 0)
{
    Console.WriteLine("  no issues");
}
else
{
    foreach (ValidationIssue issue in result.Issues)
    {
        Console.WriteLine($"  [{issue.Severity}] {issue.Message}" + (issue.Where is null ? "" : $"  ({issue.Where})"));
    }
}

// A check on our own id derivation: these must be unique across the whole catalog,
// because duplicate ids crash the game.
var dupes = catalog.Entries
    .GroupBy(e => $"{e.Category}.{e.Entry}", StringComparer.Ordinal)
    .Where(g => g.Count() > 1)
    .ToList();

Console.WriteLine();
Console.WriteLine($"catalog duplicate-id check: {dupes.Count} collision(s)");
foreach (var g in dupes.Take(10))
{
    Console.WriteLine($"  {g.Key} <- {string.Join(", ", g.Select(e => e.FullTypeName))}");
}

bool failed = result.HasErrors || dupes.Count > 0;
Console.WriteLine();
Console.WriteLine(failed ? "RESULT: FAILED" : "RESULT: OK");
return failed ? 1 : 0;
