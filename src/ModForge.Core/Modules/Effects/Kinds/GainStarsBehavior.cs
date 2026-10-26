using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 获得星辰（<c>PlayerCmd.GainStars</c>）。
///
/// 与获得能量同一条路：命令收 <c>Player</c>，所以作用域是玩家轴（见 <see cref="TargetScope.Player"/>）。
/// </summary>
internal sealed class GainStarsBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.GainStars;

    public void Emit(EffectEmit a) =>
        a.AgainstTargets(player => $"await PlayerCmd.GainStars({a.Amount}, {player});");

    public string Describe(EffectDescribe a)
    {
        a.AddCalculationVars();

        return a.Render(a.TargetedKey("stars", a.Effect.IsCalculated));
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "获得星辰", labelEn: "Gain stars",
        scope: TargetScope.Player, amount: true);
}
