using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 抽牌（<c>CardPileCmd.Draw</c>）。抽牌数走变量槽 <c>Cards</c>，所以卡面能引用
/// <c>{Cards:diff()}</c>、升级也会跟着变。
///
/// 命令收 <c>Player</c>（"谁抽"），所以作用域是玩家轴：抽给队友也写得出来
/// （见 <see cref="TargetScope.Player"/>）。
/// </summary>
internal sealed class DrawCardsBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.DrawCards;

    public void Emit(EffectEmit a)
    {
        // 要把抽到的牌存进临时牌集合时，得**留住** Draw 的返回值（它就是要存的那组牌）。
        // 抽牌可以作用于多个玩家（玩家轴），所以先攒进一个表，循环结束后一次存。
        if (a.Emitter.SetStoreLine(a.Effect, a.Ctx, Placeholder) is string store)
        {
            string collected = "drawn" + a.Emitter.NextLocalIndex();
            string line = store.Replace(Placeholder, collected, StringComparison.Ordinal);

            a.Line($"var {collected} = new List<CardModel>();");
            a.AgainstTargetsBlock((player, at) => new[]
            {
                $"{at}{collected}.AddRange((await CardPileCmd.Draw({a.Ctx.ChoiceContext}, {a.Amount}, {player})).ToList());",
            });
            a.Line(line);
            return;
        }

        a.AgainstTargets(player => $"await CardPileCmd.Draw({a.Ctx.ChoiceContext}, {a.Amount}, {player});");
    }

    /// <summary>
    /// 与 <see cref="SetStoreLine"/> 商量"要存的那组牌叫什么"用的占位符：先用它问出整行，
    /// 再把真正的变量名替换进去。
    ///
    /// 这样"存集合那一行怎么写"仍然只有 <c>EffectEmitter.SetStoreLine</c> 一处实现——
    /// 这里只是换掉其中一个操作数，而不是照着那句再拼一遍（拼两遍就会有两份会分叉的写法）。
    /// </summary>
    private const string Placeholder = "__set_cards__";

    public string Describe(EffectDescribe a)
    {
        a.AddCalculationVars();

        return a.Render(a.TargetedKey("draw", a.Effect.IsCalculated));
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "抽牌", labelEn: "Draw",
        scope: TargetScope.Player, amount: true);
}
