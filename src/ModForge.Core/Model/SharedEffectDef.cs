using System.Text.Json.Serialization;

namespace ModForge.Core.Model;

/// <summary>
/// A named, reusable effect.
///
/// Effects already appear in two places (a card's effect list and a power's turn
/// triggers), so the same behaviour is often wanted in several of them. A shared effect
/// gives that behaviour a name and one definition to edit.
///
/// It is referenced by stable id rather than by position, so reordering a card's effect
/// list can never silently repoint a reference at a different effect.
/// </summary>
public sealed class SharedEffectDef
{
    /// <summary>Stable identifier used by references. Never reused for a different effect.</summary>
    public string Id { get; set; } = "NEW_EFFECT";

    public string Name { get; set; } = "New Effect";

    /// <summary>Optional note for the author; not shown to players.</summary>
    public string Note { get; set; } = "";

    /// <summary>
    /// The behaviour this effect performs. Its amount, kind, value source and power
    /// reference are used wherever it is referenced.
    /// </summary>
    public EffectDef Effect { get; set; } = new();

    [JsonIgnore]
    public string Summary => $"{Name} — {Effect.Kind} {Effect.Amount}";
}
