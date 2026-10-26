using ModForge.Core.Model;

namespace ModForge.Core.Modules.Descriptions;

/// <summary>
/// 共用词：一句句子里反复要用的那几段——谁（who*）、牌堆（pile* / where*）、
/// 持续范围（duration*）、筛选后缀（filter* / playedCard*）、以及句首的触发时机。
///
/// 放在一处是因为它们被引得很密（README 里记着实测的引用次数：<c>PileSuffix</c> 9 处），
/// 各效果文件只写自己那句模板、不重复这些词；名字也只有一处，不会出现
/// "这里写 whereDrawPile、那里写 whereDraw"那种某一处漏了翻译的静默偏差。
/// </summary>
internal static class DescriptionWords
{
    /// <summary>
    /// 效果作用于谁。
    ///
    /// 要**按轴**取词：同一档在两轴上的读法不同——"所有友方"在玩家轴上是"所有玩家"
    /// （那边落到的是 <c>CombatState.Players</c>），"目标"在玩家轴上是"任意一名友方"
    /// （指的是选中的那名玩家）。所以这里收 <paramref name="scope"/>，与界面上的标签
    /// （<c>TargetCatalog.LabelOf</c>）说的是同一件事。
    /// </summary>
    public static string Who(TargetScope scope, EffectTarget target, SentenceSet sentences) =>
        sentences.Word(WhoKey(scope, target)) ?? target.ToString();

    /// <summary>这一档在这个作用域里对应的用词键（句子模板与界面标签共用同一套分轴规则）。</summary>
    public static string WhoKey(TargetScope scope, EffectTarget target) => (scope, target) switch
    {
        // 玩家宠物那一组：名字自己就说明了它落在宠物上，所以不分轴。
        (_, EffectTarget.SelfPet) => "whoSelfPet",
        (_, EffectTarget.TargetPet) => "whoTargetPet",
        (_, EffectTarget.AllAlliesPet) => "whoAllAlliesPet",
        (_, EffectTarget.RandomAllyPet) => "whoRandomAllyPet",
        (_, EffectTarget.AllOtherAlliesPet) => "whoAllOtherAlliesPet",
        (_, EffectTarget.RandomOtherAllyPet) => "whoRandomOtherAllyPet",

        // 玩家轴一律用"玩家"的说法（与界面标签同一套，见 TargetCatalog.LabelOf）：
        // 那一轴上"友方"读起来像"某只友方生物"，而它落到的是人。
        //
        // "其他友方"那一族：造物轴读"其他友方"、玩家轴读"其他玩家"（那一轴落到的是玩家）。
        (TargetScope.Player, EffectTarget.AllOtherAllies) => "whoAllOtherPlayers",
        (TargetScope.Player, EffectTarget.RandomOtherAlly) => "whoRandomOtherPlayer",
        (_, EffectTarget.AllOtherAllies) => "whoAllOtherAllies",
        (_, EffectTarget.RandomOtherAlly) => "whoRandomOtherAlly",

        (TargetScope.Player, EffectTarget.Target) => "whoAnyAlly",
        (TargetScope.Player, EffectTarget.AllAllies) => "whoAllPlayers",
        (TargetScope.Player, EffectTarget.RandomPlayer) => "whoRandomPlayer",

        // (Player, RandomAlly)：这一档**已经不在玩家轴的候选里**了（含自己那档是 RandomPlayer、
        // 不含自己那档是 RandomOtherAlly），但旧数据可能还留着它。出码处按旧口径（不含自己）
        // 照旧落地，所以这里也照旧给出"其他玩家"的说法——描述与生成的代码保持一致。
        (TargetScope.Player, EffectTarget.RandomAlly) => "whoRandomOtherPlayer",

        (_, EffectTarget.Target) => "whoTarget",
        (_, EffectTarget.AllEnemies) => "whoAllEnemies",
        (_, EffectTarget.RandomEnemy) => "whoRandomEnemy",
        (_, EffectTarget.AllAllies) => "whoAllAllies",
        (_, EffectTarget.RandomAlly) => "whoRandomAlly",
        (_, EffectTarget.RandomPlayer) => "whoRandomPlayer",
        (_, EffectTarget.All) => "whoAll",
        (_, _) => "whoSelf",
    };

    /// <summary>牌堆在句式表里的键后缀。</summary>
    public static string PileSuffix(PileKind pile) => pile switch
    {
        PileKind.DrawPile => "Draw",
        PileKind.DiscardPile => "Discard",
        PileKind.ExhaustPile => "Exhaust",
        _ => "Hand",
    };

    /// <summary>持续范围的说法。挂在这个效果句子的末尾。</summary>
    public static string CostDurationLabel(CostDuration duration, SentenceSet sentences) => duration switch
    {
        CostDuration.ThisCombat => sentences.Word("durationThisCombat") ?? " this combat",
        CostDuration.UntilPlayed => sentences.Word("durationUntilPlayed") ?? " until played",
        _ => sentences.Word("durationThisTurn") ?? " this turn",
    };

    /// <summary>
    /// 筛选条件在句式表里的键后缀（<c>filter&lt;后缀&gt;</c>）。
    ///
    /// 必须与 <see cref="CardFilterKind"/> 逐项对齐：早先这里只列了四种、其余落到
    /// <c>_ =&gt; "Any"</c>，于是新增的几种条件全都去找 <c>filterAny</c> 这个不存在的键，
    /// 查不到就退回后缀本身当说法——报出来的是"TypeAttack"这种程序员用语（靠测试发现）。
    /// </summary>
    public static string FilterSuffix(CardFilterKind filter) => filter switch
    {
        CardFilterKind.TagStrike => "Strike",
        CardFilterKind.TagDefend => "Defend",
        CardFilterKind.TagShiv => "Shiv",
        CardFilterKind.TagOstyAttack => "OstyAttack",
        CardFilterKind.TypeAttack => "TypeAttack",
        CardFilterKind.TypeSkill => "TypeSkill",
        CardFilterKind.TypePower => "TypePower",
        CardFilterKind.HasStarCost => "HasStarCost",
        CardFilterKind.KeywordExhaust => "KeywordExhaust",
        CardFilterKind.KeywordEthereal => "KeywordEthereal",
        CardFilterKind.KeywordInnate => "KeywordInnate",
        CardFilterKind.KeywordRetain => "KeywordRetain",
        CardFilterKind.KeywordSly => "KeywordSly",
        CardFilterKind.KeywordUnplayable => "KeywordUnplayable",
        CardFilterKind.KeywordEternal => "KeywordEternal",
        CardFilterKind.SpecificCard => "Specific",

        // Any 不是"某种条件"，它由 anyCard 那条词负责（见 PickerWording.DimensionPhrase）。
        // 真走到这里说明调用方忘了先排除 Any，返回 "Any" 让问题显形。
        _ => "Any",
    };

    /// <summary>
    /// <c>playedCard*</c> 那组词的键后缀。
    ///
    /// 与 <see cref="FilterSuffix"/> 分开：那个给的是筛选条件的说法（名词短语），
    /// 这个给的是"被打出的一张X"里的名词。两者对同一个筛选种类恰好常常同字，
    /// 但用途不同（一个进"所有X"，一个进"一张X"），所以各自一份、各自校验。
    /// </summary>
    public static string PlayedCardSuffix(CardFilterKind filter) => filter.ToString();

    /// <summary>
    /// 牌堆范围的可读说法。
    ///
    /// 复用已在句式表里的 <c>whereHand</c>/<c>whereDraw</c>/… —— 牌堆搬运的句式用的是同一组词，
    /// 共用一个名字就不会出现"这里写 whereDrawPile、那里写 whereDraw"，某一处漏了翻译也只会
    /// 静默退回英文（这个坑踩过一次）。
    /// </summary>
    public static string PileScopeLabel(PileScope scope, SentenceSet sentences) => scope switch
    {
        PileScope.Hand => sentences.Word("whereHand") ?? "hand",
        PileScope.DrawPile => sentences.Word("whereDraw") ?? "draw pile",
        PileScope.DiscardPile => sentences.Word("whereDiscard") ?? "discard pile",
        PileScope.ExhaustPile => sentences.Word("whereExhaust") ?? "exhaust pile",
        _ => sentences.Word("whereAny") ?? "play",
    };

    /// <summary>
    /// 触发时机的说法。默认的"打出时"不写——那是绝大多数卡的情况，
    /// 每条都写反而让描述变啰嗦。
    ///
    /// 回合开始/结束时还要带上"在哪个牌堆里"：同一时机的不同牌堆是两回事
    /// （"回合结束时若在手牌中"和"回合结束时若在消耗堆中"完全不同），
    /// 所以合进同一句里说出来，而不是分成两句。
    /// </summary>
    public static string TimingPhrase(ModProject? project, EffectTrigger trigger, EffectDef effect, SentenceSet sentences)
    {
        if (trigger == EffectTrigger.OnPlay)
        {
            return string.Empty;
        }

        // "打出一张牌时"的时机里含筛选条件与计数器，不是一句固定的话，单独拼。
        if (trigger == EffectTrigger.CardPlayed)
        {
            return OtherCardPlayedPhrase(project, effect, sentences);
        }

        string? key = trigger switch
        {
            EffectTrigger.AfterPlayed => "timingAfterPlayed",
            EffectTrigger.OnDraw => "timingOnDraw",
            EffectTrigger.OnDiscard => "timingOnDiscard",
            EffectTrigger.OnExhaust => "timingOnExhaust",
            EffectTrigger.EndOfTurn => "timingEndOfTurn",
            EffectTrigger.StartOfTurn => "timingStartOfTurn",
            _ => "timingAfterPlayed",
        };

        string timing = sentences.Word(key) ?? string.Empty;

        // 牌堆范围只对回合类时机有意义；Any 时不必写出来（那是默认语义）。
        if (trigger is EffectTrigger.StartOfTurn or EffectTrigger.EndOfTurn &&
            effect.PileScope != PileScope.Any)
        {
            return sentences.Render("timingInPile", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["timing"] = timing,
                ["pile"] = PileScopeLabel(effect.PileScope, sentences),
            });
        }

        return timing;
    }

    /// <summary>
    /// "打出一张牌时"的时机说法。
    ///
    /// 这一句里要装三件事，缺哪件都会读歪：
    ///
    ///   筛选  —— "打击牌" / "技能牌" / "任意牌"（不限时用"牌"这个名词，不是"所有牌"：
    ///            句子结构是"打出一张X"，直接填进名词位置）
    ///   计数  —— 启用计数器时写成"3 张技能牌"，不启用时是"一张技能牌"
    ///   归零  —— "本回合"的计数器要说明白，否则"每 3 张触发一次"会被理解成整场战斗累计
    ///
    /// 中文与英文的语序不同（中文"打出一张技能牌"，英文"play a Skill card"），
    /// 所以各写各的模板，不在这里拼语序。
    /// </summary>
    public static string OtherCardPlayedPhrase(ModProject? project, EffectDef effect, SentenceSet sentences)
    {
        // 指定了某一张牌时走另一组模板：卡名是"打击""爪击"这样的**名词**，不是"打击牌"
        // 那样的偏正短语，塞进"一张{cards}"会读成"一张打击"。所以那一组不带量词。
        if (effect.CardFilter == CardFilterKind.SpecificCard)
        {
            string named = DescriptionNames.CardNameForFilter(project, effect, sentences);

            var namedVars = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["count"] = Math.Max(1, effect.CounterTarget).ToString(),
                ["card"] = named,
            };

            if (!effect.UseCounter || effect.CounterTarget <= 1)
            {
                return sentences.Render("timingCardPlayedNamed", namedVars);
            }

            return effect.CounterReset == CounterReset.ThisCombat
                ? sentences.Render("timingCardPlayedCountCombatNamed", namedVars)
                : sentences.Render("timingCardPlayedCountTurnNamed", namedVars);
        }

        // 这里**不能**复用 filter* / anyCard 那组词：那组是名词短语（"所有牌""打击牌"），
        // 而这一句要的是量词短语里的名词（"一张**牌**""一张**技能牌**"）。
        // 中文量词与英文冠词都塞不进那组词里（"一张所有牌"读不通），所以另给一组
        // playedCard*（单数）/ playedCards*（复数），键名后缀与筛选种类一一对应
        // （由测试断言不缺项）。英文"每 2 张技能牌"必须是复数，所以两套都要。
        bool counted = effect.UseCounter && effect.CounterTarget > 1;
        string stem = counted ? "playedCards" : "playedCard";

        string cards = sentences.Word(stem + PlayedCardSuffix(effect.CardFilter))
            ?? sentences.Word(stem + "Any")
            ?? "card";

        var vars = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["count"] = Math.Max(1, effect.CounterTarget).ToString(),
            ["cards"] = cards,
        };

        // 阈值 1 等同于"每张都触发"——那就是不启用的语义，按不启用写。
        if (!counted)
        {
            return sentences.Render("timingCardPlayed", vars);
        }

        return effect.CounterReset == CounterReset.ThisCombat
            ? sentences.Render("timingCardPlayedCountCombat", vars)
            : sentences.Render("timingCardPlayedCountTurn", vars);
    }
}
