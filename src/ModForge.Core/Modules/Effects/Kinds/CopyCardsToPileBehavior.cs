using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 把牌的若干**复制品**加入某个牌堆（原版"双持"那种"复制一张牌"）。
///
/// 对应 <c>CardPileCmd.AddGeneratedCardToCombat(card.CreateClone(), …)</c>：
/// 副本是**新生成的牌**，所以要走"生成牌加入战斗"这个入口（它会把生成记录写进战斗历史），
/// 而不是普通的 <c>Add</c>。
///
/// 复制谁由作用对象决定（复用 <see cref="EffectEmitter.PickCardsLines"/>）；
/// 数值是**每张**复制几份，所以两层循环：外层遍历挑到的牌，内层复制 N 份。
/// </summary>
internal sealed class CopyCardsToPileBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.CopyCardsToPile;

    public void Emit(EffectEmit a)
    {
        string picked = a.Emitter.NextPlainPickSuffix();

        foreach (string line in a.Emitter.PickCardsLines(a.Effect, a.Ctx, a.Amount, picked))
        {
            a.Line(line);
        }

        a.Line($"foreach (var original in pickedCards{picked})");
        a.Line("{");
        a.Inner($"for (int n = 0; n < {a.Amount}; n++)");
        a.Inner("{");
        a.Inner($"    await CardPileCmd.AddGeneratedCardToCombat(original.CreateClone(), PileType.{CardPickCollector.PileTypeName(a.Effect.TargetPile)}, {a.Ctx.Player}, CardPilePosition.{a.Effect.Placement});");
        a.Inner("}");
        a.Line("}");
    }

    // 句式："{pick} 的 {num} 份复制品加入{into}"。pick 复用费用那套选牌标签
    // （已涵盖某个牌堆 / 指定牌名 / 按条件 / 此牌四种），into 复用"置于"那套目的地。
    public string Describe(EffectDescribe a)
    {
        a.AddPileVars();

        return a
            .With("pick", a.PickLabel())
            .With("into", PickerWording.TargetPileLabel(a.Effect, a.Sentences))
            .Render(a.Effect.IsCalculated ? "copyCardsCalc" : "copyCards");
    }

    // 目的地是"加入哪个牌堆"（targetPileIsAdd），界面把那一格的标题念成"加入"而不是"移到"。
    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "复制牌并加入牌堆", labelEn: "Copy cards into a pile",
        scope: TargetScope.Cards, amount: true, needsTargetPile: true, targetPileIsAdd: true);
}
