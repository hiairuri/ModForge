using System.Reflection;

namespace ModForge.Core.Generate;

/// <summary>
/// Provides the runtime sources that are emitted into generated mods.
///
/// These files live under Generate/Runtime and are embedded rather than compiled
/// into this project, because they reference game types that only exist inside the
/// game. Keeping them as real source files means they get syntax highlighting and
/// can be reviewed normally.
/// </summary>
public static class EmbeddedRuntime
{
    private const string Prefix = "ModForge.Core.Generate.Runtime.";

    private static readonly Lazy<string?> AnimationRemap =
        new(() => Read("AnimationRemap.cs"));

    private static readonly Lazy<string?> DamageBoost =
        new(() => Read("DamageBoost.cs"));

    private static readonly Lazy<string?> SlotVar =
        new(() => Read("SlotVar.cs"));

    private static readonly Lazy<string?> HealTracker =
        new(() => Read("HealTracker.cs"));

    private static readonly Lazy<string?> DamageSlots =
        new(() => Read("DamageSlots.cs"));

    private static readonly Lazy<string?> ReplayBoost =
        new(() => Read("ReplayBoost.cs"));

    private static readonly Lazy<string?> SharedRuntime =
        new(() => Read("SharedRuntime.cs"));

    /// <summary>临时牌集合（一次结算里把一组牌传给同一张牌的其它效果）。</summary>
    private static readonly Lazy<string?> TempCardSets =
        new(() => Read("TempCardSets.cs"));

    /// <summary>原版卡的永久组件（组件仓库 + 进战斗应用）。</summary>
    private static readonly Lazy<string?> CardComponents =
        new(() => Read("CardComponents.cs"));

    /// <summary>组件的存档两头挂点（Harmony 补丁）。</summary>
    private static readonly Lazy<string?> SaveComponentsPatch =
        new(() => Read("SaveComponentsPatch.cs"));

    /// <summary>Source for the animation remapper, or null if it was not embedded.</summary>
    public static string? AnimationRemapSource() => AnimationRemap.Value;

    /// <summary>Source for the persistent damage-boost helper, or null if it was not embedded.</summary>
    public static string? DamageBoostSource() => DamageBoost.Value;

    /// <summary>
    /// Source for the "second calculated value on one card" helper,
    /// or null if it was not embedded.
    /// </summary>
    public static string? SlotVarSource() => SlotVar.Value;

    /// <summary>Source for the self-made heal counter (global tracker), or null if not embedded.</summary>
    public static string? HealTrackerSource() => HealTracker.Value;

    /// <summary>
    /// Source for the "add damage to every damage slot of a card" helper,
    /// or null if it was not embedded.
    /// </summary>
    public static string? DamageSlotsSource() => DamageSlots.Value;

    /// <summary>
    /// Source for the "permanently add replay count" helper,
    /// or null if it was not embedded.
    /// </summary>
    public static string? ReplayBoostSource() => ReplayBoost.Value;

    /// <summary>
    /// 共享运行时的"宿主 / 转发"判定（<c>SharedRuntime</c>），随计数一起发出——
    /// 只有带状态的运行时件才需要它（见 <c>SharedRuntime.cs</c> 的说明）。
    /// </summary>
    public static string? SharedRuntimeSource() => SharedRuntime.Value;

    /// <summary>临时牌集合那个运行时件（工程里用到才发）。</summary>
    public static string? TempCardSetsSource() => TempCardSets.Value;

    /// <summary>原版卡的永久组件运行时件（工程里用到永久增伤/重放才发）。</summary>
    public static string? CardComponentsSource() => CardComponents.Value;

    /// <summary>组件的存档挂点补丁（与组件仓库一起发出）。</summary>
    public static string? SaveComponentsPatchSource() => SaveComponentsPatch.Value;

    private static string? Read(string fileName)
    {
        Assembly assembly = typeof(EmbeddedRuntime).Assembly;

        string? name = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.Ordinal));

        if (name is null)
        {
            return null;
        }

        using Stream? stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
