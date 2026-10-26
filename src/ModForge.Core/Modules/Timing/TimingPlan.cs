using ModForge.Core.Model;

namespace ModForge.Core.Modules.Timing;

/// <summary>
/// 一张卡要发哪些钩子、每个钩子里有哪些效果——**规划**结果。
///
/// 与"出码"分开是有意的：这里的每一条都是**一条规则**，规则错了出码再对也没用，
/// 而规则以前散在生成器里约 290 行代码中间，很难看出来。举几条：
///
///   * "打出后"与"打出一张牌时"落到**同一个游戏钩子**（<c>AfterCardPlayed</c>），
///     所以必须合成一个覆写——各发一个的话后者会覆盖前者（同名 override 重复定义是编译错误）。
///   * "回合结束时 + 若在手牌中"要换成另一个钩子（见 <see cref="HookKind.EndOfTurnInHand"/>）。
///   * "自动打出这张牌"不是"回合开始时做点什么"，而是"牌在某个牌堆里时自己出来"，
///     走的是自动打出的**相位钩子**（且不能混进普通回合钩子，否则打出时又打出自己）。
///   * 只有"启用计数器且阈值 &gt; 1"的才需要计数器字段（阈值 1 等价于不启用）。
///   * <c>ShouldReceiveCombatHooks</c> 要按**全部**依赖钩子派发的效果算出来，漏算的后果是
///     静默的：代码编译正常、牌也在牌堆里，只是那个钩子永远不派发过来。
/// </summary>
public sealed class CardTimingPlan
{
    public required IReadOnlyList<EffectDef> OnPlay { get; init; }
    public required IReadOnlyList<EffectDef> AfterPlayed { get; init; }
    public required IReadOnlyList<EffectDef> CardPlayed { get; init; }
    public required IReadOnlyList<EffectDef> OnDraw { get; init; }
    public required IReadOnlyList<EffectDef> OnDiscard { get; init; }
    public required IReadOnlyList<EffectDef> OnExhaust { get; init; }

    /// <summary>"回合开始时"，已经剔掉"自动打出这张牌"那几条。</summary>
    public required IReadOnlyList<EffectDef> StartOfTurn { get; init; }

    /// <summary>"回合结束时"，已经剔掉"在手牌中"与"自动打出这张牌"那几条。</summary>
    public required IReadOnlyList<EffectDef> EndOfTurn { get; init; }

    /// <summary>"回合结束时 + 若在手牌中"（走 <c>OnTurnEndInHand</c>）。</summary>
    public required IReadOnlyList<EffectDef> EndOfTurnInHand { get; init; }

    /// <summary>"自动打出这张牌"且时机是回合开始。</summary>
    public required IReadOnlyList<EffectDef> SelfPlayOnStart { get; init; }

    /// <summary>"自动打出这张牌"且时机是回合结束。</summary>
    public required IReadOnlyList<EffectDef> SelfPlayOnEnd { get; init; }

    /// <summary>
    /// "打出一张牌时"每条效果各自的计数器字段名；<c>null</c> 表示这条不需要计数
    /// （没启用，或阈值 1 —— 那等同于不启用）。位置与 <see cref="CardPlayed"/> 一一对应。
    /// </summary>
    public required IReadOnlyList<string?> PlayedCounters { get; init; }

    /// <summary>需要"回合归零"的计数器字段（本回合的计数器在回合开始与结束各归零一次）。</summary>
    public required IReadOnlyList<string> TurnResetCounters { get; init; }

    /// <summary>
    /// 这张牌要不要声明 <c>ShouldReceiveCombatHooks</c>：只要有一条效果依赖钩子派发就要。
    /// <see cref="HookKind.EndOfTurnInHand"/> 那条**不算**——它是引擎逐张手牌直接调用的，不走派发。
    /// </summary>
    public required bool NeedsCombatHooks { get; init; }

    /// <summary>回合开始的钩子不带选择上下文，有"回合开始时"的效果时才需要那个自制上下文。</summary>
    public required bool NeedsHookContext { get; init; }
}

/// <summary>
/// 一个状态的触发时机规划。
/// </summary>
/// <param name="TurnStart">回合开始要触发的效果。</param>
/// <param name="CardPlayed">持有者打出一张牌时要触发的效果。</param>
/// <param name="DamageTaken">持有者受到伤害时要触发的效果。</param>
/// <param name="TurnEnd">回合结束要触发的效果。</param>
/// <param name="NeedsTurnEndHook">
/// 要不要发回合结束的钩子——**不只是**因为有效果：衰减也由它驱动，
/// 所以"没有 TurnEnd 触发但有衰减"时同样要发。
/// </param>
/// <param name="NeedsHookContext">回合开始的钩子不带选择上下文，有 TurnStart 触发时才需要。</param>
public sealed record PowerTimingPlan(
    IReadOnlyList<TriggeredEffect> TurnStart,
    IReadOnlyList<TriggeredEffect> CardPlayed,
    IReadOnlyList<TriggeredEffect> DamageTaken,
    IReadOnlyList<TriggeredEffect> TurnEnd,
    bool NeedsTurnEndHook,
    bool NeedsHookContext);

/// <summary>
/// 时机模块的规划入口：把"一条效果挂在什么时候"翻译成"要发哪些游戏钩子、每个钩子里放什么"。
///
/// 它是**唯一**读时机字段的地方（<c>Trigger</c> / <c>PileScope</c> / <c>UseCounter</c> /
/// <c>CounterTarget</c> / <c>CounterReset</c> / <c>IsSelfAutoPlay</c>），
/// 所以"什么算依赖钩子派发"这类判断不会在两个地方给出不同答案。
/// </summary>
public static class TimingModule
{
    public static CardTimingPlan PlanCard(IReadOnlyList<EffectDef> resolved)
    {
        IReadOnlyDictionary<EffectTrigger, List<EffectDef>> byTrigger = GroupByTrigger(resolved);

        List<EffectDef> startOfTurn = Get(byTrigger, EffectTrigger.StartOfTurn);
        List<EffectDef> endOfTurn = Get(byTrigger, EffectTrigger.EndOfTurn);

        // "自动打出这张牌"（原版轰击那种）不是"回合开始时做点什么"，而是"这张牌在某个牌堆里
        // 时自己出来"。引擎为它准备了另外两个相位钩子，所以要单独挑出来走那条分支——
        // 混进普通回合钩子会变成"打出时又打出自己"，直接递归。
        List<EffectDef> selfPlayOnStart = startOfTurn.Where(e => e.IsSelfAutoPlay).ToList();
        List<EffectDef> selfPlayOnEnd = endOfTurn.Where(e => e.IsSelfAutoPlay).ToList();

        startOfTurn = startOfTurn.Where(e => !e.IsSelfAutoPlay).ToList();
        endOfTurn = endOfTurn.Where(e => !e.IsSelfAutoPlay).ToList();

        // "回合结束时 + 若在手牌中"必须走游戏为这件事准备的钩子（OnTurnEndInHand），
        // 不能用 AfterSideTurnEnd——那个时机手牌已经被弃掉了，牌堆条件永远不成立。
        //
        // 两处细节都核对过游戏源码：
        //   EndPlayerTurnPhaseTwo: FlushPlayerHand（手牌→弃牌堆）之后才 Hook.AfterTurnEnd，
        //   所以 AfterSideTurnEnd 里读 Pile 永远是弃牌堆/消耗堆，不是手牌。
        //   而 OnTurnEndInHand 由 DoTurnEnd 调用，在弃牌之前——正是"回合结束时还在手上"。
        //   另外那个钩子调用期间牌会被临时移到 Play 牌堆（见 CardModel.OnTurnEndInHand 的说明），
        //   所以生成时**不能**再加牌堆判断，否则同样永不成立。"在不在手牌"由引擎保证。
        List<EffectDef> endOfTurnInHand = endOfTurn
            .Where(e => e.PileScope == PileScope.Hand)
            .ToList();

        endOfTurn = endOfTurn.Where(e => e.PileScope != PileScope.Hand).ToList();

        List<EffectDef> cardPlayed = Get(byTrigger, EffectTrigger.CardPlayed);

        // 计数器字段：只有"启用了计数器且阈值大于 1"的效果才需要一个字段——阈值 1 等价于
        // 不启用（每次都触发），多发一个永远只到 1 的计数器只会生成没人读的代码。
        // 计数器是这张牌自己的运行时状态，存不进存档也不需要存——战斗内的牌本来就是临时副本。
        var playedCounters = new List<string?>();

        for (int i = 0; i < cardPlayed.Count; i++)
        {
            EffectDef effect = cardPlayed[i];
            bool counted = effect.UseCounter && effect.CounterTarget > 1;

            playedCounters.Add(counted ? $"_playedCount{i}" : null);
        }

        // "本回合"的计数器要在回合结束与回合开始各归零一次（见 CounterReset 的说明）。
        // "本场战斗"的不用管：战斗内的牌是临时副本，战斗结束自然消失。
        var turnResetCounters = new List<string>();

        for (int i = 0; i < cardPlayed.Count; i++)
        {
            if (playedCounters[i] is string field && cardPlayed[i].CounterReset == CounterReset.ThisTurn)
            {
                turnResetCounters.Add(field);
            }
        }

        // 卡牌要收到**钩子**，就要表明自己会接收战斗钩子（ShouldReceiveCombatHooks）。
        // 默认实现是"待在战斗牌堆里才接收"，所以牌堆范围就是在这个钩子里自己判断：
        // 只要牌还在（手牌/抽牌堆/弃牌堆/消耗堆之一）钩子就会到，范围不符的直接返回。
        //
        // 用到钩子的时机都要算进来——回合类、"自动打出这张牌"、以及"打出一张牌时"
        // （它也靠 AfterCardPlayed 派发）。漏算的后果是静默的：代码编译正常、卡也在牌堆里，
        // 只是那个钩子永远不派发过来。
        //
        // "回合结束时 + 若在手牌中"不需要这个标记：OnTurnEndInHand 不走钩子派发，
        // 而是引擎直接遍历手牌逐张调用（见 CombatManager.DoTurnEnd），所以这里不算它。
        bool needsCombatHooks = startOfTurn.Count > 0 || endOfTurn.Count > 0 ||
                                selfPlayOnStart.Count > 0 || selfPlayOnEnd.Count > 0 ||
                                turnResetCounters.Count > 0 || cardPlayed.Count > 0;

        return new CardTimingPlan
        {
            OnPlay = Get(byTrigger, EffectTrigger.OnPlay),
            AfterPlayed = Get(byTrigger, EffectTrigger.AfterPlayed),
            CardPlayed = cardPlayed,
            OnDraw = Get(byTrigger, EffectTrigger.OnDraw),
            OnDiscard = Get(byTrigger, EffectTrigger.OnDiscard),
            OnExhaust = Get(byTrigger, EffectTrigger.OnExhaust),
            StartOfTurn = startOfTurn,
            EndOfTurn = endOfTurn,
            EndOfTurnInHand = endOfTurnInHand,
            SelfPlayOnStart = selfPlayOnStart,
            SelfPlayOnEnd = selfPlayOnEnd,
            PlayedCounters = playedCounters,
            TurnResetCounters = turnResetCounters,
            NeedsCombatHooks = needsCombatHooks,
            NeedsHookContext = startOfTurn.Count > 0,
        };
    }

    public static PowerTimingPlan PlanPower(PowerDef power)
    {
        List<TriggeredEffect> triggers = power.Triggers;

        List<TriggeredEffect> turnEnd = triggers.Where(t => t.When == PowerTrigger.TurnEnd).ToList();

        return new PowerTimingPlan(
            TurnStart: triggers.Where(t => t.When == PowerTrigger.TurnStart).ToList(),
            CardPlayed: triggers.Where(t => t.When == PowerTrigger.CardPlayed).ToList(),
            DamageTaken: triggers.Where(t => t.When == PowerTrigger.DamageTaken).ToList(),
            TurnEnd: turnEnd,

            // 回合结束的钩子还兼管衰减，所以"没有触发但有衰减"时也要发。
            NeedsTurnEndHook: turnEnd.Count > 0 || power.Decay != DecayMode.Never,

            NeedsHookContext: triggers.Any(t => t.When == PowerTrigger.TurnStart));
    }

    /// <summary>按触发时机把效果分组。</summary>
    public static IReadOnlyDictionary<EffectTrigger, List<EffectDef>> GroupByTrigger(
        IReadOnlyList<EffectDef> resolved)
    {
        var result = new Dictionary<EffectTrigger, List<EffectDef>>();

        foreach (EffectDef effect in resolved)
        {
            if (!result.TryGetValue(effect.Trigger, out List<EffectDef>? list))
            {
                list = new List<EffectDef>();
                result[effect.Trigger] = list;
            }

            list.Add(effect);
        }

        return result;
    }

    /// <summary>取某个触发时机的效果列表，没有则返回空列表。</summary>
    private static List<EffectDef> Get(
        IReadOnlyDictionary<EffectTrigger, List<EffectDef>> byTrigger,
        EffectTrigger trigger) =>
        byTrigger.TryGetValue(trigger, out List<EffectDef>? list) ? list : new List<EffectDef>();
}
