using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace ModForge.Generated.Runtime;

/// <summary>
/// 一张牌上**第二个及以后**的计算型数值。
///
/// 为什么需要它：游戏的 <c>CalculatedDamageVar</c> / <c>CalculatedBlockVar</c> 固定从
/// <c>CalculationBase</c> 与 <c>ExtraDamage</c>（格挡是 <c>CalculationExtra</c>）读数和——
/// 那几个名字全游戏各只有一个，所以第二条计算型数值会读到第一条的和。
///
/// 但"一张牌多个数值、各有各的名字"这套机制是游戏支持的：原版"流光"（Glitterstream）就声明了
/// 两个格挡变量（<c>Block</c> 与 <c>BlockNextTurn</c>），升级时两个各加 2——它的玩法是
/// "本回合格挡 + 下回合格挡的 buff"（第二个变量是那个 buff 的数值），借的是同一套变量机制。
/// 这个类把这套机制接到计算型数值上——读自己那两个变量（名字由生成器带序号），
/// 于是同一张牌上的第二条有自己的算式、自己的基础值/增量、自己的升级。
///
/// 覆写的是基类两个 <c>virtual</c> 方法（<c>GetBaseVar</c> / <c>GetExtraVar</c>），
/// 所以计算、卡面预览、升级重算（<c>RecalculateForUpgradeOrEnchant</c>）都自动跟着走。
/// </summary>
public sealed class SlotCalculatedVar : CalculatedVar
{
    private readonly string _baseVarName;
    private readonly string _extraVarName;
    private readonly bool _isDamage;

    /// <summary>攻击还是格挡——决定预览时要跑哪一类修正钩子（与原版两个 Var 一致）。</summary>
    public ValueProp Props { get; }

    /// <param name="name">这个计算变量自己的名字（如 CalculatedDamage2），描述里按它引用。</param>
    /// <param name="baseVarName">基础值变量名（如 CalculationBase2）。</param>
    /// <param name="extraVarName">变量系数对应的变量名（如 ExtraDamage2 / CalculationExtra2）。</param>
    /// <param name="props">伤害/格挡的 ValueProp。</param>
    /// <param name="isDamage">true 表示这是伤害值（预览走伤害钩子），false 为格挡。</param>
    public SlotCalculatedVar(
        string name,
        string baseVarName,
        string extraVarName,
        ValueProp props,
        bool isDamage = true)
        : base(name)
    {
        _baseVarName = baseVarName;
        _extraVarName = extraVarName;
        Props = props;
        _isDamage = isDamage;
    }

    /// <summary>从自己的基础值变量读数，而不是全牌共用的 CalculationBase。</summary>
    protected override DynamicVar GetBaseVar() => Owner().DynamicVars[_baseVarName];

    /// <summary>从自己的增量变量读数。</summary>
    protected override DynamicVar GetExtraVar() => Owner().DynamicVars[_extraVarName];

    /// <summary>
    /// 卡面预览。逐句照着原版两个 Var 写（<c>CalculatedDamageVar</c> / <c>CalculatedBlockVar</c>），
    /// 只把"从哪里读基础值"换成自己那个名字——附魔那一段最容易被漏掉：
    ///
    ///   1. 附魔牌的**基础值**会被附魔改写（加法 + 乘法），显示在 EnchantedValue 里
    ///      （玩家眼里附魔是"牌的一部分"，不与临时增益混色）；
    ///   2. 战斗中的卡（runGlobalHooks）走一遍修正钩子——附魔的加减也在 Hook 内部生效；
    ///   3. 战斗外（牌组浏览等）钩子不跑，但附魔仍要手动套一遍，否则显示的数值比实际小；
    ///   4. 最后统一钳到 0 以上（负数伤害没有意义）。
    ///
    /// 少任何一段，附魔牌的卡面与结算都会对不上——而且只在"带附魔"时现形，平时测不出来。
    /// </summary>
    public override void UpdateCardPreview(
        CardModel card,
        CardPreviewMode previewMode,
        Creature? target,
        bool runGlobalHooks)
    {
        EnchantmentModel? enchantment = card.Enchantment;

        if (enchantment is not null)
        {
            decimal baseValue = GetBaseVar().BaseValue;

            if (_isDamage)
            {
                baseValue += enchantment.EnchantDamageAdditive(baseValue, Props);
                baseValue *= enchantment.EnchantDamageMultiplicative(baseValue, Props);
            }
            else
            {
                baseValue += enchantment.EnchantBlockAdditive(baseValue);
                baseValue *= enchantment.EnchantBlockMultiplicative(baseValue);
            }

            if (card.IsEnchantmentPreview)
            {
                base.PreviewValue = baseValue;
            }
            else
            {
                base.EnchantedValue = baseValue;
            }
        }

        decimal value = Calculate(target);

        if (runGlobalHooks)
        {
            value = _isDamage
                ? Hook.ModifyDamage(
                    card.Owner.RunState,
                    card.CombatState,
                    target,
                    card.Owner.Creature,
                    value,
                    Props,
                    card,
                    ModifyDamageHookType.All,
                    previewMode,
                    out IEnumerable<AbstractModel> _)
                : Hook.ModifyBlock(
                    card.CombatState,
                    card.Owner.Creature,
                    value,
                    Props,
                    card,
                    null,
                    out IEnumerable<AbstractModel> _);
        }
        else if (!card.IsEnchantmentPreview && enchantment is not null)
        {
            if (_isDamage)
            {
                value += enchantment.EnchantDamageAdditive(value, Props);
                value *= enchantment.EnchantDamageMultiplicative(value, Props);
            }
            else
            {
                value += enchantment.EnchantBlockAdditive(value);
                value *= enchantment.EnchantBlockMultiplicative(value);
            }
        }

        base.PreviewValue = Math.Max(value, 0m);
    }

    /// <summary>基类把所有者存在受保护字段里；计算变量只会挂在卡牌上。</summary>
    private CardModel Owner() => (CardModel)_owner!;
}

/// <summary>
/// 第二个及以后的**计算型伤害**槽的"变量系数"变量（<c>ExtraDamage2</c>、<c>ExtraDamage3</c>…）。
///
/// 为什么需要它：原版 <see cref="ExtraDamageVar"/> 没有带名字的构造，名字写死 "ExtraDamage"，
/// 而它在卡面预览里会把**乘法附魔**摊进自己的显示值——
/// <code>baseValue *= enchantment.EnchantDamageMultiplicative(baseValue, ValueProp.Move)</code>
/// 乘法可以这样摊：每个单位的伤害都 ×2，总伤害自然也 ×2。
/// 换成普通 DynamicVar 就没有这段，附魔牌的描述里那句 "造成 N 伤害，每张打击牌额外 +M"
/// 的 M 会少了乘法部分（数值本身不受影响——附魔会在伤害**落地时**对总额生效一次，
/// 见 Hook.ModifyDamage；这里差的只是卡面显示）。
///
/// 加法附魔（"锋利"等）原版也没摊进来——加法是加在总和上的，摊进每单位就重复计算了，
/// 所以 <c>ExtraDamageVar</c> 自己也只管乘法。这里保持一致。
///
/// 覆写 <c>UpdateCardPreview</c>，其余（取值、克隆、升级）与普通 DynamicVar 无异。
/// </summary>
public sealed class SlotExtraDamageVar : DynamicVar
{
    public SlotExtraDamageVar(string name, decimal value)
        : base(name, value)
    {
    }

    public override void UpdateCardPreview(
        CardModel card,
        CardPreviewMode previewMode,
        Creature? target,
        bool runGlobalHooks)
    {
        decimal value = BaseValue;
        EnchantmentModel? enchantment = card.Enchantment;

        if (enchantment is not null)
        {
            value *= enchantment.EnchantDamageMultiplicative(value, ValueProp.Move);

            if (!card.IsEnchantmentPreview)
            {
                EnchantedValue = value;
            }
        }

        PreviewValue = value;
    }
}
