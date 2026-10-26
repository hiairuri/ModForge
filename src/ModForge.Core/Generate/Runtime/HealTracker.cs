using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;

namespace ModForge.Generated.Runtime;

/// <summary>
/// 治疗的全局计数器：本 mod 自建的"恢复生命"数据源。
///
/// 为什么需要它：游戏的战斗历史里**没有治疗条目**（<c>CreatureCmd.Heal</c> 只写运行级统计，
/// 不写 <c>CombatHistory</c>），也没有战斗内治疗钩子——卡牌无从查询"治疗过几次 / 恢复了多少"。
/// 所以这里用 Harmony 观察每一次治疗，自己把账记下来。
///
/// 记账格式模仿原版战斗历史的思路：
///
///   - 每条记录带**当时的回合数**（<c>PlayerCombatState.TurnNumber</c>，原版
///     <c>HappenedThisTurn</c> 用的就是这套回合戳），"本回合"的查询就是数回合戳等于
///     当前回合数的记录——回合推进后旧记录自然出局，不需要显式清零；
///   - 每条记录带**当时的战斗状态引用**，查询时发现不是当前战斗就整体丢弃——
///     "本场战斗"的边界由此划定，战斗结束后账本自动作废；
///   - 按生物分别记账，查询时传自己的生物。
///
/// 只统计**实际恢复**的部分：治疗前已满血（或治疗量超过缺口）时，超出缺口的部分
/// 不改变生命，不计入次数也不计入总量。
///
/// **多个 mod 同时装时只有一份账**（见 <see cref="SharedRuntime"/>）：先加载的那个当宿主、
/// 负责记账；后加载的**不记账**，查询转给宿主。所以：
///
///   - <c>Postfix</c>（记账）只在宿主里做——否则每个 mod 各写一份自己的账；
///   - 四个查询方法在非宿主时转发给宿主那份实现（首次查询取一次委托，之后是普通调用）。
///
/// 于是无论装几个 mod，读到的都是同一份数字，实现与补丁也只有一份。
/// 补丁本身仍然打到每个 mod 上（生成物统一用 PatchAll），非宿主只是"什么也不做"——
/// 这比"挑着打补丁"简单得多，也不会影响同一个程序集里别的补丁（皮肤、自检）。
/// </summary>
[HarmonyPatch(typeof(Creature), nameof(Creature.HealInternal))]
public static class HealTracker
{
    /// <summary>Prefix 抓到的"治疗前的缺口"，Postfix 用它算实际恢复量。</summary>
    private static decimal _missingBefore;

    private sealed record HealRecord(ICombatState Combat, int Turn, decimal Amount);

    private static readonly Dictionary<Creature, List<HealRecord>> Records = new();

    // 转发给宿主时用；首次查询时取一次，之后是普通委托调用。
    private static Func<Creature, int>? _healsThisTurn;
    private static Func<Creature, int>? _healsThisCombat;
    private static Func<Creature, int>? _healAmountThisTurn;
    private static Func<Creature, int>? _healAmountThisCombat;

    /// <summary>本 mod 生成的卡牌查询：本回合恢复了生命的次数。</summary>
    public static int HealsThisTurn(Creature creature) => Route(
        nameof(HealsThisTurn), ref _healsThisTurn, creature, () => Sum(creature, r => 1, thisTurn: true));

    /// <summary>本 mod 生成的卡牌查询：本场战斗恢复了生命的次数。</summary>
    public static int HealsThisCombat(Creature creature) => Route(
        nameof(HealsThisCombat), ref _healsThisCombat, creature, () => Sum(creature, r => 1, thisTurn: false));

    /// <summary>本 mod 生成的卡牌查询：本回合恢复的生命总量。</summary>
    public static int HealAmountThisTurn(Creature creature) => Route(
        nameof(HealAmountThisTurn), ref _healAmountThisTurn, creature, () => Sum(creature, r => r.Amount, thisTurn: true));

    /// <summary>本 mod 生成的卡牌查询：本场战斗恢复的生命总量。</summary>
    public static int HealAmountThisCombat(Creature creature) => Route(
        nameof(HealAmountThisCombat), ref _healAmountThisCombat, creature, () => Sum(creature, r => r.Amount, thisTurn: false));

    /// <summary>
    /// 不是宿主时把查询转给宿主那份实现；是宿主（或没找到宿主）就自己算。
    /// 委托按**方法名**取——两边的类型全名相同，所以名字一定对得上。
    /// </summary>
    private static int Route(string method, ref Func<Creature, int>? cached, Creature creature, Func<int> local)
    {
        if (!SharedRuntime.IsHost && SharedRuntime.Host is Type host)
        {
            cached ??= (Func<Creature, int>)Delegate.CreateDelegate(
                typeof(Func<Creature, int>), host, method);

            return cached(creature);
        }

        return local();
    }

    /// <summary>
    /// 治疗落地前抓"缺口"（最大生命 - 当前生命）。实际恢复量不会超过它——
    /// 满血时缺口为 0，这次治疗就什么都没恢复。
    /// </summary>
    private static void Prefix(Creature __instance, decimal amount)
    {
        _missingBefore = Math.Max(__instance.MaxHp - __instance.CurrentHp, 0m);
    }

    /// <summary>治疗落地后记一笔：实际恢复量 &gt; 0 才记。</summary>
    private static void Postfix(Creature __instance, decimal amount)
    {
        // 不是宿主就不记账（查询那边会转发给宿主）。多个 mod 同时装时账只写一份。
        if (!SharedRuntime.IsHost)
        {
            return;
        }

        decimal actual = Math.Min(amount, _missingBefore);

        if (actual <= 0m)
        {
            return;
        }

        int turn = __instance.Player?.PlayerCombatState?.TurnNumber ?? -1;

        if (!Records.TryGetValue(__instance, out List<HealRecord>? list))
        {
            list = new List<HealRecord>();
            Records[__instance] = list;
        }

        // 战斗切换（上一场的旧账）在这里自然清掉：按引用比对战斗状态。
        list.RemoveAll(r => !ReferenceEquals(r.Combat, __instance.CombatState));
        list.Add(new HealRecord(__instance.CombatState!, turn, actual));
    }

    private static int Sum(Creature creature, Func<HealRecord, decimal> select, bool thisTurn)
    {
        if (creature is null || !Records.TryGetValue(creature, out List<HealRecord>? list))
        {
            return 0;
        }

        // 查询时 combat 对不上（新战斗/战斗已结束）就丢弃旧账。
        list.RemoveAll(r => !ReferenceEquals(r.Combat, creature.CombatState));

        if (list.Count == 0)
        {
            return 0;
        }

        int turn = creature.Player?.PlayerCombatState?.TurnNumber ?? -1;

        IEnumerable<HealRecord> scope = thisTurn
            ? list.Where(r => r.Turn == turn)
            : list;

        return (int)Math.Floor(scope.Sum(select));
    }
}
