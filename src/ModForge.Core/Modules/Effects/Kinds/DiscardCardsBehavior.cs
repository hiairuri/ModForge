using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 丢弃**选牌器挑出来的那些牌**。
///
/// 与"消耗牌"同构（都是"把挑出来的牌挪走"），区别在目的地与**语义**：
/// 丢弃必须走 <c>CardCmd.Discard</c>，不能直接把它挪进弃牌堆——直接 Add 只是
/// "换了个牌堆"，不会触发弃牌相关的钩子，于是"被弃掉时"的效果、以及一切监听
/// 弃牌的卡与状态都不会生效。
/// </summary>
internal sealed class DiscardCardsBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.DiscardCardsInPile;

    public void Emit(EffectEmit a)
    {
        string suffix = a.Emitter.NextPickSuffix("Discard");

        foreach (string line in a.Emitter.PickCardsLines(a.Effect, a.Ctx, a.Amount, suffix))
        {
            a.Line(line);
        }

        a.Line($"foreach (var discardTarget in pickedCards{suffix})");
        a.Line("{");
        a.Inner($"await CardCmd.Discard({a.Ctx.ChoiceContext}, discardTarget);");
        a.Line("}");
    }

    public string Describe(EffectDescribe a)
    {
        a.AddPileVars();
        return a.With("pick", a.PickLabel()).Render("discardCards");
    }

    // 选牌那一段（十步）由 schema 按策略生成（丢弃的策略在 CardPickPolicies.For 里）。
    // 数值不在这条效果上：挑几张是选牌器流程十自己那套（<see cref="CardPick.Count"/>）。
    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "丢弃牌堆中的牌", labelEn: "Discard cards from a pile",
        scope: TargetScope.Cards, amount: false);
}
