using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 引导能量球。逐个引导而不是一次给 N 个，因为游戏没有批量接口，而且逐个引导时
/// 球位满了会按原版规则自动激发最前面的球——这样行为与原版卡牌一致。
///
/// 命令收 <c>Player</c>（生成到谁的球位上），所以作用域是玩家轴
/// （见 <see cref="TargetScope.Player"/>）。
/// </summary>
internal sealed class ChannelOrbBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.ChannelOrb;

    public void Emit(EffectEmit a)
    {
        string orbType = $"MegaCrit.Sts2.Core.Models.Orbs.{a.Effect.Orb}Orb";

        a.AgainstTargetsBlock((player, at) => new[]
        {
            $"{at}int channelCount = (int){a.Amount};",
            $"{at}for (int n = 0; n < channelCount; n++)",
            $"{at}{{",
            $"{at}    await OrbCmd.Channel<{orbType}>({a.Ctx.ChoiceContext}, {player});",
            $"{at}}}",
        });
    }

    // 球名要嵌进句子（"生成 1 个闪电充能球"），所以模板拿到的是本地化过的名字。
    // 数值**算出来的**时走另一条模板（"生成 等于 手牌数 个闪电充能球"）——不调
    // AddCalculationVars 的话，句子会把基础值当成结果写出来。
    public string Describe(EffectDescribe a)
    {
        a.AddCalculationVars();

        return a.With("orb", a.OrbName())
            .Render(a.TargetedKey("channelOrb", a.Effect.IsCalculated));
    }

    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "生成充能球", labelEn: "Channel orbs",
        scope: TargetScope.Player, amount: true, needsOrb: true);
}
