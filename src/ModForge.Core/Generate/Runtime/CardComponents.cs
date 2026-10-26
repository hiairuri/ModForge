using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace ModForge.Generated.Runtime;

/// <summary>
/// 一张**原版卡牌**身上挂的"永久组件"数值。
///
/// 本 mod 生成的牌不走这里——它们把永久增伤/重放写进**自己类上的 [SavedProperty] 字段**
/// （见 <see cref="IPermanentDamageBoost"/> / <see cref="IAddReplay"/>），那是游戏的官方持久化
/// 通道。原版卡的类改不了，所以改走"组件"：数值记在这个仓库里，存档时借道
/// <c>SerializableCard.Props</c>（游戏存档里每张卡自带的 props 块，联机同步也走它）往返。
/// </summary>
public sealed class CardComponents
{
    /// <summary>永久增加的伤害（各伤害槽都加，语义与战斗内的增伤一致）。</summary>
    public int DamageBoost;

    /// <summary>永久增加的重放次数（这张牌下次被打出时整张牌多执行这么多次）。</summary>
    public int ReplayBoost;

    /// <summary>两个组件都是 0 = 没有组件（props 里就不写条目，存档形状不变）。</summary>
    public bool IsEmpty => DamageBoost == 0 && ReplayBoost == 0;
}

/// <summary>
/// 组件的**仓库与两个存档挂点**：写入存档形状 / 从存档形状读回。
///
/// **仓库**按卡实例存（<see cref="ConditionalWeakTable"/>：卡被回收组件跟着消失，不会卡住内存）。
/// 组件值在语义上属于**牌组原件**（存档从它走），战斗副本是临时镜像。
///
/// **多 mod**：条目名带 Mf 前缀；每个 mod 的补丁只读写自己前缀的条目，同一张卡被两个
/// mod 各挂一种组件时 props 里两组条目并存，互不干扰。
/// </summary>
public static class CardComponentStore
{
    /// <summary>存档条目名（props.ints 桶里的 name；前缀隔离其它 mod 的条目）。</summary>
    public const string DamageEntry = "MfDamageComponent";

    /// <summary>存档条目名（重放）。</summary>
    public const string ReplayEntry = "MfReplayComponent";

    private static readonly ConditionalWeakTable<CardModel, CardComponents> Store = new();

    /// <summary>这张卡的组件（没有就是 null——大多数卡都没有）。</summary>
    public static CardComponents? Find(CardModel? card) =>
        card is not null && Store.TryGetValue(card, out CardComponents? components)
            ? components
            : null;

    /// <summary>拿这张卡的组件（没有就建一份空的）。</summary>
    public static CardComponents For(CardModel card) => Store.GetValue(card, _ => new CardComponents());

    /// <summary>
    /// 给一张卡累加组件值，并**同步它的牌组原件**（战斗中的是临时副本，存档从原件走）。
    ///
    /// 返回这张卡自己身上的组件。战斗效果（伤害槽/重放）**不由这里应用**——战斗开始时
    /// 会按组件统一应用一次（见 <see cref="ComponentHookHost"/>），战斗中施加的 delta
    /// 由调用方立即体现，两条路各管一半，不会重复。
    /// </summary>
    public static CardComponents Add(CardModel card, int damage, int replay)
    {
        CardComponents components = For(card);
        components.DamageBoost += damage;
        components.ReplayBoost += replay;

        // 牌组原件与战斗副本各记一份 delta，保持"原件与副本的组件一致"。
        // 原件不在战斗里，战斗效果只落在传入的那张卡上（由调用方做）。
        if (card.DeckVersion is { } deckVersion && !ReferenceEquals(deckVersion, card))
        {
            CardComponents deckComponents = For(deckVersion);
            deckComponents.DamageBoost += damage;
            deckComponents.ReplayBoost += replay;
        }

        return components;
    }

    /// <summary>
    /// 把组件的**战斗效果**应用到一张战斗中的卡（伤害槽加值 + 重放次数加值）。
    /// 没有伤害槽的牌伤害那部分自然落空（与战斗内增伤同一套运行时辅助）。
    /// </summary>
    public static void ApplyToCombatCard(CardModel card, CardComponents components)
    {
        if (card is null || components is null)
        {
            return;
        }

        if (components.DamageBoost != 0)
        {
            DamageSlots.AddToAll(card, components.DamageBoost);
        }

        if (components.ReplayBoost != 0)
        {
            card.BaseReplayCount += components.ReplayBoost;
        }
    }

    /// <summary>
    /// 把组件写进一张卡的存档形状（<see cref="SerializableCard.Props"/>）。
    /// **没有组件时不碰 props**——大多数卡（包括全部未挂组件的原版卡）的存档形状
    /// 一个字节都不变。
    /// </summary>
    public static void WriteToSerializable(CardModel card, SerializableCard save)
    {
        CardComponents? components = Find(card);

        if (components is null || components.IsEmpty)
        {
            return;
        }

        save.Props ??= new SavedProperties();
        save.Props.ints ??= new List<SavedProperties.SavedProperty<int>>();

        // 先清掉自己的旧条目再写：组件值被减回 0 时不能留下陈旧数字。
        save.Props.ints.RemoveAll(p => p.name is DamageEntry or ReplayEntry);

        if (components.DamageBoost != 0)
        {
            save.Props.ints.Add(new SavedProperties.SavedProperty<int>(DamageEntry, components.DamageBoost));
        }

        if (components.ReplayBoost != 0)
        {
            save.Props.ints.Add(new SavedProperties.SavedProperty<int>(ReplayEntry, components.ReplayBoost));
        }
    }

    /// <summary>
    /// 从存档形状把组件读回来，挂到**重建出来的那张卡**上。
    /// props 里没有自己的条目（大多数卡、以及其它 mod 的卡）时返回 false，什么都不做。
    /// </summary>
    public static bool ReadFromSerializable(CardModel card, SerializableCard save)
    {
        List<SavedProperties.SavedProperty<int>>? ints = save.Props?.ints;

        if (ints is null)
        {
            return false;
        }

        int damage = 0;
        int replay = 0;

        foreach (SavedProperties.SavedProperty<int> entry in ints)
        {
            if (entry.name == DamageEntry)
            {
                damage = entry.value;
            }
            else if (entry.name == ReplayEntry)
            {
                replay = entry.value;
            }
        }

        if (damage == 0 && replay == 0)
        {
            return false;
        }

        CardComponents components = For(card);
        components.DamageBoost = damage;
        components.ReplayBoost = replay;
        return true;
    }
}

/// <summary>
/// 组件的**进战斗应用**：战斗开始时把牌组原件上的组件体现到战斗副本上。
///
/// 进战斗时游戏把每张牌组卡 <c>CloneCard</c> 成战斗卡放进抽牌堆、并让战斗卡的
/// <see cref="CardModel.DeckVersion"/> 指回原件（Player.PopulateCombatState），然后才派发
/// <c>BeforeCombatStart</c>——所以战斗开始时牌堆已装填完毕，这里遍历每名玩家的全部战斗牌堆，
/// 战斗卡从它的原件那里拿组件，把战斗效果应用上去。每张战斗卡只被应用一次（它的原件只算一份）；
/// 战斗中效果再施加的 delta 由施加处立即体现，不经过这里。
///
/// 这个 host 通过 <c>ModHelper.SubscribeForCombatStateHooks</c> 注册（见 ModEntry）：游戏每次
/// 派发战斗钩子都会调注册的回调拿要迭代的模型，回调顺手把**当前战斗**记到 host 上
/// （<see cref="AbstractModel.BeforeCombatStart"/> 本身不带参数），再返回 host 自己。
/// 它不是卡、不是状态，永远不出现在任何牌堆里。
/// </summary>
public sealed class ComponentHookHost : AbstractModel
{
    /// <summary>整个 mod 共用一个实例（注册时由 ModEntry 交给游戏）。</summary>
    public static readonly ComponentHookHost Instance = new();

    private CombatState? _combat;

    private ComponentHookHost()
    {
    }

    /// <summary>
    /// 这个 host 存在的唯一目的就是收战斗钩子——不实现它的话，
    /// 钩子派发时它根本不会出现在监听者列表里。
    /// </summary>
    public override bool ShouldReceiveCombatHooks => true;

    /// <summary>注册回调里记下"这轮钩子属于哪场战斗"。</summary>
    public void RememberCombat(CombatState combat) => _combat = combat;

    /// <inheritdoc />
    public override Task BeforeCombatStart()
    {
        if (_combat is not { } combat)
        {
            return Task.CompletedTask;
        }

        foreach (Player player in combat.Players)
        {
            ApplyToPlayer(player);
        }

        return Task.CompletedTask;
    }

    private static void ApplyToPlayer(Player player)
    {
        if (player.PlayerCombatState is not { } combat)
        {
            return;
        }

        ApplyToPile(combat.DrawPile);
        ApplyToPile(combat.Hand);
        ApplyToPile(combat.DiscardPile);
        ApplyToPile(combat.ExhaustPile);
        ApplyToPile(combat.PlayPile);
    }

    private static void ApplyToPile(CardPile pile)
    {
        foreach (CardModel card in pile.Cards)
        {
            CardComponents? components = CardComponentStore.Find(card.DeckVersion ?? card);

            if (components is not null)
            {
                CardComponentStore.ApplyToCombatCard(card, components);
            }
        }
    }
}
