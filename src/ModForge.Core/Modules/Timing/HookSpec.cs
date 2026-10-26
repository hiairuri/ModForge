namespace ModForge.Core.Modules.Timing;

/// <summary>
/// 时机模块认识的每一个**游戏钩子**。
///
/// 注意它不是"效果时机"或"状态时机"——两者最终都要落到同一批游戏钩子上，
/// 所以这里按钩子列，不按来源列（<c>EffectTrigger.StartOfTurn</c> 与
/// <c>PowerTrigger.TurnStart</c> 都落到 <see cref="TurnStart"/>）。
/// </summary>
public enum HookKind
{
    /// <summary>打出时（<c>OnPlay</c>，由出牌流程直接调用，不走钩子派发）。</summary>
    OnPlay,

    /// <summary>一张牌被打出后（<c>AfterCardPlayed</c>）。</summary>
    AfterCardPlayed,

    /// <summary>抽到时。</summary>
    AfterCardDrawn,

    /// <summary>被弃掉时。</summary>
    AfterCardDiscarded,

    /// <summary>被消耗时。</summary>
    AfterCardExhausted,

    /// <summary>回合开始。</summary>
    TurnStart,

    /// <summary>回合结束。</summary>
    TurnEnd,

    /// <summary>
    /// 回合结束时这张牌**还在手牌里**。
    ///
    /// 它与 <see cref="TurnEnd"/> 是两个不同的游戏钩子：引擎在回合结束时先弃手牌、
    /// 之后才发 <c>AfterSideTurnEnd</c>，所以那个钩子里读 <c>Pile</c> 永远不是手牌。
    /// "回合结束时若在手牌中"必须走 <c>OnTurnEndInHand</c>（它在弃牌之前，由引擎逐张手牌调用）。
    /// </summary>
    EndOfTurnInHand,

    /// <summary>自动打出的相位：回合开始那一侧。</summary>
    AutoPlayPhaseStart,

    /// <summary>自动打出的相位：回合结束那一侧。</summary>
    AutoPlayPhaseEnd,

    /// <summary>状态侧："持有者打出一张牌时"。</summary>
    PowerCardPlayed,

    /// <summary>状态侧："持有者受到伤害时"。</summary>
    PowerDamageReceived,
}

/// <summary>
/// 钩子发出去之后，前面那句"这不是给我看的"怎么判。
///
/// 单个条件、单个早退语句，各钩子用的表达式不同——差异来自**钩子的参数叫什么**
/// （牌叫 <c>card</c> 还是 <c>cardPlay.Card</c>）、以及**谁是持有者**
/// （卡牌的 Owner 是 Player、状态的 Owner 是 Creature）。这两件事都在 <see cref="HookOwner"/> 里。
/// </summary>
internal enum HookGuard
{
    /// <summary>不早退。</summary>
    None,

    /// <summary>只看"这张牌自己"（参数是一张 <c>CardModel</c>）。</summary>
    OwnCard,

    /// <summary>只看"这张牌自己被打出"（参数是一次 <c>CardPlay</c>）。</summary>
    OwnPlayedCard,

    /// <summary>只看"轮到自己这一边"。</summary>
    OwnSide,

    /// <summary>状态侧：只看持有者打出的牌。</summary>
    PowerOwnerPlayedCard,

    /// <summary>状态侧：只看持有者自己受的伤。</summary>
    PowerOwnerTarget,
}

/// <summary>
/// 发钩子时"持有者"那几个表达式怎么写。
///
/// 卡牌与状态的差别集中在这里：<c>CardModel.Owner</c> 是 <c>Player</c>、它的生物是
/// <c>Owner.Creature</c>；而 <c>PowerModel.Owner</c> 直接就是 <c>Creature</c>。
/// 早先这个差异散在十几个字符串里，写错就是编译不过或钩子永不派发。
/// </summary>
/// <param name="Side">"当前是谁的回合"读哪个属性。</param>
/// <param name="Owner">持有者本身。</param>
internal sealed record HookOwner(bool IsCard, string Side, string Owner)
{
    public static HookOwner ForCard { get; } = new(IsCard: true, Side: "Owner.Creature.Side", Owner: "Owner");

    public static HookOwner ForPower { get; } = new(IsCard: false, Side: "Owner.Side", Owner: "Owner");
}

/// <summary>
/// 一个游戏钩子的签名与早退方式——**唯一**的"时机 → 游戏钩子"表。
///
/// 这张表的价值在于它是**唯一**的：签名（方法名 + 参数）以前在卡牌侧与状态侧各写了一遍
/// （<c>AfterSideTurnStart</c> 的三行参数就是一模一样地抄了两次），改错一处不会报错，
/// 只会表现为"钩子永远不派发"或者编译失败。
/// </summary>
/// <param name="Method">游戏里的虚方法名。</param>
/// <param name="Access">
/// 覆写时的访问修饰符（<c>public</c> / <c>protected</c>）。
///
/// **必须与游戏里基类的声明一致**，否则是 CS0507（"无法更改访问修饰符"）编译错误。
/// 游戏自己就不统一：<c>OnPlay</c> 与 <c>OnTurnEndInHand</c> 是 <c>protected virtual</c>，
/// 而 <c>After*</c> 那一族是 <c>public virtual</c>。这条曾经被写成统一 public，
/// 是逐字节对比抓出来的（快照只比文本、不编译，所以这类错误要靠编译类工具兜住）。
/// </param>
/// <param name="Parameters">参数行（逐行原样发出，缩进由调用方加）。</param>
/// <param name="HasChoiceContext">
/// 钩子自己带不带 <c>PlayerChoiceContext</c>。不带的话（回合开始），
/// 效果里凡是需要"让玩家选择"的都得自己造一个（见生成的 <c>HookContext()</c>）。
/// </param>
/// <param name="Guard">早退条件。</param>
internal sealed record HookSpec(
    string Method,
    string Access,
    string[] Parameters,
    bool HasChoiceContext,
    HookGuard Guard)
{
    public static HookSpec For(HookKind kind) => kind switch
    {
        HookKind.OnPlay => new HookSpec(
            "OnPlay",
            "protected",
            new[] { "PlayerChoiceContext choiceContext,", "CardPlay cardPlay" },
            HasChoiceContext: true,
            HookGuard.None),

        HookKind.AfterCardPlayed => new HookSpec(
            "AfterCardPlayed",
            "public",
            new[] { "PlayerChoiceContext choiceContext,", "CardPlay cardPlay" },
            HasChoiceContext: true,
            HookGuard.OwnPlayedCard),

        HookKind.AfterCardDrawn => new HookSpec(
            "AfterCardDrawn",
            "public",
            new[] { "PlayerChoiceContext choiceContext,", "CardModel card,", "bool fromHandDraw" },
            HasChoiceContext: true,
            HookGuard.OwnCard),

        HookKind.AfterCardDiscarded => new HookSpec(
            "AfterCardDiscarded",
            "public",
            new[] { "PlayerChoiceContext choiceContext,", "CardModel card" },
            HasChoiceContext: true,
            HookGuard.OwnCard),

        HookKind.AfterCardExhausted => new HookSpec(
            "AfterCardExhausted",
            "public",
            new[] { "PlayerChoiceContext choiceContext,", "CardModel card,", "bool causedByEthereal" },
            HasChoiceContext: true,
            HookGuard.OwnCard),

        HookKind.TurnStart => new HookSpec(
            "AfterSideTurnStart",
            "public",
            new[] { "CombatSide side,", "IReadOnlyList<Creature> participants,", "ICombatState combatState" },
            HasChoiceContext: false,
            HookGuard.OwnSide),

        HookKind.TurnEnd => new HookSpec(
            "AfterSideTurnEnd",
            "public",
            new[] { "PlayerChoiceContext choiceContext,", "CombatSide side,", "IEnumerable<Creature> participants" },
            HasChoiceContext: true,
            HookGuard.OwnSide),

        HookKind.EndOfTurnInHand => new HookSpec(
            "OnTurnEndInHand",
            "protected",
            new[] { "PlayerChoiceContext choiceContext" },
            HasChoiceContext: true,
            HookGuard.None),

        HookKind.AutoPlayPhaseStart => new HookSpec(
            "AfterAutoPrePlayPhaseEnteredEarly",
            "public",
            new[] { "PlayerChoiceContext choiceContext,", "Player player" },
            HasChoiceContext: true,
            HookGuard.None),

        HookKind.AutoPlayPhaseEnd => new HookSpec(
            "AfterAutoPostPlayPhaseEntered",
            "public",
            new[] { "PlayerChoiceContext choiceContext,", "Player player" },
            HasChoiceContext: true,
            HookGuard.None),

        HookKind.PowerCardPlayed => new HookSpec(
            "AfterCardPlayed",
            "public",
            new[] { "PlayerChoiceContext choiceContext,", "CardPlay cardPlay" },
            HasChoiceContext: true,
            HookGuard.PowerOwnerPlayedCard),

        HookKind.PowerDamageReceived => new HookSpec(
            "AfterDamageReceived",
            "public",
            new[]
            {
                "PlayerChoiceContext choiceContext,", "Creature target,", "DamageResult result,",
                "ValueProp props,", "Creature? dealer,", "CardModel? cardSource",
            },
            HasChoiceContext: true,
            HookGuard.PowerOwnerTarget),

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
