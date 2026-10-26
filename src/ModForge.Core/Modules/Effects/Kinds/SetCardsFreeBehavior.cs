using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 让某个牌堆中若干张牌**可以免费打出**。
///
/// 走 <c>CardModel.SetToFreeThisTurn()</c> / <c>SetToFreeThisCombat()</c>——它们
/// **同时**把能量与星辰费用清成 0（这正是"免费"的语义，原版"子弹时间"用它）。
/// 所以这与单纯降费不是一回事，界面上分成两条效果（那条只碰能量）。
/// X 费牌没有固定费用可清，跳过（与降费一致，也是原版的做法）。
/// </summary>
internal sealed class SetCardsFreeBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.SetCardsFree;

    public void Emit(EffectEmit a)
    {
        string method = a.Effect.CostDuration == CostDuration.ThisCombat
            ? "SetToFreeThisCombat"
            : "SetToFreeThisTurn";

        string picked = a.Emitter.NextPlainPickSuffix();

        foreach (string line in a.Emitter.PickCardsLines(a.Effect, a.Ctx, a.Amount, picked))
        {
            a.Line(line);
        }

        a.Line($"foreach (var freed in pickedCards{picked})");
        a.Line("{");

        a.Inner("if (!freed.EnergyCost.CostsX)");
        a.Inner("{");
        a.Inner($"    freed.{method}();");
        a.Inner("}");
        a.Line("}");
    }

    /// <summary>
    /// 三种作用对象各用一条模板。不能共用一条，因为"免费"这句里 {pick}
    /// 出现在句首，英文的单复数与大小写都跟着变：
    ///   ALL cards in your hand cost 0…        复数动词、句首大写
    ///   every copy of Defend …                单数动词、句首大写
    ///   this card costs 0…                    单数动词
    /// 中文同样别扭："这张牌自己的费用"啰嗦，直接写"使所有{card}的费用变为 0"更顺。
    /// </summary>
    public string Describe(EffectDescribe a)
    {
        a.AddPileVars();

        string key = a.Effect.Pick switch
        {
            { Source: PickSource.ThisCard } => "cardsFreeThisCard",
            { IsNamedPick: true } => "cardsFreeNamedCard",
            _ => "cardsFree",
        };

        return a
            .With("pick", PickerWording.PickLabel(
                a.Project, a.Effect, a.Sentences, a.Names, sentenceStart: true))
            .With("card", DescriptionNames.CardNameForFilter(a.Project, a.Effect, a.Sentences))
            .With("duration", DescriptionWords.CostDurationLabel(a.Effect.CostDuration, a.Sentences))
            .Render(key);
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "可以免费打出", labelEn: "Make cards free to play",
        scope: TargetScope.Cards, amount: false,
        durations: new[] { CostDuration.ThisTurn, CostDuration.ThisCombat }
            .Select(d => d.ToString()).ToArray());
}
