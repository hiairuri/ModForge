using System.Text.RegularExpressions;
using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;
using ModForge.Core.Modules.Variables;
using ModForge.Core.Modules.Effects;

// 三件套验收：**每一种"有数值"的效果**都必须真的接上三件套。
//
// 为什么要这个工具：这些效果原先有 13 种**不占变量槽**，后果是一条静默的失效链——
//   * 生成时不声明变量，打出时把数值写成字面量；
//   * 升级那段只对"有槽"的效果发语句 → **填的升级增量被静默丢弃**（没有 OnUpgrade）；
//   * 描述里写死数字，不随升级变、也没有占位符按钮。
// 界面上看不出任何异常。这个工具就盯这条链，对每一种有数值的效果断言三件事：
//   ① CanonicalVars 里有属于它的变量；
//   ② 填了基础值增量就一定有 OnUpgrade，且改的是**它自己那个**变量；
//   ③ 描述里用占位符 {变量:diff()} 而不是字面数字。
//
// 用法: tripletest

int failed = 0;
int passed = 0;

void Check(string what, bool ok, string detail = "")
{
    if (ok)
    {
        passed++;
        return;
    }

    failed++;
    Console.WriteLine($"  [失败] {what}{(detail.Length > 0 ? $"  ({detail})" : "")}");
}

/// <summary>
/// 从生成的源码里取一个方法的方法体（按花括号配对，不含签名）。
///
/// 不这么做的话，"OnUpgrade 里有什么"会连后面的方法一起算进去——
/// 断言 `!body.Contains("Damage")` 就永远失败，因为它扫的是整个文件。
/// </summary>
static string MethodBody(string source, string signature)
{
    int start = source.IndexOf(signature, StringComparison.Ordinal);

    if (start < 0)
    {
        return string.Empty;
    }

    int open = source.IndexOf('{', start);

    if (open < 0)
    {
        return string.Empty;
    }

    int depth = 0;

    for (int i = open; i < source.Length; i++)
    {
        if (source[i] == '{')
        {
            depth++;
        }
        else if (source[i] == '}')
        {
            depth--;

            if (depth == 0)
            {
                return source[(open + 1)..i];
            }
        }
    }

    return source[(open + 1)..];
}

// 造一张只带一条效果的卡，取它生成出来的源码与自动描述。
(string Source, string Description) Probe(EffectDef effect, string language = "zhs")
{
    var project = new ModProject { Id = "Triple", Name = "Triple", Version = "1.0.0" };

    project.Powers.Add(new PowerDef
    {
        Id = "MARK",
        Name = new LocalizedText("eng", "Mark"),
        Kind = PowerKind.Buff,
    });

    var card = new CardDef
    {
        Id = "PROBE",
        Name = new LocalizedText("eng", "Probe"),
        Description = new LocalizedText("eng", "Test."),
        Pool = "ironclad",
        Type = CardType.Attack,
        Rarity = CardRarity.Common,
        Target = CardTargetType.AnyEnemy,
        EnergyCost = 1,
        Effects = { effect },
    };

    project.Cards.Add(card);

    return (
        new CodeGenerator(project).GenerateAll()[$"Cards/{project.CardTypeName(card)}.cs"],
        DescriptionPlaceholders.Suggest(project, card, language));
}

// ===========================================================================
// 1. 每种"有数值"的效果各来一条，填上基础值 + 升级增量
// ===========================================================================
Console.WriteLine("== 每种有数值的效果：变量 / 升级 / 描述 ==");

// 24 种"schema 里 amount: true"的效果。它们是三件套的适用范围；
// 其余 7 种（RemovePower / TriggerDoom / SetCardsFree / UpgradeCardsInPile /
// TransformCardsInPile / MoveCardsBetweenPiles / DiscardCardsInPile）没有数值，
// 不在三件套范围内。
//
// 消耗牌**在这一组里**：它的数值就是"消耗几张"（见 CardPickPolicy.CountFromEffect），
// 所以升级改张数、张数是算出来的，都走它的三件套。
var numeric = new (EffectKind Kind, string ExpectVar)[]
{
    (EffectKind.DealDamage, "Damage"),
    (EffectKind.GainBlock, "Block"),
    (EffectKind.DrawCards, "Cards"),
    (EffectKind.GainEnergy, "Energy"),
    (EffectKind.GainStars, "Stars"),
    (EffectKind.Heal, "Heal"),
    (EffectKind.LoseHp, "HpLoss"),
    (EffectKind.GainMaxHp, "MaxHp"),
    (EffectKind.LoseMaxHp, "MaxHpLost"),
    (EffectKind.ApplyPower, "StrengthPower"),
    (EffectKind.CopyCardsToPile, "Copies"),
    (EffectKind.AddNamedCardToPile, "AddCards"),
    (EffectKind.ExhaustCards, "ExhaustCount"),
    (EffectKind.ChannelOrb, "Orbs"),
    (EffectKind.EvokeOrbs, "Evoke"),
    (EffectKind.AddOrbSlots, "OrbSlots"),
    (EffectKind.Summon, "Summon"),
    (EffectKind.Forge, "Forge"),
    (EffectKind.EnchantCardsInPile, "Enchant"),
    (EffectKind.BoostCardDamage, "Increase"),
    (EffectKind.AddReplay, "Replay"),
    (EffectKind.AutoPlay, "AutoPlayCount"),
    (EffectKind.ModifyCardCost, "CostDelta"),
};

foreach ((EffectKind kind, string expectVar) in numeric)
{
    var effect = new EffectDef
    {
        Kind = kind,
        Target = EffectTarget.Self,
        Amount = 3,
        Upgrade = new EffectUpgrade { AmountDelta = 2 },
    };

    // 这几条要额外字段，否则生成出来的语句没意义（挑牌、状态、附魔、球、指定牌）。
    switch (kind)
    {
        case EffectKind.ApplyPower:
            effect.PowerId = "POWER.STRENGTH_POWER";
            break;

        case EffectKind.EnchantCardsInPile:
            effect.EnchantmentId = "ENCHANTMENT.SHARP";
            break;

        case EffectKind.AddNamedCardToPile:
            effect.CardId = "CARD.STRIKE_IRONCLAD";
            break;

        case EffectKind.AddReplay:
        case EffectKind.BoostCardDamage:
            effect.Pick = CardPick.This();
            break;
    }

    (string src, string desc) = Probe(effect);

    // ① 有自己的变量
    bool hasVar = src.Contains($"\"{expectVar}\"") || src.Contains($"new {expectVar}Var(");
    Check($"{kind}：声明了自己的变量（{expectVar}）",
        hasVar,
        "没有它的变量 —— 数值会被写成字面量");

    // ② 升级生效：填了 AmountDelta 就必须有 OnUpgrade
    bool hasUpgrade = Regex.IsMatch(src, @"protected override void OnUpgrade\(\)");
    Check($"{kind}：填了升级增量就有 OnUpgrade", hasUpgrade,
        $"没有 OnUpgrade —— 升级增量 {effect.Upgrade.AmountDelta} 被静默丢弃");

    // 升级改的是它自己的变量（名字要出现在 OnUpgrade 里）
    if (hasUpgrade)
    {
        string body = src[src.IndexOf("protected override void OnUpgrade()", StringComparison.Ordinal)..];

        Check($"{kind}：OnUpgrade 改的是自己的变量",
            body.Contains(expectVar),
            $"{expectVar} 没出现在 OnUpgrade 里");
    }

    // ③ 描述用占位符而不是字面数字
    //
    // **一个例外**：ModifyCardCost 的数值是**有符号增量**（降费 -1 / 加费 +2），
    // 而卡面显示的是**量值**（"费用降低 1 点"，代码里是 Math.Abs）。占位符只能给有符号原值，
    // 用了会读成"费用降低 -1 点"。所以这一条**只让升级生效、描述仍用量值字面数字**。
    if (kind == EffectKind.ModifyCardCost)
    {
        Check("ModifyCardCost：描述显示量值（有符号增量不适合占位符）",
            desc.Contains("3"),
            $"描述不对：{desc}");

        Check("ModifyCardCost：尽管如此，升级仍然生效",
            hasVar && hasUpgrade,
            "数值有符号不该成为跳过三件套的理由");
    }
    else
    {
        Check($"{kind}：描述用占位符（{{{expectVar}:diff()}} 或计算型措辞）",
            desc.Contains($"{{{expectVar}:diff()}}") || desc.Contains("等于"),
            $"描述里是字面数字：{desc}");
    }

    // 占位符按钮也要有它（否则作者手打名字，打错就是游戏内格式化失败）
    var card = new ModProject { Id = "Triple", Name = "Triple", Version = "1.0.0" };

    card.Powers.Add(new PowerDef { Id = "MARK", Name = new LocalizedText("eng", "Mark"), Kind = PowerKind.Buff });
    var probeCard = new CardDef
    {
        Id = "PROBE",
        Name = new LocalizedText("eng", "Probe"),
        Description = new LocalizedText("eng", "Test."),
        Pool = "ironclad",
        Type = CardType.Attack,
        Rarity = CardRarity.Common,
        Target = CardTargetType.AnyEnemy,
        EnergyCost = 1,
        Effects = { effect },
    };
    card.Cards.Add(probeCard);

    IReadOnlyList<(string Placeholder, string Label)> placeholders =
        DescriptionPlaceholders.For(card, probeCard, "zhs");

    Check($"{kind}：占位符列表里有它",
        placeholders.Any(p => p.Placeholder == expectVar),
        $"没有 {expectVar} —— 描述编辑器里点不到它");
}

// ===========================================================================
// 2. 同类两条：各自升级不能串到第一条
// ===========================================================================
Console.WriteLine();
Console.WriteLine("== 同类两条：各占各的槽 ==");

{
    var project = new ModProject { Id = "Twin", Name = "Twin", Version = "1.0.0" };

    project.Powers.Add(new PowerDef { Id = "MARK", Name = new LocalizedText("eng", "Mark"), Kind = PowerKind.Buff });
    project.Cards.Add(new CardDef
    {
        Id = "TWIN_ENERGY",
        Name = new LocalizedText("eng", "Twin Energy"),
        Description = new LocalizedText("eng", "Test."),
        Pool = "ironclad",
        Type = CardType.Skill,
        Rarity = CardRarity.Common,
        Target = CardTargetType.Self,
        EnergyCost = 1,
        Effects =
        {
            // 同类第一条：拿到默认名 "Energy"
            new EffectDef
            {
                Kind = EffectKind.GainEnergy, Target = EffectTarget.Self, Amount = 1,
                Upgrade = new EffectUpgrade { AmountDelta = 2 },
            },
            // 同类第二条：必须拿到 "Energy2"，且升级改的是它自己
            new EffectDef
            {
                Kind = EffectKind.GainEnergy, Target = EffectTarget.Self, Amount = 5,
                Upgrade = new EffectUpgrade { AmountDelta = 7 },
            },
        },
    });

    string src = new CodeGenerator(project).GenerateAll()["Cards/TwinEnergy.cs"];

    Check("同类两条：第一条用默认名", src.Contains("new EnergyVar(1)"));
    Check("同类两条：第二条带序号", src.Contains("new EnergyVar(\"Energy2\", 5)"),
        "第二条没有自己的变量 —— 升级会串到第一条");

    string upgrade = src[src.IndexOf("protected override void OnUpgrade()", StringComparison.Ordinal)..];

    Check("同类两条：第一条升级 +2", upgrade.Contains("DynamicVars[\"Energy\"].UpgradeValueBy(2m)"));
    Check("同类两条：第二条升级 +7（不串到第一条）",
        upgrade.Contains("DynamicVars[\"Energy2\"].UpgradeValueBy(7m)"));
}

// ===========================================================================
// 3. 计算型：三件套已经在起作用（对照组，确认没被这次改动弄坏）
// ===========================================================================
Console.WriteLine();
Console.WriteLine("== 计算型（对照）==");

{
    (string src, _) = Probe(new EffectDef
    {
        Kind = EffectKind.GainEnergy,
        Target = EffectTarget.Self,
        Amount = 1,
        Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 2 },
        Upgrade = new EffectUpgrade { AmountDelta = 3, PerUnitDelta = 1 },
    });

    Check("计算型：基础值与系数各改自己的字段",
        src.Contains("_calcBase0 += 3m;") && src.Contains("_calcPerUnit0 += 1m;"),
        "计算型的升级字段不对");

    Check("计算型：不声明固定值变量（数值在私有字段里）",
        !src.Contains("EnergyVar"),
        "计算型不该声明固定值变量");
}

// ===========================================================================
// 3b. 次数那一套三件套也要能升级
// ===========================================================================
//
// 次数与数值一样是"基础值 + 变量 × 变量系数"，所以它有两项升级增量。
// 上一版只有基础值那一项（RepeatDelta），系数那一项填了**被静默丢弃**——
// 生成了 OnUpgrade，看起来一切正常，就是不改那个字段。
Console.WriteLine();
Console.WriteLine("== 次数三件套的升级 ==");

{
    (string src, _) = Probe(new EffectDef
    {
        Kind = EffectKind.DealDamage,
        Target = EffectTarget.Target,
        Amount = 5,
        Repeat = 1,
        RepeatSource = new ValueSource
        {
            Actor = ValueSourceActor.Self,
            Kind = ValueSourceKind.HpLostThisCombat,
            PerUnit = 1m,
        },
        Upgrade = new EffectUpgrade { RepeatDelta = 1, RepeatPerUnitDelta = 0.5m },
    });

    string upgrade = MethodBody(src, "protected override void OnUpgrade()");

    Check("次数：填了次数基础值增量就有 OnUpgrade",
        upgrade.Length > 0,
        "没有 OnUpgrade —— 次数的升级被丢掉了");

    Check("次数：升级改的是次数自己的两个字段（基础值 + 变量系数）",
        upgrade.Contains("_calcBase0 += 1m;") && upgrade.Contains("_calcPerUnit0 += 0.5m;"),
        $"次数的基础值/系数没有各改各的：{upgrade.Replace("\n", " ").Trim()}");

    // 这一次只填了次数那一套增量，数值的字段一个都不该被碰。
    Check("次数：升级没碰数值的字段",
        !upgrade.Contains("Damage") && !upgrade.Contains("_calcBase1"),
        $"数值的字段出现在了次数的升级里：{upgrade.Replace("\n", " ").Trim()}");
}

// ===========================================================================
// 3c. 单次升级也能用等级表（换算式来源只有等级表做得到）
// ===========================================================================
//
// 判据曾经要求"可升级次数 > 1"，于是"只写一格的等级表"整份被丢掉：
// 升级什么都不做，而且哪儿都不报错。
Console.WriteLine();
Console.WriteLine("== 单级等级表 ==");

{
    (string src, _) = Probe(new EffectDef
    {
        Kind = EffectKind.DealDamage,
        Target = EffectTarget.Target,

        // 0 级就是计算型：**"0 级固定值、升级后改成算出来的"这条路走不通**，
        // 因为"是不是计算型"决定整张牌的代码形状（固定值声明 DamageVar，
        // 计算型走就地计算 + 私有字段），一张牌里没法两种都有。
        // 所以等级表能换的只是"用哪个变量"（下面那一级换成弃牌堆数）。
        Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
        Amount = 6,
        Levels = new List<EffectLevel?>
        {
            // 这一级同时做两件只有等级表做得到的事：换算式来源、按级给绝对数值。
            new()
            {
                Amount = 9,
                PerUnit = 2m,
                Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.DiscardPileCount },
            },
        },
    });

    Check("单级等级表：升级仍然生效（不再被丢掉）",
        src.Contains("protected override void OnUpgrade()", StringComparison.Ordinal),
        "可升级次数为 1 时，等级表整份没有发出去");

    Check("单级等级表：按等级绝对赋值，而不是加增量",
        src.Contains("BaseValue = 9m") || src.Contains("_calcBase0 = 9m"),
        "等级表没有走\"这一级就该是这个数\"的写法");

    // 读来源写出来的是**表达式**（"手牌数"是 player.PlayerCombatState!.Hand.Cards.Count），
    // 所以这里比的是两个牌堆各自的表达式，不是枚举名。
    Check("单级等级表：可以换算式来源",
        src.Contains("DiscardPile.Cards.Count", StringComparison.Ordinal) &&
        src.Contains("Hand.Cards.Count", StringComparison.Ordinal) &&
        src.Contains("CurrentUpgradeLevel", StringComparison.Ordinal),
        "等级表换来源的写法没生效（生成代码里没有按等级在新旧来源之间选）");
}

// ===========================================================================
// 3d. 等级表优先：填了表的效果不与增量叠加；没填表的照旧每级加
// ===========================================================================
//
// 两种效果混在一张卡上是合法的（一条换算式来源、另一条只涨数值）。混在一起写的话
// 只能二选一：不是把没填表那条的增量也丢掉，就是让等级表与增量同时生效（与校验器
// 说的"the delta is ignored"不一致，而且往往把数值算重）。
Console.WriteLine();
Console.WriteLine("== 等级表优先 ==");

{
    var project = new ModProject { Id = "Both", Name = "Both", Version = "1.0.0" };

    var card = new CardDef
    {
        Id = "BOTH",
        Name = new LocalizedText("eng", "Both"),
        Description = new LocalizedText("eng", "Test."),
        Pool = "ironclad",
        Type = CardType.Attack,
        Rarity = CardRarity.Common,
        Target = CardTargetType.AnyEnemy,
        EnergyCost = 1,
        MaxUpgradeLevel = 2,
        Effects =
        {
            // 有等级表：升级完全由表说了算，它的 +5 增量不该出现。
            new EffectDef
            {
                Kind = EffectKind.DealDamage,
                Target = EffectTarget.Target,
                Amount = 6,
                Upgrade = new EffectUpgrade { AmountDelta = 5 },
                Levels = new List<EffectLevel?> { new() { Amount = 9 }, new() { Amount = 12 } },
            },

            // 没有等级表：它的增量每级加一次，照旧生效。
            new EffectDef
            {
                Kind = EffectKind.GainBlock,
                Target = EffectTarget.Self,
                Amount = 4,
                Upgrade = new EffectUpgrade { AmountDelta = 2 },
            },
        },
    };

    project.Cards.Add(card);

    string src = new CodeGenerator(project).GenerateAll()[$"Cards/{project.CardTypeName(card)}.cs"];
    string upgrade = MethodBody(src, "protected override void OnUpgrade()");

    Console.WriteLine(upgrade.Trim());

    Check("等级表那条：第 1 级按表赋值成 9",
        upgrade.Contains("\"Damage\"].BaseValue = 9m"),
        upgrade.Trim());

    Check("等级表那条：第 2 级按表赋值成 12",
        upgrade.Contains("\"Damage\"].BaseValue = 12m"),
        upgrade.Trim());

    Check("等级表那条：它自己的 +5 增量没有被叠加（等级表优先）",
        !upgrade.Contains("UpgradeValueBy(5m)"),
        upgrade.Trim());

    Check("没等级表那条：它的增量照旧每级加一次",
        upgrade.Contains("UpgradeValueBy(2m)"),
        upgrade.Trim());
}

// ===========================================================================
// 3e. 升级后换算式来源（没有等级表时的那一路）
// ===========================================================================
Console.WriteLine();
Console.WriteLine("== 升级后换算式来源 ==");

{
    (string src, _) = Probe(new EffectDef
    {
        Kind = EffectKind.DealDamage,
        Target = EffectTarget.Target,
        Amount = 6,
        Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 2m },
        Upgrade = new EffectUpgrade
        {
            Source = new ValueSource
            {
                Actor = ValueSourceActor.Self,
                Kind = ValueSourceKind.DiscardPileCount,
                PerUnit = 1m,
            },
        },
    });

    Check("升级后换来源：两个来源都出现在取值方法里（按等级选一个）",
        src.Contains("DiscardPile.Cards.Count", StringComparison.Ordinal) &&
        src.Contains("Hand.Cards.Count", StringComparison.Ordinal));

    Check("升级后换来源：按当前等级分支",
        src.Contains("CurrentUpgradeLevel", StringComparison.Ordinal));
}

// ===========================================================================
// 4. 没有数值的效果不该被拉进来
// ===========================================================================
Console.WriteLine();
Console.WriteLine("== 没有数值的效果 ==");

foreach (EffectKind kind in new[]
{
    EffectKind.RemovePower, EffectKind.TriggerDoom, EffectKind.SetCardsFree,
    EffectKind.UpgradeCardsInPile, EffectKind.TransformCardsInPile,
    EffectKind.MoveCardsBetweenPiles, EffectKind.DiscardCardsInPile,
})
{
    var effect = new EffectDef { Kind = kind, Target = EffectTarget.Self, Amount = 3 };

    if (kind == EffectKind.RemovePower) effect.PowerId = "POWER.STRENGTH_POWER";
    if (kind is EffectKind.SetCardsFree or EffectKind.UpgradeCardsInPile or EffectKind.TransformCardsInPile)
    {
        effect.Pick = CardPick.InPile(PileKind.Hand);
    }
    if (kind == EffectKind.MoveCardsBetweenPiles) effect.Pick = CardPick.This();

    Check($"{kind}：不占变量槽（它没有数值）",
        VariableModule.Slots(new ModProject { Id = "X", Name = "X", Version = "1" }, new[] { effect }).Count == 0);
}

Console.WriteLine();
Console.WriteLine($"通过 {passed} 项，失败 {failed} 项。");
Console.WriteLine(failed == 0 ? "RESULT: OK" : "RESULT: FAILED");
return failed == 0 ? 0 : 1;
