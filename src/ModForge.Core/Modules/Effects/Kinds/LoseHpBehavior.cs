using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 失去生命。走的是 <c>CreatureCmd.Damage</c> 而不是某个"扣血"命令——游戏里扣血就是伤害，
/// 只是属性不同：
///
///   <c>ValueProp.Unblockable</c> —— 格挡挡不住（"失去生命"与"受到伤害"的区别就在这）。
///   来源给 <c>null</c> —— 这不是"某人造成的伤害"，所以不进"谁打了谁"那类统计
///   （原版"放血""献祭"自己的写法一致）。写成自己的话，会凭空多出一次"自己打自己"的记录。
/// </summary>
internal sealed class LoseHpBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.LoseHp;

    public void Emit(EffectEmit a) =>
        a.AgainstTargets(target =>
            $"await CreatureCmd.Damage({a.Ctx.ChoiceContext}, {target}, {a.Amount}, ValueProp.Unblockable, null, null);");

    public string Describe(EffectDescribe a)
    {
        a.AddCalculationVars();

        // 作用于自己是不念对象的（"失去 2 点生命。"）；作用于别人（含其奥斯提）时要念
        //（"使目标的奥斯提失去 2 点生命。"）——不念就读不出是谁在掉血。
        return a.Render(a.TargetedKey("loseHp", a.Effect.IsCalculated));
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "失去生命", labelEn: "Lose HP",
        scope: TargetScope.Creature, amount: true);
}
