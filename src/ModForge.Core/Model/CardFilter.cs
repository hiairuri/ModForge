namespace ModForge.Core.Model;

/// <summary>
/// 一个**费用维度**的比较运算符（能量消耗 / 星辰消耗）。六档，单选。
///
/// 它管"怎么比"，不管"比什么数"：阈值另外给——
///   选牌时阈值是**效果自己的数值**（<c>EnergyAmount</c> / <c>StarAmount</c>，占变量槽、能升级）；
///   按条件数牌时阈值是**这个筛选里的常数**（谓词在运行时读不到局部变量，见 <see cref="CardFilter"/>）。
///
/// 比较的对象是**印在卡面上的费用**：能量读 <c>CardModel.EnergyCost.Canonical</c>、
/// 星辰读 <c>CardModel.CanonicalStarCost</c>（-1 = 没有星辰费用）。战斗中的降费 / 加费
/// 不影响筛选。X 费能量卡的 Canonical 是 0；X 星辰卡要认 <see cref="IsX"/>，
/// 因为它的 CanonicalStarCost 仍是 -1。
/// </summary>
public enum CostComparison
{
    /// <summary>不筛选（这一维没启用；阈值也就不被读）。</summary>
    None,

    /// <summary>不小于：<c>&gt;=</c>。</summary>
    AtLeast,

    /// <summary>不大于：<c>&lt;=</c>。</summary>
    AtMost,

    /// <summary>大于：<c>&gt;</c>。</summary>
    GreaterThan,

    /// <summary>小于：<c>&lt;</c>。</summary>
    LessThan,

    /// <summary>不等于：<c>!=</c>。</summary>
    NotEqual,

    /// <summary>是 X 费 / X 星辰（阈值不参与）。</summary>
    IsX,
}

/// <summary>
/// "一批牌里**哪些算数**"——多维筛选的共同形状。
///
/// 两个实现：<see cref="Modules.CardPicking.CardPick"/>（选牌：挑出这些牌来做点什么）
/// 与 <see cref="CardFilter"/>（按条件数牌：数出有几张）。
/// 两者共用同一套维度与同一个谓词生成处（<c>CardPickFilter</c>），所以"稀有攻击牌"
/// 在"挑出来"与"数几张"两条路上判的是**同一件事**——各写一份迟早会出现
/// "这边说打击牌、那边按类型判"这种静默不一致。
///
/// **层次**（多维并存的关键）：维度**内部**多个取值之间是"或"，
/// 维度**之间**是"与"。
///
/// 阈值**不在**这个接口上：它是"比什么数"，由调用方在生成谓词时通过回调给出
/// （选牌读效果上的三件套，计数读自己的常数）。
/// </summary>
public interface ICardFilter
{
    /// <summary>筛选：卡牌类型（可多选）。</summary>
    List<CardType> Types { get; }

    /// <summary>筛选：稀有度（可多选）。</summary>
    List<CardRarity> Rarities { get; }

    /// <summary>筛选：牌名（可多个，按卡牌类型判 <c>c is X</c>）。</summary>
    List<string> CardIds { get; }

    /// <summary>筛选：标签（打击 / 防御 / 小刀 / 仆从 / 奥斯提攻击）。</summary>
    List<CardTag> Tags { get; }

    /// <summary>筛选：关键词（消耗 / 虚无 / 固有 / 保留 / 奇巧 / 不能打出 / 永恒）。</summary>
    List<CardKeyword> Keywords { get; }

    /// <summary>筛选：能量消耗的运算符。</summary>
    CostComparison EnergyComparison { get; }

    /// <summary>筛选：星辰消耗的运算符。</summary>
    CostComparison StarComparison { get; }
}

/// <summary>
/// <see cref="ICardFilter"/> 上那三条**由维度推出来**的判断。
///
/// 做成扩展方法（而不是在 <see cref="CardFilter"/> 与 <c>CardPick</c> 里各写一份）：
/// 两个实现共用同一份逻辑，而且调用方照旧写 <c>pick.ComparisonOf(role)</c>——
/// 扩展方法在实现类型上直接可用。接口的默认成员在这里用不了：默认成员必须通过接口
/// 类型调用，<c>pick.HasFilter()</c> 这种"静态类型是实现类"的写法看不到它。
/// </summary>
public static class CardFilterExtensions
{
    /// <summary>
    /// 有没有在筛（**任何一个维度启用**就算）。全空表示"什么都不筛"——
    /// 选牌那侧是"整堆都动"，计数那侧是"整堆张数"。
    ///
    /// **这是唯一的判据**：生成器据此决定走不走"自己拼候选列表 + 谓词"那条路，
    /// 描述与校验也读它。各写一份的话，改了维度就会漏掉某一处（"填了却没生效"）。
    /// 注意它**不含** <c>Piles</c> 与 <c>Source</c>——那两个是"在哪儿找"。
    /// </summary>
    public static bool HasFilter(this ICardFilter filter) =>
        filter.Types.Count > 0 || filter.Rarities.Count > 0 || filter.CardIds.Count > 0 ||
        filter.Tags.Count > 0 || filter.Keywords.Count > 0 ||
        filter.EnergyComparison != CostComparison.None ||
        filter.StarComparison != CostComparison.None;

    /// <summary>
    /// 某个数值角色对应的那个运算符（只有能量与星辰两个角色有比较器；
    /// 其它角色返回 <see cref="CostComparison.None"/>）。
    ///
    /// 逐角色读的地方都走它（变量模块分配阈值槽、描述写费用从句、出码解阈值、校验），
    /// 于是"哪个角色有哪些字段"只写一遍——早先是各处按 <c>role == Energy ? … : …</c> 手写。
    /// </summary>
    public static CostComparison ComparisonOf(this ICardFilter filter, ValueRole role) => role switch
    {
        ValueRole.Energy => filter.EnergyComparison,
        ValueRole.Star => filter.StarComparison,
        _ => CostComparison.None,
    };

    /// <summary>
    /// 这一维有没有在**比数字**——"不筛选"与"是 X"两档都不比，所以不读阈值。
    ///
    /// 逐角色问这句话的地方有三处：变量模块分配阈值槽、出码解阈值表达式、界面决定画不画
    /// 阈值那几格（选了 X 时它们隐去，与另外三项"填了不生效"的格同一个理由）。
    /// </summary>
    public static bool UsesThreshold(this ICardFilter filter, ValueRole role) =>
        filter.ComparisonOf(role) is not CostComparison.None and not CostComparison.IsX;
}

/// <summary>
/// 一份**独立的多维筛选**——给"按条件数牌"这类不是选牌、但要用同一套条件的场合用。
///
/// 与 <see cref="Modules.CardPicking.CardPick"/> 的差别只有两点：
///   它不挑牌（没有来源 / 牌堆 / 张数），只回答"哪些牌算数"；
///   两个费用阈值是**常数**（<see cref="EnergyThreshold"/> / <see cref="StarThreshold"/>）。
///
/// 阈值为什么是常数：这里生成的是一句 <c>Count(c =&gt; …)</c>，lambda 里引用不到外面
/// 算好的局部变量，所以"能量消耗大于 手牌数 的牌有几张"这种写法表达不了——
/// 那是选牌那侧的能力（它的阈值占变量槽）。要那种写法就得先把牌挑出来再数。
/// </summary>
public sealed class CardFilter : ICardFilter
{
    /// <inheritdoc />
    public List<CardType> Types { get; set; } = new();

    /// <inheritdoc />
    public List<CardRarity> Rarities { get; set; } = new();

    /// <inheritdoc />
    public List<string> CardIds { get; set; } = new();

    /// <inheritdoc />
    public List<CardTag> Tags { get; set; } = new();

    /// <inheritdoc />
    public List<CardKeyword> Keywords { get; set; } = new();

    /// <inheritdoc />
    public CostComparison EnergyComparison { get; set; } = CostComparison.None;

    /// <inheritdoc />
    public CostComparison StarComparison { get; set; } = CostComparison.None;

    /// <summary>能量消耗这一维比的那个数（<see cref="EnergyComparison"/> 不是"不筛选"/"是 X"时读它）。</summary>
    public int EnergyThreshold { get; set; }

    /// <summary>星辰消耗这一维比的那个数。</summary>
    public int StarThreshold { get; set; }

    /// <summary>某个费用角色的阈值。</summary>
    public int ThresholdOf(ValueRole role) => role == ValueRole.Energy ? EnergyThreshold : StarThreshold;

    public void SetThreshold(ValueRole role, int value)
    {
        if (role == ValueRole.Energy)
        {
            EnergyThreshold = value;
        }
        else
        {
            StarThreshold = value;
        }
    }

    /// <summary>
    /// 两份筛选是不是**同一套条件**（逐维比内容，不比引用）。
    ///
    /// 用途是生成器的等级分支判据：两级的算式只在"筛选本身换了"时才算不同，
    /// 只换了阈值也算不同（那会让谓词里的常数变掉）。
    /// </summary>
    public bool SameAs(CardFilter other) =>
        Types.SequenceEqual(other.Types) &&
        Rarities.SequenceEqual(other.Rarities) &&
        CardIds.SequenceEqual(other.CardIds, StringComparer.Ordinal) &&
        Tags.SequenceEqual(other.Tags) &&
        Keywords.SequenceEqual(other.Keywords) &&
        EnergyComparison == other.EnergyComparison &&
        StarComparison == other.StarComparison &&
        EnergyThreshold == other.EnergyThreshold &&
        StarThreshold == other.StarThreshold;

    /// <summary>
    /// 深拷贝。列表是引用类型，共享一份会让两条效果互相改——所以逐维新建。
    /// </summary>
    public CardFilter Clone() => new()
    {
        Types = new List<CardType>(Types),
        Rarities = new List<CardRarity>(Rarities),
        CardIds = new List<string>(CardIds),
        Tags = new List<CardTag>(Tags),
        Keywords = new List<CardKeyword>(Keywords),
        EnergyComparison = EnergyComparison,
        StarComparison = StarComparison,
        EnergyThreshold = EnergyThreshold,
        StarThreshold = StarThreshold,
    };
}
