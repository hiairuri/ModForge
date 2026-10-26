using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 给某个牌堆中的若干张牌附魔。
///
/// 游戏没有批量接口，原版也是自己遍历（见 GnarledHammer），所以这里同样逐个施加。
/// 每张牌都要取一份附魔的"可变副本"，否则多张牌会共用同一个实例。
///
/// **给哪些牌附魔由选牌器决定**（作用对象 + 作用方式 + 张数）——这里**不再按效果数值
/// 截断**：数值是"附魔层数"（Enchant 的第二个参数），不是"附几张牌"。
///
/// 两者是不同的东西，原版分得很清楚："墨刃"给 2 张小刀各附 1 层（张数由它遍历的牌决定、
/// amount 传 1），"自学手册"选 1 张牌附 2 层（选牌器的张数是 1、amount 传 2）。
/// 早先这里写成 `pickedCards.Take((int)amount)`，把层数当成了张数——于是"选 3 张各附 2 层"
/// 会变成"只附前 2 张、各 1 层"，两处都不对。
///
/// 挑牌用**游戏自己的判定**（<c>enchantment.CanEnchant</c>）：附魔各有各的适用范围
/// （"锋利"只能附攻击、"黏稠"只能附防御、诅咒牌与牌组里的"不能被打出"牌都不行），
/// 自己写一套迟早与原版不一致。那段谓词由选牌模块按效果的约束给出
/// （constraint: Enchantable，见 <see cref="CardPickPolicies"/>）。
///
/// 例外只有一种：**这个附魔真有层数、而牌上已经带着同种附魔**——那种牌要留下，
/// 因为我们对它做的是"加层数"，滤掉就没得加了。"有层数"用游戏自己的 <c>ShowAmount</c> 判
/// （原版声明了"这个附魔的 Amount 有意义并要显示"）。不这么判的话，克隆 / 本能 / 腐化这类
/// **计算时根本不看 Amount** 的附魔也会出现在选牌框里，玩家选中后什么都没发生。
///
/// 注意**不能**改成判 <c>IsStackable</c>：CanEnchant 的最后一关是
/// `card.Enchantment != null && (!IsStackable || 类型不同)`，同种且可叠加时它本来就放行。
/// 也就是说"同种且可叠加"的牌早已被 CanEnchant 收进来，再用它做例外等于什么都没加；
/// 而原版附魔**没有一个覆写 IsStackable**（全默认 false），那样写会让例外永不成立。
/// </summary>
internal sealed class EnchantCardsInPileBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.EnchantCardsInPile;

    public void Emit(EffectEmit a)
    {
        if (string.IsNullOrWhiteSpace(a.Effect.EnchantmentId))
        {
            a.Line("// 未指定附魔");
            return;
        }

        // Enchant<T> 每次都会从模型库取一份可变副本，所以逐个调用是正确的用法。
        string enchantType = a.Emitter.EnchantmentTypeRef(a.Effect.EnchantmentId);

        string picked = a.Emitter.NextPlainPickSuffix();

        foreach (string line in a.Emitter.PickCardsLines(a.Effect, a.Ctx, a.Amount, picked))
        {
            a.Line(line);
        }

        a.Line($"foreach (var enchantTarget in pickedCards{picked})");
        a.Line("{");

        // 已经有同种附魔 → **直接加层数**，不走 CardCmd.Enchant。
        //
        // 那条路第一步就是 `if (!enchantment.CanEnchant(card)) throw`，而"已经有同种附魔"
        // 对不可叠加的附魔恰好是 CanEnchant 返回 false 的情形（原版所有附魔都不可叠加），
        // 于是它会抛异常——它内部那段"同种附魔就 Amount += "根本走不到。
        // 这里照那段的本意做：直接加层数，不管 CanEnchant 怎么说。
        a.Inner($"if (enchantTarget.Enchantment is {enchantType} existing)");
        a.Inner("{");
        a.Inner($"    existing.Amount += (int)({a.Amount});");
        a.Inner("}");
        a.Inner("else");
        a.Inner("{");
        a.Inner($"    CardCmd.Enchant<{enchantType}>(enchantTarget, {a.Amount});");
        a.Inner("}");
        a.Blank();

        a.Inner("CardCmd.Preview(enchantTarget);");
        a.Line("}");
    }

    // 附魔的句子："为{pick}附魔 {layers} 层{enchant}"——层数是这条效果自己的数值。
    // {num} 是入口预填的同一件事：有变量名时它是占位符（升级后卡面跟着变），没有时是字面量。
    public string Describe(EffectDescribe a)
    {
        a.With("enchant", DescriptionNames.EnchantmentName(a.Effect.EnchantmentId, a.Sentences));
        a.With("pick", a.PickLabel());
        a.With("layers", a.Vars.TryGetValue("num", out string? num) ? num : a.Effect.Amount.ToString());

        return a.Render("enchant");
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "附魔牌堆中的牌", labelEn: "Enchant cards in a pile",
        scope: TargetScope.Cards, amount: true, needsEnchantment: true);
}
