using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;

namespace ModForge.Core.Modules.Variables;

/// <summary>
/// 变量模块：把效果上的三件套（<see cref="NumericValue"/>）规划成卡上的**变量槽**
/// （<see cref="VarSlot"/>），并给出"状态层数"这类状态变量的占位符名。
///
/// 它是**唯一**的规划入口。规划结果被五处读，而且必须一致：
///   声明（生成 <c>CanonicalVars</c>）、取值、升级、等级表、描述占位符。
/// 各写一份必然分叉，而且是静默分叉——描述里引用的变量名与卡上实际声明的不同时，
/// 游戏内格式化才会失败，平时看不出来。
///
/// 本模块**不做数据处理**：变量怎么读（手牌数、状态层数、战斗历史计数…）是变量来源模块
/// （<c>Modules/ValueSources/SourceEmitter</c>）的职责，这里只决定"这个数值由哪三部分构成、
/// 占哪个槽、槽里那几种文本怎么写"。
/// </summary>
public static class VariableModule
{
    /// <summary>
    /// 给每条带数值的效果分配变量槽。
    ///
    /// 会占槽的种类：伤害、格挡、召唤、铸造、最大生命（提高/降低各自一条）、伤害增益、
    /// 抽牌、消耗牌数、附魔层数、施加状态（按状态各一组）。
    /// 其它种类（回能、回复生命…）的数值在生成时是字面量，不占槽——它们没有可引用的变量，
    /// 所以也**不该**出现在描述占位符里（否则描述里写了 {X} 而卡上没有那个变量，
    /// 游戏内会格式化失败）。
    /// </summary>
    public static IReadOnlyList<VarSlot> Slots(ModProject project, IReadOnlyList<EffectDef> resolved)
    {
        var result = new List<VarSlot>();

        // 固定值与计算型各数各的：名字本来就在两个命名空间里（Damage 与 CalculatedDamage）。
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);

        // 计算型的基础值：第一个沿用原版的共用 CalculationBase（只有一条计算型的卡与原版形状
        // 完全一致，老的卡与描述都不受影响），第二个起各占一个（CalculationBase2、3…）。
        //
        // 编号是**全局**的，不按种类分开：原版把伤害与格挡的基础值挤在同一个 CalculationBase 里，
        // 那张牌上两者就互相拖累（改一个另一个跟着变）。既然目标是"每条效果独立"，
        // 两条计算型就该各占一个——原版 578 张里没有任何一张同时有计算型伤害与计算型格挡，
        // 所以这个编号方式不会与任何原版内容冲突。
        int calculatedOrdinal = 0;

        foreach (EffectDef effect in resolved)
        {
            string? baseName = BaseName(project, effect);

            if (baseName is null)
            {
                continue;
            }

            bool calculated = effect.IsCalculated;

            string key = baseName + (calculated ? ":calc" : ":fixed");
            int ordinal = counters.TryGetValue(key, out int seen) ? seen + 1 : 1;
            counters[key] = ordinal;

            if (!calculated)
            {
                string name = ordinal == 1 ? baseName : $"{baseName}{ordinal}";

                result.Add(new VarSlot(
                    effect,
                    ValueRole.Value,
                    name,
                    BaseVar: null,
                    ExtraVar: null,
                    baseName,
                    ordinal,
                    ShapeOf(effect, baseName),
                    VarStorage.GameVar,
                    PowerTypeRefOf(project, effect)));

                continue;
            }

            // 计算型的伤害与格挡：各要"基础值 + 变量系数 + 计算值"三个变量。
            // 计算值与变量系数按**种类**编号（ExtraDamage 与 CalculationExtra 本来就是两个不同的
            // 原版名，各自从 1 开始），基础值按**全局**编号（见上）。
            bool isDamage = effect.Kind == EffectKind.DealDamage;

            calculatedOrdinal++;

            string calcName = ordinal == 1 ? baseName : $"{baseName}{ordinal}";

            string baseVar = calculatedOrdinal == 1
                ? "CalculationBase"
                : $"CalculationBase{calculatedOrdinal}";

            string extraBase = isDamage ? "ExtraDamage" : "CalculationExtra";
            string extraVar = ordinal == 1 ? extraBase : $"{extraBase}{ordinal}";

            result.Add(new VarSlot(
                effect,
                ValueRole.Value,
                calcName,
                baseVar,
                extraVar,
                baseName,
                ordinal,
                VarShape.Plain,
                VarStorage.GameVar));
        }

        return result;
    }

    /// <summary>
    /// 给"游戏没有对应计算变量"的那些计算型效果造一个槽——施加状态、抽牌数、铸造…
    ///
    /// 它们**不声明变量**（所以不出现在 <see cref="Slots"/> 里，描述里也没有占位符），
    /// 数值存在生成类的私有字段（<c>_calcBase{n}</c> / <c>_calcPerUnit{n}</c>）里，
    /// 由我们自己发的取值方法读出来。但升级与等级表仍要写那两个字段，
    /// 所以它们同样需要一个槽来给出写法——就是这里造的这个。
    ///
    /// 这就是"效果可以**传入参数**引用模块"的那条路：不挂在 <c>EffectDef</c> 上、
    /// 或者压根没有声明变量的数值，也能拿到同一套文本。
    /// </summary>
    public static VarSlot FieldSlot(EffectDef effect, ValueRole role) => new(
        effect,
        role,
        Name: string.Empty,
        BaseVar: null,
        ExtraVar: null,
        BaseName: string.Empty,
        Ordinal: 1,
        VarShape.Plain,
        VarStorage.Field);

    /// <summary>
    /// 给"能升级的重复"分配重复次数槽（Repeat / Repeat2 / …）。
    ///
    /// 与数值槽同一个道理：两条各占一个，否则升级其中一条会让另一条一起涨。
    /// 只有**能升级**的重复才需要变量——不能升级的重复在循环里直接写字面量就够了。
    /// 次数是计算型的不在此列：它的次数由算式的两个字段算出，没有 RepeatVar 可改。
    /// </summary>
    public static IReadOnlyList<VarSlot> RepeatSlots(
        ModProject project,
        IReadOnlyList<EffectDef> resolved)
    {
        var result = new List<VarSlot>();
        int ordinal = 0;

        foreach (EffectDef effect in resolved)
        {
            if (effect.Upgrade.RepeatDelta == 0 || effect.Repeat <= 0 || effect.CalculatedRepeat)
            {
                continue;
            }

            ordinal++;

            result.Add(new VarSlot(
                effect,
                ValueRole.Repeat,
                ordinal == 1 ? "Repeat" : $"Repeat{ordinal}",
                BaseVar: null,
                ExtraVar: null,
                BaseName: "Repeat",
                ordinal,
                VarShape.Repeat,
                VarStorage.GameVar));
        }

        return result;
    }

    /// <summary>
    /// 给"筛费用"的效果分配**费用阈值槽**（<c>EnergyAmount</c> / <c>StarAmount</c> / 带序号的）。
    ///
    /// 两个费用维度各占一个，启用哪一维就有哪一个。它承载的是那条比较式里的**基础值**
    /// （"能量消耗大于 2"里的 2）：
    ///
    ///   * 声明成卡上的变量 → 描述里能写 <c>{EnergyAmount:diff()}</c>（升级后卡面跟着变，
    ///     也有"升了多少"的绿字），与数值、次数那两套完全同构；
    ///   * 升级改阈值（<c>&lt;upgrade&gt;</c> 里同名的 <c>energyAmount</c> 块）与等级表逐级
    ///     赋值都改这一个变量；
    ///   * 计算型阈值（"能量消耗大于 手牌数"）也用它当算式里那个基础值，
    ///     来源算出来的部分在打出时现算。
    ///
    /// 与 <see cref="Slots"/> 分开是因为它**不属于效果的数值**：一条效果可以同时在筛两个费用，
    /// 而 <see cref="EffectDef.NumericOf"/> 那种"一条效果一个数值"的读法表达不了。
    /// 名字与描述侧的 <c>PickerWording.ThresholdName</c> 是同一套（描述那边引用它）。
    /// </summary>
    public static IReadOnlyList<VarSlot> ThresholdSlots(ModProject project, IReadOnlyList<EffectDef> resolved)
    {
        var result = new List<VarSlot>();

        // 两个维度的编号各自数（EnergyAmount / EnergyAmount2），与其它槽同一个规矩：
        // 同类里第几个就是几，描述里引用的名字与卡上声明的名字必须同源。
        var counters = new Dictionary<ValueRole, int>();

        foreach (EffectDef effect in resolved)
        {
            foreach (ValueRole role in CostRoles.All)
            {
                // "是 X"那一档不比数字，所以不要变量。
                if (!effect.Pick.UsesThreshold(role))
                {
                    continue;
                }

                int ordinal = counters.TryGetValue(role, out int seen) ? seen + 1 : 1;
                counters[role] = ordinal;

                string baseName = role == ValueRole.Energy ? "EnergyAmount" : "StarAmount";
                string name = ordinal == 1 ? baseName : $"{baseName}{ordinal}";

                result.Add(new VarSlot(
                    effect,
                    role,
                    name,

                    // 基础值与变量系数各占一个变量（见 VarSlot.HasSeparateCoefficient）：
                    // 阈值是"基础值 + 来源 × 变量系数"，两个数都要能升级、能被等级表逐级覆盖。
                    // 系数变量的名字跟着基础值走（EnergyAmount / EnergyAmountExtra），
                    // 一眼能看出它俩是一对。
                    BaseVar: null,
                    ExtraVar: $"{name}Extra",
                    baseName,
                    ordinal,
                    VarShape.Plain,
                    VarStorage.GameVar));
            }
        }

        return result;
    }

    /// <summary>
    /// 状态在生成代码里的变量名/占位符。
    ///
    /// 用生成的状态**类型名**，因为游戏的 <c>PowerVar</c> 就是按类型名注册的，
    /// 描述里的 <c>{StrengthPower:diff()}</c> 也按同一个名字认它。
    /// </summary>
    public static string PowerPlaceholder(ModProject project, string powerId)
    {
        string entry = powerId.Contains('.')
            ? powerId[(powerId.LastIndexOf('.') + 1)..]
            : powerId;

        PowerDef? local = project.Powers.FirstOrDefault(p =>
            Naming.ToEntry(p.Id) == entry ||
            Naming.ToEntry(p.Id) + "_POWER" == entry ||
            project.PowerTypeName(p).Equals(entry, StringComparison.OrdinalIgnoreCase));

        return local is not null
            ? project.PowerTypeName(local)
            : Naming.ToPascal(entry);
    }

    /// <summary>
    /// 一条效果的槽基名；不占槽的种类返回 null。
    ///
    /// 这是"哪些效果有变量"的**唯一**名单——声明、取值、升级、描述、校验都从这里出发。
    /// 另写一份名单的话，加一种数值只改一处也不报错，要等到游戏内格式化失败才发现。
    /// </summary>
    private static string? BaseName(ModProject project, EffectDef effect)
    {
        // 计算型的伤害与格挡用原版的"计算值"名字（CalculatedDamage / CalculatedBlock），
        // 固定值才用 Damage / Block。
        if (effect.IsCalculated)
        {
            return effect.Kind switch
            {
                EffectKind.DealDamage => "CalculatedDamage",
                EffectKind.GainBlock => "CalculatedBlock",
                _ => null,
            };
        }

        return effect.Kind switch
        {
            EffectKind.DealDamage => "Damage",
            EffectKind.GainBlock => "Block",
            EffectKind.Summon => "Summon",

            // 铸造：原版"淬锋"声明的 ForgeVar 默认名就是 "Forge"。
            EffectKind.Forge => "Forge",

            // 提高与降低各占一个槽，不再互相顶替。
            EffectKind.GainMaxHp => "MaxHp",
            EffectKind.LoseMaxHp => "MaxHpLost",

            EffectKind.BoostCardDamage => "Increase",

            // 抽牌与消耗牌数也要声明变量：描述里的 {Cards:diff()} 只有在卡上真有这个变量时
            // 才能格式化出来，而且声明之后它们的升级也才生效。
            EffectKind.DrawCards => "Cards",

            // 附魔层数：Enchant 的第二个参数是**层数**（不是"附几张牌"——那是选牌器的事）。
            // 它与其他数值一样要能升级、能算出来，所以占一个变量槽。
            EffectKind.EnchantCardsInPile => "Enchant",

            // ---- 下面这些以前**不占槽**，后果是"填了升级没反应" ----
            //
            // 它们都有数值（schema 里 amount: true），但这里没给基名，于是：
            //   * 生成时不声明变量，打出时把数值写成字面量；
            //   * 升级那段只对 `slot is not null` 的效果发语句 → **升级增量被静默丢弃**；
            //   * 描述里写死数字，不随升级变、也没有占位符按钮。
            //
            // 前四个游戏有专门的变量类型（见 ShapeOf）；其余游戏没有，用带名字的普通 DynamicVar。
            EffectKind.GainEnergy => "Energy",
            EffectKind.GainStars => "Stars",
            EffectKind.Heal => "Heal",
            EffectKind.LoseHp => "HpLoss",

            EffectKind.ChannelOrb => "Orbs",
            EffectKind.EvokeOrbs => "Evoke",
            EffectKind.AddOrbSlots => "OrbSlots",
            EffectKind.CopyCardsToPile => "Copies",
            EffectKind.AddNamedCardToPile => "AddCards",

            // 消耗牌的张数**就是它的数值**（"每个效果的 base 值即为该效果的基础值"），
            // 所以它占一个变量槽：固定值时生成器读它、升级改张数也走它的增量、
            // 描述里能引用 {ExhaustCount:diff()}。
            EffectKind.ExhaustCards => "ExhaustCount",

            EffectKind.AddReplay => "Replay",
            EffectKind.AutoPlay => "AutoPlayCount",

            // 费用增减量（有符号：降费为负、加费为正）。
            EffectKind.ModifyCardCost => "CostDelta",

            EffectKind.ApplyPower when !string.IsNullOrWhiteSpace(effect.PowerId) =>
                PowerPlaceholder(project, effect.PowerId),

            // "部分移除"的数值就是**减几层**，与其它 base 值同一个读法（"每个效果的 base 值
            // 即为该效果的基础值"）：所以它占一个槽，升级改层数走它的增量、描述能引用
            // {RemovePower:diff()}。
            //
            // 基名不能借用状态类型名（ApplyPower 那边用的）：同一张牌上"加 3 层力量、
            // 再减 1 层力量"是两条效果，借用就会挤进同一个槽、互相改到对方的数。
            // "整个移除"时数值不参与，所以不占槽——不占槽的数值在描述里没有占位符，
            // 那正是我们要的（那句话里本来就没有数字）。
            EffectKind.RemovePower when effect.Removal == PowerRemoval.Partial => "RemovePower",

            _ => null,
        };
    }

    /// <summary>固定值该用哪个游戏变量类型声明。</summary>
    private static VarShape ShapeOf(EffectDef effect, string baseName)
    {
        // 状态要先判：它的基名是状态类型名（任意），不能拿去做下面的字面量匹配。
        if (effect.Kind == EffectKind.ApplyPower && !string.IsNullOrWhiteSpace(effect.PowerId))
        {
            return VarShape.Power;
        }

        return baseName switch
        {
            "Damage" => VarShape.Damage,
            "Block" => VarShape.Block,
            "Summon" => VarShape.Summon,
            "MaxHp" => VarShape.MaxHp,
            "Cards" => VarShape.Cards,
            "Forge" => VarShape.Forge,

            // 这四个游戏有专门的变量类型，用它们（卡面悬停说明与描述都按类型名认变量）。
            "Energy" => VarShape.Energy,
            "Stars" => VarShape.Stars,
            "Heal" => VarShape.Heal,
            "HpLoss" => VarShape.HpLoss,

            // 其余（降低最大生命、伤害增益、消耗牌数、附魔层数、球类、复制/加入/丢弃牌数、
            // 重放、自动打出、费用增减）：游戏没有专门的变量类型。
            _ => VarShape.Plain,
        };
    }

    /// <summary>状态槽要哪个类型引用（<c>PowerVar&lt;T&gt;</c> 的那个 T）。</summary>
    private static string? PowerTypeRefOf(ModProject project, EffectDef effect) =>
        effect.Kind == EffectKind.ApplyPower && !string.IsNullOrWhiteSpace(effect.PowerId)
            ? PowerRefs.TypeRef(project, effect.PowerId!)
            : null;
}
