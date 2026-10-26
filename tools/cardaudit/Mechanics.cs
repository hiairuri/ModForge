using System.Text.RegularExpressions;

namespace ModForge.CardAudit;

/// <summary>
/// 从反编译源码里认出一张卡"实际做了什么"。
///
/// 为什么扫源码而不是 IL：数值（费用、伤害）走的是已经验证过的 IL 读取
/// （<c>CardStatsReader</c>），那部分不需要重做；而"方法体里调了哪些命令"用源码看得清、
/// 规则也写在明处，比再写一套字节码解释器划算得多。代价是这一步依赖
/// <c>decompiled/</c> 目录存在——它是研究产物、可以随时重新生成，所以这个审计工具是
/// 开发期工具，不作为运行时依赖。
///
/// 规则分三层，逐层给出越来越确定的结论：
///   1. 命令调用（DamageCmd.Attack…）—— 卡确实在做这件事
///   2. 声明的数值类型（DamageVar、PowerVar&lt;T&gt;…）—— 做这件事的量
///   3. 覆写的钩子（AfterCardPlayed…）—— 依赖我们表达不了的时机
/// </summary>
internal static class Mechanics
{
    /// <summary>视觉/音效类调用，不影响机制，识别时忽略。</summary>
    private static readonly string[] CosmeticPrefixes =
    {
        "VfxCmd.", "SfxCmd.", "CreatureCmd.TriggerAnim", "CardCmd.Preview",
        "Cmd.Wait", "Cmd.CustomScaledWait",

        // 铸造的剑光特效：ForgeCmd.Forge 内部自己会播，个别卡（君王之剑本体）直接调的也是纯视觉。
        "ForgeCmd.PlayCombatRoomForgeVfx",
    };

    /// <summary>
    /// 命令 → ModForge 效果种类（或空数组表示我们没有对应效果）。
    ///
    /// 一条命令可能对应多个效果种类（例如 CardCmd.Exhaust 既可能是"消耗这张牌"，
    /// 也可能是"消耗牌堆中的牌"），所以值是一个集合，由调用点的参数再细分。
    ///
    /// 匹配用的是"类型.方法"（不含泛型参数），所以 PowerCmd.Apply&lt;VulnerablePower&gt;
    /// 与 PowerCmd.Apply&lt;WeakPower&gt; 命中同一条规则。
    /// </summary>
    private static readonly (string Pattern, string[] Effects, string? Note)[] CommandRules =
    {
        // ---- 我们有对应效果的 ----
        (@"^DamageCmd\.Attack$", new[] { "DealDamage" }, "攻击（伤害可能是计算型）"),
        (@"^DynamicVars\.Damage\.BaseValue$", new[] { "BoostCardDamage" }, "直接改自身伤害基础值（爪击/撕咬式自增伤）"),
        (@"^CreatureCmd\.Damage$", new[] { "DealDamage", "LoseHp" }, "直接造成伤害；ValueProp 决定是伤害还是失去生命"),
        (@"^CreatureCmd\.GainBlock$", new[] { "GainBlock" }, null),
        (@"^CardPileCmd\.Draw$", new[] { "DrawCards" }, null),
        (@"^PlayerCmd\.GainEnergy$", new[] { "GainEnergy" }, null),
        (@"^PlayerCmd\.GainStars$", new[] { "GainStars" }, null),
        (@"^CreatureCmd\.Heal$", new[] { "Heal" }, null),
        (@"^PowerCmd\.Apply$", new[] { "ApplyPower" }, null),
        (@"^PowerCmd\.Remove$", new[] { "RemovePower" }, null),
        // 消耗一条效果覆盖两种情形（"消耗这张牌"是它的"此牌"那一档，见 ExhaustCards）。
        (@"^CardCmd\.Exhaust$", new[] { "ExhaustCards" }, "作用对象决定消耗哪些牌"),
        (@"^CardCmd\.Discard$", new[] { "DiscardCardsInPile" }, null),
        (@"^CardCmd\.DiscardAndDraw$", new[] { "DiscardCardsInPile", "DrawCards" }, "弃掉若干张再抽若干张"),
        // "移动牌"一条效果覆盖两种情形（作用对象 = 此牌 / 某个牌堆），扫描层面不必再区分。
        (@"^CardPileCmd\.Add$", new[] { "MoveCardsBetweenPiles" }, "把牌移到某个牌堆（作用对象决定搬谁）"),
        (@"^CardPileCmd\.AddGeneratedCardToCombat$", new[] { "AddNamedCardToPile" }, null),
        (@"^CardPileCmd\.AddGeneratedCardsToCombat$", new[] { "AddNamedCardToPile" }, null),
        // 自动打出只有一种效果（"作用对象"区分牌堆里的牌与这张牌自己），
        // 所以两个入口都映射到同一个名字，下面也不用再消歧。
        (@"^CardCmd\.AutoPlay$", new[] { "AutoPlay" }, "作用对象区分自己/别人的牌"),
        (@"^CardPileCmd\.AutoPlayFromDrawPile$", new[] { "AutoPlay" }, null),
        (@"^OrbCmd\.Channel$", new[] { "ChannelOrb" }, null),
        (@"^OrbCmd\.EvokeNext$", new[] { "EvokeOrbs" }, null),
        (@"^OrbCmd\.EvokeLast$", new[] { "EvokeOrbs" }, null),
        (@"^OrbCmd\.AddSlots$", new[] { "AddOrbSlots" }, null),
        (@"^OstyCmd\.Summon$", new[] { "Summon" }, null),
        (@"^DoomPower\.DoomKill$", new[] { "TriggerDoom" }, null),
        (@"^(EnergyCost\.)?SetToFreeThisTurn$", new[] { "SetCardsFree" }, null),
        (@"^(EnergyCost\.)?SetToFreeThisCombat$", new[] { "SetCardsFree" }, null),
        (@"^EnergyCost\.(Set|Add)(UntilPlayed|ThisTurn|ThisCombat|ThisTurnOrUntilPlayed)$", new[] { "ModifyCardCost" }, null),

        // ---- 辅助：本身不是"效果"，而是别的效果的选牌步骤。单列一类，避免混进"缺的操作"。 ----
        (@"^CardSelectCmd\.FromHand$", Array.Empty<string>(), "选牌辅助"),
        (@"^CardSelectCmd\.FromHandForDiscard$", Array.Empty<string>(), "选牌辅助"),
        (@"^CardSelectCmd\.FromHandForUpgrade$", Array.Empty<string>(), "选牌辅助"),
        (@"^CardSelectCmd\.FromCombatPile$", Array.Empty<string>(), "选牌辅助"),
        (@"^CardSelectCmd\.FromChooseACardScreen$", Array.Empty<string>(), "选牌辅助"),

        // ---- 我们目前没有对应效果的 ----
        (@"^CardCmd\.Transform$", new[] { "TransformCardsInPile" }, "变化一张牌"),
        (@"^CardCmd\.TransformTo$", new[] { "TransformCardsInPile" }, "变化成指定牌"),
        (@"^CardCmd\.Upgrade$", new[] { "UpgradeCardsInPile" }, "战斗内升级一张牌"),
        (@"^CardCmd\.TransformToRandom$", new[] { "TransformCardsInPile" }, "随机变化"),
        (@"^CardCmd\.Enchant$", Array.Empty<string>(), "战斗内附魔一张牌"),
        (@"^CardCmd\.ApplyKeyword$", Array.Empty<string>(), "附加关键词"),
        (@"^CardCmd\.RemoveKeyword$", Array.Empty<string>(), "移除关键词"),
        (@"^CardCmd\.ApplySingleTurnSly$", Array.Empty<string>(), "本回合附加奇巧"),
        (@"^CardPileCmd\.RemoveFromDeck$", Array.Empty<string>(), "从牌组里移除"),
        (@"^CardPileCmd\.Shuffle$", Array.Empty<string>(), "洗牌"),
        (@"^ForgeCmd\.Forge$", new[] { "Forge" }, "铸造（储君：生成君王之剑并加伤）"),
        (@"^PlayerCmd\.GainGold$", Array.Empty<string>(), "获得金币"),
        (@"^PlayerCmd\.LoseGold$", Array.Empty<string>(), "失去金币"),
        (@"^PlayerCmd\.LoseEnergy$", Array.Empty<string>(), "失去能量"),
        (@"^PlayerCmd\.EndTurn$", Array.Empty<string>(), "立刻结束回合"),
        (@"^PlayerCmd\.CompleteQuest$", Array.Empty<string>(), "完成任务牌"),
        (@"^PotionCmd\.TryToProcure$", Array.Empty<string>(), "获得药水"),
        (@"^PowerCmd\.ModifyAmount$", Array.Empty<string>(), "直接改状态层数"),
        (@"^PowerCmd\.FindExistingInstanceForStacking$", Array.Empty<string>(), "查找已有状态实例（辅助）"),
        (@"^CreatureCmd\.GainMaxHp$", new[] { "GainMaxHp" }, "提高最大生命"),
        (@"^CreatureCmd\.LoseMaxHp$", new[] { "LoseMaxHp" }, "降低最大生命"),
        (@"^CreatureCmd\.LoseBlock$", Array.Empty<string>(), "移除格挡"),
        (@"^CreatureCmd\.Stun$", Array.Empty<string>(), "击晕"),
        (@"^CreatureCmd\.Kill$", Array.Empty<string>(), "直接击杀"),
        (@"^OrbCmd\.Passive$", Array.Empty<string>(), "触发充能球被动"),
        (@"^BaseReplayCount$", Array.Empty<string>(), "增加重放次数"),
        (@"^OrbCmd\.RemoveSlots$", Array.Empty<string>(), "移除充能球栏位"),
    };

    /// <summary>声明的数值类型 → 它说明这张卡在做什么。</summary>
    private static readonly (string Pattern, string Effect)[] VarRules =
    {
        (@"new DamageVar\(", "DealDamage"),
        (@"new CalculatedDamageVar\(", "DealDamage"),
        (@"new BlockVar\(", "GainBlock"),
        (@"new CalculatedBlockVar\(", "GainBlock"),
        (@"new CardsVar\(", "DrawCards"),
        (@"new EnergyVar\(", "GainEnergy"),
        (@"new StarsVar\(", "GainStars"),
        (@"new HealVar\(", "Heal"),
        (@"new HpLossVar\(", "LoseHp"),
        (@"new SummonVar\(", "Summon"),
        (@"new OstyDamageVar\(", "DealDamage"),
        (@"new ForgeVar\(", "*Forge"),
        (@"new RepeatVar\(", "*Repeat"),
        (@"new PowerVar<", "ApplyPower"),
        (@"new CalculationBaseVar\(", "*Calculated"),
        (@"new ExtraDamageVar\(", "*Calculated"),
        (@"new CalculationExtraVar\(", "*Calculated"),
    };

    /// <summary>
    /// 覆写的钩子里，**对应我们自己的某个触发时机**的那些。
    ///
    /// 这些钩子本身不构成"实现方式不同"：我们的效果本来就要发同名钩子来落地
    /// （"被消耗时"发出的就是 <c>AfterCardExhausted</c>，"自动打出这张牌"发出的就是
    /// <c>AfterAutoPrePlayPhaseEnteredEarly</c> / <c>AfterAutoPostPlayPhaseEntered</c>）。
    /// 不区分的话，凡是用到这些时机的原版卡都会被误报成"我们做不到"——实测：
    /// 彼岸咆哮（回合结束自动打出）、战鼓（被消耗时获得能量）、"回合结束时若在手牌中"
    /// 那一整类诅咒牌，全部误报。
    /// </summary>
    private static readonly string[] HooksWeEmit =
    {
        "AfterCardExhausted",   // 我们的"被消耗时"时机
        "AfterCardDrawn",       // 我们的"抽到时"时机
        "AfterAutoPrePlayPhaseEnteredEarly", "AfterAutoPostPlayPhaseEntered",  // "自动打出这张牌"
        "OnTurnEndInHand",      // "回合结束时若在手牌中"
    };

    /// <summary>
    /// 这些钩子我们的时机**能覆盖主要行为，但有一处已知差异**：既不算"做不到"，
    /// 也不该混进"完全等价"里，所以单独给一句说明写进报告。
    ///
    /// 值的第一项是**我们对应的时机名**，第二项是差异说明。
    ///
    /// <c>AfterCardEnteredCombat</c>：原版用它做"这张牌是战斗中途中生成的，把之前已经发生的
    /// 那些补算上"（女妖之嚎 / 精密瞄准 / 踩踏 / 重压都这么写：进入战斗牌堆时先按历史补一次）。
    /// 我们的计数从这张牌进入战斗牌堆那一刻起算，所以**战斗中生成的**这类牌会比原版少算
    /// 之前打出的张数。战斗开始时就在牌堆里的牌不受影响——那也是绝大多数情况。
    ///
    /// <c>BeforeSideTurnStart</c> / <c>AfterPlayerTurnStart</c>：引擎的回合开始其实有三个钩子，
    /// 顺序是 BeforeSideTurnStart →（抽牌）→ AfterPlayerTurnStart → AfterSideTurnStart。
    /// 我们只发最后那个，所以"回合开始时"的效果会晚于原版的 BeforeSideTurnStart 一点点——
    /// 对"从弃牌堆取一张牌"这类效果结果相同（弃手牌发生在上一回合结束时，那时牌已经在弃牌堆里），
    /// 差别只在"与抽牌的先后"。写进报告让人知道往哪个方向偏。
    /// </summary>
    private static readonly Dictionary<string, (string Ours, string Difference)> HooksWithKnownDifference =
        new(StringComparer.Ordinal)
        {
            ["AfterCardEnteredCombat"] = ("打出一张牌时", "战斗中生成的这张牌会少算之前打出的张数"),
            ["BeforeSideTurnStart"] = ("回合开始时", "原版在抽牌之前，我们在抽牌之后"),
            ["AfterPlayerTurnStart"] = ("回合开始时", "原版紧跟抽牌，我们与它相邻（同属抽牌之后）"),
        };

    /// <summary>
    /// 这些"反应任意牌"的钩子现在由**"打出一张牌时"这个时机**覆盖，所以不再算阻塞项。
    ///
    /// 原版用它表达"每打出一张X就…"（精密瞄准、踩踏、女妖之嚎），我们新加的
    /// <c>EffectTrigger.CardPlayed</c> + 卡牌筛选 + 可选计数器正是同一件事。
    ///
    /// 两点**不是逐字等价**，核对时要心里有数（写在报告里，不当作阻塞）：
    ///
    /// 1. 原版有的配合 <c>AfterCardEnteredCombat</c> 做"进入战斗时按历史补算"，
    ///    用来补上"这张牌是战斗中途中生成的"那种情况（女妖之嚎/精密瞄准/踩踏）。
    ///    我们按"进入战斗牌堆之后发生的出牌"计数，战斗开始时在牌堆里的牌不受影响，
    ///    只有**战斗中生成**的那种牌少算之前已经打出的张数。
    /// 2. 踩踏用的是 <c>BeforeCardPlayed</c>（在被那张牌结算之前），我们统一在
    ///    <c>AfterCardPlayed</c>（结算之后）。它那样写只是为了抢在那张牌之前，
    ///    对"降了多少费"的结果没有差别。
    /// </summary>
    private static readonly string[] HooksOurCardPlayedTriggerCovers =
    {
        "AfterCardPlayed", "AfterCardPlayedLate", "BeforeCardPlayed",
    };

    /// <summary>
    /// 覆写的钩子 → 意味着依赖某个"我们表达不了"的时机。
    ///
    /// 这些不是错误，而是"实现方式不同"：原版可以直接覆写任意钩子，而我们的卡牌效果
    /// 只覆盖有限几个时机。超出这些的卡，要么做不到，要么得改写成状态。
    ///
    /// **但要先排除三件事**（都会把能实现的卡误报成做不到）：
    ///
    /// 1. <see cref="HooksWeEmit"/> 里的钩子——那正是我们自己的时机在底层对应的钩子；
    /// 2. 只对"这张牌自己"生效的那种覆写（带 <c>card != this</c> / <c>cardPlay.Card != this</c>
    ///    判断）。原版"触发自身"的钩子与我们的一条同名效果是一回事；
    ///    反之，**不加自身判断**的覆写是在反应"任意一张牌被打出/被消耗"。
    /// 3. <see cref="HooksOurCardPlayedTriggerCovers"/> 里的钩子——"反应任意牌"这个语义
    ///    现在有对应时机了（见该字段的说明）。
    /// </summary>
    private static readonly string[] KnownHookOverrides =
    {
        "AfterCardPlayed", "AfterCardPlayedLate",
        "AfterCardEnteredCombat", "AfterCardGeneratedForCombat",
        "BeforeHandDraw", "BeforeSideTurnEnd", "BeforeCardPlayed", "BeforeCardRemoved",
        "AfterAutoPrePlayPhaseEntered",
        "AfterDeath", "AfterCombatEnd", "AfterTransformedFrom",
        "AfterCreated", "AfterMapGenerated", "ShouldPlay", "OnEnqueuePlayVfx",
        "TryModifyRestSiteOptions",
    };

    /// <summary>
    /// 数值来源：把 WithMultiplier 的 lambda 体映射到我们的 ValueSourceKind。
    /// 这是"计算型数值"的关键——决定我们能不能用同样的方式表达。
    /// </summary>
    private static readonly (string Pattern, string Source)[] ValueSourceRules =
    {
        (@"PileType\.Exhaust\.GetPile\([^)]*\)\.Cards\.Count", "ExhaustPileCount"),
        (@"PileType\.Draw\.GetPile\([^)]*\)\.Cards\.Count", "DrawPileCount"),
        (@"PileType\.Discard\.GetPile\([^)]*\)\.Cards\.Count", "DiscardPileCount"),
        (@"PileType\.Hand\.GetPile\([^)]*\)\.Cards\.Count", "HandCount"),
        (@"\.Hand\.Cards\.Count", "HandCount"),
        (@"GetPower<StrengthPower>\(\)", "Strength"),
        (@"GetPower<DexterityPower>\(\)", "Dexterity"),
        (@"GetPower<PoisonPower>\(\)", "Poison"),
        (@"GetPower<VulnerablePower>\(\)", "Vulnerable"),
        (@"GetPower<WeakPower>\(\)", "Weak"),
        (@"PlayerCombatState\??\.Energy", "Energy"),
        (@"PlayerCombatState\??\.Stars", "Stars"),
        (@"\.CurrentHp", "CurrentHp"),
        (@"\.MaxHp", "MaxHp"),
        (@"\.MissingHp", "MissingHp"),
        (@"\.Block", "Block"),
    };

    /// <summary>
    /// 这张卡的计算型数值用到了我们**没有**的来源。
    ///
    /// 我们支持的来源是"整堆张数 / 状态层数 / 生物属性 / 玩家资源"。超出这个范围的两类
    /// 做不到，要如实报出来：
    ///
    ///   过滤计数 —— 数"手牌里攻击牌有几张""带打击标签的有几张"（原版"完美打击"就是后者）
    ///   战斗历史 —— 数"本回合打了几张技能牌""抽了几张""受到过几次伤害"，要读
    ///               CombatManager.Instance.History
    ///
    /// 判据是 multiplier 的 lambda 体里出现了 `Count(...)` 的**带谓词**重载，或者
    /// 直接引用战斗历史 / 充能球队列。
    /// </summary>
    private static IEnumerable<string> UnsupportedValueSources(string source)
    {
        // 取出 WithMultiplier 的实参（到匹配的右括号为止）。
        foreach (Match m in Regex.Matches(source, @"\.WithMultiplier\s*\("))
        {
            int start = m.Index + m.Length;
            int depth = 1;
            int i = start;

            while (i < source.Length && depth > 0)
            {
                if (source[i] == '(')
                {
                    depth++;
                }
                else if (source[i] == ')')
                {
                    depth--;
                }

                i++;
            }

            string body = source[start..Math.Min(i, source.Length)];

            // .Count(…) 带谓词 = 按条件数牌（整堆计数是 .Cards.Count 无参形式，那个我们支持）。
            // 按条件数牌我们现在有了（FilteredCardCount），但只覆盖原版实际用到的那几类条件。
            //
            // 只在**不是**历史计数时报这一条：历史计数的写法必然也是带谓词的 Count（原版"扯碎"
            // 数"真的扣到血了的那几次"就是 `.Count(e => … UnblockedDamage > 0)`），
            // 那种由下面的历史分支单独判定，两条一起报会重复且互相矛盾。
            bool historyCount = body.Contains("History", StringComparison.Ordinal);

            if (!historyCount &&
                Regex.IsMatch(body, @"\.Count\s*\([^)]") &&
                !IsSupportedCountFilter(body))
            {
                yield return "超出范围的按条件数牌（我们只支持标签/类型/关键词/星辰这几类条件）";
            }

            // 战斗历史：我们支持的口径见 IsSupportedHistoryCount——五种条目类型
            // （伤害 / 攻击 / 消耗 / 弃置 / 生成 / 抽牌 / 打牌）里"自己维度"的计数与求和。
            // 按别人筛（"击中该敌人几次"、奥斯提的攻击、按牌的类型数）仍做不到。
            if (historyCount && !IsSupportedHistoryCount(body))
            {
                yield return "按战斗历史计数作为数值来源（超出我们支持的条目或筛选维度）";
            }

            if (body.Contains("OrbQueue", StringComparison.Ordinal))
            {
                yield return "按充能球数量作为数值来源（我们没有）";
            }
        }
    }

    /// <summary>
    /// 这个"按战斗历史计数"是不是我们支持的那几种。
    ///
    /// 我们支持的口径（与 <c>ValueSourceKind</c> 的历史计数一一对应）：
    ///   打牌数   —— CardPlaysFinished（本回合 / 本场战斗，无字段过滤）
    ///   抽牌数   —— CardDrawnEntry（本回合 / 本场战斗）
    ///   失去生命次数 / 受到的伤害量 —— DamageReceivedEntry 且 UnblockedDamage 口径
    ///                                 （"扯碎""猖狂"都是这个形状）
    ///   受击次数 —— DamageReceivedEntry 按"接收者是自己"筛（被格挡也算）
    ///   造成的伤害量 —— DamageReceivedEntry 按"来源是自己"筛
    ///   消耗 / 弃置牌数 —— CardExhaustedEntry / CardDiscardedEntry 且 Card.Owner 是自己
    ///                      （"邪眼""遗忘仪式""铭记死亡"）
    ///   生成牌数 —— CardGeneratedEntry 且 Creator 是自己（"超大质量体"）
    ///   攻击次数 —— CreatureAttackedEntry 且 Actor 是**自己的生物**
    ///               （"猛晃"数的是奥斯提的攻击——奥斯提是独立的生物，不在口径内）
    ///
    /// 唯一不支持的 DamageReceivedEntry 形状是**按别人筛**的（最常见的是
    /// <c>Receiver == target</c>："你击中该敌人几次"——那是目标维度的计数）。
    /// </summary>
    private static bool IsSupportedHistoryCount(string body)
    {
        // 目标维度的计数（"击中该敌人几次"这类）：我们所有历史来源都是自己维度的。
        bool targetScoped = Regex.IsMatch(body, @"Receiver\s*==\s*target\b");

        if (body.Contains("DamageReceivedEntry", StringComparison.Ordinal))
        {
            return !targetScoped;
        }

        // 消耗 / 弃置：按"牌的主人是谁"筛。Owner 是自己 → 支持；筛别的（某张具体的牌等）→ 不支持。
        if (body.Contains("CardExhaustedEntry", StringComparison.Ordinal) ||
            body.Contains("CardDiscardedEntry", StringComparison.Ordinal))
        {
            return Regex.IsMatch(body, @"Card\.Owner\s*==");
        }

        // 生成：按"创建者是谁"筛。
        if (body.Contains("CardGeneratedEntry", StringComparison.Ordinal))
        {
            return Regex.IsMatch(body, @"Creator\s*==");
        }

        // 攻击次数：Actor 必须是自己的生物。奥斯提（Owner.Osty）是另一个生物，不支持。
        if (body.Contains("CreatureAttackedEntry", StringComparison.Ordinal))
        {
            return Regex.IsMatch(body, @"Actor\s*==\s*[\w.]*Owner\.Creature\b")
                && !body.Contains("Owner.Osty", StringComparison.Ordinal);
        }

        // 打牌数：必须**没有**对单个条目的字段过滤（CardPlay.Card.Type/Tags/Keywords 之外
        // 的 Owner 过滤是我们支持的口径——"自己打出的牌数"）。
        if (Regex.IsMatch(body, @"CardPlaysFinished"))
        {
            return !Regex.IsMatch(body, @"CardPlay\.Card\.(Type|Tags|Keywords)");
        }

        // 抽牌数：原版抽牌计数都是无类型过滤的（!FromHandDraw 与 Actor 不算过滤）。
        if (body.Contains("CardDrawnEntry", StringComparison.Ordinal))
        {
            return !Regex.IsMatch(body, @"\bType\b");
        }

        return false;
    }

    /// <summary>
    /// 这个"按条件数牌"的谓词是不是我们支持的那几种。
    ///
    /// 我们支持的（见 <c>CardFilterKind</c>）：标签（Strike/Defend/Shiv/OstyAttack）、
    /// 类型（Attack/Skill/Power）、关键词（消耗/虚无/固有/保留/奇巧/不能被打出/永恒）、
    /// 需要星辰。超出这些的（例如"是某张具体的牌"、"带某种附魔"）还做不到，要如实报出来。
    /// </summary>
    private static bool IsSupportedCountFilter(string body) =>
        Regex.IsMatch(body, @"Tags\.Contains\(CardTag\.(Strike|Defend|Shiv|OstyAttack)\)")
        || Regex.IsMatch(body, @"Type\s*==\s*CardType\.(Attack|Skill|Power)")
        || Regex.IsMatch(body, @"Keywords\.Contains\(CardKeyword\.\w+\)")
        || Regex.IsMatch(body, @"CanonicalStarCost|HasStarCostX");

    /// <summary>一张卡里认出来的东西。</summary>
    internal sealed record ScanResult(
        IReadOnlyList<string> Commands,
        IReadOnlyList<string> RecognizedEffects,
        IReadOnlyList<string> Unsupported,
        IReadOnlyList<string> Helpers,
        IReadOnlyList<string> UnknownCommands,
        IReadOnlyList<string> HookOverrides,
        IReadOnlyList<string> ValueSources,
        bool CosmeticOnly,

        /// <summary>这张卡施加的状态类名（<c>PowerCmd.Apply&lt;XPower&gt;</c> 的泛型参数）。</summary>
        IReadOnlyList<string> AppliedPowers);

    /// <summary>
    /// 卡牌施加的状态类名。
    ///
    /// 必须跟进状态类去看，否则**能力牌会全部误判**：能力牌的卡类通常只有一句
    /// <c>PowerCmd.Apply&lt;XPower&gt;</c>，真正的行为（"每消耗一张牌抽一张""每获得格挡造成伤害"）
    /// 写在状态类里。只扫卡类的话，"黑暗之拥"看起来就是"施加一个状态"——能实现，
    /// 而实际上它需要一个"牌被消耗时"的状态触发，那个我们没有。
    /// </summary>
    internal static IReadOnlyList<string> AppliedPowerTypes(string source) =>
        Regex.Matches(source, @"PowerCmd\.Apply\s*<\s*([A-Za-z0-9_.]+)\s*>")
            .Select(m => m.Groups[1].Value[(m.Groups[1].Value.LastIndexOf('.') + 1)..])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 我们的状态（<c>PowerDef</c>）能发出的钩子。
    ///
    /// 状态触发只有四个时机（回合开始/结束、打出牌、受到伤害），加上两个伤害修正钩子；
    /// 之外的状态钩子我们表达不了，而这类卡在原版里很常见（见 <see cref="PowerUnsupportedHooks"/>）。
    /// </summary>
    private static readonly string[] PowerHooksWeEmit =
    {
        "AfterSideTurnStart",       // PowerTrigger.TurnStart
        "AfterSideTurnEnd",         // PowerTrigger.TurnEnd
        "AfterCardPlayed",          // PowerTrigger.CardPlayed
        "AfterDamageReceived",      // PowerTrigger.DamageTaken
        "ModifyDamageAdditive",     // DamageModifier.Additive
        "ModifyDamageMultiplicative", // DamageModifier.Multiplicative
    };

    /// <summary>
    /// 一个状态类覆写、而我们的状态表达不了的钩子。
    ///
    /// 只看"真的覆写了钩子"：一个状态若只声明层数与数值（"获得 2 点力量"），它自己没有行为，
    /// 我们的"施加状态"就够了，不该报成做不到。
    ///
    /// 扫三类名字，因为原版的状态钩子不止 After/Before：
    ///   After*  / Before*  —— 事件通知（被消耗、回合开始、抽牌…）
    ///   Modify*            —— 改数值（改伤害、改格挡、改抽牌数、改能量上限…）
    ///   Should*            —— 回答"要不要发生"（<c>ShouldClearBlock</c>：格挡是否在回合开始时消失，
    ///                          "壁垒"就是它；这类我们完全没有对应能力）
    /// </summary>
    internal static IReadOnlyList<string> PowerUnsupportedHooks(string powerSource)
    {
        var result = new List<string>();

        var scan = new Regex(
            @"\boverride\s+(?:async\s+)?(?:Task|decimal|int|bool|void)\s+(?:[A-Za-z<>,\[\]\?\.]+\s+)?(?<name>(?:After|Before|Modify|Should)[A-Za-z0-9_]*)\s*\(");

        foreach (Match m in scan.Matches(powerSource))
        {
            string hook = m.Groups["name"].Value;

            // 我们能发的、纯表现的、以及"能覆盖但有差异"的都不算阻塞。
            if (PowerHooksWeEmit.Contains(hook, StringComparer.Ordinal) ||
                CosmeticHooks.Contains(hook, StringComparer.Ordinal) ||
                HooksWithKnownDifference.ContainsKey(hook))
            {
                continue;
            }

            if (!result.Contains(hook, StringComparer.Ordinal))
            {
                result.Add(hook);
            }
        }

        return result.OrderBy(h => h, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 把"按参数区分"的命令落到实处。
    ///
    /// 两条命令只看名字分不出对象：
    ///   CardCmd.AutoPlay(ctx, this, …)     打出的是自己
    ///   CardCmd.AutoPlay(ctx, item, …)     打出的是牌堆里的牌
    ///   CardCmd.Exhaust(ctx, this, …)      消耗的是自己
    ///   CardCmd.Exhaust(ctx, item, …)      消耗的是牌堆里的牌
    ///
    /// 规则表里两条效果都列出来只是"有可能"，会同时命中；这里按实参把不可能的去掉，
    /// 免得把轰击（只打出自己）报成两种效果。
    /// </summary>
    private static void Disambiguate(string source, SortedSet<string> effects, SortedSet<string> hooks, SortedSet<string> unsupported)
    {
        // 自动打出不用消歧：无论"牌堆里的牌"还是"这张牌自己"，都是同一个 AutoPlay 效果
        // （编辑器里由"作用对象"区分）。早先它是两个独立的效果种类，才需要按实参去掉一个。

        // 消耗：同理，不用消歧——"消耗这张牌"与"消耗牌堆里的牌"现在是同一条效果
        // （ExhaustCards，"消耗哪些牌"由作用对象决定）。早先它是两个种类，才需要按实参去掉一个。

        // CardPileCmd.Add 的第一个实参不是 this 时，是在移动**别的牌**——例如"头槌"
        // 把选中的弃牌堆牌移到抽牌堆顶。现在两种情形都是同一条效果（MoveCardsBetweenPiles，
        // "搬谁"由作用对象决定），所以这里不需要再做什么——上面的规则表已经给出它了。

        // 这张牌自己带"消耗"关键词时，"消耗这张牌"不必再当成一条效果——它是关键词。
        // 只在该卡**没有**别的消耗线索（选牌器 / 某个牌堆）时才抹掉，
        // 否则会把"消耗 X 张牌"那种真效果一起抹了。
        if (Regex.IsMatch(source, @"CardKeyword\.Exhaust") && effects.Contains("ExhaustCards"))
        {
            bool picksOtherCards = Regex.IsMatch(source, @"(CardSelectCmd\.[A-Za-z]+\s*\(|PileType\.\w+\.GetPile)");

            if (!picksOtherCards)
            {
                effects.Remove("ExhaustCards");
            }

            hooks.Add("ExhaustKeyword");
        }

        // CreatureCmd.Damage：看它读的是哪个数值变量，这是游戏自己的区分方式。
        //
        //   DynamicVars.HpLoss  → 失去生命（原版一律配 Unblockable|Unpowered，不受格挡与增伤影响）
        //   DynamicVars.Damage  → 真正的伤害（会被格挡、被增伤）
        //
        // 两者差别很大（例如一张"失去 3 点生命"被当成"造成 3 点伤害"，就会被格挡挡掉、
        // 也被力量加成），所以必须区分。一张牌同时出现两次调用时（例如"血液动力"既
        // 失去生命又造成伤害），两个效果都是真的，不算歧义。
        //
        // DamageCmd.Attack 永远是伤害，不适用这条。
        if (Regex.IsMatch(source, @"CreatureCmd\.Damage\s*\("))
        {
            bool usesHpLoss = Regex.IsMatch(source, @"DynamicVars\.HpLoss\b");
            bool usesDamage = Regex.IsMatch(source, @"DynamicVars\.Damage\b");

            if (usesHpLoss && !usesDamage)
            {
                effects.Remove("DealDamage");
            }
            else if (usesDamage && !usesHpLoss)
            {
                effects.Remove("LoseHp");
            }
        }
    }

    /// <summary>
    /// 纯表现层的钩子：只播特效/音效/动画，不改变任何玩法状态。
    ///
    /// 覆写它们不构成"我们做不到"——我们的效果本来就不生成入场特效，卡牌照样能正常用，
    /// 只是少了点视觉反馈。实测：原版"燃烧"（获得力量）唯一的钩子就是
    /// <c>OnEnqueuePlayVfx</c>（播一团火），把它当成阻塞项会把这张完全能实现的牌误报。
    /// </summary>
    private static readonly string[] CosmeticHooks =
    {
        "OnEnqueuePlayVfx",
    };

    /// <summary>
    /// 这几种钩子"只对自己生效"是我们的时机、"对任意牌生效"就不是。判定看有没有自身判断。
    /// </summary>
    private static readonly string[] SelfOnlyHooks =
    {
        "AfterCardPlayed", "AfterCardPlayedLate", "AfterCardEnteredCombat", "AfterCardExhausted",
    };

    /// <summary>
    /// **这个覆写**有没有"只对这张牌自己生效"的判断。
    ///
    /// 必须按覆写逐个判，不能拿整份源码判：一张牌上可能既有"只对自己"的钩子、
    /// 又有"对任意牌"的钩子（实测"女妖之嚎"：<c>AfterCardEnteredCombat</c> 只对自己、
    /// <c>AfterCardPlayed</c> 对任意牌的以太牌生效）。按整份文件判会用一个钩子的判断
    /// 去cover另一个，把"反应任意牌"的那种误判成我们做得到。
    ///
    /// 原版两种写法都见过：
    ///   if (cardPlay.Card != this) return;        只对自己（＝我们的一条效果）
    ///   if (cardPlay.Card.Owner != base.Owner) …  对主人的任意牌（我们表达不了）
    /// </summary>
    private static bool IsSelfFiltered(string source, string hook)
    {
        Match signature = Regex.Match(source, $@"\boverride\s+[A-Za-z<>,\[\]\?\. ]+\s+{hook}\s*\(");

        if (!signature.Success)
        {
            return false;
        }

        // 从签名起取到下一个覆写（或文件末尾），这段就是该方法体。
        int bodyStart = signature.Index;
        Match next = Regex.Match(source[bodyStart..], @"\n\s*\S*override\s+");
        string body = next.Success && next.Index > 0
            ? source[bodyStart..(bodyStart + next.Index)]
            : source[bodyStart..];

        return body.Contains("cardPlay.Card != this", StringComparison.Ordinal)
            || body.Contains("cardPlay.Card != base.Card", StringComparison.Ordinal)
            || body.Contains("card != this", StringComparison.Ordinal)
            || body.Contains("card != base.Card", StringComparison.Ordinal)
            || body.Contains("card == this", StringComparison.Ordinal)
            || body.Contains("ReferenceEquals(card, this)", StringComparison.Ordinal);
    }

    internal static ScanResult Scan(string source)
    {
        var commands = new List<string>();
        var effects = new SortedSet<string>(StringComparer.Ordinal);
        var unsupported = new SortedSet<string>(StringComparer.Ordinal);
        var helpers = new SortedSet<string>(StringComparer.Ordinal);
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        var hooks = new SortedSet<string>(StringComparer.Ordinal);
        var sources = new SortedSet<string>(StringComparer.Ordinal);

        // ---- 命令调用 ----
        //
        // 泛型参数要一起吃掉：PowerCmd.Apply<VulnerablePower>(…) 与
        // PowerCmd.Apply<WeakPower>(…) 是同一条规则；只截到 "PowerCmd.Apply" 也能匹配，
        // 但把泛型留在名字里，报告里能看出这张卡加的是哪个状态。
        foreach (Match m in Regex.Matches(source, @"\b([A-Z][A-Za-z]*Cmd)\.([A-Za-z]+)\s*(<[^<>\n]{0,60}>)?"))
        {
            string full = m.Groups[1].Value + "." + m.Groups[2].Value;

            if (CosmeticPrefixes.Any(c => full.StartsWith(c, StringComparison.Ordinal)))
            {
                continue;
            }

            commands.Add(full);
        }

        commands = commands.Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToList();

        // ---- 直接改属性的写法 ----
        //
        // 少数卡不走 Cmd，而是直接改对象上的属性，例如：
        //   cardModel.BaseReplayCount += 2      授予重放（原版隐藏宝石）
        //   DynamicVars.Damage.BaseValue += x   自身伤害增加（爪击/撕咬）
        // 这些在按 "XxxCmd." 提取时抓不到，要单独认。
        if (Regex.IsMatch(source, @"\.BaseReplayCount\s*(\+=|=)"))
        {
            commands.Add("BaseReplayCount");
        }

        if (Regex.IsMatch(source, @"DynamicVars\.Damage\.BaseValue\s*(\+=|=)"))
        {
            commands.Add("DynamicVars.Damage.BaseValue");
        }

        commands = commands.Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToList();

        // ---- 非 *Cmd 的 API 调用 ----
        //
        // 有两种效果不走 Cmd 类，而是直接调卡牌/费用对象上的方法，例如：
        //   card.SetToFreeThisTurn()            免费（原版子弹时间）
        //   base.EnergyCost.AddThisCombat(-1)   降费
        // 上面那轮按 "XxxCmd." 提取抓不到它们，所以这里单独扫一遍。
        foreach (Match m in Regex.Matches(
            source, @"\.((?:EnergyCost\.)?SetToFreeThisTurn|(?:EnergyCost\.)?SetToFreeThisCombat|EnergyCost\.(?:Set|Add)(?:UntilPlayed|ThisTurn|ThisCombat|ThisTurnOrUntilPlayed))\s*\("))
        {
            string method = m.Groups[1].Value;

            // 报告里保留 "EnergyCost." 前缀（能看出是费用类），免费那两个只留方法名。
            commands.Add(method.StartsWith("EnergyCost.", StringComparison.Ordinal)
                ? "EnergyCost." + method["EnergyCost.".Length..]
                : method);
        }

        commands = commands.Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToList();

        // 每条命令按规则归类；没命中任何规则的记进"未识别"，那是要人工看的部分。
        foreach (string cmd in commands)
        {
            bool matched = false;

            foreach ((string pattern, string[] mapped, string? note) in CommandRules)
            {
                if (!Regex.IsMatch(cmd, pattern))
                {
                    continue;
                }

                matched = true;

                if (mapped.Length == 0)
                {
                    // "选牌辅助"是别的效果的组成部分，不是"我们缺的操作"，单列一类。
                    if (note == "选牌辅助")
                    {
                        helpers.Add(cmd);
                    }
                    else
                    {
                        unsupported.Add(note is null ? cmd : $"{cmd}（{note}）");
                    }
                }
                else
                {
                    foreach (string e in mapped)
                    {
                        effects.Add(e);
                    }
                }
            }

            if (!matched)
            {
                unknown.Add(cmd);
            }
        }

        // ---- 声明的数值类型 ----
        foreach ((string pattern, string effect) in VarRules)
        {
            if (Regex.IsMatch(source, pattern))
            {
                if (effect.StartsWith('*'))
                {
                    // 前缀 * 的是"标记性"的：它本身不是效果，只是告诉审计师这张卡
                    // 用了计算型数值 / 重复次数 / 铸造，需要单独看一眼。
                    hooks.Add(effect[1..]);
                }
                else
                {
                    effects.Add(effect);
                }
            }
        }

        // ---- 两条"按参数区分"的命令 ----
        //
        // 光看命令名分不出是"自己"还是"别人的牌"，规则表里两条都列了，但那会把
        // 轰击（只打出自己）报成两条效果。这里按实参细分，让报告可信。
        Disambiguate(source, effects, hooks, unsupported);

        // ---- 覆写的钩子 ----
        // 先收下 HooksWeEmit 里的（我们自己发得出这些时机），它们不算阻塞。
        foreach (string hook in HooksWeEmit)
        {
            if (Regex.IsMatch(source, $@"\boverride\s+[A-Za-z<>,\[\]\?\. ]+\s+{hook}\s*\("))
            {
                hooks.Add(hook);
            }
        }

        foreach (string hook in KnownHookOverrides)
        {
            if (!Regex.IsMatch(source, $@"\boverride\s+[A-Za-z<>,\[\]\?\. ]+\s+{hook}\s*\("))
            {
                continue;
            }

            // 纯表现层的不算（我们的卡不生成入场特效，照样能用）。
            if (CosmeticHooks.Contains(hook))
            {
                continue;
            }

            // 能覆盖但有已知差异的：写明差异，仍不算阻塞。
            if (HooksWithKnownDifference.TryGetValue(hook, out (string Ours, string Difference) known))
            {
                hooks.Add($"{hook}（≈我们的\"{known.Ours}\"，但{known.Difference}）");
                continue;
            }

            // "反应任意牌"这个语义现在有对应时机了（"打出一张牌时"，见
            // HooksOurCardPlayedTriggerCovers）；仍记进报告供人工核对，但不算阻塞。
            if (HooksOurCardPlayedTriggerCovers.Contains(hook))
            {
                hooks.Add(hook + "（≈我们的\"打出一张牌时\"）");
                continue;
            }

            // 这几个钩子两种用法都有：只对自己生效（＝我们的一条同名效果），
            // 或对任意牌生效（＝我们表达不了）。用有没有"自身判断"来区分。
            if (SelfOnlyHooks.Contains(hook) && !IsSelfFiltered(source, hook))
            {
                hooks.Add(hook + "（反应任意牌，不只是自己）");
                continue;
            }

            hooks.Add(hook);
        }

        // ---- 数值来源 ----
        //
        // 只在卡**真的用了计算型数值**时才报：否则 `.Block` 这类属性访问会被误当成
        // "数值来源"（防御牌也读 Block，但它的格挡是固定值，不是算出来的）。
        // 判据是有 WithMultiplier（原版所有计算型都靠它给出单位数）。
        if (Regex.IsMatch(source, @"\.WithMultiplier\("))
        {
            hooks.Add("Calculated");

            foreach ((string pattern, string source1) in ValueSourceRules)
            {
                if (Regex.IsMatch(source, pattern))
                {
                    sources.Add(source1);
                }
            }

            // 我们**做不到**的数值来源要单独报出来，否则会被误判成"能实现"。
            //
            // 两类：
            //   过滤计数 —— 数"手牌里攻击牌有几张""带打击标签的有几张"（原版"完美打击"）
            //   战斗历史 —— 数"本回合打了几张技能牌""抽了几张牌""受到过几次伤害"等；
            //               这需要读 CombatManager.Instance.History，我们没有对应来源。
            //
            // 我们支持的是**整堆计数**（手牌数、抽牌堆张数…）与状态层数、生物属性、玩家资源。
            foreach (string missing in UnsupportedValueSources(source))
            {
                unsupported.Add(missing);
            }
        }

        // ---- 纯视觉卡：没有任何机制命令 ----
        bool cosmeticOnly = commands.Count == 0 && effects.Count == 0;

        return new ScanResult(
            commands,
            effects.ToList(),
            unsupported.ToList(),
            helpers.ToList(),
            unknown.ToList(),
            hooks.ToList(),
            sources.ToList(),
            cosmeticOnly,
            AppliedPowerTypes(source));
    }
}
