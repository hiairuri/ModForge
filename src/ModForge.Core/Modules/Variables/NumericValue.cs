using ModForge.Core.Model;

namespace ModForge.Core.Modules.Variables;

/// <summary>
/// 一个数值由哪三部分构成：<c>基础值 + 变量 × 变量系数</c>。
///
/// 这就是界面上那三格（"效果基础值 / 效果变量来源 / 效果变量系数"）背后的东西，也是变量模块的输入。
/// 它**只是构成**，不含任何"变量怎么读"的知识——手牌数怎么数、状态层数怎么取，那是
/// 变量来源模块（<c>Modules/ValueSources/SourceEmitter</c>）的事，与本模块无关。
///
/// 三部分的关系：
///   <see cref="Base"/>       —— "造成 6 点伤害"里的 6；
///   <see cref="Coefficient"/> —— "每张手牌 +2"里的 2；
///   <see cref="Variable"/>   —— "每张手牌"这个变量本身（含怎么数）。
///
/// <see cref="Variable"/> 选「无」（<see cref="ValueSource.IsUsed"/> 为假）时整个式子退化成
/// 一个固定值——所以"固定值"与"计算型"不是两种模式，而是同一个式子的两个取值，
/// 不需要额外的开关（那个开关是旧限制的残留，已经去掉）。
///
/// 数值与重复次数**各有一套**三件套，两者的基础值可以不同（"造成 5 点伤害，每失去过一次
/// 生命就多打一次"：伤害 5、次数 1），所以是两个工厂方法而不是一个。
/// </summary>
public readonly record struct NumericValue(decimal Base, ValueSource Variable)
{
    /// <summary>变量系数：每 1 点变量值增加多少。支持小数（原版有 0.5 这种）。</summary>
    public decimal Coefficient => Variable.PerUnit;

    /// <summary>是不是算出来的（变量选了来源）。选了来源就是计算型，否则是固定值。</summary>
    public bool IsCalculated => Variable.IsUsed;

    /// <summary>一条效果的**数值**（伤害、格挡、抽牌数、状态层数…）在某一级的构成。</summary>
    /// <param name="effect">效果。</param>
    /// <param name="level">升级等级：0 是未升级（卡上的基础字段）。</param>
    public static NumericValue Of(EffectDef effect, int level = 0)
    {
        EffectState state = effect.StateAt(level);
        return new NumericValue(state.Amount, state.Source);
    }

    /// <summary>
    /// 一条效果的**重复次数**在某一级的构成。
    ///
    /// 固定次数没有变量，<see cref="Variable"/> 是空的，<see cref="Base"/> 就是次数本身。
    ///
    /// 次数与数值**各有一套来源**：这里取的一律是 <see cref="EffectDef.RepeatSource"/>，
    /// 不是数值的 <see cref="EffectDef.Source"/>——两者可以完全不同
    /// （"打等于手牌数的伤害，次数等于失去生命的次数"），取错一边的后果是次数悄悄按
    /// 另一个数算，而卡面文字与生成代码都看不出问题。
    /// </summary>
    public static NumericValue RepeatOf(EffectDef effect, int level = 0)
    {
        if (!effect.CalculatedRepeat)
        {
            return new NumericValue(effect.Repeat, new ValueSource());
        }

        if (level <= 0)
        {
            return new NumericValue(effect.Repeat, effect.RepeatSource);
        }

        EffectState state = effect.StateAt(level);
        return new NumericValue(state.Repeat, state.RepeatSource);
    }
}
