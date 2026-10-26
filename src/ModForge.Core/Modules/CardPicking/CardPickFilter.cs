using ModForge.Core.Model;

namespace ModForge.Core.Modules.CardPicking;

/// <summary>
/// 把"这条效果在筛哪些牌"翻成 C# 谓词体（<c>c =&gt; …</c> 里那一段）。
///
/// 这里是**唯一**的筛选判定处：选牌、以及由它派生的描述与校验都读它。各写一份的话，
/// 改了判定就会出现"编辑器说这是打击牌、生成的代码却按别的判"这种不一致，而且两边都不报错。
///
/// 形状照原版抄：
///   标签   <c>c.Tags.Contains(CardTag.X)</c>          （"完美打击"数打击牌）
///   关键词 <c>c.Keywords.Contains(CardKeyword.Y)</c>  （"女妖之嚎"按虚无计数）
///   类型   <c>c.Type == CardType.Z</c>                （"飞镖"数技能牌）
///   稀有度 <c>c.Rarity == CardRarity.W</c>
///   费用   <c>c.EnergyCost.Canonical &gt; 2</c>              （卡面费用，不是战斗中的实时费用）
///
/// **层次**（这是多维并存的关键）：
///   维度**内部**多个取值之间是**或**（<c>Rarity == Common || Rarity == Rare</c>，
///   原版 <c>CardFactory</c> 的 <c>c.Rarity != Basic &amp;&amp; c.Rarity != Ancient</c> 就是这种写法）；
///   维度**之间**是**与**（"稀有攻击牌"= <c>Rarity == Rare &amp;&amp; Type == Attack</c>）。
///
/// 早先这里把维度与"需要星辰"拼进**同一个 <c>||</c> 链**，于是"攻击牌 + 需要星辰"生成的
/// 是"任意攻击牌，或任意要花星辰的牌"——与界面上写的"与"相反，而且两边都不报错。
/// 现在每个维度各占一组（组内 <c>||</c>、组间 <c>&amp;&amp;</c>），那种偏差在结构上就写不出来了。
/// </summary>
public static class CardPickFilter
{
    /// <summary>
    /// 谓词体，变量名固定为 <c>c</c>（生成时塞进 <c>Where(c =&gt; …)</c> 或 <c>Count(c =&gt; …)</c>）。
    ///
    /// 返回 null 表示**不筛**（各个维度都空）。调用方不该把 null 当成"筛掉一切"。
    /// </summary>
    public static string? Predicate(EffectDef effect, ThresholdResolver? thresholds = null) =>
        PredicateOn("c", effect, thresholds);

    /// <summary>
    /// 同上，但把"被筛的那张牌"写成调用方给的表达式。
    ///
    /// "打出一张牌时"要筛的是**打出的那张牌**（<c>cardPlay.Card</c>），不是牌堆里遍历到的
    /// <c>c</c>；伤害增益要筛的是原版钩子传进来的 <c>cardSource</c>。三处判定的是同一件事，
    /// 共用一份映射才不会出现"这里按标签、那里按类型"这种静默不一致。
    /// </summary>
    /// <param name="skillPrefix">
    /// 牌 ID 那一维的类型判定要连着写（<c>c is StrikeIronclad || c is DefendIronclad</c>），
    /// 所以变量可能带 <c>!</c>（可空安全）之类的后缀——传进来的就是那个写法。
    /// </param>
    /// <param name="thresholds">
    /// 把费用阈值（三件套）解成 C# 表达式的回调。**选牌模块不认识"手牌数"这类来源**，
    /// 所以它由效果模块用取值器实现后传进来（见 <c>EffectEmitter</c>）；
    /// "按条件数牌"那侧传的是它自己的常数（见 <c>CounterEmitter</c>）。
    /// 费用维度没启用时可以传 null。
    /// </param>
    /// <param name="typeRef">
    /// 卡牌模型 id → C# 类型引用（<c>CARD.STRIKE_IRONCLAD</c> → <c>…Cards.StrikeIronclad</c>）。
    /// 只有"牌 ID"那一维要用（它按类型判 <c>c is X</c>），而 id 怎么解析成类型只有工程知道。
    /// 没用到那一维时可以传 null。
    /// </param>
    public static string? PredicateOn(
        string variable,
        EffectDef effect,
        ThresholdResolver? thresholds = null,
        Func<string, string>? typeRef = null) =>
        PredicateOn(variable, effect.Pick, thresholds, typeRef);

    /// <summary>
    /// 按一份**多维筛选**（<see cref="ICardFilter"/>）生成谓词。
    ///
    /// 收接口而不是收"选牌配置"：选牌（<c>CardPick</c>）与按条件数牌（<see cref="CardFilter"/>）
    /// 共用这一份判定，所以"稀有攻击牌"在"挑出来"与"数几张"两条路上判的是**同一件事**。
    /// </summary>
    public static string? PredicateOn(
        string variable,
        ICardFilter filter,
        ThresholdResolver? thresholds = null,
        Func<string, string>? typeRef = null)
    {
        var groups = new List<string>();

        // 顺序与**描述的定语链**一致（稀有度 → 类型 → 标签 → 关键词 → 牌 ID → 费用）：
        // 语义上与顺序无关，但生成的谓词按这个顺序读起来与卡面文字对得上。
        AddGroup(groups, filter.Rarities, r => $"{variable}.Rarity == CardRarity.{r}");
        AddGroup(groups, filter.Types, t => $"{variable}.Type == CardType.{t}");
        AddGroup(groups, filter.Tags, t => $"{variable}.Tags.Contains(CardTag.{t})");
        AddGroup(groups, filter.Keywords, k => $"{variable}.Keywords.Contains(CardKeyword.{k})");

        // 牌 ID 那一维按**卡牌类型**判（`c is X`），不是按 id 字符串比——
        // 与卡牌自身、生成代码用的是同一套类型引用。多个之间同样是"或"，所以要套括号。
        List<string> ids = filter.CardIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();

        if (ids.Count > 0)
        {
            if (typeRef is null)
            {
                throw new InvalidOperationException(
                    "这条路在按牌 ID 筛牌，但调用方没有把类型引用回调传进 CardPickFilter。");
            }

            string types = string.Join(" || ", ids.Select(id => $"{variable} is {typeRef(id)}"));
            groups.Add(ids.Count == 1 ? types : $"({types})");
        }

        AddCostGroup(groups, filter.EnergyComparison, CostKind.Energy, variable, thresholds);
        AddCostGroup(groups, filter.StarComparison, CostKind.Star, variable, thresholds);

        if (groups.Count == 0)
        {
            return null;
        }

        // 单条不套括号——"只有一维"（最常见）时生成结果与旧写法逐字节相同。
        return groups.Count == 1 ? groups[0] : string.Join(" && ", groups);
    }

    /// <summary>费用维度的比较对象与"是不是 X"的判据（游戏 API 见各自的注释）。</summary>
    private enum CostKind
    {
        /// <summary>能量费用：<c>EnergyCost.Canonical</c>（X 费卡的 Canonical 是 0）。</summary>
        Energy,

        /// <summary>星辰费用：<c>CanonicalStarCost</c>（-1 = 没有星辰费用）。</summary>
        Star,
    }

    private static void AddCostGroup(
        List<string> groups,
        CostComparison comparison,
        CostKind kind,
        string variable,
        ThresholdResolver? thresholds)
    {
        if (comparison == CostComparison.None)
        {
            return;
        }

        string value = kind == CostKind.Energy
            ? $"{variable}.EnergyCost.Canonical"
            : $"{variable}.CanonicalStarCost";

        if (comparison == CostComparison.IsX)
        {
            groups.Add(kind == CostKind.Energy
                ? $"{variable}.EnergyCost.CostsX"
                : $"{variable}.HasStarCostX");
            return;
        }

        // X 那一档之外都要比阈值。**必须有解析器**：阈值是效果自己的数值（可能是算出来的），
        // 只有效果模块知道怎么把三件套解成表达式；没传就是调用方漏了
        // （费用维度没启用时才允许不传，上面已经返回了）。
        if (thresholds is null)
        {
            throw new InvalidOperationException(
                "这条效果在按费用筛牌，但调用方没有把阈值的解析器传进 CardPickFilter。");
        }

        string threshold = thresholds(kind == CostKind.Energy ? ValueRole.Energy : ValueRole.Star);

        string op = comparison switch
        {
            CostComparison.AtLeast => ">=",
            CostComparison.AtMost => "<=",
            CostComparison.GreaterThan => ">",
            CostComparison.LessThan => "<",
            _ => "!=",
        };

        // 费用是 int、阈值可能是小数（"不小于 0.5 × 手牌数"这种写法有意义），
        // 比较时按 decimal 算——C# 会自动提升，不必写转型。
        groups.Add($"{value} {op} {threshold}");
    }

    /// <summary>维度内部多个取值是"或"，整组作为一个条件（与别的维度"与"）。</summary>
    private static void AddGroup<T>(
        List<string> groups,
        IReadOnlyList<T> values,
        Func<T, string> toPredicate)
    {
        if (values.Count == 0)
        {
            return;
        }

        string joined = string.Join(" || ", values.Select(toPredicate));

        // 单个取值不套括号（写出来更短），多个要套——它们还要和别的维度拼 &&。
        groups.Add(values.Count == 1 ? joined : $"({joined})");
    }

    /// <summary>
    /// 合法性约束翻成谓词（形如一整个 lambda）。这就是"效果自己声明的黑名单"落到代码上的样子，
    /// 于是"界面上能不能选它"与"结算时能不能动它"用的是同一个判据。
    ///
    /// 附魔那条必须**用游戏的 API**（<c>CanEnchant</c>），不能自己判"类型 + 可叠加"：
    /// 可附魔的规则挂在附魔类型自己身上，复刻一份的话，游戏新增附魔或改规则时我们会静默不跟。
    /// 条件里还要**或**上"已带同种附魔"——附魔有层数时同种附魔要能被再选中（"把层数加上去"），
    /// 而这时 <c>CanEnchant</c> 因为"已有附魔"是假的。
    /// </summary>
    public static string? ConstraintFilter(CardConstraintKind kind, string? enchantmentType) => kind switch
    {
        CardConstraintKind.Upgradable => "c => c.IsUpgradable",
        CardConstraintKind.Transformable => "c => c.IsTransformable",

        CardConstraintKind.Enchantable when !string.IsNullOrWhiteSpace(enchantmentType) =>
            $"c => ModelDb.Enchantment<{enchantmentType}>().CanEnchant(c)"
            + $" || (ModelDb.Enchantment<{enchantmentType}>().ShowAmount && c.Enchantment is {enchantmentType})",

        _ => null,
    };
}

/// <summary>
/// 把某个**费用阈值角色**解成 C# 表达式（可能是字面量，也可能是先算过一遍的局部变量）。
///
/// 由效果模块实现（用变量取值器），选牌模块只调用——于是"手牌数怎么数"这类知识仍然只属于
/// 变量模块，选牌模块不必认识它。
/// </summary>
public delegate string ThresholdResolver(ValueRole role);
