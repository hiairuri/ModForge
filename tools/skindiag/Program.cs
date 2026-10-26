using ModForge.Core.Pck;
using ModForge.Core.Skin;

// Diagnoses why packs are or are not recognised as skins.
// Usage: skindiag <rootDir>

string root = args.Length > 0
    ? args[0]
    : @"D:\Program Files (x86)\Steam\steamapps\workshop\content\2868840";

foreach (string pack in Directory.EnumerateFiles(root, "*.pck", SearchOption.AllDirectories))
{
    string label = $"{new DirectoryInfo(Path.GetDirectoryName(pack)!).Name}/{Path.GetFileName(pack)}";
    try
    {
        SkinPack skin = SkinPackReader.Read(pack);
        bool isSkin = skin.Assets.Any(a => a.Kind != SkinTargetKind.Other) ||
                      skin.AnimationCandidates.Count > 0;

        Console.WriteLine(
            $"{(isSkin ? "SKIN " : "skip ")} {label,-52} assets={skin.Assets.Count,5} " +
            $"routes={skin.Targets.Count} anims={skin.AnimationCandidates.Count,4} " +
            $"{(skin.Targets.Count > 0 ? string.Join(",", skin.Targets.Take(3)) : "")}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR {label,-52} {ex.GetType().Name}: {ex.Message}");
    }
}
