using ModForge.Core.Model;

namespace ModForge.Core.Modules.Counting;

/// <summary>
/// 全部计数的**唯一名单**，以及"某个数值来源是不是计数"的唯一判据。
///
/// 为什么要有一份名单：这些来源原来散在取值器的一个大 switch 里（战斗历史、牌堆张数、
/// 按条件数牌、自建跟踪混在一起），于是"哪些来源需要自建跟踪"这类判断没有出处——
/// 想知道"这个工程用不用共享运行时"，只能去 grep 生成器。有了名单就能一句话问出来
/// （<see cref="UsesSelfTracked"/>）。
///
/// **只收"数出来的"来源**。读当前状态的那些（生命 / 格挡 / 能量 / 星辰 / 金币 / X 值 /
/// 回合数）不是计数，留在取值器里——它们问的是"现在是多少"，不是"发生过几次"。
/// </summary>
public static class CounterCatalog
{
    /// <summary>战斗历史类：十种口径，各有"本回合 / 本场战斗"两档。</summary>
    private static IEnumerable<Counter> History()
    {
        yield return HistoryCounter(ValueSourceKind.CardsPlayedThisTurn, "本回合打出的牌数", "cards played this turn",
            new HistorySpec("CardPlayFinished", "e.CardPlay.Card.Owner == player", ThisTurn: true, FinishedPlays: true));

        yield return HistoryCounter(ValueSourceKind.CardsPlayedThisCombat, "本场战斗打出的牌数", "cards played this combat",
            new HistorySpec("CardPlayFinished", "e.CardPlay.Card.Owner == player", ThisTurn: false, FinishedPlays: true));

        // 抽牌只数玩家自己的，且排除回合开始的起手抽牌（!e.FromHandDraw），与原版一致。
        yield return HistoryCounter(ValueSourceKind.CardsDrawnThisTurn, "本回合抽到的牌数", "cards drawn this turn",
            new HistorySpec("CardDrawnEntry", "e.Actor == player.Creature && !e.FromHandDraw", ThisTurn: true));

        yield return HistoryCounter(ValueSourceKind.CardsDrawnThisCombat, "本场战斗抽到的牌数", "cards drawn this combat",
            new HistorySpec("CardDrawnEntry", "e.Actor == player.Creature && !e.FromHandDraw", ThisTurn: false));

        // 失去生命：只数"真的扣到血了"的那些（UnblockedDamage > 0）。被格挡完全挡下的不算，
        // 而"受到伤害"与"失去生命"两种来源都会留下这种记录。
        yield return HistoryCounter(ValueSourceKind.HpLostThisTurn, "本回合失去生命的次数", "the number of times you lost HP this turn",
            new HistorySpec("DamageReceivedEntry", "e.Receiver == player.Creature && e.Result.UnblockedDamage > 0", ThisTurn: true));

        yield return HistoryCounter(ValueSourceKind.HpLostThisCombat, "本场战斗失去生命的次数", "the number of times you lost HP this combat",
            new HistorySpec("DamageReceivedEntry", "e.Receiver == player.Creature && e.Result.UnblockedDamage > 0", ThisTurn: false));

        // 受击：所有打到自己身上的伤害事件，被格挡挡光的也算——与"失去生命"只差这条口径。
        yield return HistoryCounter(ValueSourceKind.HitsTakenThisTurn, "本回合受击次数", "the number of times you were hit this turn",
            new HistorySpec("DamageReceivedEntry", "e.Receiver == player.Creature", ThisTurn: true));

        yield return HistoryCounter(ValueSourceKind.HitsTakenThisCombat, "本场战斗受击次数", "the number of times you were hit this combat",
            new HistorySpec("DamageReceivedEntry", "e.Receiver == player.Creature", ThisTurn: false));

        // 攻击次数：一次攻击动作一条记录（多段攻击只算一次）；不含奥斯提的攻击。
        yield return HistoryCounter(ValueSourceKind.AttacksThisTurn, "本回合攻击次数", "the number of attacks you made this turn",
            new HistorySpec("CreatureAttackedEntry", "e.Actor == player.Creature", ThisTurn: true));

        yield return HistoryCounter(ValueSourceKind.AttacksThisCombat, "本场战斗攻击次数", "the number of attacks you made this combat",
            new HistorySpec("CreatureAttackedEntry", "e.Actor == player.Creature", ThisTurn: false));

        yield return HistoryCounter(ValueSourceKind.CardsExhaustedThisTurn, "本回合消耗的牌数", "the number of cards you exhausted this turn",
            new HistorySpec("CardExhaustedEntry", "e.Card.Owner == player", ThisTurn: true));

        yield return HistoryCounter(ValueSourceKind.CardsExhaustedThisCombat, "本场战斗消耗的牌数", "the number of cards you exhausted this combat",
            new HistorySpec("CardExhaustedEntry", "e.Card.Owner == player", ThisTurn: false));

        yield return HistoryCounter(ValueSourceKind.CardsDiscardedThisTurn, "本回合弃置的牌数", "the number of cards you discarded this turn",
            new HistorySpec("CardDiscardedEntry", "e.Card.Owner == player", ThisTurn: true));

        yield return HistoryCounter(ValueSourceKind.CardsDiscardedThisCombat, "本场战斗弃置的牌数", "the number of cards you discarded this combat",
            new HistorySpec("CardDiscardedEntry", "e.Card.Owner == player", ThisTurn: false));

        yield return HistoryCounter(ValueSourceKind.CardsGeneratedThisTurn, "本回合生成的牌数", "the number of cards you generated this turn",
            new HistorySpec("CardGeneratedEntry", "e.Creator == player", ThisTurn: true));

        yield return HistoryCounter(ValueSourceKind.CardsGeneratedThisCombat, "本场战斗生成的牌数", "the number of cards you generated this combat",
            new HistorySpec("CardGeneratedEntry", "e.Creator == player", ThisTurn: false));

        // 伤害量：把自己相关的每次伤害里真正扣到血的部分求和（被格挡的不算）。
        yield return HistoryCounter(ValueSourceKind.DamageTakenThisTurn, "本回合受到的伤害", "the damage you took this turn",
            new HistorySpec("DamageReceivedEntry", "e.Receiver == player.Creature", ThisTurn: true,
                SumValue: "e.Result.UnblockedDamage"),
            CounterUnit.Amount);

        yield return HistoryCounter(ValueSourceKind.DamageTakenThisCombat, "本场战斗受到的伤害", "the damage you took this combat",
            new HistorySpec("DamageReceivedEntry", "e.Receiver == player.Creature", ThisTurn: false,
                SumValue: "e.Result.UnblockedDamage"),
            CounterUnit.Amount);

        yield return HistoryCounter(ValueSourceKind.DamageDealtThisTurn, "本回合造成的伤害", "the damage you dealt this turn",
            new HistorySpec("DamageReceivedEntry", "e.Dealer == player.Creature", ThisTurn: true,
                SumValue: "e.Result.UnblockedDamage"),
            CounterUnit.Amount);

        yield return HistoryCounter(ValueSourceKind.DamageDealtThisCombat, "本场战斗造成的伤害", "the damage you dealt this combat",
            new HistorySpec("DamageReceivedEntry", "e.Dealer == player.Creature", ThisTurn: false,
                SumValue: "e.Result.UnblockedDamage"),
            CounterUnit.Amount);
    }

    /// <summary>
    /// 自建跟踪类：恢复生命。
    ///
    /// 游戏**没有**治疗条目（<c>CreatureCmd.Heal</c> 只写运行级统计，不写 <c>CombatHistory</c>），
    /// 也没有战斗内治疗钩子——所以这四项只能由我们自己观察每次治疗并记账
    /// （运行时见 <c>Runtime/HealTracker.cs</c>）。这也是**唯一**一类需要共享运行的计数：
    /// 别的都读游戏的记录，天然就是共享的。
    /// </summary>
    private static IEnumerable<Counter> SelfTracked()
    {
        yield return new Counter(ValueSourceKind.HealsThisTurn, "本回合恢复生命的次数", "the number of times you healed this turn",
            CounterSource.SelfTracked, CounterUnit.Count, TrackerCall: "HealsThisTurn");

        yield return new Counter(ValueSourceKind.HealsThisCombat, "本场战斗恢复生命的次数", "the number of times you healed this combat",
            CounterSource.SelfTracked, CounterUnit.Count, TrackerCall: "HealsThisCombat");

        yield return new Counter(ValueSourceKind.HealAmountThisTurn, "本回合恢复的生命量", "the HP you restored this turn",
            CounterSource.SelfTracked, CounterUnit.Amount, TrackerCall: "HealAmountThisTurn");

        yield return new Counter(ValueSourceKind.HealAmountThisCombat, "本场战斗恢复的生命量", "the HP you restored this combat",
            CounterSource.SelfTracked, CounterUnit.Amount, TrackerCall: "HealAmountThisCombat");
    }

    /// <summary>牌堆与条件：读当前牌堆，天然共享。</summary>
    private static IEnumerable<Counter> Piles()
    {
        yield return PileCounter(ValueSourceKind.HandCount, "手牌数", "cards in hand",
            "player.PlayerCombatState!.Hand.Cards.Count");

        yield return PileCounter(ValueSourceKind.DrawPileCount, "抽牌堆张数", "cards in the draw pile",
            "player.PlayerCombatState!.DrawPile.Cards.Count");

        yield return PileCounter(ValueSourceKind.DiscardPileCount, "弃牌堆张数", "cards in the discard pile",
            "player.PlayerCombatState!.DiscardPile.Cards.Count");

        yield return PileCounter(ValueSourceKind.ExhaustPileCount, "消耗堆张数", "cards in the exhaust pile",
            "player.PlayerCombatState!.ExhaustPile.Cards.Count");

        // 所有牌堆 = 整副战斗牌堆（含出牌堆），与原版 AllCards 一致。
        yield return PileCounter(ValueSourceKind.AllPilesCount, "所有牌堆张数", "cards in all piles",
            "player.PlayerCombatState!.AllCards.Count()");

        // 按条件数牌：范围与条件由 ValueSource 上的两个字段给出，所以在 CounterEmitter 里拼。
        yield return new Counter(ValueSourceKind.FilteredCardCount, "符合条件的牌数", "cards matching a filter",
            CounterSource.Filter, CounterUnit.Count);
    }

    /// <summary>全部计数。顺序即界面呈现的顺序（与枚举顺序一致，便于对照）。</summary>
    public static IReadOnlyList<Counter> All { get; } = History()
        .Concat(SelfTracked())
        .Concat(Piles())
        .ToList();

    private static readonly Dictionary<ValueSourceKind, Counter> ByKind =
        All.ToDictionary(c => c.Kind);

    /// <summary>这个来源是不是计数；不是的话返回 null（它读的是"当前状态"）。</summary>
    public static Counter? Find(ValueSourceKind kind) =>
        ByKind.TryGetValue(kind, out Counter? counter) ? counter : null;

    /// <summary>这个来源是不是计数。</summary>
    public static bool IsCounter(ValueSourceKind kind) => ByKind.ContainsKey(kind);

    /// <summary>
    /// 这组来源里有没有**自建跟踪**的。有的话就需要共享运行时
    /// （游戏的记录里没有治疗，只能自己观察并记账）。
    /// </summary>
    public static bool UsesSelfTracked(IEnumerable<ValueSourceKind> kinds) =>
        kinds.Any(k => Find(k)?.Source == CounterSource.SelfTracked);

    private static Counter HistoryCounter(
        ValueSourceKind kind,
        string zh,
        string en,
        HistorySpec spec,
        CounterUnit unit = CounterUnit.Count) =>
        new(kind, zh, en, CounterSource.CombatHistory, unit, History: spec);

    private static Counter PileCounter(ValueSourceKind kind, string zh, string en, string pileExpr) =>
        new(kind, zh, en, CounterSource.Pile, CounterUnit.Count, Pile: pileExpr);
}
