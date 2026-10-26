using ModForge.Core.Model;
using ModForge.Core.Modules.Effects;

namespace ModForge.Core.Modules.Descriptions;

/// <summary>
/// 描述模块的入口：把一条效果写成一句话。
///
/// 界面列表、卡牌描述的"按效果自动填充"、导出时的提示语都读它，所以三处说法一定一致。
///
/// **句式与用词不在这里硬编码**，而是按语言从 <c>language/sentences/&lt;语言&gt;.json</c> 读取
/// （见 <see cref="SentenceSet"/>）：加一种语言只要放一个文件，改措辞不必重新编译，
/// 各语言也能有自己的语序（中文把修饰语放名词前，英文放名词后）。
///
/// 模块的分工（一处的名字与"哪个文件管什么"见 README 的目录结构一节）：
///
///   <see cref="SentenceSet"/> / <see cref="SentenceLibrary"/> —— 句式表与加载
///   <see cref="DescriptionWords"/>   —— 共用词（谁 / 牌堆 / 持续范围 / 筛选后缀 / 时机）
///   <see cref="PickerWording"/>      —— 选牌器十步流程的文案侧
///   <see cref="ValueWording"/>       —— 数值三件套的文案侧
///   <see cref="DescriptionNames"/>   —— 名词（卡 / 状态 / 球 / 附魔），原版名走 <see cref="VanillaNames"/>
///   <see cref="DescriptionPlaceholders"/> —— 描述里能引用哪些占位符 + 建议描述
///   <see cref="PendingSentences"/>   —— 描述模块到效果目录的**入口**（效果自己的句子都在 Kinds/）
///
/// 一条效果自己的句子不在这里：它跟这条效果的出码写在同一个文件里
/// （<c>Modules/Effects/Kinds/</c>，见 <c>IEffectBehavior</c>）——**效果好念成什么，取决于
/// 它有哪些参数**，两件事分开写就一定会脱节。所以本模块负责"怎么说"（语言、用词、
/// 名词、占位符），效果文件负责"说哪句"。
///
/// 唯一一处反向依赖：本模块要问效果目录"这条效果由谁来说"
/// （<see cref="EffectCatalog"/>，见 <see cref="PendingSentences.Sentence"/>）。
/// 搬迁完成后未搬的那一坨会消失，但这一问会留着——它是两个模块之间唯一的一根线。
/// </summary>
public static class DescriptionModule
{
    /// <summary>原版名称查询器。宿主知道游戏目录时设置它，不设置也能正常工作。</summary>
    public static VanillaNames? Names { get; set; }

    /// <summary>
    /// 这段代码会去 <c>language/sentences/*.json</c> 里取的**用词**键。
    ///
    /// 列出来是为了能自动校验：取不到时这里大多有 <c>?? "fallback"</c> 兜底，于是漏了翻译
    /// 不会报错，只是中文卡面上突然冒出一句英文（"若这张牌在draw pile中"——踩过一次）。
    /// 有这份清单，测试就能断言每种语言都提供了它们。
    ///
    /// 带 <c>{n}</c> 的是按枚举拼出来的键（牌堆、筛选条件、时机、谁），要展开检查。
    /// </summary>
    public static IReadOnlyList<string> RequiredWordKeys()
    {
        var keys = new List<string>
        {
            "anyCard", "cardFallback", "enchantFallback", "sentenceEnd", "upgraded",
            "whereAny", "whereCardSet",
            "addFromPoolAnyWord",

            // 条件判断：前缀句一个、比较方式六个（比较方式是"词"，进 words 段；
            // 前缀句是模板，进 templates 段——两段都靠这份清单逐个盯）。
            "condPrefix",
            "condOp_AtLeast", "condOp_AtMost", "condOp_GreaterThan",
            "condOp_LessThan", "condOp_Equal", "condOp_NotEqual",
            "whoTarget", "whoAllEnemies", "whoRandomEnemy", "whoAllAllies", "whoRandomAlly",
            "whoAll", "whoSelf",

            // 按轴分出来的那几条 + 玩家宠物那一组。
            //   玩家轴上"目标"读作"任意一名友方"、"所有友方"读作"所有玩家"——
            //   因为那一轴落到的是 Player 集合，不是生物集合。
            //   宠物那一组自带"…的奥斯提"的说法（它不是"某一档 + 后缀"，而是独立的档）。
            "whoAnyAlly", "whoAllPlayers", "whoRandomPlayer",
            "whoAllOtherAllies", "whoRandomOtherAlly", "whoAllOtherPlayers", "whoRandomOtherPlayer", "whoRandomOtherPlayer",
            "whoSelfPet", "whoTargetPet", "whoAllAlliesPet", "whoRandomAllyPet",
            "whoAllOtherAlliesPet", "whoRandomOtherAllyPet",
            "timingAfterPlayed", "timingOnDraw", "timingOnDiscard", "timingOnExhaust",
            "timingEndOfTurn", "timingStartOfTurn",

            // 费用类：挑选方式的说法（单复数各一条）与三种持续范围。
            "pickAll", "pickRandom1", "pickRandomN", "pickChosen", "pickChosen1",
            "pickSetChosen", "pickSetChosen1",
            "pickThisCard", "pickNamedCard",
            "durationThisTurn", "durationThisCombat", "durationUntilPlayed",

            // 筛选短语（多维度）：定语链的模板、带筛选时的取法模板、两个费用维度的从句与零件。
            "pickPhrase", "pickPhraseWithCosts", "pickPhraseCostsOnly",
            "pickFilteredAll", "pickFilteredAtMost",
            "pickFilteredChosen", "pickFilteredChosen1", "pickFilteredRandomN", "pickFilteredRandom1",
            "costClause", "costClauseIsX", "costKindEnergy", "costKindStar",
            "costOpAtLeast", "costOpAtMost", "costOpGreaterThan", "costOpLessThan", "costOpNotEqual",
            "costJoin", "pickCardNoun", "modJoin",

            // 显式从句写法（同一维多值、或三个以上维度时用；判据见 PickerWording.UseShortForm）。
            // 缺了不报错，只会退回兜底的英文句形或让两部分粘连，所以逐个列出来靠测试盯。
            "clauseRarity", "clauseType", "clauseTag", "clauseKeyword", "clauseCardId",
            "clauseCost", "clauseCostIsX", "clauseJoin", "filterClausePhrase",
            "valueJoinParts", "valueJoinLast",

            // 关键词的裸词 → 紧凑写法那层包装（"带「消耗」的"）。
            "modKeywordCompact",

            // 球名串与"充能球/Orb"之间的连接（中文空、英文一个空格）：少了它英文会印成
            // "FrostOrb"，而这是**静默**的——句子看起来只是短了一点，所以列进清单靠测试盯。
            "orbJoinSuffix",

            // 激发"哪几个球"的说法。这一组原来不在清单里，而它是**静默**缺失的：
            // 取不到就只是少一段（"激发充能球 2 次"），看不出是漏了翻译。作用于自己与
            // 作用于别人各一套（英文是"your"与"their"的区别），所以共八条。
            "evokeTargetRightmost", "evokeTargetLeftmost", "evokeTargetAll", "evokeTargetRandom",
            "evokeTargetRightmostOther", "evokeTargetLeftmostOther",
            "evokeTargetAllOther", "evokeTargetRandomOther",
        };

        // 每个筛选维度各自的**修饰语**（定语链上的那几段）：值与枚举一一对应，
        // 少一个就会在卡面上印出枚举名（"Rare CardType.Attack"），所以逐个列出来校验。
        foreach (CardType type in Enum.GetValues<CardType>())
        {
            keys.Add("modType" + type);
        }

        foreach (CardRarity rarity in Enum.GetValues<CardRarity>())
        {
            keys.Add("modRarity" + rarity);
        }

        foreach (CardTag tag in Enum.GetValues<CardTag>())
        {
            keys.Add("modTag" + tag);
        }

        foreach (CardKeyword keyword in Enum.GetValues<CardKeyword>())
        {
            keys.Add("modKeyword" + keyword);
        }

        // 牌堆：pile*（名词本身）、where*（"在……中"里的那个词）。
        foreach (PileKind pile in Enum.GetValues<PileKind>())
        {
            keys.Add("pile" + DescriptionWords.PileSuffix(pile));
            keys.Add("where" + DescriptionWords.PileSuffix(pile));
        }

        // 筛选条件：Any 用 anyCard，其余各自一个。指定卡牌除外——那时直接写那张牌的名字
        // （见 DescriptionNames.CardNameForFilter），没有对应的通用词，硬要一个 filterSpecific 只会是死条目。
        //
        // playedCard* / playedCards* 是"打出一张X"与"每打出 N 张X"里的那个名词，
        // 与上面的名词短语是两套说法：filter* 进"所有X"，playedCard* 进"一张X"。
        foreach (CardFilterKind filter in Enum.GetValues<CardFilterKind>())
        {
            if (filter == CardFilterKind.SpecificCard)
            {
                // 这一种用卡名，没有通用词可说（见 DescriptionWords.OtherCardPlayedPhrase）。
                continue;
            }

            keys.Add("playedCard" + DescriptionWords.PlayedCardSuffix(filter));
            keys.Add("playedCards" + DescriptionWords.PlayedCardSuffix(filter));

            if (filter != CardFilterKind.Any)
            {
                keys.Add("filter" + DescriptionWords.FilterSuffix(filter));
            }
        }

        return keys;
    }

    public static string Describe(
        ModProject? project,
        EffectDef effect,
        string uiLanguage = "zh",
        EffectNames? names = null)
    {
        ArgumentNullException.ThrowIfNull(effect);

        SentenceSet sentences = SentenceLibrary.For(uiLanguage);
        string sentence = PendingSentences.Sentence(project, effect, sentences, names);

        // 触发时机写在句首：卡面上的文字要能看出"这是回合结束时才发生的"，
        // 否则读起来会像是打出时就生效（默认时机不写，那是最常见的情况）。
        string timing = DescriptionWords.TimingPhrase(project, effect.Trigger, effect, sentences);

        // 启用条件接在时机之后、动作之前："回合开始时，若手牌数不小于 3，造成 6 点伤害。"
        // 条件可以挂在任何效果上，所以前缀在这里**中央**拼一次——
        // 30 种行为各写一遍必然漏掉一半（这正是"共用形状进共用层"的老规矩）。
        if (effect.HasCondition)
        {
            sentence = ValueWording.ConditionPrefix(project, effect.Condition!, sentences) + sentence;
        }

        if (timing.Length > 0)
        {
            sentence = timing + sentence;
        }

        return WithRepeat(project, sentence, effect, sentences, names);
    }

    /// <summary>
    /// 这一句里引用的变量名——给了就用占位符（<c>{Damage:diff()}</c>），不给就写字面数字。
    ///
    /// 两个用途各取所需：**写进卡牌描述的自动填充**要占位符（原版就是这么写的，
    /// 数值会跟着升级变化、也才有"升了多少"的绿字）；**编辑器里的效果行摘要**要字面数字
    /// （那是一句话的说明，写成 `{Damage:diff()}` 反而难读）。
    ///
    /// 名字由变量模块的规划一处给出（<c>VariableModule.Slots</c> / <c>RepeatSlots</c>），
    /// 所以描述里引用的变量一定在卡上真的声明了（否则游戏内格式化失败）。
    /// 只有伤害与格挡有这些变量：其它种类的计算型系数存在生成类的私有字段里，没有变量可引用。
    /// </summary>
    public sealed record EffectNames(
        string? Value = null,
        string? BaseValue = null,
        string? ExtraValue = null,
        string? Repeat = null,
        string? EnergyThreshold = null,
        string? StarThreshold = null)
    {
        /// <summary>数值占位符；没给名字时退回字面数字。</summary>
        public string Token(string? name, decimal fallback) =>
            string.IsNullOrEmpty(name) ? Trim(fallback) : $"{{{name}:diff()}}";

        /// <summary>某个费用阈值角色的变量名（描述里引用它时用）。</summary>
        public string? ThresholdOf(Model.ValueRole role) =>
            role == Model.ValueRole.Energy ? EnergyThreshold : StarThreshold;

        /// <summary>占位符是否可用（用于决定要不要写成 " + N" 这种形式）。</summary>
        public static string Trim(decimal value) =>
            value == Math.Floor(value)
                ? ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 重复次数作为后缀接在句尾，因为重复是整条效果循环执行若干次。
    ///
    /// 中文要接在逗号后面（"造成…的伤害，重复 3 次。"），所以先去掉原句的句号，句号由后缀
    /// 自带。句号与连接方式各语言不同，都写在句式表里（words.sentenceEnd / templates.repeatSuffix）。
    ///
    /// 次数是计算型时换成 <c>repeatCalcSuffix</c>（"重复 等于X 次"），与数值部分的计算型
    /// 措辞同一套读法。
    /// </summary>
    private static string WithRepeat(
        ModProject? project,
        string sentence,
        EffectDef effect,
        SentenceSet sentences,
        EffectNames? names = null)
    {
        int repeat = effect.Repeat;

        bool calc = effect.CalculatedRepeat;

        if (!calc && repeat <= 1)
        {
            return sentence;
        }

        string end = sentences.Word("sentenceEnd") ?? string.Empty;

        if (end.Length > 0 && sentence.EndsWith(end, StringComparison.Ordinal))
        {
            sentence = sentence[..^end.Length];
        }

        if (calc)
        {
            string source = ValueWording.SourceLabel(
                project,
                new EffectDef { Source = effect.RepeatSource, Amount = effect.RepeatBase },
                sentences);

            string plus = effect.RepeatBase == 0 ? string.Empty : $" + {effect.RepeatBase}";

            return sentence + sentences.Render("repeatCalcSuffix", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = source,
                ["plus"] = plus,
            });
        }

        // 能升级的重复次数各占一个 RepeatVar，所以描述里引用它——升级后卡面文字跟着变。
        // 不能升级的重复没有变量（生成代码在循环里直接写字面量），只能写数字。
        string num = names is null ? repeat.ToString() : names.Token(names.Repeat, repeat);

        return sentence + sentences.Render("repeatSuffix", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["num"] = num,
        });
    }
}

/// <summary>
/// 把一条效果写成一句话所需的上下文。**描述模块与效果模块之间的契约**：
/// 效果自己的句子（<c>Modules/Effects/Kinds/</c> 的 <c>Describe</c>）收到它，
/// 往里补自己那几个词再渲染。
///
/// <paramref name="Vars"/> 是句子模板的替换表，入口已经放好 <c>who / num / kind</c>。
/// </summary>
internal sealed record EffectDescribe(
    ModProject? Project,
    EffectDef Effect,
    SentenceSet Sentences,
    DescriptionModule.EffectNames? Names,
    Dictionary<string, string> Vars)
{
    /// <summary>补一个词。</summary>
    public EffectDescribe With(string key, string value)
    {
        Vars[key] = value;
        return this;
    }

    /// <summary>按模板名渲染这一句。</summary>
    public string Render(string key) => Sentences.Render(key, Vars);

    /// <summary>补上计算型的两个零件（来源、基础值）。固定型效果不必调。</summary>
    public void AddCalculationVars() =>
        ValueWording.AddCalculationVars(Project, Vars, Effect, Sentences, Names);

    /// <summary>
    /// 这条效果的句子该用"作用于自己"那一套模板吗？
    ///
    /// 用"自己"那一套的前提是**真的作用在自己身上**：选了"作用于其奥斯提"就不是了——
    /// 那时句子必须念出对象（"对自己的奥斯提施加 2 层印记"），而"自己"那几条模板是不念对象的
    /// （"获得 2 层印记"）。所以判据是这两条一起看，写成一处省得每个行为各判一遍。
    /// </summary>
    public bool SelfSubject => Effect.Target == EffectTarget.Self;

    /// <summary>
    /// 按"作用对象是不是自己"给句式名插一段：<c>energy</c> → <c>energyOther</c>、
    /// <c>energyCalc</c> → <c>energyOtherCalc</c>。
    ///
    /// 作用于自己时不念对象（"获得 1 点能量。"），作用于别人时要念出来
    /// （"使所有友方获得 1 点能量。"）——玩家轴的效果（能量 / 星辰 / 抽牌 / 充能球 / 铸造 /
    /// 召唤）与造物轴那一族（<c>damage</c> / <c>damageOther</c>）是同一个读法，
    /// 只是那几条把组合写成了显式的三元表达式。
    ///
    /// 规则落在这一处是为了"不漏掉一半"：句子有两半（自己 / 别人），而
    /// <c>Calc</c> 又各有一份，四处组合手写容易只补一处——卡面于是读起来像作用在自己身上。
    /// </summary>
    public string TargetedKey(string baseName, bool calculated) =>
        baseName + (SelfSubject ? string.Empty : "Other") + (calculated ? "Calc" : string.Empty);

    /// <summary>
    /// 补上牌堆操作类的词（<c>pile</c> / <c>where</c> / <c>num</c> / <c>kind</c>）。
    /// 消耗 / 丢弃 / 搬运 / 复制这类"动牌堆里的牌"的效果才要调。
    /// </summary>
    public void AddPileVars() =>
        PickerWording.PileVars(Project, Effect, Sentences, Names, Vars);

    /// <summary>这条效果引导的那种球的显示名（游戏本地化表里的叫法）。</summary>
    public string OrbName() => OrbNameFor(Effect.Orb);

    /// <summary>任意一种球的显示名（筛球种那一格要逐个取名字）。</summary>
    public string OrbNameFor(OrbKind kind) => DescriptionNames.OrbName(kind, Sentences);

    /// <summary>取句式表里的一个词（"最右侧"这类）。</summary>
    public string Word(string key) => Sentences.Word(key) ?? string.Empty;

    /// <summary>
    /// "作用于哪些牌"的短语（"手牌中所有牌""选择的 1 张牌""这张牌"…），
    /// 由作用对象与作用方式决定，与费用类效果读的是同一份说法（见
    /// <see cref="PickerWording.PickLabel"/>）。
    /// </summary>
    public string PickLabel() => PickerWording.PickLabel(Project, Effect, Sentences, Names);
}
