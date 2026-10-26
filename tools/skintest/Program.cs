using ModForge.Core.Skin;

// Validates the skin reader against installed skin packs.
// Usage: skintest <modsDirOrPack...>

var targets = new List<string>();

if (args.Length == 0)
{
    string workshop = @"D:\Program Files (x86)\Steam\steamapps\workshop\content\2868840";
    if (Directory.Exists(workshop))
    {
        targets.AddRange(Directory.EnumerateDirectories(workshop));
    }
}
else
{
    targets.AddRange(args);
}

foreach (string target in targets)
{
    List<string> packs = Directory.Exists(target)
        ? Directory.EnumerateFiles(target, "*.pck", SearchOption.AllDirectories).ToList()
        : new List<string> { target };

    foreach (string pack in packs)
    {
        SkinPack? skin = null;
        try
        {
            skin = SkinPackReader.Read(pack);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{Path.GetFileName(pack)}: not readable ({ex.Message})");
            continue;
        }

        // A skin pack is any pack that shadows the game's animation or image routes.
        bool looksLikeSkin = skin.Assets.Any(a => a.Kind != SkinTargetKind.Other) ||
                             skin.AnimationCandidates.Count > 0;

        if (!looksLikeSkin)
        {
            Console.WriteLine($"[skip] {Path.GetFileName(pack)}: assets={skin.Assets.Count} anims={skin.AnimationCandidates.Count}");
            continue;
        }

        Console.WriteLine("=".PadRight(72, '='));
        Console.WriteLine($"{Path.GetFileName(pack)}  ({new FileInfo(pack).Length / 1024 / 1024} MB)");
        Console.WriteLine($"  targets    : {string.Join(", ", skin.Targets)}");

        foreach (SkinTargetKind kind in Enum.GetValues<SkinTargetKind>())
        {
            int count = skin.Assets.Count(a => a.Kind == kind);
            if (count > 0)
            {
                var names = skin.Assets.Where(a => a.Kind == kind).Select(a => a.Target).Distinct();
                Console.WriteLine($"  {kind,-16} {count,3} assets  -> {string.Join(", ", names)}");
            }
        }

        List<string> candidates = skin.AnimationCandidates.ToList();
        Console.WriteLine($"  animations : {candidates.Count} candidates");
        if (candidates.Count > 0)
        {
            Console.WriteLine("    " + string.Join(", ", candidates.Take(20)));
        }

        foreach (var (route, anims) in skin.AnimationsByRoute.Take(3))
        {
            Console.WriteLine($"    {route}: {string.Join(", ", anims.Take(10))}");
        }

        Console.WriteLine();
    }
}

return 0;
