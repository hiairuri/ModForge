using ModForge.Core.Model;

namespace ModForge.Core.Modules.Cards;

/// <summary>
/// 卡牌固有属性的校验：**组合**上的矛盾，而不是单看某一项。
///
/// 这些检查之所以必要，是因为它们全都会"生成得出来、游戏里才炸"或者"填了没反应"：
///
///   * X 费 + 升级费用变化 —— X 费牌没有固定费用可以升降，游戏侧会自行跳过
///     （<c>CardEnergyCost.UpgradeBy</c> 对 X 费什么都不做），所以填了是白填。
///   * X 星 + 升级星辰变化 —— 这一条**会抛异常**：<c>UpgradeStarCostBy</c> 开头就是
///     <c>if (HasStarCostX) throw new InvalidOperationException(...)</c>（游戏源码里写死的），
///     而且只在升级时抛——卡面预览看不出来。
///   * 星辰费用为负却不是 X —— 那等于没填，说明作者本意多半是想要 X 星或忘了填。
///   * 重放次数为负 —— 没有意义，且会让 <c>BaseReplayCount</c> 变成负数。
/// </summary>
public static class CardCoreRules
{
    /// <summary>一条校验结果：严重程度与说明。</summary>
    public sealed record Issue(string Severity, string Message);

    /// <summary>校验一张卡的固有属性组合。</summary>
    public static IEnumerable<Issue> Check(CardCore core)
    {
        // X 费牌没有固定费用可升降。游戏自己会跳过，所以不报错，但要说明白。
        if (core.EnergyCostX && core.UpgradeCostDelta != 0)
        {
            yield return new Issue(
                "warn",
                $"card {core.Id}: X-cost cards have no fixed energy cost to change, so the upgrade "
                + $"cost change ({core.UpgradeCostDelta}) does nothing. "
                + "X 费牌没有固定能量费用，升级费用变化不会生效。");
        }

        // 这一条是真的会炸：游戏在 UpgradeStarCostBy 里对 X 星直接抛异常。
        if (core.StarCostX && core.UpgradeStarDelta != 0)
        {
            yield return new Issue(
                "error",
                $"card {core.Id}: the game throws when upgrading the star cost of an X-star card "
                + $"(\"UpgradeStarCostBy called on {core.Id} which has star cost X\"), so the upgrade "
                + $"star change ({core.UpgradeStarDelta}) must be removed. "
                + "X 星的牌不能有升级星辰费用变化——游戏会抛异常。");
        }

        // 负的星辰费用既不是"没有"（那是 -1）、也不是一个合法费用。
        if (!core.StarCostX && core.StarCost < -1)
        {
            yield return new Issue(
                "warn",
                $"card {core.Id}: star cost {core.StarCost} is below -1, which the game treats as "
                + "\"no star cost\" but is not a meaningful value. Use -1 for \"none\". "
                + "星辰费用低于 -1 没有意义：-1 才是「没有星辰费用」。");
        }

        // 升级会把星辰费用降到负数 → 打出时"倒给星辰"，多半不是本意。
        if (core.HasStarCost && core.StarCost + core.UpgradeStarDelta < 0)
        {
            yield return new Issue(
                "warn",
                $"card {core.Id}: upgrading would push the star cost to "
                + $"{core.StarCost + core.UpgradeStarDelta}, a negative cost. "
                + "升级后星辰费用为负——打出时反而会获得星辰，确认是有意为之。");
        }

        if (core.ReplayCount < 0)
        {
            yield return new Issue(
                "error",
                $"card {core.Id}: replay count cannot be negative (it is {core.ReplayCount}). "
                + "重放次数不能为负。");
        }
    }
}
