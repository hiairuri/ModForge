using System.Reflection;
using System.Runtime.Loader;
using ModForge.Core;
using ModForge.Core.Compile;
using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Variables;
using ModForge.Core.Modules.Counting;

// 计数模块验收：① 三十种计数的读表达式；② **真两 mod 共用**（宿主 / 转发）；
// ③ 不用计数的工程不引入共享运行时；④ 跨 mod 按方法名识别。
//
// 为什么 ② 值得真跑一遍：共享的做法是"先加载的当宿主、后加载的转发"，而这件事只有
// **把两个程序集装进同一个 ALC、各自 Adopt 一次**才能真正验证。光看生成出来的源码看不出
// 它到底能不能找到对方。所以这里编两个 mod、装进同一个上下文、反射调一遍，断言
// 第二个找到的是第一个的实现。
//
// 用法: counttest <gameDir>

string gameDir = args.Length > 0
    ? args[0]
    : @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2";

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

// ===========================================================================
// 1. 三十种计数的读表达式
//
// 期望取自**搬迁前的源码**（不是跑一遍新代码抄下来）——那样才是回归断言而不是循环论证。
// 每种只钉住它**区别于其它计数**的那一段，读起来也就知道它到底数的是什么。
// ===========================================================================
Console.WriteLine("== 计数的读表达式 ==");

var project = new ModProject { Id = "CountProbe", Name = "Count Probe", Version = "1.0.0" };

project.Powers.Add(new PowerDef
{
    Id = "MARK",
    Name = new LocalizedText("eng", "Mark"),
    Kind = PowerKind.Buff,
});

// 一种计数一张牌，取它的生成源码。键用**唯一的名字**——按条件数牌有五个范围、种类相同，
// 按种类做键的话后一个会覆盖前一个（第一版就是这么写的，于是五个范围都在查同一张牌）。
var probes = new Dictionary<string, string>(StringComparer.Ordinal);

void Probe(string key, ValueSourceKind kind, Action<ValueSource>? tweak = null)
{
    string id = "CN_" + key.ToUpperInvariant();
    var source = new ValueSource { Actor = ValueSourceActor.Self, Kind = kind, PerUnit = 1 };

    tweak?.Invoke(source);

    var card = new CardDef
    {
        Id = id,
        Name = new LocalizedText("eng", id),
        Description = new LocalizedText("eng", "Test."),
        Pool = "ironclad",
        Type = CardType.Attack,
        Rarity = CardRarity.Common,
        Target = CardTargetType.AnyEnemy,
        EnergyCost = 1,
        Effects =
        {
            new EffectDef { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 0, Source = source },
        },
    };

    project.Cards.Add(card);

    // 文件路径用的是**生成的类型名**（Pascal 化），不是 id。
    probes[key] = $"Cards/{project.CardTypeName(card)}.cs";
}

// ---- 战斗历史（20 种）----
var historyCounts = new (ValueSourceKind Kind, string Entry, string Filter, bool ThisTurn, bool Finished)[]
{
    (ValueSourceKind.CardsPlayedThisTurn, "CardPlayFinished", "e.CardPlay.Card.Owner == player", true, true),
    (ValueSourceKind.CardsPlayedThisCombat, "CardPlayFinished", "e.CardPlay.Card.Owner == player", false, true),
    (ValueSourceKind.CardsDrawnThisTurn, "CardDrawnEntry", "e.Actor == player.Creature && !e.FromHandDraw", true, false),
    (ValueSourceKind.CardsDrawnThisCombat, "CardDrawnEntry", "e.Actor == player.Creature && !e.FromHandDraw", false, false),
    (ValueSourceKind.HpLostThisTurn, "DamageReceivedEntry", "e.Receiver == player.Creature && e.Result.UnblockedDamage > 0", true, false),
    (ValueSourceKind.HpLostThisCombat, "DamageReceivedEntry", "e.Receiver == player.Creature && e.Result.UnblockedDamage > 0", false, false),
    (ValueSourceKind.HitsTakenThisTurn, "DamageReceivedEntry", "e.Receiver == player.Creature", true, false),
    (ValueSourceKind.HitsTakenThisCombat, "DamageReceivedEntry", "e.Receiver == player.Creature", false, false),
    (ValueSourceKind.AttacksThisTurn, "CreatureAttackedEntry", "e.Actor == player.Creature", true, false),
    (ValueSourceKind.AttacksThisCombat, "CreatureAttackedEntry", "e.Actor == player.Creature", false, false),
    (ValueSourceKind.CardsExhaustedThisTurn, "CardExhaustedEntry", "e.Card.Owner == player", true, false),
    (ValueSourceKind.CardsExhaustedThisCombat, "CardExhaustedEntry", "e.Card.Owner == player", false, false),
    (ValueSourceKind.CardsDiscardedThisTurn, "CardDiscardedEntry", "e.Card.Owner == player", true, false),
    (ValueSourceKind.CardsDiscardedThisCombat, "CardDiscardedEntry", "e.Card.Owner == player", false, false),
    (ValueSourceKind.CardsGeneratedThisTurn, "CardGeneratedEntry", "e.Creator == player", true, false),
    (ValueSourceKind.CardsGeneratedThisCombat, "CardGeneratedEntry", "e.Creator == player", false, false),
};

foreach ((ValueSourceKind kind, string entry, string filter, bool thisTurn, bool finished) in historyCounts)
{
    Probe(kind.ToString(), kind);
}

var historySums = new (ValueSourceKind Kind, string Filter, bool ThisTurn)[]
{
    (ValueSourceKind.DamageTakenThisTurn, "e.Receiver == player.Creature", true),
    (ValueSourceKind.DamageTakenThisCombat, "e.Receiver == player.Creature", false),
    (ValueSourceKind.DamageDealtThisTurn, "e.Dealer == player.Creature", true),
    (ValueSourceKind.DamageDealtThisCombat, "e.Dealer == player.Creature", false),
};

foreach ((ValueSourceKind kind, string filter, bool thisTurn) in historySums)
{
    Probe(kind.ToString(), kind);
}

// ---- 牌堆（5 种）与按条件数牌（5 个范围）----
// 键一律用**枚举名**（查询那边按 SourceOfKind(kind) 取），只有按条件数牌那五个
// 种类相同、必须各用一个带范围的键。
Probe(nameof(ValueSourceKind.HandCount), ValueSourceKind.HandCount);
Probe(nameof(ValueSourceKind.DrawPileCount), ValueSourceKind.DrawPileCount);
Probe(nameof(ValueSourceKind.DiscardPileCount), ValueSourceKind.DiscardPileCount);
Probe(nameof(ValueSourceKind.ExhaustPileCount), ValueSourceKind.ExhaustPileCount);
Probe(nameof(ValueSourceKind.AllPilesCount), ValueSourceKind.AllPilesCount);

foreach (CountScope scope in Enum.GetValues<CountScope>())
{
    // 一条维度（按标签）+ 范围：谓词那部分与选牌模块同一个生成处。
    Probe("Filter" + scope, ValueSourceKind.FilteredCardCount, s =>
    {
        s.CountScope = scope;
        s.Filter.Tags.Add(CardTag.Strike);
    });
}

// ---- 按条件数牌：多维并存 + 两个费用维度（阈值是筛选自己的常数）----
Probe("FilterMulti", ValueSourceKind.FilteredCardCount, s =>
{
    s.CountScope = CountScope.Hand;
    s.Filter.Rarities.Add(CardRarity.Rare);
    s.Filter.Types.Add(CardType.Attack);
    s.Filter.EnergyComparison = CostComparison.GreaterThan;
    s.Filter.EnergyThreshold = 2;
    s.Filter.StarComparison = CostComparison.AtLeast;
    s.Filter.StarThreshold = 0;
});

// 同一维多个取值之间是"或"，要套括号（与选牌模块同一形状）。
Probe("FilterOr", ValueSourceKind.FilteredCardCount, s =>
{
    s.CountScope = CountScope.DrawPile;
    s.Filter.Rarities.Add(CardRarity.Common);
    s.Filter.Rarities.Add(CardRarity.Uncommon);
});

// 一个维度都没筛：整堆都算（不带 Count(c => …)）。
Probe("FilterNone", ValueSourceKind.FilteredCardCount, s => s.CountScope = CountScope.ExhaustPile);

// ---- 自建跟踪（4 种）----
Probe(nameof(ValueSourceKind.HealsThisTurn), ValueSourceKind.HealsThisTurn);
Probe(nameof(ValueSourceKind.HealsThisCombat), ValueSourceKind.HealsThisCombat);
Probe(nameof(ValueSourceKind.HealAmountThisTurn), ValueSourceKind.HealAmountThisTurn);
Probe(nameof(ValueSourceKind.HealAmountThisCombat), ValueSourceKind.HealAmountThisCombat);

IReadOnlyDictionary<string, string> sources = new CodeGenerator(project).GenerateAll();

string SourceOf(string key) => sources[probes[key]];
string SourceOfKind(ValueSourceKind kind) => SourceOf(kind.ToString());

// 历史：数次数 —— 出牌走 CardPlaysFinished，其余走 Entries.OfType<T>()
foreach ((ValueSourceKind kind, string entry, string filter, bool thisTurn, bool finished) in historyCounts)
{
    string src = SourceOfKind(kind);

    string expected = finished
        ? $"CombatManager.Instance.History.CardPlaysFinished.Count(e => {filter}"
        : $"CombatManager.Instance.History.Entries.OfType<{entry}>().Count(e => {filter}";

    Check($"{kind}：数次数用对的记录源", src.Contains(expected), expected);

    // "本回合"的多一条 HappenedThisTurn 条件——这是两套口径唯一的差别。
    if (thisTurn)
    {
        Check($"{kind}：限定本回合", src.Contains("&& e.HappenedThisTurn(player.Creature.CombatState)"));
    }
}

// 历史：求总量 —— 先 Where 筛、再 Sum
foreach ((ValueSourceKind kind, string filter, bool thisTurn) in historySums)
{
    string src = SourceOfKind(kind);

    Check($"{kind}：求总量（Where + Sum）",
        src.Contains($"CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>().Where(e => {filter}")
        && src.Contains(".Sum(e => e.Result.UnblockedDamage)"));
}

// 牌堆
Check("手牌数", SourceOfKind(ValueSourceKind.HandCount).Contains("player.PlayerCombatState!.Hand.Cards.Count"));
Check("抽牌堆张数", SourceOfKind(ValueSourceKind.DrawPileCount).Contains("player.PlayerCombatState!.DrawPile.Cards.Count"));
Check("弃牌堆张数", SourceOfKind(ValueSourceKind.DiscardPileCount).Contains("player.PlayerCombatState!.DiscardPile.Cards.Count"));
Check("消耗堆张数", SourceOfKind(ValueSourceKind.ExhaustPileCount).Contains("player.PlayerCombatState!.ExhaustPile.Cards.Count"));
Check("所有牌堆张数（含出牌堆）", SourceOfKind(ValueSourceKind.AllPilesCount).Contains("player.PlayerCombatState!.AllCards.Count()"));

// 按条件数牌：五个范围各一个表达式
var filterScopes = new (CountScope Scope, string Expr)[]
{
    (CountScope.Hand, "player.PlayerCombatState!.Hand.Cards"),
    (CountScope.DrawPile, "player.PlayerCombatState!.DrawPile.Cards"),
    (CountScope.DiscardPile, "player.PlayerCombatState!.DiscardPile.Cards"),
    (CountScope.ExhaustPile, "player.PlayerCombatState!.ExhaustPile.Cards"),
    (CountScope.AllPiles, "player.PlayerCombatState!.AllCards"),
};

foreach ((CountScope scope, string expr) in filterScopes)
{
    string src = SourceOf("Filter" + scope);
    Check($"按条件数牌（{scope}）", src.Contains($"{expr}.Count(c => c.Tags.Contains(CardTag.Strike))"));
}

// 多维并存：维度之间"与"、维度内部"或"，两个费用维度比的是筛选自己的阈值。
Check("按条件数牌：多维并存（与 + 内层或 + 两个费用阈值）",
    SourceOf("FilterMulti").Contains(
        "player.PlayerCombatState!.Hand.Cards.Count(c => "
        + "c.Rarity == CardRarity.Rare && c.Type == CardType.Attack && "
        + "c.EnergyCost.Canonical > 2 && c.CanonicalStarCost >= 0)"));

Check("按条件数牌：同维多值是「或」、要套括号",
    SourceOf("FilterOr").Contains(
        "player.PlayerCombatState!.DrawPile.Cards.Count(c => "
        + "(c.Rarity == CardRarity.Common || c.Rarity == CardRarity.Uncommon))"));

Check("按条件数牌：一个维度都没筛就整堆都算（不发 Count(c => …)）",
    SourceOf("FilterNone").Contains("player.PlayerCombatState!.ExhaustPile.Cards")
    && !SourceOf("FilterNone").Contains("Count(c =>"));

// 自建跟踪：查我们的跟踪器
Check("恢复生命次数（本回合）", SourceOfKind(ValueSourceKind.HealsThisTurn)
    .Contains("ModForge.Generated.Runtime.HealTracker.HealsThisTurn(player.Creature)"));
Check("恢复生命次数（本场战斗）", SourceOfKind(ValueSourceKind.HealsThisCombat)
    .Contains("ModForge.Generated.Runtime.HealTracker.HealsThisCombat(player.Creature)"));
Check("恢复生命量（本回合）", SourceOfKind(ValueSourceKind.HealAmountThisTurn)
    .Contains("ModForge.Generated.Runtime.HealTracker.HealAmountThisTurn(player.Creature)"));
Check("恢复生命量（本场战斗）", SourceOfKind(ValueSourceKind.HealAmountThisCombat)
    .Contains("ModForge.Generated.Runtime.HealTracker.HealAmountThisCombat(player.Creature)"));

// 名单自洽：30 种，且"哪些是计数"只有一个出处
Check($"计数名单共 30 种（实际 {CounterCatalog.All.Count}）", CounterCatalog.All.Count == 30);
Check("名单里每种都能按种类查到", CounterCatalog.All.All(c => CounterCatalog.Find(c.Kind)?.Kind == c.Kind));
Check("读当前状态的来源**不是**计数（它们在变量来源模块里）",
    !CounterCatalog.IsCounter(ValueSourceKind.CurrentHp)
    && !CounterCatalog.IsCounter(ValueSourceKind.Block)
    && !CounterCatalog.IsCounter(ValueSourceKind.Energy)
    && !CounterCatalog.IsCounter(ValueSourceKind.TurnNumber)
    && !CounterCatalog.IsCounter(ValueSourceKind.PowerStacks));
Check("自建跟踪只有恢复生命那 4 种",
    CounterCatalog.All.Count(c => c.Source == CounterSource.SelfTracked) == 4);

// ===========================================================================
// 2. 共享运行时：只有用到计数（带状态的件）才引入
// ===========================================================================
Console.WriteLine();
Console.WriteLine("== 共享运行时 ==");

Check("用到计数：发 SharedRuntime.cs", sources.ContainsKey("Runtime/SharedRuntime.cs"));
Check("用到计数：发 HealTracker.cs", sources.ContainsKey("Runtime/HealTracker.cs"));
Check("用到计数：ModEntry 里先 Adopt 再打补丁",
    sources["ModEntry.cs"].Contains("SharedRuntime.Adopt();"));
Check("Adopt 排在 PatchAll 之前（不是宿主的话记账要跳过）",
    sources["ModEntry.cs"].IndexOf("SharedRuntime.Adopt();", StringComparison.Ordinal)
    < sources["ModEntry.cs"].IndexOf(".PatchAll(", StringComparison.Ordinal),
    // 搜 ".PatchAll(" 而不是 "PatchAll"：注释里也出现了这个词（搜字符串撞注释是常犯的错）。
    $"Adopt@{sources["ModEntry.cs"].IndexOf("SharedRuntime.Adopt();", StringComparison.Ordinal)}"
    + $" PatchAll@{sources["ModEntry.cs"].IndexOf(".PatchAll(", StringComparison.Ordinal)}");

// 不用计数的工程：一个字节都不该变（没有任何共享运行时的痕迹）。
var plain = new ModProject { Id = "PlainProbe", Name = "Plain", Version = "1.0.0" };
plain.Powers.Add(new PowerDef { Id = "MARK", Name = new LocalizedText("eng", "Mark"), Kind = PowerKind.Buff });
plain.Cards.Add(new CardDef
{
    Id = "PLAIN",
    Name = new LocalizedText("eng", "Plain"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects = { new EffectDef { Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 5 } },
});

IReadOnlyDictionary<string, string> plainSources = new CodeGenerator(plain).GenerateAll();

Check("不用计数：不发 SharedRuntime.cs", !plainSources.ContainsKey("Runtime/SharedRuntime.cs"));
Check("不用计数：不发 HealTracker.cs", !plainSources.ContainsKey("Runtime/HealTracker.cs"));
Check("不用计数：ModEntry 里没有 Adopt", !plainSources["ModEntry.cs"].Contains("SharedRuntime.Adopt"));

// ===========================================================================
// 3. 跨 mod：按方法名识别（接口是每 mod 一份，所以类型判定跨不过去）
// ===========================================================================
Console.WriteLine();
Console.WriteLine("== 跨 mod 识别 ==");

var boostProject = new ModProject { Id = "BoostProbe", Name = "Boost", Version = "1.0.0" };
boostProject.Powers.Add(new PowerDef { Id = "MARK", Name = new LocalizedText("eng", "Mark"), Kind = PowerKind.Buff });
boostProject.Cards.Add(new CardDef
{
    Id = "BOOSTED",
    Name = new LocalizedText("eng", "Boosted"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Skill,
    Rarity = CardRarity.Common,
    Target = CardTargetType.Self,
    EnergyCost = 1,
    Effects =
    {
        new EffectDef
        {
            Kind = EffectKind.BoostCardDamage,
            Target = EffectTarget.Self,
            Amount = 2,
            Pick = new CardPick { Source = PickSource.ThisCard },
            CostDuration = CostDuration.Permanent,
        },
        new EffectDef
        {
            Kind = EffectKind.AddReplay,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = new CardPick { Source = PickSource.ThisCard },
            CostDuration = CostDuration.Permanent,
        },
    },
});

IReadOnlyDictionary<string, string> boostSources = new CodeGenerator(boostProject).GenerateAll();

Check("永久增伤：按方法名找（跨 mod 也能认）",
    boostSources["Runtime/DamageBoost.cs"].Contains("nameof(IPermanentDamageBoost.ApplyPermanentDamageBoost)")
    && boostSources["Runtime/DamageBoost.cs"].Contains("GetMethod"));

Check("永久重放：按方法名找", boostSources["Runtime/ReplayBoost.cs"]
    .Contains("nameof(IAddReplay.ApplyPermanentReplay)"));

Check("两个辅助都保留同 mod 的接口快路径（不走反射）",
    boostSources["Runtime/DamageBoost.cs"].Contains("card is IPermanentDamageBoost")
    && boostSources["Runtime/ReplayBoost.cs"].Contains("card is IAddReplay"));

// ===========================================================================
// 4. 真两 mod 共用：编两个 mod、装进同一个 ALC、各自 Adopt 一次
// ===========================================================================
Console.WriteLine();
Console.WriteLine("== 两 mod 共用（真编译 + 真加载）==");

string dataDir = Path.Combine(gameDir, "data_sts2_windows_x86_64");
if (!Directory.Exists(dataDir))
{
    dataDir = gameDir;
}

ModProject CounterMod(string id)
{
    var mod = new ModProject { Id = id, Name = id, Version = "1.0.0" };

    mod.Powers.Add(new PowerDef { Id = "MARK", Name = new LocalizedText("eng", "Mark"), Kind = PowerKind.Buff });
    mod.Cards.Add(new CardDef
    {
        Id = "HEAL_READER",
        Name = new LocalizedText("eng", "Heal Reader"),
        Description = new LocalizedText("eng", "Test."),
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
                Amount = 0,
                Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HealsThisCombat },
            },
        },
    });

    return mod;
}

// 程序集名按工程 id 取唯一名——两个 mod 会被装进**同一个** ALC，
// 同名会造成按名解析的歧义（共享判定正是按类型名找另一份实现）。
CompileResult first = ModCompiler.Compile(new CodeGenerator(CounterMod("CountA")).GenerateAll(), gameDir, "ModForge.CountA");
CompileResult second = ModCompiler.Compile(new CodeGenerator(CounterMod("CountB")).GenerateAll(), gameDir, "ModForge.CountB");

if (first.Assembly is null || second.Assembly is null)
{
    Console.WriteLine("  [失败] 探针 mod 没编译出来");
    foreach (CompileDiagnostic d in first.Diagnostics.Concat(second.Diagnostics).Where(d => d.Severity == "error").Take(10))
    {
        Console.WriteLine($"         {d.Id}: {d.Message}");
    }

    Console.WriteLine();
    Console.WriteLine($"通过 {passed} 项，失败 {failed + 1} 项。");
    Console.WriteLine("RESULT: FAILED");
    return 1;
}

var alc = new AssemblyLoadContext("counttest", isCollectible: true);
alc.Resolving += (ctx, name) =>
{
    string candidate = Path.Combine(dataDir, name.Name + ".dll");
    return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
};

// 用内存里的字节加载（不落盘）——两个名字不同的程序集装进同一个上下文。
Assembly asmA = alc.LoadFromStream(new MemoryStream(first.Assembly));
Assembly asmB = alc.LoadFromStream(new MemoryStream(second.Assembly));

Check("两个 mod 装进同一个 ALC（不同程序集名）", asmA != asmB);

Type sharedA = asmA.GetType("ModForge.Generated.Runtime.SharedRuntime")!;
Type sharedB = asmB.GetType("ModForge.Generated.Runtime.SharedRuntime")!;
Type trackerA = asmA.GetType("ModForge.Generated.Runtime.HealTracker")!;
Type trackerB = asmB.GetType("ModForge.Generated.Runtime.HealTracker")!;

Check("两个 mod 都带共享运行时", sharedA is not null && sharedB is not null && trackerA is not null && trackerB is not null);

// 两个都已经加载了才各自 Adopt —— 这是**更难**的顺序（现实里游戏是逐个加载并初始化的，
// 所以先加载的那个 Adopt 时另一个还没进来）。宿主的选定必须与顺序无关，否则两个程序集
// 都已加载时会互相找到、双双变成非宿主，计数就永远读到 0。
//
// 这里先让 B Adopt（它会看到 A，而按程序集名 A 更小 → B 不当宿主），再让 A Adopt（当宿主）。
sharedB.GetMethod("Adopt")!.Invoke(null, null);

bool bIsHost = (bool)sharedB.GetProperty("IsHost")!.GetValue(null)!;
object? bHost = sharedB.GetProperty("Host")!.GetValue(null);

Check("后加载的**不**当宿主（转发给宿主）", !bIsHost);
Check("后加载的找到的是**先加载那份**的实现", ReferenceEquals(bHost, trackerA),
    bHost is null ? "没找到宿主" : $"找到的是 {((Type)bHost).Assembly.GetName().Name}");

sharedA.GetMethod("Adopt")!.Invoke(null, null);

Check("先加载的当宿主", (bool)sharedA.GetProperty("IsHost")!.GetValue(null)!);
Check("先加载的仍然是宿主（不会被后来者顶掉）",
    (bool)sharedA.GetProperty("IsHost")!.GetValue(null)!);

// 转发真的能用：B 的查询走 A 的实现（creature 传 null → 走 A 的 Sum 直接返回 0）。
object? routed = trackerB.GetMethod("HealsThisCombat")!.Invoke(null, new object?[] { null });
Check("后加载的查询能转发给宿主（调用不抛、返回 0）", routed is int and 0, $"返回 {routed}");

// 宿主自己也算得出来（同样的入参）。
object? direct = trackerA.GetMethod("HealsThisCombat")!.Invoke(null, new object?[] { null });
Check("宿主自己的查询也正常", direct is int and 0);

Console.WriteLine();
Console.WriteLine($"通过 {passed} 项，失败 {failed} 项。");
Console.WriteLine(failed == 0 ? "RESULT: OK" : "RESULT: FAILED");
return failed == 0 ? 0 : 1;
