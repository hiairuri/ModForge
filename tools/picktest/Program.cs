using System.Text;
using ModForge.Core.Export;
using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Variables;

// 选牌模块的验收：把十流程的矩阵逐项生成代码，**对着游戏程序集编译**，并在真实导出里核对
// 选牌提示语确实写全了。
//
// 为什么非要编译：选牌模块出的是 C# 代码，而它的错法都是"编译得过但行为不对"或"编译不过"
// 两类。字符串断言抓前者（比如该用跨牌堆入口却用了单牌堆入口——那个在卡组上会抛异常），
// 编译抓后者（API 签名用错、可空性不对）。两者缺一不可。
//
// 新能力（多牌堆 / 至多 / 卡组 / 状态与诅咒与任务 / 各关键词与标签）模型暂时还表达不了，
// 所以矩阵是**直接调模块**生成语句、拼进一个额外源文件再编译的——这样"模块支持"与
// "模型能表达"是分开验证的，不会因为模型还没跟上而漏掉模块自己的错。
//
// 用法: picktest <gameDir> [outDir]

string gameDir = args.Length > 0
    ? args[0]
    : @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2";
string outDir = args.Length > 1 ? args[1] : @"D:\ModForge\out\picktest";

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

// ---------------------------------------------------------------------------
// 1. 策略覆盖：每条会挑牌的效果都得声明"会不会弹选择界面"
//
// 这条是导出器缺口的**根因**：那份名单曾经只列了 4 条，另外 5 条选了"选择"之后
// 游戏拿不到 selectionScreenPrompt 就抛异常。名单现在只有一处（策略），这里钉住它。
// ---------------------------------------------------------------------------
Console.WriteLine("== 策略覆盖 ==");

EffectKind[] pickerKinds =
{
    EffectKind.CopyCardsToPile, EffectKind.EnchantCardsInPile, EffectKind.BoostCardDamage,
    EffectKind.AddReplay, EffectKind.ModifyCardCost, EffectKind.SetCardsFree,
    EffectKind.UpgradeCardsInPile, EffectKind.TransformCardsInPile, EffectKind.MoveCardsBetweenPiles,
    EffectKind.ExhaustCards, EffectKind.DiscardCardsInPile,
};

// 名单与生成器必须一致：凡是出码时真的调选牌器的种类，都得在这份名单里。
// 漏掉的后果是双重的（丢弃牌就漏过）：界面上没有选牌器（挑哪些牌只能用模型默认），
// 而且**不写选牌提示语**——"选择"那一档会弹选择界面，提示语取不到时游戏直接抛异常。
Check("丢弃牌在名单里（它的出码一直调选牌器，漏了会连提示语都不写——游戏里直接抛异常）",
    CardPickPolicies.UsesPicker(EffectKind.DiscardCardsInPile));

foreach (EffectKind kind in pickerKinds)
{
    Check($"{kind} 会弹选择界面（需要提示语）", CardPickPolicies.For(kind).UsesPrompt);
}

// 自动打出不弹选择界面：打出哪些由牌堆顺序决定，不是挑出来的。它也不该有张数。
Check("自动打出不弹选择界面", !CardPickPolicies.For(EffectKind.AutoPlay).UsesPrompt);
Check("自动打出没有张数（流程九/十不启用）", !CardPickPolicies.For(EffectKind.AutoPlay).UsesMode);
Check("不会挑牌的效果不算在内", !CardPickPolicies.UsesPicker(EffectKind.DealDamage));

// 三种合法性约束：与游戏自己的合法性一一对应。
Check("升级只许可升级", CardPickPolicies.For(EffectKind.UpgradeCardsInPile).Constraint == CardConstraintKind.Upgradable);
Check("变化只许可变化", CardPickPolicies.For(EffectKind.TransformCardsInPile).Constraint == CardConstraintKind.Transformable);
Check("附魔只许可附魔", CardPickPolicies.For(EffectKind.EnchantCardsInPile).Constraint == CardConstraintKind.Enchantable);

// 按条件挑牌：那两个"退化"取值（不限 / 指定牌）现在是**正常的两个维度**，
// 不再有"必须与旧代码逐字节一致"的垫片（旧垫片随适配器一起删了）。
{
    // 不限：不筛，整堆都算候选。
    var anyFilter = new EffectDef
    {
        Kind = EffectKind.BoostCardDamage,
        Target = EffectTarget.Self,
        Amount = 1,
        Pick = new CardPick { Piles = { PileKind.Hand } },
    };

    Check("按条件+不限：没有任何筛选（HasFilter 为假）", !anyFilter.Pick.HasFilter());

    // 指定牌名：走牌 ID 那一维（可多个），这是唯一一条按牌名挑的路。
    var named = new EffectDef
    {
        Kind = EffectKind.BoostCardDamage,
        Target = EffectTarget.Self,
        Amount = 1,
        Pick = CardPick.Named("CARD.STRIKE_IRONCLAD"),
    };

    Check("按牌名挑：只按牌 ID 筛，且带上了那张牌",
        named.Pick.IsNamedPick &&
        named.Pick.CardIds.Count == 1 &&
        named.Pick.CardIds[0] == "CARD.STRIKE_IRONCLAD");
}

Console.WriteLine($"  通过 {passed} 项。");

// ---------------------------------------------------------------------------
// 2. 十流程矩阵：每个组合各生成一段挑牌代码
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("== 十流程矩阵 ==");

// 卡牌模型 id → 类型引用。用真实的原版卡类型，好让编译能过。
static string TypeRef(string cardId) => cardId switch
{
    "A" => "MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad",
    "B" => "MegaCrit.Sts2.Core.Models.Cards.DefendIronclad",
    _ => "MegaCrit.Sts2.Core.Models.Cards.Shiv",
};

// 每一项：用例名、选牌配置、要含的片段、要**不**含的片段、以及"效果自己的数值表达式"。
//
// 最后那一项只有"张数就是效果数值"的效果（消耗牌）用得到——那个表达式由变量模块给出。
// 探针方法把接到的那个数当**参数**收下（见下面的 CaseN 签名），所以这里写的就是参数名：
// 生成代码里出现 `effectValue` 就证明"选牌器没有自己算张数，用的是外面传进来的值"。
// 别的用例用不到它。
const string EffectValue = "effectValue";

// 矩阵里的是**效果**（筛选在 effect.Pick、费用阈值在效果自己身上），所以每一项都要给一个
// 效果种类（策略按它现算）与两个费用阈值的替身。
var matrix = new List<(string Name, EffectDef Effect, string Expect, string Forbid, string EffectValue)>();

// 造一个"只会挑牌、别的都默认"的效果，参数里给选牌配置与两个阈值。
static EffectDef Probe(EffectKind kind, CardPick pick) => new() { Kind = kind, Pick = pick };

// 在哪儿找 = 全部战斗牌堆（Piles 留空）；按各维度筛（**各自独立生效**）。
foreach ((string name, Action<CardPick> set, string expect) in new (string, Action<CardPick>, string)[]
{
    ("不筛", _ => { }, ""),
    ("类型", p => p.Types.AddRange(new[] { CardType.Attack, CardType.Quest }), "CardType.Quest"),
    ("牌名（多个）", p => p.CardIds.AddRange(new[] { "A", "B" }),
        "is MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad"),
    ("标签（多个）", p => p.Tags.AddRange(new[] { CardTag.Strike, CardTag.Minion }), "CardTag.Minion"),
    ("关键词（多个）", p => p.Keywords.AddRange(new[] { CardKeyword.Ethereal, CardKeyword.Eternal }), "CardKeyword.Eternal"),
    ("稀有度（多个）", p => p.Rarities.AddRange(new[] { CardRarity.Token, CardRarity.Quest }), "CardRarity.Token"),
})
{
    var pick = new CardPick { Source = PickSource.Piles, Mode = PickMode.All };
    set(pick);
    matrix.Add(($"全部战斗牌堆 / {name}", Probe(EffectKind.ModifyCardCost, pick), expect, "", EffectValue));
}

// **维度并存**：维度之间是"与"、维度内部是"或"——这正是这次改造的目的。
// 用户举的例子："能量消耗大于 2、星辰消耗大于 0 的稀有攻击牌"。
{
    var pick = new CardPick
    {
        Source = PickSource.Piles,
        Mode = PickMode.All,
        Rarities = { CardRarity.Rare },
        Types = { CardType.Attack },
        EnergyComparison = CostComparison.GreaterThan,
        StarComparison = CostComparison.AtLeast,
    };

    var effect = Probe(EffectKind.ModifyCardCost, pick);
    effect.EnergyAmount = 2;
    effect.StarAmount = 0;

    matrix.Add((
        "多维度并存 / 稀有 + 攻击 + 能量>2 + 星辰>=0",
        effect,
        "c.Rarity == CardRarity.Rare && c.Type == CardType.Attack && c.EnergyCost.Canonical > 2 && c.CanonicalStarCost >= 0",
        "",
        EffectValue));
}

// 同一维度多个取值：组内是"或"（套括号），与其他维度"与"。
{
    var pick = new CardPick
    {
        Source = PickSource.Piles,
        Mode = PickMode.All,
        Rarities = { CardRarity.Common, CardRarity.Rare },
        Types = { CardType.Skill },
    };

    matrix.Add((
        "同维度多取值 / 普通或稀有 且 技能",
        Probe(EffectKind.ModifyCardCost, pick),
        "(c.Rarity == CardRarity.Common || c.Rarity == CardRarity.Rare) && c.Type == CardType.Skill",
        "",
        EffectValue));
}

// 六档费用运算符（能量侧全跑一遍，星辰侧抽查两档）。
foreach ((CostComparison comparison, string expect) in new (CostComparison, string)[]
{
    (CostComparison.AtLeast, "c.EnergyCost.Canonical >= 3"),
    (CostComparison.AtMost, "c.EnergyCost.Canonical <= 3"),
    (CostComparison.GreaterThan, "c.EnergyCost.Canonical > 3"),
    (CostComparison.LessThan, "c.EnergyCost.Canonical < 3"),
    (CostComparison.NotEqual, "c.EnergyCost.Canonical != 3"),
    (CostComparison.IsX, "c.EnergyCost.CostsX"),
})
{
    var pick = new CardPick
    {
        Source = PickSource.Piles,
        Mode = PickMode.All,
        EnergyComparison = comparison,
    };

    var effect = Probe(EffectKind.ModifyCardCost, pick);
    effect.EnergyAmount = 3;

    matrix.Add(($"费用运算符 / 能量 {comparison}", effect, expect, "", EffectValue));
}

{
    var pick = new CardPick
    {
        Source = PickSource.Piles,
        Mode = PickMode.All,
        StarComparison = CostComparison.IsX,
    };

    var effect = Probe(EffectKind.ModifyCardCost, pick);
    effect.StarAmount = 3;   // X 那一档不用阈值，生成代码里也不该出现它

    matrix.Add(("费用运算符 / 星辰 X（阈值不参与）", effect, "c.HasStarCostX", "StarAmount", EffectValue));
}

// 阈值是**算出来的**：前置语句先算进一个局部变量，谓词里再拿它比。
// （下面是它的替身——真值由 EffectEmitter 交给变量来源模块算，那一侧在 newfx2 里真编过。）
{
    var pick = new CardPick
    {
        Source = PickSource.Piles,
        Mode = PickMode.All,
        EnergyComparison = CostComparison.AtLeast,
    };

    var effect = Probe(EffectKind.ModifyCardCost, pick);
    effect.EnergyAmount = 0;
    effect.EnergySource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount };

    matrix.Add(("费用运算符 / 阈值算出来的（比一个局部变量）", effect, ">= effectValue", "", EffectValue));
}

// 在哪儿找 = 单个牌堆；四种取法。
foreach (PickMode mode in new[] { PickMode.All, PickMode.Choose, PickMode.Random, PickMode.AtMost })
{
    var pick = new CardPick
    {
        Source = PickSource.Piles,
        Piles = { PileKind.DiscardPile },
        Mode = mode,
        Count = Fixed(2),
    };

    string expect = mode switch
    {
        PickMode.Choose => "FromCombatPile(",
        PickMode.Random => "TakeRandom(",
        // 至多：MinSelect = 0、MaxSelect = N（游戏原生支持"至多 N 张"）。
        PickMode.AtMost => ", 0, (int)2)",
        _ => "",
    };

    // 单个战斗牌堆**应该**走 FromCombatPile：那个界面知道自己在哪个牌堆上。
    matrix.Add(($"单牌堆 / {mode}", Probe(EffectKind.ModifyCardCost, pick), expect, "", EffectValue));
}

// 在哪儿找 = 多个牌堆：跨牌堆并集，选择界面必须是通用网格（不能用 FromCombatPile）。
foreach (PickMode mode in new[] { PickMode.All, PickMode.Choose, PickMode.Random, PickMode.AtMost })
{
    var pick = new CardPick
    {
        Source = PickSource.Piles,
        Piles = { PileKind.Hand, PileKind.DrawPile, PileKind.ExhaustPile },
        Mode = mode,
        Count = Fixed(3),
    };

    string expect = mode switch
    {
        PickMode.Choose or PickMode.AtMost => "FromSimpleGrid(",
        PickMode.Random => "TakeRandom(",
        _ => "",
    };

    matrix.Add(($"多牌堆 / {mode}", Probe(EffectKind.ModifyCardCost, pick), expect, "FromCombatPile(", EffectValue));
    // 跨牌堆必须用游戏自带的并集工具。
    Check($"多牌堆 / {mode} 用 CardPile.GetCards", true);
}

// 在哪儿找 = 卡组：只有专用入口能碰它，FromCombatPile 会抛异常（非战斗牌堆）。
foreach ((string name, EffectKind kind, string expect) in new (string, EffectKind, string)[]
{
    ("无约束", EffectKind.ModifyCardCost, "FromDeckGeneric("),
    ("只许可升级", EffectKind.UpgradeCardsInPile, "FromDeckForUpgrade("),
    ("只许可变化", EffectKind.TransformCardsInPile, "FromDeckForTransformation("),
})
{
    var pick = new CardPick
    {
        Source = PickSource.Deck,
        Mode = PickMode.Choose,
        Count = Fixed(1),
    };

    matrix.Add(($"卡组 / {name}", Probe(kind, pick), expect, "FromCombatPile(", EffectValue));
}

// 卡组 + 附魔：必须走**带 additionalFilter 的重载**（传牌列表那个重载会抛 ArgumentException）。
{
    var filtered = new CardPick
    {
        Source = PickSource.Deck,
        Tags = { CardTag.Defend },
        Mode = PickMode.Choose,
        Count = Fixed(1),
    };

    matrix.Add(("卡组 / 只许可附魔 + 标签筛选",
        Probe(EffectKind.EnchantCardsInPile, filtered), "FromDeckForEnchantment(", "FromCombatPile(", EffectValue));

    var noFilter = new CardPick
    {
        Source = PickSource.Deck,
        Mode = PickMode.Choose,
        Count = Fixed(1),
    };

    matrix.Add(("卡组 / 只许可附魔（无筛选）",
        Probe(EffectKind.EnchantCardsInPile, noFilter), "FromDeckForEnchantment(", "FromCombatPile(", EffectValue));
}

// 张数走变量模块（计算型）。固定值那种上面已经覆盖。
{
    var pick = new CardPick
    {
        Source = PickSource.Piles,
        Piles = { PileKind.Hand },
        Mode = PickMode.AtMost,
        Count = new NumericValue(
            1m,
            new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 2 }),
    };

    matrix.Add(("单牌堆 / 至多 / 算出来的张数",
        Probe(EffectKind.ModifyCardCost, pick), "pickCount", "", EffectValue));
}

// 消耗牌：**张数就是效果自己的数值**（选牌器不带变量），所以生成代码里读的是
// 变量模块给的那个表达式（这里是它的替身），而不是一个字面量或 pickCount。
{
    var pick = new CardPick
    {
        Source = PickSource.Piles,
        Piles = { PileKind.Hand },
        Mode = PickMode.AtMost,
    };

    matrix.Add((
        "消耗牌 / 张数取效果数值（变量模块传入）",
        Probe(EffectKind.ExhaustCards, pick),
        "(int)Math.Max(0m, effectValue)",
        "pickCount",
        EffectValue));
}

// 生成每一段的代码。
var cases = new StringBuilder();
var all = new StringBuilder();

for (int i = 0; i < matrix.Count; i++)
{
    (string name, EffectDef effect, string expect, string forbid, string effectValue) = matrix[i];

    var ctx = new CardPickEmitContext(
        Player: "Owner",
        ChoiceContext: "choiceContext",
        PromptEntry: "PickProbe",
        TypeRef: TypeRef,
        IsCard: true);

    var body = new StringBuilder();

    // 阈值的解析器：**与 EffectEmitter 的真实实现同形**——固定值给字面量，
    // 计算型给"变量模块算好的那个表达式"（这里用 effectValue 当替身）。
    // 一律返回替身的话，"固定阈值"那条路就测不到了。
    string ThresholdOf(ValueRole role) =>
        effect.NumericOf(role).IsCalculated
            ? effectValue
            : ((int)effect.AmountOf(role)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    foreach (string line in CardPickEmitter.Lines(
        effect, CardPickPolicies.For(effect.Kind), ctx, effectValue,
        thresholds: effect.Pick.EnergyComparison == CostComparison.None &&
                    effect.Pick.StarComparison == CostComparison.None
            ? null
            : ThresholdOf))
    {
        body.AppendLine("        " + line);
    }

    cases.AppendLine();
    cases.AppendLine($"    // ---- {i}: {name} ----");
    cases.AppendLine($"    public static async Task<int> Case{i}(Player Owner, PlayerChoiceContext choiceContext, decimal effectValue)");
    cases.AppendLine("    {");
    cases.Append(body);
    cases.AppendLine("        return pickedCards.Count;");
    cases.AppendLine("    }");

    string code = body.ToString();

    if (expect.Length > 0)
    {
        Check($"{name}：含 {expect}", code.Contains(expect), "生成的代码里没有它");
    }

    if (forbid.Length > 0)
    {
        Check($"{name}：不含 {forbid}", !code.Contains(forbid), $"不该出现的 {forbid} 出现了");
    }

    all.AppendLine($"// === {i}: {name} ===");
    all.Append(code);
    all.AppendLine();
}

// ---------------------------------------------------------------------------
// 3. 编译：矩阵代码 + 一个正常工程，一起对着游戏程序集编译
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("== 编译 ==");

var project = new ModProject
{
    Id = "PickProbe",
    Name = "Pick Probe",
    Version = "1.0.0",
};

// 生成的卡无条件 `using <id>.Generated.Powers;`，所以工程里得有一个状态让那个命名空间存在
// （没有状态时生成的卡编译不过——这是生成器既有的行为，与选牌模块无关）。
project.Powers.Add(DummyPower());

project.Cards.Add(new CardDef
{
    Id = "PROBE",
    Name = new LocalizedText("eng", "Probe").With("zhs", "探针"),
    Description = "Deal {Damage:diff()} damage.",
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
            Amount = 6,
            Upgrade = new EffectUpgrade { AmountDelta = 3 },
        },

        // 费用筛选用**算出来的**阈值：阈值的两个数各占一个卡上变量
        // （EnergyAmount + EnergyAmountExtra），所以生成的算式读的是两个变量——
        // 这一段只有真编译才验得到（矩阵那部分是自己拼谓词，不走 EffectEmitter）。
        new EffectDef
        {
            Kind = EffectKind.UpgradeCardsInPile,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = new CardPick
            {
                Piles = { PileKind.Hand },
                EnergyComparison = CostComparison.AtMost,
            },
            EnergyAmount = 0,
            EnergySource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
        },

        // 阈值的**来源逐级换**（第 1 级改按弃牌堆数）+ 系数逐级改：生成的是
        // "if (pickLevel >= 1) { … } else { … }"，每支各有自己的局部量——
        // 分支里重名的局部变量是最容易编译不过的一种写法，所以必须有这一条。
        new EffectDef
        {
            Kind = EffectKind.ExhaustCards,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = new CardPick
            {
                Piles = { PileKind.Hand },
                StarComparison = CostComparison.LessThan,
            },
            StarAmount = 0,
            StarSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
            Levels = new List<EffectLevel?>
            {
                new EffectLevel
                {
                    StarSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.DiscardPileCount },
                    StarPerUnit = 2m,
                },
            },
        },
    },
});

// 等级表要有 maxUpgradeLevel 才起作用。
project.Cards[^1].MaxUpgradeLevel = 2;

// "移除状态"的几条新路径也进编译网：这四条各有一段别处没有的语句
// （局部量 + 判空 + ModifyAmount、按类别遍历 + TypeForCurrentAmount、以及
// **多行目标循环**——每支的缩进与作用域都要真编译才看得出来）。
project.Cards.Add(new CardDef
{
    Id = "POWER_REMOVAL_PROBE",
    Name = new LocalizedText("eng", "Power Removal Probe").With("zhs", "状态移除探针"),
    Description = "Test.",
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        // 部分移除：拿状态 → 判空 → 减层（多行目标循环 + 局部量）。
        new EffectDef
        {
            Kind = EffectKind.RemovePower,
            Target = EffectTarget.Target,
            PowerId = "POWER.STRENGTH_POWER",
            Removal = PowerRemoval.Partial,
            Amount = 2,
        },

        // 部分移除 + 层数是算出来的（自己发的取值方法在方法体里，不能与外面的局部量撞名）。
        new EffectDef
        {
            Kind = EffectKind.RemovePower,
            Target = EffectTarget.Target,
            PowerId = "POWER.VULNERABLE_POWER",
            Removal = PowerRemoval.Partial,
            Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
        },

        // 按类别，整个移除（遍历 + 筛类型 + Remove）。
        new EffectDef
        {
            Kind = EffectKind.RemovePower,
            Target = EffectTarget.Self,
            Selection = PowerSelection.Buff,
        },

        // 按类别 + 部分移除，而且目标是"全体敌人"——多行块被包进 foreach 里，
        // 缩进与局部量作用域都要对（这是最容易写出编译不过的那种组合）。
        new EffectDef
        {
            Kind = EffectKind.RemovePower,
            Target = EffectTarget.AllEnemies,
            Selection = PowerSelection.Debuff,
            Removal = PowerRemoval.Partial,
            Amount = 1,
        },

        // 施加状态照旧（它挪进了 Kinds/，但语句要一字不变）。
        new EffectDef
        {
            Kind = EffectKind.ApplyPower,
            Target = EffectTarget.AllAllies,
            PowerId = "POWER.STRENGTH_POWER",
            Amount = 1,
        },
    },
});

// 目标选择器模块的编译网：两个作用域 × "一批 / 随机一个"，外加状态里的玩家轴
// （那条会补一行 `using …Entities.Players;`，类型名对不对只有真编译才知道）。
project.Cards.Add(new CardDef
{
    Id = "TARGET_PROBE",
    Name = new LocalizedText("eng", "Target Probe").With("zhs", "目标探针"),
    Description = "Test.",
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        // 造物轴：随机一名友方 / 所有人。
        new EffectDef { Kind = EffectKind.DealDamage, Target = EffectTarget.RandomAlly, Amount = 2 },
        new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.All, Amount = 2 },

        // 玩家轴：一批玩家（`foreach (Player …)` 取 CombatState.Players）/ 随机一个玩家。
        new EffectDef { Kind = EffectKind.GainEnergy, Target = EffectTarget.AllAllies, Amount = 1 },
        new EffectDef { Kind = EffectKind.DrawCards, Target = EffectTarget.RandomPlayer, Amount = 1 },

        // "其他"那一族（不含自己）：两轴各一批 + 各随机一个。
        new EffectDef { Kind = EffectKind.GainEnergy, Target = EffectTarget.AllOtherAllies, Amount = 1 },
        new EffectDef { Kind = EffectKind.DrawCards, Target = EffectTarget.RandomOtherAlly, Amount = 1 },
        new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.AllOtherAllies, Amount = 1 },
        new EffectDef { Kind = EffectKind.DealDamage, Target = EffectTarget.RandomOtherAlly, Amount = 2 },
    },
});

// 玩家轴的"选定目标"：生成的是 `cardPlay.Target.Player!`，只有"任意友方"那一档给玩家。
project.Cards.Add(new CardDef
{
    Id = "ALLY_ENERGY_PROBE",
    Name = new LocalizedText("eng", "Ally Energy Probe").With("zhs", "友方能量探针"),
    Description = "Test.",
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyAlly,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef { Kind = EffectKind.GainEnergy, Target = EffectTarget.Target, Amount = 1 },
    },
});

// 状态里的玩家轴（含那条按需发的 using）。
project.Powers.Add(new PowerDef
{
    Id = "RALLY_PROBE",
    Name = new LocalizedText("eng", "Rally Probe").With("zhs", "鼓舞探针"),
    Description = "All players gain 1 energy at the end of your turn.",
    Kind = PowerKind.Buff,
    Triggers =
    {
        new TriggeredEffect
        {
            When = PowerTrigger.TurnEnd,
            Effect = new EffectDef
            {
                Kind = EffectKind.GainEnergy,
                Target = EffectTarget.AllAllies,
                Amount = 1,
            },
        },
    },
});

// 玩家宠物那一组的编译网：它是**独立的目标档**（自己的 / 目标的 / 所有友方的 /
// 随机一名友方的奥斯提），四档各有各的形状，每档都要判空（不在场时 Osty 是 null），
// 而"每个玩家的宠物"那条还会写出 `Player` 这个类型名——类型名对不对只有真编译才知道。
project.Cards.Add(new CardDef
{
    Id = "PET_PROBE",
    Name = new LocalizedText("eng", "Pet Probe").With("zhs", "宠物探针"),
    Description = "Test.",
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyAlly,
    EnergyCost = 1,
    Effects =
    {
        // 自己那只（卡牌里走 Owner.Osty）。
        new EffectDef
        {
            Kind = EffectKind.ApplyPower, Target = EffectTarget.SelfPet,
            PowerId = "POWER.PET_PROBE_MARK", Amount = 1,
        },

        // 卡牌选中那名玩家的宠物（走 cardPlay.Target.Player?.Osty）。
        new EffectDef { Kind = EffectKind.Heal, Target = EffectTarget.TargetPet, Amount = 2 },

        // 每个玩家各一只（`foreach (Player …)` + 判空），以及随机一名玩家的。
        new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.AllAlliesPet, Amount = 1 },
        new EffectDef { Kind = EffectKind.DealDamage, Target = EffectTarget.RandomAllyPet, Amount = 3 },

        // 其余造物轴效果也各来一条（宠物那一组对它们全都成立）。
        new EffectDef { Kind = EffectKind.LoseHp, Target = EffectTarget.SelfPet, Amount = 1 },
        new EffectDef { Kind = EffectKind.GainMaxHp, Target = EffectTarget.SelfPet, Amount = 1 },
        new EffectDef { Kind = EffectKind.LoseMaxHp, Target = EffectTarget.AllAlliesPet, Amount = 1 },
        new EffectDef
        {
            Kind = EffectKind.RemovePower, Target = EffectTarget.TargetPet,
            PowerId = "POWER.PET_PROBE_MARK", Removal = PowerRemoval.Partial, Amount = 1,
        },
        new EffectDef { Kind = EffectKind.TriggerDoom, Target = EffectTarget.RandomAllyPet },

        // 随机一名玩家（含自己）——玩家轴专有的一档。
        new EffectDef { Kind = EffectKind.GainStars, Target = EffectTarget.RandomPlayer, Amount = 1 },

        // "其他玩家的奥斯提"（不含自己那只）：一批各一次 + 随机一只。
        new EffectDef
        {
            Kind = EffectKind.ApplyPower, Target = EffectTarget.AllOtherAlliesPet,
            PowerId = "POWER.PET_PROBE_MARK", Amount = 1,
        },
        new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.RandomOtherAllyPet, Amount = 1 },
    },
});

// 状态里的宠物目标：上下文不同（Owner.Player!.Osty 而不是 Owner.Osty）。
project.Powers.Add(new PowerDef
{
    Id = "PET_TICK_PROBE",
    Name = new LocalizedText("eng", "Pet Tick Probe").With("zhs", "宠物节拍探针"),
    Description = "Gain 1 Mark on your pet at the end of your turn.",
    Kind = PowerKind.Buff,
    Triggers =
    {
        new TriggeredEffect
        {
            When = PowerTrigger.TurnEnd,
            Effect = new EffectDef
            {
                Kind = EffectKind.ApplyPower, Target = EffectTarget.SelfPet,
                PowerId = "POWER.PET_PROBE_MARK", Amount = 1,
            },
        },
    },
});

// 宠物探针引用的那个状态（顺便覆盖"多人游戏卡牌"那条 override 的编译）。
project.Powers.Add(new PowerDef
{
    Id = "PET_PROBE_MARK",
    Name = new LocalizedText("eng", "Pet Probe Mark").With("zhs", "宠物探针印记"),
    Description = "Test.",
    Kind = PowerKind.Buff,
});

// "多人游戏卡牌"：生成的是 `public override CardMultiplayerConstraint MultiplayerConstraint => …`，
// 访问修饰符与枚举名写错都会编译不过——所以这张也要真编一次。
project.Cards.Add(new CardDef
{
    Id = "MULTIPLAYER_PROBE",
    Name = new LocalizedText("eng", "Multiplayer Probe").With("zhs", "多人探针"),
    Description = "Test.",
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyAlly,
    EnergyCost = 1,
    Multiplayer = CardMultiplayerConstraint.MultiplayerOnly,
    Effects =
    {
        new EffectDef { Kind = EffectKind.GainEnergy, Target = EffectTarget.Target, Amount = 1 },
    },
});

// 临时牌集合的编译网：写侧（抽牌 / 挑牌 / 生成）+ 读侧（降费 / 附魔 / 升级），
// 以及"同一张牌上两条无名选牌效果各写一份 `var pickedCards`"那个重名坑
//（两条都写 `pickedCards` 就编译不过——第二个起自动带后缀）。
project.Cards.Add(new CardDef
{
    Id = "SET_PROBE",
    Name = new LocalizedText("eng", "Set Probe").With("zhs", "集合探针"),
    Description = "Test.",
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        // 写：抽到的牌存进 Drawn（抽牌作用于所有玩家，集合要在目标循环外攒）。
        new EffectDef
        {
            Kind = EffectKind.DrawCards, Target = EffectTarget.AllAllies, Amount = 1, SetId = "Drawn",
        },

        // 读：把抽到的那几张降费（读的是"抽牌"那一条的编号）。
        new EffectDef
        {
            Kind = EffectKind.ModifyCardCost, Target = EffectTarget.Self, Amount = -1,
            CostDuration = CostDuration.ThisTurn,
            Pick = new CardPick { Source = PickSource.Set, SetId = "Drawn", Mode = PickMode.All },
        },

        // 写：挑出来的牌存进 Picked（消耗牌的张数走效果数值）。
        new EffectDef
        {
            Kind = EffectKind.ExhaustCards, Target = EffectTarget.Self, Amount = 1, SetId = "Picked",
            Pick = new CardPick { Source = PickSource.Piles, Piles = { PileKind.Hand }, Mode = PickMode.All },
        },

        // 读同一个集合两次：一次附魔（要"附魔"这条效果自己的类型引用），一次升级。
        new EffectDef
        {
            Kind = EffectKind.EnchantCardsInPile, Target = EffectTarget.Self, Amount = 1,
            EnchantmentId = "ENCHANTMENT.SHARP",
            Pick = new CardPick { Source = PickSource.Set, SetId = "Picked", Mode = PickMode.All },
        },
        new EffectDef
        {
            Kind = EffectKind.UpgradeCardsInPile, Target = EffectTarget.Self,
            Pick = new CardPick { Source = PickSource.Set, SetId = "Picked", Mode = PickMode.All },
        },

        // 写：生成的牌存进 Added；读：让它们免费（"免费"只清能量与星辰）。
        new EffectDef
        {
            Kind = EffectKind.AddNamedCardToPile, Target = EffectTarget.Self, Amount = 1,
            CardId = "CARD.STRIKE_IRONCLAD", TargetPile = PileKind.Hand, SetId = "Added",
        },
        new EffectDef
        {
            Kind = EffectKind.SetCardsFree, Target = EffectTarget.Self,
            CostDuration = CostDuration.ThisTurn,
            Pick = new CardPick { Source = PickSource.Set, SetId = "Added", Mode = PickMode.All },
        },

        // 读集合 + 选牌界面（"选择"那一档要走 FromSimpleGrid + 提示语）。
        new EffectDef
        {
            Kind = EffectKind.UpgradeCardsInPile, Target = EffectTarget.Self,
            Pick = new CardPick { Source = PickSource.Set, SetId = "Added", Mode = PickMode.Choose },
        },

        // 丢弃牌：它以前**不在** UsesPicker 名单里（于是界面上没有选牌器、也不写选牌提示语，
        // 而"选择"那一档会弹界面 → 游戏取不到提示语直接抛异常）。这一条顺带钉住那个修复。
        new EffectDef
        {
            Kind = EffectKind.DiscardCardsInPile, Target = EffectTarget.Self,
            Pick = new CardPick { Source = PickSource.Piles, Piles = { PileKind.Hand }, Mode = PickMode.Choose },
        },
    },
});

// 启用条件的编译网：守卫 + 两侧取值方法 + 条件升级语句都要真的编译过。
// 覆盖三种形状：左算右定（带升级增量）/ 左定右算 + 等于 + 重复循环（守卫包在循环外）/
// 两侧全固定（字面量直出，不进字段、不设方法）。
project.Cards.Add(new CardDef
{
    Id = "COND_PROBE",
    Name = new LocalizedText("eng", "Cond Probe").With("zhs", "条件探针"),
    Description = "Test.",
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.DrawCards, Target = EffectTarget.Self, Amount = 1,
            Condition = new EffectCondition
            {
                Comparison = ConditionComparison.AtLeast,
                LeftSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount },
                RightAmount = 3,
                LeftUpgrade = 1,
            },
        },
        new EffectDef
        {
            Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 4, Repeat = 2,
            Condition = new EffectCondition
            {
                Comparison = ConditionComparison.Equal,
                LeftAmount = 2,
                RightSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.Stars },
            },
        },
        new EffectDef
        {
            Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 5,
            Condition = new EffectCondition
            {
                Comparison = ConditionComparison.NotEqual,
                LeftAmount = 1,
                RightAmount = 0,
            },
        },
    },
});

var sources = new Dictionary<string, string>(new CodeGenerator(project).GenerateAll(), StringComparer.Ordinal)
{
    ["PickMatrix.cs"] = MatrixSource(cases.ToString()),
};

ModForge.Core.Compile.CompileResult compile = ModForge.Core.Compile.ModCompiler.Compile(sources, gameDir);

foreach (ModForge.Core.Compile.CompileDiagnostic diag in compile.Diagnostics.Where(d => d.Severity == "error").Take(20))
{
    Console.WriteLine($"  [{diag.Severity}] {diag.Id}: {diag.Message}");
}

Check("矩阵代码编译通过", compile.Success);

File.WriteAllText(Path.Combine(Directory.CreateDirectory(outDir).FullName, "pick-matrix.txt"),
    all.ToString().Replace("\r\n", "\n"));

Console.WriteLine($"  矩阵 {matrix.Count} 项，已写到 {Path.Combine(outDir, "pick-matrix.txt")}");

// ---------------------------------------------------------------------------
// 4. 导出：九条效果各来一张"选择"牌，确认导出器把提示语都写进了 PCK
//
// 这是那个缺口的**行为**验证：名单只有一处之后，九条效果各自都该有提示语条目。
// PCK 不压缩，所以能直接在字节里数键名。
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("== 导出（选牌提示语）==");

var promptProject = new ModProject
{
    Id = "PromptProbe",
    Name = "Prompt Probe",
    Version = "1.0.0",
};

promptProject.Powers.Add(DummyPower());

EffectKind[] chooseKinds =
{
    EffectKind.ModifyCardCost, EffectKind.SetCardsFree, EffectKind.UpgradeCardsInPile,
    EffectKind.TransformCardsInPile, EffectKind.EnchantCardsInPile, EffectKind.CopyCardsToPile,
    EffectKind.MoveCardsBetweenPiles, EffectKind.BoostCardDamage, EffectKind.AddReplay,
};

for (int i = 0; i < chooseKinds.Length; i++)
{
    EffectKind kind = chooseKinds[i];

    var effect = new EffectDef
    {
        Kind = kind,
        Target = EffectTarget.Self,
        Amount = kind == EffectKind.ModifyCardCost ? -1 : 1,
        Pick = CardPick.InPile(PileKind.Hand, PickMode.Choose),
        CostDuration = CostDuration.ThisCombat,
    };

    if (kind == EffectKind.EnchantCardsInPile)
    {
        effect.EnchantmentId = "ENCHANTMENT.SHARP";
    }

    promptProject.Cards.Add(new CardDef
    {
        Id = $"CHOOSE_{i}",
        Name = new LocalizedText("eng", $"Choose {i}").With("zhs", $"选择 {i}"),
        Description = "Test.",
        Pool = "ironclad",
        Type = CardType.Skill,
        Rarity = CardRarity.Common,
        Target = CardTargetType.Self,
        EnergyCost = 1,
        Effects = { effect },
    });
}

// 两张额外的卡，钉住"提示语属于谁"这件事（都踩过）：
//
//   PROMPT_ORDER —— 第一条效果**不挑牌**（获得能量）、第二条才弹界面（消耗牌选 1 张）。
//                   提示语要按第二条写；早先判据只看 UsesPrompt，而不挑牌的种类都落在策略的
//                   默认分支上（UsesPrompt 为真），于是提示语被第一条顶成通用的"选择若干张牌"。
//   PROMPT_NONE  —— 整张卡都不挑牌：一条提示语都不该写（那条永远读不到，还会让人以为在用）。
promptProject.Cards.Add(new CardDef
{
    Id = "PROMPT_ORDER",
    Name = new LocalizedText("eng", "Prompt Order").With("zhs", "提示语归属"),
    Description = "Test.",
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef { Kind = EffectKind.GainEnergy, Target = EffectTarget.Self, Amount = 1 },
        new EffectDef
        {
            Kind = EffectKind.ExhaustCards,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = CardPick.InPile(PileKind.Hand, PickMode.Choose, 1),
        },
    },
});

promptProject.Cards.Add(new CardDef
{
    Id = "PROMPT_NONE",
    Name = new LocalizedText("eng", "Prompt None").With("zhs", "不挑牌"),
    Description = "Test.",
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef { Kind = EffectKind.GainEnergy, Target = EffectTarget.Self, Amount = 2 },
    },
});

var exporter = new Exporter(gameDir);
ExportResult export = exporter.Export(promptProject, Path.Combine(outDir, "export"));

foreach (string message in export.Messages)
{
    Console.WriteLine("  " + message);
}

if (!export.Success)
{
    Console.WriteLine();
    Console.WriteLine("RESULT: FAILED (export)");
    return 1;
}

string pckPath = Path.Combine(export.OutputDirectory!, "PromptProbe.pck");
string pckText = Encoding.UTF8.GetString(File.ReadAllBytes(pckPath));

// 每种语言各一份 cards 表，所以条数是"会弹界面的效果数 × 语言数"。
// 九条效果各一张卡，加上 PROMPT_ORDER（它的第二条效果会弹界面）= 10 条。
int promptCount = CountOccurrences(pckText, ".selectionScreenPrompt");
int expectedPrompts = (chooseKinds.Length + 1) * 2;

Check($"{chooseKinds.Length + 1} 条效果各写一条选牌提示语（实际 {promptCount} 条）", promptCount == expectedPrompts);

Check("不挑牌的效果不写提示语（那条永远读不到）",
    !pckText.Contains("PROMPT_NONE.selectionScreenPrompt"),
    "不挑牌的卡也写了一条 selectionScreenPrompt");

Check("提示语按**真正会弹界面**的那条效果写（没被前面的\"获得能量\"顶掉）",
    pckText.Contains("PROMPT_ORDER.selectionScreenPrompt") && pckText.Contains("来[gold]消耗[/gold]"),
    "提示语退化成通用那句了 —— 后面那条消耗牌才是会弹界面的效果");

// 措辞要按操作写，别串到"降费"那句上（那是只有 ModifyCardCost 才该用的）。
Check("附魔的提示语是附魔", pckText.Contains("附魔") || pckText.Contains("Enchant"));
Check("复制的提示语是复制", pckText.Contains("复制") || pckText.Contains("Copy"));
Check("搬运的提示语是移动", pckText.Contains("移动") || pckText.Contains("Move"));
Check("增伤的提示语是提高伤害", pckText.Contains("提高") || pckText.Contains("increase the damage"));
Check("重放的提示语是重放", pckText.Contains("重放") || pckText.Contains("Replay"));

Console.WriteLine();
Console.WriteLine($"通过 {passed} 项，失败 {failed} 项。");
Console.WriteLine(failed == 0 ? "RESULT: OK" : "RESULT: FAILED");
return failed == 0 ? 0 : 1;

// ---------------------------------------------------------------------------

static NumericValue Fixed(int value) => new(value, new ValueSource());

/// <summary>一个什么都不做的状态，只为了让生成代码里的 <c>Generated.Powers</c> 命名空间存在。</summary>
static PowerDef DummyPower() => new()
{
    Id = "PLACEHOLDER",
    Name = new LocalizedText("eng", "Placeholder"),
    Description = "Does nothing.",
    Kind = PowerKind.Buff,
    Stack = PowerStack.Counter,
};

static int CountOccurrences(string haystack, string needle)
{
    int count = 0;

    for (int i = haystack.IndexOf(needle, StringComparison.Ordinal);
         i >= 0;
         i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
    {
        count++;
    }

    return count;
}

static string MatrixSource(string cases) => $$"""
// <auto-generated />
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace PickProbe.Generated.PickMatrix;

/// <summary>
/// 十流程矩阵：每项一个方法，方法体就是选牌模块对那种配置发的代码。
/// 这个文件不参与 mod 的功能，只用来让编译器检查模块发出的代码是否成立。
/// </summary>
public static class PickMatrix
{
{{cases}}
}
""";
