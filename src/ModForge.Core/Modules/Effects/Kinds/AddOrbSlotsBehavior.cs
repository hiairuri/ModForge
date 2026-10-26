using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 获得充能球栏位（<c>OrbCmd.AddSlots</c>）。
///
/// 命令收 <c>Player</c>（加谁的栏位），所以作用域是玩家轴（见 <see cref="TargetScope.Player"/>）。
/// </summary>
internal sealed class AddOrbSlotsBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.AddOrbSlots;

    public void Emit(EffectEmit a) =>
        a.AgainstTargets(player => $"await OrbCmd.AddSlots({player}, (int){a.Amount});");

    // 固定值时是"获得 2 个充能球栏位"；数值**算出来的**时走另一条模板
    // （"获得 等于 手牌数 个充能球栏位"）——不调 AddCalculationVars 的话，句子会把
    // 基础值当成结果写出来（"获得 1 个充能球栏位"，而实际是 1 + 手牌数 个）。
    public string Describe(EffectDescribe a)
    {
        a.AddCalculationVars();

        return a.Render(a.TargetedKey("addOrbSlots", a.Effect.IsCalculated));
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "获得充能球栏位", labelEn: "Gain orb slots",
        scope: TargetScope.Player, amount: true);
}
