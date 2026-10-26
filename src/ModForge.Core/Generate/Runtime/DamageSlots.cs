using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace ModForge.Generated.Runtime;

/// <summary>
/// "使某些牌的伤害增加 N"（本场战斗内有效）要改**这张牌的每一个伤害槽**，不是只改第一个。
///
/// 一张牌可以有多个伤害值：本编辑器给每条造成伤害的效果各分配一个变量
/// （<c>Damage</c>、<c>Damage2</c>、<c>Damage3</c>…，见 EffectResolver.EffectSlots）。
/// 原版爪击那种"本场战斗中所有爪击伤害 +2"只改自己那一个 Damage 变量就够了，
/// 但我们的目标是"这张牌的伤害增加 N"——若只加第一个槽，第二条伤害就漏掉了，
/// 同一张牌上各效果又会互相牵连（那正是"每个效果独立"要根除的）。
///
/// 目标牌是**运行时**由玩家挑选的，生成代码不知道它有哪些槽，所以只能按命名约定扫描：
/// <c>Damage</c>，或 <c>Damage</c> 后面全是数字。只认**固定值**伤害槽——
/// 计算型伤害的"基础值"是算式的一部分（<c>CalculationBase</c>），加进去会改变算式含义，
/// 且那个名字未必属于伤害（一张牌上计算型伤害与格挡的基础值按全局顺序编号，
/// 光看名字分不出谁是伤害）。
/// </summary>
public static class DamageSlots
{
    /// <summary>把 <paramref name="extra"/> 加到这张牌的每个固定伤害槽上。没有伤害槽就什么也不做。</summary>
    public static void AddToAll(CardModel card, decimal extra)
    {
        if (card is null || extra == 0m)
        {
            return;
        }

        // 先取键列表再改：DynamicVars 是字典，遍历中改值本身没问题，但这里保持与游戏
        // 其它地方的写法一致（UpdateDynamicVarPreview 也是先 ToList 再处理）。
        foreach (string key in new List<string>(card.DynamicVars.Keys))
        {
            if (IsDamageSlot(key) && card.DynamicVars.TryGetValue(key, out DynamicVar? var))
            {
                var.BaseValue += extra;
            }
        }
    }

    /// <summary>这个变量名是不是一个固定值伤害槽（Damage / Damage2 / Damage3…）。</summary>
    public static bool IsDamageSlot(string name)
    {
        if (name == "Damage")
        {
            return true;
        }

        // "Damage" 之后必须**全是数字**：免得把别人的 "DamageBoost"、"DamageMultiplier"
        // 这类名字算进来。
        if (name.Length <= 6 || !name.StartsWith("Damage", StringComparison.Ordinal))
        {
            return false;
        }

        for (int i = 6; i < name.Length; i++)
        {
            if (name[i] < '0' || name[i] > '9')
            {
                return false;
            }
        }

        return true;
    }
}
