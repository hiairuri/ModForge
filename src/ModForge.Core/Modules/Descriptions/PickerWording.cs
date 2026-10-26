using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;

namespace ModForge.Core.Modules.Descriptions;

/// <summary>
/// 选牌器那十步流程的**文案侧**：把"挑了哪些牌"说成人话。
///
/// 它读的是同一个 <see cref="CardPick"/> 模型（<c>Modules/CardPicking/</c>）。
///
/// **维度并存之后，短语是"定语链 + 中心词"**：五个取值维度各自贡献一个**修饰语**
/// （"稀有""攻击""打击""带「消耗」的"、卡名），费用维度贡献一个**从句**
/// （"能量消耗大于 2"）；中心词是"牌"。中文把费用从句放前面（"…的稀有攻击牌"），
/// 英文放后面（"Rare Attack cards with…"）——语序由各语言的模板决定，不在这里判语言。
///
/// 两条容易读歪的规则（都有测试钉着）：
///
///   **带筛选时不念牌堆**。筛出来的牌可能散在各个牌堆里，念成"手牌中所有牌"
///   就与卡的实际作用对不上（phrasetest 当年发现的那条）。代价是取法与张数必须在这里说出来。
///
///   **张数从哪儿来由效果自己的策略决定**（<c>CardPickPolicy.CountFromEffect</c>，与生成侧同一判据）。
///   消耗牌这类"数值即张数"的效果用效果自己的值（固定值时是 <c>{ExhaustCount:diff()}</c> 占位符）；
///   其余效果用选牌器自己的基础值（计算型的张数在卡面上没法逐值算出来）。
///
///   **英文的单复数看基础值**，计算型（用占位符）时按复数说不猜。
/// </summary>
internal static class PickerWording
{
    /// <summary>
    /// "挑了哪些牌"这个短语。**选牌器文案侧的唯一入口**（各效果文件都调它）。
    /// </summary>
    /// <param name="sentenceStart">
    /// 这个短语是否位于句子开头。英文的 "this card" 在句首要写成 "This card"
    /// （词表里给的是小写，因为同一个短语也用在句中——例如 "Enchant this card"）。
    /// </param>
    public static string PickLabel(
        ModProject? project,
        EffectDef effect,
        SentenceSet sentences,
        DescriptionModule.EffectNames? names = null,
        bool sentenceStart = false)
    {
        CardPick pick = effect.Pick;

        string label;

        // 此牌：牌堆与筛选都无关。
        if (pick.Source == PickSource.ThisCard)
        {
            label = sentences.Word("pickThisCard") ?? "this card";
        }
        else
        {
            // 有筛选时用"定语链"（带筛选就不念牌堆，见类注释）；
            // 没筛选时才是"某个牌堆里的全部/若干张"。
            string? filter = FilterPhrase(project, effect, sentences, names);

            label = filter is null
                ? UnfilteredPhrase(effect, sentences, names)
                : CountedPhrase(pick, effect, filter, sentences, names);
        }

        if (!sentenceStart || sentences.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return label;
        }

        // 只把**首个字母**提上来："ALL cards…" 已经是全大写，不动它。
        return label.Length > 1 && char.IsLower(label[0])
            ? char.ToUpperInvariant(label[0]) + label[1..]
            : label;
    }

    /// <summary>
    /// 没有筛选时的短语：由"在哪儿找"（流程二/卡组）与"怎么取"（流程九）拼成
    /// （"手牌中所有牌""选择的 2 张牌"…）。
    /// </summary>
    private static string UnfilteredPhrase(
        EffectDef effect,
        SentenceSet sentences,
        DescriptionModule.EffectNames? names)
    {
        CardPick pick = effect.Pick;

        string where = pick.Source switch
        {
            PickSource.Deck => sentences.Word("whereDeck") ?? "deck",

            // 临时牌集合：说成"刚才那个集合里的牌"。不念集合 ID——句子是给人读卡面用的，
            // ID 是给机器对键用的（卡面上写"集合 A"读者也不知道 A 是哪几张）。
            PickSource.Set => sentences.Word("whereCardSet") ?? "that card set",

            _ => PileListLabel(pick, sentences),
        };

        // 流程九 = 全部：不需要张数。
        if (pick.Mode == PickMode.All)
        {
            return sentences.Word("pickAll") is { } all
                ? SentenceSet.Substitute(all, Vars(("where", where)))
                : $"all cards in {where}";
        }

        // 单数单独一条模板：英文的 "1 cards" 是错的（"a card"/"a random card"）。
        // 张数与单数取自**同一处**（见 CountInfo）——两处各判一次就会分叉，
        // "张数取效果数值"的效果会被按选牌器那一格判，于是句子印出"选择的 1 张"而不是实际张数。
        (string count, bool singular) = CountInfo(effect, names);

        string key = pick.Mode switch
        {
            PickMode.AtMost => "pickAtMost",

            // "选择"这一档的三条模板**不念来源**（"选择的 2 张牌"），而临时牌集合必须念出来
            // ——不念的话"选择的 2 张牌"是从哪儿选的完全看不出来。所以给它单独一对模板。
            PickMode.Choose when pick.Source == PickSource.Set =>
                singular ? "pickSetChosen1" : "pickSetChosen",
            PickMode.Choose => singular ? "pickChosen1" : "pickChosen",
            _ => singular ? "pickRandom1" : "pickRandomN",
        };

        return Template(sentences, key, pick.Mode switch
        {
            PickMode.AtMost => $"{count} or fewer cards from {where}",
            PickMode.Choose => $"{count} chosen cards",
            _ => $"{count} random cards in {where}",
        },
        ("where", where), ("count", count));
    }

    /// <summary>
    /// 有筛选时的短语：**定语链**（<paramref name="filter"/>）外面套上"怎么取、取几张"。
    ///
    /// 中文与英文的语序不同，所以各语言一套模板（见 <c>pickFiltered*</c>）；
    /// 这里只负责按流程九挑模板、把张数填进去。
    /// </summary>
    private static string CountedPhrase(
        CardPick pick,
        EffectDef effect,
        string filter,
        SentenceSet sentences,
        DescriptionModule.EffectNames? names)
    {
        // 张数与单数取自同一处（见 CountInfo）——"张数取效果数值"的效果必须按效果的值判，
        // 按选牌器那一格判会印出"选择的 1 张"（实际是 2 张）。
        (string count, bool singular) = CountInfo(effect, names);

        string key = pick.Mode switch
        {
            PickMode.All => "pickFilteredAll",
            PickMode.AtMost => "pickFilteredAtMost",
            PickMode.Random => singular ? "pickFilteredRandom1" : "pickFilteredRandomN",
            _ => singular ? "pickFilteredChosen1" : "pickFilteredChosen",
        };

        return Template(sentences, key, filter, ("pick", filter), ("count", count));
    }

    /// <summary>
    /// 这条效果在筛哪些牌（"定语链 + 中心词"）；**没有任何筛选时返回 null**。
    ///
    /// 只念条件、不念牌堆——筛出来的牌可能散在各个牌堆里（见类注释）。
    /// </summary>
    public static string? FilterPhrase(
        ModProject? project,
        EffectDef effect,
        SentenceSet sentences,
        DescriptionModule.EffectNames? names = null) =>
        FilterPhrase(project, effect.Pick, sentences,
            role => ThresholdText(project, effect, role, sentences, names));

    /// <summary>
    /// 一份多维筛选的定语链（"稀有攻击牌"、"能量消耗大于 2 的牌"）；**没有筛选时返回 null**。
    ///
    /// 收 <see cref="ICardFilter"/> 而不是收效果：选牌与"按条件数牌"用的是同一套条件，
    /// 所以念法也只能有一份——两处各写一遍迟早会出现"这边说稀有、那边说罕见"。
    /// 阈值的念法由 <paramref name="thresholdText"/> 给（选牌念效果的三件套，
    /// 计数念筛选自己的常数）。
    /// </summary>
    public static string? FilterPhrase(
        ModProject? project,
        ICardFilter filter,
        SentenceSet sentences,
        Func<ValueRole, string> thresholdText)
    {
        string phrase = UseShortForm(filter)
            ? ShortPhrase(project, filter, sentences, thresholdText)
            : ClausePhrase(project, filter, sentences, thresholdText);

        // 没有任何一句可说（各维都空，或者只写了几个空白牌名）时返回 null——
        // 调用方据此知道"这条没在筛"，不该把空串当成"筛掉一切"。
        return phrase.Length == 0 ? null : phrase;
    }

    /// <summary>
    /// 用**紧凑**写法（定语栈，"稀有攻击牌"）还是**显式从句**写法（"稀有度为稀有的且类型为攻击的"）。
    ///
    /// 判据：每个启用的**取值维度**恰好一个值、且这类维度不超过两个 → 紧凑。
    /// 取值维度 = 稀有度 / 类型 / 标签 / 关键词 / 牌 ID；**费用维度不计入**——它在紧凑写法里
    /// 本来就是那个"…的"前缀从句（"能量消耗大于 2 的稀有攻击牌"），读得通。
    ///
    /// 为什么要分两套：紧凑写法是**定语栈**，只在"每一段都是单个定语"时成立。同一维有多个
    /// 取值（"攻击、技能、能力"）或叠到三段以上时，定语栈就散了——关键词自带"的"，会叠成
    /// "带「保留」的带「消耗」的…"，而中文又没有词间空格，整串读不出层次。
    ///
    /// 这条判据有个好性质：**紧凑写法与旧输出逐字节相同**（每维只有一个值时，连接符都用不上），
    /// 所以切到显式的只有那些本来就念不通的句子。
    ///
    /// 判据是启发式，所以只写在这一处（读法与写法都从它出发），并由 `kindtest` 把边界钉死。
    /// </summary>
    private static bool UseShortForm(ICardFilter filter) =>
        ValueDimensionSizes(filter) is { Count: <= 2 } sizes && sizes.All(n => n == 1);

    /// <summary>各**取值维度**的取值个数（只统计启用的维；空白牌名不算数，与渲染一致）。</summary>
    private static List<int> ValueDimensionSizes(ICardFilter filter) => new[]
    {
        filter.Rarities.Count,
        filter.Types.Count,
        filter.Tags.Count,
        filter.Keywords.Count,
        filter.CardIds.Count(id => !string.IsNullOrWhiteSpace(id)),
    }.Where(n => n > 0).ToList();

    /// <summary>
    /// 紧凑写法：定语栈 + 头名词（"能量消耗大于 2 的稀有攻击牌"）。
    ///
    /// 走这条路时每一维都只有一个值，所以维度之间与取值之间都不需要连接词——
    /// 输出与"分两套句式之前"逐字节相同。
    /// </summary>
    private static string ShortPhrase(
        ModProject? project,
        ICardFilter filter,
        SentenceSet sentences,
        Func<ValueRole, string> thresholdText)
    {
        // 修饰语按固定顺序串起来（稀有度 → 类型 → 标签 → 关键词 → 牌 ID）：
        // 中文里"稀有攻击牌"读得通、"攻击稀有牌"读不通，所以顺序不能随意。
        // 段与段之间用 modJoin 连（中文空串、英文一个空格）——英文少了它就是 "RareAttack"。
        string join = sentences.Word("modJoin") ?? " ";

        string mods = string.Join(join, new[]
        {
            Modifier(filter.Rarities, r => sentences.Word("modRarity" + r), sentences),
            Modifier(filter.Types, t => sentences.Word("modType" + t), sentences),
            Modifier(filter.Tags, t => sentences.Word("modTag" + t), sentences),
            Modifier(filter.Keywords, k => KeywordCompact(sentences, k), sentences),
            Modifier(filter.CardIds, id => DescriptionNames.CardName(project, id, sentences.Language), sentences),
        }.Where(part => part.Length > 0));

        string costs = CostClauses(project, filter, sentences, thresholdText);

        if (mods.Length == 0 && costs.Length == 0)
        {
            return string.Empty;
        }

        // 中心词、语序、以及"只有费用没有修饰语"那种情况由模板定：
        // 中文"{costs}的{mods}牌"、英文"{mods} card(s) with {costs}"；
        // **只有费用**时不能再套那条模板（英文会多一个空格、中文会多一个"的"的位置）。
        string head = sentences.Word("pickCardNoun") ?? "cards";

        (string key, string fallback) = (mods.Length, costs.Length) switch
        {
            (0, _) => ("pickPhraseCostsOnly", $"{costs} {head}"),
            (_, 0) => ("pickPhrase", $"{mods} {head}"),
            _ => ("pickPhraseWithCosts", $"{mods} {head} with {costs}"),
        };

        return Template(sentences, key, fallback,
            ("mods", mods), ("costs", costs), ("head", head));
    }

    /// <summary>
    /// 显式从句写法：每个维度一个带标签词的从句，从句之间「且」、同一维的多个取值「、」。
    ///
    /// 中文的"的"落在从句尾（"类型为攻击、技能、能力的**且**…的牌"），英文是介词短语跟在
    /// 名词后面（"cards with type …, that have …"），所以语序由各语言的模板定，这里只管拼。
    ///
    /// 阈值的念法由 <paramref name="thresholdText"/> 给，与紧凑写法同一处（见
    /// <see cref="CostClauses"/>）——所以两套句式的差别只有语序与标签词，数字怎么念不会分叉。
    /// </summary>
    private static string ClausePhrase(
        ModProject? project,
        ICardFilter filter,
        SentenceSet sentences,
        Func<ValueRole, string> thresholdText)
    {
        var clauses = new List<string>();

        // 顺序与谓词生成处一致（稀有度 → 类型 → 标签 → 关键词 → 牌 ID → 费用）。
        // 两个费用维度也从"前缀"挪到从句序列的末尾。
        AddValueClause(clauses, sentences, "clauseRarity", filter.Rarities,
            r => sentences.Word("modRarity" + r));
        AddValueClause(clauses, sentences, "clauseType", filter.Types,
            t => sentences.Word("modType" + t));
        AddValueClause(clauses, sentences, "clauseTag", filter.Tags,
            t => sentences.Word("modTag" + t));
        AddValueClause(clauses, sentences, "clauseKeyword", filter.Keywords,
            k => sentences.Word("modKeyword" + k));
        AddValueClause(clauses, sentences, "clauseCardId",
            filter.CardIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
            id => DescriptionNames.CardName(project, id, sentences.Language));

        AddCostClause(clauses, project, filter, ValueRole.Energy, sentences, thresholdText,
            templateKey: "clauseCost", isXKey: "clauseCostIsX");
        AddCostClause(clauses, project, filter, ValueRole.Star, sentences, thresholdText,
            templateKey: "clauseCost", isXKey: "clauseCostIsX");

        if (clauses.Count == 0)
        {
            return string.Empty;
        }

        return Template(sentences, "filterClausePhrase", "{clauses} {head}",
            ("clauses", string.Join(sentences.Word("clauseJoin") ?? " and ", clauses)),
            ("head", sentences.Word("pickCardNoun") ?? "cards"));
    }

    /// <summary>
    /// 一个取值维度的从句（"类型为攻击、技能的"）；这一维没启用、或值都读不出词时什么都不加。
    /// </summary>
    private static void AddValueClause<T>(
        List<string> clauses,
        SentenceSet sentences,
        string templateKey,
        IReadOnlyList<T> values,
        Func<T, string?> word)
    {
        var words = new List<string>();

        foreach (T value in values)
        {
            if (word(value) is { Length: > 0 } text)
            {
                words.Add(text);
            }
        }

        if (words.Count == 0)
        {
            return;
        }

        clauses.Add(Template(sentences, templateKey, "{values}",
            ("values", ValuesText(sentences, words))));
    }

    /// <summary>
    /// 一个维度里的多个取值串成一段话（"攻击、技能" / "Attack, Skill or Power"）。
    ///
    /// 同一维的多个取值是**或**的关系，所以用**取值连接词**：中文「、」，英文把最后两个用
    /// " or " 连、其余用逗号（"Attack, Skill or Power"）。
    ///
    /// 早先这里错用了**维度之间**的连接符（中文是空串），于是三个类型粘成一段
    /// "攻击技能能力"、两张牌名粘成"打击防御"——而中文没有词间空格，读的人根本分不开。
    /// </summary>
    private static string ValuesText(SentenceSet sentences, IReadOnlyList<string> words)
    {
        if (words.Count == 0)
        {
            return string.Empty;
        }

        if (words.Count == 1)
        {
            return words[0];
        }

        string parts = sentences.Word("valueJoinParts") ?? ", ";
        string last = sentences.Word("valueJoinLast") ?? " or ";

        return string.Join(parts, words.Take(words.Count - 1)) + last + words[^1];
    }

    /// <summary>
    /// 一个维度的**修饰语**（紧凑写法用）。
    ///
    /// 值缺失（没填）时按空串——与"校验器会提示这种中间状态、生成时按不筛处理"一致，
    /// 描述这边也不该凭空多出半个词。
    /// </summary>
    private static string Modifier<T>(IReadOnlyList<T> values, Func<T, string?> word, SentenceSet sentences)
    {
        var parts = new List<string>();

        foreach (T value in values)
        {
            if (word(value) is { Length: > 0 } text)
            {
                parts.Add(text);
            }
        }

        return ValuesText(sentences, parts);
    }

    /// <summary>
    /// 关键词在**紧凑**写法里的那一段（"带「消耗」的"）。
    ///
    /// 词表里存的是**裸词**（"消耗"）：显式从句用它拼"带有消耗、保留的"，紧凑写法则套上
    /// 这一层包装才读得通（"消耗牌"会被读成"会消耗的牌"，不是"带消耗关键词的牌"）。
    /// 一个值一份词，包装只有这一处——两套句式不会各有一份"消耗"的写法。
    /// </summary>
    private static string KeywordCompact(SentenceSet sentences, CardKeyword keyword)
    {
        if (sentences.Word("modKeyword" + keyword) is not { Length: > 0 } value)
        {
            return string.Empty;
        }

        string template = sentences.Word("modKeywordCompact") ?? "{value}";

        return template.Replace("{value}", value, StringComparison.Ordinal);
    }

    /// <summary>
    /// 两个费用维度各出一个从句（"能量消耗大于 2"），用连接词连起来；都没启用时返回空串。
    ///
    /// 阈值的读法与数值那边一致：固定值写它那个变量的占位符（升级后卡面跟着变），
    /// 计算型把来源念出来（"能量消耗大于 手牌数 + 1"）。阈值怎么念由
    /// <paramref name="thresholdText"/> 给——选牌那侧念效果的三件套，"按条件数牌"那侧念
    /// 筛选自己的常数，**从句的形状两处完全一样**（筛的是同一件事）。
    /// </summary>
    private static string CostClauses(
        ModProject? project,
        ICardFilter filter,
        SentenceSet sentences,
        Func<ValueRole, string> thresholdText)
    {
        var clauses = new List<string>();

        AddCostClause(clauses, project, filter, ValueRole.Energy, sentences, thresholdText);
        AddCostClause(clauses, project, filter, ValueRole.Star, sentences, thresholdText);

        if (clauses.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(sentences.Word("costJoin") ?? " and ", clauses);
    }

    private static string CostClauses(
        ModProject? project,
        EffectDef effect,
        SentenceSet sentences,
        DescriptionModule.EffectNames? names) =>
        CostClauses(project, effect.Pick, sentences,
            role => ThresholdText(project, effect, role, sentences, names));

    /// <summary>
    /// 选牌那侧的阈值怎么念（"一个数"或"一段算式"）。
    ///
    /// 与数值那边的规矩一致（变量模块给名字，见 VarSlot）：
    ///
    ///   固定值 —— 那个数住在卡上的变量里（EnergyAmount / StarAmount），所以写占位符
    ///     （"{EnergyAmount:diff()}"），升级后卡面跟着变；
    ///   计算型 —— 算式是"基础值 + 来源 × 系数"：来源念出来（与"等于 手牌数 + 1"
    ///     同一句式），基础值那一项同样占变量、同样写占位符。系数在算式里是字面量，
    ///     所以只能写数字。
    /// </summary>
    private static string ThresholdText(
        ModProject? project,
        EffectDef effect,
        ValueRole role,
        SentenceSet sentences,
        DescriptionModule.EffectNames? names)
    {
        Modules.Variables.NumericValue value = effect.NumericOf(role);

        if (!value.IsCalculated)
        {
            return names is null
                ? DescriptionModule.EffectNames.Trim(value.Base)
                : names.Token(names.ThresholdOf(role), value.Base);
        }

        string source = ValueWording.SourceLabel(
            project,
            new EffectDef { Source = effect.SourceOf(role), Amount = (int)value.Base },
            sentences);

        // 基础值为 0 就是"纯系数乘法"，句子里不该出现" + 0"。
        string plus = names is null
            ? DescriptionModule.EffectNames.Trim(value.Base)
            : names.Token(names.ThresholdOf(role), value.Base);

        return value.Base == 0m ? source : $"{source} + {plus}";
    }

    /// <summary>
    /// 一个费用维度的从句（"能量消耗大于 2"）。
    ///
    /// 两套句式共用这一份逻辑（比什么数、怎么念阈值都一样），差别只在**模板**：
    /// 紧凑写法用 <c>costClause</c>（"能量消耗大于 2 "，末尾一个空格，好与后面的"的"分开），
    /// 显式写法用 <c>clauseCost</c>（"能量消耗大于 2 的"，自己收尾成一句从句）。
    /// </summary>
    private static void AddCostClause(
        List<string> clauses,
        ModProject? project,
        ICardFilter filter,
        ValueRole role,
        SentenceSet sentences,
        Func<ValueRole, string> thresholdText,
        string templateKey = "costClause",
        string isXKey = "costClauseIsX")
    {
        CostComparison comparison = filter.ComparisonOf(role);

        if (comparison == CostComparison.None)
        {
            return;
        }

        string kind = sentences.Word(role == ValueRole.Energy ? "costKindEnergy" : "costKindStar")
            ?? (role == ValueRole.Energy ? "energy cost" : "star cost");

        // X 单独一条模板：那种牌没有固定费用可比（阈值不参与）。
        if (comparison == CostComparison.IsX)
        {
            clauses.Add(Template(sentences, isXKey, $"X {kind}",
                ("kind", kind), ("amount", string.Empty)));
            return;
        }

        string op = sentences.Word(comparison switch
        {
            CostComparison.AtLeast => "costOpAtLeast",
            CostComparison.AtMost => "costOpAtMost",
            CostComparison.LessThan => "costOpLessThan",
            CostComparison.NotEqual => "costOpNotEqual",
            _ => "costOpGreaterThan",
        }) ?? string.Empty;

        string amount = thresholdText(role);

        clauses.Add(Template(sentences, templateKey, $"{kind} {op} {amount}",
            ("kind", kind), ("op", op), ("amount", amount)));
    }

    /// <summary>
    /// 几个牌堆的说法（流程二）。空 = 全部战斗牌堆；多个用连接词连起来
    /// （"手牌或弃牌堆"）——只说第一个会让卡面与卡的实际作用对不上。
    /// </summary>
    private static string PileListLabel(CardPick pick, SentenceSet sentences)
    {
        if (pick.Piles.Count == 0)
        {
            return sentences.Word("whereAllPiles") ?? "all piles";
        }

        var names = new List<string>();

        foreach (PileKind pile in pick.Piles)
        {
            names.Add(
                sentences.Word("where" + DescriptionWords.PileSuffix(pile))
                ?? sentences.Word("pile" + DescriptionWords.PileSuffix(pile))
                ?? pile.ToString());
        }

        return string.Join(sentences.Word("orJoin") ?? " or ", names);
    }

    /// <summary>
    /// 挑牌那个**名词短语**（"牌" / "稀有攻击牌" / "能量消耗大于 2 的牌"…）。
    ///
    /// 给"张数是算出来的"那些句子起头用：那种句子写不出一个确定的数字，
    /// 得由名词起头（"消耗 等于 当前星辰 的**牌**"）。
    /// 有筛选时它就是那条定语链——不然句子会把"挑哪些牌"整个漏掉。
    /// </summary>
    public static string PickNoun(
        ModProject? project,
        EffectDef effect,
        SentenceSet sentences) =>
        FilterPhrase(project, effect, sentences)
        ?? sentences.Word("pickCardNoun")
        ?? "cards";

    /// <summary>
    /// 目标牌堆的说法（搬运、复制都用它）。抽牌堆与弃牌堆要把顶/底带出来
    /// （"抽牌堆顶"），手牌与消耗堆没有玩家可见的位置之分，只给牌堆名。
    /// </summary>
    public static string TargetPileLabel(EffectDef effect, SentenceSet sentences)
    {
        string pile = sentences.Word("pile" + DescriptionWords.PileSuffix(effect.TargetPile))
            ?? sentences.Word("where" + DescriptionWords.PileSuffix(effect.TargetPile))
            ?? effect.TargetPile.ToString();

        if (effect.TargetPile is not (PileKind.DrawPile or PileKind.DiscardPile))
        {
            return pile;
        }

        return SentenceSet.Substitute(
            sentences.Word(effect.Placement switch
            {
                PilePlacement.Top => "intoPileTop",
                PilePlacement.Bottom => "intoPileBottom",
                _ => "intoPileRandom",
            }) ?? "{where}",
            Vars(("where", pile)));
    }

    /// <summary>
    /// 牌堆操作类句式的几个词（<c>pile</c> / <c>where</c> / <c>num</c> / <c>kind</c>），
    /// 外加计算型的来源与基础值。
    ///
    /// 抽出来是因为"动牌堆里的牌"这一族（消耗 / 丢弃 / 搬运 / 复制 / 自动打出…）**共用**这套
    /// 前缀，而它们分在各效果文件里——前缀留在这里，各文件只写自己那一句模板。
    /// </summary>
    public static void PileVars(
        ModProject? project,
        EffectDef effect,
        SentenceSet sentences,
        DescriptionModule.EffectNames? names,
        Dictionary<string, string> vars)
    {
        // 来源牌堆的说法（流程二，可多选）。只有"从哪个牌堆取"这类模板用它；
        // 目的地那套词由调用方按 TargetPile 覆盖（见 TargetPileLabel）。
        vars["pile"] = PileListLabel(effect.Pick, sentences);
        vars["where"] = PileListLabel(effect.Pick, sentences);

        // 牌堆操作的"张数"通常是"效果量"，所以引用效果自己那个变量。
        // 计算型走 source/plus 那套。
        vars["num"] = effect.IsCalculated || names is null
            ? effect.Amount.ToString()
            : names.Token(names.Value, effect.Amount);

        vars["kind"] = effect.Kind.ToString();

        if (effect.IsCalculated)
        {
            ValueWording.AddCalculationVars(project, vars, effect, sentences, names);
        }
    }

    /// <summary>
    /// 张数的说法（流程十）与"是不是单数"。
    ///
    /// 两条来源，与生成侧同一判据（<see cref="CardPickPolicy.CountFromEffect"/>）：
    /// **张数就是效果数值**的效果（消耗牌）用效果自己的值，其余效果用选牌器自己那一格。
    /// 两者都可能"算出来"，有变量时写占位符。
    ///
    /// 单数只在**固定值且等于 1** 时成立：计算型的张数在卡面上不确定，按复数说
    /// （"选择的 {ExhaustCount:diff()} 张牌"，不猜单复数）。
    /// </summary>
    private static (string Text, bool Singular) CountInfo(
        EffectDef effect,
        DescriptionModule.EffectNames? names)
    {
        CardPick pick = effect.Pick;

        if (CardPickPolicies.For(effect.Kind).CountFromEffect)
        {
            int amount = Math.Max(1, effect.Amount);

            // 张数就是效果数值：**有变量名就写占位符**（那个变量在卡上真的声明了，
            // 升级改的就是它），没给名字（编辑器里的行摘要）才写字面数字。
            return (
                names is null ? DescriptionModule.EffectNames.Trim(amount) : names.Token(names.Value, amount),
                !effect.IsCalculated && amount == 1);
        }

        // 其余效果的张数住在选牌器那一格，它不是卡上的变量（没有变量名可引用），
        // 所以只能写字面数字；单数同样看"固定值且等于 1"。
        int count = Math.Max(1, (int)pick.Count.Base);

        return (count.ToString(), !pick.Count.IsCalculated && count == 1);
    }

    /// <summary>
    /// 按句式表里的模板渲染；**模板缺失时退回兜底句**（并把缺的键记进 MissingTemplateKeys，
    /// phrasetest 会断言没有缺键）。
    ///
    /// 两处都查：新的筛选短语住在 <c>templates</c> 段，而"全部/选择的 N 张牌"那几条历史键
    /// 住在 <c>words</c> 段（它们是名词短语，当年按词收的）——两处都认，免得为了放对段落
    /// 去动一圈调用。
    /// </summary>
    private static string Template(
        SentenceSet sentences,
        string key,
        string fallback,
        params (string Key, string Value)[] vars)
    {
        if (sentences.HasTemplate(key))
        {
            return sentences.Render(key, Vars(vars));
        }

        if (sentences.Word(key) is { } word)
        {
            return SentenceSet.Substitute(word, Vars(vars));
        }

        return fallback;
    }

    private static Dictionary<string, string> Vars(params (string Key, string Value)[] vars)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach ((string key, string value) in vars)
        {
            result[key] = value;
        }

        return result;
    }
}
