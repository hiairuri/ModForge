using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace ModForge.Generated.Runtime;

/// <summary>
/// 一张牌能被"永久增加伤害"的意思。
///
/// 为什么需要这个接口：游戏的存档格式是封闭的——它只存"卡牌 id + 升级等级 + **该类自己
/// 声明的 SavedProperty** + 附魔"，动态数值（DynamicVars）不进存档，读档时按标准数值重建。
/// 所以"永久改一张牌的伤害"有两条路：往**那张牌自己的类**上加一个 SavedProperty 字段
/// （本 mod 生成的牌都走这条），或者挂**组件**、数值随存档 props 往返
/// （原版牌走这条，见 <see cref="CardComponentStore"/>）。
///
/// 本 mod 生成的每张有伤害的牌都会实现它（见 CodeGenerator 发出的实现），于是：
///
///   - 本 mod 的牌之间可以永久互相增伤，且不占任何"槽"（与附魔互不相干）；
///   - 原版卡牌由 <see cref="PermanentDamageBoost.Apply"/> 自动落到组件那条路上。
/// </summary>
public interface IPermanentDamageBoost
{
    /// <summary>
    /// 把这张牌的伤害永久加上 <paramref name="extra"/> 点，并同步到牌组里的那张牌
    /// （战斗中的是临时副本，只改它的话出了战斗就丢了）。
    /// </summary>
    void ApplyPermanentDamageBoost(int extra);
}

/// <summary>
/// 生成卡牌的永久增伤实现。每个实现类自己带着 <c>[SavedProperty]</c> 字段，
/// 所以读档后增益还在——数值由 CanonicalVars 从那个字段读出来（原版"镰刀"TheScythe 同理）。
/// </summary>
public static class PermanentDamageBoost
{
    /// <summary>
    /// 按**方法名**找"永久增伤"能力。找到一次就缓存，之后是普通委托调用。
    ///
    /// 为什么不能只用 <c>card is IPermanentDamageBoost</c>：接口是**每个 mod 各有一份**的
    /// （它必须能被卡牌类在编译期实现，所以拿不到"共享一份"这条路）。两个由本编辑器生成的 mod
    /// 同时装时，"本 mod 的增伤效果"遇到"另一个 mod 的牌"，类型判定是 false——明明那张牌
    /// 有这个能力却认不出来。
    ///
    /// 所以这里额外按**方法名**认一次（鸭子类型）：不同 mod 的接口是不同的类型，但方法名相同，
    /// 于是跨 mod 也能生效。原版牌当然没有这个方法（我们改不了它们的类），自然落空。
    /// </summary>
    private static readonly Dictionary<Type, Action<CardModel, int>?> Appliers = new();

    /// <summary>
    /// 把增益写到一张牌上，并同步它的牌组原件。
    ///
    /// 三条路，按牌的种类自动落位：
    ///   本 mod 的牌     —— 接口快路径，写它自己的 [SavedProperty] 字段（游戏官方持久化）。
    ///   其它 mod 的牌   —— 按方法名认（见 Appliers 的说明），同样走它自己的字段。
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
        if (card is IPermanentDamageBoost boostable)
        {
            boostable.ApplyPermanentDamageBoost(extra);
            return;
        }

        // 跨 mod：按方法名认（见 Appliers 的说明）。
        Type type = card.GetType();

        if (!Appliers.TryGetValue(type, out Action<CardModel, int>? apply))
        {
            MethodInfo? method = type.GetMethod(
                nameof(IPermanentDamageBoost.ApplyPermanentDamageBoost),
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

        // 原版牌：挂组件（数值进仓库随存档走），战斗效果立即体现——
        // 否则这场战斗里"刚被增伤"的牌要等下一场战斗才变强。
        CardComponentStore.Add(card, extra, 0);
        CardComponentStore.ApplyToCombatCard(card, new CardComponents { DamageBoost = extra });
    }
}
