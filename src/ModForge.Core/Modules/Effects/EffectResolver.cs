using ModForge.Core.Model;
using ModForge.Core.Modules.Variables;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 展开效果列表（把共享效果引用换成它指向的效果），并给出由变量模块的规划**派生**出来的查询：
/// 哪些数值要声明变量、每个占位符叫什么。
///
/// 放在这里而不是放在模型上，因为展开需要整个工程（共享效果表在工程里）。
///
/// 数值改成推导之后，一张卡的行为只有一个出处，不会出现两处数值对不上的情况。
///
/// 描述那一侧的两件事（占位符按钮清单、按效果生成一段描述）在**描述模块**里
/// （<c>Modules/Descriptions/DescriptionPlaceholders</c>）——它们读的是同一份规划。
/// </summary>
public static partial class EffectResolver
{
    /// <summary>
    /// 展开后的一条效果 + 它在**原始列表**里的编号（1 基）。
    /// 编号是临时牌集合的键要用的那一段，见 <see cref="ResolveNumbered"/>。
    /// </summary>
    public sealed record ResolvedEffect(EffectDef Effect, int Number);

    /// <summary>
    /// 把共享效果引用展开成它指向的效果，保持原有顺序。
    ///
    /// 引用失效时这里会跳过，由校验负责报告：生成过程仍应产出可编译的结果，
    /// 让作者能看到卡牌其余部分。
    /// </summary>
    public static IReadOnlyList<EffectDef> Resolve(ModProject project, IEnumerable<EffectDef> effects) =>
        ResolveNumbered(project, effects).Select(r => r.Effect).ToList();

    /// <summary>
    /// 与 <see cref="Resolve"/> 相同，但顺带给出每一条**对应到原始列表里的编号**
    /// （1 基——界面上"效果 N"那个号）。
    ///
    /// 临时牌集合的键里要这个编号（<c>{地址}.{效果编号}.{集合ID}</c>，见
    /// <c>Modules/CardSets</c>），而读集合的那条效果需要的是**定义集合那条**的编号：
    /// 编号必须在"按原始列表数"这个口径下算，与界面上看到的一致，否则用户按界面的
    /// "效果 3"填了 ID，生成出来的键却是另一个数。
    ///
    /// 失效的引用照样跳过（保持与 <see cref="Resolve"/> 一致），所以编号可能不连续——
    /// 那是对的：编号说的是"它在原列表里是第几条"，不是"它是第几个能用的"。
    /// </summary>
    public static IReadOnlyList<ResolvedEffect> ResolveNumbered(
        ModProject project,
        IEnumerable<EffectDef> effects)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(effects);

        var byId = new Dictionary<string, SharedEffectDef>(StringComparer.OrdinalIgnoreCase);
        foreach (SharedEffectDef shared in project.SharedEffects)
        {
            if (!string.IsNullOrWhiteSpace(shared.Id))
            {
                byId[shared.Id] = shared;
            }
        }

        var result = new List<ResolvedEffect>();
        int number = 0;

        foreach (EffectDef effect in effects)
        {
            number++;

            if (string.IsNullOrWhiteSpace(effect.SharedEffectId))
            {
                result.Add(new ResolvedEffect(effect, number));
                continue;
            }

            if (byId.TryGetValue(effect.SharedEffectId, out SharedEffectDef? named))
            {
                result.Add(new ResolvedEffect(Merge(named.Effect, effect), number));
            }
        }

        return result;
    }

    /// <summary>
    /// 把共享定义与引用它的那一行合并：行为以定义为准，引用行可以调整升级增量。
    ///
    /// 用**整体复制 + 只换升级增量**，而不是逐字段挑着抄。逐字段抄过一次教训：
    /// 每加一个新属性都要记得在这里补一行，漏掉的那一项就会静默失效（共享效果里
    /// 设了"作用条件"、引用它的卡上却没有——编译正常，只是行为不是设的那样）。
    /// 复制整个对象就没有这种"忘了补"的可能。
    ///
    /// 升级增量**逐角色相加**（见 <see cref="ValueRole"/>）：四个角色走同一个循环，
    /// 所以加第五个角色时这里不用改——这正是"按角色分组"要解决的那类问题。
    /// </summary>
    private static EffectDef Merge(EffectDef definition, EffectDef reference)
    {
        if (!reference.Upgrade.IsSet)
        {
            return definition;
        }

        EffectDef merged = definition.Clone();

        foreach (ValueRole role in EffectUpgrade.AllRoles)
        {
            merged.Upgrade.SetAmountDelta(
                role,
                definition.Upgrade.AmountDeltaOf(role) + reference.Upgrade.AmountDeltaOf(role));

            merged.Upgrade.SetPerUnitDelta(
                role,
                definition.Upgrade.PerUnitDeltaOf(role) + reference.Upgrade.PerUnitDeltaOf(role));

            // "升级后换用的变量"不是数，取引用行写的那个（没写就沿用定义里的）。
            merged.Upgrade.SetSource(
                role,
                reference.Upgrade.SourceOf(role) ?? definition.Upgrade.SourceOf(role));
        }

        return merged;
    }

    // 变量槽的规划在变量模块里（Modules/Variables/VariableModule）：每条效果占哪个槽、
    // 槽名叫什么、声明/取值/升级/等级赋值怎么写，都由它一处给出，声明、取值、升级、等级表、
    // 描述五处读同一份规划。各写一份必然分叉，而且是静默的——那正是这条设计要根除的问题。
    //
    // 本文件只保留**由规划派生出来**的查询（要声明哪些变量、每个变量此刻的值），
    // 以及效果列表的展开与分组。

    /// <summary>
    /// 卡牌是否需要计算变量。计算型效果一出现就必须声明对应变量，否则卡面显示不出数值。
    /// </summary>
    public static bool NeedsCalculatedDamage(IReadOnlyList<EffectDef> resolved) =>
        resolved.Any(e => e.Kind == EffectKind.DealDamage && e.IsCalculated);

    public static bool NeedsCalculatedBlock(IReadOnlyList<EffectDef> resolved) =>
        resolved.Any(e => e.Kind == EffectKind.GainBlock && e.IsCalculated);

    /// <summary>
    /// 描述可以引用的数值（槽名 → 值），每条效果各一项。界面预览按这张表替换占位符。
    ///
    /// 计算型的值不在表里：它们由计算变量算出，值随战斗状态变，没有固定数值可列。
    /// </summary>
    public static IReadOnlyDictionary<string, int> DynamicVars(
        ModProject project,
        IReadOnlyList<EffectDef> resolved)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (VarSlot slot in VariableModule.Slots(project, resolved))
        {
            if (!slot.Effect.IsCalculated && slot.Effect.Amount != 0)
            {
                result[slot.Name] = slot.Effect.Amount;
                continue;
            }

            // 计算型的**基础值**与**变量系数**是静态可知的（算式本身要等战斗才知道），
            // 而且它们各自有变量，描述里可以引用。给它们值，界面预览才不会把
            // {CalculationBase:diff()} 原样印在卡面上。
            if (slot.Effect.IsCalculated)
            {
                result[slot.BaseVar!] = slot.Effect.EffectiveBaseValue;
                result[slot.ExtraVar!] = (int)slot.Effect.Source.PerUnit;
            }
        }

        // 可升级的重复次数各占一个 RepeatVar，也一并给出。
        foreach (VarSlot slot in VariableModule.RepeatSlots(project, resolved))
        {
            result[slot.Name] = Math.Max(1, slot.Effect.Repeat);
        }

        return result;
    }

    /// <summary>
    /// 按**引用**比较效果，用于"效果 → 该用哪个变量"的查表。
    ///
    /// 不能按值比：同一张牌上可以有两条字段完全相同的效果（比如两条"获得 2 点力量"、
    /// 时机不同），按值比会让它们共用一个键，查表结果互相覆盖。
    /// 解析后的效果是同一批实例（<see cref="Resolve"/> 返回的对象一路传下来），
    /// 所以引用相等就是"同一条效果"。
    /// </summary>
    public sealed class EffectReferenceComparer : IEqualityComparer<EffectDef>
    {
        public static readonly EffectReferenceComparer Instance = new();

        public bool Equals(EffectDef? a, EffectDef? b) => ReferenceEquals(a, b);

        public int GetHashCode(EffectDef e) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(e);
    }

    /// <summary>
    /// 给"能升级的重复"分配重复次数变量（Repeat / Repeat2 / …）。
    ///
    /// 与数值槽同一个道理：两条各占一个，否则升级其中一条会让另一条一起涨。
    /// 只有**能升级**的重复才需要变量——不能升级的重复在循环里直接写字面量就够了。
    /// 次数是计算型的不在此列：它的次数由算式的两个字段算出，没有 RepeatVar 可改。
    ///
    /// 生成器与描述都调这里。两处各算一遍的话，名字一旦不同步，描述里引用的
    /// 占位符在卡上就不存在（游戏内格式化失败），而且只在"描述用过"时才现形。
    /// </summary>
    public static IReadOnlyList<VarSlot> RepeatSlots(
        ModProject project,
        IReadOnlyList<EffectDef> resolved) =>
        VariableModule.RepeatSlots(project, resolved);
}
