using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.ValueSources;
using ModForge.Core.Modules.Variables;

// 变量来源模块验收：① 名单**覆盖每一个枚举成员**（漏一个就红）；② 每个来源的读法与名字
// （玩家维度 / 状态层数 / 生物属性 / 计数 / 不取值 五类各钉住它区别于别类的那些行）；
// ③ **五个角色共用同一套取值**（数值 / 次数 / 张数 / 两个费用阈值各读一次同一个来源）。
//
// 为什么值得单独一个工具：这些文本错了不会编译失败——变量读到另一侧（编译过、行为错）、
// 名字换了一套（界面与描述不一致）、或者某个来源落到兜底分支上当成 0（静默少算）。
// 而这一轮之前，"哪些来源属于玩家维度"是一份三十个枚举项的手写清单，
// 与取值器里的三个分支必须对得上却没有任何东西看着。
//
// 期望全部**取自搬迁前的源码**（Generate/ValueSourceEmitter.cs 与 Model/ValueSource.cs），
// 不是跑一遍新代码抄下来——那样只是循环论证。
//
// 用法: srctest

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

void CheckText(string what, string actual, string expected) =>
    Check(what, actual == expected, $"期望 {expected} / 实际 {actual}");

void CheckSeq(string what, IEnumerable<string> actual, params string[] expected)
{
    List<string> lines = actual.ToList();

    // 拼成一段再放进消息里：内插字符串里再写一层 string.Join(", ") 会让引号提前收尾
    // （C# 的字符串字面量不允许在内插孔里出现同样的引号），这个坑本项目踩过一次。
    string want = string.Join(" | ", expected);
    string got = string.Join(" | ", lines);

    Check(what, lines.SequenceEqual(expected), $"期望 [{want}] / 实际 [{got}]");
}

// ===========================================================================
// 1. 名单覆盖：枚举的每一个成员都要有读法
//
// 这是本模块最要紧的一条断言。以前 enum 加了成员、取值器没跟上的后果是：
// 这个来源落到最后那个 else（生物分支），于是"读能量"变成了"读某个生物的格挡"——
// 编译能过（两个属性都存在）、数值是错的、而且只在用到它的那张牌上体现。
// ===========================================================================
Console.WriteLine("== 名单覆盖 ==");

ValueSourceKind[] kinds = Enum.GetValues<ValueSourceKind>();
List<ValueSourceKind> uncovered = kinds
    .Where(k => k != ValueSourceKind.None && SourceCatalog.Read(k) == SourceReader.None)
    .ToList();

Check($"{kinds.Length} 个来源全部有读法（漏了：{string.Join(", ", uncovered)}）", uncovered.Count == 0);

// 名单里没有重复行：重复会在建索引时抛，但那是运行时才发现；这里先钉住。
List<ValueSourceKind> duplicated = SourceCatalog.All
    .GroupBy(s => s.Kind)
    .Where(g => g.Count() > 1)
    .Select(g => g.Key)
    .ToList();

Check($"声明表里没有重复行（重复：{string.Join(", ", duplicated)}）", duplicated.Count == 0);

// 每个来源的名字两种语言都要有（界面按界面语言取，描述按内容语言取）。
List<string> unnamed = kinds
    .Where(k => SourceCatalog.Label(k, "zh").Length == 0 || SourceCatalog.Label(k, "en").Length == 0)
    .Select(k => k.ToString())
    .ToList();

Check($"每个来源都有中英名字（缺：{string.Join(", ", unnamed)}）", unnamed.Count == 0);

// ===========================================================================
// 2. 玩家维度与"要选状态"两张名单：与搬迁前那份手写清单逐项一致
//
// 搬家前这两件事是 `ValueSource.IsPlayerScoped` 里的三十三个枚举项 + schema 里一句
// `k == PowerStacks`。现在它们从每一行的读法推出来——下面这份期望就是搬走的那份清单，
// 所以它同时证明"推导结果与手写清单一项不差"。
// ===========================================================================
Console.WriteLine();
Console.WriteLine("== 玩家维度 / 需要状态 ==");

HashSet<ValueSourceKind> expectedPlayerScoped = new()
{
    // 玩家资源与 X 值
    ValueSourceKind.Energy, ValueSourceKind.Stars, ValueSourceKind.Gold,
    ValueSourceKind.EnergyX, ValueSourceKind.StarsX, ValueSourceKind.TurnNumber,

    // 牌堆张数
    ValueSourceKind.HandCount, ValueSourceKind.DrawPileCount, ValueSourceKind.DiscardPileCount,
    ValueSourceKind.ExhaustPileCount, ValueSourceKind.AllPilesCount,

    // 战斗历史计数（20 种）
    ValueSourceKind.CardsPlayedThisTurn, ValueSourceKind.CardsPlayedThisCombat,
    ValueSourceKind.CardsDrawnThisTurn, ValueSourceKind.CardsDrawnThisCombat,
    ValueSourceKind.HpLostThisTurn, ValueSourceKind.HpLostThisCombat,
    ValueSourceKind.HitsTakenThisTurn, ValueSourceKind.HitsTakenThisCombat,
    ValueSourceKind.AttacksThisTurn, ValueSourceKind.AttacksThisCombat,
    ValueSourceKind.CardsExhaustedThisTurn, ValueSourceKind.CardsExhaustedThisCombat,
    ValueSourceKind.CardsDiscardedThisTurn, ValueSourceKind.CardsDiscardedThisCombat,
    ValueSourceKind.CardsGeneratedThisTurn, ValueSourceKind.CardsGeneratedThisCombat,
    ValueSourceKind.DamageTakenThisTurn, ValueSourceKind.DamageTakenThisCombat,
    ValueSourceKind.DamageDealtThisTurn, ValueSourceKind.DamageDealtThisCombat,

    // 自建跟踪（4 种）与按条件数牌
    ValueSourceKind.HealsThisTurn, ValueSourceKind.HealsThisCombat,
    ValueSourceKind.HealAmountThisTurn, ValueSourceKind.HealAmountThisCombat,
    ValueSourceKind.FilteredCardCount,
};

CheckText("玩家维度是这 36 种（多/少都算错）",
    expectedPlayerScoped.Count.ToString(), "36");

List<string> scopedMismatch = kinds
    .Where(k => SourceCatalog.IsPlayerScoped(k) != expectedPlayerScoped.Contains(k))
    .Select(k => $"{k}(实际 {SourceCatalog.IsPlayerScoped(k)})")
    .ToList();

string scopedText = string.Join(", ", scopedMismatch);

Check($"玩家维度判定与搬迁前的手写清单一致（不一致：{scopedText}）", scopedMismatch.Count == 0);

List<string> needPower = kinds.Where(SourceCatalog.NeedsPower).Select(k => k.ToString()).ToList();

CheckSeq("只有「状态层数」要再选一个状态", needPower, "PowerStacks");

// 读法分类：玩家维度的那些必须都是"从玩家读"或"计数"，生物属性那四个必须都是生物维度。
CheckSeq("生物属性只有那四种",
    kinds.Where(k => SourceCatalog.Read(k) == SourceReader.CreatureStat).Select(k => k.ToString()),
    "CurrentHp", "MaxHp", "MissingHp", "Block");

List<string> playerReader = kinds
    .Where(k => SourceCatalog.Read(k) == SourceReader.PlayerScoped)
    .Select(k => k.ToString())
    .ToList();

CheckSeq("本模块直接出码的玩家维度（其余计数转计数模块）", playerReader,
    "Energy", "Stars", "Gold", "EnergyX", "StarsX", "TurnNumber");

// 声明表按**枚举顺序**排列（照枚举加成员时不用来回找位置，也不会漏看）。
// 计数类的读法在计数模块里，所以这里与枚举的差集就是那三十种。
CheckSeq("声明表按枚举顺序、只差计数那些",
    SourceCatalog.All.Select(s => s.Kind.ToString()),
    kinds.Where(k => SourceCatalog.Read(k) != SourceReader.Counter)
        .Select(k => k.ToString())
        .ToArray());

CheckText("计数类的读法转给计数模块",
    kinds.Count(k => SourceCatalog.Read(k) == SourceReader.Counter).ToString(), "30");

// ===========================================================================
// 3. 五类读法的取值语句（逐字对照搬迁前的源码）
// ===========================================================================
Console.WriteLine();
Console.WriteLine("== 取值语句 ==");

// 卡牌实例方法里的上下文：Player = this.Owner、自身 = this.Owner.Creature。
SourceEmitter.Context cardCtx = SourceEmitter.Context.ForCard();

// ---- 玩家资源 ----
CheckSeq("玩家资源（当前能量）",
    SourceEmitter.EmitValue(new ValueSource { Kind = ValueSourceKind.Energy }, cardCtx),
    "decimal value = 0m;",
    "var player = this.Owner;",
    "if (player?.PlayerCombatState is not null)",
    "{",
    "    value = player.PlayerCombatState!.Energy;",
    "}");

// ---- X 值：读的是"实际消耗了多少"，走游戏自己的解析方法 ----
CheckSeq("X 值（能量）",
    SourceEmitter.EmitValue(new ValueSource { Kind = ValueSourceKind.EnergyX }, cardCtx),
    "decimal value = 0m;",
    "var player = this.Owner;",
    "if (player?.PlayerCombatState is not null)",
    "{",
    "    value = this.ResolveEnergyXValue();",
    "}");

// 状态里没有"这张牌"，X 值取不到——给一行说明，而不是发出编译不过的代码。
CheckSeq("状态里的 X 值：说明 + 保持 0",
    SourceEmitter.EmitValue(new ValueSource { Kind = ValueSourceKind.StarsX }, SourceEmitter.Context.ForPower()),
    "decimal value = 0m;",
    "// X 值只在卡牌里可用（状态没有对应的实例）。");

// ---- 状态层数：要解析状态类型；取自谁由 Actor 决定 ----
ValueSource stacks = new()
{
    Kind = ValueSourceKind.PowerStacks,
    Actor = ValueSourceActor.Self,
    PowerId = "POWER.STRENGTH_POWER",
};

CheckSeq("状态层数：自身（类型由调用方注入）",
    SourceEmitter.EmitValue(stacks, cardCtx with { PowerTypeFor = _ => "MyMod.Generated.Powers.StrengthPower" }),
    "decimal value = 0m;",
    "var powerOwner = this.Owner.Creature;",
    "if (powerOwner is not null)",
    "{",
    "    value = powerOwner.GetPower<MyMod.Generated.Powers.StrengthPower>()?.Amount ?? 0;",
    "}");

ValueSource stacksOnTarget = new()
{
    Kind = ValueSourceKind.PowerStacks,
    Actor = ValueSourceActor.Target,
    PowerId = "POWER.STRENGTH_POWER",
};

CheckSeq("状态层数：目标（没注入解析器时按原版命名空间猜）",
    SourceEmitter.EmitValue(stacksOnTarget, cardCtx),
    "decimal value = 0m;",
    "var powerOwner = target;",
    "if (powerOwner is not null)",
    "{",
    "    value = powerOwner.GetPower<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>()?.Amount ?? 0;",
    "}");

CheckSeq("状态层数：没选状态就取 0（校验器已报错，这里不发坏代码）",
    SourceEmitter.EmitValue(new ValueSource { Kind = ValueSourceKind.PowerStacks }, cardCtx),
    "decimal value = 0m;",
    "// 选了\"状态层数\"却没选是哪个状态，按 0 处理。",
    "value = 0m;");

// ---- 生物属性：表达式直接对着那个生物写，局部量只用于判空 ----
CheckSeq("生物属性：自身格挡",
    SourceEmitter.EmitValue(new ValueSource { Kind = ValueSourceKind.Block }, cardCtx),
    "decimal value = 0m;",
    "var who = this.Owner.Creature;",
    "if (who is not null)",
    "{",
    "    value = this.Owner.Creature!.Block;",
    "}");

CheckSeq("生物属性：目标已损失生命（两个属性相减）",
    SourceEmitter.EmitValue(
        new ValueSource { Kind = ValueSourceKind.MissingHp, Actor = ValueSourceActor.Target }, cardCtx),
    "decimal value = 0m;",
    "var who = target;",
    "if (who is not null)",
    "{",
    "    value = (target!.MaxHp - target!.CurrentHp);",
    "}");

// ---- 计数：读法在计数模块里（本模块只转交） ----
CheckSeq("计数（手牌数）走计数模块",
    SourceEmitter.EmitValue(new ValueSource { Kind = ValueSourceKind.HandCount }, cardCtx),
    "decimal value = 0m;",
    "var player = this.Owner;",
    "if (player?.PlayerCombatState is not null)",
    "{",
    "    value = player.PlayerCombatState!.Hand.Cards.Count;",
    "}");

// ---- 不取值：变量是 0（结果等于基础值本身） ----
CheckSeq("没选来源：保持 0",
    SourceEmitter.EmitValue(new ValueSource(), cardCtx),
    "decimal value = 0m;",
    "// 没有选变量来源，按 0 处理。");

// ---- Emit 比 EmitValue 多一行 return（就地计算的取值方法要用它）----
CheckSeq("Emit 末尾收尾",
    SourceEmitter.Emit(new ValueSource { Kind = ValueSourceKind.CurrentHp }, cardCtx).TakeLast(2),
    "}",
    "return value;");

CheckText("Emit 的返回值",
    SourceEmitter.Emit(new ValueSource { Kind = ValueSourceKind.Gold }, cardCtx).Last(), "return value;");

// 结果变量名由调用方给：同一个来源被五个角色各读一次时，各自的局部量互不重名。
CheckSeq("结果变量名可换（选牌张数用 pickCount）",
    SourceEmitter.EmitValue(new ValueSource { Kind = ValueSourceKind.Gold }, cardCtx, "pickCount"),
    "decimal pickCount = 0m;",
    "var player = this.Owner;",
    "if (player?.PlayerCombatState is not null)",
    "{",
    "    pickCount = player.Gold;",
    "}");

// ===========================================================================
// 4. 名字与简短说明
// ===========================================================================
Console.WriteLine();
Console.WriteLine("== 名字 ==");

CheckText("不取值叫「无」", SourceCatalog.Label(ValueSourceKind.None, "zh"), "无");
CheckText("状态层数是泛指说法（具体状态由 PowerId 给）",
    SourceCatalog.Label(ValueSourceKind.PowerStacks, "zh"), "状态层数");
CheckText("X 值与「当前能量」分开命名", SourceCatalog.Label(ValueSourceKind.EnergyX, "zh"), "X 值（能量）");
CheckText("英文名", SourceCatalog.Label(ValueSourceKind.TurnNumber, "en"), "turn number");

// 计数类的名字来自计数模块（那是"这个计数叫什么"的唯一出处）。
CheckText("计数名来自计数模块（zh）", SourceCatalog.Label(ValueSourceKind.HandCount, "zh"), "手牌数");
CheckText("计数名来自计数模块（en）", SourceCatalog.Label(ValueSourceKind.HandCount, "en"), "cards in hand");

// 简短说明：玩家维度不写"自身/目标"，生物属性才写。
CheckText("简短说明：玩家维度不带 actor",
    SourceCatalog.Describe(new ValueSource { Kind = ValueSourceKind.HandCount }), "手牌数");

CheckText("简短说明：生物属性带 actor",
    SourceCatalog.Describe(new ValueSource
    {
        Kind = ValueSourceKind.Block,
        Actor = ValueSourceActor.Target,
        PerUnit = 2,
    }),
    "2 × 目标格挡");

CheckText("简短说明：英文语序",
    SourceCatalog.Describe(
        new ValueSource { Kind = ValueSourceKind.Block, Actor = ValueSourceActor.Target }, "en"),
    "the target's Block");

// ===========================================================================
// 5. 五个角色共用同一套取值
//
// "获取计算型数值的变量"这件事有五个角色：效果数值、重复次数、选牌张数、能量阈值、星辰阈值。
// 它们以前各自去找取值器，现在都走本模块——**同一个来源在五处的读法必须一字不差**，
// 只有结果变量的名字不同（各自的局部量）。这里用一张卡把它们全放上，按各自的变量名找那一行。
// ===========================================================================
Console.WriteLine();
Console.WriteLine("== 五个角色共用同一套取值 ==");

var project = new ModProject { Id = "SrcProbe", Name = "Src Probe", Version = "1.0.0" };

project.Powers.Add(new PowerDef
{
    Id = "MARK",
    Name = new LocalizedText("eng", "Mark"),
    Kind = PowerKind.Buff,
});

var source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount };

var probe = new CardDef
{
    Id = "SRC_ROLES",
    Name = new LocalizedText("eng", "Src Roles"),
    Description = new LocalizedText("eng", "Test."),
    Pool = "ironclad",
    Type = CardType.Attack,
    Rarity = CardRarity.Common,
    Target = CardTargetType.AnyEnemy,
    EnergyCost = 1,
    Effects =
    {
        // 数值（计算型的伤害走游戏变量，取值语句在静态的乘数方法里）
        new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 0,
            Source = source.Clone(),
        },

        // 重复次数
        new EffectDef
        {
            Kind = EffectKind.GainBlock,
            Target = EffectTarget.Self,
            Amount = 0,
            RepeatSource = source.Clone(),
        },

        // 选牌张数
        new EffectDef
        {
            Kind = EffectKind.UpgradeCardsInPile,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = new CardPick
            {
                Piles = { PileKind.Hand },
                Mode = PickMode.Choose,
                Count = new NumericValue(1m, source.Clone()),
            },
        },

        // 两个费用阈值
        new EffectDef
        {
            Kind = EffectKind.ExhaustCards,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = new CardPick
            {
                Piles = { PileKind.Hand },
                EnergyComparison = CostComparison.GreaterThan,
                StarComparison = CostComparison.AtMost,
            },
            EnergySource = source.Clone(),
            StarSource = source.Clone(),
        },
    },
};

project.Cards.Add(probe);

string code = new CodeGenerator(project).GenerateAll()[$"Cards/{project.CardTypeName(probe)}.cs"];
string[] lines = code.Split('\n').Select(l => l.Trim()).ToArray();

// 同一个来源的读数：五个角色各读一次，读法必须一字不差。
//
// 结果变量名分两档：效果数值 / 重复次数 / 张数各自就地铺在方法体里，所以各有各的局部量名
// （value / sourceValue / pickCount）；两个**费用阈值**例外——它们发成一个方法
// （`CalcPick{Energy|Star}{n}`），因为同一张卡上可能有多条效果在筛费用，就地铺开会让各自的
// `player` 在同一个方法里撞名（CS0136，编译不过）。所以那两个角色读到的局部量是方法自己的
// `value`——**与"效果数值"那两行逐字相同**，正是"同一个来源同一套读法"的体现。
foreach ((string role, string variable) in new[]
{
    ("效果数值", "value"),
    ("重复次数", "sourceValue"),
    ("选牌张数", "pickCount"),
    ("能量阈值", "value"),
    ("星辰阈值", "value"),
})
{
    string read = $"{variable} = player.PlayerCombatState!.Hand.Cards.Count;";

    Check($"{role}：读了同一个来源（{read}）", lines.Contains(read), "生成的代码里没有这一行");
    Check($"{role}：结果变量是 {variable}",
        lines.Contains($"decimal {variable} = 0m;"), "没有这一行的声明");
}

// 两个费用阈值各发了一个取值方法（名字里带角色）。
Check("能量阈值：发成取值方法 CalcPickEnergy",
    lines.Any(l => l.StartsWith("private decimal CalcPickEnergy", StringComparison.Ordinal)),
    "没有生成能量阈值的取值方法");

Check("星辰阈值：发成取值方法 CalcPickStar",
    lines.Any(l => l.StartsWith("private decimal CalcPickStar", StringComparison.Ordinal)),
    "没有生成星辰阈值的取值方法");

Console.WriteLine();

if (failed > 0)
{
    Console.WriteLine($"通过 {passed} 项，失败 {failed} 项。");
    Console.WriteLine("RESULT: FAILED");
    return 1;
}

Console.WriteLine($"通过 {passed} 项，失败 0 项。");
Console.WriteLine("RESULT: OK");
return 0;
