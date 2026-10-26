using ModForge.Core;
using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Variables;
using ModForge.Core.Modules.Timing;

// 时机模块验收：断言**规划出来的钩子形状**与**发出去的语句**。
//
// 为什么值得单独一个工具：时机的每条规则都是"改错了不会报错、只是不触发"的那种——
//   * "打出后"与"打出一张牌时"落到同一个游戏钩子，必须合成一个覆写；
//   * "回合结束时 + 若在手牌中"要换成另一个钩子（用错那个永远不成立）；
//   * "自动打出这张牌"走自动打出的相位钩子（混进普通回合钩子会无限递归）；
//   * ShouldReceiveCombatHooks 要按全部依赖派发的效果算出来（漏算 = 钩子永不派发）。
//
// 工具里还钉住两处**容易写错、写错就编译不过**的细节：各钩子的访问修饰符
// （OnPlay / OnTurnEndInHand 是 protected，其余 public）与多行签名的括号位置。
//
// 用法: timingtest

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

// 造一张只带指定效果的卡，取它生成出来的源码。
string CardSource(params EffectDef[] effects)
{
    var project = new ModProject { Id = "TimingProbe", Name = "Timing Probe", Version = "1.0.0" };

    project.Powers.Add(new PowerDef
    {
        Id = "MARK",
        Name = new LocalizedText("eng", "Mark"),
        Kind = PowerKind.Buff,
    });

    project.Cards.Add(new CardDef
    {
        Id = "PROBE",
        Name = new LocalizedText("eng", "Probe"),
        Description = new LocalizedText("eng", "Test."),
        Pool = "ironclad",
        Type = CardType.Skill,
        Rarity = CardRarity.Common,
        Target = CardTargetType.Self,
        EnergyCost = 1,
        Effects = effects.ToList(),
    });

    return new CodeGenerator(project).GenerateAll()["Cards/Probe.cs"];
}

static EffectDef Block(EffectTrigger trigger, Action<EffectDef>? tweak = null)
{
    var effect = new EffectDef
    {
        Kind = EffectKind.GainBlock,
        Target = EffectTarget.Self,
        Amount = 3,
        Trigger = trigger,
    };

    tweak?.Invoke(effect);
    return effect;
}

// ===========================================================================
// 1. 规划：哪些效果落到哪个钩子上
// ===========================================================================
Console.WriteLine("== 规划 ==");

// 八种时机都要被分到对应的组里（默认 OnPlay 也算一组）。
foreach ((EffectTrigger trigger, Func<CardTimingPlan, IReadOnlyList<EffectDef>> pick, string group) in new (EffectTrigger, Func<CardTimingPlan, IReadOnlyList<EffectDef>>, string)[]
{
    (EffectTrigger.OnPlay, p => p.OnPlay, "OnPlay"),
    (EffectTrigger.AfterPlayed, p => p.AfterPlayed, "AfterPlayed"),
    (EffectTrigger.CardPlayed, p => p.CardPlayed, "CardPlayed"),
    (EffectTrigger.OnDraw, p => p.OnDraw, "OnDraw"),
    (EffectTrigger.OnDiscard, p => p.OnDiscard, "OnDiscard"),
    (EffectTrigger.OnExhaust, p => p.OnExhaust, "OnExhaust"),
    (EffectTrigger.StartOfTurn, p => p.StartOfTurn, "StartOfTurn"),
    (EffectTrigger.EndOfTurn, p => p.EndOfTurn, "EndOfTurn"),
})
{
    CardTimingPlan plan = TimingModule.PlanCard(new[] { Block(trigger) });
    Check($"{trigger} 落到 {group}", pick(plan).Count == 1, "没落到预期的组");
}

// "回合结束时 + 若在手牌中"必须换成 OnTurnEndInHand，不能留在 EndOfTurn。
{
    CardTimingPlan plan = TimingModule.PlanCard(new[] { Block(EffectTrigger.EndOfTurn, e => e.PileScope = PileScope.Hand) });
    Check("回合结束时 + 在手牌 → 走 OnTurnEndInHand", plan.EndOfTurnInHand.Count == 1 && plan.EndOfTurn.Count == 0, "分错了钩子");
}

// 自动打出这张牌要单独挑出来走相位钩子，不能混在普通回合钩子里（会递归）。
{
    CardTimingPlan start = TimingModule.PlanCard(new[]
    {
        Block(EffectTrigger.StartOfTurn, e => { e.Kind = EffectKind.AutoPlay; e.Pick = CardPick.This(); e.PileScope = PileScope.ExhaustPile; }),
    });

    Check("自动打出（回合开始）走相位钩子", start.SelfPlayOnStart.Count == 1 && start.StartOfTurn.Count == 0, "混进了普通回合钩子");

    CardTimingPlan end = TimingModule.PlanCard(new[]
    {
        Block(EffectTrigger.EndOfTurn, e => { e.Kind = EffectKind.AutoPlay; e.Pick = CardPick.This(); e.PileScope = PileScope.DrawPile; }),
    });

    Check("自动打出（回合结束）走相位钩子", end.SelfPlayOnEnd.Count == 1 && end.EndOfTurn.Count == 0, "混进了普通回合钩子");
}

// 计数器：阈值 1 等价于不启用（不发字段）；"本回合"的才要归零。
{
    CardTimingPlan counted = TimingModule.PlanCard(new[]
    {
        Block(EffectTrigger.CardPlayed, e => { e.UseCounter = true; e.CounterTarget = 3; e.CounterReset = CounterReset.ThisTurn; }),
    });

    Check("阈值 3：发计数器字段", counted.PlayedCounters[0] is not null);
    Check("本回合的计数器要归零", counted.TurnResetCounters.Count == 1);

    CardTimingPlan one = TimingModule.PlanCard(new[]
    {
        Block(EffectTrigger.CardPlayed, e => { e.UseCounter = true; e.CounterTarget = 1; }),
    });

    Check("阈值 1：不发计数器字段（等价于不启用）", one.PlayedCounters[0] is null);

    CardTimingPlan combat = TimingModule.PlanCard(new[]
    {
        Block(EffectTrigger.CardPlayed, e => { e.UseCounter = true; e.CounterTarget = 2; e.CounterReset = CounterReset.ThisCombat; }),
    });

    Check("本场战斗的计数器不归零", combat.TurnResetCounters.Count == 0);
}

// ShouldReceiveCombatHooks：只要有一条依赖钩子派发就要（"在手牌里"那条**不算**，
// 它由引擎逐张手牌直接调用）。
{
    Check("只有打出时：不需要接收战斗钩子",
        !TimingModule.PlanCard(new[] { Block(EffectTrigger.OnPlay) }).NeedsCombatHooks);

    Check("有回合开始时：需要",
        TimingModule.PlanCard(new[] { Block(EffectTrigger.StartOfTurn) }).NeedsCombatHooks);

    Check("有打出一张牌时：需要",
        TimingModule.PlanCard(new[] { Block(EffectTrigger.CardPlayed) }).NeedsCombatHooks);

    Check("有自动打出：需要",
        TimingModule.PlanCard(new[]
        {
            Block(EffectTrigger.StartOfTurn, e => { e.Kind = EffectKind.AutoPlay; e.Pick = CardPick.This(); e.PileScope = PileScope.ExhaustPile; }),
        }).NeedsCombatHooks);

    Check("只有本回合计数器要归零：也需要（归零挂在回合钩子上）",
        TimingModule.PlanCard(new[]
        {
            Block(EffectTrigger.CardPlayed, e => { e.UseCounter = true; e.CounterTarget = 2; e.CounterReset = CounterReset.ThisTurn; }),
        }).NeedsCombatHooks);

    Check("只有「在手牌里」那条：不需要",
        !TimingModule.PlanCard(new[] { Block(EffectTrigger.EndOfTurn, e => e.PileScope = PileScope.Hand) }).NeedsCombatHooks);

    Check("抽到时：不需要（不靠回合钩子派发）",
        !TimingModule.PlanCard(new[] { Block(EffectTrigger.OnDraw) }).NeedsCombatHooks);
}

// ===========================================================================
// 2. 出码：访问修饰符与签名形状
// ===========================================================================
Console.WriteLine();
Console.WriteLine("== 出码 ==");

// 访问修饰符必须与游戏基类一致，否则 CS0507 编译不过。
// 游戏自己就不统一：OnPlay / OnTurnEndInHand 是 protected，After* 那一族是 public。
foreach ((EffectTrigger trigger, string declaration, string why) in new[]
{
    (EffectTrigger.OnPlay, "protected override async Task OnPlay(", "OnPlay 是 protected virtual"),
    (EffectTrigger.AfterPlayed, "public override async Task AfterCardPlayed(", "After* 是 public virtual"),
    (EffectTrigger.OnDraw, "public override async Task AfterCardDrawn(", "After* 是 public virtual"),
    (EffectTrigger.OnDiscard, "public override async Task AfterCardDiscarded(", "After* 是 public virtual"),
    (EffectTrigger.OnExhaust, "public override async Task AfterCardExhausted(", "After* 是 public virtual"),
    (EffectTrigger.CardPlayed, "public override async Task AfterCardPlayed(", "After* 是 public virtual"),
    (EffectTrigger.StartOfTurn, "public override async Task AfterSideTurnStart(", "After* 是 public virtual"),
    (EffectTrigger.EndOfTurn, "public override async Task AfterSideTurnEnd(", "After* 是 public virtual"),
})
{
    string src = CardSource(Block(trigger));
    Check($"{trigger} 的访问修饰符正确（{why}）", src.Contains(declaration), $"缺 `{declaration}`");
}

{
    string src = CardSource(Block(EffectTrigger.EndOfTurn, e => e.PileScope = PileScope.Hand));
    Check("在手牌里：OnTurnEndInHand 是 protected（游戏里也是）",
        src.Contains("protected override async Task OnTurnEndInHand("),
        "访问修饰符不对");
    Check("在手牌里：要声明 HasTurnEndInHandEffect",
        src.Contains("public override bool HasTurnEndInHandEffect => true;"),
        "缺那个标记");
}

// 打出后 + 打出一张牌时：必须合成**一个** AfterCardPlayed，而且不能早退掉另一半。
{
    string src = CardSource(Block(EffectTrigger.AfterPlayed), Block(EffectTrigger.CardPlayed));

    int occurrences = src.Split("async Task AfterCardPlayed(").Length - 1;

    Check("打出后 + 打出一张牌时：只发一个 AfterCardPlayed", occurrences == 1, $"发了 {occurrences} 个（重复定义会编译不过）");
    Check("两者同在时不早退，改成 if 包住自身那半段",
        src.Contains("if (cardPlay.Card == this)") && !src.Contains("if (cardPlay.Card != this)"),
        "形状不对");
}

// 只"打出后"时可以早退。
{
    string src = CardSource(Block(EffectTrigger.AfterPlayed));
    Check("只有打出后：可以早退", src.Contains("if (cardPlay.Card != this)"), "形状不对");
}

// 自动打出的相位钩子。
{
    string src = CardSource(Block(EffectTrigger.StartOfTurn, e =>
    {
        e.Kind = EffectKind.AutoPlay;
        e.Pick = CardPick.This();
        e.PileScope = PileScope.ExhaustPile;
    }));

    Check("自动打出：发相位钩子（回合开始那一侧）",
        src.Contains("AfterAutoPrePlayPhaseEnteredEarly("), "钩子名不对");
    Check("自动打出：钩子必须带 PlayerChoiceContext（与普通回合钩子不同）",
        src.Contains("AfterAutoPrePlayPhaseEnteredEarly(PlayerChoiceContext choiceContext, Player player)"),
        "签名不对");
    Check("自动打出：判牌堆 + 判行动者",
        src.Contains("if (player == Owner && Pile?.Type == PileType.Exhaust)"), "条件不对");
    Check("自动打出：不该混进普通回合钩子",
        !src.Contains("AfterSideTurnStart("), "混进去了（会递归）");
}

{
    string src = CardSource(Block(EffectTrigger.EndOfTurn, e =>
    {
        e.Kind = EffectKind.AutoPlay;
        e.Pick = CardPick.This();
        e.PileScope = PileScope.DrawPile;
    }));

    Check("自动打出（回合结束那侧）", src.Contains("AfterAutoPostPlayPhaseEntered("), "钩子名不对");
}

// 自动打出的重复：牌堆条件只判一次、循环在里层。
{
    string src = CardSource(Block(EffectTrigger.StartOfTurn, e =>
    {
        e.Kind = EffectKind.AutoPlay;
        e.Pick = CardPick.This();
        e.PileScope = PileScope.ExhaustPile;
        e.Repeat = 2;
    }));

    int pileCheck = src.IndexOf("Pile?.Type == PileType.Exhaust", StringComparison.Ordinal);
    int loop = src.IndexOf("for (int i = 0; i < 2; i++)", StringComparison.Ordinal);

    Check("重复打出：循环在牌堆判断**里面**（牌堆只判一次）",
        pileCheck >= 0 && loop > pileCheck, "顺序不对——每轮重判会让第二次不触发");
}

// 计数器出码。
{
    string src = CardSource(Block(EffectTrigger.CardPlayed, e =>
    {
        e.UseCounter = true;
        e.CounterTarget = 3;
        e.CounterReset = CounterReset.ThisTurn;
    }));

    Check("计数器：发字段", src.Contains("private int _playedCount0;"), "没发字段");
    Check("计数器：先加再判阈值，够了就归零并执行",
        src.Contains("_playedCount0++;") && src.Contains("if (_playedCount0 >= 3)") && src.Contains("_playedCount0 = 0;"),
        "计数逻辑不对");
    Check("本回合的计数器：回合开始与结束各归零一次",
        src.Split("_playedCount0 = 0;").Length - 1 >= 3, "归零次数不对（至少有：达到阈值时 + 两个回合边界）");
}

// 牌堆范围逐条判断（同一时机下两条范围不同时，不能整体早退）。
{
    string src = CardSource(
        Block(EffectTrigger.StartOfTurn, e => e.PileScope = PileScope.DrawPile),
        Block(EffectTrigger.StartOfTurn, e => e.PileScope = PileScope.ExhaustPile));

    Check("同一时机两条不同范围：各判各的",
        src.Contains("if (Pile?.Type == PileType.Draw)") && src.Contains("if (Pile?.Type == PileType.Exhaust)"),
        "有一条的判断被吃掉了（那一条会变成死代码）");
}

// 回合开始的钩子不带选择上下文，效果需要时就自己造一个。
{
    string src = CardSource(Block(EffectTrigger.StartOfTurn));

    Check("回合开始：发 HookContext() 自制上下文",
        src.Contains("private PlayerChoiceContext HookContext() =>"), "没发");
    Check("回合开始：效果里用的是 HookContext()",
        src.Contains("HookContext()"), "没接上");
}

// ===========================================================================
// 3. 状态侧的钩子
// ===========================================================================
Console.WriteLine();
Console.WriteLine("== 状态侧 ==");

string PowerSource(params (PowerTrigger When, EffectDef Effect)[] triggers)
{
    var project = new ModProject { Id = "TimingPowerProbe", Name = "Timing Power", Version = "1.0.0" };

    var power = new PowerDef
    {
        Id = "PROBE",
        Name = new LocalizedText("eng", "Probe"),
        Description = "Test.",
        Kind = PowerKind.Buff,
        Stack = PowerStack.Counter,
    };

    foreach ((PowerTrigger when, EffectDef effect) in triggers)
    {
        power.Triggers.Add(new TriggeredEffect { When = when, Effect = effect });
    }

    project.Powers.Add(power);

    return new CodeGenerator(project).GenerateAll()["Powers/ProbePower.cs"];
}

static EffectDef Fx() => new() { Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 1 };

{
    string src = PowerSource((PowerTrigger.TurnStart, Fx()));

    Check("状态：回合开始发 AfterSideTurnStart", src.Contains("public override async Task AfterSideTurnStart("), "没发");
    Check("状态：多行签名的右括号在最后一行（与卡牌侧的单行不同，这是原写法）",
        src.Contains("ICombatState combatState)"), "括号位置不对");
    Check("状态：早退读 Owner.Side（状态的 Owner 就是生物）",
        src.Contains("if (side != Owner.Side)"), "早退条件不对");
}

{
    string src = PowerSource((PowerTrigger.CardPlayed, Fx()));
    Check("状态：打出一张牌时发 AfterCardPlayed（单行签名）",
        src.Contains("public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)"),
        "签名不对");
    Check("状态：早退看牌的持有者", src.Contains("if (cardPlay.Card.Owner?.Creature != Owner)"), "早退条件不对");
}

{
    string src = PowerSource((PowerTrigger.DamageTaken, Fx()));
    Check("状态：受伤发 AfterDamageReceived", src.Contains("public override async Task AfterDamageReceived("), "没发");
    Check("状态：早退看目标", src.Contains("if (target != Owner)"), "早退条件不对");
}

{
    string src = PowerSource((PowerTrigger.TurnEnd, Fx()));
    Check("状态：回合结束发 AfterSideTurnEnd", src.Contains("public override async Task AfterSideTurnEnd("), "没发");
}

// ===========================================================================
// 4. 校验
// ===========================================================================
Console.WriteLine();
Console.WriteLine("== 校验 ==");

static List<TimingRules.Issue> Rules(EffectDef effect, CardType? type = null) =>
    TimingRules.Check(effect, "where", type).ToList();

Check("自动打出 + 非回合时机 → error",
    Rules(new EffectDef { Kind = EffectKind.AutoPlay, Pick = CardPick.This(), Trigger = EffectTrigger.OnPlay })
        .Any(i => i.Severity == "error"));

Check("自动打出 + 牌堆范围任意 → error",
    Rules(new EffectDef { Kind = EffectKind.AutoPlay, Pick = CardPick.This(), Trigger = EffectTrigger.StartOfTurn, PileScope = PileScope.Any })
        .Any(i => i.Severity == "error"));

Check("自动打出配置齐全 → 没有 error",
    !Rules(new EffectDef { Kind = EffectKind.AutoPlay, Pick = CardPick.This(), Trigger = EffectTrigger.StartOfTurn, PileScope = PileScope.ExhaustPile })
        .Any(i => i.Severity == "error"));

Check("计数器阈值 < 1 → warn",
    Rules(new EffectDef { Kind = EffectKind.GainBlock, Trigger = EffectTrigger.CardPlayed, UseCounter = true, CounterTarget = 0 })
        .Any(i => i.Severity == "warn"));

Check("筛指定卡却没填 → error",
    Rules(new EffectDef { Kind = EffectKind.GainBlock, Trigger = EffectTrigger.CardPlayed, CardFilter = CardFilterKind.SpecificCard })
        .Any(i => i.Severity == "error"));

Check("能力牌 + 打出一张牌时 → error（它打完就离开战斗牌堆，收不到钩子）",
    Rules(new EffectDef { Kind = EffectKind.GainBlock, Trigger = EffectTrigger.CardPlayed }, CardType.Power)
        .Any(i => i.Severity == "error"));

Check("普通时机不产生时机类问题",
    Rules(new EffectDef { Kind = EffectKind.GainBlock, Trigger = EffectTrigger.OnPlay }).Count == 0);

Console.WriteLine();
Console.WriteLine($"通过 {passed} 项，失败 {failed} 项。");
Console.WriteLine(failed == 0 ? "RESULT: OK" : "RESULT: FAILED");
return failed == 0 ? 0 : 1;
