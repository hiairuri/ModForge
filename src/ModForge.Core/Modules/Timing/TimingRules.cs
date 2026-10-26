using ModForge.Core.Model;

namespace ModForge.Core.Modules.Timing;

/// <summary>
/// 时机相关的校验：几处"能生成得出来、但等于不存在"的组合。
///
/// 它们都归时机管，因为问题出在**时机与牌堆范围**上，而不是某个效果做了什么：
///
///   * "自动打出这张牌"配上非回合时机 —— 生成器不给它挂任何钩子，这条效果等于不存在；
///     要是还挂在"打出时"，那就变成打出时再打出自己，无限递归。
///   * 同上，牌堆范围是"任意" —— 生成的判断会把牌堆名退化成 Hand，于是"在手牌里就自己出来"。
///   * "打出一张牌时"的计数器阈值 &lt; 1 —— 每张牌 +1，永远到不了阈值。
///   * "打出一张牌时"筛"指定卡"却没填是哪张 —— 判定的类型引用为空，条件无法成立。
///   * 能力牌反应别人出牌 —— 能力牌打完就离开战斗牌堆（<c>PileType.None</c>），
///     而只有待在战斗牌堆里的牌才收得到钩子，所以它只会看见自己那一次出牌。
///
/// 前四条是"配置缺失"，第五条是**游戏机制**决定的，更容易误判成 bug，所以说明写得更长。
/// </summary>
public static class TimingRules
{
    /// <summary>一条校验结果：严重程度与说明。</summary>
    public sealed record Issue(string Severity, string Message);

    /// <param name="effect">要检查的效果。</param>
    /// <param name="where">出问题的地方（用于消息前缀）。</param>
    /// <param name="cardType">
    /// 这条效果所在卡牌的类型；状态与共享效果定义处为 null（那种情况没有"能力牌"这个问题）。
    /// </param>
    public static IEnumerable<Issue> Check(EffectDef effect, string where, CardType? cardType = null)
    {
        if (effect.IsSelfAutoPlay)
        {
            foreach (Issue issue in CheckSelfAutoPlay(effect, where))
            {
                yield return issue;
            }

            // 重复次数在这里是有效的（"若在消耗堆中，将此牌重复打出 2 次"）：
            // 牌堆条件只判一次，循环里重复打出。所以不再对"填了重复"报提示。
            yield break;
        }

        if (effect.Trigger != EffectTrigger.CardPlayed)
        {
            yield break;
        }

        if (effect.UseCounter && effect.CounterTarget < 1)
        {
            yield return new Issue(
                "warn",
                $"{where}: the {effect.Kind} effect uses a counter with a threshold of "
                + $"{effect.CounterTarget}, so it never fires (use 1 to fire on every card).");
        }

        if (effect.CardFilter == CardFilterKind.SpecificCard &&
            string.IsNullOrWhiteSpace(effect.CardId))
        {
            yield return new Issue(
                "error",
                $"{where}: the {effect.Kind} effect is filtered to one specific card but none is "
                + "selected, so it never fires.");
        }

        if (cardType == CardType.Power)
        {
            yield return new Issue(
                "error",
                $"{where}: a Power card cannot react to other cards being played. Powers leave the "
                + "combat piles when played (PileType.None), and only cards in a combat pile receive "
                + "hooks — so this one would see nothing but its own play. Make it an Attack or Skill "
                + "(it then sits in the discard pile and keeps listening), or move the behaviour to a "
                + "power that triggers on \"when a card is played\".");
        }
    }

    /// <summary>
    /// "自动打出这张牌"要求回合时机 + 具体牌堆范围，缺哪一样都会生成出坏代码
    /// （理由见类型说明）。所以这里明确报错并说清要补什么，而不是让它静默失效。
    /// </summary>
    private static IEnumerable<Issue> CheckSelfAutoPlay(EffectDef effect, string where)
    {
        if (effect.Trigger is not (EffectTrigger.StartOfTurn or EffectTrigger.EndOfTurn))
        {
            yield return new Issue(
                "error",
                $"{where}: an {effect.Kind} effect needs the trigger set to StartOfTurn or EndOfTurn "
                + $"(it is {effect.Trigger}); otherwise the card never plays itself.");
        }

        if (effect.PileScope == PileScope.Any)
        {
            yield return new Issue(
                "error",
                $"{where}: an {effect.Kind} effect needs a pile scope (Hand / DrawPile / DiscardPile / ExhaustPile) "
                + "to say which pile the card must be in to play itself.");
        }

        // "这张牌自己"的自动打出由引擎的相位钩子直接执行，不经过效果出码的那条路——
        // 所以启用条件包不出这层 if。要条件，把它写在触发时机的牌堆范围里
        // （"在消耗堆中"本身就是一种条件）。
        if (effect.HasCondition)
        {
            yield return new Issue(
                "warn",
                $"{where}: a self auto-play {effect.Kind} effect cannot use a condition "
                + "(the engine plays the card directly, so the condition is never checked). "
                + "The pile scope above already acts as the condition.");
        }
    }
}
