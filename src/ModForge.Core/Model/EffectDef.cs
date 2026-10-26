using System.Text.Json.Serialization;

namespace ModForge.Core.Model;

/// <summary>效果种类。</summary>
public enum EffectKind
{
    // ---- 打出类 ----
    DealDamage,
    GainBlock,
    DrawCards,
    GainEnergy,
    GainStars,
    Heal,
    LoseHp,

    /// <summary>
    /// 提高最大生命（并回复等量的当前生命）。
    ///
    /// 对应 <c>CreatureCmd.GainMaxHp</c>（原版"狂宴"斩杀后永久加最大生命用的就是它）。
    /// 这个改动是**战斗之外也保留**的——它动的是玩家的最大生命值本身，不是战斗内的临时数值，
    /// 所以与"永久增伤"那类需要存档字段的效果不同，不需要额外的存档支持。
    ///
    /// 目标只能是自己或队友：给敌人加最大生命通常不是想要的（真有需要也能选，不拦）。
    /// </summary>
    GainMaxHp,

    /// <summary>
    /// 降低最大生命（对应 <c>CreatureCmd.LoseMaxHp</c>）。
    ///
    /// 当前生命高于新上限时，超出的部分会作为**不可格挡、不受增伤**的伤害扣掉
    /// （引擎自己的行为，见 <c>LoseMaxHp</c>）。
    /// </summary>
    LoseMaxHp,

    // ---- 状态类 ----
    ApplyPower,
    RemovePower,

    // ---- 牌堆操作 ----
    /// <summary>
    /// 把牌的若干**复制品**加入某个牌堆。对应
    /// <c>CardPileCmd.AddGeneratedCardToCombat(card.CreateClone(), …)</c>。
    ///
    /// 复制**哪些牌**由"作用对象"（<see cref="EffectDef.PickScope"/>）决定，与其它选牌器一致：
    ///   某个牌堆   —— 该牌堆里取若干张，各复制若干份
    ///   指定牌名 / 按条件 —— 匹配到的牌各复制若干份
    ///   此牌       —— **这张牌自己**的复制品（原版"双持"这类"复制一张牌"的效果）
    ///
    /// <see cref="EffectDef.Amount"/> 是**每张**复制几份。目的地是
    /// <see cref="EffectDef.TargetPile"/>（不是 <see cref="EffectDef.Pile"/>——后者在
    /// "作用对象 = 某个牌堆"时是**来源**牌堆）。
    /// </summary>
    CopyCardsToPile,

    /// <summary>把指定卡牌的若干张加入某个牌堆。</summary>
    AddNamedCardToPile,

    /// <summary>把牌**消耗**掉（从所在牌堆直接移进消耗堆）。
    ///
    /// **消耗哪些牌由"作用对象"（<see cref="EffectDef.PickScope"/>）决定**，与其它用选牌器的
    /// 效果一致（原版"坚毅"从手牌里挑一张消耗、诅咒牌消耗自己）：
    ///   某个牌堆   —— 从该牌堆里取若干张（"全部"就是整手牌）
    ///   指定牌名   —— 该牌的所有副本
    ///   按条件     —— 符合条件的牌
    ///   此牌       —— **这张牌自己**
    ///
    /// 早先"消耗这张牌"与"消耗牌堆中的牌"是两条独立的效果，各自的"动哪些牌"写法不同；
    /// 选牌器统一之后"此牌"只是作用对象的一档，两条合并成这一条——与"在牌堆间移动牌"
    /// 吸收掉"将此牌置于牌堆"是同一件事（那次也是这么合的）。
    ///
    /// 张数走选牌器的流程十（<see cref="EffectDef.Count"/>），不是数值：与"在牌堆间移动牌"
    /// 一致。**注意**这条效果因此没有 <see cref="EffectDef.Amount"/>，也就没有可引用的变量
    /// ——"消耗等于手牌数的牌"这类写法要等选牌器的张数接上三件套之后才做得到
    /// （见 README 的已知缺口）。
    ///
    /// 消耗不是弃牌：<c>CardPileCmd.Add(card, PileType.Exhaust)</c> 只是把它挪进消耗堆，
    /// 不会走弃牌那条流程（那边的语义见 <see cref="DiscardCardsInPile"/>）。
    /// </summary>
    ExhaustCards,

    /// <summary>丢弃某个牌堆中的若干张牌。</summary>
    DiscardCardsInPile,

    // ---- 能量球（故障机器人）----

    /// <summary>引导一个能量球。</summary>
    ChannelOrb,

    /// <summary>激发最前面若干个能量球。</summary>
    EvokeOrbs,

    /// <summary>获得若干能量球槽位。</summary>
    AddOrbSlots,

    // ---- 召唤 ----

    /// <summary>召唤（亡灵契约师的召唤物）。数值是它的生命值。</summary>
    Summon,

    // ---- 铸造（储君）----

    /// <summary>
    /// 铸造（储君体系）。对应 <c>ForgeCmd.Forge(数量, 玩家, 来源)</c>，它的行为一次到位：
    ///
    ///   1. 战斗里还没有"君王之剑"时，先生成一张加入手牌；
    ///   2. 把数量加到**所有**君王之剑的伤害上（含消耗堆里的副本）。
    ///
    /// 数值就是"铸造 N"的那个 N。原版形状（"淬锋"RefineBlade）：声明 <c>ForgeVar(N)</c>、
    /// OnPlay 调用、升级改那个变量——我们照搬。多个铸造效果各占一个槽（Forge / Forge2…）。
    /// 目标固定是自己；数量为 0 时会生成君王之剑但不加伤（多半是填错了，校验器会提醒）。
    /// </summary>
    Forge,

    // ---- 附魔 ----

    /// <summary>给某个牌堆中的若干张牌附魔。</summary>
    EnchantCardsInPile,

    // ---- 伤害增益 ----

    /// <summary>
    /// 让**指定的那些牌**的伤害增加。作用于哪些牌由选牌器决定
    /// （<see cref="EffectDef.PickScope"/>：某个牌堆 / 指定牌名 / 按条件 / 这张牌自己），
    /// 所以"给这张牌自己加伤害"与"给某类牌加伤害"是**同一条效果**，不必分开。
    ///
    /// 实现分两种持续范围（<see cref="EffectDef.CostDuration"/>）：
    ///
    /// **本场战斗** —— 直接改那些牌的伤害值（原版爪击 Claw / 撕咬 Maul 的做法）。
    /// 爪击自己的说明就是"本场战斗中所有爪击卡牌的伤害增加 2 点"，做法就是先正常造成
    /// 伤害、再对自己牌堆里每一张爪击加一次值。改的是牌自己的数值，所以与"谁在造成伤害"
    /// 无关——这也是它比走状态更好的地方（状态要挂在出伤者身上，目标选敌人时永远不匹配）。
    /// 战斗中的牌是临时副本，战斗结束自然失效。
    ///
    /// **永久** —— 写进目标自己身上的存档字段（<c>SavedProperty</c>），跨战斗保留。
    /// 这里有一条硬约束：**只有本 mod 自己生成的卡牌带那个字段**，所以永久增益只能作用
    /// 于本 mod 的卡牌。原版卡牌的类我们改不了，而游戏的存档格式是封闭的——它只存
    /// "卡牌 id + 升级等级 + 该类自己声明的 SavedProperty + 附魔"，动态数值（DynamicVars）
    /// **不进存档**，读档时是按标准数值重建的。所以对原版卡牌"永久改数值"没有可行路径；
    /// 要永久强化原版卡牌，用已有的「升级牌堆中的牌」或「附魔牌堆中的牌」两条效果
    /// （这也正是游戏自己的做法：升级与附魔都是持久化的）。
    /// </summary>
    BoostCardDamage,

    // ---- 灾厄 ----

    /// <summary>
    /// 立刻对目标做一次灾厄判定：生命值已不高于灾厄层数的目标当场死亡。
    ///
    /// 对应游戏里 <c>DoomPower.DoomKill</c>（原版"末日"用的就是它）。平时灾厄只在
    /// 对方回合结束时判定，这条效果让它现在立刻判定。
    /// </summary>
    TriggerDoom,

    // ---- 自动打出 ----

    /// <summary>
    /// 自动打出。打出**哪些牌**由"作用对象"（<see cref="EffectDef.PickScope"/>）决定，
    /// 与"使牌的伤害增加"共用同一套选牌器：
    ///
    ///   某个牌堆   —— 从该牌堆里自动打出 <see cref="EffectDef.Amount"/> 张
    ///                    （原版"浩劫"：打出抽牌堆顶并消耗）。任何时机都能用。
    ///   这张牌自己 —— **这张牌**从牌堆里出来，牌堆是"它必须在哪个牌堆里"这个条件
    ///                    （原版"轰击"：回合开始时若在消耗堆中则打出）。走引擎专门的
    ///                    自动打出手时机，所以时机必须是回合开始/结束。
    ///
    /// 两者对应同一个游戏接口 <c>CardCmd.AutoPlay</c>，区别只是把它作用在哪张牌上。
    /// 早先它们是两个独立的效果种类，选牌器统一后合并成一个。
    /// </summary>
    AutoPlay,

    // ---- 费用类 ----

    /// <summary>
    /// 降低或提高某个牌堆中若干张牌的费用（能量）。
    ///
    /// 对应游戏的 <c>CardEnergyCost.Add*</c> 一族，按持续范围选具体方法：
    /// 本回合 <c>AddThisTurnOrUntilPlayed</c>、本场战斗 <c>AddThisCombat</c>、
    /// 直到打出 <c>AddUntilPlayed</c>。数值为正即提高、为负即降低。
    ///
    /// **只碰能量**，不动星辰——这与 <see cref="SetCardsFree"/> 的区别是刻意的：
    /// 游戏里"降费"和"免费"本来就是两套接口，后者会同时把能量与星辰清成 0。
    /// 原版"子弹时间"（本回合手牌费用变 0）、"启蒙"（降到 1）用的都是这一族。
    ///
    /// X 费牌没有固定费用，会被跳过（原版"子弹时间"也是这么做的）。
    /// </summary>
    ModifyCardCost,

    /// <summary>
    /// 让某个牌堆中若干张牌**可以免费打出**。
    ///
    /// 对应 <c>CardModel.SetToFreeThisTurn()</c> / <c>SetToFreeThisCombat()</c>。
    /// 注意这两个接口**同时**把能量费用与星辰费用清成 0，而单纯降费只碰能量——
    /// 所以界面上与 <see cref="ModifyCardCost"/> 分开呈现，措辞也分开
    /// （"可以免费打出" vs "费用降低 N 点"）。
    ///
    /// 原版"子弹时间"（BulletTime）用的就是它。
    /// </summary>
    SetCardsFree,

    // ---- 改造牌堆里的牌 ----

    /// <summary>
    /// 把某个牌堆中若干张牌**升级**。
    ///
    /// 对应 <c>CardCmd.Upgrade</c>。它与别的选牌效果只多一条要求：**排除不可升级的牌**——
    /// 已经升满的牌再升没有意义。游戏自己会在 <c>CardCmd.Upgrade</c> 里跳过它们
    /// （<c>if (!card.IsUpgradable) continue;</c>），所以我们**不**在选牌时过滤，
    /// 而是直接交给它处理：这样"能多次升级的牌"（<c>MaxUpgradeLevel &gt; 1</c>，
    /// 原版有些诅咒牌如此，mod 也可能）会自然升到下一级，而不是被我们误判成"已升满"。
    ///
    /// 原版"神化"（升级全部手牌）、"武装"（选择一张升级）用的都是它。
    /// </summary>
    UpgradeCardsInPile,

    /// <summary>
    /// 给牌**增加一次"重放"**：这张牌下次被打出时，效果再执行 N 次。
    ///
    /// 对应 <c>CardModel.BaseReplayCount</c>（原版"隐藏宝石"给抽牌堆一张牌 +2 次重放、
    /// "变形"给选中的一张牌 +1 次）。它与"重复次数"（<see cref="EffectDef.Repeat"/>）不同：
    ///   重复次数     —— 这张牌**这一条效果**在本次打出时循环 N 次；
    ///   重放         —— 这张牌**整张牌**被打出 N 次（每次都是完整的出牌流程）。
    ///
    /// 哪些牌由"作用对象"决定（某个牌堆 / 指定牌名 / 按条件 / 此牌），与其它选牌效果一致。
    ///
    /// **持续范围**（<see cref="EffectDef.CostDuration"/>）：
    ///   ThisCombat —— 直接加 <c>BaseReplayCount</c>，本场战斗有效（战斗中的牌是临时副本）。
    ///   Permanent  —— 写进目标牌自己的 [SavedProperty] 字段，跨战斗保留；
    ///                 所以**只有本 mod 生成的牌**能永久加重放（改不了原版牌的类）。
    /// </summary>
    AddReplay,

    /// <summary>
    /// 把某个牌堆中若干张牌**变化**成别的牌。
    ///
    /// 对应 <c>CardCmd.TransformToRandom</c> / <c>CardCmd.TransformTo&lt;T&gt;</c> /
    /// <c>CardCmd.Transform(original, replacement)</c> 三个入口，由
    /// <see cref="TransformInto"/> 决定走哪个：
    ///
    ///   Random  —— 随机变化。**不是"变成任意一张"**，而是游戏自己的变化池：
    ///              同稀有度（原版 rare 及以上的牌放宽到 common~rare）、排除自己、
    ///              战斗中还要能战斗中生成（原版"降灵"变出的"魂"就是战斗中生成的）。
    ///              我们不去复刻那个池子，直接调游戏接口，池子随游戏版本走。
    ///   Specific —— 变成指定的一张（可以指定升级版）。
    ///
    /// 原版"冲锋！！"（把抽牌堆若干张变成小卒突袭）用的是指定牌那条；"降灵"用随机。
    ///
    /// 注意"变化"与"加入卡牌"是两件事：变化会把原来的牌替换掉，而且**不可变化的牌
    /// 会被排除**（例如带"永恒"关键词的）。原版没有"从三张随机牌里选一张变化"这种用法，
    /// 那个三选一（<c>CardSelectCmd.FromChooseACardScreen</c>）是**加入**卡牌用的。
    /// </summary>
    TransformCardsInPile,

    /// <summary>
    /// 把牌**移动到另一个牌堆**。对应 <c>CardPileCmd.Add(card, 目标牌堆, 位置)</c>。
    ///
    /// 移动**哪些牌**由"作用对象"（<see cref="EffectDef.PickScope"/>）决定，与其它用选牌器的
    /// 效果一致：
    ///   某个牌堆   —— 从该牌堆取若干张（原版"头槌"：弃牌堆里选一张放到抽牌堆顶）
    ///   指定牌名   —— 该牌的所有副本
    ///   按条件     —— 符合条件的牌
    ///   此牌       —— **这张牌自己**（原版"深挖"：把这牌放回抽牌堆顶）
    ///
    /// 早先"此牌"那一档是一条独立的效果（<c>MoveThisCardToPile</c>"将此牌置于牌堆"），
    /// 与这条重叠——两者都是 <c>CardPileCmd.Add</c>，只是"搬谁"不同，所以合并成一条。
    /// </summary>
    MoveCardsBetweenPiles,
}

/// <summary>变化成什么。</summary>
public enum TransformInto
{
    /// <summary>随机变化（用游戏自己的变化池，随游戏版本走）。</summary>
    Random,

    /// <summary>变成指定的某一张牌。</summary>
    SpecificCard,
}

/// <summary>
/// "加入指定卡牌"的**生成方式**。两个模式对应两种出码路径：
///
///   Named —— 加**指定的那张**：牌名（+ 可选升级版）写死在效果里，
///            原版"愤怒"（把这张牌的复制品加入弃牌堆）的写法。
///   ChooseFromPool —— 从**卡池**里按多维筛选（稀有度 / 类型 / 标签 / 关键词 / 牌 ID /
///            能量与星辰消耗）筛出候选，随机取 N 张：
///            N = 1 时直接加入（"随机生成一张攻击牌"），
///            N ≥ 2 时弹游戏自己的"三选一"式选牌界面（<c>CardSelectCmd.FromChooseACardScreen</c>，
///            原版"发现"就是这么做的）让玩家选一张加入。
///
/// N 选一的候选数**上限是 3**：游戏那条接口候选超过 3 张直接抛异常（不是降级），
/// 所以校验器会拦。卡池是**出牌者自己的角色池**（含 mod 加进该池的牌）；游戏对
/// "战斗中生成"自有一道门槛（<c>CardFactory.FilterForCombat</c>：排除基础 / 远古 / 事件
/// 稀有度与声明了不可生成的牌），我们走原版工厂方法，这道门槛自动生效。
/// </summary>
public enum AddCardMode
{
    /// <summary>加入指定的那张牌（牌名 + 可选升级版）。</summary>
    Named,

    /// <summary>按筛选从卡池随机取 N 张：1 张直接加入，多张弹"三选一"式界面选一张。</summary>
    ChooseFromPool,
}

/// <summary>
/// 条件判断的**比较方式**（见 <see cref="EffectCondition"/>）。
///
/// 六档都是"左值怎么与右值比"。没有"是 X"那一档——那是费用筛选特有的
/// （X 费牌没有固定费用可读），条件比较的是两个数值，永远有确定的值可比。
/// </summary>
public enum ConditionComparison
{
    /// <summary>不小于（&gt;=）。</summary>
    AtLeast,

    /// <summary>不大于（&lt;=）。</summary>
    AtMost,

    /// <summary>大于（&gt;）。</summary>
    GreaterThan,

    /// <summary>小于（&lt;）。</summary>
    LessThan,

    /// <summary>等于（==）。</summary>
    Equal,

    /// <summary>不等于（!=）。</summary>
    NotEqual,
}

/// <summary>
/// 一条效果的**启用条件**：把一个变量三件套与另一个变量三件套比较
/// （<c>左值 = 左基础值 + 左来源 × 左系数</c>，右值同理），**满足时这条效果才执行**。
///
/// 两侧各自是一个完整的三件套（<see cref="Modules.Variables.NumericValue"/> 的形状：
/// 基础值 + 变量来源 × 变量系数），与效果数值、次数同一套读法——所以"若手牌数不小于 3"、
/// "若 2 × 星辰 大于 能量"都写得出来，来源也用同一张表（手牌数 / 星辰 / 状态层数 / 按条件数牌…）。
///
/// **升级**：四个增量字段（两侧各自的基础值增量与系数增量）让条件本身能升级——
/// "手牌数不小于 3"升级后变"不小于 4"。条件不在等级表机器里（<see cref="EffectState"/>
/// 只合成数值 / 次数 / 两个费用阈值），所以多级升级对条件是**每级按同样的增量走**；
/// 要逐级不同的条件值，现在表达不了（校验器/文档会说明）。
///
/// **出码**：不需要变量槽——两侧的数存在生成类的私有字段里（与"就地计算"的数值同一套，
/// 见 <c>PendingConditionMethod</c>），守卫是整条效果外面的一层 <c>if</c>。
/// </summary>
public sealed class EffectCondition
{
    /// <summary>怎么比。默认"不小于"（最常见的条件写法）。</summary>
    public ConditionComparison Comparison { get; set; } = ConditionComparison.AtLeast;

    /// <summary>左值的基础值。</summary>
    public int LeftAmount { get; set; }

    /// <summary>左值的变量来源（选「无」= 固定值）。</summary>
    public ValueSource LeftSource { get; set; } = new();

    /// <summary>右值的基础值。默认 1——"不小于 1"是最常见的启用条件（"至少有一张"）。</summary>
    public int RightAmount { get; set; } = 1;

    /// <summary>右值的变量来源。</summary>
    public ValueSource RightSource { get; set; } = new();

    // ---- 升级增量（每升一级加多少；条件不在等级表里，多级也是同样的增量）----

    /// <summary>左值基础值的增量。</summary>
    public int LeftUpgrade { get; set; }

    /// <summary>左值变量系数的增量（只对"左值选了来源"有意义）。</summary>
    public decimal LeftPerUnitUpgrade { get; set; }

    /// <summary>右值基础值的增量。</summary>
    public int RightUpgrade { get; set; }

    /// <summary>右值变量系数的增量。</summary>
    public decimal RightPerUnitUpgrade { get; set; }

    /// <summary>某一侧的三件套（出码与描述都读它）。</summary>
    public Modules.Variables.NumericValue SideOf(bool left) => left
        ? new Modules.Variables.NumericValue(LeftAmount, LeftSource)
        : new Modules.Variables.NumericValue(RightAmount, RightSource);

    /// <summary>某一侧需不需要"字段 + 取值方法"（选了来源，或填了任何一项升级增量）。
    /// 都没填时这一侧就是字面量，不需要字段。</summary>
    public bool SideNeedsField(bool left) => left
        ? LeftSource.IsUsed || LeftUpgrade != 0 || LeftPerUnitUpgrade != 0
        : RightSource.IsUsed || RightUpgrade != 0 || RightPerUnitUpgrade != 0;

    /// <summary>深复制（来源各自复制，与 <see cref="EffectDef.Clone"/> 同一个道理）。</summary>
    public EffectCondition Clone() => new()
    {
        Comparison = Comparison,
        LeftAmount = LeftAmount,
        LeftSource = LeftSource.Clone(),
        RightAmount = RightAmount,
        RightSource = RightSource.Clone(),
        LeftUpgrade = LeftUpgrade,
        LeftPerUnitUpgrade = LeftPerUnitUpgrade,
        RightUpgrade = RightUpgrade,
        RightPerUnitUpgrade = RightPerUnitUpgrade,
    };
}

/// <summary>费用调整的持续范围。对应游戏的 CostModifier 过期时机。</summary>
public enum CostDuration
{
    /// <summary>本回合（引擎里是"本回合结束或直到打出"，取先到者）。</summary>
    ThisTurn,

    /// <summary>本场战斗。</summary>
    ThisCombat,

    /// <summary>直到这张牌被打出。只有"降低/提高费用"有这一项。</summary>
    UntilPlayed,

    /// <summary>
    /// 永久（写进存档，跨战斗保留）。
    ///
    /// 费用类没有这一项——游戏里持久化费用改动的接口只有"升级"（<c>CostsLess</c> 不是
    /// 存档字段，动态数值一律不存档）。伤害增益有这一项，见 <see cref="EffectKind.BoostCardDamage"/>。
    /// </summary>
    Permanent,
}

/// <summary>
/// 这些"改造一批牌"的效果作用于哪些牌。
///
/// 前三种从某个牌堆里取；最后一种是**这张牌自己**——原版两种写法都有
/// （"武装"从手牌里挑一张升级、"王者之踢"把自身的费用降 1），所以同一套选择器
/// 两种都提供，而不是给"作用于自己"单开一条效果。
/// </summary>
public enum CardPickMode
{
    /// <summary>该牌堆里的全部牌。</summary>
    All,

    /// <summary>随机若干张（用游戏的战斗随机数）。</summary>
    Random,

    /// <summary>由玩家选择若干张（用游戏的选择界面）。</summary>
    Choose,
}

/// <summary>
/// 这些"改造一批牌"的效果**作用于什么**。
///
/// 与 <see cref="CardPickMode"/> 是两级选择，顺序不能颠倒：
///
///   先选作用对象（本枚举）——
///     Pile         从某个牌堆里取，这时才谈得上"怎么取"（再选 <see cref="CardPickMode"/>）
///     SpecificCard 指定牌名的所有副本（按卡牌 id 匹配，扫全部战斗牌堆）
///     ThisCard     只有这张牌自己（原版"王者之踢"改自身费用、"自省"升级自身）
///
/// 只有 <see cref="Pile"/> 才需要"作用方式"与"张数"——指定牌名时牌是确定的那几张，
/// 这张牌自己更是只有一张，两者都没有"全部/随机/选择"可言。
///
/// 这就是为什么"作用于自己"不该塞进 <see cref="CardPickMode"/> 里当第四个选项：
/// 它回答的是"作用于什么"，不是"怎么取"。
/// </summary>
public enum PickScope
{
    /// <summary>从某个牌堆里取（配合 <see cref="CardPickMode"/> 决定怎么取）。</summary>
    Pile,

    /// <summary>指定牌名的所有副本。</summary>
    SpecificCard,

    /// <summary>按条件（标签 / 类型）匹配的全部卡牌，条件见 <see cref="EffectDef.CardFilter"/>。</summary>
    ByFilter,

    /// <summary>这张牌自己。</summary>
    ThisCard,
}

/// <summary>能量球种类。游戏里每种球是一个模型类，这里给出可选集合。</summary>
public enum OrbKind
{
    Lightning,
    Frost,
    Dark,
    Plasma,
    Glass,
}

/// <summary>
/// 激发**哪些**能量球。
///
/// **队列的首尾与画面的左右是反的**，这里按画面说，因为作者看到的是画面：
///
///   <see cref="Rightmost"/> → <c>OrbCmd.EvokeNext</c>（队列的 <c>First()</c>）＝ 画面**最右侧**
///   <see cref="Leftmost"/>  → <c>OrbCmd.EvokeLast</c>（队列的 <c>Last()</c>）＝ 画面**最左侧**
///
/// 依据是游戏自己的文本与实现对上号：DUALCAST 的中文写"激发你最右侧的充能球两次"、
/// 实现是 <c>EvokeNext</c>；CONSUMING_SHADOW 写"激发你最左侧的充能球"、实现是 <c>EvokeLast</c>。
/// （引导时球位满了也是 <c>EvokeNext</c>，即自动激发最右侧那个。）
///
/// <see cref="All"/> 是原版本来就有的（"碎片"：激发你所有的充能球两次）；<see cref="Random"/>
/// 是我们扩的——游戏只有"队首 / 全部"两个接口，随机那个要自己挑。
///
/// **数值 m 对四档是同一个意思**：每个被选中的球**各**激发几次（见 EvokeOrbsBehavior）。
/// </summary>
public enum OrbEvokeTarget
{
    /// <summary>最右侧（队列的第一个）。原版绝大多数激发类卡牌都是它。</summary>
    Rightmost,

    /// <summary>最左侧（队列的最后一个）。原版"逝影之力"（回合结束时激发最左侧的球）用它。</summary>
    Leftmost,

    /// <summary>全部（每个球各激发 m 次）。原版"碎片"（激发所有球两次）用它。</summary>
    All,

    /// <summary>随机一个。游戏没有这个接口，由我们按 <c>Rng.CombatOrbs</c> 挑一个。</summary>
    Random,
}

/// <summary>
/// 移除状态的方式：**整个移除**还是**只减几层**。
///
/// 只对 <see cref="EffectKind.RemovePower"/> 有意义。游戏侧对应两条不同的入口：
/// 整层移除是 <c>PowerCmd.Remove</c>；减层是 <c>PowerCmd.ModifyAmount</c>（负数偏移）——
/// 后者会走游戏的加/减量钩子、记战斗历史，并且**降到阈值时由游戏自己把那层状态移掉**
/// （<c>PowerModel.ShouldRemoveDueToAmount</c>），所以"减到 0 就该消失"不需要我们判。
/// </summary>
public enum PowerRemoval
{
    /// <summary>整个移除（原版的"净化"那种）。不需要数值。</summary>
    All,

    /// <summary>只减 <see cref="EffectDef.Amount"/> 层（走三件套，所以能算出来、能升级）。</summary>
    Partial,
}

/// <summary>
/// 移除**哪一类**状态。只对 <see cref="EffectKind.RemovePower"/> 有意义。
///
/// 正面/负面的判据用游戏自己的 <c>PowerModel.TypeForCurrentAmount</c>（卡面数字的颜色用的
/// 就是它）：它按**当前层数**算类型，所以"力量 -3"会被算成负面而不是"某个正面效果"——
/// 这与玩家的直觉一致。自己按类型名列名单会漏掉"同一个状态层数为负时是负面的"那种情况。
/// </summary>
public enum PowerSelection
{
    /// <summary>按 <see cref="EffectDef.PowerId"/> 指定那一个（原版"净化"那种）。</summary>
    PowerId,

    /// <summary>目标身上的**所有正面效果**（层数为正的那些）。</summary>
    Buff,

    /// <summary>目标身上的**所有负面效果**。</summary>
    Debuff,
}

/// <summary>
/// 卡牌筛选：给"按某类牌增强伤害"或"只给某种牌附魔"用。
///
/// 前三种是游戏自带的标签（原版就是这么判断的，例如打击、防御、小刀），最后一种是
/// 指定某一张牌。
/// </summary>
/// <summary>
/// "哪些牌"的判定方式。三个地方共用一套：附魔的作用范围、伤害增益的作用范围
/// （<see cref="PickScope.ByFilter"/>），以及"按条件数牌"这个数值来源
/// （<see cref="ValueSourceKind.FilteredCardCount"/>）。
///
/// 只有原版实际用到的那几种——不是不能做更多，而是多出来的没有对照，
/// 写进去也无法验证是否与原版一致（原版用的是 <c>c.Tags.Contains(CardTag.X)</c>
/// 与 <c>c.Type == CardType.Y</c> 这两种形状）。
/// </summary>
public enum CardFilterKind
{
    /// <summary>不限，所有牌。</summary>
    Any,

    /// <summary>带"打击"标签的牌（<c>CardTag.Strike</c>）。</summary>
    TagStrike,

    /// <summary>带"防御"标签的牌（<c>CardTag.Defend</c>）。</summary>
    TagDefend,

    /// <summary>带"小刀"标签的牌（<c>CardTag.Shiv</c>）。</summary>
    TagShiv,

    /// <summary>带"奥斯提攻击"标签的牌（<c>CardTag.OstyAttack</c>）。</summary>
    TagOstyAttack,

    /// <summary>攻击牌（按类型）。</summary>
    TypeAttack,

    /// <summary>技能牌（按类型）。</summary>
    TypeSkill,

    /// <summary>能力牌（按类型）。</summary>
    TypePower,

    /// <summary>需要星辰费用的牌（<c>CanonicalStarCost &gt;= 0 || HasStarCostX</c>）。</summary>
    HasStarCost,

    /// <summary>带"消耗"关键词的牌（<c>CardKeyword.Exhaust</c>）。</summary>
    KeywordExhaust,

    /// <summary>带"虚无"关键词的牌（<c>CardKeyword.Ethereal</c>）。原版"女妖之嚎"按它计数。</summary>
    KeywordEthereal,

    /// <summary>带"固有"关键词的牌（<c>CardKeyword.Innate</c>）。</summary>
    KeywordInnate,

    /// <summary>带"保留"关键词的牌（<c>CardKeyword.Retain</c>）。</summary>
    KeywordRetain,

    /// <summary>带"奇巧"关键词的牌（<c>CardKeyword.Sly</c>）。</summary>
    KeywordSly,

    /// <summary>带"不能被打出"关键词的牌（<c>CardKeyword.Unplayable</c>）。</summary>
    KeywordUnplayable,

    /// <summary>带"永恒"关键词的牌（<c>CardKeyword.Eternal</c>）。</summary>
    KeywordEternal,

    /// <summary>指定的某一张牌（用 <c>CardId</c> 指定）。</summary>
    SpecificCard,
}

/// <summary>
/// 效果作用于谁。**一份词表覆盖两轴**——同一个值在两个作用域里落到不同的集合上
/// （见 <c>Modules/Targets/TargetCatalog</c> 的那张表：造物轴落到 Creature、玩家轴落到 Player）。
///
/// 名字尽量对齐游戏的 <c>TargetType</c>（<c>AllAllies</c> / <c>AnyAlly</c> 那几个），
/// 因为界面上要跟原版的叫法对得上。**没有收全部十个**：<c>None</c> 是"无目标"（在这套词里
/// 它就是"自己"那类效果缺省的样子）、<c>AnyPlayer</c> / <c>TargetedNoCreature</c> / <c>Osty</c>
/// 三个对卡牌不成立或没有用处，理由见 <c>Modules/Targets/TargetCatalog</c> 的注释。
/// </summary>
public enum EffectTarget
{
    /// <summary>出牌者自身，或状态的持有者。</summary>
    Self,

    /// <summary>
    /// 出牌时选定的那个目标。卡牌目标那一档决定它是什么：造物轴上是"打到的那个生物"、
    /// 玩家轴上是"选中的那名玩家"（界面在玩家轴上把它读作**任意一名友方**）。
    ///
    /// 只有卡牌目标给得出目标时它才成立（见 <c>TargetCatalog.ProvidesPlayedTarget</c>）——
    /// 界面按这一条把它从候选里去掉，校验器也拦。
    /// </summary>
    Target,

    /// <summary>所有敌人。</summary>
    AllEnemies,

    /// <summary>随机一名敌人。</summary>
    RandomEnemy,

    /// <summary>
    /// 所有友方。造物轴上是**玩家阵营的玩家生物**（<c>CombatState.PlayerCreatures</c>，
    /// **不含宠物**——原版"所有友方"的牌用的就是它，见 <c>LegionOfBone</c>）；玩家轴上是
    /// **所有玩家**（<c>CombatState.Players</c>），所以那一轴的标签写的是「所有玩家」，
    /// 见 <see cref="Modules.Targets.TargetCatalog.LabelOf"/>。
    ///
    /// 想作用到宠物上就显式选 <see cref="AllAlliesPet"/> 那一组——那是另一个目标。
    /// </summary>
    AllAllies,

    /// <summary>
    /// 随机一名友方。造物轴上是**玩家生物里随机一个**（<c>PlayerCreatures</c>，**含自己**——
    /// 与"随机一名敌人"从全体敌人里抽是同一个口径：随机那一档抽的是"那一档的全集"）；
    /// 玩家轴上是随机一名玩家（含自己）。
    ///
    /// "不含自己"的是 <see cref="RandomOtherAlly"/>：这两档的分别正是"友方"与"其他友方"
    /// ——前者是整条友方战线（含自己），后者只指队友。
    /// </summary>
    RandomAlly,

    /// <summary>
    /// 随机一名玩家（含自己）。玩家轴专有——造物轴上它与 <see cref="RandomAlly"/> 同集
    /// （都是"玩家生物里随机一个"），列两档只会让人挑不动。
    /// </summary>
    RandomPlayer,

    /// <summary>
    /// 所有**其他**友方（不含自己）。造物轴上是队友的生物（<c>PlayerCreatures</c> 去掉自己）；
    /// 玩家轴上是所有其他玩家（<c>CombatState.Players</c> 去掉自己，界面读作「所有其他玩家」）。
    ///
    /// 与 <see cref="AllAllies"/> 的分别就是那条规矩：**"友方"含自己，"其他"不含自己**。
    /// 游戏自己也分这两套——原版"所有友方"的牌遍历时含自己（<c>PlayerCreatures</c> / <c>IsPlayer</c>），
    /// 而"任意友方"（<c>TargetType.AnyAlly</c>）的候选**明确排除自己**
    /// （游戏自己的注释：<c>Targets any ally (any player excluding itself)</c>）。
    /// </summary>
    AllOtherAllies,

    /// <summary>随机一名**其他**友方（不含自己）。见 <see cref="AllOtherAllies"/>。</summary>
    RandomOtherAlly,

    /// <summary>场上所有人（敌我双方，<c>CombatState.Creatures</c>）。</summary>
    All,

    /* ---------------- 玩家宠物那一组：从玩家派生（`player.Osty`） ---------------- */

    /// <summary>
    /// 自己的奥斯提（<c>Owner.Osty</c>）。
    ///
    /// 这一组四档都是**从玩家派生**的：宠物是玩家的东西（游戏侧 <c>Player.Osty</c> →
    /// <c>PlayerCombatState.GetPet&lt;Osty&gt;()</c>），所以"谁"必须先读成**玩家**，
    /// 再取那个玩家的 <c>Osty</c>。它们是**造物轴**的落点（宠物是 <c>Creature</c>，
    /// 而收 <c>Creature</c> 的命令才接得住它；能量/抽牌那几条收 <c>Player</c>，接不住宠物）。
    /// </summary>
    SelfPet,

    /// <summary>
    /// 目标的奥斯提（卡牌选中的那名玩家的宠物）。**要求这张牌的目标能给出一名玩家**
    /// （"任意友方"那一档）——敌人身上没有玩家，也就没有宠物。
    /// </summary>
    TargetPet,

    /// <summary>所有友方的奥斯提：每个玩家各一只（没宠物 / 不在场的跳过）。**含自己那只**。</summary>
    AllAlliesPet,

    /// <summary>随机一名友方的奥斯提：随机一名玩家的宠物。**含自己那只**——与上一档同一口径
    /// （"随机"抽的是这一档的全集，与"随机一名敌人"从全体敌人里抽同规矩）。</summary>
    RandomAllyPet,

    /// <summary>
    /// 所有**其他**玩家的奥斯提（不含自己那只）。队友的宠物各一次，没宠物 / 不在场的跳过。
    /// 与 <see cref="AllAlliesPet"/> 的分别就是那条规矩：**"友方"含自己、"其他"不含自己**。
    /// </summary>
    AllOtherAlliesPet,

    /// <summary>随机一名**其他**玩家的奥斯提（不含自己那只）。见 <see cref="AllOtherAlliesPet"/>。</summary>
    RandomOtherAllyPet,
}

/// <summary>
/// 一条效果的作用对象**能是哪些东西**——决定界面给它哪一套目标选项、以及目标怎么展开成代码。
///
/// 分档的依据是游戏接口收什么参数，不是我们的偏好：三个作用域各自对应游戏里的一族命令：
///
///   <see cref="Creature"/> —— 收 <c>Creature</c> 的命令（伤害 / 格挡 / 生命 / 施加与移除状态 /
///                            灾厄判定）。目标是**任何生物**，所以七档全给。
///   <see cref="Player"/>   —— 收 <c>Player</c> 的命令（能量 / 星辰 / 充能球 / 抽牌 / 铸造 / 召唤）。
///                            目标只能是**玩家**，所以只给表达得出来的四档；
///                            给"所有敌人"这种档就等于发一段永远不成立的代码。
///   <see cref="Cards"/>    —— 作用于牌（消耗牌这类：谁受影响由选牌器决定，与生物无关）。
///                            只有"自己"这一档——它的含义是"这张牌的执行者"。
///
/// 这个分档就是"目标选择器模块"的骨架，见 <c>Modules/Targets/</c>。
///
/// 序列化成**名字**（<c>"Player"</c>）而不是序号：它会进界面 schema 快照，读的人要能看懂。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TargetScope>))]
public enum TargetScope { Creature, Player, Cards }

/// <summary>牌堆。</summary>
public enum PileKind
{
    Hand,
    DrawPile,
    DiscardPile,
    ExhaustPile,
}

/// <summary>
/// 回合触发时机要求这张牌待在哪个牌堆里。
///
/// 只有 <see cref="EffectTrigger.StartOfTurn"/> / <see cref="EffectTrigger.EndOfTurn"/>
/// 会用到：那两种时机默认只看"这张牌在不在战斗牌堆里"，加上这个范围就能表达
/// "回合开始时，若这张牌在抽牌堆中"这类写法。
///
/// 它同时吸收了原来的 <c>EndOfTurnInHand</c>——那只是本枚举取 <see cref="Hand"/> 的特例。
/// </summary>
public enum PileScope
{
    /// <summary>任意战斗牌堆（手牌、抽牌堆、弃牌堆、消耗堆都算）。</summary>
    Any,

    Hand,
    DrawPile,
    DiscardPile,
    ExhaustPile,
}

/// <summary>放入牌堆时的位置。</summary>
public enum PilePlacement
{
    Top,
    Bottom,
    Random,
}

/// <summary>效果行的触发时机。</summary>
public enum EffectTrigger
{
    /// <summary>打出时（默认）。</summary>
    OnPlay,

    /// <summary>这张牌被打出后（可用于"打出后再生效"的效果）。</summary>
    AfterPlayed,

    /// <summary>抽到时。</summary>
    OnDraw,

    /// <summary>被弃掉时。</summary>
    OnDiscard,

    /// <summary>被消耗时。</summary>
    OnExhaust,

    /// <summary>
    /// 回合结束时。默认只看这张牌在不在任意战斗牌堆里；
    /// 用 <see cref="EffectDef.PileScope"/> 可以收窄到某个具体牌堆
    /// （例如"回合结束时，若在手牌中"）。
    /// </summary>
    EndOfTurn,

    /// <summary>
    /// 回合开始时。牌堆范围的处理同 <see cref="EndOfTurn"/>。
    /// </summary>
    StartOfTurn,

    /// <summary>
    /// **打出一张牌时**——反应的是"任意一张被打出的牌"，不要求是这张牌
    /// （对比 <see cref="OnPlay"/> / <see cref="AfterPlayed"/>，那两个只看这张牌自己）。
    ///
    /// 用 <see cref="EffectDef.CardFilter"/> 限定"哪些牌算数"（按标签 / 关键词 / 类型 /
    /// 指定卡 / 不限），所以原版"每打出过一张攻击牌，其耗能减少 1"（踩踏）、
    /// "每打出一张技能牌降 1 费"（精密瞄准）这类可以照搬。
    ///
    /// 默认**每打出一张符合条件的牌就触发一次**；要"攒够 N 张才触发"就用
    /// <see cref="EffectDef.UseCounter"/> + <see cref="EffectDef.CounterTarget"/>
    /// （原版"如此甚好"：每打出 N 张技能牌就回到手牌）。
    /// </summary>
    CardPlayed,
}

/// <summary>
/// 计数器什么时候归零。
///
/// "本回合"的会在**回合结束与回合开始各归零一次**：只靠回合开始归零的话，
/// 本回合末到下次回合开始之间读到的还是上一回合的累计值——而"本回合打出了几张"
/// 这个概念在回合结束后就已经过期了。
/// </summary>
public enum CounterReset
{
    /// <summary>本回合（回合结束与回合开始各重置一次）。</summary>
    ThisTurn,

    /// <summary>本场战斗。战斗内的牌是临时副本，战斗结束自然归零，不需要显式重置。</summary>
    ThisCombat,
}

/// <summary>
/// 效果的**四个数值角色**：数值 / 次数 / 能量阈值 / 星辰阈值。
///
/// 每个角色都有一套"基础值 + 变量 × 变量系数"，升级时各有一套增量
/// （见 <see cref="EffectUpgrade"/> 与 <see cref="EffectLevel"/>）。
///
/// 做成枚举 + **索引读写**（<see cref="EffectUpgrade.AmountDeltaOf"/> 等）是为了让
/// "逐角色处理"的代码只写一遍：共享效果合并、IsSet、等级合成、XML 读写、描述、校验
/// 全都遍历它。加第五个角色时只改这一处的 switch，不会再出现"某一处忘了补一行、
/// 于是那一项在生成结果里少一半"那种静默失效（<c>EffectResolver.Merge</c> 的注释记着这个坑）。
/// </summary>
public enum ValueRole
{
    /// <summary>效果的数值（伤害 / 格挡 / 张数…）。</summary>
    Value,

    /// <summary>重复次数。</summary>
    Repeat,

    /// <summary>能量消耗条件的阈值（选牌的费用维度之一）。</summary>
    Energy,

    /// <summary>星辰消耗条件的阈值。</summary>
    Star,
}

/// <summary>
/// 两个**费用阈值**角色（选牌的两个费用维度：能量消耗 / 星辰消耗）。
///
/// 只处理费用阈值的代码都遍历它（变量模块分配阈值槽、校验器报"填了不生效"、
/// 生成器发升级与等级表语句），于是"哪些角色算费用角色"只写一遍。
/// </summary>
public static class CostRoles
{
    /// <summary>两个费用角色，顺序固定（能量在前）。</summary>
    public static readonly IReadOnlyList<ValueRole> All = new[] { ValueRole.Energy, ValueRole.Star };
}

/// <summary>
/// 升级时数值如何变化——**每升一级加多少**（增量语义）。
///
/// 四个数值角色（见 <see cref="ValueRole"/>）各有"基础值增量 / 变量系数增量 /
/// 升级后换用的来源"三项，相互独立，可以只升其中一项。
///
/// 例：升级后伤害基础值从 5 变 8（<see cref="AmountDelta"/> = 3）；
///     或者"每点力量加成"从 1 变 1.5（<see cref="PerUnitDelta"/> = 0.5）；
///     或者"每失去过一次生命就多打一次"的系数从 0.5 变 1（<see cref="RepeatPerUnitDelta"/> = 0.5）。
///
/// 要表达"每级加得不一样"或"升级后换算式来源"，用等级表（<see cref="EffectDef.Levels"/>）——
/// 加法做不到那两件事。
/// </summary>
public sealed class EffectUpgrade
{
    // ---- 数值 ----

    /// <summary>
    /// 数值基础值的增量。计算型改的是 CalculationBase，固定型改的就是数值本身。
    /// </summary>
    public int AmountDelta { get; set; }

    /// <summary>
    /// 数值的变量系数增量，支持小数（如每点加成 1 → 1.5）。
    /// </summary>
    public decimal PerUnitDelta { get; set; }

    // ---- 次数 ----

    /// <summary>
    /// 次数基础值的增量（如"造成 4 点伤害，重复 3 次"升级后变 4 次）。
    ///
    /// 次数是固定值（没有变量来源）时它就是那个次数本身；次数是计算型时它是算式里的基础值。
    /// 两种情况下改的都是同一个数（见 <see cref="EffectDef.Repeat"/>）。
    /// </summary>
    public int RepeatDelta { get; set; }

    /// <summary>
    /// 次数的变量系数增量：只对**计算型次数**有意义（"每失去过一次生命就多打一次"，
    /// 升级后变成"每次都多打一次"）。
    ///
    /// 次数是固定值时没有变量、也就没有系数，填了这一项不会有任何效果——
    /// 校验器会就此提示。
    /// </summary>
    public decimal RepeatPerUnitDelta { get; set; }

    // ---- 两个费用阈值（选牌的两个费用维度）----

    /// <summary>
    /// 能量消耗条件的阈值增量。只对"调用选牌控件"的效果有意义
    /// （见 <c>Modules.CardPicking.CardPickPolicies.UsesPicker</c>）。
    /// </summary>
    public int EnergyDelta { get; set; }

    /// <summary>能量阈值的变量系数增量。</summary>
    public decimal EnergyPerUnitDelta { get; set; }

    /// <summary>星辰消耗条件的阈值增量。</summary>
    public int StarDelta { get; set; }

    /// <summary>星辰阈值的变量系数增量。</summary>
    public decimal StarPerUnitDelta { get; set; }

    // ---- 升级后换用的算式来源（各角色一份）----

    /// <summary>
    /// 升级后**换用的算式来源**（数值）。null = 不换。
    ///
    /// 与等级表里的 <see cref="EffectLevel.Source"/> 是同一件事的两种写法：
    /// 等级表能逐级换（第 1 级按手牌数、第 2 级按弃牌堆数），这里只能"升级后统一换一个"。
    /// **只在没有等级表时生效**——有等级表时整条效果的升级由表说了算，这一项会被忽略
    /// （校验器会提示）。
    /// </summary>
    public ValueSource? Source { get; set; }

    /// <summary>升级后换用的**次数**算式来源。语义同 <see cref="Source"/>，只是作用在次数上。</summary>
    public ValueSource? RepeatSource { get; set; }

    /// <summary>升级后换用的**能量阈值**来源。语义同 <see cref="Source"/>。</summary>
    public ValueSource? EnergySource { get; set; }

    /// <summary>升级后换用的**星辰阈值**来源。语义同 <see cref="Source"/>。</summary>
    public ValueSource? StarSource { get; set; }

    /* ------------------------------------------------------------ 逐角色读写 */

    /// <summary>全部角色。逐角色处理的代码都遍历它（见 <see cref="ValueRole"/> 的说明）。</summary>
    public static readonly IReadOnlyList<ValueRole> AllRoles = Enum.GetValues<ValueRole>();

    /// <summary>某个角色的基础值增量。</summary>
    public int AmountDeltaOf(ValueRole role) => role switch
    {
        ValueRole.Repeat => RepeatDelta,
        ValueRole.Energy => EnergyDelta,
        ValueRole.Star => StarDelta,
        _ => AmountDelta,
    };

    public void SetAmountDelta(ValueRole role, int value)
    {
        switch (role)
        {
            case ValueRole.Repeat: RepeatDelta = value; break;
            case ValueRole.Energy: EnergyDelta = value; break;
            case ValueRole.Star: StarDelta = value; break;
            default: AmountDelta = value; break;
        }
    }

    /// <summary>某个角色的变量系数增量。</summary>
    public decimal PerUnitDeltaOf(ValueRole role) => role switch
    {
        ValueRole.Repeat => RepeatPerUnitDelta,
        ValueRole.Energy => EnergyPerUnitDelta,
        ValueRole.Star => StarPerUnitDelta,
        _ => PerUnitDelta,
    };

    public void SetPerUnitDelta(ValueRole role, decimal value)
    {
        switch (role)
        {
            case ValueRole.Repeat: RepeatPerUnitDelta = value; break;
            case ValueRole.Energy: EnergyPerUnitDelta = value; break;
            case ValueRole.Star: StarPerUnitDelta = value; break;
            default: PerUnitDelta = value; break;
        }
    }

    /// <summary>某个角色"升级后换用的来源"。</summary>
    public ValueSource? SourceOf(ValueRole role) => role switch
    {
        ValueRole.Repeat => RepeatSource,
        ValueRole.Energy => EnergySource,
        ValueRole.Star => StarSource,
        _ => Source,
    };

    public void SetSource(ValueRole role, ValueSource? value)
    {
        switch (role)
        {
            case ValueRole.Repeat: RepeatSource = value; break;
            case ValueRole.Energy: EnergySource = value; break;
            case ValueRole.Star: StarSource = value; break;
            default: Source = value; break;
        }
    }

    /// <summary>这个角色填了任何一项没有。</summary>
    public bool RoleIsSet(ValueRole role) =>
        AmountDeltaOf(role) != 0 || PerUnitDeltaOf(role) != 0m || SourceOf(role) is not null;

    /// <summary>是否设置了任何升级改动（四个角色逐一判）。</summary>
    [JsonIgnore]
    public bool IsSet
    {
        get
        {
            foreach (ValueRole role in AllRoles)
            {
                if (RoleIsSet(role))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 深复制。来源对象各自复制一份（见 <see cref="EffectDef.Clone"/> 的说明）；
    /// 逐角色复制，加新角色不用在这里补行。
    /// </summary>
    public EffectUpgrade Clone()
    {
        var copy = new EffectUpgrade();

        foreach (ValueRole role in AllRoles)
        {
            copy.SetAmountDelta(role, AmountDeltaOf(role));
            copy.SetPerUnitDelta(role, PerUnitDeltaOf(role));
            copy.SetSource(role, SourceOf(role)?.Clone());
        }

        return copy;
    }
}

/// <summary>
/// 多次升级（<see cref="CardDef.MaxUpgradeLevel"/> &gt; 1）时，某一级的数值。
///
/// **null 表示"沿用上一级"**，所以只填真正变化的那几项就够了——大多数牌的升级就是
/// "数字变大"，那一项填 Amount 即可。
///
/// 为什么要有它：增量模型只能表达"每级加同样的量"，表达不了"每级加得不一样"
/// （第 1 级 +2、第 2 级 +3），更表达不了**换算式来源**（0 级按手牌数、1 级按弃牌堆数）。
/// 等级表把每一级的数值直接写下来，这两件事都能做。
///
/// 各条效果各占自己的等级表，所以"这张牌第 2 级时只改伤害、不动格挡"也写得出来。
/// </summary>
public sealed class EffectLevel
{
    // ---- 数值 ----

    /// <summary>这一级的数值（计算型时是基础值）。null 表示沿用上一级。</summary>
    public int? Amount { get; set; }

    /// <summary>
    /// 这一级的"每 1 点来源值增加的数值"。null 表示沿用上一级。
    /// 只改这一项时，来源本身不变、只有系数变。
    /// </summary>
    public decimal? PerUnit { get; set; }

    /// <summary>
    /// 这一级换用的算式来源（"0 级按手牌数、1 级按弃牌堆数"）。null 表示沿用上一级。
    ///
    /// 只对计算型有意义。换了来源时，生成的方法会按等级分支取值——**但卡面文字
    /// 跟不上**：游戏描述里只有 <c>{IfUpgraded:...|...}</c> 这种"升级前/升级后"的二元写法，
    /// 分不出第几级（原版一张多级升级的牌都没有，游戏没有这个机制）。所以换来源时
    /// 描述要自己写，校验器也会就此提示。
    /// </summary>
    public ValueSource? Source { get; set; }

    // ---- 次数 ----

    /// <summary>
    /// 这一级的重复次数（计算型时是算式的基础值）。null 表示沿用上一级。
    /// </summary>
    public int? Repeat { get; set; }

    /// <summary>
    /// 这一级的**次数变量系数**。null 表示沿用上一级。
    /// 只对计算型次数有意义（固定次数没有变量，也就没有系数）。
    /// </summary>
    public decimal? RepeatPerUnit { get; set; }

    /// <summary>
    /// 这一级换用的**次数**算式来源。null 表示沿用上一级。
    ///
    /// 与 <see cref="Source"/>（数值的来源）各管各的：两者可以分别换，
    /// 例如"0 级按手牌数打、1 级改成按弃牌堆数打，而次数一直按失去生命的次数算"。
    /// </summary>
    public ValueSource? RepeatSource { get; set; }

    // ---- 两个费用阈值（选牌的费用维度）----

    /// <summary>这一级的能量消耗阈值。null 表示沿用上一级。</summary>
    public int? EnergyAmount { get; set; }

    /// <summary>这一级能量阈值的变量系数。null 表示沿用上一级。</summary>
    public decimal? EnergyPerUnit { get; set; }

    /// <summary>这一级换用的能量阈值来源。null 表示沿用上一级。</summary>
    public ValueSource? EnergySource { get; set; }

    /// <summary>这一级的星辰消耗阈值。null 表示沿用上一级。</summary>
    public int? StarAmount { get; set; }

    /// <summary>这一级星辰阈值的变量系数。null 表示沿用上一级。</summary>
    public decimal? StarPerUnit { get; set; }

    /// <summary>这一级换用的星辰阈值来源。null 表示沿用上一级。</summary>
    public ValueSource? StarSource { get; set; }

    /* ------------------------------------------------------------ 逐角色读写 */

    /// <summary>这一级里某个角色写的**基础值**（null = 沿用上一级）。</summary>
    public int? AmountOf(ValueRole role) => role switch
    {
        ValueRole.Repeat => Repeat,
        ValueRole.Energy => EnergyAmount,
        ValueRole.Star => StarAmount,
        _ => Amount,
    };

    public void SetAmount(ValueRole role, int? value)
    {
        switch (role)
        {
            case ValueRole.Repeat: Repeat = value; break;
            case ValueRole.Energy: EnergyAmount = value; break;
            case ValueRole.Star: StarAmount = value; break;
            default: Amount = value; break;
        }
    }

    /// <summary>这一级里某个角色写的**变量系数**（null = 沿用上一级）。</summary>
    public decimal? PerUnitOf(ValueRole role) => role switch
    {
        ValueRole.Repeat => RepeatPerUnit,
        ValueRole.Energy => EnergyPerUnit,
        ValueRole.Star => StarPerUnit,
        _ => PerUnit,
    };

    public void SetPerUnit(ValueRole role, decimal? value)
    {
        switch (role)
        {
            case ValueRole.Repeat: RepeatPerUnit = value; break;
            case ValueRole.Energy: EnergyPerUnit = value; break;
            case ValueRole.Star: StarPerUnit = value; break;
            default: PerUnit = value; break;
        }
    }

    /// <summary>这一级里某个角色换用的**来源**（null = 沿用上一级）。</summary>
    public ValueSource? SourceOf(ValueRole role) => role switch
    {
        ValueRole.Repeat => RepeatSource,
        ValueRole.Energy => EnergySource,
        ValueRole.Star => StarSource,
        _ => Source,
    };

    public void SetSource(ValueRole role, ValueSource? value)
    {
        switch (role)
        {
            case ValueRole.Repeat: RepeatSource = value; break;
            case ValueRole.Energy: EnergySource = value; break;
            case ValueRole.Star: StarSource = value; break;
            default: Source = value; break;
        }
    }

    /// <summary>这一级在这个角色上写了任何一项没有（写了就该覆盖上一级）。</summary>
    public bool RoleIsSet(ValueRole role) =>
        AmountOf(role) is not null || PerUnitOf(role) is not null || SourceOf(role) is not null;

    /// <summary>逐角色深复制（来源对象各自复制，见 <see cref="EffectDef.Clone"/> 的说明）。</summary>
    public EffectLevel Clone()
    {
        var copy = new EffectLevel();

        foreach (ValueRole role in EffectUpgrade.AllRoles)
        {
            copy.SetAmount(role, AmountOf(role));
            copy.SetPerUnit(role, PerUnitOf(role));
            copy.SetSource(role, SourceOf(role)?.Clone());
        }

        return copy;
    }
}

/// <summary>
/// 一条效果在**某一级**下的数值状态：四个角色（数值 / 次数 / 能量阈值 / 星辰阈值）各一份
/// "基础值 + 变量来源"。
///
/// 由 <see cref="EffectDef.StateAt"/> 把"基础字段 + 等级表覆盖"合成一个确定的值，
/// 生成、描述、校验都读它——三处各推一遍的话，升级后卡面写的与实际打出的会不一致。
/// </summary>
public readonly record struct EffectState(
    int Amount,
    int Repeat,
    ValueSource Source,
    ValueSource RepeatSource,
    CostState Energy,
    CostState Star);

/// <summary>一个费用阈值角色在某一级下的取值（基础值 + 变量来源）。</summary>
public readonly record struct CostState(int Amount, ValueSource Source);

/// <summary>
/// 一条效果。效果按列表顺序执行，所以后面的效果可以依赖前面已经结算的结果。
///
/// 一条效果可以内联描述，也可以是指向共享效果的引用（见 <see cref="SharedEffectId"/>）。
/// </summary>
public sealed class EffectDef
{
    /// <summary>
    /// 引用共享效果时的 id；设置后其它字段以该定义为准。按 id 而不是按位置引用，
    /// 所以调整顺序不会把引用指到别的效果上。
    /// </summary>
    public string? SharedEffectId { get; set; }

    public EffectKind Kind { get; set; } = EffectKind.DealDamage;

    /// <summary>何时触发。默认打出时。</summary>
    public EffectTrigger Trigger { get; set; } = EffectTrigger.OnPlay;

    public EffectTarget Target { get; set; } = EffectTarget.Target;

    /// <summary>
    /// 把这条效果**产生/选出的那些牌**存进一个**临时牌集合**，集合叫这个名字
    /// （界面上是「临时牌集合ID」那一格）。空 = 不存（默认）。
    ///
    /// 用途：让**同一张牌**后面的效果再动那些牌——"抽到的那几张降费 / 附魔 / 升级"。
    /// 后面那条效果把"作用方式"（选牌来源）设成「牌集合ID」并填同一个名字即可
    /// （<c>CardPick.Source = PickSource.Set</c> + <c>CardPick.SetId</c>）。
    ///
    /// **生命周期是一次出牌结算**：生成代码在 <c>OnPlay</c> 开头开一个"局内的牌地址"，
    /// 结算结束（含异常）时把它下面的集合全部清掉。所以集合只在这张牌这一次结算里存在，
    /// 既不会跨牌、也不会跨回合——这正是它叫"临时"的意思。嵌套结算互不干扰
    /// （打出的牌又打出牌，各有各的地址）。
    ///
    /// 名字按 <c>[A-Za-z0-9_]+</c> 写（它进运行时的键，键用 <c>.</c> 分段，所以不许有点），
    /// 而且**一张牌上不许重复用同一个名字**——否则后面那条效果不知道该读哪一个。
    /// </summary>
    public string? SetId { get; set; }

    /// <summary>
    /// 效果的数值（界面上叫「效果基础值」）。
    ///
    /// 数值 = <c>基础值 + 变量 × 变量系数</c>：
    /// 选了来源就是"算出来的"（原版"完美打击"是 6 + 2 × 打击牌数），
    /// 来源选「无」就是固定值，只用这个数。
    /// </summary>
    public int Amount { get; set; }

    /// <summary>
    /// 数值的来源。选「无」表示这是固定值（<see cref="EffectDef.IsCalculated"/> 为假）。
    ///
    /// 早先"固定 / 计算"是一个单独的下拉（<c>AmountMode</c>），现在与"重复次数"一样
    /// 由来源决定：来源非「无」就是计算型。少一个开关，也少一种"开关与来源不一致"的状态。
    /// </summary>
    public ValueSource Source { get; set; } = new();

    /// <summary>施加或移除的状态，模型 id 形式，例如 POWER.STRENGTH_POWER。</summary>
    public string? PowerId { get; set; }

    // ---- 牌堆操作的参数 ----

    /// <summary>
    /// 搬运类效果的**目标**牌堆（来源是 <see cref="EffectDef.Pick"/> 里那几个牌堆）。
    /// 只对 <see cref="EffectKind.MoveCardsBetweenPiles"/> 与"复制牌并加入牌堆"有意义。
    /// </summary>
    public PileKind TargetPile { get; set; } = PileKind.DrawPile;

    /// <summary>放入时的位置。</summary>
    public PilePlacement Placement { get; set; } = PilePlacement.Top;

    /// <summary>
    /// 指定的**某一张**牌，模型 id 形式（例如 CARD.STRIKE_IRONCLAD）。三种用途，都不是选牌：
    ///
    ///   加入指定卡牌（<see cref="EffectKind.AddNamedCardToPile"/>）——加的就是它；
    ///   变化成指定牌（<see cref="EffectKind.TransformCardsInPile"/>）——变成它；
    ///   "打出一张牌时"那个时机（<see cref="CardFilter"/> = SpecificCard）——只看它。
    ///
    /// 选牌器里"按牌名挑"（可多个）走 <see cref="EffectDef.Pick"/> 的 <c>CardIds</c>。
    /// </summary>
    public string? CardId { get; set; }

    /// <summary>是否加入升级版。</summary>
    public bool Upgraded { get; set; }

    /// <summary>
    /// 加入指定卡牌用哪种生成方式（<see cref="AddCardMode"/>）。
    ///
    /// 只对 <see cref="EffectKind.AddNamedCardToPile"/> 有意义。两种模式读的字段不同：
    ///   Named 读 <see cref="CardId"/> / <see cref="Upgraded"/>；
    ///   ChooseFromPool 读 <see cref="PoolFilter"/> / <see cref="Candidates"/>——
    ///   <see cref="CardId"/> 与 <see cref="Upgraded"/> 在这一模式**不参与**
    ///   （卡池筛出来的牌谈不上"指定"，升级也不会 roll——那是奖励流的逻辑，战斗生成不做）。
    /// </summary>
    public AddCardMode AddMode { get; set; } = AddCardMode.Named;

    /// <summary>
    /// "从卡池 N 选一"的候选数 N（<see cref="AddCardMode.ChooseFromPool"/>）。
    ///
    /// 1 = 从筛选过的卡池随机取一张直接加入（不弹界面）；2~3 = 弹游戏自己的选牌界面
    /// 让玩家选一张。**上限是 3**：游戏那条接口候选超过 3 张直接抛异常
    /// （<c>FromChooseACardScreen</c> 里的 <c>ArgumentException</c>），校验器会拦。
    /// </summary>
    public int Candidates { get; set; } = 3;

    /// <summary>
    /// "从卡池 N 选一"的**多维筛选**（<see cref="AddCardMode.ChooseFromPool"/>）。
    ///
    /// 与选牌器那份筛选同一个形状（<see cref="CardFilter"/>），判定也走同一个谓词生成处
    /// （<c>CardPickFilter</c>）——差别只在"范围"：这里没有牌堆 / 卡组 / 此牌那几个
    /// 运行时范围，候选来自**出牌者自己的角色卡池**（原版"发现"的取法）。费用阈值用
    /// 筛选自带的常数（卡池里都是卡面印着的费用，用不到变量槽）。
    /// </summary>
    public CardFilter PoolFilter { get; set; } = new();

    /// <summary>
    /// 执行次数（这套三件套的**基础值**）：<c>次数 = Repeat + RepeatSource 的变量系数 × 变量值</c>。
    ///
    /// 固定次数时它就是次数本身；计算型时它是算式里的基础值。两者是同一个字段：
    /// 次数与数值一样只有一套三件套，没有"基础值"与"次数"两个数。
    /// </summary>
    public int Repeat { get; set; } = 1;

    /// <summary>
    /// 执行次数是不是算出来的：次数 = <see cref="EffectDef.Repeat"/> +
    /// <see cref="EffectDef.RepeatSource"/> 的变量系数 × 变量值。
    ///
    /// 原版"扯碎"（TearAsunder）就是这种："本场战斗中你每失去过一次生命，这张牌就额外
    /// 造成一次伤害"——伤害是固定 5，**次数**是 1 + 失去生命的次数。注意它的次数与伤害
    /// 用的不是同一个基础值（伤害 5、次数 1），所以次数必须有自己的一套
    /// <see cref="EffectDef.Repeat"/> / <see cref="EffectDef.RepeatSource"/>，
    /// 不能借用数值那一套。
    ///
    /// 与数值的来源（<see cref="EffectDef.Source"/>）相互独立：两者可以各算各的
    /// （"打等于手牌数的伤害，次数等于失去生命的次数"），也可以只有一个是计算型。
    ///
    /// **由来源推出，不是单独一个开关**（与"固定值/计算型"同一个规矩）：
    /// 选了来源就是计算型，没选就是固定次数。少一个开关，也就少一种"开关与来源不一致"
    /// 的状态——那种状态以前要单独写一条校验去拦，现在根本表达不出来。
    /// 关掉它会清掉来源（否则开关一关、来源还留着，下次打开又变成计算型）。
    /// </summary>
    public bool CalculatedRepeat
    {
        get => RepeatSource.IsUsed;
        set
        {
            if (!value)
            {
                RepeatSource = new ValueSource();
            }
        }
    }

    /// <summary>
    /// 旧名字：次数用计算型时的基础值。
    ///
    /// **与 <see cref="EffectDef.Repeat"/> 是同一个数**（工程文件里那两处写法、以及界面上的
    /// 那次读写都还叫这个名字，所以留着一个等价的入口）。新代码请直接用
    /// <see cref="EffectDef.Repeat"/>——"次数"与"数值"一样只有一条三件套，
    /// 分成两个字段只会让人以为它们可以不同。
    /// </summary>
    public int RepeatBase
    {
        get => Repeat;
        set => Repeat = value;
    }

    /// <summary>次数用计算型时的来源。仅在 <see cref="EffectDef.CalculatedRepeat"/> 时使用。</summary>
    public ValueSource RepeatSource { get; set; } = new();

    // ---- 两个费用阈值（选牌的两个费用维度）----
    //
    // 它们是**效果自己的数值**（角色 Energy / Star，见 ValueRole）：与 Amount（数值）和
    // Repeat（次数）并列，同样占变量槽、同样能升级、同样能进等级表。
    // 而"怎么比"（不小于 / 大于 / 是 X…）是选牌的**条件运算**，住在 CardPick 里
    // （CardPick.EnergyComparison / StarComparison）——一个管"比什么数"、一个管"怎么比"。

    /// <summary>
    /// 能量消耗条件的阈值（基础值）。只对"调用选牌控件"的效果有意义
    /// （见 <c>Modules.CardPicking.CardPickPolicies.UsesPicker</c>）。
    ///
    /// 比较的对象是**印在卡面上的能量费用**（<c>CardModel.EnergyCost.Canonical</c>），
    /// 不是战斗中的实时费用。X 费卡的 Canonical 是 0。
    /// </summary>
    public int EnergyAmount { get; set; }

    /// <summary>能量阈值的变量来源（选「无」= 固定阈值）。</summary>
    public ValueSource EnergySource { get; set; } = new();

    /// <summary>
    /// 星辰消耗条件的阈值（基础值）。比较对象是 <c>CardModel.CanonicalStarCost</c>
    /// （-1 = 没有星辰费用）；星辰是 X 的牌要用"是 X"那一档判，见 CardPick.StarComparison。
    /// </summary>
    public int StarAmount { get; set; }

    /// <summary>星辰阈值的变量来源（选「无」= 固定阈值）。</summary>
    public ValueSource StarSource { get; set; } = new();

    /// <summary>
    /// 这条效果的**启用条件**（见 <see cref="EffectCondition"/>）：null = 无条件（默认）。
    ///
    /// 启用后生成代码把整条效果（含"重复 N 次"循环）包在一层 <c>if</c> 里，
    /// 条件不成立时这条效果什么都不做——但不影响同卡其它效果。
    /// </summary>
    public EffectCondition? Condition { get; set; }

    /// <summary>有没有启用条件（读 <see cref="Condition"/> 的一层皮，判空散在各处不如一个名字）。</summary>
    [JsonIgnore]
    public bool HasCondition => Condition is not null;

    /// <summary>引导的能量球种类（见 <see cref="EffectKind.ChannelOrb"/>）。</summary>
    public OrbKind Orb { get; set; } = OrbKind.Lightning;

    /// <summary>
    /// 激发哪些球（见 <see cref="OrbEvokeTarget"/>）。只对 <see cref="EffectKind.EvokeOrbs"/> 有意义。
    ///
    /// 默认最右侧，因为原版的激发类卡牌几乎都是它。
    /// </summary>
    public OrbEvokeTarget EvokeTarget { get; set; } = OrbEvokeTarget.Rightmost;

    /// <summary>
    /// 只激发这些**种类**的球（闪电 / 冰霜 / 黑暗 / 等离子 / 玻璃）。空 = 不限种类。
    /// 只对 <see cref="EffectKind.EvokeOrbs"/> 有意义。
    ///
    /// 与 <see cref="EvokeTarget"/> 组合读：例如"全部的闪电、冰霜球""最右侧的黑暗球"。
    /// 挑出来的球如果不是队首，激发前会先把它挪到队首——游戏只给了"激发队首 / 队尾"两个
    /// 接口，没有"激发任意一个"的入口。
    /// </summary>
    public List<OrbKind> OrbKinds { get; set; } = new();

    /// <summary>附魔的模型 id，例如 ENCHANTMENT.SHARP。</summary>
    public string? EnchantmentId { get; set; }

    /// <summary>
    /// "打出一张牌时"这个时机看哪些牌（<see cref="CardFilterKind"/>）。
    /// 只对 <see cref="EffectTrigger.CardPlayed"/> 有意义——**选牌不再读它**：
    /// "给哪些牌附魔 / 给哪些牌增伤"现在由 <see cref="EffectDef.Pick"/> 的维度那一格表达。
    /// </summary>
    public CardFilterKind CardFilter { get; set; } = CardFilterKind.Any;

    /// <summary>
    /// 回合触发时这张牌必须待在哪个牌堆（见 <see cref="PileScope"/>）。
    /// 只对 <see cref="EffectTrigger.StartOfTurn"/> / <see cref="EffectTrigger.EndOfTurn"/> 有意义。
    /// </summary>
    public PileScope PileScope { get; set; } = PileScope.Any;

    /// <summary>费用调整的持续范围。只对费用类效果有意义。</summary>
    public CostDuration CostDuration { get; set; } = CostDuration.ThisTurn;

    /// <summary>
    /// **选哪些牌**——选牌模块的十步流程（见 <see cref="Modules.CardPicking.CardPick"/>）。
    ///
    /// 会挑牌的那几条效果（消耗 / 丢弃 / 搬运 / 复制 / 升级 / 变化 / 附魔 / 降费 / 免费 /
    /// 增伤 / 重放 / 自动打出）都读它：十步的字段与流程一一对应，效果自己的策略
    /// （<see cref="Modules.CardPicking.CardPickPolicies"/>）决定哪些流程能用。
    ///
    /// 不挑牌的效果忽略这一项。
    /// </summary>
    public Modules.CardPicking.CardPick Pick { get; set; } = new();

    // ---- "打出一张牌时"的计数器 ----

    /// <summary>
    /// 是否启用计数器。只对 <see cref="EffectTrigger.CardPlayed"/> 有意义。
    ///
    /// 不启用：每打出一张符合条件的牌就触发一次（原版踩踏、女妖之嚎）。
    /// 启用：每打出一张符合条件的牌计数 +1，达到 <see cref="CounterTarget"/> 才触发效果
    /// 并把计数器归零（原版"如此甚好"：每打出 N 张技能牌回到手牌）。
    /// </summary>
    public bool UseCounter { get; set; }

    /// <summary>计数器达到多少才触发。启用计数器时至少为 1（1 等同于不启用）。</summary>
    public int CounterTarget { get; set; } = 3;

    /// <summary>计数器什么时候归零。</summary>
    public CounterReset CounterReset { get; set; } = CounterReset.ThisTurn;

    /// <summary>变化类效果：变成随机牌还是指定的牌。只对 <see cref="EffectKind.TransformCardsInPile"/> 有意义。</summary>
    public TransformInto TransformInto { get; set; } = TransformInto.Random;

    // ---- 移除状态的两个选项（只对 <see cref="EffectKind.RemovePower"/> 有意义）----

    /// <summary>
    /// 整个移除还是只减几层（见 <see cref="PowerRemoval"/>）。默认"整个移除"——那是这条效果
    /// 一直以来的行为，所以旧文件读出来与从前一致。
    ///
    /// 选"部分"时数值才参与（它就是"减几层"）——所以界面上那三格与"升级时数值 +"跟着出现。
    /// </summary>
    public PowerRemoval Removal { get; set; } = PowerRemoval.All;

    /// <summary>
    /// 移除哪一类状态：指定的那一个 / 所有正面 / 所有负面（见 <see cref="PowerSelection"/>）。
    /// 默认"按 id 指定"，同样是旧行为。
    ///
    /// 选"正面/负面"时 <see cref="PowerId"/> 不参与——**校验器要据此放行"没填状态"**：
    /// 那两档本来就不需要指定是哪个状态（不同分档的规则不同）。
    /// </summary>
    public PowerSelection Selection { get; set; } = PowerSelection.PowerId;


    public EffectUpgrade Upgrade { get; set; } = new();

    /// <summary>
    /// 多次升级时每一级的数值（只在 <see cref="CardDef.MaxUpgradeLevel"/> &gt; 1 时读取）。
    ///
    /// 索引即等级：<c>Levels[0]</c> 是**第 1 级**的数值（0 级即卡的初始状态，由上面的
    /// 基础字段表达）。null 项表示"沿用上一级"，数组长度可以短于最大等级。
    ///
    /// 与 <see cref="EffectDef.Upgrade"/> 的关系是**二者择一**：增量模型表达不了
    /// "每级加得不一样"，等级表能；但等级表也不能表达"每级加同样的量"之外的增量语义。
    /// 两个都填时以等级表为准（校验器会提示，避免作者以为两处会叠加）。
    /// </summary>
    public List<EffectLevel?>? Levels { get; set; }

    /// <summary>
    /// 数值是不是算出来的：选了来源就是（否则是固定值）。
    ///
    /// 判据只看来源，没有单独的"数值方式"开关——与重复次数同一套规则，
    /// 界面上也就少一个可能与来源不一致的下拉。
    /// </summary>
    [JsonIgnore]
    public bool IsCalculated => Source.IsUsed;

    /// <summary>
    /// 是不是"让**这张牌自己**从牌堆里出来"（<see cref="EffectKind.AutoPlay"/> 选了"这张牌自己"）。
    ///
    /// 它与"自动打出牌堆里的牌"是同一个效果的两个分支，但生成方式完全不同：前者要走引擎
    /// 专门的自动打出手时机（不是"打出时做什么"），后者才是普通的打出时语句。判断放在模型上，
    /// 生成器与取值器都读它——两处各写一份的话，改了选牌器语义会漏掉一边。
    /// </summary>
    [JsonIgnore]
    public bool IsSelfAutoPlay => Kind == EffectKind.AutoPlay && Pick.Source == Modules.CardPicking.PickSource.ThisCard;

    /// <summary>
    /// 参与计算/生成的基础值。
    ///
    /// 计算型与固定型都是同一个字段 <see cref="EffectDef.Amount"/>：固定型时它就是"数值"，
    /// 计算型时它是"基础值"。基础值填 0 就等于"纯系数乘法"
    /// （原版"全身撞击"是 0 + 1 × 格挡），不需要额外的开关——那个开关（UseBaseValue）
    /// 是旧限制的残留（那时同一张牌的计算型共用同一个基础值槽），已经去掉。
    /// </summary>
    [JsonIgnore]
    public int EffectiveBaseValue => Amount;

    /// <summary>这张效果有没有等级表（多次升级用）。</summary>
    [JsonIgnore]
    public bool HasLevels => Levels is { Count: > 0 };

    /// <summary>
    /// 把这条效果在**某一级**的数值合成为一份确定的状态。
    ///
    /// 0 级就是卡上的基础字段；1 级起逐级叠加 <see cref="Levels"/> 里的覆盖
    /// （没填的项沿用上一级）。生成、描述、校验都读这里，三处不会各推一套。
    ///
    /// **四个角色逐一套用同一条规则**（见 <see cref="ValueRole"/>）：各自取基础字段、
    /// 各自累计系数与来源。所以下面是一个角色一份的局部变量，而不是把数值与次数
    /// 各写一遍——加角色时只加一行。
    /// </summary>
    /// <param name="level">升级等级：0 是未升级。</param>
    public EffectState StateAt(int level)
    {
        var state = new RoleState(this, ValueRole.Value);
        var repeatState = new RoleState(this, ValueRole.Repeat);
        var energyState = new RoleState(this, ValueRole.Energy);
        var starState = new RoleState(this, ValueRole.Star);

        bool hasLevels = Levels is { Count: > 0 };

        // 没有等级表时，"升级后换用的变量"（<see cref="EffectUpgrade.SourceOf"/>）也是一次
        // 绝对选择：从 1 级起整套算式改用另一个变量。有等级表时它不参与（等级表说了算）。
        if (level > 0 && !hasLevels)
        {
            state.ApplyUpgradeSource();
            repeatState.ApplyUpgradeSource();
            energyState.ApplyUpgradeSource();
            starState.ApplyUpgradeSource();
        }

        if (level > 0 && hasLevels)
        {
            for (int i = 0; i < level; i++)
            {
                // 等级表比最大等级短时，多出来的级沿用最后一项（作者不必把每级都写满）。
                EffectLevel? step = i < Levels!.Count ? Levels[i] : Levels[^1];

                if (step is null)
                {
                    continue;
                }

                state.ApplyLevel(step);
                repeatState.ApplyLevel(step);
                energyState.ApplyLevel(step);
                starState.ApplyLevel(step);
            }
        }

        return new EffectState(
            state.Amount, repeatState.Amount, state.Source, repeatState.Source,
            new CostState(energyState.Amount, energyState.Source),
            new CostState(starState.Amount, starState.Source));
    }

    /// <summary>
    /// 合成过程中**一个角色**的可变状态（<see cref="StateAt"/> 的临时累加器）。
    ///
    /// 四个角色共用这一套"取基础字段 → 累计系数与来源 → 按等级覆盖"的逻辑，
    /// 所以写一遍就够；角色的差别只在 <see cref="ValueRole"/> 传给模型的那几个读写。
    /// </summary>
    private struct RoleState
    {
        private readonly EffectDef _effect;
        private readonly ValueRole _role;
        private ValueSource _source;
        private decimal _perUnit;

        public RoleState(EffectDef effect, ValueRole role)
        {
            _effect = effect;
            _role = role;
            Amount = effect.AmountOf(role);
            _source = effect.SourceOf(role).Clone();
            _perUnit = _source.PerUnit;
        }

        public int Amount { get; private set; }

        /// <summary>合成好的来源（系数已按累计值写回）。</summary>
        public ValueSource Source
        {
            get
            {
                _source.PerUnit = _perUnit;
                return _source;
            }
        }

        /// <summary>没有等级表时，从升级里那一次"换用来源"开始。</summary>
        public void ApplyUpgradeSource()
        {
            if (_effect.Upgrade.SourceOf(_role) is ValueSource upgraded)
            {
                _source = upgraded.Clone();
                _perUnit = _source.PerUnit;
            }
        }

        /// <summary>按等级表里这一级填了的项覆盖（null 项沿用上一级）。</summary>
        public void ApplyLevel(EffectLevel step)
        {
            if (step.AmountOf(_role) is int a)
            {
                Amount = a;
            }

            if (step.SourceOf(_role) is ValueSource s)
            {
                _source = s.Clone();
            }

            if (step.PerUnitOf(_role) is decimal p)
            {
                _perUnit = p;
            }
        }
    }

    /// <summary>某个角色在这一级下的基础值（数值角色就是 <see cref="Amount"/>）。</summary>
    public int AmountOf(ValueRole role) => role switch
    {
        ValueRole.Repeat => Repeat,
        ValueRole.Energy => EnergyAmount,
        ValueRole.Star => StarAmount,
        _ => Amount,
    };

    public void SetAmount(ValueRole role, int value)
    {
        switch (role)
        {
            case ValueRole.Repeat: Repeat = value; break;
            case ValueRole.Energy: EnergyAmount = value; break;
            case ValueRole.Star: StarAmount = value; break;
            default: Amount = value; break;
        }
    }

    /// <summary>某个角色在这一级下的变量来源。</summary>
    public ValueSource SourceOf(ValueRole role) => role switch
    {
        ValueRole.Repeat => RepeatSource,
        ValueRole.Energy => EnergySource,
        ValueRole.Star => StarSource,
        _ => Source,
    };

    public void SetSource(ValueRole role, ValueSource value)
    {
        switch (role)
        {
            case ValueRole.Repeat: RepeatSource = value; break;
            case ValueRole.Energy: EnergySource = value; break;
            case ValueRole.Star: StarSource = value; break;
            default: Source = value; break;
        }
    }

    /// <summary>某个角色在某一级下的构成（基础值 + 变量 × 变量系数）。</summary>
    public Modules.Variables.NumericValue NumericOf(ValueRole role, int level = 0)
    {
        EffectState state = StateAt(level);

        return role switch
        {
            ValueRole.Repeat => new Modules.Variables.NumericValue(state.Repeat, state.RepeatSource),
            ValueRole.Energy => new Modules.Variables.NumericValue(state.Energy.Amount, state.Energy.Source),
            ValueRole.Star => new Modules.Variables.NumericValue(state.Star.Amount, state.Star.Source),
            _ => new Modules.Variables.NumericValue(state.Amount, state.Source),
        };
    }

    /// <summary>某一级下的基础值。</summary>
    public int EffectiveBaseValueAt(int level) => StateAt(level).Amount;

    /// <summary>某一级下用的算式来源。</summary>
    public ValueSource SourceAt(int level) => IsCalculated ? StateAt(level).Source : Source;

    /// <summary>
    /// 深复制这条效果。
    ///
    /// 引用类型的字段（来源、升级增量）必须各自复制：共享效果合并时若只复制外层对象，
    /// 引用行与定义就指向同一个 <see cref="ValueSource"/>，改一处会连带改到共享定义——
    /// 那是"共享"变成"互相污染"，比不共享更糟。
    /// </summary>
    public EffectDef Clone()
    {
        var copy = (EffectDef)MemberwiseClone();

        copy.Source = Source.Clone();
        copy.RepeatSource = RepeatSource.Clone();
        copy.EnergySource = EnergySource.Clone();
        copy.StarSource = StarSource.Clone();
        copy.Condition = Condition?.Clone();
        copy.Upgrade = Upgrade.Clone();

        // 等级表里也有 Source 引用，同样要各自复制一份：共享效果被两张卡引用时，
        // 改一处会连带改到另一处（与上面 Source 同一个道理）。
        copy.Levels = Levels?.Select(l => l?.Clone()).ToList();

        return copy;
    }
}
