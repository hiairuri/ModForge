using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 把某个牌堆里的若干张牌变化成别的牌。
///
/// 两种变化走的是两个不同的入口，所以生成时按"变成什么"分支：
///   指定牌   —— <c>CardCmd.TransformTo&lt;T&gt;</c>，T 由卡牌 id 解析成类型
///   随机     —— <c>CardCmd.TransformToRandom</c>，用游戏自己的变化池
///               （同稀有度、排除自己、战斗中还要能战斗中生成）。
///               我们不复刻那个池子，直接调游戏接口，池子随游戏版本走。
///
/// 不可变化的牌（例如带"永恒"）由游戏的 <c>AssertTransformable</c> 拒绝，
/// 所以挑牌时用 <c>IsTransformable</c> 过滤掉（策略的 <c>CardConstraintKind.Transformable</c>），
/// 免得中途抛异常打断整条效果。
/// </summary>
internal sealed class TransformCardsInPileBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.TransformCardsInPile;

    public void Emit(EffectEmit a)
    {
        // 变化会把原牌替换掉，不可变化的牌必须排除，否则游戏会抛异常。
        string picked = a.Emitter.NextPlainPickSuffix();

        foreach (string line in a.Emitter.PickCardsLines(a.Effect, a.Ctx, a.Amount, picked))
        {
            a.Line(line);
        }

        bool specific = a.Effect.TransformInto == TransformInto.SpecificCard &&
                        !string.IsNullOrWhiteSpace(a.Effect.CardId);

        if (specific)
        {
            string typeRef = a.Emitter.CardModelTypeRef(a.Effect.CardId!);
            a.Line($"foreach (var target in pickedCards{picked})");
            a.Line("{");

            // 指定牌带"升级版"时，变化完再把它升一级（没有可升的就跳过）。
            // 这与原版"冲锋！！"一致：它先 TransformTo<MinionDiveBomb>，再对结果调 Upgrade。
            a.Inner($"var added = await CardCmd.TransformTo<{typeRef}>(target);");

            if (a.Effect.Upgraded)
            {
                a.Inner("if (added.HasValue)");
                a.Inner("{");
                a.Inner("    CardCmd.Upgrade(added.Value.cardAdded);");
                a.Inner("}");
            }

            a.Line("}");
            return;
        }

        // 没指定牌（或者选了"指定"却没填 id）时按随机处理，并说明清楚。
        if (a.Effect.TransformInto == TransformInto.SpecificCard)
        {
            a.Line("// 选了\"变成指定牌\"但没有指定是哪张，按随机变化处理。");
        }

        a.Line($"foreach (var target in pickedCards{picked})");
        a.Line("{");
        a.Inner($"await CardCmd.TransformToRandom(target, {a.Ctx.Player}.RunState.Rng.CombatCardSelection);");
        a.Line("}");
    }

    public string Describe(EffectDescribe a)
    {
        a.AddPileVars();

        if (a.Effect.TransformInto == TransformInto.SpecificCard &&
            !string.IsNullOrWhiteSpace(a.Effect.CardId))
        {
            return a
                .With("pick", a.PickLabel())
                .With("card", DescriptionNames.CardNameForFilter(a.Project, a.Effect, a.Sentences))
                .Render("transformInto");
        }

        return a.With("pick", a.PickLabel()).Render("transformRandom");
    }

    // 挑牌与否由"变成什么"决定（选"指定牌"才挑），所以不给 needsCard，
    // 免得界面在"变成随机牌"时也弹出一个用不上的挑牌框。
    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "变化牌堆中的牌", labelEn: "Transform cards in a pile",
        scope: TargetScope.Cards, amount: false, needsTransform: true);
}
