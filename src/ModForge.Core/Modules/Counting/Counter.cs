using ModForge.Core.Model;

namespace ModForge.Core.Modules.Counting;

/// <summary>
/// 一个计数是从**哪来的**——这决定它的读法，也决定它能不能跨 mod 共用。
///
///   <see cref="CombatHistory"/> —— 游戏自己的战斗历史里有记录，我们是**读**它（天然共享）。
///   <see cref="Pile"/>          —— 牌堆张数，读当前牌堆（天然共享）。
///   <see cref="Filter"/>        —— 按条件数牌（天然共享）。
///   <see cref="SelfTracked"/>   —— 游戏**没有**这份记录，只能我们自己观察并记账
///                                  （治疗就是这一类）。它是唯一需要"共享运行时"的一类。
/// </summary>
public enum CounterSource
{
    CombatHistory,
    Pile,
    Filter,
    SelfTracked,
}

/// <summary>计数计的是"发生了几次"还是"累计多少量"。只影响界面上的说法与文案。</summary>
public enum CounterUnit
{
    Count,
    Amount,
}

/// <summary>
/// 战斗历史计数的读法参数。
/// </summary>
/// <param name="EntryType">条目类型（如 <c>DamageReceivedEntry</c>）。</param>
/// <param name="Filter">筛选条件（谓词变量是 <c>e</c>）。</param>
/// <param name="ThisTurn">
/// 是否限定"本回合"。这是唯一的口径差别：本回合的多一条 <c>HappenedThisTurn</c> 条件，
/// 本场战斗的不加。
/// </param>
/// <param name="FinishedPlays">
/// 出牌记录走 <c>History.CardPlaysFinished</c>（它不在 <c>Entries</c> 里），其余走
/// <c>Entries.OfType&lt;T&gt;()</c>。这是游戏那边的结构，不是我们的选择。
/// </param>
/// <param name="SumValue">
/// 非 null 表示这是"求总量"而不是"数次数"：先按条件筛出条目，再对每条的某个数值求和。
/// </param>
public sealed record HistorySpec(
    string EntryType,
    string Filter,
    bool ThisTurn,
    bool FinishedPlays = false,
    string? SumValue = null);

/// <summary>
/// 一个计数。**显示名也在这里**——它是"这个计数叫什么"的唯一出处，
/// 描述与界面都从这里取（见 <see cref="ValueSources.SourceCatalog.Label"/> 的分发）。
/// </summary>
/// <param name="Kind">对应的数值来源种类。</param>
/// <param name="LabelZh">中文名。</param>
/// <param name="LabelEn">英文名。</param>
/// <param name="Source">来自哪里（决定读法与能否共用）。</param>
/// <param name="Unit">计次数还是计量。</param>
/// <param name="History">战斗历史类才有的读法参数。</param>
/// <param name="Pile">牌堆类才有的：读哪个牌堆的表达式。</param>
/// <param name="TrackerCall">
/// 自建跟踪类才有的：跟踪器上的方法名（治疗的四项）。
/// </param>
public sealed record Counter(
    ValueSourceKind Kind,
    string LabelZh,
    string LabelEn,
    CounterSource Source,
    CounterUnit Unit,
    HistorySpec? History = null,
    string? Pile = null,
    string? TrackerCall = null);
