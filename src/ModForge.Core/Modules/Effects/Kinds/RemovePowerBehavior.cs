using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 移除状态。两件事各有两个选项（都是 <c>EffectKind.RemovePower</c> 自己的参数）：
///
///   **移除方式**（<see cref="PowerRemoval"/>）—— 整个移除，还是只减几层；
///   **移除哪一类**（<see cref="PowerSelection"/>）—— 指定的那一个，还是所有正面 / 所有负面。
///
/// 于是四条代码路径（两种方式 × 两类选择），但只有两条"动作"：
///
///   整层移除 <c>PowerCmd.Remove</c>；
///   减 N 层   <c>PowerCmd.ModifyAmount</c> 配负数偏移——**这是减层的正确入口**：
///             它走游戏的加/减量钩子、记战斗历史，并且降到阈值时由游戏自己把状态移掉
///             （<c>PowerModel.ShouldRemoveDueToAmount</c>），所以"减到 0 该消失"不用我们判。
///
/// 目标要照 <see cref="EffectDef.Target"/> 走（原版"净化"是给对方去增益、也可以给自己清负面），
/// 所以用共用层的目标循环。
///
/// **正面 / 负面的判据是游戏自己的** <c>PowerModel.TypeForCurrentAmount</c>（卡面数字的颜色
/// 用的是同一个）：它按**当前层数**算类型，所以"力量 -3"算负面。自己按类型名列名单就会漏掉
/// 那种情况，而且游戏新增状态时我们没有对照。
///
/// 多目标时**每个匹配到的状态各减 N 层**（与"整个移除"那一档＝每个状态移除所有层数相对称）。
/// </summary>
internal sealed class RemovePowerBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.RemovePower;

    public void Emit(EffectEmit a)
    {
        bool partial = a.Effect.Removal == PowerRemoval.Partial;

        // 减层＝负数偏移。**括起来**：amount 可能是字面量负数（三件套的基础值允许为负），
        // 直接拼 "--3" 就不是合法表达式了。
        string offset = $"-({a.Amount})";

        if (a.Effect.Selection == PowerSelection.PowerId)
        {
            string powerType = a.Emitter.PowerModelTypeRef(a.Effect.PowerId!);

            if (!partial)
            {
                a.AgainstTargets(target => $"await PowerCmd.Remove<{powerType}>({target});");
                return;
            }

            // 减层要先拿到那层状态（没有它就没得减）。
            string name = "power" + a.Emitter.NextPickSuffix("Remove");

            a.AgainstTargetsBlock((target, at) => new[]
            {
                $"{at}var {name} = {target}.GetPower<{powerType}>();",
                $"{at}if ({name} is not null)",
                $"{at}{{",
                $"{at}    await PowerCmd.ModifyAmount({a.Ctx.ChoiceContext}, {name}, {offset}, {a.Ctx.Dealer}, {a.Ctx.CardSource});",
                $"{at}}}",
            });

            return;
        }

        // 正面 / 负面：遍历目标身上所有状态，按**当前**类型挑。
        //
        // 必须 `.ToList()` 快照：循环里 ModifyAmount 可能在降到阈值时把那层状态移掉，
        // 正在遍历的集合就变了（改用快照之后，筛哪些也固定在"开始那一刻的层数"上）。
        string wanted = a.Effect.Selection == PowerSelection.Buff ? "Buff" : "Debuff";
        string item = "power" + a.Emitter.NextPickSuffix("All");

        a.AgainstTargetsBlock((target, at) =>
        {
            var lines = new List<string>
            {
                $"{at}foreach (var {item} in {target}.Powers.ToList())",
                $"{at}{{",
                $"{at}    if ({item}.TypeForCurrentAmount != PowerType.{wanted})",
                $"{at}    {{",
                $"{at}        continue;",
                $"{at}    }}",
            };

            lines.Add(partial
                ? $"{at}    await PowerCmd.ModifyAmount({a.Ctx.ChoiceContext}, {item}, {offset}, {a.Ctx.Dealer}, {a.Ctx.CardSource});"
                : $"{at}    await PowerCmd.Remove({item});");
            lines.Add($"{at}}}");

            return lines;
        });
    }

    /// <summary>
    /// 这句话怎么说：**方式 × 类别** 四种组合各有各的说法。
    ///
    /// 部分移除那几档固定/计算各一条模板（固定值写占位符 "3 层"，算出来的念来源
    /// "等于 手牌数 + 1 层"）——与 `draw` / `drawCalc` 那种成对写法同一个规矩，
    /// 不能合成一条：算式那句要"等于"，数量短语的构造方式与固定值不同。
    /// </summary>
    public string Describe(EffectDescribe a)
    {
        bool partial = a.Effect.Removal == PowerRemoval.Partial;
        bool byId = a.Effect.Selection == PowerSelection.PowerId;

        // 按 id 指定时才需要状态名（正面/负面那两档的句子里没有状态名）。
        if (byId)
        {
            a.With("name", DescriptionNames.PowerName(a.Project, a.Effect.PowerId, a.Sentences.Language));
        }

        // 作用于别人（含其奥斯提）时句子要说清"是谁的"（状态挂在目标身上）：所以每一条
        // 都有 Other 变体。判据用 TargetedKey 同一套（自己 + 不是取其宠物）。
        if (!partial)
        {
            // 整层移除：句子里没有数字，"类"由选择器决定。
            return a.Render(a.TargetedKey(
                a.Effect.Selection switch
                {
                    PowerSelection.Buff => "removePowerBuff",
                    PowerSelection.Debuff => "removePowerDebuff",
                    _ => "removePower",
                },
                calculated: false));
        }

        // 部分移除：层数走三件套（固定值的 {num} 由共用层给，算出来的要补 source/plus）。
        a.AddCalculationVars();

        bool calc = a.Effect.IsCalculated;

        return a.Render(a.TargetedKey(
            a.Effect.Selection switch
            {
                PowerSelection.Buff => "removePowerPartialBuff",
                PowerSelection.Debuff => "removePowerPartialDebuff",
                _ => "removePowerPartial",
            },
            calc));
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "移除状态", labelEn: "Remove power",
        scope: TargetScope.Creature,

        // amount: true 是"部分移除"那一档的另一面——它的数值就是"减几层"，
        // 所以界面上那三格（基础值 / 变量来源 / 变量系数）与"升级时数值 +"都要显示。
        // 整层移除时那些格填了不生效，界面按 Removal 隐去（见 ui/app.js）。
        amount: true,

        // 要不要选"哪个状态"由 Selection 决定（选正面/负面时不必指定），
        // 界面按它隐去那一格；这里只表示"这条效果会用到状态那一格"。
        needsPower: true,

        // 两个新下拉（移除方式 / 移除哪一类）——界面据此多画那两格。
        hasPowerRemoval: true);
}
