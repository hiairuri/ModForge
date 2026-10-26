using System.Text;
using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.CardSets;
using ModForge.Core.Modules.Targets;
using ModForge.Core.Modules.ValueSources;
using ModForge.Core.Modules.Variables;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 把一条效果编译成 C# 语句。卡牌与状态共用，所以"效果定义 → 游戏 API 调用"的映射
/// 只有一处实现。
///
/// 数值有两种来源：
///   固定值   —— 读卡牌声明的变量（有变量时），否则写死字面量
///   计算型   —— 调用卡牌上的 ValueMultiplier，它按原版的 CalculatedVar 机制算出实时值
/// </summary>
internal sealed class EffectEmitter
{
    /// <summary>方法体里最外层的缩进（4 的倍数，与生成的源码一致）。</summary>
    private const string Indent = "        ";

    private readonly ModProject _project;
    private int _calcCounter;

    /// <summary>增伤效果用的局部变量序号（同一张牌上可能有多条）。</summary>
    private int _boostCounter;

    /// <summary>
    /// 增伤 / 重放效果领取的**独立**变量序号（<c>boost0</c> / <c>pickedCards0</c>）。
    ///
    /// 不与 <see cref="NextLocalIndex"/> 那个序列共用：共用的话这里每领一次号，
    /// 别的效果的变量编号会整体平移——"多写一条增伤"就会把一堆与此无关的生成代码
    /// 也改掉，逐字节对比没法看。独立的计数器让增伤家族自己编号。
    /// </summary>
    internal string NextBoostIndex() =>
        _boostCounter++.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// 生成代码里那些中间变量的序号（给<strong>已搬进 <c>Kinds/</c></strong>的效果用）。
    ///
    /// 一张牌上可能有好几条同类效果（两条增伤、两条消耗、三条激发球），各写各的
    /// <c>var pickedCards</c> / <c>var evokeQueue</c> 就会**重名、编译不过**——实测撞过两次
    /// （选牌那次、激发那次）。所以中间变量的名字一律带上这个卡内唯一的编号。
    /// </summary>
    private int _localCounter;

    /// <summary>领一个卡内唯一的编号（同一条效果要凑一对变量时，取一次、两处共用）。</summary>
    internal int NextLocalIndex() => _localCounter++;

    /// <summary>
    /// 带业务标签的编号：选牌那些局部变量名长，编号前加个标签才读得出是干什么的
    /// （<c>pickedCardsExhaust0</c>）。
    /// </summary>
    internal string NextPickSuffix(string tag) => $"{tag}{NextLocalIndex()}";

    /// <summary>本卡上第几条"无名"的选牌效果（见 <see cref="NextPlainPickSuffix"/>）。</summary>
    private int _plainPickVars;

    /// <summary>
    /// "这条效果的选牌结果"那个局部变量的**后缀**：本卡上第一条是空（名字就是
    /// <c>pickedCards</c>），第二条起给一个卡内唯一的编号。
    ///
    /// 为什么第一条保持空后缀：只有一条挑牌效果的卡**一个字节都不变**（既有工程零改动）。
    /// 为什么第二条起必须带：不带的话两条各写一份 <c>var pickedCards</c> 就是重名、
    /// 编译不过——而且这不是理论上会撞：一条"降费" + 一条"升级"就撞（实测）。
    /// 带后缀的那几条（消耗 / 丢弃 / 自动打出 / 复制 / 重放）本来就有 tag，不受影响。
    /// </summary>
    internal string NextPlainPickSuffix() =>
        _plainPickVars++ == 0
            ? string.Empty
            : NextLocalIndex().ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// 把状态模型 id 解析成 C# 类型引用（"状态层数"来源要用）。
    ///
    /// 由生成器注入 <c>PowerRefs.TypeRef</c>：本工程自定义的状态住在本项目命名空间，
    /// 原版的住在游戏命名空间，猜错会编译不过。为 null 时退回按原版命名空间猜。
    /// </summary>
    public Func<string, string>? PowerTypeFor { get; set; }

    /// <summary>
    /// 把卡牌模型 id 解析成 C# 类型引用（"按条件数牌"的牌 ID 那一维要用，判定是 <c>c is X</c>）。
    ///
    /// 与 <see cref="PowerTypeFor"/> 同一个理由与同一个注入处（生成器给
    /// <c>CardRefs.TypeRef</c>）：本工程的卡住本项目命名空间，原版的住游戏命名空间。
    /// </summary>
    public Func<string, string>? CardTypeFor { get; set; }

    /// <summary>
    /// 需要生成的取值方法。就地计算的数值（施加状态量、牌堆张数等）会登记到这里，
    /// 由调用方在类里发出对应的方法体；游戏只为伤害/格挡提供了计算变量，
    /// 其它种类的计算必须自己写方法。
    /// </summary>
    public List<PendingCalcMethod> PendingMethods { get; } = new();

    /// <summary>
    /// 需要生成的**条件侧**取值方法（启用条件的两侧各至多一个，见
    /// <see cref="ConditionGuard"/>）。与 <see cref="PendingMethods"/> 同一套字段机制
    /// （<c>_calcBase{n}</c> / <c>_calcPerUnit{n}</c>，升级改字段），所以字段序号
    /// 由 <see cref="NextFieldIndex"/> 统一分配——两份名单各编号的话会撞字段名。
    /// </summary>
    public List<PendingConditionMethod> PendingConditions { get; } = new();

    /// <summary>
    /// 下一个"就地计算字段"的序号：数值/次数与条件侧共享一个编号空间。
    /// 没有条件的工程两份名单里只有 <see cref="PendingMethods"/>，编号与旧算法
    /// （只数它自己）完全一致——既有产物一个字节都不变。
    /// </summary>
    internal int NextFieldIndex() => PendingMethods.Count + PendingConditions.Count;

    /// <summary>
    /// 需要生成的**费用阈值来源**取值方法（见 <see cref="PendingThresholdMethod"/>），由调用方
    /// 在类里发出方法体。计算型的费用阈值都走这一条，而不是就地铺语句。
    /// </summary>
    public List<PendingThresholdMethod> PendingThresholds { get; } = new();

    /// <summary>费用阈值取值方法的序号（一卡内唯一，见 <see cref="CostThresholdExpr"/>）。</summary>
    private int _thresholdCounter;

    /// <summary>
    /// 正在生成的这张牌的 `cards` 表条目名（如 DUAL_WIELD）。
    ///
    /// 只用于"让玩家挑牌"类效果的选择界面提示语——游戏要求提示语来自这张牌的
    /// `<条目>.selectionScreenPrompt`（原版 CardModel.SelectionScreenPrompt 就是这么取的），
    /// 所以生成代码时得知道是哪张牌。为 null 时（例如状态里）退化用游戏自带的通用提示。
    /// </summary>
    public string? CardEntry { get; set; }

    public EffectEmitter(ModProject project)
    {
        _project = project;
    }

    /// <summary>
    /// 一个待生成的"就地计算"取值方法。
    ///
    /// <paramref name="ForRepeat"/> 为真时算的是**执行次数**（用 <c>RepeatBase</c> /
    /// <c>RepeatSource</c>），否则算的是效果数值（用 <c>Amount</c> / <c>Source</c>）。
    /// 两者必须分开：原版"扯碎"的伤害是固定 5、次数才是算出来的，共用一个来源就写不出来。
    ///
    /// <paramref name="FieldIndex"/> 是它在生成类里那个 <c>_calcBase{n}</c> / <c>_calcPerUnit{n}</c>
    /// 字段的序号。**建方法时就定下来**，因为升级与等级表都要按它写字段名——早先是那些地方
    /// 在下标里反查（<c>FindIndex(ReferenceEquals(...))</c>），方法列表的顺序一变就静默错位：
    /// 编译照过，只是升级改到了另一个数值上。
    /// </summary>
    public sealed record PendingCalcMethod(
        string Name,
        EffectDef Effect,
        bool IsCard,
        bool ForRepeat = false,
        int FieldIndex = -1);

    /// <summary>
    /// 一个待生成的**费用阈值来源**取值方法：算出一个费用维度在"比什么数"里的**来源值**
    /// （基础值与变量系数不在这里，它们是卡上的变量，由表达式直接读）。
    ///
    /// 与 <see cref="PendingCalcMethod"/> 的区别：那个算的是整个数值（还要写私有字段），
    /// 这个只算来源那一段，且**没有字段**——所以两者各占一个列表，混在一起反而要到处判分支。
    ///
    /// 为什么要一个方法而不是就地铺语句：同一张卡上可能有多条效果在筛费用，每套取值都要一个
    /// 自己的 <c>player</c>；就地铺开会在同一个方法里重复声明同名局部量，C# 编译不过
    /// （CS0136，实测踩过）。发成方法后每份取值各有自己的作用域。
    /// </summary>
    public sealed record PendingThresholdMethod(
        string Name,
        EffectDef Effect,
        ValueRole Role,
        bool IsCard);

    /// <summary>
    /// 一个待生成的**条件侧**取值方法：算出条件某一侧的完整数值
    /// （基础值 + 来源 × 系数，见 <see cref="EffectCondition"/>）。
    ///
    /// 与 <see cref="PendingCalcMethod"/> 同一套字段机制（序号也共用同一个编号空间），
    /// 区别只在数值构成写死在登记项里（<see cref="Value"/>——它来自条件自己，
    /// 不读效果的数值/次数角色）。只有"选了来源或填了升级增量"的那一侧才登记；
    /// 全固定的一侧就是字面量，不需要方法。
    /// </summary>
    public sealed record PendingConditionMethod(
        string Name,
        EffectDef Effect,
        bool IsLeft,
        Modules.Variables.NumericValue Value,
        bool IsCard,
        int FieldIndex);

    /// <summary>
    /// 某条效果的"就地计算"方法分到的字段序号（<c>_calcBase{n}</c> 的 n）。
    ///
    /// 序号在**建方法时**就存在 <see cref="PendingCalcMethod.FieldIndex"/> 上，这里只是把它读出来。
    /// 升级与等级表要按它写字段名，早先那两处按方法在列表里的位置反推——列表顺序一变就静默错位。
    /// 找不到返回 -1（那条效果没有就地计算的方法）。
    /// </summary>
    public int FieldIndexFor(EffectDef effect, bool forRepeat) =>
        PendingMethods.FirstOrDefault(m => ReferenceEquals(m.Effect, effect) && m.ForRepeat == forRepeat)
            ?.FieldIndex ?? -1;

    public void Emit(StringBuilder sb, EffectDef effect, EmitContext ctx) => Emit(sb, effect, ctx, Indent);

    /// <summary>
    /// 同上，但从指定缩进开始写。用于把效果嵌进一层额外的判断里（例如"打出一张牌时"要
    /// 先筛打出的那张牌是否算数），让生成出来的源码缩进与结构一致。
    /// </summary>
    public void Emit(StringBuilder sb, EffectDef effect, EmitContext ctx, string indent)
    {
        // 启用条件：整条效果（含"重复 N 次"循环、自动打出的取牌堆）包在一层 if 里，
        // 条件不成立时这条效果什么都不做——但不影响同一张牌的其它效果。
        // 守卫里"算出来的那一侧"会登记取值方法（方法体在类级统一发），这里只写调用。
        if (effect.HasCondition)
        {
            sb.AppendLine($"{indent}if ({ConditionGuard(effect, ctx)})");
            sb.AppendLine($"{indent}{{");
            EmitUnguarded(sb, effect, ctx, indent + "    ");
            sb.AppendLine($"{indent}}}");
            return;
        }

        EmitUnguarded(sb, effect, ctx, indent);
    }

    /// <summary>守卫的**比较式**："左值 >= 右值"这个样子。两侧各是字面量或方法调用。</summary>
    private string ConditionGuard(EffectDef effect, EmitContext ctx)
    {
        EffectCondition condition = effect.Condition!;

        string op = condition.Comparison switch
        {
            ConditionComparison.AtLeast => ">=",
            ConditionComparison.AtMost => "<=",
            ConditionComparison.GreaterThan => ">",
            ConditionComparison.LessThan => "<",
            ConditionComparison.Equal => "==",
            _ => "!=",
        };

        return $"{ConditionSideExpr(effect, condition, isLeft: true, ctx)} {op} "
            + ConditionSideExpr(effect, condition, isLeft: false, ctx);
    }

    /// <summary>
    /// 条件某一侧的表达式：全固定（没有来源、也没有升级增量）时是字面量；
    /// 否则登记一个取值方法（基础值与系数进私有字段，升级改字段——与"就地计算"的数值同一套）。
    /// </summary>
    private string ConditionSideExpr(EffectDef effect, EffectCondition condition, bool isLeft, EmitContext ctx)
    {
        Modules.Variables.NumericValue side = condition.SideOf(isLeft);

        if (!condition.SideNeedsField(isLeft))
        {
            return $"{(int)side.Base}m";
        }

        string name = $"CondSide{_conditionCounter++}";
        PendingConditions.Add(new PendingConditionMethod(
            name, effect, isLeft, side, ctx.HasDynamicVars, NextFieldIndex()));

        return $"{name}({ctx.CardTarget ?? "null"})";
    }

    /// <summary>条件侧取值方法的序号（升级语句按它找字段）。没登记过返回 -1。</summary>
    public int FieldIndexForCondition(EffectDef effect, bool isLeft) =>
        PendingConditions.FirstOrDefault(m => ReferenceEquals(m.Effect, effect) && m.IsLeft == isLeft)
            ?.FieldIndex ?? -1;

    /// <summary>条件侧取值方法的计数器（一张牌从零开始，与其它方法名各走各的）。</summary>
    private int _conditionCounter;

    private void EmitUnguarded(StringBuilder sb, EffectDef effect, EmitContext ctx, string indent)
    {
        // 自动打出（牌堆那一路）自己处理"重复打出"：它的重复要作用在**打出**上、
        // 不重新选牌，所以不能套这层通用循环——那会把"取牌堆"也一起重复
        // （见 AutoPlayBehavior 里的说明）。它的分支在 EmitOnce 里发。
        if (effect.Kind == EffectKind.AutoPlay && !effect.IsSelfAutoPlay)
        {
            EmitOnce(sb, effect, ctx, indent);
            return;
        }

        (IReadOnlyList<string> setup, string count) = RepeatCount(effect, ctx, indent);

        // 固定就一次、也没有要先算的东西 → 直接发一次，省掉一层循环。
        if (setup.Count == 0 && count == "1")
        {
            EmitOnce(sb, effect, ctx, indent);
            return;
        }

        foreach (string line in setup)
        {
            sb.AppendLine(line);
        }

        sb.AppendLine($"{indent}for (int i = 0; i < {count}; i++)");
        sb.AppendLine($"{indent}{{");

        EmitOnce(sb, effect, ctx, indent + "    ");

        sb.AppendLine($"{indent}}}");
    }

    /// <summary>
    /// "重复 N 次"里那个 N 的取值。
    ///
    /// 分成两段返回：<c>Setup</c> 是循环之前要写的语句（计算型次数要先算出来），
    /// <c>Count</c> 是次数表达式。Emit 与 EmitAutoPlay 都用它——两处各写一份的话，
    /// 计算型次数、可升级次数的处理迟早分叉。
    /// </summary>
    public (IReadOnlyList<string> Setup, string Count) RepeatCount(
        EffectDef effect,
        EmitContext ctx,
        string indent)
    {
        // 计算型的次数：先算出来再循环。卡牌走"取值方法"（卡面显示的次数也要能跟着变，
        // 与数值同一套机制；原版"扯碎"声明 CalculatedHits 就是这么用的），
        // 状态里没有变量表，就地算。
        if (effect.CalculatedRepeat)
        {
            if (ctx.HasDynamicVars)
            {
                string target = ctx.CardTarget ?? "null";
                string methodName = $"CalcRepeat{_calcCounter++}";

                PendingMethods.Add(new PendingCalcMethod(
                    methodName, effect, IsCard: true, ForRepeat: true, FieldIndex: NextFieldIndex()));

                return (Array.Empty<string>(), $"(int){methodName}({target})");
            }

            var setup = SourceEmitter
                .EmitValue(
                    effect.RepeatSource,
                    SourceEmitter.Context.ForPower() with
                    {
                        PowerTypeFor = PowerTypeFor,
                        CardTypeFor = CardTypeFor,
                    },
                    "repeatCount")
                .Select(line => indent + line)
                .ToList();

            return (setup, $"(int)({effect.RepeatBase}m + repeatCount)");
        }

        // 固定次数：能升级时读变量（升级改的是变量，不是字面量）。
        // 只有卡牌能声明变量；状态里没有变量表，只能写死。
        int repeat = Math.Max(1, effect.Repeat);
        bool useVar = effect.Upgrade.RepeatDelta != 0 && ctx.HasDynamicVars;

        // 次数变量按这条效果自己的槽取名（同类多条时各占一个，否则两条会一起涨）。
        VarSlot? repeatSlot = ctx.RepeatSlotFor?.Invoke(effect);

        return (Array.Empty<string>(),
            useVar && repeatSlot is not null ? repeatSlot.IntValueExpression() : repeat.ToString());
    }

    private void EmitOnce(StringBuilder sb, EffectDef effect, EmitContext ctx, string indent)
    {
        string amount = AmountExpr(effect, ctx, indent, sb);

        // 已搬进 Kinds/ 的效果：这条语句由它自己的行为发出。
        if (EffectCatalog.Find(effect.Kind) is IEffectBehavior behavior)
        {
            behavior.Emit(new EffectEmit(sb, effect, ctx, indent, amount, this));
            return;
        }

        switch (effect.Kind)
        {
            // "这张牌自己"的自动打出走到这里时什么也不发（牌堆那一路在上面被直通进来、
            // 由 AutoPlayBehavior 发；自档本身由 CodeGenerator 挑去走引擎的自动打出手时机）。
            case EffectKind.AutoPlay:
                break;

            default:
                sb.AppendLine($"{indent}// 尚未支持的效果：{effect.Kind}");
                break;
        }
    }

    /// <summary>
    /// 从指定牌堆里取出要处理的牌，写进 <c>pickedCards</c> 局部变量。
    ///
    /// 真正出码的是选牌模块（<see cref="CardPickEmitter"/>）：那边一处决定"在哪儿找 / 按哪一维筛 /
    /// 怎么取"，这里只把卡牌上下文翻译成它要的表达式。选牌这件事曾经有九个调用点各拼一遍字符串，
    /// 于是"同一件事有几种写法"是必然的——那些写法现在都在模块里。
    ///
    /// 合法性约束（升级只许可升级、变化只许可变化、附魔只许可附魔）由效果自己的策略给出
    /// （见 <see cref="CardPickPolicies"/>），不再由调用点各传一个裸谓词字符串。
    /// </summary>
    /// <param name="amount">
    /// 这条效果的数值表达式（<see cref="AmountExpr"/> 算出来的那个）。
    /// **"张数就是效果数值"的效果要用它当挑牌的张数**（消耗牌，见
    /// <c>CardPickPolicy.CountFromEffect</c>）；其余效果不参与。
    /// 必须传 <see cref="AmountExpr"/> 的结果、不要在这里另算一遍：计算型的数值会登记一个取值方法，
    /// 算两次会登记两个（顺序一错，升级就改到别的字段上——那条注释在 PendingCalcMethod 上）。
    /// </param>
    /// <param name="nameSuffix">同名变量的后缀（一张牌上多条选牌效果时区分开）。</param>
    internal IEnumerable<string> PickCardsLines(
        EffectDef effect,
        EmitContext ctx,
        string amount,
        string nameSuffix = "")
    {
        // 策略按种类现算（模型里不存白名单，免得留一份会过期的）。
        CardPickPolicy policy = CardPickPolicies.For(effect.Kind);

        // 附魔的合法性判据要按附魔类型来写（`ModelDb.Enchantment<T>().CanEnchant`），
        // 而类型引用只有工程知道，所以在这里补上。
        if (policy.Constraint == CardConstraintKind.Enchantable)
        {
            policy = policy with
            {
                EnchantmentType = string.IsNullOrWhiteSpace(effect.EnchantmentId)
                    ? null
                    : EnchantmentRefs.TypeRef(_project, effect.EnchantmentId!),
            };
        }

        var emitCtx = new CardPickEmitContext(
            Player: ctx.Player,
            ChoiceContext: ctx.ChoiceContext,
            PromptEntry: CardEntry,
            TypeRef: CardModelTypeRef,
            IsCard: ctx.HasDynamicVars,
            PowerTypeFor: PowerTypeFor);

        // 两个费用维度各自启用时才去解阈值。解的动作**只登记一个取值方法**（不再就地铺
        // 前置语句），所以这里不必再关心语句顺序；缓存一下保证每个角色只登记一次
        // （同一个角色被解两次会多发一个没人调的方法）。
        var resolved = new Dictionary<ValueRole, string>();

        string Resolve(ValueRole role)
        {
            if (!resolved.TryGetValue(role, out string? expr))
            {
                expr = CostThresholdExpr(effect, role, ctx);
                resolved[role] = expr;
            }

            return expr;
        }

        bool needsThreshold = CostRoles.All.Any(effect.Pick.UsesThreshold);

        // 来源是临时牌集合时，"取哪些牌"由这里算好交给选牌模块（键的规则在 CardSets 模块里）。
        string? setCards = effect.Pick.Source == PickSource.Set
            ? SetReadExpr(effect.Pick.SetId, ctx)
            : null;

        foreach (string line in CardPickEmitter.Lines(
            effect, policy, emitCtx with { SetCards = setCards }, amount,
            needsThreshold ? Resolve : null,
            nameSuffix))
        {
            yield return line;
        }

        // 存进临时牌集合：挑出来的那几张就是这一组。
        //
        // 放在**共用层**而不是各效果里：会挑牌的效果有十二种，各写一遍等于十二处会漏的地方
        // （这就是"一个事实只有一处实现"的老规矩）。不填集合 ID 时这一行根本不发，
        // 所以既有工程一个字节都不动。
        if (SetStoreLine(effect, ctx, $"pickedCards{nameSuffix}") is string store)
        {
            yield return store;
        }
    }

    /// <summary>
    /// 把一组牌存进临时牌集合的那一行；这条效果不存集合（或当前上下文给不出集合）时返回 null。
    ///
    /// 三件事都要成立才发：这条效果**产出一组牌**（<see cref="CardSetCatalog.Stores"/>）、
    /// 填了集合 ID、而且当前在**一次出牌结算**里（卡牌的 <c>OnPlay</c> 才有"局内的牌地址"，
    /// 状态里没有——那种组合由校验器拦下）。
    /// </summary>
    internal string? SetStoreLine(EffectDef effect, EmitContext ctx, string cardsExpr)
    {
        if (!CardSetCatalog.Stores(effect.Kind) ||
            string.IsNullOrWhiteSpace(effect.SetId) ||
            ctx.SetAddress is null ||
            ctx.EffectNumberFor is null)
        {
            return null;
        }

        return CardSetEmitter.Store(
            ctx.SetAddress, ctx.EffectNumberFor(effect), effect.SetId!, cardsExpr);
    }

    /// <summary>
    /// 取集合里那些牌的表达式（键 = <c>地址.定义它的效果编号.集合ID</c>）。
    ///
    /// 编号取的是**定义那个集合的那条效果**的号，不是读它的这条的号——读的那条只需要知道
    /// "读哪个集合"，由模块查出谁写的（<see cref="CardSetCatalog.DefinitionNumber"/>）。
    /// 查不到（没人定义）时给一段空列表：生成结果仍可编译，问题由校验器报出来。
    /// </summary>
    internal string? SetReadExpr(string? setId, EmitContext ctx)
    {
        if (ctx.SetAddress is null || ctx.SetNumberFor is null || string.IsNullOrWhiteSpace(setId))
        {
            return null;
        }

        int number = ctx.SetNumberFor(setId!);

        return number == 0
            ? null
            : CardSetEmitter.Cards(ctx.SetAddress, number, setId!);
    }

    /// <summary>
    /// 把一个**费用阈值角色**解成 C# 表达式。
    ///
    /// 阈值的构成 = <c>基础值 + 来源 × 变量系数</c>，与效果数值同一套读法，只是三个零件
    /// 都在卡上的变量里：基础值 <c>EnergyAmount</c>、变量系数 <c>EnergyAmountExtra</c>
    /// （见变量模块的 <c>ThresholdSlots</c>）。**两个数都是变量**，所以升级、等级表都改得到
    /// 它们（校验器不再把系数算作"填了不生效"）。
    ///
    /// 计算型的**取值**发成一个方法（登记到 <see cref="PendingThresholds"/>），表达式里写
    /// 方法调用：
    ///
    /// 为什么不就地铺语句（效果数值与张数都是就地铺的）：同一张卡上可能有多条效果在筛费用，
    /// 而每条的各套取值都要一个自己的 <c>player</c>；就地铺开会让它们在同一个方法里重复声明，
    /// C# 直接编译不过（CS0136，实测踩过：两个计算型阈值的那种卡）。发成方法后每份取值各有
    /// 自己的作用域，顺带把"阈值 + 张数都是算出来的"那种组合也一并解掉。
    ///
    /// 状态里没有变量表（<see cref="EmitContext.HasDynamicVars"/> 为假）：那种地方阈值只能写
    /// 字面量——升级与等级表都改不到它，校验器会把这种组合报出来。
    /// </summary>
    private string CostThresholdExpr(EffectDef effect, ValueRole role, EmitContext ctx)
    {
        Modules.Variables.NumericValue value = effect.NumericOf(role);
        Modules.Variables.VarSlot? slot = ctx.ThresholdSlotFor?.Invoke(effect, role);

        string baseExpr = slot is not null
            ? slot.ValueExpression(ctx.CardTarget ?? "null")
            : ((int)value.Base).ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (!value.IsCalculated)
        {
            return baseExpr;
        }

        string name = $"CalcPick{role}{_thresholdCounter++}";

        PendingThresholds.Add(new PendingThresholdMethod(name, effect, role, ctx.HasDynamicVars));

        // 基础值与变量系数两项都照写（哪怕基础值是 0）：两个都是变量，
        // 升级与等级表可以抬它们，省掉任何一项就等于把那条升级路径编译掉了。
        string coefficient = slot is not null
            ? slot.CoefficientExpression()
            : $"{value.Coefficient}m";

        return $"{baseExpr} + {name}({ctx.CardTarget ?? "null"}) * {coefficient}";
    }

    /// <summary>
    /// 按作用对象把一条语句发出去。这是"作用于目标"那一族效果共用的目标循环，
    /// 所以留在共用层——各效果只写"对每个目标做什么"（见 <see cref="EffectEmit.AgainstTargets"/>）。
    /// </summary>
    internal void EmitAgainstTargets(
        StringBuilder sb,
        EffectDef effect,
        EmitContext ctx,
        string indent,
        Func<string, string> statement) =>
        EmitAgainstTargets(sb, effect, ctx, indent,
            (target, at) => new[] { $"{at}{statement(target)}" });

    /// <summary>
    /// 同上，但"对每个目标做什么"可以是**多行**（见 <see cref="EffectEmit.AgainstTargetsBlock"/>）。
    ///
    /// <paramref name="body"/> 收"目标变量名"与"这一层的缩进"，返回该层要写的那几行
    /// （每行自带缩进，不含换行符）。所以目标循环本身只写一遍，
    /// "一句"与"一段"两种用法共用同一个骨架——各写一份的话，两者的目标分支迟早对不上
    /// （多支持一种目标时容易只改一处）。
    ///
    /// **展开本身在目标模块**（<c>Modules/Targets/</c>）：这里只把共用的表达式转成
    /// <see cref="TargetContext"/>。作用域（造物 / 玩家 / 牌）由这条效果的行为声明，从
    /// 效果目录问出来——这样"哪几档目标能用"与"怎么发"在同一本账上，不会各写一套
    /// （以前正是各写一套：移除状态少一档、回复生命与造成伤害还不一样）。
    /// </summary>
    internal void EmitAgainstTargets(
        StringBuilder sb,
        EffectDef effect,
        EmitContext ctx,
        string indent,
        Func<string, string, IEnumerable<string>> body)
    {
        var target = new TargetContext(
            Self: ctx.Self,
            Player: ctx.Player,
            CardTarget: ctx.CardTarget,
            Enemies: ctx.Enemies,
            PlayerCreatures: ctx.PlayerCreatures,
            CombatState: ctx.CombatState,
            Rng: ctx.Rng,
            NextIndex: () => _targetCounter++);

        TargetEmitter.Emit(
            sb,
            EffectCatalog.ScopeOf(effect.Kind),
            effect.Target,
            target,
            indent,
            body);
    }

    /// <summary>
    /// 随机目标的局部变量编号（<c>picked0</c> / <c>picked1</c>…）。
    ///
    /// 用**独立的**计数器，不跟 <c>_localCounter</c>（选牌那些中间变量）共用：共用的话
    /// 这里每取一次编号，后面那些 <c>pickedCardsExhaust0</c> 的编号就会跟着整体平移，
    /// 于是"给目标加了随机这一档"会把一堆与此无关的生成代码也改掉，逐字节对比就没法看了。
    /// 与 <c>_localCounter</c> 一样是**每张牌从零开始**（每个文件一个新实例）。
    /// </summary>
    private int _targetCounter = 0;

    /// <summary>
    /// 数值表达式。
    ///
    /// 三种情况：
    ///   伤害/格挡的计算型 —— 走卡牌声明的 CalculatedDamage/CalculatedBlock 变量，
    ///                        这样卡面会显示实时算出的数值（原版就是这么做的）。
    ///   其它计算型        —— 就地算出来存进一个局部变量，因为游戏只为伤害/格挡提供了
    ///                        计算变量；施加状态的量、牌堆张数这类没有对应变量。
    ///   固定型           —— 优先读声明的变量，读不到就写死字面量。
    /// </summary>
    private string AmountExpr(EffectDef effect, EmitContext ctx, string indent, StringBuilder sb)
    {
        VarSlot? slot = ctx.SlotFor?.Invoke(effect);

        if (effect.IsCalculated)
        {
            string target = ctx.CardTarget ?? "null";

            // 伤害与格挡用游戏的计算变量：卡面上会显示算出来的实时数值。
            //
            // 走属性还是索引器由**槽**决定：占着原版名字的槽是原版类型，走
            // DynamicVars.CalculatedDamage 这类强类型属性；没占到的（第二个起，以及
            // "计算型伤害 + 计算型格挡"里后出现的那个）是我们的运行时子类，
            // 属性里写着转型，会抛 InvalidCastException，只能走索引器 + 转基类。
            //
            // 状态里没有变量表（slot 为 null）：没有槽可查，按原版形状读——原版
            // "完美打击"就是这个结构。
            if (effect.Kind is EffectKind.DealDamage or EffectKind.GainBlock)
            {
                if (slot is not null)
                {
                    return slot.ValueExpression(target);
                }

                string prop = effect.Kind == EffectKind.DealDamage
                    ? "CalculatedDamage"
                    : "CalculatedBlock";
                return $"DynamicVars.{prop}.Calculate({target})";
            }

            // 其它种类的计算没有现成变量，登记一个取值方法，这里只调用它。
            string methodName = $"CalcAmount{_calcCounter++}";
            PendingMethods.Add(new PendingCalcMethod(
                methodName, effect, ctx.HasDynamicVars, ForRepeat: false, FieldIndex: NextFieldIndex()));
            return $"{methodName}({target})";
        }

        // 固定值：同样按规划查自己那个变量（Damage / Damage2 / BlockNextTurn / StrengthPower2…）。
        if (ctx.HasDynamicVars && slot is not null)
        {
            return slot.ValueExpression("null");
        }

        return $"{effect.Amount}m";
    }

    /// <summary>
    /// 牌堆的 C# 枚举名。映射只有一处（在选牌模块里），因为选牌器与"放入哪个牌堆"必须对上：
    /// 两处各写一份的话，改了一处会让"挑出来的牌在 A 堆、放进去的却是 B 堆"。
    /// </summary>
    private static string PileTypeName(PileKind pile) => CardPickCollector.PileTypeName(pile);

    /// <summary>
    /// 状态模型 id 对应的类型引用（施加 / 移除状态要用），优先用本工程里的状态。
    ///
    /// 与 <see cref="CardModelTypeRef"/> 同一个理由：本工程定义的状态住本项目命名空间、
    /// 原版的住游戏命名空间，只有生成器知道工程。行为文件不必认识工程。
    /// </summary>
    internal string PowerModelTypeRef(string powerId) => PowerRefs.TypeRef(_project, powerId);

    /// <summary>附魔模型 id 对应的 C# 类型引用（附魔要用），优先用本工程里的附魔。</summary>
    internal string EnchantmentTypeRef(string enchantmentId) =>
        EnchantmentRefs.TypeRef(_project, enchantmentId);

    /// <summary>卡牌模型 id 对应的类型引用，优先用本工程里的卡。</summary>
    internal string CardModelTypeRef(string cardId)
    {
        string entry = cardId.Contains('.') ? cardId[(cardId.LastIndexOf('.') + 1)..] : cardId;

        CardDef? local = _project.Cards.FirstOrDefault(c =>
            ModelIds.Slugify(_project.CardTypeName(c)) == entry ||
            _project.CardTypeName(c).Equals(Naming.ToPascal(entry), StringComparison.OrdinalIgnoreCase));

        return local is not null
            ? $"Cards.{_project.CardTypeName(local)}"
            : $"MegaCrit.Sts2.Core.Models.Cards.{Naming.ToPascal(entry)}";
    }
}
