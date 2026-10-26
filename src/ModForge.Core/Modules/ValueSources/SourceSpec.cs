using ModForge.Core.Model;

namespace ModForge.Core.Modules.ValueSources;

/// <summary>
/// 一个变量来源**从哪儿读出来**——这是它的读法分类，决定取值语句长什么样。
///
/// 分类的用处不只是"出码时分个支"：它还回答两个界面与校验都要问的问题——
/// **要不要"取自谁"**（只有生物属性要）、**要不要再选一个状态**（只有状态层数要）。
/// 早先这两件事各有一份手写名单（`ValueSource.IsPlayerScoped` 里三十个枚举项、
/// schema 里一句 `k == PowerStacks`），三份名单必须互相对得上，而漏改一处没有任何报错。
/// 现在它们都从这一列推出来，见 <see cref="SourceCatalog"/>。
/// </summary>
public enum SourceReader
{
    /// <summary>不取值（"无"，即固定值）。</summary>
    None,

    /// <summary>
    /// 从**玩家**读：资源、X 值、金币、回合数。与具体生物无关，所以没有"取自谁"。
    /// </summary>
    PlayerScoped,

    /// <summary>
    /// 读**某个状态的层数**：还要知道是哪个状态（<see cref="ValueSource.PowerId"/>），
    /// 所以界面在它后面加一格状态选择框。
    /// </summary>
    PowerStacks,

    /// <summary>读**生物**的属性（当前 / 最大 / 已损失生命、格挡）。取自身还是目标由 Actor 决定。</summary>
    CreatureStat,

    /// <summary>
    /// 计数（战斗历史、牌堆张数、按条件数牌、自建跟踪）。**读法不在本模块**——
    /// 那三十种在计数模块里（<c>Modules/Counting/</c>），本模块只把请求转过去。
    /// 这一档存在的意义是"名单要全"：每个来源都必须在这里出现一次，漏一个会被测试拦下。
    /// </summary>
    Counter,
}

/// <summary>
/// 一个变量来源的声明：**叫什么名字** + **怎么读**。
///
/// 两条都只有这一处出处——名字被界面下拉与描述取用，取值表达式被所有"要算出变量值"的
/// 地方取用（数值 / 次数 / 选牌张数 / 两个费用阈值，五个角色同一套）。
///
/// <paramref name="Expression"/> 里的占位符由发射器填：
///   <c>{player}</c> —— 玩家表达式（资源、金币、回合数）；
///   <c>{who}</c>    —— 生物表达式（自身或目标，看 Actor）；
///   <c>{card}</c>   —— 卡牌实例（只有 X 值用）。
/// 占位符只在这三处出现，所以拼装就三行 replace，不值得为它做一套模板引擎。
/// </summary>
/// <param name="Kind">对应的数值来源种类。</param>
/// <param name="LabelZh">界面与描述里的中文名。</param>
/// <param name="LabelEn">英文名。</param>
/// <param name="Reader">读法（见 <see cref="SourceReader"/>）。</param>
/// <param name="Expression">取值表达式（玩家与生物两类才有；其余由专门的读法给出）。</param>
/// <param name="NeedsCard">X 值那条：只有卡牌里取得到（状态没有对应的实例）。</param>
public sealed record SourceSpec(
    ValueSourceKind Kind,
    string LabelZh,
    string LabelEn,
    SourceReader Reader,
    string? Expression = null,
    bool NeedsCard = false);
