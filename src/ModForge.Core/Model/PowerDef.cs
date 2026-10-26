using System.Text.Json.Serialization;

namespace ModForge.Core.Model;

/// <summary>Matches the game's PowerType.</summary>
public enum PowerKind { Buff, Debuff }

/// <summary>Matches the game's PowerStackType.</summary>
public enum PowerStack
{
    /// <summary>Amount is visible and accumulates.</summary>
    Counter,

    /// <summary>Amount is hidden and always 1.</summary>
    Single,
}

/// <summary>When a power's effect fires.</summary>
public enum PowerTrigger
{
    /// <summary>At the start of the owner's turn.</summary>
    TurnStart,

    /// <summary>At the end of the owner's turn.</summary>
    TurnEnd,

    /// <summary>When the owner plays any card.</summary>
    CardPlayed,

    /// <summary>When the owner takes damage.</summary>
    DamageTaken,
}

/// <summary>How a power modifies outgoing damage.</summary>
public enum DamageModifier
{
    None,

    /// <summary>Adds Amount to each attack.</summary>
    Additive,

    /// <summary>Multiplies damage; Amount is a percentage, e.g. 50 means x1.5.</summary>
    Multiplicative,
}

/// <summary>How the power loses stacks over time.</summary>
public enum DecayMode
{
    Never,

    /// <summary>Lose one stack at the end of each of the owner's turns.</summary>
    OnePerTurnEnd,

    /// <summary>Lose all stacks at the end of the owner's turn.</summary>
    AllAtTurnEnd,
}

/// <summary>
/// A real buff or debuff. This produces a genuine PowerModel subclass, so it
/// appears in the power bar, stacks, decays, and interacts with other content the
/// same way a vanilla power does.
/// </summary>
public sealed class PowerDef
{
    /// <summary>Identifier used for the class name, e.g. FORGE_MIGHT -> ForgeMightPower.</summary>
    public string Id { get; set; } = "NEW_POWER";

    /// <summary>状态名，按语言分别填写。未填的语言由游戏回退到英语。</summary>
    public LocalizedText Name { get; set; } = new(GameLanguages.Fallback, "New Power");

    /// <summary>
    /// 说明文本，按语言分别填写。{Amount} 自动可用，显示当前层数；其它数值按常规语法引用。
    /// </summary>
    public LocalizedText Description { get; set; } =
        new(GameLanguages.Fallback, "Your attacks deal {Amount} additional damage.");

    public PowerKind Kind { get; set; } = PowerKind.Buff;

    public PowerStack Stack { get; set; } = PowerStack.Counter;

    /// <summary>Modifies damage dealt by the owner.</summary>
    public DamageModifier DamageModifier { get; set; } = DamageModifier.None;

    /// <summary>
    /// 伤害增益只作用于哪些牌。仅在 <see cref="DamageModifier"/> 为
    /// <see cref="DamageModifier.Additive"/> 时有意义：原版就是按标签判断的，
    /// 例如"所有打击牌额外造成 N 点伤害"。
    /// </summary>
    public CardFilterKind CardFilter { get; set; } = CardFilterKind.Any;

    /// <summary>
    /// <see cref="CardFilterKind.SpecificCard"/> 时作用的那张牌（模型 id）。
    /// </summary>
    public string? FilterCardId { get; set; }

    /// <summary>How the power loses stacks.</summary>
    public DecayMode Decay { get; set; } = DecayMode.Never;

    /// <summary>
    /// Effects run on each trigger. An effect whose target is Self acts on the power
    /// owner; Target and enemy targets resolve against the power owner's enemies.
    /// </summary>
    public List<TriggeredEffect> Triggers { get; set; } = new();

    /// <summary>Optional icon file placed next to the mod, loaded at runtime.</summary>
    public string? IconFile { get; set; }

    /// <summary>The generated C# type name, e.g. ForgeMightPower.</summary>
    [JsonIgnore]
    public string TypeName => Naming.ToPascal(Id) + "Power";

    /// <summary>Literal summary for the editor UI.</summary>
    [JsonIgnore]
    public string Summary
    {
        get
        {
            var parts = new List<string> { Kind == PowerKind.Debuff ? "Debuff" : "Buff" };
            if (DamageModifier != DamageModifier.None)
            {
                parts.Add(DamageModifier == DamageModifier.Additive
                    ? "+Amount damage"
                    : "x(1+Amount%) damage");
            }

            foreach (TriggeredEffect t in Triggers)
            {
                // 这里只需要一个粗略摘要，具体句子由描述模块负责。
                parts.Add($"{t.When}: {t.Effect.Kind}");
            }

            if (Decay != DecayMode.Never)
            {
                parts.Add(Decay == DecayMode.OnePerTurnEnd ? "lose 1/turn" : "expires at turn end");
            }

            return string.Join("; ", parts);
        }
    }
}

/// <summary>An effect bound to a specific trigger.</summary>
public sealed class TriggeredEffect
{
    public PowerTrigger When { get; set; } = PowerTrigger.TurnStart;

    public EffectDef Effect { get; set; } = new();
}

/// <summary>Identifier helpers shared by the generator and the UI.</summary>
public static class Naming
{
    /// <summary>Converts FORGE_MIGHT or forge might into ForgeMight.</summary>
    public static string ToPascal(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "Unnamed";
        }

        var parts = id
            .Split(new[] { '_', ' ', '-', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant());

        string result = string.Concat(parts);

        // A C# identifier cannot start with a digit.
        return result.Length > 0 && char.IsDigit(result[0]) ? "X" + result : result;
    }

    /// <summary>Normalizes an id to UPPER_SNAKE form.</summary>
    public static string ToEntry(string id)
    {
        var chars = new List<char>();
        foreach (char c in id.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                chars.Add(char.ToUpperInvariant(c));
            }
            else if (chars.Count > 0 && chars[^1] != '_')
            {
                chars.Add('_');
            }
        }

        return new string(chars.ToArray()).Trim('_');
    }
}
