namespace ModForge.Core.Model;

/// <summary>
/// 把 <see cref="CardFilterKind"/> 翻成 C# 谓词体。
///
/// **两处**共用同一份映射（都是"按一个扁平条件筛"的场合）：
///   - "伤害增益"的作用对象选"按条件"时（<see cref="PickScope.ByFilter"/>）；
///   - "打出一张牌时"这个时机（<see cref="EffectTrigger.CardPlayed"/>）。
///
/// "按条件数牌"**不用这一份**：它用的是与选牌器相同的**多维筛选**
/// （<see cref="CardFilter"/> + <c>CardPickFilter.PredicateOn</c>），六个维度可同时生效。
/// 这里是"一条扁平条件"，那里是"一组条件"——两者不是一回事，所以各有各的生成处。
///
/// 共用的那两处也必须共用：各写一份的话，改了标签判定就会出现"编辑器说这是打击牌、生成的代码
/// 却按别的判"这种不一致，而且两边都不报错。
///
/// 这些形状都照着原版抄：原版用的是 <c>c.Tags.Contains(CardTag.X)</c> 与
/// <c>c.Type == CardType.Y</c>（见"完美打击""飞镖""新月长矛"）。
/// </summary>
public static class CardFilters
{
    /// <summary>
    /// 谓词体，变量名固定为 <c>c</c>（生成时塞进 <c>Where(c =&gt; …)</c> 或 <c>Count(c =&gt; …)</c>）。
    ///
    /// <see cref="CardFilterKind.Any"/> 与 <see cref="CardFilterKind.SpecificCard"/> 没有通用谓词
    /// ——前者不筛（调用方不该走到这里），后者要按具体卡牌类型判（由调用方用 CardId 处理）。
    /// 真调到这两个时返回 <c>true</c> / <c>false</c>，宁可宽/严也不要生成出坏代码。
    /// </summary>
    public static string Predicate(CardFilterKind filter) => PredicateOn("c", filter);

    /// <summary>
    /// 同上，但把"被筛的那张牌"写成调用方给的表达式。
    ///
    /// "打出一张牌时"要筛的是**打出的那张牌**（<c>cardPlay.Card</c>），不是牌堆里遍历到的
    /// <c>c</c>；伤害增益要筛的是原版钩子传进来的 <c>cardSource</c>。三处判定的是同一件事，
    /// 共用一份映射才不会出现"这里按标签、那里按类型"这种静默不一致。
    /// </summary>
    public static string PredicateOn(string variable, CardFilterKind filter) => filter switch
    {
        CardFilterKind.TagStrike => $"{variable}.Tags.Contains(CardTag.Strike)",
        CardFilterKind.TagDefend => $"{variable}.Tags.Contains(CardTag.Defend)",
        CardFilterKind.TagShiv => $"{variable}.Tags.Contains(CardTag.Shiv)",
        CardFilterKind.TagOstyAttack => $"{variable}.Tags.Contains(CardTag.OstyAttack)",

        CardFilterKind.TypeAttack => $"{variable}.Type == CardType.Attack",
        CardFilterKind.TypeSkill => $"{variable}.Type == CardType.Skill",
        CardFilterKind.TypePower => $"{variable}.Type == CardType.Power",

        // 关键词与标签是两套东西：虚无 / 消耗 / 固有这些是 CardKeyword，
        // 打击 / 防御 / 小刀是 CardTag。原版"女妖之嚎"按关键词判（Ethereal），
        // "完美打击"按标签判（Strike）。
        CardFilterKind.KeywordExhaust => $"{variable}.Keywords.Contains(CardKeyword.Exhaust)",
        CardFilterKind.KeywordEthereal => $"{variable}.Keywords.Contains(CardKeyword.Ethereal)",
        CardFilterKind.KeywordInnate => $"{variable}.Keywords.Contains(CardKeyword.Innate)",
        CardFilterKind.KeywordRetain => $"{variable}.Keywords.Contains(CardKeyword.Retain)",
        CardFilterKind.KeywordSly => $"{variable}.Keywords.Contains(CardKeyword.Sly)",
        CardFilterKind.KeywordUnplayable => $"{variable}.Keywords.Contains(CardKeyword.Unplayable)",
        CardFilterKind.KeywordEternal => $"{variable}.Keywords.Contains(CardKeyword.Eternal)",

        CardFilterKind.HasStarCost => $"{variable}.CanonicalStarCost >= 0 || {variable}.HasStarCostX",

        // 不限条件：不筛。指定牌名不该走到这里（那边按类型判），保守起见返回 false 而不是 true
        // ——真出了错宁可"没加成"，也不要"给所有牌都加成"。
        CardFilterKind.Any => "true",
        _ => "false",
    };

    /// <summary>这个条件需不需要具体卡牌类型（即要不要 CardId）。</summary>
    public static bool NeedsCardId(CardFilterKind filter) => filter == CardFilterKind.SpecificCard;
}
