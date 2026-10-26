using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 降低或提高某个牌堆中若干张牌的费用。
///
/// 走 <c>CardEnergyCost.Add*</c> 一族（原版"子弹时间""开悟"用的就是它）。按持续范围
/// 选具体方法；数值为正即提高、为负即降低（这是它跳过"数值 ≤ 0 就没用"校验的原因）。
///
/// 两个细节：
///
/// * **只碰能量**，不动星辰。游戏里"降费"和"免费"是两套接口，后者会连星辰一起清 0；
///   所以界面与措辞都把两者分开（见 <see cref="EffectKind.SetCardsFree"/>）。
/// * X 费牌没有固定费用，跳过（原版"子弹时间"也是这么做的）。
/// </summary>
internal sealed class ModifyCardCostBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.ModifyCardCost;

    public void Emit(EffectEmit a)
    {
        string method = a.Effect.CostDuration switch
        {
            // "本回合"在游戏里就是"本回合结束或直到打出"——原版所有写"本回合"的降费都是这个。
            CostDuration.ThisCombat => "AddThisCombat",
            CostDuration.UntilPlayed => "AddUntilPlayed",
            _ => "AddThisTurnOrUntilPlayed",
        };

        a.Line($"int costDelta = (int){a.Amount};");

        string picked = a.Emitter.NextPlainPickSuffix();

        foreach (string line in a.Emitter.PickCardsLines(a.Effect, a.Ctx, a.Amount, picked))
        {
            a.Line(line);
        }

        a.Line($"foreach (var costed in pickedCards{picked})");
        a.Line("{");

        // X 费牌没有固定费用，跳过（原版"子弹时间"也是这么做的）。
        a.Inner("if (!costed.EnergyCost.CostsX)");
        a.Inner("{");
        a.Inner($"    costed.EnergyCost.{method}(costDelta, costDelta < 0);");
        a.Inner("}");
        a.Line("}");
    }

    // 句子按方向分两条（"降低/提高"），数字是**绝对值**——方向已经说在动词里了。
    public string Describe(EffectDescribe a)
    {
        a.AddPileVars();

        return a
            .With("num", Math.Abs(a.Effect.Amount).ToString())
            .With("pick", a.PickLabel())
            .With("duration", DescriptionWords.CostDurationLabel(a.Effect.CostDuration, a.Sentences))
            .Render(a.Effect.Amount < 0 ? "costReduce" : "costRaise");
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "降低/提高牌的费用", labelEn: "Reduce or raise card costs",
        scope: TargetScope.Cards, amount: true,
        durations: new[] { CostDuration.ThisTurn, CostDuration.ThisCombat, CostDuration.UntilPlayed }
            .Select(d => d.ToString()).ToArray());
}
