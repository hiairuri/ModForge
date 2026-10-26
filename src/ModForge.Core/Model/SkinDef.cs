using System.Text.Json.Serialization;

namespace ModForge.Core.Model;

/// <summary>
/// A skin this mod ships. The editor reads an existing skin package and records
/// which of its animations to use for each gameplay slot; export copies the
/// package's assets into the generated mod and writes the binding table.
/// </summary>
public sealed class SkinDef
{
    /// <summary>Absolute path of the source skin pack (.pck).</summary>
    public string SourcePack { get; set; } = "";

    /// <summary>Display name, taken from the source mod's manifest when available.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Character or monster the skin applies to, e.g. "regent". Taken from the
    /// source pack's routes.
    /// </summary>
    public string Target { get; set; } = "";

    /// <summary>
    /// Animation bound to each gameplay slot, keyed by slot name
    /// (idle, attack, cast, hurt, die, entry).
    /// </summary>
    public Dictionary<string, string> SlotBindings { get; set; } = new();

    /// <summary>Animation names available in the source pack, cached for the UI.</summary>
    public List<string> AvailableAnimations { get; set; } = new();

    /// <summary>Routes the source pack provides, cached for display.</summary>
    public List<string> Routes { get; set; } = new();

    [JsonIgnore]
    public bool HasBindings => SlotBindings.Values.Any(v => !string.IsNullOrWhiteSpace(v));

    [JsonIgnore]
    public string Summary => string.IsNullOrWhiteSpace(Target)
        ? "no target"
        : $"{Target} · {SlotBindings.Count(b => !string.IsNullOrWhiteSpace(b.Value))} slot(s) bound";
}
