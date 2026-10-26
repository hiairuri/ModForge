using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;

namespace ModForge.Core.Modules.Counting;

/// <summary>
/// 计数 → C# 读表达式。**唯一**的出码处，按 <see cref="CounterSource"/> 分发。
///
/// 四种来源的读法完全不同，这也是把它们收进一个模块的理由——原来它们混在取值器的一个
/// 大 switch 里与"读当前生命""读能量"那些并列，看不出"这四个是计数、那三个是状态"。
/// </summary>
internal static class CounterEmitter
{
    /// <summary>
    /// 读这个计数的表达式。
    /// </summary>
    /// <param name="counter">哪一个计数。</param>
    /// <param name="source">它的参数（按条件数牌的"范围"与"条件"、以及各来源的每单位系数）。</param>
    /// <param name="card">
    /// 卡牌实例表达式。目前只有"按条件数牌"在 X 费卡的场景下会用到；
    /// 没有卡牌上下文（状态里）时传 null。
    /// </param>
    /// <param name="cardTypeFor">
    /// 卡牌模型 id → C# 类型引用。只有"按条件数牌"的**牌 ID** 那一维要用（判定是 <c>c is X</c>），
    /// 而那件事需要工程知识，所以由调用方注入；没用到那一维时传 null。
    /// </param>
    public static string Expression(
        Counter counter,
        ValueSource source,
        string? card,
        Func<string, string>? cardTypeFor = null) => counter.Source switch
    {
        CounterSource.CombatHistory => HistoryExpression(counter.History!),
        CounterSource.Pile => counter.Pile!,
        CounterSource.SelfTracked => TrackerExpression(counter),
        CounterSource.Filter => FilterExpression(source, cardTypeFor),
        _ => "0m",
    };

    /// <summary>
    /// 战斗历史的读法。
    ///
    /// 出牌记录走 <c>History.CardPlaysFinished</c>（**不在** <c>Entries</c> 里），
    /// 其余从 <c>Entries</c> 按条目类型取。"本回合"的加一条 <c>HappenedThisTurn</c>，
    /// "本场战斗"的不加——这是两套口径唯一的差别。
    /// </summary>
    private static string HistoryExpression(HistorySpec spec)
    {
        string source = spec.FinishedPlays
            ? "CombatManager.Instance.History.CardPlaysFinished"
            : $"CombatManager.Instance.History.Entries.OfType<{spec.EntryType}>()";

        string turn = spec.ThisTurn ? " && e.HappenedThisTurn(player.Creature.CombatState)" : string.Empty;

        // 数次数：直接 Count。
        if (spec.SumValue is null)
        {
            return $"{source}.Count(e => {spec.Filter}{turn})";
        }

        // 求总量：先按条件筛出条目，再对每条的某个数值求和。
        return $"{source}.Where(e => {spec.Filter}{turn}).Sum(e => {spec.SumValue})";
    }

    /// <summary>
    /// 自建跟踪：查我们的跟踪器（治疗那四项）。
    ///
    /// 这里写的是**本地**同名类型——每个 mod 都有同一份，加载时按"宿主 / 转发"决定谁真正记账
    /// （见 <c>Runtime/SharedRuntime.cs</c>）。所以这行代码在多 mod 环境下仍然读到同一份数字。
    /// </summary>
    private static string TrackerExpression(Counter counter) =>
        $"ModForge.Generated.Runtime.HealTracker.{counter.TrackerCall}(player.Creature)";

    /// <summary>
    /// 按条件数牌：范围 × 多维条件，与原版同一个形状（某牌堆.Cards.Count(c =&gt; 条件)）。
    /// 范围在 <see cref="ValueSource.CountScope"/> 上，条件在 <see cref="ValueSource.Filter"/> 上。
    ///
    /// 条件**与选牌模块共用同一份谓词生成处**（<c>CardPickFilter.PredicateOn</c>），
    /// 所以"能量消耗大于 2 的稀有攻击牌"在"挑出来改造"与"数有几张"两条路上判的是同一件事。
    /// 差别只在阈值：那边读效果上的三件套，这边读筛选自己的常数（谓词里引用不到局部变量）。
    /// </summary>
    private static string FilterExpression(ValueSource source, Func<string, string>? cardTypeFor)
    {
        string scope = CountScopeExpression(source.CountScope);

        string? predicate = CardPickFilter.PredicateOn(
            "c",
            source.Filter,
            role => ThresholdLiteral(source.Filter, role),
            cardTypeFor);

        // 一个维度都没筛：整堆都算（"手牌数"另一种写法）。校验器会提示这多半是想直接选
        // "手牌数"那个来源，而不是把条件清空的计数。
        return predicate is null ? scope : $"{scope}.Count(c => {predicate})";
    }

    /// <summary>
    /// 按条件数牌的阈值是**筛选自己的常数**（写进谓词里，运行时读不到外面算好的局部变量）。
    /// </summary>
    private static string ThresholdLiteral(CardFilter filter, ValueRole role) =>
        filter.ThresholdOf(role).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"符合条件的牌"从哪个范围数。</summary>
    private static string CountScopeExpression(CountScope scope) => scope switch
    {
        CountScope.Hand => "player.PlayerCombatState!.Hand.Cards",
        CountScope.DrawPile => "player.PlayerCombatState!.DrawPile.Cards",
        CountScope.DiscardPile => "player.PlayerCombatState!.DiscardPile.Cards",
        CountScope.ExhaustPile => "player.PlayerCombatState!.ExhaustPile.Cards",

        // 所有牌堆 = 整副牌（含出牌堆），与原版 AllCards 一致。
        _ => "player.PlayerCombatState!.AllCards",
    };
}
