using System.Reflection;
using System.Runtime.InteropServices;
using ModForge.Core.Pck;

namespace ModForge.Core.Catalog;

/// <summary>One piece of vanilla content, with display names resolved.</summary>
public sealed record CatalogEntry(
    string Category,
    string Entry,
    string FullTypeName,
    string? NameEn,
    string? NameZh);

/// <summary>
/// Builds the vanilla content catalog fully offline.
///
/// Types come from the game assembly via metadata-only reflection (no code is
/// executed), and display names come from the localization tables inside the
/// game's PCK.
/// </summary>
public sealed class VanillaCatalog
{
    private static readonly Dictionary<string, string> CategoryToLocTable = new(StringComparer.Ordinal)
    {
        ["CARD"] = "cards",
        ["POWER"] = "powers",
        ["RELIC"] = "relics",
        ["POTION"] = "potions",
        ["ORB"] = "orbs",
        ["CHARACTER"] = "characters",
        ["ENCHANTMENT"] = "enchantments",
        ["AFFLICTION"] = "afflictions",
        ["MONSTER"] = "monsters",
        ["EVENT"] = "events",
    };

    public IReadOnlyList<CatalogEntry> Entries { get; }

    public string GameVersion { get; }

    private VanillaCatalog(IReadOnlyList<CatalogEntry> entries, string gameVersion)
    {
        Entries = entries;
        GameVersion = gameVersion;
    }

    public IEnumerable<CatalogEntry> ByCategory(string category) =>
        Entries.Where(e => e.Category == category);

    public static VanillaCatalog Build(string gameDir, string? pckPath = null)
    {
        string assemblyDir = Path.Combine(gameDir, "data_sts2_windows_x86_64");
        if (!Directory.Exists(assemblyDir))
        {
            assemblyDir = gameDir;
        }

        string sts2Path = Path.Combine(assemblyDir, "sts2.dll");
        if (!File.Exists(sts2Path))
        {
            throw new FileNotFoundException($"Game assembly not found at {sts2Path}");
        }

        List<(string Category, string Entry, string FullTypeName)> types = LoadModelTypes(assemblyDir, sts2Path);

        Dictionary<string, string> en = new(StringComparer.Ordinal);
        Dictionary<string, string> zh = new(StringComparer.Ordinal);
        string resolvedPck = pckPath ?? Path.Combine(gameDir, "SlayTheSpire2.pck");
        if (File.Exists(resolvedPck))
        {
            var reader = new PckLocReader(resolvedPck);
            foreach (string table in CategoryToLocTable.Values.Distinct())
            {
                foreach ((string k, string v) in reader.LoadTable("eng", table))
                {
                    en[k] = v;
                }
            }

            foreach (string table in CategoryToLocTable.Values.Distinct())
            {
                foreach ((string k, string v) in reader.LoadTable("zhs", table))
                {
                    zh[k] = v;
                }
            }
        }

        var entries = new List<CatalogEntry>(types.Count);
        foreach ((string category, string entry, string fullTypeName) in types)
        {
            if (IsNoise(category, entry, fullTypeName))
            {
                continue;
            }

            string? nameEn = LookupName(entry, category, en);
            string? nameZh = LookupName(entry, category, zh);
            entries.Add(new CatalogEntry(category, entry, fullTypeName, nameEn, nameZh));
        }

        entries.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.Category, b.Category);
            return c != 0 ? c : string.CompareOrdinal(a.Entry, b.Entry);
        });

        return new VanillaCatalog(entries, ReadGameVersion(gameDir));
    }

    /// <summary>
    /// Resolves a display name. Monster tables use ".name" rather than ".title",
    /// and some powers are card-derived so their title lives in the cards table.
    /// </summary>
    private static string? LookupName(
        string entry,
        string category,
        IReadOnlyDictionary<string, string> table)
    {
        if (category == "MONSTER" && table.TryGetValue($"{entry}.name", out string? monsterName))
        {
            return monsterName;
        }

        return table.TryGetValue($"{entry}.title", out string? title) ? title : null;
    }

    private static bool IsNoise(string category, string entry, string fullTypeName)
    {
        if (fullTypeName.Contains(".Mocks.", StringComparison.Ordinal) ||
            fullTypeName.Contains("Mock", StringComparison.Ordinal))
        {
            return true;
        }

        return entry.StartsWith("MOCK_", StringComparison.Ordinal)
            || entry.StartsWith("DEPRECATED_", StringComparison.Ordinal)
            || entry.EndsWith(".MODDED", StringComparison.Ordinal)
            || entry == "MODDED";
    }

    private static string CategoryOf(Type type)
    {
        Type? t = type;
        while (t.BaseType is not null && t.BaseType.Name != "AbstractModel")
        {
            t = t.BaseType;
        }

        return ModelIds.SlugifyCategory(t.Name);
    }

    private static List<(string Category, string Entry, string FullTypeName)> LoadModelTypes(
        string assemblyDir,
        string sts2Path)
    {
        // Types are materialized into plain strings while the context is alive,
        // because Type objects are only valid for the lifetime of their context.
        using var mlc = CreateLoadContext(assemblyDir);
        Assembly asm = mlc.LoadFromAssemblyPath(sts2Path);

        Type abstractModel = asm.GetType("MegaCrit.Sts2.Core.Models.AbstractModel")
            ?? throw new InvalidOperationException("AbstractModel not found in sts2.dll");

        var result = new List<(string, string, string)>();
        foreach (Type type in asm.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
            {
                continue;
            }

            if (!IsSubclassOf(type, abstractModel))
            {
                continue;
            }

            result.Add((
                CategoryOf(type),
                ModelIds.Slugify(type.Name),
                type.FullName ?? type.Name));
        }

        return result;
    }

    /// <summary>
    /// Builds a metadata-only load context over the game's own runtime.
    /// The game is self-contained, so its folder already holds the full .NET runtime
    /// that sts2.dll was compiled against; mixing in the host runtime would load
    /// System.Private.CoreLib twice.
    /// </summary>
    internal static MetadataLoadContext CreateLoadContext(string assemblyDir)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string dll in Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
        {
            byName[Path.GetFileName(dll)] = dll;
        }

        // Game-provided assemblies win over the host runtime.
        foreach (string dll in Directory.GetFiles(assemblyDir, "*.dll"))
        {
            byName[Path.GetFileName(dll)] = dll;
        }

        var resolver = new PathAssemblyResolver(byName.Values);
        return new MetadataLoadContext(resolver, "System.Private.CoreLib");
    }

    private static bool IsSubclassOf(Type type, Type baseType)
    {
        for (Type? t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.FullName == baseType.FullName)
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadGameVersion(string gameDir)
    {
        string infoPath = Path.Combine(gameDir, "release_info.json");
        if (!File.Exists(infoPath))
        {
            return "unknown";
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(infoPath));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "unknown" : "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }
}
