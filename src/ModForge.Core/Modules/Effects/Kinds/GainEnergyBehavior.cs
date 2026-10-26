using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 获得能量（<c>PlayerCmd.GainEnergy</c>）。
///
/// 命令收的是 <c>Player</c>（不是 Creature），所以作用域是**玩家轴**：能选自己、打出时选定的
/// 目标、所有友方、随机一名友方。敌人身上没有 Player，所以"所有敌人"那几档这个作用域里
/// 根本不给——给了就等于发一段永远不成立的代码（见 <see cref="TargetScope.Player"/>）。
/// </summary>
internal sealed class GainEnergyBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.GainEnergy;

    public void Emit(EffectEmit a) =>
        a.AgainstTargets(player => $"await PlayerCmd.GainEnergy({a.Amount}, {player});");

    public string Describe(EffectDescribe a)
    {
        // 计算型要多带来源与基础值（"获得 等于手牌数 的能量"），固定值只用 num。
        a.AddCalculationVars();

        return a.Render(a.TargetedKey("energy", a.Effect.IsCalculated));
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "获得能量", labelEn: "Gain energy",
        scope: TargetScope.Player, amount: true);
}
