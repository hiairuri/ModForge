using ModForge.Core.Model;
using ModForge.Core.Modules.Variables;

namespace ModForge.Core.Modules.CardPicking;

/// <summary>
/// "在哪儿找"。选牌流程的第一步（此牌 / 牌堆 / 卡组）决定用哪一个。
/// </summary>
public enum PickSource
{
    /// <summary>这张牌自己（原版"王者之踢"改自身费用就是这样）。只有卡牌能用。</summary>
    ThisCard,

    /// <summary>
    /// 从牌堆里找。具体哪些牌堆看 <see cref="CardPick.Piles"/>——
    /// **空表示全部战斗牌堆**（手牌 + 抽牌堆 + 弃牌堆 + 消耗堆 + 出牌堆）。
    /// </summary>
    Piles,

    /// <summary>
    /// 永久牌库（游戏的 <c>PileType.Deck</c>）。战斗外也存在，卡组改动是持久化的。
    /// 注意它**不是**战斗牌堆：<c>CardSelectCmd.FromCombatPile</c> 对它抛异常。
    /// </summary>
    Deck,

    /// <summary>
    /// 从**临时牌集合**里取（集合 ID 在 <see cref="CardPick.SetId"/>）。
    ///
    /// 这是"同一张牌的其它效果动这些牌"的入口：前一条效果（抽牌 / 生成 / 挑牌）把结果**存进**
    /// 集合，后面这条按集合取，于是"让抽到的那几张牌降费 / 附魔"写得出来。
    /// 集合的生命周期是**一次出牌结算**（见 <c>Modules/CardSets</c>），所以这里取到的永远只是
    /// "这次结算里刚产生的那几张牌"。
    /// </summary>
    Set,
}

/// <summary>
/// "怎么取"。选牌流程的第九步。
///
/// 与 <see cref="Model.CardPickMode"/>（旧模型）的区别是多了 <see cref="AtMost"/>——
/// "至多 N 张"游戏原生支持（<c>CardSelectorPrefs(prompt, 0, N)</c>），旧模型表达不了。
/// 名字刻意不叫 CardPickMode：那是旧模型的枚举，两者会在同一个文件作用域里撞名。
/// </summary>
public enum PickMode
{
    /// <summary>不筛张数，取该范围里的全部（原版"神化"升级全部手牌）。</summary>
    All,

    /// <summary>由玩家挑 N 张，必须正好 N 张。</summary>
    Choose,

    /// <summary>随机取 N 张。少于 N 张时取到多少算多少（<c>TakeRandom</c> 的 Take 语义）。</summary>
    Random,

    /// <summary>至多 N 张：玩家挑不超过 N 张（<c>MinSelect = 0, MaxSelect = N</c>）。原版"净化"。 </summary>
    AtMost,
}

/// <summary>
/// 选牌模块的输入：选哪些牌。**界面上就是那十步流程，字段与流程一一对应。**
///
///   流程一 来源    —— <see cref="Source"/>：此牌 / 牌堆 / 卡组；
///   流程二 牌堆    —— <see cref="Piles"/>：在哪些牌堆里找（可多选；空 = 全部战斗牌堆）。
///                     来源是"按某一维筛"时它是**范围**，是"牌堆"时它就是来源本身；
///   流程三 筛选维度 —— 五个取值列表 + 两个费用运算符，**可以同时生效（与）**：
///                     类型 / 稀有度 / 标签 / 关键词 / 牌 ID / 能量消耗 / 星辰消耗。
///                     "卡组"那一档不筛选时就是整副卡组；
///   流程九 取法    —— <see cref="Mode"/>：全部 / 选择 / 随机 / 至多；
///   流程十 张数    —— <see cref="Count"/>：走变量模块的三件套（所以"等于手牌数"也算得出来）。
///
/// **维度之间是"与"、维度内部取值之间是"或"**：所以"能量消耗大于 2、星辰消耗大于 0 的
/// 稀有攻击牌"写得出来（<c>Rarity == Rare &amp;&amp; Type == Attack &amp;&amp; EnergyCost &gt; 2 …</c>）。
/// 取值列表空 = 那一维不筛；费用运算符 <see cref="CostComparison.None"/> = 那一维不筛。
///
/// **哪些选项能出现由效果自己的策略决定**（<see cref="CardPickPolicies"/>）：
/// 例如消耗牌只屏蔽"卡组"，附魔把"不可附魔"加进黑名单。策略不存进模型——它按种类现算，
/// 所以模型里不会有一份"可能过期的白名单"。
/// </summary>
public sealed class CardPick : ICardFilter
{
    /// <summary>在哪儿找（流程一）。</summary>
    public PickSource Source { get; set; } = PickSource.Piles;

    /// <summary>
    /// 从哪些牌堆找（流程二，可多选）。**空 = 全部战斗牌堆**，此时用游戏的
    /// <c>PlayerCombatState.AllCards</c>（含出牌堆）——原版"完美打击"数所有牌堆里的打击牌
    /// 就是这个范围。
    ///
    /// 默认**空**而不是"手牌"：空是流程二"一个都没勾"的那个语义（= 全部战斗牌堆），
    /// 而且 C# 的集合初始化器 <c>Piles = { X }</c> 是**往现有列表里追加**——
    /// 默认给了手牌的话，`Piles = { Hand }` 会变成 `[Hand, Hand]`、
    /// `Piles = { DrawPile }` 会变成 `[Hand, DrawPile]`，静默改变语义（这个坑已经踩过）。
    /// 界面上新增一条效果时，默认勾的就是手牌（那是最常见的形态），但它写在界面的初值里，
    /// 不写进模型默认值。
    /// </summary>
    public List<PileKind> Piles { get; set; } = new();

    /// <summary>筛选：卡牌类型（可多选；空 = 不筛这一维）。</summary>
    public List<CardType> Types { get; set; } = new();

    /// <summary>筛选：稀有度（可多选）。</summary>
    public List<CardRarity> Rarities { get; set; } = new();

    /// <summary>筛选：牌名（可多个，按卡牌类型判 <c>c is X</c>）。</summary>
    public List<string> CardIds { get; set; } = new();

    /// <summary>筛选：标签（可多个；打击 / 防御 / 小刀 / 仆从 / 奥斯提攻击）。</summary>
    public List<CardTag> Tags { get; set; } = new();

    /// <summary>筛选：关键词（可多个；消耗 / 虚无 / 固有 / 保留 / 奇巧 / 不能打出 / 永恒）。</summary>
    public List<CardKeyword> Keywords { get; set; } = new();

    /// <summary>
    /// 筛选：能量消耗的运算符（阈值在效果的 <c>EnergyAmount</c> 上，见
    /// <see cref="Model.ValueRole.Energy"/>）。<see cref="CostComparison.None"/> = 这一维不筛。
    /// </summary>
    public CostComparison EnergyComparison { get; set; } = CostComparison.None;

    /// <summary>
    /// 筛选：星辰消耗的运算符（阈值在效果的 <c>StarAmount</c> 上）。
    /// <see cref="CostComparison.None"/> = 这一维不筛。
    /// </summary>
    public CostComparison StarComparison { get; set; } = CostComparison.None;

    /// <summary>怎么取（流程九）。</summary>
    public PickMode Mode { get; set; } = PickMode.Choose;

    /// <summary>
    /// 来源是 <see cref="PickSource.Set"/> 时，取的是哪个**临时牌集合**（不可多选：一个集合）。
    ///
    /// 名字按 <c>[A-Za-z0-9_]+</c> 写：它进运行时的键（键用 <c>.</c> 分段），所以不许有点。
    /// 集合由**同一张牌**上的一条效果定义（<see cref="Model.EffectDef.SetId"/>），
    /// 校验器保证它指向一个真的存在的定义——指错了生成出来的会是一个空集合，
    /// 于是"那条效果什么也不做"，而这是**静默**的。
    /// </summary>
    public string? SetId { get; set; }

    /// <summary>
    /// 取几张（流程十）。走变量模块的三件套：<c>基础值 + 变量 × 变量系数</c>。
    ///
    /// **只给"挑几张不体现在效果数值里"的那些效果用**（升级 / 变化 / 搬运 / 附魔 / 复制 /
    /// 增伤 / 重放 / 降费 / 免费 / 自动打出——它们的数值另有含义：复制几份、附几层、打几次…）。
    /// 消耗牌这类"效果数值就是张数"的效果**不用这一格**：那种效果把张数写在自己的
    /// <c>&lt;base&gt;</c> 里，由变量模块算好传给出码处（见 <c>CardPickPolicy.CountFromEffect</c>）——
    /// "每个效果的 base 值即为该效果的基础值"，所以 pick 里不该再存一份变量。
    ///
    /// 默认"1 张、没有变量"——流程九默认是"选择"，配 1 张就是最常见的"挑一张"。
    /// </summary>
    public NumericValue Count { get; set; } = new(1, new ValueSource());

    /* ------------------------------------------------------------------ 便捷构造 */

    /// <summary>挑**这张牌自己**（原版"王者之踢"改自身费用那种）。</summary>
    public static CardPick This() => new() { Source = PickSource.ThisCard };

    /// <summary>从某个牌堆里取若干张（默认整个牌堆）。</summary>
    public static CardPick InPile(PileKind pile, PickMode mode = PickMode.All, int count = 1) => new()
    {
        Source = PickSource.Piles,
        Piles = { pile },
        Mode = mode,
        Count = new NumericValue(count, new ValueSource()),
    };

    /// <summary>从**整副运行牌库**里取（永久生效的那类改造用它，原版在商店/事件里做）。</summary>
    public static CardPick InDeck(PickMode mode = PickMode.All, int count = 1) => new()
    {
        Source = PickSource.Deck,
        Piles = new List<PileKind>(),
        Mode = mode,
        Count = new NumericValue(count, new ValueSource()),
    };

    /// <summary>按**牌名**挑（可多个，多个之间是"或"）。</summary>
    public static CardPick Named(params string[] cardIds) => new()
    {
        CardIds = cardIds.ToList(),
    };

    /// <summary>按**标签**挑（可多个）。</summary>
    public static CardPick OfTags(params CardTag[] tags) => new()
    {
        Tags = tags.ToList(),
    };

    /// <summary>按**类型**挑（可多个）。</summary>
    public static CardPick OfTypes(params CardType[] types) => new()
    {
        Types = types.ToList(),
    };

    /// <summary>按**关键词**挑（可多个）。</summary>
    public static CardPick OfKeywords(params CardKeyword[] keywords) => new()
    {
        Keywords = keywords.ToList(),
    };

    /// <summary>按**稀有度**挑（可多个）。</summary>
    public static CardPick OfRarities(params CardRarity[] rarities) => new()
    {
        Rarities = rarities.ToList(),
    };

    /// <summary>
    /// 筛的是"**具名的**某几张牌"（而不是"满足一批条件的牌"）。
    ///
    /// 判据：只按牌名筛、没筛别的维度。描述读它决定英文的单复数
    /// （"every copy of Defend" 是单数、"Attack cards" 是复数），
    /// "免费 / 增伤 / 重放"那几句也据此选模板。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsNamedPick =>
        CardIds.Count > 0 &&
        Types.Count == 0 && Rarities.Count == 0 && Tags.Count == 0 && Keywords.Count == 0 &&
        EnergyComparison == CostComparison.None && StarComparison == CostComparison.None;

    /// <summary>
    /// 流程二那格里**唯一的**那个牌堆；没选（空 = 全部战斗牌堆）时给手牌。
    ///
    /// 给"只认一个牌堆"的效果用（自动打出、以及生成代码里需要单数牌堆的地方）——
    /// 它们的策略把流程二限成单选，所以"空"只可能是还没填，不是"全部"。
    /// </summary>
    public PileKind SourcePile() => Piles.Count > 0 ? Piles[0] : PileKind.Hand;

    /// <summary>这条配置的副本（编辑界面时会复制一份改，避免改到别处共享的那份）。</summary>
    public CardPick Clone() => new()
    {
        Source = Source,
        SetId = SetId,
        Piles = new List<PileKind>(Piles),
        Types = new List<CardType>(Types),
        Rarities = new List<CardRarity>(Rarities),
        CardIds = new List<string>(CardIds),
        Tags = new List<CardTag>(Tags),
        Keywords = new List<CardKeyword>(Keywords),
        EnergyComparison = EnergyComparison,
        StarComparison = StarComparison,
        Mode = Mode,
        Count = new NumericValue(Count.Base, Count.Variable.Clone()),
    };
}
