using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Models;

namespace ModForge.Generated.Runtime;

/// <summary>
/// 一张牌能被**永久增加重放次数**的意思。
///
/// 与"永久伤害增益"（<see cref="IPermanentDamageBoost"/>）同一条路：游戏的存档格式只存
/// 「卡牌 id + 升级等级 + **该类自己声明的 SavedProperty** + 附魔」，动态数值不进存档，
/// 读档时按标准数值重建。所以"永久多打几次"有两条路：记在**那张牌自己的类**上
/// （本 mod 生成的牌都走这条），或者挂**组件**（原版牌走这条，见 <see cref="CardComponentStore"/>）。
///
/// 本 mod 生成的每张牌都会实现它，于是：
///
///   - 本 mod 的牌之间可以永久互相加重放，且不占任何"槽"；
///   - 原版卡牌由 <see cref="ReplayBoost.Apply"/> 自动落到组件那条路上。
/// </summary>
public interface IAddReplay
{
    /// <summary>
    /// 把这张牌的重放次数永久加上 <paramref name="extra"/> 次，并同步到牌组里的那张牌
    /// （战斗中的是临时副本，只改它的话出了战斗就丢了）。
    /// </summary>
    void ApplyPermanentReplay(int extra);
}

/// <summary>
/// 生成卡牌的永久重放实现。每个实现类自己带着 <c>[SavedProperty]</c> 字段，
/// 所以读档后次数还在——存档回填走的是那个字段的 setter，setter 里把值写进
/// <c>BaseReplayCount</c>。
/// </summary>
public static class ReplayBoost
{
    /// <summary>
    /// 按**方法名**找"永久加重放"能力。找到一次就缓存，之后是普通委托调用。
    /// 理由与 <see cref="PermanentDamageBoost.Apply"/> 完全相同：接口是每个 mod 各一份的
    /// （卡牌类要在编译期实现它），所以跨 mod 只能按方法名认。
    /// </summary>
    private static readonly Dictionary<Type, Action<CardModel, int>?> Appliers = new();

    /// <summary>
    /// 给一张牌永久加重放，并同步它的牌组原件。
    ///
    /// 三条路，按牌的种类自动落位（与 <see cref="PermanentDamageBoost.Apply"/> 同一形状）：
    ///   本 mod 的牌     —— 接口快路径，写它自己的 [SavedProperty] 字段。
    ///   其它 mod 的牌   —— 按方法名认（见 Appliers 的说明）。
    ///   原版牌          —— 类改不了，改挂**组件**（见 <see cref="CardComponentStore"/>）：
    ///                      数值进组件仓库、随存档往返，进战斗时由
    ///                      <see cref="ComponentHookHost"/> 应用到战斗副本。
    ///
    /// 调用方（生成出来的效果代码）不需要自己判断走哪条。
    /// </summary>
    public static void Apply(CardModel card, int extra)
    {
        if (card is null)
        {
            return;
        }

        // 同 mod 的快路径：直接按接口调，不走反射。
        if (card is IAddReplay replayable)
        {
            replayable.ApplyPermanentReplay(extra);
            return;
        }

        // 跨 mod：按方法名认（见 Appliers 的说明）。
        Type type = card.GetType();

        if (!Appliers.TryGetValue(type, out Action<CardModel, int>? apply))
        {
            MethodInfo? method = type.GetMethod(
                nameof(IAddReplay.ApplyPermanentReplay),
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(int) },
                modifiers: null);

            apply = method is null
                ? null
                : (target, amount) => method.Invoke(target, new object[] { amount });

            Appliers[type] = apply;
        }

        if (apply is not null)
        {
            apply(card, extra);
            return;
        }

        // 原版牌：挂组件（数值进仓库随存档走），战斗效果立即体现。
        CardComponentStore.Add(card, 0, extra);
        CardComponentStore.ApplyToCombatCard(card, new CardComponents { ReplayBoost = extra });
    }
}
