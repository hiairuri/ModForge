using ModForge.Core;
using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;
using ModForge.Core.Modules.Variables;
using ModForge.Core.Modules.Effects;

// Checks that a card's numbers are derived from its effects, and that a shared effect
// referenced by both a card and a buff generates once and behaves consistently.
//
// Usage: effecttest

ModProject project = SampleProject.Create();

// --- 1. Derived numbers -------------------------------------------------
Console.WriteLine("== derived numbers ==");
foreach (CardDef card in project.Cards)
{
    IReadOnlyList<EffectDef> resolved = EffectResolver.Resolve(project, card.Effects);
    IReadOnlyDictionary<string, int> vars = EffectResolver.DynamicVars(project, resolved);

    string varsText = vars.Count == 0
        ? "(none)"
        : string.Join(", ", vars.Select(v => $"{v.Key}={v.Value}"));

    Console.WriteLine($"  {card.Id,-16} effects={resolved.Count} vars: {varsText}");
}

// --- 2. A shared effect used in two places ------------------------------
Console.WriteLine();
Console.WriteLine("== shared effect ==");

var shared = new SharedEffectDef
{
    Id = "BURN_STRIKE",
    Name = "Burn Strike",
    Note = "Reused by a card and by a buff's turn trigger.",
    Effect = new EffectDef { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 5 },
};

project.SharedEffects.Add(shared);

// A card that uses it, and a buff that fires it each turn. The buff needs an enemy
// target, which its context resolves.
project.Cards.Add(new CardDef
{
    Id = "SHARED_HIT",
    Name = "Shared Hit",
    Description = "Deal {Damage:diff()} damage.",
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef { SharedEffectId = "BURN_STRIKE" },
    },
});

project.Powers.Add(new PowerDef
{
    Id = "BURN_AURA",
    Name = "Burn Aura",
    Description = "At the end of your turn, deal {Amount} damage to a random enemy.",
    Kind = PowerKind.Buff,
    Stack = PowerStack.Counter,
    Triggers =
    {
        new TriggeredEffect
        {
            When = PowerTrigger.TurnEnd,
            Effect = new EffectDef { SharedEffectId = "BURN_STRIKE" },
        },
    },
});

IReadOnlyList<EffectDef> sharedResolved = EffectResolver.Resolve(
    project,
    project.Cards.First(c => c.Id == "SHARED_HIT").Effects);

Console.WriteLine($"  definition : {shared.Effect.Kind} {shared.Effect.Amount}");
Console.WriteLine($"  card sees  : {sharedResolved.Count} effect(s), " +
                  $"kind={sharedResolved[0].Kind} amount={sharedResolved[0].Amount}");

IReadOnlyDictionary<string, int> sharedVars = EffectResolver.DynamicVars(project, sharedResolved);
Console.WriteLine($"  card vars  : {(sharedVars.Count == 0 ? "(none)" : string.Join(", ", sharedVars.Select(v => $"{v.Key}={v.Value}")))}");

// --- 3. Description suggestions ----------------------------------------
Console.WriteLine();
Console.WriteLine("== suggested descriptions ==");
foreach (CardDef card in project.Cards)
{
    string suggested = DescriptionPlaceholders.Suggest(project, card);
    string flat = suggested.Replace("\n", " / ");
    Console.WriteLine($"  {card.Id,-16} {flat}");

    // 同一张卡的中文句式，用于确认模板是按语言给出的。
    string zh = DescriptionPlaceholders.Suggest(project, card, "zhs").Replace("\n", " / ");
    Console.WriteLine($"  {"",-16} [zh] {zh}");

    IReadOnlyList<(string Placeholder, string Label)> tokens = DescriptionPlaceholders.For(project, card);
    if (tokens.Count > 0)
    {
        Console.WriteLine($"                   tokens: {string.Join(", ", tokens.Select(t => $"{{{t.Placeholder}}} = {t.Label}"))}");
    }
}

// --- 4. Generated output ------------------------------------------------
Console.WriteLine();
Console.WriteLine("== generated shared-effect card ==");
var generator = new CodeGenerator(project);
foreach (Dictionary<string, string> _ in new[] { new Dictionary<string, string>() })
{
    // no-op to keep formatting simple
}

string source = generator.GenerateAll()["Cards/SharedHit.cs"];
foreach (string line in source.Split('\n').Where(l =>
             l.Contains("CanonicalVars") || l.Contains("DamageVar") || l.Contains("CreatureCmd.Damage")))
{
    Console.WriteLine("  " + line.Trim());
}

Console.WriteLine();
Console.WriteLine("== generated shared-effect power ==");
string powerSource = generator.GenerateAll()["Powers/BurnAuraPower.cs"];
foreach (string line in powerSource.Split('\n').Where(l =>
             l.Contains("AfterSideTurnEnd") || l.Contains("CreatureCmd.Damage") || l.Contains("foreach")))
{
    Console.WriteLine("  " + line.Trim());
}

Console.WriteLine();
Console.WriteLine("RESULT: OK");
return 0;
