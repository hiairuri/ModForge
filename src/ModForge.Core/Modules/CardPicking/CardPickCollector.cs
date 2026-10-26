using ModForge.Core.Model;

namespace ModForge.Core.Modules.CardPicking;

/// <summary>
/// 收集表达式：把"在哪儿找"翻成一段取出候选牌的 C# 表达式。这是**跨牌堆**那件事的落点。
///
/// 游戏自己**没有**多牌堆的选牌入口（<c>CardSelectCmd.FromCombatPile</c> 只收单个牌堆），
/// 所以跨牌堆要我们自己拼集合。拼法有现成工具：<c>CardPile.GetCards(player, params PileType[])</c>
/// （原版"窃贼蝗虫"就是用它跨抽牌堆 + 弃牌堆取牌）。
///
/// 拼出来的并集顺序必须**确定**：多牌堆的选择界面走 <c>CardSelectCmd.FromSimpleGrid</c>，
/// 它用"索引"在多人之间同步（注释明确要求列表是"确定性生成"的），所以两端必须构造出
/// 同样顺序的列表。按牌堆固定顺序拼接天然满足，**不能**按语言/文化排序。
/// </summary>
public static class CardPickCollector
{
    /// <summary>
    /// 是不是"全部战斗牌堆"（流程二一个都没勾）。
    ///
    /// 空列表就是全选，用游戏的 <c>PlayerCombatState.AllCards</c>——它含**出牌堆**，
    /// 比我们在流程二里列出的四个牌堆多一个。原版"完美打击"数所有牌堆里的打击牌用的就是它，
    /// 所以这是原版口径，不是疏漏。
    /// </summary>
    public static bool IsAllCombatPiles(CardPick pick) =>
        pick.Source == PickSource.Piles && pick.Piles.Count == 0;

    /// <summary>
    /// 候选牌的来源表达式（后面接 <c>.Where(...)</c> / <c>.ToList()</c> / <c>.TakeRandom(...)</c>）。
    /// </summary>
    /// <param name="setCards">
    /// 来源是**临时牌集合**时，那个"取集合里那些牌"的表达式（由调用方按
    /// <c>Modules/CardSets</c> 的键规则拼好传进来）。为 null 时说明当前上下文给不出集合
    /// （状态里没有"一次出牌结算"，见 <c>EmitContext.SetAddress</c>）——那时给一个空列表，
    /// 让生成结果仍然可编译，具体问题由校验器报出来。
    /// </param>
    public static string CardList(CardPick pick, string player, string? setCards = null)
    {
        if (pick.Source == PickSource.Deck)
        {
            return $"PileType.Deck.GetPile({player}).Cards";
        }

        // 临时牌集合：候选就是这一次结算里刚产生/挑出的那几张。取不到时它给的是**空列表**
        // （运行时那侧保证），所以这里不必判空，后面接 .Where / .ToList 都成立。
        if (pick.Source == PickSource.Set)
        {
            return setCards ?? "new List<CardModel>()";
        }

        if (IsAllCombatPiles(pick))
        {
            return $"{player}.PlayerCombatState!.AllCards";
        }

        if (pick.Piles.Count == 1)
        {
            return $"PileType.{PileTypeName(pick.Piles[0])}.GetPile({player}).Cards";
        }

        // 多个牌堆：用游戏自带的并集工具，顺序即传入顺序（确定）。
        string piles = string.Join(", ", pick.Piles.Select(p => $"PileType.{PileTypeName(p)}"));
        return $"CardPile.GetCards({player}, {piles})";
    }

    /// <summary>
    /// 候选牌所在的**单个战斗牌堆**表达式；不是"恰好一个战斗牌堆"时返回 null。
    ///
    /// <c>CardSelectCmd.FromCombatPile</c> 要的是 <c>CardPile</c> 而不是牌列表，而且它
    /// **只收战斗牌堆**（对卡组会抛 <c>InvalidOperationException("Cannot perform on a non combat pile")</c>）。
    /// 所以卡组、多牌堆、全战斗牌堆这三种都必须换别的入口（见 <see cref="CardPickEmitter"/>）。
    /// </summary>
    public static string? SingleCombatPile(CardPick pick, string player) =>
        pick.Source == PickSource.Piles && pick.Piles.Count == 1
            ? $"PileType.{PileTypeName(pick.Piles[0])}.GetPile({player})"
            : null;

    /// <summary>牌堆的 C# 枚举名。生成器给"放入哪个牌堆"之类的地方也用这一份，避免两份映射。</summary>
    public static string PileTypeName(PileKind pile) => pile switch
    {
        PileKind.Hand => "Hand",
        PileKind.DrawPile => "Draw",
        PileKind.DiscardPile => "Discard",
        PileKind.ExhaustPile => "Exhaust",
        _ => "Hand",
    };
}
