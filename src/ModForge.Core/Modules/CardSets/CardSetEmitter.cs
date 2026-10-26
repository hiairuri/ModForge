namespace ModForge.Core.Modules.CardSets;

/// <summary>
/// 临时牌集合的**出码形状**——那几个运行时调用的写法只有这一处。
///
/// 与 <see cref="CardSets"/> 分开：那边是"规则"（谁能存、谁能取、编号怎么算），
/// 这边是"发成哪几行 C#"。生成器与出码处只调这里，所以运行时的类名、方法名、
/// 键的顺序都只有一个出处。
///
/// 键的构成见 <see cref="CardSets"/> 的说明：<c>{地址}.{效果编号}.{集合ID}</c>。
/// 三段的拼法**在运行时那一侧**做（<c>TempCardSets</c>），生成代码只把三个零件传进去——
/// 这样"键怎么拼"就只有一处实现，不会出现生成侧拼一套、运行时拼另一套。
/// </summary>
internal static class CardSetEmitter
{
    /// <summary>
    /// 开一个**局内的牌地址**（每次出牌结算一个）。<paramref name="entry"/> 是这张牌的模型条目
    /// （例如 <c>MYMOD_NEW_CARD</c>），只为了让地址在日志里读得懂。
    /// </summary>
    public static string Open(string entry) =>
        $"{CardSetCatalog.RuntimeClass}.Open(\"{entry}\")";

    /// <summary>结算结束：清掉这个地址下的全部集合。</summary>
    public static string Clear(string address) =>
        $"{CardSetCatalog.RuntimeClass}.Clear({address});";

    /// <summary>把一组牌存进集合。</summary>
    public static string Store(string address, int effectNumber, string setId, string cards) =>
        $"{CardSetCatalog.RuntimeClass}.Store({address}, {effectNumber}, \"{setId}\", {cards});";

    /// <summary>
    /// 取集合的候选列表表达式（**NULL 安全**：没有就返回空列表，交给调用方接
    /// <c>.Where(...)</c> / <c>.ToList()</c>，与别的来源一样用）。
    /// </summary>
    public static string Cards(string address, int effectNumber, string setId) =>
        $"{CardSetCatalog.RuntimeClass}.Cards({address}, {effectNumber}, \"{setId}\")";
}
