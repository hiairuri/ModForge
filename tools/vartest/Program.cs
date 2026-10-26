using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Variables;

// 变量模块的验收：断言四种出口（声明 / 取值 / 升级 / 等级赋值）在各种槽上都给出正确的文本。
//
// 为什么值得单独一个工具：模块的存在意义就是"这些文本只有一处出处"。文本错了不会编译失败，
// 而是运行时才现形（变量名对不上 → DynamicVars[...] 抛 KeyNotFoundException；字段序号错 →
// 升级改到别的数值上）。这里把每种形状的正确文本钉下来，改坏了立刻知道。
//
// 用法: vartest

int failed = 0;
int passed = 0;

void Check(string what, string actual, string expected)
{
    if (actual == expected)
    {
        passed++;
        return;
    }

    failed++;
    Console.WriteLine($"  [失败] {what}");
    Console.WriteLine($"         期望: {expected}");
    Console.WriteLine($"         实际: {actual}");
}

void CheckSeq(string what, IReadOnlyList<string> actual, params string[] expected)
{
    if (actual.Count == expected.Length && actual.SequenceEqual(expected))
    {
        passed++;
        return;
    }

    failed++;
    Console.WriteLine($"  [失败] {what}");
    Console.WriteLine($"         期望: {string.Join(" | ", expected)}");
    Console.WriteLine($"         实际: {string.Join(" | ", actual)}");
}

var project = new ModProject { Id = "VarTest", Name = "Var Test", Version = "1.0.0" };

project.Powers.Add(new PowerDef
{
    Id = "MIGHT",
    Name = new LocalizedText("eng", "Might"),
    Kind = PowerKind.Buff,
});

// ---------------------------------------------------------------------------
// 1. 固定值：同类第一个沿用原版名，第二个起带序号
// ---------------------------------------------------------------------------
var fixedTwo = new List<EffectDef>
{
    Fixed(EffectKind.DealDamage, 6),
    Fixed(EffectKind.DealDamage, 9),
};

IReadOnlyList<VarSlot> slots = VariableModule.Slots(project, fixedTwo);

Check("固定值：两个伤害各占一个槽", slots.Count.ToString(), "2");

CheckSeq("固定值：第一个沿用原版名",
    slots[0].Declarations(NumericValue.Of(slots[0].Effect)).ToList(),
    "new DamageVar(6m, ValueProp.Move)");

CheckSeq("固定值：第二个带序号",
    slots[1].Declarations(NumericValue.Of(slots[1].Effect)).ToList(),
    "new DamageVar(\"Damage2\", 9m, ValueProp.Move)");

Check("固定值：取值读自己的变量",
    slots[1].ValueExpression("null"), "DynamicVars[\"Damage2\"].BaseValue");

Check("固定值：升级",
    slots[1].UpgradeText(VarPart.Base, 3m), "DynamicVars[\"Damage2\"].UpgradeValueBy(3m);");

Check("固定值：等级赋值",
    slots[1].SetText(VarPart.Base, 14m), "DynamicVars[\"Damage2\"].BaseValue = 14m;");

// 每种"有自己变量类型"的固定值，声明形状必须对上（卡面悬停说明按类型名认变量）。
foreach ((EffectKind kind, string expected) in new[]
{
    (EffectKind.Summon, "new SummonVar(4m)"),
    (EffectKind.Forge, "new ForgeVar(4)"),
    (EffectKind.GainMaxHp, "new MaxHpVar(4m)"),
    (EffectKind.DrawCards, "new CardsVar(4)"),
})
{
    var one = new List<EffectDef> { Fixed(kind, 4) };
    VarSlot slot = VariableModule.Slots(project, one)[0];
    CheckSeq($"{kind}：声明用游戏的变量类型",
        slot.Declarations(NumericValue.Of(slot.Effect)).ToList(), expected);
}

// 状态：声明 PowerVar<T>，T 是本工程的状态类型。
{
    var apply = new List<EffectDef>
    {
        new() { Kind = EffectKind.ApplyPower, Target = EffectTarget.Self, PowerId = "MIGHT", Amount = 3 },
    };

    VarSlot slot = VariableModule.Slots(project, apply)[0];
    CheckSeq("状态：声明 PowerVar<T>",
        slot.Declarations(NumericValue.Of(slot.Effect)).ToList(), "new PowerVar<Powers.MightPower>(3m)");
}

// 其余种类游戏没有专门的变量类型，用 DynamicVar。
{
    var misc = new List<EffectDef> { Fixed(EffectKind.LoseMaxHp, 4) };
    VarSlot slot = VariableModule.Slots(project, misc)[0];
    CheckSeq("其它固定值：用普通 DynamicVar",
        slot.Declarations(NumericValue.Of(slot.Effect)).ToList(), "new DynamicVar(\"MaxHpLost\", 4m)");
}

// ---------------------------------------------------------------------------
// 2. 计算型伤害：第一个占原版名字，第二个起换运行时子类
// ---------------------------------------------------------------------------
var calcTwo = new List<EffectDef>
{
    Calc(EffectKind.DealDamage, baseValue: 3, perUnit: 2),
    Calc(EffectKind.DealDamage, baseValue: 1, perUnit: 3),
};

IReadOnlyList<VarSlot> calcSlots = VariableModule.Slots(project, calcTwo);
VarSlot calc1 = calcSlots[0];
VarSlot calc2 = calcSlots[1];

CheckSeq("计算型1：三个变量都用原版名字",
    calc1.Declarations(NumericValue.Of(calc1.Effect)).ToList(),
    "new CalculationBaseVar(3m)",
    "new ExtraDamageVar(2m)",
    "new CalculatedDamageVar(ValueProp.Move).WithMultiplier(DamageMultiplier)");

CheckSeq("计算型2：基础值/增量/计算值都带序号，且换运行时子类",
    calc2.Declarations(NumericValue.Of(calc2.Effect)).ToList(),
    "new DynamicVar(\"CalculationBase2\", 1m)",
    "new ModForge.Generated.Runtime.SlotExtraDamageVar(\"ExtraDamage2\", 3m)",
    "new ModForge.Generated.Runtime.SlotCalculatedVar(\"CalculatedDamage2\", \"CalculationBase2\", \"ExtraDamage2\", ValueProp.Move).WithMultiplier(DamageMultiplier2)");

Check("计算型：占原版名字走强类型属性",
    calc1.ValueExpression("cardPlay.Target"), "DynamicVars.CalculatedDamage.Calculate(cardPlay.Target)");

Check("计算型：没占到的走索引器 + 转型",
    calc2.ValueExpression("cardPlay.Target"),
    "((CalculatedVar)DynamicVars[\"CalculatedDamage2\"]).Calculate(cardPlay.Target)");

Check("计算型：升级改自己的基础值变量",
    calc2.UpgradeText(VarPart.Base, 2m), "DynamicVars[\"CalculationBase2\"].UpgradeValueBy(2m);");

Check("计算型：升级改自己的增量变量",
    calc2.UpgradeText(VarPart.PerUnit, 0.5m), "DynamicVars[\"ExtraDamage2\"].UpgradeValueBy(0.5m);");

Check("计算型：等级表写基础值",
    calc2.SetText(VarPart.Base, 8m), "DynamicVars[\"CalculationBase2\"].BaseValue = 8m;");

Check("计算型：等级表写增量",
    calc2.SetText(VarPart.PerUnit, 2.5m), "DynamicVars[\"ExtraDamage2\"].BaseValue = 2.5m;");

// 计算型伤害 + 计算型格挡：基础值编号是**全局**的，所以格挡占 CalculationBase2，
// 而且必须换运行时子类（原版 CalculatedBlockVar 固定读 CalculationBase，会读到伤害的基础值）。
{
    var mixed = new List<EffectDef>
    {
        Calc(EffectKind.DealDamage, baseValue: 2, perUnit: 1),
        Calc(EffectKind.GainBlock, baseValue: 4, perUnit: 2),
    };

    IReadOnlyList<VarSlot> mixedSlots = VariableModule.Slots(project, mixed);
    VarSlot block = mixedSlots[1];

    Check("混用：格挡的基础值拿到全局第二个编号", block.BaseVar ?? "(null)", "CalculationBase2");

    CheckSeq("混用：格挡也换运行时子类，并声明 isDamage: false",
        block.Declarations(NumericValue.Of(block.Effect)).ToList(),
        "new DynamicVar(\"CalculationBase2\", 4m)",
        "new CalculationExtraVar(2m)",
        "new ModForge.Generated.Runtime.SlotCalculatedVar(\"CalculatedBlock\", \"CalculationBase2\", \"CalculationExtra\", ValueProp.Move, isDamage: false).WithMultiplier(BlockMultiplier)");

    Check("混用：格挡的乘数方法叫 BlockMultiplier", block.MultiplierName(), "BlockMultiplier");
}

// ---------------------------------------------------------------------------
// 3. 计算型但游戏没有对应变量类型：不声明变量，数值在私有字段里
// ---------------------------------------------------------------------------
{
    var calcPower = new List<EffectDef>
    {
        Calc(EffectKind.ApplyPower, baseValue: 1, perUnit: 1, powerId: "MIGHT"),
    };

    Check("非伤害/格挡的计算型：不占声明变量",
        VariableModule.Slots(project, calcPower).Count.ToString(), "0");

    VarSlot field = VariableModule.FieldSlot(calcPower[0], ValueRole.Value);

    CheckSeq("非伤害/格挡的计算型：两个私有字段",
        field.FieldDeclarations(0, NumericValue.Of(calcPower[0])).ToList(),
        "private decimal _calcBase0 = 1m;",
        "private decimal _calcPerUnit0 = 1m;");

    Check("非伤害/格挡的计算型：方法体返回式",
        field.FieldFormula(0), "_calcBase0 + _calcPerUnit0 * sourceValue");

    Check("非伤害/格挡的计算型：升级写字段",
        field.UpgradeText(VarPart.Base, 2m, 0), "_calcBase0 += 2m;");

    Check("非伤害/格挡的计算型：等级表写字段",
        field.SetText(VarPart.PerUnit, 1.5m, 3), "_calcPerUnit3 = 1.5m;");
}

// ---------------------------------------------------------------------------
// 4. 重复次数：只有"能升级的重复"占槽
// ---------------------------------------------------------------------------
{
    var repeats = new List<EffectDef>
    {
        Repeat(EffectKind.DealDamage, amount: 3, repeat: 2, repeatDelta: 1),
        Repeat(EffectKind.DealDamage, amount: 5, repeat: 3, repeatDelta: 2),
        // 不能升级的重复不占槽（循环里直接写字面量）。
        Repeat(EffectKind.DealDamage, amount: 7, repeat: 4, repeatDelta: 0),
    };

    IReadOnlyList<VarSlot> repeatSlots = VariableModule.RepeatSlots(project, repeats);

    Check("重复次数：只有能升级的才占槽", repeatSlots.Count.ToString(), "2");

    CheckSeq("重复次数：第一个用原版名",
        repeatSlots[0].Declarations(NumericValue.RepeatOf(repeatSlots[0].Effect)).ToList(),
        "new RepeatVar(\"Repeat\", 2)");

    CheckSeq("重复次数：第二个带序号",
        repeatSlots[1].Declarations(NumericValue.RepeatOf(repeatSlots[1].Effect)).ToList(),
        "new RepeatVar(\"Repeat2\", 3)");

    Check("重复次数：取值用 IntValue",
        repeatSlots[1].IntValueExpression(), "DynamicVars[\"Repeat2\"].IntValue");

    Check("重复次数：升级",
        repeatSlots[1].UpgradeText(VarPart.Base, 2m), "DynamicVars[\"Repeat2\"].UpgradeValueBy(2m);");

    Check("重复次数：等级赋值",
        repeatSlots[1].SetText(VarPart.Base, 5m), "DynamicVars[\"Repeat2\"].BaseValue = 5m;");
}

// 计算型次数不占 RepeatVar（次数由两个字段算出），它的槽是 FieldSlot。
{
    var calcRepeat = new List<EffectDef>
    {
        new()
        {
            Kind = EffectKind.DealDamage,
            Target = EffectTarget.Target,
            Amount = 5,
            Repeat = 1,
            CalculatedRepeat = true,
            RepeatBase = 2,
            RepeatSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HpLostThisCombat },
        },
    };

    Check("计算型次数：不占 RepeatVar",
        VariableModule.RepeatSlots(project, calcRepeat).Count.ToString(), "0");

    // 0 级的基础值是 RepeatBase（不是 Repeat——后者在计算型时不被读取）。
    Check("计算型次数：0 级基础值取 RepeatBase",
        NumericValue.RepeatOf(calcRepeat[0]).Base.ToString(), "2");

    Check("计算型次数：升级写字段",
        VariableModule.FieldSlot(calcRepeat[0], ValueRole.Repeat).UpgradeText(VarPart.Base, 1m, 1),
        "_calcBase1 += 1m;");
}

// ---------------------------------------------------------------------------
// 5. 三件套本身
// ---------------------------------------------------------------------------
{
    NumericValue fixedValue = NumericValue.Of(fixedTwo[0]);
    Check("三件套：固定值不是计算型", fixedValue.IsCalculated.ToString(), "False");
    Check("三件套：固定值的基础值", fixedValue.Base.ToString(), "6");

    NumericValue calcValue = NumericValue.Of(calcTwo[1]);
    Check("三件套：选了来源就是计算型", calcValue.IsCalculated.ToString(), "True");
    Check("三件套：变量系数", calcValue.Coefficient.ToString(), "3");

    // 等级表：每一级各推一遍，0 级就是卡上的字段。
    Check("三件套：0 级基础值", NumericValue.Of(calcTwo[1], 0).Base.ToString(), "1");
    Check("三件套：等级表覆盖后基础值", NumericValue.Of(calcTwo[1], 1).Base.ToString(), "1");
}

// ---------------------------------------------------------------------------
// 费用阈值槽（EnergyAmount / StarAmount）：四个出口的文本各一条
//
// 阈值的"基础值"住在卡上的变量里，所以声明、取值、升级、等级赋值四件事都由这个槽给出。
// 第四条（等级赋值）另有一个守卫：**"是 X"那一档不比数字**，所以不该占变量。
// ---------------------------------------------------------------------------
{
    var cost = new List<EffectDef>
    {
        new()
        {
            Kind = EffectKind.ModifyCardCost,
            Target = EffectTarget.Self,
            Amount = -1,
            Pick = new CardPick
            {
                EnergyComparison = CostComparison.GreaterThan,
                StarComparison = CostComparison.AtLeast,
            },
            EnergyAmount = 2,
            StarAmount = 0,
            Upgrade = new EffectUpgrade { EnergyDelta = 1 },
        },
        // 同一条卡上的第二个能量阈值：带序号（EnergyAmount2）。
        new()
        {
            Kind = EffectKind.UpgradeCardsInPile,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = new CardPick { EnergyComparison = CostComparison.AtMost },
            EnergyAmount = 1,
        },
        // "是 X"：不比数字，不该占槽。
        new()
        {
            Kind = EffectKind.CopyCardsToPile,
            Target = EffectTarget.Self,
            Amount = 1,
            Pick = new CardPick { StarComparison = CostComparison.IsX },
        },
    };

    IReadOnlyList<VarSlot> costSlots = VariableModule.ThresholdSlots(project, cost);

    CheckSeq("费用阈值：两个维度各占一个、带序号、X 不占",
        costSlots.Select(s => $"{s.Role}:{s.Name}").ToList(),
        "Energy:EnergyAmount", "Star:StarAmount", "Energy:EnergyAmount2");

    VarSlot energy = costSlots[0];

    // 阈值 = 基础值 + 来源 × 变量系数，**两个数各占一个变量**（所以两个都能升级、
    // 都能被等级表逐级覆盖）。系数变量的名字跟着基础值走，一眼看得出是一对。
    CheckSeq("费用阈值：声明两个变量（基础值 + 变量系数）",
        energy.Declarations(cost[0].NumericOf(ValueRole.Energy)).ToList(),
        "new DynamicVar(\"EnergyAmount\", 2m)",
        "new DynamicVar(\"EnergyAmountExtra\", 1m)");

    Check("费用阈值：基础值取值", energy.ValueExpression("null"), "DynamicVars[\"EnergyAmount\"].BaseValue");
    Check("费用阈值：变量系数取值", energy.CoefficientExpression(), "DynamicVars[\"EnergyAmountExtra\"].BaseValue");

    Check("费用阈值：基础值升级", energy.UpgradeText(VarPart.Base, 1m),
        "DynamicVars[\"EnergyAmount\"].UpgradeValueBy(1m);");
    Check("费用阈值：变量系数升级", energy.UpgradeText(VarPart.PerUnit, 0.5m),
        "DynamicVars[\"EnergyAmountExtra\"].UpgradeValueBy(0.5m);");

    Check("费用阈值：基础值等级赋值", energy.SetText(VarPart.Base, 3m),
        "DynamicVars[\"EnergyAmount\"].BaseValue = 3m;");
    Check("费用阈值：变量系数等级赋值", energy.SetText(VarPart.PerUnit, 2m),
        "DynamicVars[\"EnergyAmountExtra\"].BaseValue = 2m;");

    // 逐角色读比较器：界面的"画不画阈值那几格"与这里同一句判据。
    Check("费用阈值：能量在比数字", cost[0].Pick.UsesThreshold(ValueRole.Energy).ToString(), "True");
    Check("费用阈值：星辰在比数字", cost[0].Pick.UsesThreshold(ValueRole.Star).ToString(), "True");
    Check("费用阈值：X 不比数字", cost[2].Pick.UsesThreshold(ValueRole.Star).ToString(), "False");
    Check("费用阈值：没筛的维度不比数字", cost[0].Pick.UsesThreshold(ValueRole.Value).ToString(), "False");
}

Console.WriteLine();
Console.WriteLine($"通过 {passed} 项，失败 {failed} 项。");
Console.WriteLine(failed == 0 ? "RESULT: OK" : "RESULT: FAILED");
return failed == 0 ? 0 : 1;

// ---------------------------------------------------------------------------

static EffectDef Fixed(EffectKind kind, int amount) => new()
{
    Kind = kind,
    Target = EffectTarget.Self,
    Amount = amount,
};

static EffectDef Calc(EffectKind kind, int baseValue, decimal perUnit, string? powerId = null) => new()
{
    Kind = kind,
    Target = EffectTarget.Self,
    PowerId = powerId,
    Amount = baseValue,
    Source = new ValueSource
    {
        Actor = ValueSourceActor.Self,
        Kind = ValueSourceKind.HandCount,
        PerUnit = perUnit,
    },
};

static EffectDef Repeat(EffectKind kind, int amount, int repeat, int repeatDelta) => new()
{
    Kind = kind,
    Target = EffectTarget.Target,
    Amount = amount,
    Repeat = repeat,
    Upgrade = new EffectUpgrade { RepeatDelta = repeatDelta },
};
