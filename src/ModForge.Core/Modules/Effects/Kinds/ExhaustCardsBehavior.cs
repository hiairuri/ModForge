using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 消耗牌：把**选牌器挑出来的那些牌**移进消耗堆。
///
/// 挑哪些牌由选牌器的十步流程决定（某个牌堆 / 按某一维筛 / 按牌名 / 此牌），
/// 挑**几张**由这条效果自己的数值给——<c>&lt;base&gt;</c> 的三件套（基础值 + 变量 × 变量系数），
/// 所以"消耗这张牌"（原版诅咒牌消耗自己）、"消耗手牌中所有牌"、"消耗等于星辰数一半的牌"、
/// "升级后多消耗一张"全都是**同一条效果**的不同取值。
///
/// 数值即张数这条规则由 <c>CardPickPolicy.CountFromEffect</c> 声明（见
/// <see cref="CardPickPolicies"/>），所以出码处拿到的是变量模块算好的表达式，
/// 而不是选牌器自己的一套变量。
///
/// 不走弃牌那条路：消耗是直接 <c>CardPileCmd.Add</c>，与 <c>CardCmd.Discard</c> 的语义不同
/// （见 <c>EffectKind.DiscardCardsInPile</c> 里关于弃牌的说明）。
/// </summary>
internal sealed class ExhaustCardsBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.ExhaustCards;

    public void Emit(EffectEmit a)
    {
        // 同一张牌上可能有多条选牌效果，各自的局部变量要区分开（否则生成的两个
        // pickedCards 重名，编译不过）。
        string suffix = a.Emitter.NextPickSuffix("Exhaust");

        // a.Amount 就是"消耗几张"（变量模块算好的表达式）：固定值读那个变量、
        // 计算型读算出来的值。流程九选"全部"时它用不上（整个牌堆都消耗）。
        foreach (string line in a.Emitter.PickCardsLines(a.Effect, a.Ctx, a.Amount, suffix))
        {
            a.Line(line);
        }

        a.Line($"foreach (var exhaustTarget in pickedCards{suffix})");
        a.Line("{");
        a.Inner("await CardPileCmd.Add(exhaustTarget, PileType.Exhaust);");
        a.Line("}");
    }

    /// <summary>
    /// 这张牌怎么念。
    ///
    /// 张数是**固定的**（或固定 + 变量，有变量名可引用）时走通用那套："消耗{pick}。"
    /// ——里面的张数写成 <c>{ExhaustCount:diff()}</c>，卡面显示实时数字。
    ///
    /// 张数是**算出来的**（有来源，例如"0 + 星辰 × 1"）时，游戏里没有可以引用的变量名
    /// （非伤害/格挡的计算型存在生成类的私有字段里），所以不能写占位符、更不能写一个
    /// 看着像真的的数字。这时改由名词起头、把来源说出来：
    /// "消耗数量等于当前星辰的牌。"——与其它计算型效果的说法一致
    /// （"获得 等于 手牌数 + 1 的格挡"），且两种语言的语序都读得通。
    /// </summary>
    public string Describe(EffectDescribe a)
    {
        CardPick pick = a.Effect.Pick;

        if (a.Effect.IsCalculated && pick.Mode != PickMode.All && pick.Source != PickSource.ThisCard)
        {
            // 来源说法由共用层给（它要管"每单位"系数、"状态层数"换成具体状态名这些事）。
            a.AddCalculationVars();

            string amount = a.Vars["source"] + a.Vars["plus"];

            EffectDescribe described = a
                .With("amount", amount)
                .With("what", PickerWording.PickNoun(a.Project, a.Effect, a.Sentences));

            return described.Render(pick.Mode switch
            {
                PickMode.AtMost => "exhaustCardsCalcAtMost",
                PickMode.Random => "exhaustCardsCalcRandom",
                _ => "exhaustCardsCalc",
            });
        }

        return a.With("pick", a.PickLabel()).Render("exhaustCards");
    }

    // 选牌那一段（十步）由 schema 按策略生成——消耗牌的策略只屏蔽"卡组"，
    // 其余流程全都能用。那条限制写在 CardPickPolicies.For 里（与生成器读的是同一份）。
    //
    // amount: true 是"数值即张数"的另一面：界面上那三格（基础值 / 变量来源 / 变量系数）
    // 就是"消耗几张"，升级改张数也走它们的增量，所以必须显示出来。
    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "消耗牌", labelEn: "Exhaust cards",
        scope: TargetScope.Cards, amount: true);
}
