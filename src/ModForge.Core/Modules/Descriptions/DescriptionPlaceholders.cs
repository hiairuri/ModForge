using ModForge.Core.Model;
using ModForge.Core.Modules.Effects;
using ModForge.Core.Modules.ValueSources;
using ModForge.Core.Modules.Variables;

namespace ModForge.Core.Modules.Descriptions;

/// <summary>
/// 描述编辑要用的三件事，都从这里取：
///
///   <see cref="For"/>     —— 这张卡**能**引用哪些占位符（界面上的"插入数值"按钮，附带可读标签）
///   <see cref="Suggest"/> —— 按当前效果生成一段描述（"按效果自动填充"）
///   <see cref="Extract"/> —— 从一段模板里取出占位符名（校验用：描述里引用了卡上没有的变量要报错）
///
/// 三件事共用同一份"哪个效果有哪些变量"的规划（<c>VariableModule</c>），所以界面上给得出的按钮、
/// 自动填充写出来的占位符、校验认可的名字，三者永远一致——分开写迟早会出现
/// "按钮给了、生成时却没有那个变量"这种只有游戏里才会发现的错。
/// </summary>
public static class DescriptionPlaceholders
{
    /// <summary>
    /// 这张卡的描述里**可以**引用哪些占位符，以及它们各自的可读标签。
    ///
    /// 只发**真的会有对应变量**的那些：占位符写进描述后由游戏格式化，卡上没有那个变量
    /// 就会格式化失败。所以这里跟着变量模块的规划走，而不是另外判断一遍。
    /// </summary>
    public static IReadOnlyList<(string Placeholder, string Label)> For(
        ModProject project,
        CardDef card,
        string language = GameLanguages.Fallback)
    {
        IReadOnlyList<EffectDef> resolved = EffectResolver.Resolve(project, card.Effects);
        var result = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string placeholder, string label)
        {
            if (seen.Add(placeholder))
            {
                result.Add((placeholder, label));
            }
        }

        bool zh = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        // 每条带数值的效果各给一个占位符（Damage / Damage2 / CalculatedDamage / CalculatedDamage2 …）。
        foreach (VarSlot slot in VariableModule.Slots(project, resolved))
        {
            EffectDef effect = slot.Effect;

            if (effect.IsCalculated)
            {
                bool block = effect.Kind == EffectKind.GainBlock;

                Add(slot.Name, zh
                    ? $"{(block ? "获得的格挡" : "造成的伤害")}（= {SourceCatalog.Describe(effect.Source, language)}）"
                    : $"{(block ? "block gained" : "damage dealt")} (= {SourceCatalog.Describe(effect.Source, language)})");

                // 算式的三个零件各有变量、也各有意义，所以都给按钮：
                // 基础值（" + 6"那一段）、每单位系数（"2 × 手牌数"里的 2）、以及计算值本身。
                // 不给的话作者只能手打这些名字，打错就是游戏内格式化失败。
                Add(slot.BaseVar!, zh
                    ? $"基础值（= {effect.EffectiveBaseValue}）"
                    : $"base value (= {effect.EffectiveBaseValue})");

                Add(slot.ExtraVar!, zh
                    ? $"变量系数（= {DescriptionModule.EffectNames.Trim(effect.Source.PerUnit)}）"
                    : $"coefficient (= {DescriptionModule.EffectNames.Trim(effect.Source.PerUnit)})");

                continue;
            }

            string label = effect.Kind switch
            {
                EffectKind.DealDamage => zh ? "伤害" : "damage",
                EffectKind.GainBlock => zh ? "格挡" : "block",
                EffectKind.Summon => zh ? "召唤" : "summon",
                EffectKind.Forge => zh ? "铸造" : "forge",
                EffectKind.GainMaxHp => zh ? "最大生命" : "Max HP",
                EffectKind.LoseMaxHp => zh ? "失去最大生命" : "Max HP lost",
                EffectKind.BoostCardDamage => zh ? "伤害增量" : "damage increase",
                EffectKind.DrawCards => zh ? "牌数" : "cards",
                EffectKind.ApplyPower => PowerLabel(project, effect.PowerId, language),

                // 下面这些以前不占槽，所以没有标签、按钮上也看不到它们。
                EffectKind.GainEnergy => zh ? "能量" : "energy",
                EffectKind.GainStars => zh ? "星辰" : "stars",
                EffectKind.Heal => zh ? "回复生命" : "HP healed",
                EffectKind.LoseHp => zh ? "失去生命" : "HP lost",
                EffectKind.ChannelOrb => zh ? "生成球数" : "orbs channeled",
                EffectKind.EvokeOrbs => zh ? "激发球数" : "orbs evoked",
                EffectKind.AddOrbSlots => zh ? "球位" : "orb slots",
                EffectKind.CopyCardsToPile => zh ? "复制份数" : "copies",
                EffectKind.AddNamedCardToPile => zh ? "加入张数" : "cards added",
                // 消耗牌的张数就是它的数值（见 VariableModule：它占 ExhaustCount 槽）。
                EffectKind.ExhaustCards => zh ? "消耗张数" : "cards to exhaust",
                EffectKind.AddReplay => zh ? "额外打出次数" : "extra plays",
                EffectKind.AutoPlay => zh ? "自动打出张数" : "cards auto-played",
                EffectKind.ModifyCardCost => zh ? "费用增减" : "cost change",

                _ => slot.BaseName,
            };

            Add(slot.Name, $"{label} ({effect.Amount})");
        }

        // 可升级的重复次数各占一个 RepeatVar，描述里可以引用它（"重复 {Repeat:diff()} 次"）。
        foreach (VarSlot slot in VariableModule.RepeatSlots(project, resolved))
        {
            Add(slot.Name, zh
                ? $"重复次数（= {Math.Max(1, slot.Effect.Repeat)}）"
                : $"repeat count (= {Math.Max(1, slot.Effect.Repeat)})");
        }

        // 两个费用阈值各占一个变量（筛了那一维才有）：卡面要能写"能量消耗大于 {EnergyAmount:diff()}"，
        // 所以按钮上也得能看到它——不给的话作者只能手打变量名，打错就是游戏内格式化失败。
        foreach (VarSlot slot in VariableModule.ThresholdSlots(project, resolved))
        {
            string amount = DescriptionModule.EffectNames.Trim(slot.Effect.NumericOf(slot.Role).Base);

            Add(slot.Name, slot.Role == ValueRole.Energy
                ? (zh ? $"能量阈值（= {amount}）" : $"energy threshold (= {amount})")
                : (zh ? $"星辰阈值（= {amount}）" : $"star threshold (= {amount})"));
        }

        return result;
    }

    /// <summary>
    /// 按卡牌当前的效果生成描述，供编辑器"按效果自动填充"使用。
    ///
    /// 句式按目标语言给出，而不是先写英文再翻译：卡牌文本是直接展示给玩家的句子。
    /// </summary>
    public static string Suggest(
        ModProject project,
        CardDef card,
        string language = GameLanguages.Fallback)
    {
        IReadOnlyList<EffectDef> resolved = EffectResolver.Resolve(project, card.Effects);
        SentenceSet sentences = SentenceLibrary.For(language);
        var lines = new List<string>();

        // 每条效果引用**它自己**那个变量（Damage / Damage2、CalculationBase / CalculationBase2…），
        // 名字由变量模块的规划一处给出。
        //
        // 写字面数字也能读，但那样升级后卡面文字不会变——原版全是 {Damage:diff()} 这样的占位符，
        // 数值跟着升级走、也才有"提升了多少"的绿字。两条同类效果写同一个字面数字时，
        // 作者还得自己判断该填哪个序号；这里直接给出正确的那一个。
        var slots = VariableModule.Slots(project, resolved)
            .ToDictionary(s => s.Effect, s => s, EffectResolver.EffectReferenceComparer.Instance);

        var repeats = VariableModule.RepeatSlots(project, resolved)
            .ToDictionary(s => s.Effect, s => s.Name, EffectResolver.EffectReferenceComparer.Instance);

        // 费用阈值的变量名（EnergyAmount / StarAmount）：与生成器读的是同一份规划，
        // 所以描述里引用的占位符一定在卡上声明了。
        var thresholds = new Dictionary<EffectDef, (string? Energy, string? Star)>(
            EffectResolver.EffectReferenceComparer.Instance);

        foreach (VarSlot slot in VariableModule.ThresholdSlots(project, resolved))
        {
            thresholds.TryGetValue(slot.Effect, out (string? Energy, string? Star) pair);
            thresholds[slot.Effect] = slot.Role == ValueRole.Energy
                ? (slot.Name, pair.Star)
                : (pair.Energy, slot.Name);
        }

        foreach (EffectDef effect in resolved)
        {
            slots.TryGetValue(effect, out VarSlot? slot);
            thresholds.TryGetValue(effect, out (string? Energy, string? Star) pair);

            // 有变量名就交过去（数值槽、次数槽、费用阈值槽任意一个有都算）：
            // 没有数值槽的效果（升级/复制这类"数值另有含义"的）也可能筛费用，
            // 那种效果的阈值同样要写成占位符。
            DescriptionModule.EffectNames? names = null;

            if (slot is not null || pair.Energy is not null || pair.Star is not null)
            {
                names = new DescriptionModule.EffectNames(
                    Value: slot?.Name,
                    BaseValue: slot?.BaseVar,
                    ExtraValue: slot?.ExtraVar,
                    Repeat: repeats.TryGetValue(effect, out string? r) ? r : null,
                    EnergyThreshold: pair.Energy,
                    StarThreshold: pair.Star);
            }

            lines.Add(DescriptionModule.Describe(project, effect, language, names));
        }

        if (card.Keywords.Contains(CardKeyword.Exhaust))
        {
            // 关键词的写法也在句式表里，这样"消耗。"只有一个来源。
            lines.Add(sentences.Word("exhaustKeyword")
                ?? (sentences.Language == "zhs" ? "消耗。" : "Exhaust."));
        }

        return lines.Count > 0 ? string.Join("\n", lines) : "";
    }

    /// <summary>
    /// 从描述模板里取出顶层占位符名（<c>{Damage:diff()}</c> → <c>Damage</c>），供校验使用。
    ///
    /// 这是"这段文本引用了哪些变量"的**唯一**判据：校验器与测试都读它，所以
    /// "游戏里会格式化失败的那些引用"只有一处判断——以前校验器里还有一份拷贝，两处迟早会分叉。
    /// </summary>
    public static IEnumerable<string> Extract(string template)
    {
        var result = new List<string>();
        int depth = 0;
        int start = -1;

        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];

            if (c == '{')
            {
                if (depth == 0)
                {
                    start = i + 1;
                }

                depth++;
            }
            else if (c == '}')
            {
                depth--;

                if (depth == 0 && start >= 0 && start < i)
                {
                    string inner = template[start..i];
                    int cut = inner.IndexOfAny(new[] { ':', '(', '}' });
                    string name = (cut >= 0 ? inner[..cut] : inner).Trim();

                    if (name.Length > 0 && char.IsLetter(name[0]))
                    {
                        result.Add(name);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>状态在描述里的显示名：用本工程自己定义的名字，否则退回 id 整理形式。</summary>
    private static string PowerLabel(ModProject project, string? powerId, string language)
    {
        if (string.IsNullOrWhiteSpace(powerId))
        {
            return "power";
        }

        string placeholder = VariableModule.PowerPlaceholder(project, powerId);
        PowerDef? owner = project.Powers
            .FirstOrDefault(p => project.PowerTypeName(p) == placeholder);

        return owner is not null ? owner.Name.GetOrFallback(language) : placeholder;
    }
}
