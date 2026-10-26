using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 施加状态（<c>PowerCmd.Apply</c>）。数值就是**层数**，走变量槽
/// （固定值时基名是那个状态类型名——<c>PowerVar&lt;T&gt;</c> 默认名就是它，
/// 所以卡面能写 <c>{StrengthPower:diff()}</c>，悬停说明也认这个变量）。
///
/// 目标的写法与伤害那一族完全一样（都由目标模块按作用域展开，见 <see cref="TargetScope.Creature"/>），
/// 所以这一族都声明同一个作用域，候选自然一致。
///
/// <c>applier</c> 给"谁施加的"、<c>cardSource</c> 给"来自哪张牌"——两者都会进战斗历史，
/// 也会被状态自己的钩子读（例如"施加毒时层数翻倍"那类遗物）。
/// </summary>
internal sealed class ApplyPowerBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.ApplyPower;

    public void Emit(EffectEmit a)
    {
        string powerType = a.Emitter.PowerModelTypeRef(a.Effect.PowerId!);

        a.AgainstTargets(target =>
            $"await PowerCmd.Apply<{powerType}>({a.Ctx.ChoiceContext}, {target}, {a.Amount}, "
            + $"{a.Ctx.Dealer}, {a.Ctx.CardSource});");
    }

    public string Describe(EffectDescribe a)
    {
        a.With("name", DescriptionNames.PowerName(a.Project, a.Effect.PowerId, a.Sentences.Language));
        a.AddCalculationVars();

        string key = (a.SelfSubject, a.Effect.IsCalculated) switch
        {
            (true, false) => "applyPower",
            (true, true) => "applyPowerCalc",
            (false, false) => "applyPowerOther",
            (false, true) => "applyPowerOtherCalc",
        };

        return a.Render(key);
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "施加状态", labelEn: "Apply power",
        scope: TargetScope.Creature, amount: true, needsPower: true);
}
