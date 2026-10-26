using ModForge.Core.Model;

namespace ModForge.Core.Modules.Variables;

/// <summary>
/// 固定值用哪个游戏变量类型声明。
///
/// 这些类型不是随便选的：卡面的悬停说明、描述里的 <c>{X:diff()}</c> 都按类型名认变量，
/// 所以能对上就对上（"抽牌"必须用 <c>CardsVar</c>，"铸造"必须用 <c>ForgeVar</c>）。
/// <see cref="Plain"/> 是游戏没有专门类型的那些，用普通 <c>DynamicVar</c>。
/// </summary>
public enum VarShape
{
    Damage,
    Block,
    Summon,
    MaxHp,
    Cards,
    Forge,

    /// <summary>获得能量（游戏的 <c>EnergyVar</c>，默认名 <c>Energy</c>）。</summary>
    Energy,

    /// <summary>获得星辰（游戏的 <c>StarsVar</c>，默认名 <c>Stars</c>）。</summary>
    Stars,

    /// <summary>回复生命（游戏的 <c>HealVar</c>，默认名 <c>Heal</c>）。</summary>
    Heal,

    /// <summary>失去生命（游戏的 <c>HpLossVar</c>，默认名 <c>HpLoss</c>）。原版"放血"用的就是它。</summary>
    HpLoss,

    Power,
    Plain,

    /// <summary>重复次数变量（<c>RepeatVar</c>）。</summary>
    Repeat,
}

/// <summary>
/// 这个槽的数值存在哪里——这是本模块唯一的"实现细节"，三件套的语义不受它影响。
///
///   <see cref="GameVar"/> —— 存进游戏的 <c>DynamicVars</c>，用游戏自带的变量类型。
///                            固定值、以及计算型的伤害/格挡走这里。
///   <see cref="Field"/>    —— 游戏只为伤害与格挡提供了计算变量，其它种类的计算型
///                            只能写在生成类的私有字段里，由我们自己发的方法读出来。
/// </summary>
public enum VarStorage
{
    GameVar,
    Field,
}

/// <summary>一个数值由哪几部分组成。</summary>
public enum VarPart
{
    /// <summary>基础值（<c>造成 6 点</c> 里的 6）。</summary>
    Base,

    /// <summary>变量系数（<c>每张手牌 +2</c> 里的 2）。</summary>
    PerUnit,
}

/// <summary>
/// 一条效果在卡上占用的**变量槽**——变量模块的输出。
///
/// 每条效果各占一个槽（同类第一个沿用原版名，后续加序号），所以同一张牌上
/// "造成 4 点伤害两次 + 再造成 9 点伤害"可以各是各的数值、各升各的。
/// 这不是我们的发明：原版"流光"就在一张牌上声明了两个格挡变量（<c>Block</c> 与
/// <c>BlockNextTurn</c>）并各自升级；"万象斩"在一条 OnPlay 里连续调了两次 Damage。
///
/// **这里同时是那个槽全部文本的唯一出处**：声明、取值、升级、等级赋值四种写法都在
/// 这个类型上，而不是散在生成器里。散着写必然分叉，而且是静默的——那正是这条设计要根除的问题。
/// </summary>
/// <param name="Effect">这条槽属于哪条效果。</param>
/// <param name="Role">承载的是哪个数值角色（数值 / 次数 / 能量阈值 / 星辰阈值，见 <see cref="ValueRole"/>）。</param>
/// <param name="Name">调用用的槽名：固定值是存数值的变量名，计算型是计算值变量名
/// （<c>Damage2</c> / <c>CalculatedDamage2</c> / <c>Repeat2</c>）。</param>
/// <param name="BaseVar">计算型的"基础值"变量名；固定值为 null。</param>
/// <param name="ExtraVar">计算型的"变量系数"变量名；固定值为 null。</param>
/// <param name="BaseName">同类的基名（Damage / Block / 状态类型名…），描述与升级都用它。</param>
/// <param name="Ordinal">同类里第几个（1 起）。</param>
/// <param name="Shape">固定值用哪个变量类型声明。</param>
/// <param name="Storage">这个槽的数值存在哪里（见 <see cref="VarStorage"/>）。</param>
/// <param name="PowerTypeRef">施加状态时的状态类型引用（<c>PowerVar&lt;T&gt;</c> 的那个 T）。</param>
public sealed record VarSlot(
    EffectDef Effect,
    ValueRole Role,
    string Name,
    string? BaseVar,
    string? ExtraVar,
    string BaseName,
    int Ordinal,
    VarShape Shape,
    VarStorage Storage,
    string? PowerTypeRef = null)
{
    /// <summary>
    /// 这个计算型槽是否占着**原版那几个名字**（<c>CalculationBase</c> + <c>ExtraDamage</c>
    /// / <c>CalculationExtra</c>）。
    ///
    /// 只有它才能用游戏的原版类型（<c>CalculatedDamageVar</c> / <c>CalculatedBlockVar</c>）：
    /// 那两个类型写死了从 <c>CalculationBase</c> 读基础值，只对**全牌第一个**计算型槽成立。
    /// 第二个起——哪怕是另一种类（"计算型伤害 + 计算型格挡"里的格挡也是第二个）——
    /// 必须换用 <c>Runtime/SlotVar.cs</c> 的子类去读自己那个带序号的名字，
    /// 否则会静默读到第一个槽的基础值（格挡数跟着伤害的基础值走，且只在结算时现形）。
    /// </summary>
    public bool UsesOriginalCalcNames => BaseVar == "CalculationBase";

    /// <summary>计算型伤害槽。它决定增量变量叫 ExtraDamage 还是 CalculationExtra（游戏自己埋的坑）
    /// 以及预览走伤害钩子还是格挡钩子。只在槽内部用。</summary>
    private bool IsDamage => BaseName == "CalculatedDamage";

    /// <summary>这个槽是不是计算型。</summary>
    public bool IsCalculated => Role == ValueRole.Value && Effect.IsCalculated;

    /// <summary>
    /// 这个槽的**变量系数是否占一个独立变量**。
    ///
    /// 只有两个费用阈值是这样。阈值 = <c>基础值 + 来源 × 变量系数</c>，而这两部分都要能升级、
    /// 要被等级表逐级覆盖，所以各占一个变量（<c>EnergyAmount</c> + <c>EnergyAmountExtra</c>）。
    /// 别的固定值不参与算式，没有"系数"这回事；计算型的系数按自己那一套声明
    /// （伤害/格挡是 <c>ExtraDamage</c> / <c>CalculationExtra</c>）。
    /// </summary>
    public bool HasSeparateCoefficient => Role is ValueRole.Energy or ValueRole.Star;

    /// <summary>
    /// 变量系数那一项的**取值表达式**（只对占独立系数变量的槽有意义，见
    /// <see cref="HasSeparateCoefficient"/>）。
    ///
    /// 费用阈值的算式写成"基础值 + 来源 × 系数"，两部分都要从卡上的变量读出来，
    /// 所以各有一个表达式（基础值那个是 <see cref="ValueExpression"/>）。
    /// </summary>
    public string CoefficientExpression() => $"DynamicVars[\"{ExtraVar}\"].BaseValue";

    /// <summary>
    /// 这个槽要声明的那几个变量（计算型是"基础值 + 增量 + 计算值"三个，固定值一个，
    /// 费用阈值两个）。
    ///
    /// 重复次数的槽只有能升级时才存在，所以直接发 <c>RepeatVar</c>；
    /// 两个费用阈值各占**两个**普通变量——基础值（<c>EnergyAmount</c>，描述里写
    /// <c>{EnergyAmount:diff()}</c>）与变量系数（<c>EnergyAmountExtra</c>），见
    /// <see cref="HasSeparateCoefficient"/>。
    /// <see cref="VarStorage.Field"/> 的槽没有声明（字段由"就地计算方法"那一段发出）。
    /// </summary>
    /// <param name="value">这个槽的数值构成。固定值取基础值，计算型取基础值与前系数。</param>
    public IEnumerable<string> Declarations(NumericValue value)
    {
        if (Role == ValueRole.Repeat)
        {
            yield return $"new RepeatVar(\"{Name}\", {Math.Max(1, (int)value.Base)})";
            yield break;
        }

        // 费用阈值：基础值与变量系数各占一个变量（两个都要能升级、能逐级覆盖）。
        if (HasSeparateCoefficient)
        {
            yield return $"new DynamicVar(\"{Name}\", {value.Base}m)";
            yield return $"new DynamicVar(\"{ExtraVar}\", {value.Coefficient}m)";
            yield break;
        }

        if (!IsCalculated)
        {
            int amount = (int)value.Base;
            bool first = Ordinal == 1;

            if (Shape == VarShape.Power)
            {
                yield return first
                    ? $"new PowerVar<{PowerTypeRef}>({amount}m)"
                    : $"new PowerVar<{PowerTypeRef}>(\"{Name}\", {amount}m)";
                yield break;
            }

            yield return Shape switch
            {
                VarShape.Damage => first
                    ? $"new DamageVar({amount}m, ValueProp.Move)"
                    : $"new DamageVar(\"{Name}\", {amount}m, ValueProp.Move)",

                VarShape.Block => first
                    ? $"new BlockVar({amount}m, ValueProp.Move)"
                    : $"new BlockVar(\"{Name}\", {amount}m, ValueProp.Move)",

                // 召唤用游戏的 SummonVar，卡面提示（悬停说明）才会认这个变量。
                VarShape.Summon => first
                    ? $"new SummonVar({amount}m)"
                    : $"new SummonVar(\"{Name}\", {amount}m)",

                // 最大生命用游戏的 MaxHpVar（原版"狂宴"声明的就是它）。
                VarShape.MaxHp => first
                    ? $"new MaxHpVar({amount}m)"
                    : $"new MaxHpVar(\"{Name}\", {amount}m)",

                // 抽牌用游戏的 CardsVar，描述里的 {Cards:diff()} 才认得它。
                VarShape.Cards => first
                    ? $"new CardsVar({amount})"
                    : $"new CardsVar(\"{Name}\", {amount})",

                // 铸造用游戏的 ForgeVar（原版"淬锋"声明的就是它）。构造收 int，不要带 m 后缀。
                VarShape.Forge => first
                    ? $"new ForgeVar({amount})"
                    : $"new ForgeVar(\"{Name}\", {amount})",

                // 获得能量 / 星辰：游戏有专门的类型（原版"祭品""星尘"用的就是它们）。
                // 构造收 int。
                VarShape.Energy => first
                    ? $"new EnergyVar({amount})"
                    : $"new EnergyVar(\"{Name}\", {amount})",

                VarShape.Stars => first
                    ? $"new StarsVar({amount})"
                    : $"new StarsVar(\"{Name}\", {amount})",

                // 回复生命 / 失去生命：游戏有专门的类型（原版"放血"是 HpLossVar）。
                // 这两个构造收 decimal，要带 m 后缀——与上面那几个 int 的不一样。
                VarShape.Heal => first
                    ? $"new HealVar({amount}m)"
                    : $"new HealVar(\"{Name}\", {amount}m)",

                VarShape.HpLoss => first
                    ? $"new HpLossVar({amount}m)"
                    : $"new HpLossVar(\"{Name}\", {amount}m)",

                // 其余（伤害增益、降低最大生命、消耗牌数…）游戏没有专门的变量类型，
                // 用普通 DynamicVar 带名字声明即可，描述照样能引用。
                _ => $"new DynamicVar(\"{Name}\", {amount}m)",
            };

            yield break;
        }

        // 计算型：基础值、变量系数、计算值三个变量。
        yield return UsesOriginalCalcNames
            ? $"new CalculationBaseVar({value.Base}m)"
            : $"new DynamicVar(\"{BaseVar}\", {value.Base}m)";

        // 两个增量变量各只有一个原版名：伤害的 ExtraDamage、格挡的 CalculationExtra
        // （游戏自己埋的坑，见下面的说明）。那两个类型都没有带名字的构造，
        // 所以带序号的增量用普通 DynamicVar 承载——只有伤害那个例外：
        // 原版 ExtraDamageVar 在附魔预览里会把**乘法**附魔摊进自己的显示值
        // （乘法可以摊进每单位：×2 的总伤害 = 每单位也 ×2；加法不行，它加在总和上），
        // 普通 DynamicVar 没有这段，描述里引用它时卡面会差一截，所以用运行时子类补上。
        bool vanillaExtra = ExtraVar == (IsDamage ? "ExtraDamage" : "CalculationExtra");

        if (vanillaExtra)
        {
            yield return IsDamage
                ? $"new ExtraDamageVar({value.Coefficient}m)"
                : $"new CalculationExtraVar({value.Coefficient}m)";
        }
        else if (IsDamage)
        {
            yield return $"new ModForge.Generated.Runtime.SlotExtraDamageVar(\"{ExtraVar}\", {value.Coefficient}m)";
        }
        else
        {
            yield return $"new DynamicVar(\"{ExtraVar}\", {value.Coefficient}m)";
        }

        yield return UsesOriginalCalcNames
            ? (IsDamage
                ? $"new CalculatedDamageVar(ValueProp.Move).WithMultiplier({MultiplierName()})"
                : $"new CalculatedBlockVar(ValueProp.Move).WithMultiplier({MultiplierName()})")
            : $"new ModForge.Generated.Runtime.SlotCalculatedVar(\"{Name}\", \"{BaseVar}\", \"{ExtraVar}\", ValueProp.Move"
                + (IsDamage ? string.Empty : ", isDamage: false")
                + $").WithMultiplier({MultiplierName()})";
    }

    /// <summary>
    /// 计算型伤害/格挡的"单位数量"方法名。
    ///
    /// 每个槽一个方法（同类多条时不能都叫 DamageMultiplier，那是重复定义）：
    /// 第一个沿用原版名字，后续加序号。
    /// </summary>
    public string MultiplierName() => IsDamage
        ? (Ordinal == 1 ? "DamageMultiplier" : $"DamageMultiplier{Ordinal}")
        : (Ordinal == 1 ? "BlockMultiplier" : $"BlockMultiplier{Ordinal}");

    /// <summary>
    /// 调用点上的取值表达式（只对 <see cref="VarStorage.GameVar"/> 有意义）。
    ///
    /// 计算型走游戏的 <c>Calculate(target)</c>：占着原版名字的槽可以用强类型属性
    /// （<c>DynamicVars.CalculatedDamage</c>），没占到的必须用索引器 + 转型——属性里写着转型，
    /// 名字对但类型是子类时会抛 <c>InvalidCastException</c>。
    /// </summary>
    public string ValueExpression(string target) => IsCalculated
        ? (UsesOriginalCalcNames
            ? $"DynamicVars.{(IsDamage ? "CalculatedDamage" : "CalculatedBlock")}.Calculate({target})"
            : $"((CalculatedVar)DynamicVars[\"{Name}\"]).Calculate({target})")
        : $"DynamicVars[\"{Name}\"].BaseValue";

    /// <summary>
    /// 走 <see cref="VarStorage.Field"/> 的槽，其生成方法体的返回式（基础值 + 增量 × 来源值）。
    ///
    /// 参数是该槽那个方法分配到的**字段序号**——序号由调用方在建方法时定下并存住，
    /// 不再靠在下标里反查（反查会在顺序一变时静默错位）。
    /// </summary>
    public string FieldFormula(int fieldIndex) =>
        $"_calcBase{fieldIndex} + _calcPerUnit{fieldIndex} * sourceValue";

    /// <summary>走 <see cref="VarStorage.Field"/> 的槽，那两个私有字段的声明。</summary>
    public IEnumerable<string> FieldDeclarations(int fieldIndex, NumericValue value)
    {
        yield return $"private decimal _calcBase{fieldIndex} = {value.Base}m;";
        yield return $"private decimal _calcPerUnit{fieldIndex} = {value.Coefficient}m;";
    }

    /// <summary>重复次数变量的整数读取式（循环次数用它）。</summary>
    public string IntValueExpression() => $"DynamicVars[\"{Name}\"].IntValue";

    /// <summary>升级（增量语义）：每次升级把这个数加上去。</summary>
    public string UpgradeText(VarPart part, decimal delta, int fieldIndex = -1) =>
        Storage == VarStorage.Field
            ? $"{FieldName(part, fieldIndex)} += {delta}m;"
            : $"DynamicVars[\"{VariableNameFor(part)}\"].UpgradeValueBy({delta}m);";

    /// <summary>等级表（绝对语义）：这一级就应该是这个数。</summary>
    public string SetText(VarPart part, decimal value, int fieldIndex = -1) =>
        Storage == VarStorage.Field
            ? $"{FieldName(part, fieldIndex)} = {value}m;"
            : $"DynamicVars[\"{VariableNameFor(part)}\"].BaseValue = {value}m;";

    /// <summary>这一部分对应哪个变量名。</summary>
    private string VariableNameFor(VarPart part) => IsCalculated
        ? (part == VarPart.PerUnit ? ExtraVar! : BaseVar!)
        : HasSeparateCoefficient
            ? (part == VarPart.PerUnit ? ExtraVar! : Name)
            : Name;

    private static string FieldName(VarPart part, int fieldIndex) =>
        part == VarPart.PerUnit ? $"_calcPerUnit{fieldIndex}" : $"_calcBase{fieldIndex}";
}
