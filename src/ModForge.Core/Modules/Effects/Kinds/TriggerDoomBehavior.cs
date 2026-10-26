using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 立即执行灾厄判定：生命值已不高于灾厄层数的目标**当场**死亡（不用等到回合末）。
///
/// 走 <c>DoomPower.GetDoomedCreatures</c> + <c>DoomPower.DoomKill</c> 而不是自己比大小：
/// 判定规则挂在灾厄状态自己身上，复刻一份的话游戏改口径我们就静默不跟。
///
/// 逐个目标收集、一次性传入——<c>DoomPower</c> 的注释特别说明"同一次判定里死亡的生物要
/// 一起传"，否则会漏掉 Reattach 这类复活状态的 Fatal 触发。
///
/// **没有数值**：判定用的是目标身上已有的灾厄层数，所以 <c>amount: false</c>、句子里也没有数字。
/// </summary>
internal sealed class TriggerDoomBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.TriggerDoom;

    public void Emit(EffectEmit a) =>
        a.AgainstTargets(target =>
            $"await DoomPower.DoomKill(DoomPower.GetDoomedCreatures(new[] {{ {target} }}));");

    /// <summary>这句话里没有数字（判的是目标身上已有的灾厄层数），所以不补计算型那两个零件。</summary>
    /// <summary>
    /// 作用于自己时不念对象（"立即执行灾厄判定。"）——模板里那个 <c>{who}</c> 在"自己"那一档
    /// 是**空词**，直接套会念成"立即对执行灾厄判定"。所以自己与别人各一条模板。
    /// </summary>
    public string Describe(EffectDescribe a) => a.Render(a.TargetedKey("triggerDoom", calculated: false));

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "立即执行灾厄判定", labelEn: "Trigger Doom now",
        scope: TargetScope.Creature, amount: false);
}
