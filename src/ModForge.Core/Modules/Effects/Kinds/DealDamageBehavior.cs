using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 造成伤害（<c>CreatureCmd.Damage</c>）。
///
/// <c>ValueProp.Move</c> 是"来自一次移动"那一档属性——攻击伤害都算它（原版"打击"等
/// 全部攻击牌如此）。它决定格挡能不能挡、以及"来自卡牌"相关的减免怎么算，写错不会编译失败，
/// 只会让伤害的结算口径与别的攻击牌不一致。
/// </summary>
internal sealed class DealDamageBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.DealDamage;

    public void Emit(EffectEmit a) =>
        a.AgainstTargets(target =>
            $"await CreatureCmd.Damage({a.Ctx.ChoiceContext}, {target}, {a.Amount}, ValueProp.Move, {a.Ctx.Dealer}, {a.Ctx.CardSource});");

    /// <summary>
    /// 伤害怎么念：给自己时不念目标（"造成 6 点伤害"），给别人时要念（"对目标造成 6 点伤害"）。
    ///
    /// 两种语言各有"造成"与"对…造成"两条模板；计算型另有两条（把数值换成"等于 {source}{plus}"）。
    /// </summary>
    public string Describe(EffectDescribe a)
    {
        a.AddCalculationVars();

        bool self = a.SelfSubject;

        return a.Render(a.Effect.IsCalculated
            ? (self ? "damageCalc" : "damageOtherCalc")
            : (self ? "damage" : "damageOther"));
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "伤害", labelEn: "Deal damage",
        scope: TargetScope.Creature, amount: true);
}
