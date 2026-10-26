using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 回复生命（<c>CreatureCmd.Heal</c>）。
///
/// 与伤害、格挡共用"作用于目标"那套：可以回自己，也可以回队友（原版有回队友的牌）。
/// 注意它**不是**"失去生命"的反向写法——两者是不同的命令，扣血那条走
/// <see cref="LoseHpBehavior"/>。
/// </summary>
internal sealed class HealBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.Heal;

    public void Emit(EffectEmit a) =>
        a.AgainstTargets(target => $"await CreatureCmd.Heal({target}, {a.Amount});");

    public string Describe(EffectDescribe a)
    {
        a.AddCalculationVars();

        bool self = a.SelfSubject;

        return a.Render(a.Effect.IsCalculated
            ? (self ? "healCalc" : "healOtherCalc")
            : (self ? "heal" : "healOther"));
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "回复生命", labelEn: "Heal",
        scope: TargetScope.Creature, amount: true);
}
