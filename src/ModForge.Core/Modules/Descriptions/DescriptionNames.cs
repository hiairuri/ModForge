using ModForge.Core.Model;

namespace ModForge.Core.Modules.Descriptions;

/// <summary>
/// 名词：把模型 id 说成玩家认得的名字（卡名 / 状态名 / 球名 / 附魔名）。
///
/// 三处来源，按优先级：
///
///   本工程自己定义的（<c>ModProject</c> 里的卡与状态）—— 用作者填的名字；
///   原版内容 —— 用游戏自己的叫法（查 <see cref="VanillaNames"/>，表在 PCK 里）；
///   查不到 —— 把 id 整理成可读形式（<c>Naming.ToPascal</c>），保证句子仍然读得通。
///
/// 术语一律跟游戏走，不在这里另写一份翻译：游戏更新或换语言时这里不用改。
/// </summary>
internal static class DescriptionNames
{
    /// <summary>
    /// 附魔名。用游戏本地化表里的正式叫法（SHARP -> 锋利），查不到就按 id 反推。
    /// 与卡牌、状态名同一套做法。
    /// </summary>
    public static string EnchantmentName(string? enchantmentId, SentenceSet sentences)
    {
        if (string.IsNullOrWhiteSpace(enchantmentId))
        {
            return sentences.Word("enchantFallback") ?? "enchantment";
        }

        string entry = enchantmentId.Contains('.')
            ? enchantmentId[(enchantmentId.LastIndexOf('.') + 1)..]
            : enchantmentId;

        return DescriptionModule.Names?.Lookup($"ENCHANTMENT.{entry}", sentences.Language)
            ?? Naming.ToPascal(entry);
    }

    /// <summary>
    /// 能量球名。游戏表里是"闪电""冰霜"这样**不带"球"字**的名字，模板负责补上"充能球"。
    ///
    /// 查不到时退回**枚举名**（Lightning / Frost…），不用句式表里的"充能球/Orb"那种词：
    /// 那种词本身就是"球"的意思，而模板后面还会再补一次后缀，于是会印成
    /// "生成 1 个充能球充能球"（实测过）。枚举名还与本工程其它"没有游戏目录"的兜底一致
    /// （界面里的球种下拉同样退回枚举名）。
    /// </summary>
    public static string OrbName(OrbKind orb, SentenceSet sentences) =>
        DescriptionModule.Names?.Lookup($"ORB.{orb.ToString().ToUpperInvariant()}_ORB", sentences.Language)
        ?? orb.ToString();

    /// <summary>
    /// 把几个球种名连成一串（"闪电、冰霜" / "Lightning or Frost"）。
    ///
    /// 连接词来自句式表的 <c>orbJoin</c>（中文顿号、英文 or），所以两种语言的读法各自自然，
    /// 不需要在这里判语言。空列表返回空串——调用方据此知道"没有筛种类"。
    /// </summary>
    public static string JoinOrbNames(IReadOnlyList<string> names, string join)
    {
        if (names.Count == 0)
        {
            return string.Empty;
        }

        return join.Length == 0 ? string.Concat(names) : string.Join(join, names);
    }

    /// <summary>
    /// 状态显示名。本项目定义的状态用它的名字；原版状态优先用游戏自己在当前语言下的叫法，
    /// 查不到再去掉模型 id 的 "POWER" 后缀。
    /// </summary>
    public static string PowerName(ModProject? project, string? powerId, string language)
    {
        if (string.IsNullOrWhiteSpace(powerId))
        {
            return "power";
        }

        string entry = powerId.Contains('.') ? powerId[(powerId.LastIndexOf('.') + 1)..] : powerId;

        PowerDef? local = project?.Powers.FirstOrDefault(p =>
            Naming.ToEntry(p.Id) == entry || Naming.ToEntry(p.Id) + "_POWER" == entry);

        if (local is not null)
        {
            return local.Name.GetOrFallback(language);
        }

        string? vanilla = DescriptionModule.Names?.Lookup(powerId, language);
        if (!string.IsNullOrWhiteSpace(vanilla))
        {
            return vanilla;
        }

        string pascal = Naming.ToPascal(entry);
        return pascal.EndsWith("Power", StringComparison.Ordinal) && pascal.Length > 5
            ? pascal[..^5]
            : pascal;
    }

    /// <summary>
    /// 卡牌显示名。本项目定义的卡用它的名字；原版卡优先用游戏自己的叫法，
    /// 查不到就把模型 id 整理成可读形式。
    /// </summary>
    public static string CardName(ModProject? project, string? cardId, string language)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return SentenceLibrary.For(language).Word("cardFallback") ?? "a card";
        }

        string entry = cardId.Contains('.') ? cardId[(cardId.LastIndexOf('.') + 1)..] : cardId;

        CardDef? local = project?.Cards.FirstOrDefault(c =>
            ModelIds.Slugify(project.CardTypeName(c)) == entry);

        if (local is not null)
        {
            return local.Name.GetOrFallback(language);
        }

        return DescriptionModule.Names?.Lookup(cardId, language) ?? Naming.ToPascal(entry);
    }

    /// <summary>指定卡牌筛选时那张牌的名字。</summary>
    public static string CardNameForFilter(ModProject? project, EffectDef effect, SentenceSet sentences) =>
        string.IsNullOrWhiteSpace(effect.CardId)
            ? sentences.Word("cardFallback") ?? "card"
            : CardName(project, effect.CardId, sentences.Language);
}
