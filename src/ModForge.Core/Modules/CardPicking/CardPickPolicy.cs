using ModForge.Core.Model;

namespace ModForge.Core.Modules.CardPicking;

/// <summary>
/// 效果的**合法性约束**——相当于一张黑名单：这类目标根本不进候选集合。
///
/// 它不是我们加的判断，而是游戏自己的合法性：<c>CardCmd.Upgrade</c> 内部跳过不可升级的牌、
/// <c>CardModel.IsTransformable</c> 排除"永恒"、<c>EnchantmentModel.CanEnchant</c> 判附魔能否上。
/// 把它做成声明式的一项，是为了让"选牌界面里该不该出现这张牌"与"结算时能不能动它"用同一个判据。
/// </summary>
public enum CardConstraintKind
{
    /// <summary>不约束。</summary>
    None,

    /// <summary>只选可升级的牌（<c>c.IsUpgradable</c>）。</summary>
    Upgradable,

    /// <summary>只选可变化的牌（<c>c.IsTransformable</c>）。</summary>
    Transformable,

    /// <summary>
    /// 只选可附魔的牌（<c>ModelDb.Enchantment&lt;T&gt;().CanEnchant(c)</c>）。
    ///
    /// 判据必须**用游戏 API**，不能自己判"类型 + 可叠加"那套：可附魔的规则在附魔类型自己身上，
    /// 复刻一份的话，游戏新增附魔或改规则时我们这里会静默不跟。
    /// 同种附魔已有层数时还要能被选中（"把层数加上去"），所以条件是"可附魔**或**已带同种附魔"。
    /// </summary>
    Enchantable,
}

/// <summary>
/// 选牌的**筛选维度**：可以同时生效的那几维（见 <see cref="CardPick"/> 的说明）。
///
/// 策略用它说"这条效果能不能按这一维筛"，界面据此决定"筛选维度"那个多值框里出现哪些候选、
/// 以及每一维的取值行要不要画。
/// </summary>
public enum FilterDimension
{
    /// <summary>按卡牌类型（攻击 / 技能 / 能力 / 状态 / 诅咒 / 任务）。</summary>
    CardType,

    /// <summary>按牌名（可多个；判定按卡牌类型 <c>c is X</c>）。</summary>
    CardId,

    /// <summary>按标签（打击 / 防御 / 小刀 / 仆从 / 奥斯提攻击）。</summary>
    Tag,

    /// <summary>按关键词（消耗 / 虚无 / 固有 / 保留 / 奇巧 / 不能打出 / 永恒）。</summary>
    Keyword,

    /// <summary>按稀有度。</summary>
    Rarity,

    /// <summary>按能量消耗（阈值在效果的 Energy 角色上，见 <see cref="Model.ValueRole"/>）。</summary>
    EnergyCost,

    /// <summary>按星辰消耗（阈值在效果的 Star 角色上）。</summary>
    StarCost,
}

/// <summary>
/// 一条效果允许用选牌流程里的哪些选项——这就是效果模块的"白名单"，
/// 没列进来的选项在界面上不出现，生成器也不会走到。
///
/// 收成一个对象是因为这些限制本来散在 <c>EditorSchema</c> 的四个零散标志里
/// （<c>pickScopes</c> / <c>noAmountScopes</c> / <c>scopeOnly</c> / <c>pileAsScope</c>），
/// 于是界面、生成器、导出器、校验器各判一遍，改一处就会漏掉几处。
/// </summary>
/// <param name="Sources">允许哪几个"在哪儿找"（流程一）。</param>
/// <param name="Dimensions">允许按哪几维筛（可同时生效，维度之间是"与"）。</param>
/// <param name="SinglePileOnly">流程二只能选一个牌堆（消耗 / 弃牌 / 自动打出这类）。</param>
/// <param name="UsesMode">是否启用流程九（全部 / 选择 / 随机 / 至多）与流程十（张数）。</param>
/// <param name="UsesPrompt">
/// 这条效果会不会打开选择界面。会的话导出的语言表**必须**写入
/// <c>&lt;条目&gt;.selectionScreenPrompt</c>——游戏取不到那句提示语时会抛异常，不是"少一句话"。
/// </param>
/// <param name="Constraint">合法性约束（黑名单）。</param>
/// <param name="EnchantmentType">附魔约束时用哪个附魔的 C# 类型引用。</param>
/// <param name="SelfNoAmount">
/// "这张牌自己"那一档**不需要数值**。只有自动打出是这样：选它时打出的就是这张牌本身，
/// 没有"打几张"可言（界面上数值那一组要隐去，填了也不生效）。
/// </param>
/// <param name="CountFromEffect">
/// "挑几张"就是**这条效果自己的数值**（<c>&lt;base&gt;</c> 那三件套：基础值 + 变量 × 变量系数），
/// 由变量模块算好传进来，而不是 <see cref="CardPick.Count"/> 自己那一套。
///
/// 消耗牌是第一条这样定语的效果——它的 base 值就是"消耗几张"（用户定的规则：
/// "每个效果的 base 值即为该效果的基础值，消耗牌的 base 值即为张数基础值"）。
/// 这样升级改张数、把张数写成算出来的（"消耗等于星辰数一半的牌"）都走**同一条**已有通路，
/// 不需要给选牌器另加一套变量与升级增量。
///
/// 其余会挑牌的效果（升级 / 变化 / 搬运 / 附魔 / 复制 / 增伤 / 重放 / 降费 / 免费 / 自动打出）
/// 的数值另有含义（复制几份、附几层、打几次…），所以暂时仍用 <see cref="CardPick.Count"/>；
/// 等它们各自整理时再逐条归到这条规则上。
/// </param>
public sealed record CardPickPolicy(
    PickSource[] Sources,
    FilterDimension[] Dimensions,
    bool SinglePileOnly,
    bool UsesMode,
    bool UsesPrompt,
    CardConstraintKind Constraint = CardConstraintKind.None,
    string? EnchantmentType = null,
    bool SelfNoAmount = false,
    bool CountFromEffect = false);

/// <summary>
/// 每条会挑牌的效果用哪套策略。**一处定义**，界面、生成器、导出器、校验器都读它。
/// </summary>
public static class CardPickPolicies
{
    /// <summary>全部七维筛选，默认都允许（不约束的效果用这一套）。</summary>
    private static readonly FilterDimension[] AllDimensions =
    {
        FilterDimension.CardType, FilterDimension.CardId, FilterDimension.Tag,
        FilterDimension.Keyword, FilterDimension.Rarity,
        FilterDimension.EnergyCost, FilterDimension.StarCost,
    };

    private static readonly PickSource[] PilesOnly = { PickSource.Piles };

    private static readonly PickSource[] PilesOrThisCard = { PickSource.Piles, PickSource.ThisCard };

    /// <summary>
    /// 消耗牌那一档：牌堆 / 此牌 / **临时牌集合**。与自动打出分开，虽然它们考的都是
    /// "从哪儿取"——自动打出**不能**取集合（它的出码直接读牌堆，见 <see cref="For"/> 的说明）。
    /// </summary>
    private static readonly PickSource[] PilesOrThisCardOrSet =
    {
        PickSource.ThisCard, PickSource.Piles, PickSource.Set,
    };

    private static readonly PickSource[] AllSources =
    {
        PickSource.ThisCard, PickSource.Piles, PickSource.Deck, PickSource.Set,
    };

    /// <summary>
    /// 这条效果允不允许挑牌（= 它有没有选牌器）。名单与生成器一致。
    ///
    /// **丢弃牌**也在这里：它的生成代码一直调选牌器（<c>EmitDiscardCards</c> → 选牌出码），
    /// 但这一名单漏了它，于是它既在界面上没有选牌器（填不了挑哪些牌、只能用模型默认），
    /// 又**不写选牌提示语**——而"选择"那一档会弹选择界面，提示语取不到时游戏直接抛异常。
    /// 也就是说：一张"丢弃牌（选择 1 张）"的卡打出去会崩。这条是跟着临时牌集合一起补的。
    /// </summary>
    public static bool UsesPicker(EffectKind kind) => kind is
        EffectKind.CopyCardsToPile or EffectKind.EnchantCardsInPile or EffectKind.BoostCardDamage or
        EffectKind.AddReplay or EffectKind.ModifyCardCost or EffectKind.SetCardsFree or
        EffectKind.UpgradeCardsInPile or EffectKind.TransformCardsInPile or
        EffectKind.MoveCardsBetweenPiles or EffectKind.ExhaustCards or EffectKind.AutoPlay or
        EffectKind.DiscardCardsInPile;

    public static CardPickPolicy For(EffectKind kind) => kind switch
    {
        // 升级：只选可升级的（与 CardCmd.Upgrade 自己的跳过条件一致）。
        EffectKind.UpgradeCardsInPile => new CardPickPolicy(
            AllSources, AllDimensions, SinglePileOnly: false, UsesMode: true, UsesPrompt: true,
            CardConstraintKind.Upgradable),

        // 变化：只选可变化的（与 CardModel.IsTransformable 一致）。
        EffectKind.TransformCardsInPile => new CardPickPolicy(
            AllSources, AllDimensions, SinglePileOnly: false, UsesMode: true, UsesPrompt: true,
            CardConstraintKind.Transformable),

        // 附魔：只选可附魔的，判据用游戏 API（Enchantable 的说明）。类型引用由调用处补。
        EffectKind.EnchantCardsInPile => new CardPickPolicy(
            AllSources, AllDimensions, SinglePileOnly: false, UsesMode: true, UsesPrompt: true,
            CardConstraintKind.Enchantable),

        // 自动打出：只能"从牌堆里取"或"这张牌自己"，而且**没有张数**——
        // 打出哪些是牌堆顺序决定的，不是挑出来的（原版"浩劫"打抽牌堆顶）。
        // "这张牌自己"那一档更没有数值（打出的就是这张牌），所以标 SelfNoAmount。
        //
        // **不给"临时牌集合"这一档**：它的出码是直接从牌堆取顶牌（`autoPlayPile.Cards[…]`），
        // 根本不看候选列表——给了这一档，选了"集合"生成出来的仍然是"从手牌打一张"，
        // 而界面上看不出这一点。
        EffectKind.AutoPlay => new CardPickPolicy(
            PilesOrThisCard, AllDimensions, SinglePileOnly: true, UsesMode: false, UsesPrompt: false,
            SelfNoAmount: true),

        // 消耗：与搬运同构（"挑几张牌，把它们挪去某个地方"），只是目的地固定是消耗堆。
        // 来源限"牌堆 / 这张牌"——从运行牌库（卡组）里消耗一张牌没有原版对照，语义也不清楚
        // （消耗堆是战斗内的东西），要与"从牌库里永久移除"，那是另一条效果。
        //
        // **张数走效果自己的数值**（CountFromEffect）：base 值就是"消耗几张"，
        // 于是升级改张数（升级时效果基础值 +）、张数是算出来的（基础值 + 变量 × 变量系数）
        // 都自动成立，不需要选牌器再带一套变量。
        // "临时牌集合"那一档对消耗牌成立："把刚才抽到的那几张消耗掉"。
        // 它的出码（选牌器 + 逐个挪走）对任何来源都成立，所以给了就是对的。
        EffectKind.ExhaustCards => new CardPickPolicy(
            PilesOrThisCardOrSet, AllDimensions, SinglePileOnly: false, UsesMode: true, UsesPrompt: true,
            CountFromEffect: true),

        // 其余（降费 / 免费 / 复制 / 搬运 / 增伤 / 重放）：不约束目标，四种来源都行。
        _ => new CardPickPolicy(
            AllSources, AllDimensions, SinglePileOnly: false, UsesMode: true, UsesPrompt: true),
    };
}
