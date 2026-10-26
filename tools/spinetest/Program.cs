using ModForge.Core.Pck;
using ModForge.Core.Spine;

// Validates the Spine reader against a real skin pack.
// Usage: spinetest <pack.pck>

string packPath = args.Length > 0
    ? args[0]
    : @"D:\Program Files (x86)\Steam\steamapps\workshop\content\2868840\3747589574\regentSkin.pck";

Dictionary<string, byte[]> entries = PckFile.Read(packPath);
Console.WriteLine($"pack: {Path.GetFileName(packPath)}  ({entries.Count} entries)");
Console.WriteLine();

int skelCount = 0;
foreach ((string name, byte[] data) in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
{
    if (!name.EndsWith(".spskel", StringComparison.Ordinal) &&
        !name.EndsWith(".skel", StringComparison.Ordinal))
    {
        continue;
    }

    skelCount++;
    SpineSkeletonReader.Result r = SpineSkeletonReader.Read(data);
    Console.WriteLine($"{name}");
    Console.WriteLine($"  version    : {r.Version}");
    Console.WriteLine($"  strings    : {r.AllNames.Count}");
    Console.WriteLine($"  candidates : {r.AnimationCandidates.Count}");
    if (r.Error is not null)
    {
        Console.WriteLine($"  ERROR      : {r.Error}");
    }

    if (r.AnimationCandidates.Count > 0)
    {
        Console.WriteLine("  -> " + string.Join(", ", r.AnimationCandidates.Take(28)));
    }

    Console.WriteLine();
}

Console.WriteLine($"skeletons examined: {skelCount}");
return skelCount > 0 ? 0 : 1;
