using System.Text.Json;
using System.Text.RegularExpressions;
using ModForge.Core.Pck;

namespace ModForge.Core.Skin;

/// <summary>What a skin pack replaces, in terms of the game's own asset routes.</summary>
public enum SkinTargetKind
{
    Character,

    /// <summary>Character-select portrait and its animations.</summary>
    CharacterSelect,

    /// <summary>Shop / merchant appearance.</summary>
    Merchant,

    /// <summary>Rest-site (campfire) appearance.</summary>
    RestSite,

    Monster,

    Background,

    /// <summary>Any other route; kept so nothing is silently dropped.</summary>
    Other,
}

/// <summary>
/// One logical skin asset, i.e. a game resource route the pack provides (possibly as
/// several files: source, imported binary, import stub).
/// </summary>
public sealed record SkinAsset(
    string Route,
    SkinTargetKind Kind,
    string Target,
    string ImportedPath,
    long Size);

/// <summary>
/// A skin package read from a mod's PCK.
///
/// A skin works by shadowing the game's own resource routes (for example
/// res://animations/characters/regent/regent.skel), so describing a skin means
/// classifying the routes it provides.
/// </summary>
public sealed class SkinPack
{
    public string SourcePath { get; }

    public string Name { get; }

    public IReadOnlyList<SkinAsset> Assets { get; }

    /// <summary>Animation candidates per skeleton route.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> AnimationsByRoute { get; }

    public SkinPack(
        string sourcePath,
        string name,
        IReadOnlyList<SkinAsset> assets,
        IReadOnlyDictionary<string, IReadOnlyList<string>> animations)
    {
        SourcePath = sourcePath;
        Name = name;
        Assets = assets;
        AnimationsByRoute = animations;
    }

    /// <summary>The characters or monsters this pack provides, e.g. ["regent"].</summary>
    public IReadOnlyList<string> Targets => Assets
        .Where(a => a.Kind != SkinTargetKind.Other)
        .Select(a => a.Target)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(t => t, StringComparer.Ordinal)
        .ToList();

    public bool HasKind(SkinTargetKind kind) => Assets.Any(a => a.Kind == kind);

    /// <summary>
    /// Distinct animation names across the pack, ordered so the conventional
    /// combat animations come first.
    /// </summary>
    public IReadOnlyList<string> AnimationCandidates => AnimationsByRoute.Values
        .SelectMany(v => v)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(s => LookLikeCore(s) ? 0 : 1)
        .ThenBy(s => s.Length)
        .ThenBy(s => s, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// The skeleton routes in this pack that belong to the character battle
    /// animation set, which is the one a user normally binds slots against.
    /// </summary>
    public IReadOnlyList<SkinAsset> BattleSkeletons => Assets
        .Where(a => a.Kind == SkinTargetKind.Character &&
                    a.Route.EndsWith(".skel", StringComparison.OrdinalIgnoreCase))
        .ToList();

    private static bool LookLikeCore(string name) =>
        Spine.SpineSkeletonReader.CoreSlotNames.Contains(name.ToLowerInvariant());
}

/// <summary>
/// Reads skins out of a mod PCK.
///
/// Godot stores imported assets under an opaque hashed name in .godot/imported/,
/// while the source route lives in the neighbouring *.import stub. The stub is
/// therefore the authoritative map from a game route to the bytes that implement
/// it, and this reader is built on that mapping.
/// </summary>
public static partial class SkinPackReader
{
    [GeneratedRegex(
        @"^res://animations/(characters|character_select|merchant|rest_site|monsters|backgrounds)/([^/]+)/",
        RegexOptions.IgnoreCase)]
    private static partial Regex RouteRegex();

    [GeneratedRegex(@"^path\s*=\s*""([^""]+)""", RegexOptions.Multiline)]
    private static partial Regex RemapPathRegex();

    public static SkinPack Read(string pckPath)
    {
        // Only the directory plus a few small files are needed: import stubs (tiny) and
        // skeleton data. Reading whole payloads would mean loading hundreds of megabytes
        // per pack just to list what a skin provides.
        IReadOnlyList<PckFile.EntryInfo> directory = PckFile.ReadDirectory(pckPath);

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (PckFile.EntryInfo entry in directory)
        {
            bool needed =
                entry.Path.EndsWith(".import", StringComparison.OrdinalIgnoreCase) ||
                entry.Path.EndsWith(".spskel", StringComparison.OrdinalIgnoreCase) ||
                entry.Path.EndsWith(".skel", StringComparison.OrdinalIgnoreCase);

            // Skeletons can be a few hundred KB; anything larger is not something this
            // reader inspects, and is reported by size only.
            if (!needed || entry.Size > 8 * 1024 * 1024)
            {
                continue;
            }

            entries[entry.Path] = PckFile.ReadEntry(pckPath, entry);
        }

        return Build(pckPath, Path.GetFileNameWithoutExtension(pckPath), entries, directory);
    }

    public static SkinPack Build(string sourcePath, string name, IReadOnlyDictionary<string, byte[]> entries) =>
        Build(
            sourcePath,
            name,
            entries,
            entries.Select(kv => new PckFile.EntryInfo(kv.Key, 0, kv.Value.Length)).ToList());

    public static SkinPack Build(
        string sourcePath,
        string name,
        IReadOnlyDictionary<string, byte[]> entries,
        IReadOnlyList<PckFile.EntryInfo> directory)
    {
        var assets = new List<SkinAsset>();
        var animations = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        // Route -> imported payload, learned from the *.import stubs.
        var routeToImported = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach ((string path, byte[] data) in entries)
        {
            if (!path.EndsWith(".import", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string stub = System.Text.Encoding.UTF8.GetString(data);

            // The stub's own name is the route, minus the .import suffix. (Some
            // exporters also emit source_file, but it is not always present, so the
            // filename is the reliable source.)
            string route = path[..^".import".Length];

            Match remap = RemapPathRegex().Match(stub);
            if (!remap.Success)
            {
                continue;
            }

            routeToImported[route] = remap.Groups[1].Value;
        }

        foreach ((string route, string imported) in routeToImported)
        {
            // Payloads are only loaded for the files this reader inspects, so a large
            // pack is described from its directory table alone.
            entries.TryGetValue(imported, out byte[]? payload);
            long size = payload?.Length
                ?? directory.FirstOrDefault(e => e.Path == imported)?.Size
                ?? 0;

            (SkinTargetKind kind, string target) = Classify(route);
            assets.Add(new SkinAsset(route, kind, target, imported, size));

            if (payload is not null && IsSkeleton(route))
            {
                Spine.SpineSkeletonReader.Result read = Spine.SpineSkeletonReader.Read(payload);
                if (read.AnimationCandidates.Count > 0)
                {
                    animations[route] = read.AnimationCandidates;
                }
            }
        }

        // Assets without an import stub (plain images, scripts, localization) still
        // belong to the pack; record them so a pack is never silently under-reported.
        foreach (PckFile.EntryInfo entry in directory)
        {
            string path = entry.Path;

            if (path.EndsWith(".import", StringComparison.OrdinalIgnoreCase) ||
                routeToImported.ContainsValue(path) ||
                path.Contains("/.godot/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            (SkinTargetKind kind, string target) = Classify(path);
            assets.Add(new SkinAsset(path, kind, target, path, entry.Size));
        }

        assets.Sort((a, b) => string.CompareOrdinal(a.Route, b.Route));

        return new SkinPack(sourcePath, name, assets, animations);
    }

    private static bool IsSkeleton(string route) =>
        route.EndsWith(".skel", StringComparison.OrdinalIgnoreCase) ||
        route.EndsWith(".atlas", StringComparison.OrdinalIgnoreCase);

    private static (SkinTargetKind Kind, string Target) Classify(string path)
    {
        Match m = RouteRegex().Match(path);
        if (!m.Success)
        {
            return (SkinTargetKind.Other, "(other)");
        }

        string bucket = m.Groups[1].Value.ToLowerInvariant();
        string target = m.Groups[2].Value;

        SkinTargetKind kind = bucket switch
        {
            "characters" => SkinTargetKind.Character,
            "character_select" => SkinTargetKind.CharacterSelect,
            "merchant" => SkinTargetKind.Merchant,
            "rest_site" => SkinTargetKind.RestSite,
            "monsters" => SkinTargetKind.Monster,
            "backgrounds" => SkinTargetKind.Background,
            _ => SkinTargetKind.Other,
        };

        return (kind, target);
    }

    /// <summary>Reads a skin mod's manifest for display name and id.</summary>
    public static (string? Name, string? Id) ReadManifest(string modDirectory)
    {
        try
        {
            string? manifest = Directory
                .EnumerateFiles(modDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

            if (manifest is null)
            {
                return (null, null);
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifest));
            JsonElement root = doc.RootElement;

            string? name = root.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
            string? id = root.TryGetProperty("id", out JsonElement i) ? i.GetString() : null;
            return (name, id);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }
}
