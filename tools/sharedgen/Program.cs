using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Variables;

// Prints the generated code for a project whose card and buff both use one shared effect,
// to confirm the shared behaviour lands in both places.
//
// Usage: sharedgen

var project = new ModProject
{
    Id = "SharedDemo",
    Name = "Shared Demo",
    Version = "1.0.0",
};

project.SharedEffects.Add(new SharedEffectDef
{
    Id = "DRAIN",
    Name = "Drain",
    Effect = new EffectDef
    {
        Kind = EffectKind.Heal,
        Target = EffectTarget.Self,
        Amount = 4,
        Upgrade = new EffectUpgrade { AmountDelta = 2 },
    },
});

project.Powers.Add(new PowerDef
{
    Id = "LEECH",
    Name = "Leech",
    Description = "At the end of your turn, heal {Amount}.",
    Kind = PowerKind.Buff,
    Stack = PowerStack.Counter,
    Triggers =
    {
        new TriggeredEffect
        {
            When = PowerTrigger.TurnEnd,
            Effect = new EffectDef { SharedEffectId = "DRAIN" },
        },
    },
});

project.Cards.Add(new CardDef
{
    Id = "DRAIN_STRIKE",
    Name = "Drain Strike",
    Description = "Deal {Damage:diff()} damage.\nHeal up to 4 HP.",
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 7,
            Upgrade = new EffectUpgrade { AmountDelta = 3 },
        },
        new EffectDef { SharedEffectId = "DRAIN" },
    },
});

var generator = new CodeGenerator(project);

// 共享定义里那些"引用行不该丢"的字段：合并时必须是整体复制，逐字段抄会漏。
//
// 这里用一条带作用条件与计算型来源的定义，检查引用它的卡上这些东西都还在——
// 逐字段抄的写法（曾经如此）会把这些悄悄丢掉：编译照过，只是行为不是设的那样。
project.SharedEffects.Add(new SharedEffectDef
{
    Id = "SCALD",
    Name = "Scald",
    Effect = new EffectDef
    {
        Kind = EffectKind.BoostCardDamage,
        Target = EffectTarget.Self,
        Amount = 2,
        Pick = CardPick.OfTags(CardTag.Shiv),
        CostDuration = CostDuration.Permanent,
        CalculatedRepeat = true,
        RepeatBase = 1,
        RepeatSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HpLostThisCombat },
    },
});

project.Cards.Add(new CardDef
{
    Id = "SCALD_TEST",
    Name = "Scald Test",
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        // 引用行只带升级增量：其余字段必须完全来自定义。
        new EffectDef { SharedEffectId = "SCALD", Upgrade = new EffectUpgrade { AmountDelta = 1 } },
    },
});

var scalded = generator.GenerateAll()["Cards/ScaldTest.cs"];

bool keptFilter = scalded.Contains("CardTag.Shin") || scalded.Contains("CardTag.Shiv");
bool keptPermanent = scalded.Contains("ApplyPermanentDamageBoost") || scalded.Contains("PermanentDamageBoost");
bool keptScope = scalded.Contains("AllCards") || scalded.Contains("PickScope");
bool keptCalcRepeat = scalded.Contains("CalcRepeat");

if (!keptFilter || !keptPermanent || !keptCalcRepeat)
{
    Console.WriteLine();
    Console.WriteLine("  [失败] 共享效果合并时丢了字段"
        + $"（作用条件={keptFilter}，永久={keptPermanent}，计算型次数={keptCalcRepeat}）。");
    Console.WriteLine("         逐字段复制漏一项就会这样：编译正常，只是行为不是设的那样。");
    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (shared effect dropped fields)");
    return 1;
}

Console.WriteLine();
Console.WriteLine("== 共享效果带作用条件 / 永久 / 计算型次数 ==");
Console.WriteLine($"  作用条件在: {keptFilter}，永久: {keptPermanent}，计算型次数: {keptCalcRepeat}");

Console.WriteLine("== card ==");
foreach (string line in generator.GenerateAll()["Cards/DrainStrike.cs"]
             .Split('\n')
             .Where(l => l.Contains("Heal") || l.Contains("Damage") || l.Contains("OnUpgrade") || l.Contains("CanonicalVars") || l.Contains("UpgradeValueBy")))
{
    Console.WriteLine("  " + line.Trim());
}

Console.WriteLine();
Console.WriteLine("== power ==");
foreach (string line in generator.GenerateAll()["Powers/LeechPower.cs"]
             .Split('\n')
             .Where(l => l.Contains("Heal") || l.Contains("AfterSideTurnEnd") || l.Contains("if (side")))
{
    Console.WriteLine("  " + line.Trim());
}

Console.WriteLine();
Console.WriteLine("The same shared Heal appears in both, and the card keeps its own Damage.");
Console.WriteLine("RESULT: OK");
return 0;
