using ModForge.Core;
using ModForge.Core.Export;
using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;
using ModForge.Core.Modules.Variables;
using ModForge.Core.Modules.Effects;

// 验证新能力：计算型数值、牌堆操作、触发时机。
//
// 用你举的那几个例子来构造卡牌，确认生成的代码能编译，并打印关键行。
//
// 用法: newfx <gameDir> [outDir]

string gameDir = args.Length > 0
    ? args[0]
    : @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2";
string outDir = args.Length > 1 ? args[1] : @"D:\ModForge\out\newfx";

var project = new ModProject
{
    Id = "NewFxDemo",
    Name = "新效果演示",
    Author = "ModForge",
    Version = "1.0.0",

    // 启动时打印每张卡的注册情况，便于确认真的进了牌池。
    SelfCheckOnStartup = true,
};

// 状态：嗜血
project.Powers.Add(new PowerDef
{
    Id = "BLOODTHIRST",
    Name = new LocalizedText("eng", "Bloodthirst").With("zhs", "嗜血"),
    Description = new LocalizedText("eng", "Heal {Amount} HP at end of turn.")
        .With("zhs", "在你的回合结束时回复 {Amount} 点生命。"),
    Kind = PowerKind.Buff,
    Stack = PowerStack.Counter,
});

// 力量（用于"等于自身力量的"计算）
project.Powers.Add(new PowerDef
{
    Id = "MIGHT",
    Name = new LocalizedText("eng", "Might").With("zhs", "伟力"),
    Description = new LocalizedText("eng", "Gain {Amount} Might.").With("zhs", "获得 {Amount} 层伟力。"),
    Kind = PowerKind.Buff,
    Stack = PowerStack.Counter,
});

// 卡 1：获得 等于 自身力量层数 的 伟力
project.Cards.Add(new CardDef
{
    Id = "MIRROR_MIGHT",
    Name = new LocalizedText("eng", "Mirror Might").With("zhs", "镜像伟力"),
    Description = new LocalizedText("eng", "Gain Might equal to your Strength.")
        .With("zhs", "获得 等于 自身力量 的伟力。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.ApplyPower,
            Target = EffectTarget.Self,
            PowerId = "MIGHT",
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.PowerStacks, PowerId = "POWER.STRENGTH_POWER", PerUnit = 1m },
        },
    },
});

// 卡 2：恢复 等于 目标格挡×2 的 HP，并消耗
project.Cards.Add(new CardDef
{
    Id = "GUARD_DRAIN",
    Name = new LocalizedText("eng", "Guard Drain").With("zhs", "化盾为血"),
    Description = new LocalizedText("eng", "Heal HP equal to twice the target's Block. Exhaust.")
        .With("zhs", "回复 等于 目标格挡×2 的 HP。消耗。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Keywords = { CardKeyword.Exhaust },
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.Heal,
            Target = EffectTarget.Target,
            Source = new ValueSource { Actor = ValueSourceActor.Target, Kind = ValueSourceKind.Block, PerUnit = 2m },
        },
    },
});

// 卡 3：对所有人造成等于手牌数的伤害，再随机消耗手牌里**一半**的牌。
//
// 消耗的张数就是这条效果自己的数值（见 CardPickPolicy.CountFromEffect）：
// 基础值 0 + 手牌数 × 0.5。所以"算出来的张数"走的是变量模块那套通路，
// 选牌器自己不存变量；升级改张数也走它（AmountDelta / PerUnitDelta）。
project.Cards.Add(new CardDef
{
    Id = "HAND_BARRAGE",
    Name = new LocalizedText("eng", "Hand Barrage").With("zhs", "手牌轰炸"),
    Description = new LocalizedText("eng", "Deal damage equal to your hand size to ALL enemies.\nExhaust half of your hand (rounded up).")
        .With("zhs", "对所有敌人造成 等于手牌数 的伤害。\n消耗手牌中一半的牌（张数 = 手牌数 × 0.5）。"),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.AllEnemies,
    EnergyCost = 2,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.AllEnemies,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 1m },

            // 计算型的两个部分都能升级：基础 +1，每单位 +0.5。
            Upgrade = new EffectUpgrade { AmountDelta = 1, PerUnitDelta = 0.5m },
        },
        new EffectDef
        {
            Kind = EffectKind.ExhaustCards,
            Target = EffectTarget.Self,

            // 张数 = 0 + 手牌数 × 0.5（取整由生成代码里的 (int) 负责）。
            Amount = 0,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 0.5m },
            Pick = CardPick.InPile(PileKind.Hand, PickMode.Random),
        },
    },
});

// 卡 4：把这张牌置于抽牌堆顶，并在回合开始时抽一张（触发时机示例）
project.Cards.Add(new CardDef
{
    Id = "RECALL",
    Name = new LocalizedText("eng", "Recall").With("zhs", "回收"),
    Description = new LocalizedText("eng", "Put this card on top of your draw pile.")
        .With("zhs", "将这张牌置于抽牌堆顶。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 0,
    Effects =
    {
        new EffectDef
        {
            // 将此牌置于抽牌堆顶（并入"在牌堆间移动牌"后：作用对象 = 此牌）。
            Kind = EffectKind.MoveCardsBetweenPiles,
            Target = EffectTarget.Self,
            Pick = CardPick.This(),
            TargetPile = PileKind.DrawPile,
            Placement = PilePlacement.Top,
        },
    },
});

// 卡 5：把 3 张 打击 加入手牌（加入指定卡牌）
project.Cards.Add(new CardDef
{
    Id = "CALL_STRIKES",
    Name = new LocalizedText("eng", "Call Strikes").With("zhs", "召唤打击"),
    Description = new LocalizedText("eng", "Add 3 Strikes to your hand.")
        .With("zhs", "将 3 张打击加入手牌。"),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.AddNamedCardToPile,
            Target = EffectTarget.Self,
            
            Amount = 3,
            CardId = "CARD.STRIKE_IRONCLAD",
        Pick = CardPick.InPile(PileKind.Hand),
        },
    },
});

// 卡 6：回合结束时对所有人造成伤害（触发时机示例）
project.Cards.Add(new CardDef
{
    Id = "LINGERING_HEAT",
    Name = new LocalizedText("eng", "Lingering Heat").With("zhs", "余温"),
    Description = new LocalizedText("eng", "Deal 4 damage to ALL enemies at the end of your turn.")
        .With("zhs", "在你的回合结束时对所有敌人造成 4 点伤害。"),
    Pool = "ironclad",
    Type = CardType.Power,
    Rarity = CardRarity.Uncommon,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.AllEnemies,
            Amount = 4,
            Trigger = EffectTrigger.EndOfTurn,
        },
    },
});

// 接上游戏本地化表，原版卡名才会显示成中文/英文名而不是模型 id。
DescriptionModule.Names = new VanillaNames(gameDir);

Console.WriteLine("=== 生成的效果说明（中 / 英）===");
foreach (CardDef card in project.Cards)
{
    Console.WriteLine();
    Console.WriteLine($"{card.Id}  ({card.Name.GetOrFallback("zhs")})");
    foreach (EffectDef effect in EffectResolver.Resolve(project, card.Effects))
    {
        Console.WriteLine($"   [zh] {DescriptionModule.Describe(project, effect, "zhs")}");
        Console.WriteLine($"   [en] {DescriptionModule.Describe(project, effect, "en")}");
    }
}

Console.WriteLine();
Console.WriteLine("=== 自动填充的描述 ===");
foreach (CardDef card in project.Cards)
{
    Console.WriteLine($"  {card.Id} [zh]: {DescriptionPlaceholders.Suggest(project, card, "zhs").Replace("\n", " / ")}");
}

Console.WriteLine();
Console.WriteLine("=== 计算型数值生成的代码 ===");
var generator = new CodeGenerator(project);
IReadOnlyDictionary<string, string> sources = generator.GenerateAll();

foreach (string file in new[] { "Cards/MirrorMight.cs", "Cards/HandBarrage.cs", "Cards/GuardDrain.cs" })
{
    Console.WriteLine();
    Console.WriteLine($"--- {file} ---");
    foreach (string line in sources[file].Split('\n').Where(l =>
                 l.Contains("CanonicalVars") || l.Contains("CalculationBase") ||
                 l.Contains("CalculationExtra") || l.Contains("Calculated") ||
                 l.Contains("ValueMultiplier") || l.Contains("GetPower") ||
                 l.Contains("decimal value") || l.Contains("GainBlock") ||
                 l.Contains("CreateClone") || l.Contains("moveCount") ||
                 l.Contains("AfterSideTurnEnd")))
    {
        Console.WriteLine("  " + line.Trim());
    }
}

Console.WriteLine();
Console.WriteLine("=== 编译 ===");
var exporter = new Exporter(gameDir)
{
    WebpConverter = ModForge.Core.Gfx.WebpConverter.Discover(@"D:\Tools", ""),
};

ExportResult result = exporter.Export(project, outDir);
foreach (string m in result.Messages)
{
    Console.WriteLine("  " + m);
}

Console.WriteLine();
Console.WriteLine(result.Success ? "RESULT: OK" : "RESULT: FAILED");
return result.Success ? 0 : 1;
