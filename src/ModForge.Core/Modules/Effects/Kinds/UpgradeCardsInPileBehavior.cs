using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 升级某个牌堆里的若干张牌。
///
/// **挑牌时就要排除不可升级的牌**：原版"武装"的做法就是这样（未升级时走
/// <c>CardSelectCmd.FromHandForUpgrade</c>，它内部按 <c>IsUpgradable</c> 过滤；
/// 升级后则是 <c>Hand.Cards.Where(c =&gt; c.IsUpgradable)</c>）。
/// 不排除的话玩家会白选一张——选完发现什么都没发生。那个黑名单由选牌策略的
/// <c>CardConstraintKind.Upgradable</c> 给出（见 <see cref="CardPickPolicies"/>）。
///
/// 两种取法：
///   手牌 + 选择 1 张 —— 用游戏专门的 <c>FromHandForUpgrade</c>：它除了过滤，还会让
///                        手牌进入"升级预览"显示模式，与原版完全一致。
///   其它情况 —— 用通用的选牌入口（游戏在 <c>CardCmd.Upgrade</c> 里自己跳过不可升级的）。
///
/// 注意"能不能升级"交给游戏判（<c>IsUpgradable</c> 会看 <c>MaxUpgradeLevel</c>），
/// 所以不写"是否已升级"这种简化判断，能多次升级的牌不会被漏掉。
/// </summary>
internal sealed class UpgradeCardsInPileBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.UpgradeCardsInPile;

    public void Emit(EffectEmit a)
    {
        // 手牌 + 选 1 张，且不按任何维度筛：走游戏专门的 FromHandForUpgrade
        // （它自带升级预览）。其余组合走通用入口。
        CardPick pick = a.Effect.Pick;

        if (pick.Source == PickSource.Piles && !pick.HasFilter() &&
            pick.Mode == PickMode.Choose && pick.Piles.Count == 1 && pick.Piles[0] == PileKind.Hand &&
            (int)pick.Count.Base == 1 && !pick.Count.IsCalculated)
        {
            a.Line($"var toUpgrade = await CardSelectCmd.FromHandForUpgrade({a.Ctx.ChoiceContext}, {a.Ctx.Player}, {a.Ctx.CardSourceOrSelf});");
            a.Line("if (toUpgrade is not null)");
            a.Line("{");
            a.Inner("CardCmd.Upgrade(toUpgrade);");
            a.Line("}");
            return;
        }

        string picked = a.Emitter.NextPlainPickSuffix();

        foreach (string line in a.Emitter.PickCardsLines(a.Effect, a.Ctx, a.Amount, picked))
        {
            a.Line(line);
        }

        a.Line($"foreach (var target in pickedCards{picked})");
        a.Line("{");
        a.Inner("CardCmd.Upgrade(target);");
        a.Line("}");
    }

    public string Describe(EffectDescribe a)
    {
        a.AddPileVars();

        return a.With("pick", a.PickLabel()).Render("upgradeFromPile");
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "升级牌堆中的牌", labelEn: "Upgrade cards in a pile",
        scope: TargetScope.Cards, amount: false);
}
