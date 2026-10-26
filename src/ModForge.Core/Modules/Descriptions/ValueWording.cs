using ModForge.Core.Model;
using ModForge.Core.Modules.ValueSources;

namespace ModForge.Core.Modules.Descriptions;

/// <summary>
/// 数值那三件套的**文案侧**：把"等于 手牌数 + 6"这半句说成人话。
///
/// 三个零件各有各的说法，语序由各语言的模板决定（中文"等于 X 的"放在名词前，
/// 英文"equal to X"放在名词后），所以这里只提供零件、不拼句子。
/// </summary>
internal static class ValueWording
{
    /// <summary>
    /// 计算型的两个零件（<c>source</c> / <c>plus</c>），交给模板决定怎么摆。
    ///
    /// 这里只提供零件、不拼句子：中文要"等于 手牌数 + 6 的"，英文要"equal to cards in hand + 6"，
    /// 语序相反，只有各写各的模板才对。
    /// </summary>
    public static void AddCalculationVars(
        ModProject? project,
        Dictionary<string, string> vars,
        EffectDef effect,
        SentenceSet sentences,
        DescriptionModule.EffectNames? names = null)
    {
        if (!effect.IsCalculated)
        {
            return;
        }

        vars["source"] = SourceLabel(project, effect, sentences, names);

        // 基础值为 0 就是"纯系数乘法"，句子里不该出现" + 0"。
        int baseValue = effect.EffectiveBaseValue;

        if (baseValue == 0)
        {
            vars["plus"] = string.Empty;
            return;
        }

        // 基础值自己占一个变量（CalculationBase / CalculationBase2…），描述引用它，
        // 升级基础值时卡面文字跟着涨。
        string shown = names is null
            ? baseValue.ToString()
            : names.Token(names.BaseValue, baseValue);

        vars["plus"] = $" + {shown}";
    }

    /// <summary>
    /// 计算型效果"每单位"那部分的说法。
    ///
    /// 有变量时写成占位符（伤害/格挡的系数存在 ExtraDamage / ExtraDamage2… 里），
    /// 这样升级系数时描述跟着变——原版"完美打击"的写法就是"伤害 +{ExtraDamage:diff()}"。
    /// 没有变量时（其它种类的计算型把系数放在生成类的私有字段里）只能写字面数字。
    ///
    /// 注意乘号两边：系数为 1 时不写乘号（"每有一张牌" 比 "1 × 每有一张牌" 读得顺），
    /// 这与原版的省略方式一致。
    /// </summary>
    public static string PerUnitLabel(EffectDef effect, SentenceSet sentences, DescriptionModule.EffectNames? names)
    {
        if (effect.Source.PerUnit == 1m)
        {
            return string.Empty;
        }

        string per = names is null
            ? DescriptionModule.EffectNames.Trim(effect.Source.PerUnit)
            : names.Token(names.ExtraValue, effect.Source.PerUnit);

        return $"{per} × ";
    }

    /// <summary>
    /// "按条件数牌的"说法：**范围 + 条件**（"手牌中的稀有攻击牌数"）。
    ///
    /// 条件那半句与选牌器的定语链共用同一个生成处（<see cref="PickerWording.FilterPhrase"/>），
    /// 所以"能量消耗大于 2 的稀有攻击牌"在"挑出来改造"与"数有几张"两条路上的措辞也一致。
    /// 阈值念的是筛选自己的常数（那个数就写在文件里，不是卡上的变量）。
    /// </summary>
    private static string CountLabel(ModProject? project, ValueSource source, SentenceSet sentences)
    {
        string where = ScopeWord(source.CountScope, sentences);

        string? picked = PickerWording.FilterPhrase(
            project,
            source.Filter,
            sentences,
            role => source.Filter.ThresholdOf(role).ToString(System.Globalization.CultureInfo.InvariantCulture));

        // 一个维度都没筛：只念范围（"手牌的牌数"）。这一档校验器会提示，
        // 因为那种写法多半是想直接用"手牌数"那个来源。
        if (picked is null)
        {
            return sentences.Render("countPhraseAll", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["where"] = where,
            });
        }

        return sentences.Render("countPhrase", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["where"] = where,
            ["pick"] = picked,
        });
    }

    /// <summary>"从哪个范围数"的说法（手牌 / 抽牌堆 / … / 所有牌堆）。</summary>
    private static string ScopeWord(CountScope scope, SentenceSet sentences)
    {
        string key = scope switch
        {
            CountScope.Hand => "whereHand",
            CountScope.DrawPile => "whereDraw",
            CountScope.DiscardPile => "whereDiscard",
            CountScope.ExhaustPile => "whereExhaust",
            _ => "whereAllPiles",
        };

        return sentences.Word(key) ?? scope.ToString();
    }

    /// <summary>数值来源的可读说法，用句式表里的词。</summary>
    public static string SourceLabel(
        ModProject? project,
        EffectDef effect,
        SentenceSet sentences,
        DescriptionModule.EffectNames? names = null) =>
        ComposeSource(project, effect.Source, sentences,
            names is null ? null : names.Token(names.ExtraValue, effect.Source.PerUnit));

    /// <summary>
    /// 一个**变量来源**的完整说法（"2 × 自身力量 层数"这个样子）：系数段 + 取自谁 + 来源名。
    ///
    /// 收来源而不是收效果：**条件判断的两侧**（<see cref="EffectCondition"/>）不是效果的
    /// 数值/次数角色，但念法必须与数值、次数完全一致——两处各写一遍的话，改了一处就会出现
    /// "这句说手牌数、那句说每张手牌"的静默不一致，所以只写这一份。
    ///
    /// <paramref name="perUnitToken"/> 是系数段的显示串（占位符或字面量），null 时写数字；
    /// 系数为 1 时整段不写（原版的省略方式："1 × 每有一张牌"读不顺）。
    /// </summary>
    public static string ComposeSource(
        ModProject? project,
        ValueSource source,
        SentenceSet sentences,
        string? perUnitToken = null)
    {
        // 句式表优先：它按语言精调过（"牌堆里的牌数"这类），是给整句用的说法。
        // 表里没有时才退回变量来源模块的名字——那是"这个来源叫什么"的**唯一**出处，
        // 与界面下拉读的是同一份，所以两处不会各叫各的。
        string kind = sentences.Source(source.Kind.ToString())
            ?? SourceCatalog.Label(source.Kind, sentences.Language);

        // "按条件数牌"要多念两句：从哪个范围数、数的是哪些牌。
        // 不然卡面上只会写"符合条件的牌数"——哪一堆、什么条件都没说，
        // 读的人与生成出来的代码对不上（这是这一轮补上的缺口）。
        if (source.Kind == ValueSourceKind.FilteredCardCount)
        {
            kind = CountLabel(project, source, sentences);
        }

        // "状态层数"是个泛指说法，句子里要换成**具体那个状态**的名字
        // （"等于 自身力量 层数"），否则读起来是"等于 自身状态层数 层数"。
        if (source.Kind == ValueSourceKind.PowerStacks)
        {
            string powerName = DescriptionNames.PowerName(project, source.PowerId, sentences.Language);

            kind = sentences.Render("sourcePowerStacks", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["power"] = powerName,
            });
        }

        string per = source.PerUnit == 1m
            ? string.Empty
            : $"{perUnitToken ?? DescriptionModule.EffectNames.Trim(source.PerUnit)} × ";

        if (SourceCatalog.IsPlayerScoped(source.Kind))
        {
            return per + kind;
        }

        string actor = sentences.Actor(source.Actor.ToString()) ?? string.Empty;

        if (actor.Length == 0)
        {
            return per + kind;
        }

        // 中文里"自身"与"力量"直接相连；英文的 "your" 后面需要空格，句式表里已带尾空格。
        return $"{per}{actor}{kind}";
    }

    /// <summary>
    /// 启用条件的**前缀句**："若 2 × 手牌数 不小于 3，"。
    ///
    /// 由描述的中央拼装处（<c>DescriptionModule.Describe</c>）接在时机短语之后、
    /// 效果句子之前——条件可以挂在任何效果上，让 30 种行为各写一遍必然漏掉一半。
    /// 两侧的说法走 <see cref="ComposeSource"/>，与数值/次数同一套读法。
    /// </summary>
    public static string ConditionPrefix(
        ModProject? project,
        EffectCondition condition,
        SentenceSet sentences)
    {
        return sentences.Render("condPrefix", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["left"] = ConditionSide(project, condition, isLeft: true, sentences),
            ["op"] = sentences.Word("condOp_" + condition.Comparison) ?? condition.Comparison.ToString(),
            ["right"] = ConditionSide(project, condition, isLeft: false, sentences),
        });
    }

    /// <summary>条件一侧的说法：固定值就是数字本身；选了来源走与数值同一套组合。</summary>
    private static string ConditionSide(
        ModProject? project,
        EffectCondition condition,
        bool isLeft,
        SentenceSet sentences)
    {
        Modules.Variables.NumericValue side = condition.SideOf(isLeft);

        if (!side.Variable.IsUsed)
        {
            return ((int)side.Base).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return ComposeSource(project, side.Variable, sentences);
    }
}
