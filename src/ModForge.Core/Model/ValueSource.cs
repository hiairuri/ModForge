namespace ModForge.Core.Model;

/// <summary>
/// 计算型数值的来源，也就是"乘以多少单位"里的那个单位数量。
///
/// 对应原版 <c>CalculatedVar</c> 的可变乘数部分（<c>WithMultiplier</c> 的返回值）。
/// </summary>
public enum ValueSourceKind
{
    /// <summary>不使用计算，只用固定数值。</summary>
    None,

    /// <summary>
    /// 某个状态的层数。**哪个状态由 <see cref="ValueSource.PowerId"/> 给出**。
    ///
    /// 早先每个原版状态各占一个枚举项（力量 / 敏捷 / 中毒……共 18 条），那个列表既长又永远
    /// 不全——本工程自定义的状态、以及游戏后续新增的状态都进不去。现在只有这一条，
    /// 状态从界面上的状态选择框里挑，原版与本工程的状态都在同一张表里。
    /// </summary>
    PowerStacks,

    // ---- 生物基础属性 ----
    CurrentHp,
    MaxHp,
    MissingHp,
    Block,

    // ---- 玩家资源 ----
    Energy,
    Stars,
    Gold,

    /// <summary>
    /// X 费卡实际消耗的能量（对应游戏的 <c>ResolveEnergyXValue</c>）。
    ///
    /// X 费卡打出时能量已被扣光，所以 <see cref="Energy"/> 读到的是 0；要取"花了多少"
    /// 只能用这个。它还包含"化学 X"这类遗物的修正。
    /// </summary>
    EnergyX,

    /// <summary>X 费卡实际消耗的星辰（对应 <c>ResolveStarXValue</c>）。</summary>
    StarsX,

    // ---- 牌堆张数 ----
    HandCount,
    DrawPileCount,
    DiscardPileCount,
    ExhaustPileCount,
    AllPilesCount,

    /// <summary>当前回合数，从 1 开始。</summary>
    TurnNumber,

    // ---- 战斗历史计数 ----
    //
    // 读游戏的 CombatManager.Instance.History（原版"完美打击""刀刃陷阱"这类牌用的就是它）。
    // 它只能数出"发生了多少次"，**数不出类型等细节**——例如"本回合打出的牌数"是全体，
    // 不是"本回合打出的攻击牌数"；后者要按条件过滤，我们暂时没有对应来源。
    // "本回合"与"本场战斗"是两套统计口径，两者都提供。
    //
    // 完整统计见 CardPlaysFinished / Entries（原版还用它数"被消耗的牌数""生成的牌数"等），
    // 需要时按同样的方式往下加即可。

    /// <summary>本回合打出的牌数（含所有类型）。</summary>
    CardsPlayedThisTurn,

    /// <summary>本场战斗打出的牌数。</summary>
    CardsPlayedThisCombat,

    /// <summary>本回合抽到的牌数（不含回合开始的起手抽牌）。</summary>
    CardsDrawnThisTurn,

    /// <summary>本场战斗抽到的牌数。</summary>
    CardsDrawnThisCombat,

    /// <summary>
    /// 某个牌堆里**符合条件**的牌数（用 <see cref="ValueSource.CountFilter"/> 与
    /// <see cref="ValueSource.CountScope"/> 指定条件与范围）。
    ///
    /// 原版这类牌不少，用的都是同一形状：`某牌堆.Cards.Count(c => 条件)`。
    /// 例如"完美打击"数所有牌堆里的打击牌、"飞镖"数手牌里的技能牌。
    ///
    /// 注意原版中文描述会写成"名字中含有『打击』的牌"，但那只是措辞——代码里判的是
    /// **卡牌标签**（<c>CardTag</c>），不是名字。按名字判反而不可靠（改名/换语言就变）。
    /// </summary>
    FilteredCardCount,

    /// <summary>
    /// 本回合**失去过几次生命**（原版"扯碎"用的就是它）。
    ///
    /// 判据照抄原版：数战斗历史里的 <c>DamageReceivedEntry</c>，要求接收者是自己、
    /// 且 <c>Result.UnblockedDamage &gt; 0</c>——也就是"这一次真的扣到血了"，被格挡挡光的
    /// 那几次不算。它同时涵盖"受到伤害"与"失去生命"两种来源，因为后者
    /// （<see cref="EffectKind.LoseHp"/>）在引擎里也走同一条扣血路径。
    ///
    /// 原版中文描述写的是"每失去过一次生命值"，代码判的是上面那个条件，
    /// 我们照代码来，措辞按玩家的读法写，两者一致。
    /// </summary>
    HpLostThisTurn,

    /// <summary>本场战斗失去过几次生命。判据同 <see cref="HpLostThisTurn"/>，只是不限回合。</summary>
    HpLostThisCombat,

    // ---- 战斗历史计数（第二批）：受击 / 攻击 / 消耗 / 弃置 / 生成 / 伤害量 ----
    //
    // 与上面同一套口径：只数得出"发生了多少次 / 累计多少量"，数不出类型等细节；
    // 本回合与本场战斗各一个来源。

    /// <summary>
    /// 本回合**被击中**的次数：数自己收到的所有伤害事件，**被格挡挡光的也算**。
    ///
    /// 与 <see cref="HpLostThisTurn"/> 的区别就在这一条口径上：每次掉血必然是一次受击，
    /// 但被完全格挡的那几下是"受击而没掉血"。原版对照："复仇"类按 UnblockedDamage&gt;0
    /// 数的是掉血（见 <see cref="HpLostThisTurn"/>），受击口径用于"每被击中一次"这类描述。
    /// </summary>
    HitsTakenThisTurn,

    /// <summary>本场战斗被击中的次数。判据同 <see cref="HitsTakenThisTurn"/>，只是不限回合。</summary>
    HitsTakenThisCombat,

    /// <summary>
    /// 本回合**自己发起攻击**的次数。数的是战斗历史里的攻击条目——一次攻击动作算一次
    /// （多段攻击是同一个动作的多次伤害，只算一次）。不含奥斯提的攻击（它是独立的生物，
    /// 原版"猛晃"数的就是奥斯提的攻击，与我们这个来源是两回事）。
    /// </summary>
    AttacksThisTurn,

    /// <summary>本场战斗自己发起攻击的次数。口径同 <see cref="AttacksThisTurn"/>。</summary>
    AttacksThisCombat,

    /// <summary>本回合**自己消耗**的牌数。原版"邪眼""遗忘仪式"用的就是这个口径
    /// （数战斗历史里 Card.Owner 是自己的消耗条目）。</summary>
    CardsExhaustedThisTurn,

    /// <summary>本场战斗自己消耗的牌数。口径同 <see cref="CardsExhaustedThisTurn"/>。</summary>
    CardsExhaustedThisCombat,

    /// <summary>本回合**自己弃置**的牌数。原版"铭记死亡"用的就是这个口径
    /// （数战斗历史里 Card.Owner 是自己的弃置条目）。</summary>
    CardsDiscardedThisTurn,

    /// <summary>本场战斗自己弃置的牌数。口径同 <see cref="CardsDiscardedThisTurn"/>。</summary>
    CardsDiscardedThisCombat,

    /// <summary>本回合**自己生成**的牌数（含变化、临时生成）。原版"超大质量体"用的就是这个口径
    /// （数战斗历史里 Creator 是自己的生成条目）。</summary>
    CardsGeneratedThisTurn,

    /// <summary>本场战斗自己生成的牌数。口径同 <see cref="CardsGeneratedThisTurn"/>。</summary>
    CardsGeneratedThisCombat,

    /// <summary>
    /// 本回合**受到的伤害总量**：把自己收到的每次伤害里真正扣到血的部分加起来
    /// （与 <see cref="HpLostThisTurn"/> 同一口径——被格挡挡掉的部分不算）。
    /// 它是"失去生命的次数"的量版：那个数次数，这个数总量。
    /// </summary>
    DamageTakenThisTurn,

    /// <summary>本场战斗受到的伤害总量。口径同 <see cref="DamageTakenThisTurn"/>。</summary>
    DamageTakenThisCombat,

    /// <summary>
    /// 本回合**自己造成的伤害总量**：把自己是伤害来源的每次伤害里真正扣到血的部分加起来
    /// （被格挡挡掉的不算）。
    /// </summary>
    DamageDealtThisTurn,

    /// <summary>本场战斗自己造成的伤害总量。口径同 <see cref="DamageDealtThisTurn"/>。</summary>
    DamageDealtThisCombat,

    // ---- 自制全局计数（恢复生命）----
    //
    // 游戏的战斗历史里没有治疗条目，CreatureCmd.Heal 也不发战斗钩子——所以"恢复过几次 /
    // 恢复了多少"没法从游戏数据里查。这一组由我们自己的全局计数器（Runtime/HealTracker.cs，
    // Harmony 观察每次治疗）维护，对外呈现与上面的历史计数同一格式：本回合 / 本场战斗各一档。

    /// <summary>
    /// 本回合**恢复生命**的次数（自己被治疗、且真的恢复了生命的事件数；满血时的治疗不算）。
    /// 由我们的全局计数器维护（游戏没有治疗历史），见 <see cref="HealsThisCombat"/>。
    /// </summary>
    HealsThisTurn,

    /// <summary>
    /// 本场战斗恢复生命的次数。
    ///
    /// 这是四个"恢复生命"来源的机制说明（次数/总量 × 本回合/本场战斗共用）：
    /// 生成 mod 里带一个全局计数器（Runtime/HealTracker.cs），用 Harmony 观察每一次治疗，
    /// 按生物记下（战斗、当时回合数、实际恢复量）；查询时按"同一战斗 + 当前回合数"筛。
    /// 只统计**实际恢复**的量——满血时的治疗不改变生命，不计入。
    /// </summary>
    HealsThisCombat,

    /// <summary>本回合恢复的生命总量（实际恢复的部分，满血时的过量治疗不计）。</summary>
    HealAmountThisTurn,

    /// <summary>本场战斗恢复的生命总量。口径同 <see cref="HealAmountThisTurn"/>。</summary>
    HealAmountThisCombat,
}

/// <summary>"符合条件的牌"从哪个范围里数。</summary>
public enum CountScope
{
    /// <summary>所有战斗牌堆（手牌 + 抽牌堆 + 弃牌堆 + 消耗堆 + 出牌堆）。</summary>
    AllPiles,

    Hand,
    DrawPile,
    DiscardPile,
    ExhaustPile,
}

/// <summary>来源值取自哪一方。</summary>
public enum ValueSourceActor
{
    Self,
    Target,
    AllEnemies,
    AllAllies,
}

/// <summary>
/// 一个计算型数值：结果 = 基础值 + 变量 × 变量系数。
///
/// 与原版 <c>CalculatedVar</c> 一一对应（<c>CalculationBaseVar</c> 是基础值，
/// <c>CalculationExtraVar</c> 承载变量系数，乘数就是这里的变量本身），因此编辑器能表达的
/// 计算与原版手写代码能表达的完全一致。
///
/// 例子（格式：基础值 + 变量 × 变量系数）：
///   获得 等于 自身 力量 层数 的力量   -> 0 + 自身力量 × 1
///   恢复 等于 目标 格挡 ×2 的 HP      -> 0 + 目标格挡 × 2
///   对 所有人 造成 等于 手牌数 的伤害  -> 0 + 手牌数 × 1
///   消耗 等于 手牌数 ×0.5 张 抽牌堆的牌 -> 0 + 手牌数 × 0.5
///   造成 5 + 等于力量 的伤害          -> 5 + 自身力量 × 1
///
/// **这里只有数据**：这个变量要"从哪儿读、叫什么名字"在变量来源模块里
/// （<c>Modules/ValueSources/SourceCatalog</c>，每个种类一行；计数那三十种再转给计数模块）。
/// 早先这两件事长在本文件上（一份三十个枚举项的手写名单 + 一段名字 switch），
/// 与取值器里另外三份判断必须互相对得上——漏改一处不报错，只会静默读到另一侧的数。
/// </summary>
public sealed class ValueSource
{
    public ValueSourceActor Actor { get; set; } = ValueSourceActor.Self;

    /// <summary>
    /// 这个数值把哪个值当作**变量**（手牌数 / 状态层数 / 战斗历史计数…）。
    /// 「来源」指的是把值传给变量的那个东西，它本身不属于这条效果。
    /// </summary>
    public ValueSourceKind Kind { get; set; } = ValueSourceKind.None;

    /// <summary>
    /// 变量系数：变量每 1 点对应多少数值。支持小数，因为原版存在 0.5 这类系数（如"一半手牌"）。
    /// </summary>
    public decimal PerUnit { get; set; } = 1m;

    /// <summary>
    /// 只对 <see cref="ValueSourceKind.FilteredCardCount"/> 有意义：数**哪些**牌（多维筛选，
    /// 与选牌模块同一套条件与同一个谓词生成处）。
    ///
    /// 阈值是**这里自己的常数**（<see cref="CardFilter.EnergyThreshold"/>），不是效果级的
    /// 三件套：生成的是一句 <c>Count(c =&gt; …)</c>，lambda 里引用不到外面算好的局部变量。
    /// 所以"能量消耗大于 手牌数 的牌有几张"表达不了——那是选牌那侧的能力。
    /// </summary>
    public CardFilter Filter { get; set; } = new();

    /// <summary>只对 <see cref="ValueSourceKind.FilteredCardCount"/> 有意义：从哪个范围数。</summary>
    public CountScope CountScope { get; set; } = CountScope.AllPiles;

    /// <summary>
    /// 只对 <see cref="ValueSourceKind.PowerStacks"/> 有意义：读哪个状态的层数。
    /// 模型 id 形式（如 <c>POWER.STRENGTH_POWER</c>），原版与本工程的状态都行。
    /// </summary>
    public string? PowerId { get; set; }

    public bool IsUsed => Kind != ValueSourceKind.None;

    /// <summary>
    /// 两份来源是不是**同一套算式**——比种类、取自谁、哪个状态、以及按条件数牌的条件与范围。
    ///
    /// **不比变量系数**：系数由变量承载（费用阈值那两个甚至是独立变量），升级或逐级改它
    /// 只要写那个变量就够了，不需要按等级分支。所以"第 1 级只是系数从 2 变 3"不会多出分支代码。
    ///
    /// "0 级按手牌数、1 级按弃牌堆数"这类**换算式**才需要分支，生成器据此决定要不要
    /// 按等级分别发代码——两处（效果数值、费用阈值）共用这一份判据，免得各自漏比某一项
    /// （漏了不报错，只是那一级的算式被静默忽略）。
    /// </summary>
    public bool SameAs(ValueSource other) =>
        Kind == other.Kind
        && Actor == other.Actor
        && PowerId == other.PowerId
        && CountScope == other.CountScope
        && Filter.SameAs(other.Filter);

    public ValueSource Clone() => new()
    {
        Actor = Actor,
        Kind = Kind,
        PerUnit = PerUnit,
        Filter = Filter.Clone(),
        CountScope = CountScope,
        PowerId = PowerId,
    };
}
