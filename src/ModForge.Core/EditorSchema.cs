using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.CardSets;
using ModForge.Core.Modules.Effects;
using ModForge.Core.Modules.Targets;
using ModForge.Core.Modules.ValueSources;

namespace ModForge.Core;

/// <summary>
/// 把可编辑的选项告诉界面，这样界面表单由与代码生成器同一套定义生成，不会与后端脱节。
///
/// 每个效果种类都标注了它支持哪些目标、是否需要指定状态或卡牌、是否有数值，
/// 界面据此决定显示哪些输入框。
/// </summary>
public static class EditorSchema
{
    /// <summary>
    /// 效果 → 界面元数据的**唯一**来源。
    ///
    /// 已搬进 <c>Kinds/</c> 的效果从它自己的行为里取（<see cref="EffectSchemaEntry"/>），
    /// 没搬的仍用下面的 <see cref="Describe"/> 旧写法——搬迁是逐批进行的，一批搬完就删掉那批的
    /// 旧条目，最后这里只剩前一句。
    ///
    /// **只列出已由新模块提供的效果**（见 <c>Served</c>）：迁移期间这样能让编辑器只跑新模块，
    /// 未搬的那些不出现在效果下拉里，也就不会走到还没重构的旧实现。
    /// </summary>
    public static object Build()
    {
        // 这个种类是不是已经由新模块（Kinds/）提供。
        static bool Served(EffectSchemaEntry entry) =>
            Enum.TryParse(entry.kind, out EffectKind kind) && EffectCatalog.IsMigrated(kind);

        return new
        {
            effects = new EffectSchemaEntry[]
            {
            // ---- 打出类 ----
            // 伤害 / 格挡 / 回复生命 / 失去生命 / 两个最大生命已搬进 Modules/Effects/Kinds/，
            // 元数据跟着效果走（批 2）。
            EffectCatalog.SchemaOf(EffectKind.DealDamage),
            EffectCatalog.SchemaOf(EffectKind.GainBlock),
            // 抽牌 / 铸造 / 召唤 / 立即执行灾厄判定也已搬进 Kinds/（批 3）。
            EffectCatalog.SchemaOf(EffectKind.DrawCards),

            // 以下六条已搬进 Modules/Effects/Kinds/，元数据跟着效果走。
            EffectCatalog.SchemaOf(EffectKind.GainEnergy),
            EffectCatalog.SchemaOf(EffectKind.GainStars),
            EffectCatalog.SchemaOf(EffectKind.Heal),
            EffectCatalog.SchemaOf(EffectKind.LoseHp),

            // 最大生命：改动跨战斗保留（动的是玩家自己的最大生命值，不是战斗内的临时数值），
            // 所以与"永久增伤"那种需要存档字段的效果不同，不需要额外支持。
            EffectCatalog.SchemaOf(EffectKind.GainMaxHp),
            EffectCatalog.SchemaOf(EffectKind.LoseMaxHp),

            // ---- 状态类 ----
            // 施加状态（批 4）：层数走三件套，要选是哪个状态。
            EffectCatalog.SchemaOf(EffectKind.ApplyPower),

            // 移除状态（批 4）：多出"移除方式（全部/部分）"与"移除哪一类（指定/正面/负面）"
            // 两个下拉，所以元数据里带 hasPowerRemoval；"哪个状态"那一格由界面按"哪一类"
            // 决定隐不隐（选正面/负面时不必指定）。
            EffectCatalog.SchemaOf(EffectKind.RemovePower),

            // ---- 牌堆操作 ----
            // 消耗牌（"消耗这张牌"是它"此牌"那一档）：挑哪些牌由选牌器十步决定，
            // 目的地固定是消耗堆，所以没有"移到哪个牌堆"那一格。
            EffectCatalog.SchemaOf(EffectKind.ExhaustCards),
            // 加入指定卡牌**不挑牌**：加的就是 <cardId> 那一张，加到 <targetPile>。
            // ---- 生成 / 丢弃（批 5）----
            // 加入指定卡牌：两种生成方式（指定牌 / 从卡池 N 选一）由 hasAddMode 声明，
            // 界面按取值切换"指定牌名"与"候选数 + 多维筛选"两组格。
            EffectCatalog.SchemaOf(EffectKind.AddNamedCardToPile),
            EffectCatalog.SchemaOf(EffectKind.DiscardCardsInPile),

            // ---- 充能球（故障机器人）----
            EffectCatalog.SchemaOf(EffectKind.ChannelOrb),
            EffectCatalog.SchemaOf(EffectKind.EvokeOrbs),
            EffectCatalog.SchemaOf(EffectKind.AddOrbSlots),

            // ---- 召唤（亡灵契约师）----
            EffectCatalog.SchemaOf(EffectKind.Summon),

            // ---- 铸造（储君）----
            // 数值是给君王之剑加的伤害量；没有君王之剑时会先生成一张加到手牌。
            EffectCatalog.SchemaOf(EffectKind.Forge),

            // ---- 附魔 / 增伤 / 重放（批 7）----
            EffectCatalog.SchemaOf(EffectKind.EnchantCardsInPile),
            EffectCatalog.SchemaOf(EffectKind.BoostCardDamage),
            EffectCatalog.SchemaOf(EffectKind.AddReplay),

            // ---- 灾厄 ----
            EffectCatalog.SchemaOf(EffectKind.TriggerDoom),

            // ---- 自动打出 ----
            //
            // 一条效果覆盖"打出牌堆里的牌"与"这张牌自己出来"：由"作用对象"选牌器决定，
            // 与"使牌的伤害增加"同一个模式（早先是两个独立的效果种类）。
            //
            //   某个牌堆   → 从该牌堆打出 N 张（要"牌堆"与"张数"）
            //   这张牌自己 → 这张牌从牌堆里出来（原版"轰击"；不要张数，"在哪个牌堆里"
            //                由触发时机的**牌堆范围**给出，所以那一格也不显示）
            //
            // 所以"这张牌自己"这一档不要数值：界面的判据来自选牌策略里的
            // CardPickPolicy.SelfNoAmount（见 CardPickPolicies.For(AutoPlay)）。
            EffectCatalog.SchemaOf(EffectKind.AutoPlay),

            // ---- 费用 / 改造（批 6）----
            EffectCatalog.SchemaOf(EffectKind.ModifyCardCost),
            EffectCatalog.SchemaOf(EffectKind.SetCardsFree),
            EffectCatalog.SchemaOf(EffectKind.UpgradeCardsInPile),
            EffectCatalog.SchemaOf(EffectKind.TransformCardsInPile),
            EffectCatalog.SchemaOf(EffectKind.MoveCardsBetweenPiles),
            EffectCatalog.SchemaOf(EffectKind.CopyCardsToPile),
        }.Where(Served).Select(WithPick).ToArray(),

        // 目标候选的**兜底**：界面实际按每条效果自己的 scope 取（见 EffectSchemaEntry.targets），
        // 这一份只在"界面拿不到那条效果的元数据"时才用得上。给最全的那套（造物轴七档）——
        // 兜底给少了才是真出问题（该有的选择项没了），给多了只是多几个没人选的档。
        targets = TargetCatalog.Names(TargetScope.Creature),
        targetLabels = TargetCatalog.Labels(TargetScope.Creature),
        playedTargetSources = TargetCatalog.PlayedTargetSources(TargetScope.Creature),

        // 界面上归到「玩家宠物」那一组的目标档：界面据此把它们放进一个 optgroup，
        // 而不是与"自身 / 所有敌人"混在同一列里（用户要的"专门一个新组件"）。
        petTargets = TargetCatalog.Options(TargetScope.Creature)
            .Where(TargetCatalog.IsPetBand)
            .Select(t => t.ToString())
            .ToArray(),
        triggers = Enum.GetNames<EffectTrigger>(),
        pileScopes = Enum.GetNames<PileScope>(),
        costDurations = Enum.GetNames<CostDuration>(),
        // 选牌器那几个枚举（来源 / 筛选维度 / 取法 / 费用运算符）；策略里只列可用的那几项。
        // "按条件数牌"用的是**同一份筛选维度与运算符**（多维并存），所以不再单列一套条件枚举；
        // 它只多一个"从哪个范围数"（countScopes）。
        pickSources = Enum.GetNames<Modules.CardPicking.PickSource>(),
        filterDimensions = Enum.GetNames<Modules.CardPicking.FilterDimension>(),
        pickModes = Enum.GetNames<Modules.CardPicking.PickMode>(),

        // 比较方式的候选**排除哨兵值 `None`**（"不筛选"）：这一维启不启用由"维度框里有没有它"
        // 表达，那个下拉只该回答"**怎么比**"。把它列进去会给用户一个自相矛盾的选项——
        // 选了它等于没启用这一维，可那一行还在（界面上就剩一个"不筛选"的下拉）。
        // 枚举本身保留 `None`：它是模型侧的哨兵（`HasFilter` / XML / 描述都按它判"没启用"），
        // 只是**不给用户选**。
        costComparisons = Enum.GetNames<CostComparison>().Where(c => c != nameof(CostComparison.None)).ToArray(),
        transformIntos = Enum.GetNames<TransformInto>(),
        countScopes = Enum.GetNames<CountScope>(),

        // "打出一张牌时"的计数器归零范围（本回合 / 本场战斗）。
        counterResets = Enum.GetNames<CounterReset>(),

        // 移除状态的两个选项（只对"移除状态"有效）：整个移除 / 只减几层；
        // 按 id 指定 / 所有正面 / 所有负面。都是三选一以内的单选。
        powerRemovals = Enum.GetNames<PowerRemoval>(),
        powerSelections = Enum.GetNames<PowerSelection>(),

        // 数值是固定还是算出来的，由「数值来源」决定（选「无」即固定），没有单独的下拉，
        // 所以这里不再下发 amountModes。
        piles = Enum.GetNames<PileKind>(),
        placements = Enum.GetNames<PilePlacement>(),
        valueSourceActors = Enum.GetNames<ValueSourceActor>(),
        orbs = Enum.GetNames<OrbKind>(),
        // 激发哪些球（最右 / 最左 / 全部 / 随机）。名字由界面按界面语言换成中文。
        orbEvokeTargets = Enum.GetNames<OrbEvokeTarget>(),

        // "加入指定卡牌"的生成方式（指定牌 / 从卡池 N 选一）。
        addCardModes = Enum.GetNames<AddCardMode>(),
        cardFilters = Enum.GetNames<CardFilterKind>(),
        enchantments = Enchantments,

        // 数值来源带上中英标签，界面不必自己维护一份名字表。
        valueSourceKinds = Enum.GetValues<ValueSourceKind>()
            .Select(k => new
            {
                value = k.ToString(),
                labelZh = SourceCatalog.Label(k, "zh"),
                labelEn = SourceCatalog.Label(k, "en"),
                // 牌堆与资源与具体生物无关，界面据此隐藏"从谁身上取"这一项。
                playerScoped = SourceCatalog.IsPlayerScoped(k),

                // 这一条要再选"是哪个状态"（状态层数）。界面据此在它后面加一格状态选择框。
                needsPower = SourceCatalog.NeedsPower(k),
            })
            .ToList(),

        cardTypes = Enum.GetNames<CardType>(),
        rarities = Enum.GetNames<CardRarity>(),
        cardTargets = Enum.GetNames<CardTargetType>(),

        // 按玩家人数的限制（"多人游戏卡牌"）。名字与游戏一致，界面直接照这张表画下拉。
        cardMultiplayerConstraints = Enum.GetNames<CardMultiplayerConstraint>(),

        // 启用条件的比较方式（六档）。名字由界面按界面语言换成中文。
        conditionComparisons = Enum.GetNames<ConditionComparison>(),
        keywords = Enum.GetNames<CardKeyword>(),

        // 卡牌标签（选牌流程七）。与"关键词"是两套集合，所以是两张表。
        cardTags = Enum.GetNames<CardTag>(),
        pools = CardPools,
        powerKinds = Enum.GetNames<PowerKind>(),
        powerStacks = Enum.GetNames<PowerStack>(),
        powerTriggers = Enum.GetNames<PowerTrigger>(),
        damageModifiers = Enum.GetNames<DamageModifier>(),
        decayModes = Enum.GetNames<DecayMode>(),
        };
    }

    /// <summary>原版卡牌牌池，也是新卡牌唯一合法的去处。</summary>
    public static IReadOnlyList<string> CardPools { get; } = new[]
    {
        "ironclad", "silent", "defect", "necrobinder", "regent",
        "colorless", "curse", "status", "event", "quest", "token",
    };

    /// <summary>可以附魔的原版附魔。自定义附魔还需要图标资源，所以 v1 只支持原版。</summary>
    public static IReadOnlyList<string> Enchantments { get; } = new[]
    {
        "SHARP", "ADROIT", "CLONE", "CORRUPTED", "GLAM", "GOOPY", "IMBUED", "INKY",
        "INSTINCT", "MOMENTUM", "NIMBLE", "PERFECT_FIT", "ROYALLY_APPROVED", "SLITHER",
        "SLUMBERING_ESSENCE", "SOULS_POWER", "SOWN", "SPIRAL", "STEADY", "SWIFT",
        "TEZCATARAS_EMBER", "VIGOROUS",
    };

    /// <summary>
    /// 还没搬进 <c>Kinds/</c> 的效果的元数据写法（搬迁期间两套并存，一批搬完就少一批）。
    ///
    /// 返回 <see cref="EffectSchemaEntry"/> 而不是匿名对象：匿名对象没法跨类型传递，而搬过来的
    /// 效果必须从它自己的文件里返回同一形状的东西。
    /// </summary>
    /// <summary>
    /// 给一条界面元数据补上**选牌器那十步**的投影。
    ///
    /// 这里是**唯一**一处：搬运后的效果（<c>Kinds/</c> 自己给出 <see cref="EffectSchemaEntry"/>）
    /// 与还没搬的（<see cref="Describe"/> 手写）都要经过它。以前只写在 <c>Describe</c> 里，
    /// 于是搬进 Kinds 的效果拿不到 <c>pick</c>——界面看不到十流程，只能退回旧表单
    /// （消耗牌那种"没用对选牌组件"的现象就是这么来的）。
    ///
    /// 允许哪些流程**由效果自己的策略算出来**（<see cref="CardPickPolicies"/>）：界面据此只显示
    /// 这条效果能用的流程，生成器与它读的是同一份策略。
    /// </summary>
    /// <summary>
    /// 每条元数据都过一遍：补上选牌策略的投影，以及"能不能把结果存进临时牌集合"。
    ///
    /// 两件事都在这里做的理由一样——它们是**按种类算出来**的（策略表 / 集合模块各一处），
    /// 不该让每条行为自己声明，也不该让界面自己维护一份名单。
    /// </summary>
    private static EffectSchemaEntry WithPick(EffectSchemaEntry entry)
    {
        if (!Enum.TryParse(entry.kind, out EffectKind kind))
        {
            return entry;
        }

        return entry with
        {
            pick = CardPickPolicies.UsesPicker(kind) ? PickSchema(kind) : null,
            canStoreSet = CardSetCatalog.Stores(kind),
        };
    }

    private static EffectPickSchema PickSchema(EffectKind kind)
    {
        CardPickPolicy policy = CardPickPolicies.For(kind);

        return new EffectPickSchema(
            sources: policy.Sources.Select(s => s.ToString()).ToArray(),
            dimensions: policy.Dimensions.Select(d => d.ToString()).ToArray(),
            singlePile: policy.SinglePileOnly,
            usesMode: policy.UsesMode,
            usesPrompt: policy.UsesPrompt,
            constraint: policy.Constraint.ToString(),
            selfNoAmount: policy.SelfNoAmount,
            countFromEffect: policy.CountFromEffect);
    }

    /// <summary>
    /// 还没搬进 <c>Kinds/</c> 的效果的界面元数据。搬进去之后改用各行为自己的
    /// <see cref="EffectCatalog.SchemaOf"/>，这条就少一个调用点。
    ///
    /// 目标那一格给的是**作用域**而不是一串候选：候选与出码都归目标模块
    /// （<c>Modules/Targets/</c>），这里只说"这条效果作用于什么"。
    /// 未搬的那十三条全是作用于牌的（谁受影响由选牌器决定），所以一律
    /// <see cref="TargetScope.Cards"/>。
    /// </summary>
    private static EffectSchemaEntry Describe(
        EffectKind kind,
        string labelZh,
        string labelEn,
        TargetScope scope,
        bool amount,
        bool needsPower = false,
        bool needsCard = false,
        bool needsOrb = false,
        bool needsEnchantment = false,
        bool hasPermanent = false,
        CostDuration[]? durations = null,
        bool needsTransform = false,
        bool needsTargetPile = false,
        bool targetPileIsAdd = false) => new EffectSchemaEntry(
        kind: kind.ToString(),
        labelZh: labelZh,
        labelEn: labelEn,
        scope: scope,
        amount: amount,
        needsPower: needsPower,
        needsCard: needsCard,

        // 生成充能球要选球种。
        needsOrb: needsOrb,

        // 附魔要选附魔种类。
        needsEnchantment: needsEnchantment,

        // 这张牌自身的伤害增益要选"仅本场战斗 / 永久"。
        hasPermanent: hasPermanent,

        // 费用类效果要选持续范围（本回合 / 本场战斗 / 直到打出）。
        durations: (durations ?? Array.Empty<CostDuration>()).Select(d => d.ToString()).ToArray(),

        // 变化类效果要选"变成随机牌还是指定牌"；选指定牌时才需要挑牌。
        needsTransform: needsTransform,

        // 搬运类效果要选"移到哪个牌堆"（来源牌堆是常规的 Pile）。
        needsTargetPile: needsTargetPile,

        // "加入"类效果（复制牌并加入牌堆）：TargetPile 是**目的地**，
        // 所以那一格叫"加入哪个牌堆"，而且不给"放入位置"以外的搬运选项。
        targetPileIsAdd: targetPileIsAdd);
}
