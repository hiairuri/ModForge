using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 召唤（亡灵契约师的召唤物）。数值是它的生命值，走变量槽 <c>Summon</c>（游戏自己的
/// <c>SummonVar</c>）——所以卡面能引用、悬停说明也认这个变量。
///
/// 两句一起发：先让召唤者放一次施法动画（原版召唤牌都有这个动作，少了它召唤物会直接冒出来），
/// 再把召唤交给 <c>OstyCmd.Summon</c>。第二句的来源参数是"这张牌"（卡牌里）/ 状态自身
/// （状态里）——由共用层的 <c>CardSourceOrSelf</c> 给。
///
/// 命令收 <c>Player</c>（召唤给谁），所以作用域是玩家轴（见 <see cref="TargetScope.Player"/>）。
/// 动画挂在自己身上（<c>Ctx.Self</c>，动画是"召唤者做动作"）、延迟取**目标玩家**的角色配置；
/// 多目标时施法动画仍然只演一遍——它演的本来就是"这次施法"。
/// </summary>
internal sealed class SummonBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.Summon;

    public void Emit(EffectEmit a) =>
        a.AgainstTargetsBlock((player, at) => new[]
        {
            $"{at}await CreatureCmd.TriggerAnim({a.Ctx.Self}, \"Cast\", {player}.Character.CastAnimDelay);",
            $"{at}await OstyCmd.Summon({a.Ctx.ChoiceContext}, {player}, {a.Amount}, {a.Ctx.CardSourceOrSelf});",
        });

    /// <summary>
    /// 数值是**算出来的**时要念出来源（"召唤 等于 手牌数 的生命值"），不能只念基础值——
    /// 卡面写一个确定的数字，而实际召唤出来的生命值不是那个数，等于卡面与代码对不上。
    /// </summary>
    public string Describe(EffectDescribe a)
    {
        a.AddCalculationVars();

        return a.Render(a.TargetedKey("summon", a.Effect.IsCalculated));
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "召唤", labelEn: "Summon",
        scope: TargetScope.Player, amount: true);
}
