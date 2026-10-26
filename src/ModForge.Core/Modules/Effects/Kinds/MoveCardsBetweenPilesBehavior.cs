using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 把牌移动到另一个牌堆。
///
/// 对应 <c>CardPileCmd.Add(card, 目标牌堆, 位置)</c>。原版"头槌"（弃牌堆→抽牌堆顶）、
/// "万事通"（弃牌堆→手牌）、"深挖"（此牌→抽牌堆顶）都是它。
///
/// **"移动哪些牌"由作用对象决定**（见 <see cref="EffectEmitter.PickCardsLines"/>）：
/// 某个牌堆 / 指定牌名 / 按条件 / 此牌。"此牌"那一档挑出来的就是这一个实例
/// （<c>{ this }</c>），所以原来那条独立的"将此牌置于牌堆"与它完全同构，已合并到这里。
/// </summary>
internal sealed class MoveCardsBetweenPilesBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.MoveCardsBetweenPiles;

    public void Emit(EffectEmit a)
    {
        string picked = a.Emitter.NextPlainPickSuffix();

        foreach (string line in a.Emitter.PickCardsLines(a.Effect, a.Ctx, a.Amount, picked))
        {
            a.Line(line);
        }

        a.Line($"foreach (var target in pickedCards{picked})");
        a.Line("{");
        a.Inner($"await CardPileCmd.Add(target, PileType.{CardPickCollector.PileTypeName(a.Effect.TargetPile)}, CardPilePosition.{a.Effect.Placement});");
        a.Line("}");
    }

    /// <summary>
    /// 移动"此牌"说成"将这张牌置于{目的地}"（目的地带顶/底："放回抽牌堆顶"），
    /// 其余走通用句（"{pick} 移到{into}"）。
    /// </summary>
    public string Describe(EffectDescribe a)
    {
        a.AddPileVars();

        if (a.Effect.Pick.Source == PickSource.ThisCard)
        {
            return a
                .With("where", PickerWording.TargetPileLabel(a.Effect, a.Sentences))
                .Render("moveThisCard");
        }

        return a
            .With("pick", a.PickLabel())
            .With("into", PickerWording.TargetPileLabel(a.Effect, a.Sentences))
            .Render("moveCardsBetween");
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "在牌堆间移动牌", labelEn: "Move cards between piles",
        scope: TargetScope.Cards, amount: false, needsTargetPile: true);
}
