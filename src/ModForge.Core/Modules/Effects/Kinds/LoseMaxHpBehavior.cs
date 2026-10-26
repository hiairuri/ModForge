using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 降低最大生命（<c>CreatureCmd.LoseMaxHp</c>）。
///
/// 与提高那一条不同，**这个要 PlayerChoiceContext**：当前生命高于新上限时，引擎从这段
/// "多出来的生命"里扣血，扣血要能询问（有格挡/减免之类的交互）。
///
/// <c>isFromCard: true</c> 影响那部分扣血的 <c>ValueProp</c>（算不算"来自卡牌"的伤害）。
/// 给 true 与原版"狂宴"一致：它就是把最大生命降下来再把多出来的部分吃掉。
/// </summary>
internal sealed class LoseMaxHpBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.LoseMaxHp;

    public void Emit(EffectEmit a) =>
        a.AgainstTargets(target =>
            $"await CreatureCmd.LoseMaxHp({a.Ctx.ChoiceContext}, {target}, {a.Amount}, isFromCard: true);");

    public string Describe(EffectDescribe a)
    {
        a.AddCalculationVars();

        return a.Render(a.TargetedKey("loseMaxHp", a.Effect.IsCalculated));
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "降低最大生命", labelEn: "Lose Max HP",
        scope: TargetScope.Creature, amount: true);
}
