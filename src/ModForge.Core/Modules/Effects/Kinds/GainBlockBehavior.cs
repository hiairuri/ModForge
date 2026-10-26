using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 获得格挡（<c>CreatureCmd.GainBlock</c>）。
///
/// 与伤害共用"作用于目标"那套（<see cref="EffectEmit.AgainstTargets"/>）：给自己是常规用法，
/// 也支持给队友。第四个参数是"来源遗物"，这里给 <c>null</c>——我们不是某件遗物在发格挡，
/// 给了会让游戏把它算进遗物相关的统计里。
/// </summary>
internal sealed class GainBlockBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.GainBlock;

    public void Emit(EffectEmit a) =>
        a.AgainstTargets(target =>
            $"await CreatureCmd.GainBlock({target}, {a.Amount}, ValueProp.Move, null);");

    public string Describe(EffectDescribe a)
    {
        a.AddCalculationVars();

        bool self = a.SelfSubject;

        return a.Render(a.Effect.IsCalculated
            ? (self ? "blockCalc" : "blockOtherCalc")
            : (self ? "block" : "blockOther"));
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "格挡", labelEn: "Block",
        scope: TargetScope.Creature, amount: true);
}
