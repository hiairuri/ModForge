using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 提高最大生命（<c>CreatureCmd.GainMaxHp</c>）。
///
/// 签名是 <c>(Creature, decimal)</c>——**不接受 PlayerChoiceContext**，因为它不改当前生命，
/// 只把上限抬上去（当前生命跟着抬），没有"要不要扣到新上限"这种需要询问的边界情况。
/// 多传一个参数会直接编译失败，所以这一条是照签名写死的。
///
/// 改动跨战斗保留（动的是那个生物的最大生命值，不是战斗内的临时数值），所以不需要存档字段。
/// </summary>
internal sealed class GainMaxHpBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.GainMaxHp;

    public void Emit(EffectEmit a) =>
        a.AgainstTargets(target => $"await CreatureCmd.GainMaxHp({target}, {a.Amount});");

    public string Describe(EffectDescribe a)
    {
        a.AddCalculationVars();

        return a.Render(a.TargetedKey("gainMaxHp", a.Effect.IsCalculated));
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "提高最大生命", labelEn: "Gain Max HP",
        scope: TargetScope.Creature, amount: true);
}
