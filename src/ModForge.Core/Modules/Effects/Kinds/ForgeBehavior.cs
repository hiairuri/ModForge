using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 铸造（储君）。<c>ForgeCmd.Forge</c> 自己处理整套逻辑——"没有君王之剑就先生成一张加到手牌，
/// 然后把这个数量加到所有副本（含消耗堆）"——我们只把数量传进去。
///
/// 数量按**值**传入（与原版 <c>DynamicVars.Forge.IntValue</c> 同一个入口）：它不读卡上的
/// 变量表，所以这里给的是算好的表达式。变量槽 <c>Forge</c> 是为了卡面能引用、升级能跟着变
/// （<c>ForgeVar</c>）。
///
/// 命令收 <c>Player</c>（铸给谁），所以作用域是玩家轴（见 <see cref="TargetScope.Player"/>）。
/// </summary>
internal sealed class ForgeBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.Forge;

    public void Emit(EffectEmit a) =>
        a.AgainstTargets(player =>
            $"await ForgeCmd.Forge({a.Amount}, {player}, {a.Ctx.CardSourceOrSelf});");

    public string Describe(EffectDescribe a)
    {
        a.AddCalculationVars();

        return a.Render(a.TargetedKey("forge", a.Effect.IsCalculated));
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "铸造", labelEn: "Forge",
        scope: TargetScope.Player, amount: true);
}
