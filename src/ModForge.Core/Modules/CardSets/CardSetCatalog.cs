using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;

namespace ModForge.Core.Modules.CardSets;

/// <summary>
/// **临时牌集合**：一次出牌结算期间，把"抽到的 / 生成的 / 选出的那些牌"存起来，
/// 供**同一张牌**的其它效果再动它们（"抽到的那几张降费 / 附魔 / 升级"）。
///
/// 为什么需要它：一张牌的效果是"按顺序各自独立"的——"抽 3 张"那条只说抽了 3 张，
/// 后面那条"降费"没法指明"就是刚才那 3 张"（它只能按牌堆或条件挑，而"刚抽到的那 3 张"
/// 在牌堆里与别的牌长得一样）。集合就是那个"刚才那几张"的把手。
///
/// **键的形状**（用户定的命名规则）：
///
///     {局内的牌地址}.{效果编号}.{集合ID}
///     MYMOD_NEW_CARD#7.3.A
///
///   **局内的牌地址** —— <c>TempCardSets.Open(entry)</c> 在 <c>OnPlay</c> 开头发的一个字符串，
///   每次出牌（含重放的每一遍）各一个（<c>entry#序号</c>）。它是"这一次结算"的代号，
///   所以嵌套结算（打出的牌又打出牌）不会互相看见对方的集合。
///   **效果编号** —— 定义这个集合的那条效果在这张牌的效果列表里是第几条（**1 基**，
///   界面上"效果 N"那个号）。放进键里是为了让键指向"谁写的"，读写两侧因此自动对上：
///   读的那条不需要知道自己的编号，只需要知道"哪个集合"→ 找到定义它的那条效果的编号。
///   **集合ID** —— 用户起的名字（<see cref="EffectDef.SetId"/>）。
///
/// **清空**：结算结束时（<c>finally</c>）按地址前缀把这一轮的集合全部丢掉，
/// 所以它不会跨牌、跨回合地留着。
///
/// **NULL 防御**：取一个不存在的集合**返回空列表**（不是 null、不抛异常）。集合可能因为
/// "定义它的那条效果没走到"（比如它在一个条件分支里、或牌堆空了没抽到牌）
/// 而不存在，那是**正常情况**，用它的效果按"没有牌可动"处理即可。
/// </summary>
public static class CardSetCatalog
{
    /// <summary>
    /// 生成代码里那个运行时类的**全名**（与 <c>Runtime/TempCardSets.cs</c> 一致）。
    ///
    /// 全名而不是短名：生成的卡里没有 <c>using ModForge.Generated.Runtime;</c>，
    /// 别的运行时助手也都是写全名的（<c>ModForge.Generated.Runtime.PermanentDamageBoost.Apply</c>），
    /// 与那一族的写法保持一致。
    /// </summary>
    public const string RuntimeClass = "ModForge.Generated.Runtime.TempCardSets";

    /// <summary>
    /// 集合 ID 写不写得出来：只能用字母、数字、下划线，且非空。
    ///
    /// 不能有点是因为键用 <c>.</c> 分段（<c>地址.编号.集合ID</c>）——ID 里带点的话
    /// 键就不再能唯一地切出三段，清空（按地址前缀）也会误伤别的地址。空格、中文同理：
    /// 它们本身能进字符串键，但读的人再也看不懂这个键在说什么。
    /// </summary>
    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        return id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    }

    /// <summary>
    /// 这条效果能不能**从临时牌集合取牌**（它的"作用方式"里有没有「牌集合ID」这一项）。
    ///
    /// 两个条件都要满足：这条效果**有选牌器**（<see cref="CardPickPolicies.UsesPicker"/>），
    /// 而且它的策略**允许**集合这一档（<see cref="CardPickPolicies.For"/> 的 Sources 里有
    /// <see cref="PickSource.Set"/>）。策略是"这条效果的选牌器允许哪些来源"的唯一出处，
    /// 界面/生成器/校验器都读它——这里再写一份名单就会有两份会分叉的白名单。
    ///
    /// 为什么必须先判"有没有选牌器"：策略表最后那个 <c>_ =&gt;</c> 默认分支覆盖**所有**没列出
    /// 的种类（伤害、能量…），只看 Sources 会让"伤害也能取集合"。
    ///
    /// 自动打出就是被这条判据排除掉的：它的出码直接从牌堆取顶牌，不看候选列表。
    /// </summary>
    public static bool Reads(EffectKind kind) =>
        CardPickPolicies.UsesPicker(kind)
        && CardPickPolicies.For(kind).Sources.Contains(PickSource.Set);

    /// <summary>
    /// 这条效果会不会**产出一组牌**（所以能存进临时集合）。
    ///
    /// 能取集合的（<see cref="Reads"/>，也就是会挑牌的那几种）都能存：挑出来的那几张就是
    /// 要存的那组。另外还有两个**没有选牌器但同样产出一组牌**的：
    ///   抽牌 —— <c>CardPileCmd.Draw</c> 的返回值就是抽到的那几张；
    ///   加入指定卡牌 —— 生成的每一张都留着。
    /// 这两个正是"抽到的那几张"最典型的用处，所以必须在名单里。
    ///
    /// 有数值但结果不是牌的（伤害 / 格挡 / 能量…）不在名单里：它们没东西可存。
    /// </summary>
    public static bool Stores(EffectKind kind) =>
        Reads(kind) || kind is EffectKind.DrawCards or EffectKind.AddNamedCardToPile;

    /// <summary>
    /// 这张卡的这么多效果里，**谁**定义了这个集合（返回 1 基的效果编号；没人定义返回 0）。
    ///
    /// 读一个集合时要拿定义那一条的编号去拼键（见类的说明），所以这个查找是读写两侧
    /// 对上号的唯一依据。定义重复时取**第一个**——重复本身由校验器报错。
    /// </summary>
    public static int DefinitionNumber(IEnumerable<EffectDef> effects, string? setId)
    {
        if (string.IsNullOrWhiteSpace(setId))
        {
            return 0;
        }

        int number = 0;

        foreach (EffectDef effect in effects)
        {
            number++;

            if (string.Equals(effect.SetId, setId, StringComparison.Ordinal))
            {
                return number;
            }
        }

        return 0;
    }

    /// <summary>
    /// 这张卡上定义的全部集合（ID + 定义它的效果编号），按效果顺序。
    /// 界面上那个带下拉提示的输入框就照它列（不可多选：填一个）。
    /// </summary>
    public static IReadOnlyList<(string Id, int Number)> Definitions(IEnumerable<EffectDef> effects)
    {
        var found = new List<(string, int)>();
        int number = 0;

        foreach (EffectDef effect in effects)
        {
            number++;

            if (!string.IsNullOrWhiteSpace(effect.SetId))
            {
                found.Add((effect.SetId!, number));
            }
        }

        return found;
    }
}
