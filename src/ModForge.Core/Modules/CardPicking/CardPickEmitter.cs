using ModForge.Core.Model;
using ModForge.Core.Modules.ValueSources;
using ModForge.Core.Modules.Variables;

namespace ModForge.Core.Modules.CardPicking;

/// <summary>发一段选牌代码需要的外部表达式（随"卡牌 / 状态"两种上下文而不同）。</summary>
/// <param name="Player">玩家表达式（<c>Owner</c> / <c>Owner.Player!</c>）。</param>
/// <param name="ChoiceContext">玩家选择上下文表达式。</param>
/// <param name="PromptEntry">本牌在 cards 表里的条目名；为 null 时退回游戏自带的提示语。</param>
/// <param name="TypeRef">卡牌模型 id → C# 类型引用。</param>
/// <param name="IsCard">在卡牌里发还是状态里发（决定读"选牌数"时用哪套取值上下文）。</param>
/// <param name="PowerTypeFor">状态模型 id → C# 类型引用，读"状态层数"这类来源时要用。</param>
public sealed record CardPickEmitContext(
    string Player,
    string ChoiceContext,
    string? PromptEntry,
    Func<string, string> TypeRef,
    bool IsCard,
    Func<string, string>? PowerTypeFor = null,
    string? SetCards = null)
{
    /// <summary>
    /// 读"选牌数"那个变量来源时用的取值上下文——**取值上下文由变量来源模块给**
    /// （<c>Modules/ValueSources/SourceEmitter</c>），本模块只是把"是卡牌还是状态"
    /// 与"状态类型怎么解析"这两个已知的事实转交过去，不自己拼一份。
    ///
    /// <c>Player</c> 以本上下文的为准（不沿用 <c>ForCard()</c> 默认的 <c>this.Owner</c>）：
    /// 卡牌里两者等价，但模块不该假定自己一定生成在 CardModel 的实例方法里。
    /// </summary>
    internal SourceEmitter.Context SourceCtx
    {
        get
        {
            SourceEmitter.Context inner = IsCard
                ? SourceEmitter.Context.ForCard()
                : SourceEmitter.Context.ForPower();

            return inner with { Player = Player, PowerTypeFor = PowerTypeFor };
        }
    }
}

/// <summary>
/// 选牌模块的唯一出码处：把 <see cref="CardPick"/> 发成一段挑牌代码，结果放进
/// <c>pickedCards{后缀}</c>。
///
/// 三个决定各管一段，组合起来就是十流程：
///   **在哪儿找**（<see cref="CardPickCollector"/>）—— 此牌 / 全部战斗牌堆 / 某几个牌堆 / 卡组；
///   **按哪一维筛**（<see cref="CardPickFilter"/>）—— 类型 / 牌名 / 标签 / 关键词 / 稀有度 / 不筛；
///   **怎么取**（第九流程）—— 全部 / 选择 / 随机 / 至多。
///
/// 有两条是"照游戏接口用"，不是我们加的判断：
///
///   **合法目标不足时全选。** 游戏每个入口都有
///   <c>if (!prefs.RequireManualConfirmation &amp;&amp; list.Count &lt;= prefs.MinSelect) return list;</c>，
///   所以"选择 N 张"在候选不足 N 张时会直接把候选全部返回；随机那条走 <c>TakeRandom</c>，
///   <c>Take</c> 天然截断。
///
///   **"至多 N 张"** 就是 <c>new CardSelectorPrefs(prompt, 0, N)</c>——构造时会自动把
///   <c>RequireManualConfirmation</c> 置真，于是由玩家自己点确认结束（原版"净化""守护"用的就是它）。
/// </summary>
public static class CardPickEmitter
{
    /// <summary>
    /// 发出挑牌代码。结果变量名是 <c>pickedCards{nameSuffix}</c>。
    /// </summary>
    /// <param name="effect">这条效果（筛选维度在 <c>Pick</c> 上、两个费用阈值在它自己身上）。</param>
    /// <param name="ctx">外部表达式。</param>
    /// <param name="effectValue">
    /// **这条效果自己的数值表达式**（变量模块算好的：固定值读它那个变量的 BaseValue、
    /// 计算型读算出来的值）。只有 <see cref="CardPickPolicy.CountFromEffect"/> 的效果用得到它
    /// ——那种效果的"挑几张"就是它的 base 值，见消耗牌。其余效果这一项不参与。
    /// </param>
    /// <param name="thresholds">
    /// 费用阈值的解析器（把 Energy / Star 角色解成 C# 表达式）。**费用维度没启用时可以是 null**；
    /// 启用了而没传就是调用方的错（<see cref="CardPickFilter"/> 会当场抛）。
    /// </param>
    /// <param name="nameSuffix">同名变量的后缀（一张牌上多条选牌效果时区分开）。</param>
    /// <param name="policy">
    /// 这条效果的策略（允许哪些选项、合法性约束）。**由调用方按种类现算**
    /// （<see cref="CardPickPolicies.For"/>）——它不存进模型，免得留一份会过期的白名单。
    /// </param>
    public static IEnumerable<string> Lines(
        EffectDef effect,
        CardPickPolicy policy,
        CardPickEmitContext ctx,
        string effectValue,
        ThresholdResolver? thresholds = null,
        string nameSuffix = "")
    {
        CardPick pick = effect.Pick;
        string var = $"pickedCards{nameSuffix}";

        // 作用对象 = 这张牌自己：牌堆与筛选都无关（原版"王者之踢"改自身费用就是这么写的）。
        // 只有卡牌能用（状态没有"这张牌"），校验器会拦下状态里的误用。
        if (pick.Source == PickSource.ThisCard)
        {
            yield return $"var {var} = new List<CardModel> {{ this }};";
            yield break;
        }

        // 按牌名筛，却没指定是哪张牌：筛不到任何东西。校验器会报错，这里给个空集合，
        // 免得生成出"按 null 类型匹配"这种坏代码。
        if (pick.CardIds.Count > 0 && pick.CardIds.All(string.IsNullOrWhiteSpace))
        {
            yield return "// 按牌名筛却没指定是哪张牌，筛不到任何牌。";
            yield return $"var {var} = new List<CardModel>();";
            yield break;
        }

        // 合法性约束由**效果自己的策略**给出（升级只许可升级、变化只许可变化、附魔只许可附魔），
        // 作为一次独立的 .Where / 独立参数串上去——不能塞进另一个 lambda 里拼 &&，
        // 那样生成的是"bool && lambda"，编译不过。
        string? extraFilter = CardPickFilter.ConstraintFilter(policy.Constraint, policy.EnchantmentType);

        string list = CardPickCollector.CardList(pick, ctx.Player, ctx.SetCards);
        string? inline = CardPickFilter.PredicateOn("c", effect, thresholds, ctx.TypeRef);

        // 选择 / 至多：走游戏的选择界面（自己会处理"候选不足"与"至多"）。
        if (pick.Mode is PickMode.Choose or PickMode.AtMost)
        {
            foreach (string line in SelectionLines(effect, policy, ctx, inline, extraFilter, effectValue, thresholds, var))
            {
                yield return line;
            }

            yield break;
        }

        if (pick.Mode == PickMode.All)
        {
            // 带维度筛选时把 .ToList() 放在续行上：一行里连着两次 lambda 会很长，
            // 读起来是"先按维度筛、再按要求筛、最后取列表"三步。
            if (inline is not null)
            {
                yield return $"var {var} = {list}.Where(c => {inline})";

                if (extraFilter is not null)
                {
                    yield return $"    .Where({extraFilter})";
                }

                yield return "    .ToList();";
                yield break;
            }

            yield return $"var {var} = {Compose(list, null, extraFilter)}.ToList();";
            yield break;
        }

        // 随机：先算出张数，再从中随机取。
        string source = Compose(list, inline, extraFilter);

        (IReadOnlyList<string> setup, string count) = CountExpression(pick, policy, ctx, effectValue);

        foreach (string line in setup)
        {
            yield return line;
        }

        yield return $"var {var} = {source}.ToList()";
        yield return $"    .TakeRandom({count}, {ctx.Player}.RunState.Rng.CombatCardSelection)";
        yield return "    .ToList();";
    }

    /// <summary>把"候选集合 + 维度筛选 + 额外筛选"拼成一个表达式（调用方后接 <c>.ToList()</c>）。</summary>
    private static string Compose(string list, string? inline, string? extraFilter)
    {
        string result = inline is null ? list : $"{list}.Where(c => {inline})";

        if (extraFilter is not null)
        {
            result += $".Where({extraFilter})";
        }

        return result;
    }

    /// <summary>
    /// 选牌界面那一路（选择 / 至多）。入口按"在哪儿找"选：单个战斗牌堆走
    /// <c>FromCombatPile</c>（有牌堆感知的界面），多牌堆 / 全战斗牌堆走 <c>FromSimpleGrid</c>
    /// （通用网格），卡组走 <c>FromDeck*</c> 一族（自带升级 / 变化 / 附魔预览）。
    /// </summary>
    private static IEnumerable<string> SelectionLines(
        EffectDef effect,
        CardPickPolicy policy,
        CardPickEmitContext ctx,
        string? inline,
        string? extraFilter,
        string effectValue,
        ThresholdResolver? thresholds,
        string var)
    {
        CardPick pick = effect.Pick;

        // 提示语用这张牌自己的 `cards` 表条目（`<ENTRY>.selectionScreenPrompt`）——这正是原版
        // 的做法（见 CardModel.SelectionScreenPrompt：它读的就是 cards 表里这个键）。
        // 不能用游戏自带的 TO_UPGRADE / TO_EXHAUST 那些：它们说的是别的操作，会误导玩家。
        // 条目由 Exporter 写进 cards.json；缺了它游戏取文本时会抛异常，所以两边必须配套。
        string prompt = ctx.PromptEntry is null
            ? "CardSelectorPrefs.ExhaustSelectionPrompt"
            : $"new LocString(\"cards\", \"{ctx.PromptEntry}.selectionScreenPrompt\")";

        (IReadOnlyList<string> setup, string count) = CountExpression(pick, policy, ctx, effectValue);

        foreach (string line in setup)
        {
            yield return line;
        }

        // 选择：MinSelect = MaxSelect = N，玩家必须正好挑 N 张。
        // 至多：MinSelect = 0、MaxSelect = N，玩家挑不超过 N 张（游戏原生支持）。
        string prefs = pick.Mode == PickMode.AtMost
            ? $"new CardSelectorPrefs({prompt}, 0, {count})"
            : $"new CardSelectorPrefs({prompt}, {count})";

        // ---- 卡组：走专用入口，自带预览 ----
        // 附魔那个入口还要一个 int 形式的张数，所以把算好的 `count` 一起带下去
        // （它可能与 prefs 里的写法不同：那边要括号包起来）。
        if (pick.Source == PickSource.Deck)
        {
            foreach (string line in DeckSelectionLines(effect, policy, ctx, inline, count, prefs, var))
            {
                yield return line;
            }

            yield break;
        }

        // ---- 单个战斗牌堆 + 没有维度筛选：走 FromCombatPile，界面知道这是哪个牌堆 ----
        string? singlePile = CardPickCollector.SingleCombatPile(pick, ctx.Player);

        if (singlePile is not null && inline is null)
        {
            // 牌堆本身已经是候选集合了，额外筛选（若有）作为 filter 参数传进去，
            // 这样不可选的目标在界面上根本不出现。
            yield return $"var {var} = (await CardSelectCmd.FromCombatPile(";
            yield return $"    {ctx.ChoiceContext}, {singlePile}, {ctx.Player},";
            yield return extraFilter is null
                ? $"    {prefs})).ToList();"
                : $"    {prefs}, {extraFilter})).ToList();";
            yield break;
        }

        // ---- 多牌堆 / 全战斗牌堆 / 带维度筛选的单牌堆：走通用网格 ----
        //
        // FromSimpleGrid 接受任意牌列表，所以自己拼出来的跨牌堆并集交给它就行，不必自己做
        // 选牌界面。它用**索引**在多人之间同步，要求列表"确定性生成"（见 CardPickCollector）。
        string filter = Compose(CardPickCollector.CardList(pick, ctx.Player, ctx.SetCards), inline, extraFilter);

        yield return $"var {var} = (await CardSelectCmd.FromSimpleGrid(";
        yield return $"    {ctx.ChoiceContext}, {filter}.ToList(), {ctx.Player},";
        yield return $"    {prefs})).ToList();";
    }

    /// <summary>
    /// 卡组的选牌：有专属预览的约束走专用入口，其余走通用入口。
    /// </summary>
    /// <param name="count">
    /// 已经算好的张数表达式（<see cref="SelectionLines"/> 那一步算的）。附魔入口的第三个参数
    /// 是 <c>int</c> 形的张数，就传它——**不能在这里另算一遍**：计算型张数会先算出变量值，
    /// 两处各算一次就会发出两条同名/重复的取值代码。
    /// </param>
    private static IEnumerable<string> DeckSelectionLines(
        EffectDef effect,
        CardPickPolicy policy,
        CardPickEmitContext ctx,
        string? inline,
        string count,
        string prefs,
        string var)
    {
        switch (policy.Constraint)
        {
            case CardConstraintKind.Upgradable:
                yield return $"var {var} = (await CardSelectCmd.FromDeckForUpgrade(";
                yield return $"    {ctx.Player}, {prefs})).ToList();";
                yield break;

            case CardConstraintKind.Transformable:
                yield return $"var {var} = (await CardSelectCmd.FromDeckForTransformation(";
                yield return $"    {ctx.Player}, {prefs})).ToList();";
                yield break;

            case CardConstraintKind.Enchantable:
                // 附魔必须走**带 additionalFilter 的重载**：那个"传牌列表"的重载要求每张牌都在
                // 卡组里且可附魔，否则抛 ArgumentException——筛过的列表正好会违反它。
                // additionalFilter 的参数是可空的（Func<CardModel?, bool>），所以谓词要写成可空安全
                // （成员访问用 c!），并且先判 null——否则筛"类型/标签"时会对 null 解引用。
                yield return $"var {var} = (await CardSelectCmd.FromDeckForEnchantment(";
                yield return $"    {ctx.Player}, ModelDb.Enchantment<{policy.EnchantmentType ?? "EnchantmentModel"}>(),";
                yield return $"    {count}, {(inline is null ? "null" : $"c => c is not null && ({inline})")},";
                yield return $"    {prefs})).ToList();";
                yield break;

            default:
                // 卡组通用入口。没有筛选时省掉那个参数。
                yield return $"var {var} = (await CardSelectCmd.FromDeckGeneric(";
                yield return inline is null
                    ? $"    {ctx.Player}, {prefs})).ToList();"
                    : $"    {ctx.Player}, {prefs}, c => {inline})).ToList();";
                yield break;
        }
    }

    /// <summary>
    /// 流程十：选牌数的表达式（C# 的 <c>int</c>，直接塞进 <c>CardSelectorPrefs</c> /
    /// <c>TakeRandom</c>）。
    ///
    /// 两条来源，由策略决定：
    ///   <see cref="CardPickPolicy.CountFromEffect"/> —— 张数就是**效果自己的数值**
    ///   （<c>&lt;base&gt;</c> 三件套），表达式由变量模块算好传进来（消耗牌走这条）；
    ///   否则 —— 用 <see cref="CardPick.Count"/> 自己那一套（升级 / 搬运 / 附魔…那些数值
    ///   另有含义的效果），那种情况要先算出变量值，所以返回若干条前置语句。
    ///
    /// 选择 / 随机时按 <c>Math.Max(1, …)</c> 保底（"取 0 张"没有意义）；**至多不保底**
    /// （0 张是合法的：玩家可以不选）。
    /// </summary>
    private static (IReadOnlyList<string> Setup, string Expr) CountExpression(
        CardPick pick,
        CardPickPolicy policy,
        CardPickEmitContext ctx,
        string effectValue)
    {
        bool clamp = pick.Mode is PickMode.Choose or PickMode.Random;

        // 张数 = 效果数值：变量模块已经把它算成表达式了（固定值读那个变量的 BaseValue、
        // 计算型读算出来的值），这里只做保底与取整——张数是 int（界面上的张数、随机取的个数）。
        if (policy.CountFromEffect)
        {
            if (effectValue.Length == 0)
            {
                // 编程错误，不是用户输入问题：这条效果的张数必须由调用方把效果数值传进来。
                // 直接抛，免得静默生成"取 0 张"的卡（那种卡看不出坏，只是什么都不做）。
                throw new InvalidOperationException(
                    $"效果 {pick.GetType().Name} 的张数来自它的数值，但调用方没有把数值表达式传进 CardPickEmitter。");
            }

            return (Array.Empty<string>(),
                clamp ? $"(int)Math.Max(1m, {effectValue})" : $"(int)Math.Max(0m, {effectValue})");
        }

        if (!pick.Count.IsCalculated)
        {
            int value = (int)pick.Count.Base;
            return (Array.Empty<string>(),
                clamp ? $"(int){Math.Max(1, value)}" : $"(int){Math.Max(0, value)}");
        }

        // 计算型：变量值先算出来存进局部变量，再按"基础值 + 变量 × 变量系数"合成。
        var setup = SourceEmitter
            .EmitValue(pick.Count.Variable, ctx.SourceCtx, "pickCount")
            .ToList();

        string raw = pick.Count.Base == 0m
            ? $"pickCount * {pick.Count.Coefficient}m"
            : $"{pick.Count.Base}m + pickCount * {pick.Count.Coefficient}m";

        return (setup, clamp ? $"(int)Math.Max(1m, {raw})" : $"(int)Math.Max(0m, {raw})");
    }
}
