using System.Text;
using ModForge.Core;
using ModForge.Core.Compile;
using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.Cards;

// 卡牌核心模块的验收：把固有属性的每种组合各生成一张卡，**对着游戏程序集编译**，
// 并断言校验器抓得住那两种"生成得出来、游戏里才炸"的组合。
//
// 为什么必须编译：星辰费用、X 星、升级星辰变化这三项都是游戏侧的虚属性 / protected 方法，
// 名字写错、签名用错都只有编译能发现（原版 40 多张星费卡用的就是同一个 API，编译过就等于
// 形状与游戏一致）。
//
// 用法: cardtest <gameDir> [outDir]

string gameDir = args.Length > 0
    ? args[0]
    : @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2";
string outDir = args.Length > 1 ? args[1] : @"D:\ModForge\out\cardtest";

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
// 1. 一张卡把所有固有属性都用上，逐条断言生成的文本
// ---------------------------------------------------------------------------
Console.WriteLine("== 固有属性出码 ==");

var project = new ModProject { Id = "CardProbe", Name = "Card Probe", Version = "1.0.0" };

// 生成的卡无条件 `using <id>.Generated.Powers;`，所以工程里得有一个状态让那个命名空间存在。
project.Powers.Add(new PowerDef
{
    Id = "PLACEHOLDER",
    Name = new LocalizedText("eng", "Placeholder"),
    Kind = PowerKind.Buff,
});

project.Cards.Add(new CardDef
{
    Id = "FULL_CORE",
    Name = new LocalizedText("eng", "Full Core").With("zhs", "完整核心"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 2,
    StarCost = 3,
    UpgradeCostDelta = -1,
    UpgradeStarDelta = -1,
    Keywords = { CardKeyword.Exhaust, CardKeyword.Innate },
    ReplayCount = 2,
    MaxUpgradeLevel = 2,
    Effects = { new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 5 } },
});

// X 费 / X 星：两种 X 各一张（X 与数值互斥）。
project.Cards.Add(new CardDef
{
    Id = "X_ENERGY",
    Name = new LocalizedText("eng", "X Energy"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 0,
    EnergyCostX = true,
    Effects = { new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 5 } },
});

project.Cards.Add(new CardDef
{
    Id = "X_STAR",
    Name = new LocalizedText("eng", "X Star"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Rare,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    StarCostX = true,
    Effects = { new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 5 } },
});

// 最小卡：只验证"每张牌都带重放自有属性"。
project.Cards.Add(new CardDef
{
    Id = "BARE",
    Name = new LocalizedText("eng", "Bare"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects = { new EffectDef { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 6 } },
});

IReadOnlyDictionary<string, string> sources = new CodeGenerator(project).GenerateAll();

string full = sources["Cards/FullCore.cs"];
string xEnergy = sources["Cards/XEnergy.cs"];
string xStar = sources["Cards/XStar.cs"];
string bare = sources["Cards/Bare.cs"];

// ---- 构造器：X 费时传 0，其余照填 ----
Check("构造器带上类型/稀有度/目标",
    full.Contains(": base(2, CardType.Skill, CardRarity.Rare, TargetType.Self)"),
    "构造器签名不符");

// ---- 星辰费用（全新能力：以前从来没被生成过）----
Check("星辰费用发 CanonicalStarCost",
    full.Contains("public override int CanonicalStarCost => 3;"),
    "没有发出星辰费用");
Check("没有星辰费用的卡不发它",
    !bare.Contains("CanonicalStarCost"),
    "不应该发却发了");

// ---- X 费 / X 星 ----
Check("X 费发 HasEnergyCostX 且构造器传 0",
    xEnergy.Contains("protected override bool HasEnergyCostX => true;")
    && xEnergy.Contains(": base(0, CardType.Skill"),
    "X 费的出码不对");
Check("X 星发 HasStarCostX 且不发 CanonicalStarCost",
    xStar.Contains("public override bool HasStarCostX => true;")
    && !xStar.Contains("public override int CanonicalStarCost"),
    "X 星的出码不对——两者都发会自相矛盾");

// 两个 X 开关的**访问修饰符不一样**（游戏就是这么定的）：HasEnergyCostX 是 protected，
// HasStarCostX 是 public。写错会报 CS0507，而"从来不编译 X 星"的话这个错会一直潜伏。
Check("X 费开关是 protected（游戏约定）",
    xEnergy.Contains("protected override bool HasEnergyCostX => true;"),
    "X 费开关的访问修饰符不符");
Check("X 星开关是 public（游戏约定）",
    xStar.Contains("public override bool HasStarCostX => true;")
    && !xStar.Contains("protected override bool HasStarCostX"),
    "X 星开关的访问修饰符不符——游戏里它是 public virtual");

// ---- 关键词 ----
Check("关键词按列表发出",
    full.Contains("CanonicalKeywords => new CardKeyword[] { CardKeyword.Exhaust, CardKeyword.Innate }"),
    "关键词出码不符");

// ---- 升级：能量与星辰各一条，走增量语义 ----
Check("升级改能量费用（增量）", full.Contains("EnergyCost.UpgradeBy(-1);"), "缺能量费用升级");
Check("升级改星辰费用（增量）", full.Contains("UpgradeStarCostBy(-1);"), "缺星辰费用升级");

// 只改星辰的卡也必须有 OnUpgrade——否则填了完全没反应。
var starOnly = new ModProject { Id = "StarOnlyProbe", Name = "Star Only", Version = "1.0.0" };
starOnly.Powers.Add(new PowerDef { Id = "PLACEHOLDER", Name = new LocalizedText("eng", "P"), Kind = PowerKind.Buff });
starOnly.Cards.Add(new CardDef
{
    Id = "STAR_ONLY",
    Name = new LocalizedText("eng", "Star Only"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    StarCost = 2,
    UpgradeStarDelta = -1,
    Effects = { new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 5 } },
});

string starOnlyCard = new CodeGenerator(starOnly).GenerateAll()["Cards/StarOnly.cs"];
Check("只改星辰、不改能量时也发 OnUpgrade",
    starOnlyCard.Contains("protected override void OnUpgrade()")
    && starOnlyCard.Contains("UpgradeStarCostBy(-1);"),
    "只有星辰升级的卡没生成 OnUpgrade —— 填了不会生效");

// ---- 重放：每张牌都带自有存档属性 ----
Console.WriteLine();
Console.WriteLine("== 重放自有属性 ==");

foreach ((string name, string source) in new[] { ("完整核心", full), ("最小卡", bare) })
{
    Check($"{name}：带 [SavedProperty] ReplayCount",
        source.Contains("[SavedProperty]") && source.Contains("public int ReplayCount"),
        "缺重放自有属性");
    Check($"{name}：setter 写进 BaseReplayCount",
        System.Text.RegularExpressions.Regex.IsMatch(
            source, @"public int ReplayCount[\s\S]{0,200}?BaseReplayCount = value;"),
        "setter 没写进 BaseReplayCount");
}

// 标准值只在 AfterCreated 里赋值，且带"还没被赋过值"的守卫
// （读档不经过 AfterCreated，但战斗副本是克隆出来的，无脑覆盖会把带回的值抹掉）。
Check("有标准值的卡在 AfterCreated 里赋值",
    System.Text.RegularExpressions.Regex.IsMatch(
        full, @"public override void AfterCreated\(\)[\s\S]{0,300}?if \(_replayCount == 0\)[\s\S]{0,120}?ReplayCount = 2;"),
    "AfterCreated 里的赋值形状不对");
Check("标准值为 0 的卡不发 AfterCreated",
    !bare.Contains("AfterCreated"),
    "标准值 0 时多发了一个空钩子");

// ---------------------------------------------------------------------------
// 2. 校验器：两种组合必须被拦下
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("== 校验 ==");

List<CardCoreRules.Issue> fullIssues = CardCoreRules.Check(CardCore.From(new CardDef
{
    Id = "X_STAR_UPGRADE",
    StarCostX = true,
    UpgradeStarDelta = -1,
})).ToList();

Check("X 星 + 升级星辰变化 → error",
    fullIssues.Any(i => i.Severity == "error" && i.Message.Contains("star cost X")),
    "没有拦下会抛异常的组合");

List<CardCoreRules.Issue> xCostIssues = CardCoreRules.Check(CardCore.From(new CardDef
{
    Id = "X_ENERGY_UPGRADE",
    EnergyCostX = true,
    UpgradeCostDelta = -1,
})).ToList();

Check("X 费 + 升级费用变化 → warn",
    xCostIssues.Any(i => i.Severity == "warn"),
    "没有提示 X 费的升级费用不会生效");

List<CardCoreRules.Issue> okIssues = CardCoreRules.Check(CardCore.From(new CardDef
{
    Id = "SANE",
    EnergyCost = 1,
    StarCost = 2,
    UpgradeStarDelta = -1,
    ReplayCount = 1,
})).ToList();

Check("正常组合没有 error", !okIssues.Any(i => i.Severity == "error"),
    string.Join(" / ", okIssues.Select(i => i.Message)));

// ---------------------------------------------------------------------------
// 3. 编译：整个探针工程对着游戏程序集编译
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("== 编译 ==");

CompileResult compile = ModCompiler.Compile(sources, gameDir);

foreach (CompileDiagnostic diag in compile.Diagnostics.Where(d => d.Severity == "error").Take(20))
{
    Console.WriteLine($"  [{diag.Severity}] {diag.Id}: {diag.Message}");
}

Check("固有属性矩阵编译通过", compile.Success);

Directory.CreateDirectory(outDir);
File.WriteAllText(Path.Combine(outDir, "full-core.cs"), full.Replace("\r\n", "\n"));

Console.WriteLine($"  已写到 {Path.Combine(outDir, "full-core.cs")}");
Console.WriteLine();
Console.WriteLine($"通过 {passed} 项，失败 {failed} 项。");
Console.WriteLine(failed == 0 ? "RESULT: OK" : "RESULT: FAILED");
return failed == 0 ? 0 : 1;
