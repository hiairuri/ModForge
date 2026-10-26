using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 让挑出来的那些牌伤害增加。（"这张牌自己"只是选牌器流程一里的一档，见
/// <see cref="Modules.CardPicking.PickSource"/>。）
///
/// **本场战斗**：直接改那些牌的伤害值——原版爪击（Claw）/ 撕咬（Maul）的做法。
/// 爪击的说明就是"本场战斗中所有爪击卡牌的伤害增加 2 点"，做法是先正常造成伤害、
/// 再对自己牌堆里每一张爪击加一次值。改的是牌自己的数值，所以与"谁在造成伤害"无关
/// ——这比走状态好：状态得挂在出伤者身上，目标选敌人时永远不匹配，于是
/// "使我方打击牌伤害增加"会变成"给我方生物挂状态"。
///
/// **永久**：写进目标牌自己类上的 <c>[SavedProperty]</c> 字段（接口见运行时的
/// <c>IPermanentDamageBoost</c>）；**原版卡**的类改不了，运行时会自动落到**组件**那条路
/// （数值随存档 props 往返、进战斗时应用，见运行时 <c>CardComponents</c>）——两条路都不占
/// 附魔槽，也互不干扰。对"没有伤害槽的牌"两边都自然落空。
/// </summary>
internal sealed class BoostCardDamageBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.BoostCardDamage;

    public void Emit(EffectEmit a)
    {
        // 同一张牌上可以有多条增伤效果（原版"刀刃虔诚"就是两条），所以局部变量要带序号，
        // 否则生成的两个 `decimal boost` / `pickedCards` 会重名，编译不过。
        string suffix = a.Emitter.NextBoostIndex();
        string boostVar = "boost" + suffix;

        a.Line($"decimal {boostVar} = {a.Amount};");

        foreach (string line in a.Emitter.PickCardsLines(a.Effect, a.Ctx, a.Amount, suffix))
        {
            a.Line(line);
        }

        a.Line($"foreach (var boosted in pickedCards{suffix})");
        a.Line("{");

        // 下面的行**自带缩进**（inner 已含一层），直接写、不走 a.Line。
        string inner = a.Indent + "    ";

        // 目标牌是运行时挑出来的，生成代码不知道它有几个伤害槽，所以统一走运行时辅助：
        // 它按命名约定扫出全部**固定值**伤害槽（Damage / Damage2 / …）并逐个加上去。
        // 只加第一个槽的话，"这张牌的伤害增加 N"会漏掉同牌的后几条伤害——
        // 那正是"每个效果独立"要避免的牵连。
        if (a.Effect.CostDuration == CostDuration.Permanent)
        {
            a.Sb.AppendLine($"{inner}// 永久：写进目标自己的存档字段（各伤害槽都加），并同步牌组里的那张牌。");
            a.Sb.AppendLine($"{inner}// 原版卡没有那个字段，运行时会改挂随存档往返的组件（见 CardComponents）。");
            a.Sb.AppendLine($"{inner}ModForge.Generated.Runtime.PermanentDamageBoost.Apply(boosted, (int){boostVar});");
        }
        else
        {
            a.Sb.AppendLine($"{inner}// 本场战斗：加在这张牌的每个伤害槽上。战斗中的牌是临时副本，战斗结束自然失效。");
            a.Sb.AppendLine($"{inner}ModForge.Generated.Runtime.DamageSlots.AddToAll(boosted, {boostVar});");
        }

        a.Line("}");
    }

    /// <summary>
    /// 措辞按"持续多久"与**主语单复数**变（英文动词跟着变）：
    /// "单数"指主语那个名词短语是单数——这张牌自己、或按牌名挑出来的"每一张的副本"。
    /// </summary>
    public string Describe(EffectDescribe a)
    {
        a.AddPileVars();

        bool singular = a.Effect.Pick.Source == Modules.CardPicking.PickSource.ThisCard ||
                        a.Effect.Pick.IsNamedPick;

        string key = (a.Effect.CostDuration == CostDuration.Permanent, singular) switch
        {
            (true, true) => "boostDamagePermanentSingular",
            (true, false) => "boostDamagePermanent",
            (false, true) => "boostDamageCombatSingular",
            _ => "boostDamageCombat",
        };

        // {num} 要重新算一遍（与入口预填的差别在**计算型**：那种要引用变量名，
        // 入口为防某条模板用到才填字面量）。
        string num = a.Names is null
            ? a.Effect.Amount.ToString()
            : a.Names.Token(a.Names.Value, a.Effect.Amount);

        return a
            .With("num", num)
            .With("pick", PickerWording.PickLabel(
                a.Project, a.Effect, a.Sentences, a.Names, sentenceStart: true))
            .Render(key);
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "使牌的伤害增加", labelEn: "Increase card damage",
        scope: TargetScope.Cards, amount: true,
        durations: new[] { CostDuration.ThisCombat, CostDuration.Permanent }
            .Select(d => d.ToString()).ToArray());
}
