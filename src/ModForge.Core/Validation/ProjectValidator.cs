using ModForge.Core.Catalog;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.CardSets;
using ModForge.Core.Modules.Cards;
using ModForge.Core.Modules.Descriptions;
using ModForge.Core.Modules.Effects;
using ModForge.Core.Modules.Targets;
using ModForge.Core.Modules.Timing;

namespace ModForge.Core.Validation;

public sealed record ProjectIssue(string Severity, string Message, string? Where = null);

/// <summary>
/// Validates a project before export. The checks here target the failures that are
/// otherwise only discovered as a game crash or a card that silently never appears.
/// </summary>
public static class ProjectValidator
{
    private static readonly HashSet<string> RewardRarities = new(StringComparer.Ordinal)
    {
        nameof(CardRarity.Common), nameof(CardRarity.Uncommon), nameof(CardRarity.Rare),
    };

    public static IReadOnlyList<ProjectIssue> Validate(ModProject project, VanillaCatalog? catalog)
    {
        var issues = new List<ProjectIssue>();

        if (string.IsNullOrWhiteSpace(project.Id))
        {
            issues.Add(new ProjectIssue("error", "Mod id is required; it names the DLL and PCK."));
        }
        else if (project.Id.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '.'))
        {
            issues.Add(new ProjectIssue(
                "error",
                $"Mod id '{project.Id}' contains characters that are unsafe in a file name.",
                project.Id));
        }

        if (project.Cards.Count == 0 && project.Powers.Count == 0)
        {
            issues.Add(new ProjectIssue("warn", "This project has no cards or powers yet."));
        }

        // Generated C# type names must be unique and collision-free.
        var typeNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var modelIds = new Dictionary<string, string>(StringComparer.Ordinal);

        ValidateSharedEffects(project, issues);

        foreach (CardDef card in project.Cards)
        {
            ValidateCard(project, card, catalog, issues, typeNames, modelIds);

            // 固有属性的**组合**检查（X 与升级增量、星辰费用为负…）在卡牌核心模块里，
            // 与出码读同一份视图（CardCore）——两处各判一遍的话，改了生成就会与校验脱节。
            foreach (CardCoreRules.Issue coreIssue in CardCoreRules.Check(CardCore.From(card)))
            {
                issues.Add(new ProjectIssue(coreIssue.Severity, coreIssue.Message, card.Id));
            }
        }

        foreach (PowerDef power in project.Powers)
        {
            ValidatePower(project, power, catalog, issues, typeNames, modelIds);
        }

        // Every applied power must exist, either as a generated power or as vanilla.
        var knownPowerEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PowerDef p in project.Powers)
        {
            knownPowerEntries.Add(Naming.ToEntry(p.Id));
        }

        if (catalog is not null)
        {
            foreach (CatalogEntry entry in catalog.ByCategory("POWER"))
            {
                knownPowerEntries.Add(entry.Entry);
            }
        }

        foreach (CardDef card in project.Cards)
        {
            foreach (EffectDef effect in card.Effects)
            {
                ValidatePowerReference(effect, knownPowerEntries, issues, $"card {card.Id}");
            }
        }

        foreach (PowerDef power in project.Powers)
        {
            foreach (TriggeredEffect trigger in power.Triggers)
            {
                ValidatePowerReference(trigger.Effect, knownPowerEntries, issues, $"power {power.Id}");
            }
        }

        if (catalog is not null && !catalog.Entries.Any())
        {
            issues.Add(new ProjectIssue("warn", "Vanilla catalog is empty, so collision checks were skipped."));
        }

        return issues;
    }

    /// <summary>
    /// Checks the shared effect library: ids must be present and unique, references must
    /// resolve, and a definition nothing uses is reported so dead entries can be removed.
    /// </summary>
    private static void ValidateSharedEffects(ModProject project, List<ProjectIssue> issues)
    {
        var byId = new Dictionary<string, SharedEffectDef>(StringComparer.OrdinalIgnoreCase);

        foreach (SharedEffectDef shared in project.SharedEffects)
        {
            if (string.IsNullOrWhiteSpace(shared.Id))
            {
                issues.Add(new ProjectIssue("error", $"Shared effect '{shared.Name}' has no id, so it cannot be referenced.", shared.Name));
                continue;
            }

            string id = Naming.ToEntry(shared.Id);

            if (byId.TryGetValue(id, out SharedEffectDef? existing))
            {
                issues.Add(new ProjectIssue(
                    "error",
                    $"Shared effects '{existing.Name}' and '{shared.Name}' share the id '{shared.Id}'. " +
                    "Ids must be unique because references use them.",
                    shared.Id));
                continue;
            }

            byId[id] = shared;
            ValidateEffect(project, shared.Effect, issues, $"shared effect '{shared.Name}'", isCard: true);
        }

        // Collect every reference so dangling and unused entries are both visible.
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (CardDef card in project.Cards)
        {
            foreach (EffectDef effect in card.Effects)
            {
                NoteReference(project, effect, referenced, issues, $"card '{card.Id}'", byId);
            }
        }

        foreach (PowerDef power in project.Powers)
        {
            foreach (TriggeredEffect trigger in power.Triggers)
            {
                NoteReference(project, trigger.Effect, referenced, issues, $"power '{power.Id}'", byId);
            }
        }

        foreach (SharedEffectDef shared in project.SharedEffects)
        {
            if (!string.IsNullOrWhiteSpace(shared.Id) &&
                !referenced.Contains(Naming.ToEntry(shared.Id)))
            {
                issues.Add(new ProjectIssue(
                    "warn",
                    $"Shared effect '{shared.Name}' is not used by any card or buff, so it has no effect on the mod.",
                    shared.Id));
            }
        }
    }

    private static void NoteReference(
        ModProject project,
        EffectDef effect,
        HashSet<string> referenced,
        List<ProjectIssue> issues,
        string where,
        IReadOnlyDictionary<string, SharedEffectDef> byId)
    {
        if (string.IsNullOrWhiteSpace(effect.SharedEffectId))
        {
            return;
        }

        string id = Naming.ToEntry(effect.SharedEffectId);

        if (!byId.ContainsKey(id))
        {
            issues.Add(new ProjectIssue(
                "error",
                $"{where} references shared effect '{effect.SharedEffectId}', which does not exist. " +
                "The reference will be skipped.",
                where));
            return;
        }

        referenced.Add(id);
    }

    private static void ValidateCard(
        ModProject project,
        CardDef card,
        VanillaCatalog? catalog,
        List<ProjectIssue> issues,
        Dictionary<string, string> typeNames,
        Dictionary<string, string> modelIds)
    {
        if (string.IsNullOrWhiteSpace(card.Id))
        {
            issues.Add(new ProjectIssue("error", "A card is missing its id."));
            return;
        }

        RegisterType(project.CardTypeName(card), $"card {card.Id}", issues, typeNames);

        // The game derives the id from the generated type name, so compute it the same way.
        RegisterModelId($"CARD.{ModelIds.Slugify(project.CardTypeName(card))}", $"card {card.Id}", catalog, issues, modelIds);

        if (!RewardRarities.Contains(card.Rarity.ToString()))
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"Card '{card.Id}' has rarity {card.Rarity}, which never appears in card rewards or shops. " +
                "Use Common, Uncommon or Rare to make it obtainable.",
                card.Id));
        }

        if (card.Type == CardType.Attack &&
            card.Target is CardTargetType.AnyEnemy or CardTargetType.AllEnemies or CardTargetType.RandomEnemy &&
            !card.Effects.Any(e => e.Kind == EffectKind.DealDamage))
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"Attack card '{card.Id}' has no effect that deals damage.",
                card.Id));
        }

        // 效果要"打出时选定的目标"，而这张牌的目标模式给不出一个合用的对象——生成出来
        // cardPlay.Target 是 null（或是个敌人而效果要的是玩家），那些效果全落空。
        // "要什么样的对象"由目标模块按作用域判：造物轴要一个生物（任意敌人 / 任意友方都行）、
        // 玩家轴要**一名玩家**（只有任意友方给得出——敌人身上没有 Player，
        // `cardPlay.Target.Player!` 会空引用）。
        //
        // 按**展开后**的效果列表判：共享效果被这张卡引用时，它里面那条"选定目标"同样要成立
        // （共享效果自己编辑时不知道会被挂在哪种目标的卡上，所以只能在引用它的卡这里判）。
        foreach (EffectDef effect in EffectResolver.Resolve(project, card.Effects)
                     .Where(e => TargetCatalog.UsesPlayedTarget(e.Target)))
        {
            TargetScope scope = EffectCatalog.ScopeOf(effect.Kind);

            if (TargetCatalog.AcceptsPlayedTarget(scope, effect.Target, card.Target))
            {
                continue;
            }

            // 两种"要出牌目标"的档，各自要的东西不同：造物轴的「目标」要一个生物、
            // 「目标的奥斯提」与玩家轴的「目标」要**一名玩家**。提示按各自缺什么说清楚。
            bool wantsPlayer = TargetCatalog.GivesPlayerTargetOnly(scope, effect.Target);

            issues.Add(new ProjectIssue(
                "error",
                $"Card '{card.Id}' has an effect ({effect.Kind}) whose target is {effect.Target}, but the " +
                $"card's target mode is {card.Target}, which gives no {(wantsPlayer ? "player" : "target")} "
                + "to act on. Change the card's target to "
                + (wantsPlayer ? "AnyAlly" : "AnyEnemy or AnyAlly")
                + ", or give the effect a concrete target.",
                card.Id));
        }

        // 这一档在**这条效果所属的那条轴**上根本不成立。两种来路：
        //   宠物那一组      —— 宠物是 Creature，收 Player 的命令接不住它（"给宠物加能量"编译不过），
        //                      作用于牌那一轴与宠物无关；
        //   玩家轴的旧值    —— 比如 RandomAlly：它以前在玩家轴上表示"除自己以外的随机一名玩家"，
        //                      现在那一档改叫 RandomOtherAlly（含自己那档是 RandomPlayer），
        //                      RandomAlly 只留给造物轴（那里它含自己，是不同的意思）。
        // 手改的 JSON 由这里拦，并把该改成哪一档说出来。
        foreach (EffectDef effect in EffectResolver.Resolve(project, card.Effects))
        {
            TargetScope scope = EffectCatalog.ScopeOf(effect.Kind);

            if (TargetCatalog.SupportsBand(scope, effect.Target))
            {
                continue;
            }

            string hint = effect.Target switch
            {
                EffectTarget.RandomAlly when scope == TargetScope.Player =>
                    "Use RandomPlayer (includes you) or RandomOtherAlly (excludes you).",
                _ when TargetCatalog.IsPetBand(effect.Target) =>
                    "A pet is a Creature, so only creature-scoped effects can act on it.",
                _ => "Pick one of the targets this effect offers.",
            };

            issues.Add(new ProjectIssue(
                "error",
                $"Card '{card.Id}' has an effect ({effect.Kind}) whose target is {effect.Target}, but that " +
                $"band does not exist for this kind of effect. {hint}",
                card.Id));
        }

        // ---- 临时牌集合（见 Modules/CardSets）----
        //
        // 这一族规则守的是"键能不能对上"：键是 `地址.定义它的效果编号.集合ID`，
        // 所以"谁定义"必须唯一、ID 必须能进键、被引用的 ID 必须真的有人定义。
        // 出错时的表现都是**静默**的（那条效果读到空集合 → 什么也不做），所以必须拦在这里。
        ValidateCardSets(card, issues);

        // 单人游戏里"任意友方"没有合法目标：游戏会把这张牌直接移进结算牌堆、不结算。
        // 这不是错误（多人合作里它是对的），但单人测试时会表现成"这张牌什么都不做"，
        // 所以提示一句，免得当成 bug 找半天。
        if (card.Target == CardTargetType.AnyAlly)
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"Card '{card.Id}' targets any ally, which only has candidates in multiplayer co-op. " +
                "In single player the game moves the card to the result pile without playing it.",
                card.Id));
        }

        // 反向的一条：声明了"只在多人出现"，却没有任何需要队友的东西——多半是想写"仅单人"。
        // 只提示，不拦：单人牌完全可以是设计选择（与原版"恶魔之盾"那种要有队友的不同）。
        if (card.Multiplayer == CardMultiplayerConstraint.MultiplayerOnly &&
            card.Target is not (CardTargetType.AnyAlly or CardTargetType.AllAllies) &&
            !card.Effects.Any(e => e.Target is EffectTarget.Target or EffectTarget.AllAllies
                or EffectTarget.RandomAlly or EffectTarget.RandomPlayer or EffectTarget.All))
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"Card '{card.Id}' is marked multiplayer-only but nothing about it needs another player. " +
                "Single player will never show this card, so it becomes unreachable content.",
                card.Id));
        }

        ValidateCalculatedVars(project, card, issues);
        ValidateUpgradeLevels(project, card, issues);

        if (card.EnergyCost < 0)
        {
            issues.Add(new ProjectIssue("error", $"Card '{card.Id}' has a negative energy cost.", card.Id));
        }

        ValidateDescriptionVars(project, card, issues, catalog);

        foreach (EffectDef effect in card.Effects)
        {
            ValidateEffect(project, effect, issues, card.Id, isCard: true, cardType: card.Type);
        }
    }

    /// <summary>
    /// 计算型数值相关的检查（现在这里没有规则了，保留函数是为了记下曾经有什么、为什么去掉）。
    ///
    /// **不再有"同一种数值只能一条"的限制**：每条带数值的效果各占一个变量槽
    /// （见 <see cref="EffectResolver.EffectSlots"/>），所以一张牌可以有多条伤害、多条格挡、
    /// 同一个状态施加多次，各是各的数值、各升各的。原版"流光"就是两个格挡变量各自升级。
    /// 那条报错是**生成器的**限制（它把所有同种效果合并到一个变量），不是游戏的。
    ///
    /// **"计算型却没有来源"也构造不出来了**：是否计算就取决于来源有没有选
    /// （见 <see cref="EffectDef.IsCalculated"/>）。早先"数值方式"是一个独立的下拉，
    /// 才会出现"选了计算型但没选来源"这种需要报错拦截的组合。
    /// </summary>
    /// <summary>
    /// 临时牌集合那几条规则。四条都指向"键对不上"，而对不上的表现是**静默**的：
    /// 读的那条效果拿到空集合，于是"什么也没做"，卡面上却写着它要做的事。
    /// </summary>
    private static void ValidateCardSets(CardDef card, List<ProjectIssue> issues)
    {
        // 1. 谁能存 / 谁能取：填了集合 ID 的效果必须真的产出一组牌，取集合的效果必须真的能取。
        //    拦的是手改的 JSON，以及"效果种类换了但旧字段留下"。
        foreach (EffectDef effect in card.Effects)
        {
            if (!string.IsNullOrWhiteSpace(effect.SetId) && !CardSetCatalog.Stores(effect.Kind))
            {
                issues.Add(new ProjectIssue(
                    "error",
                    $"Card '{card.Id}' stores a temp card set from effect {effect.Kind}, but that effect " +
                    "does not produce a group of cards — there is nothing to store.",
                    card.Id));
            }

            if (effect.Pick.Source == PickSource.Set && !CardSetCatalog.Reads(effect.Kind))
            {
                issues.Add(new ProjectIssue(
                    "error",
                    $"Card '{card.Id}' reads a temp card set in effect {effect.Kind}, but that effect has no " +
                    "\"card set\" source (auto-play takes cards straight from a pile).",
                    card.Id));
            }
        }

        // 2. 集合 ID 要能进键：键用 "." 分段，所以 ID 里不许有点、空格、非 ASCII。
        //    带点的 ID 会让"按地址前缀清空"误伤别的地址。
        foreach (EffectDef effect in card.Effects.Where(e => !string.IsNullOrWhiteSpace(e.SetId)))
        {
            if (!CardSetCatalog.IsValidId(effect.SetId))
            {
                issues.Add(new ProjectIssue(
                    "error",
                    $"Card '{card.Id}' has a temp card set id '{effect.SetId}', which cannot be used as a key " +
                    "part. Use letters, digits and underscores only (the runtime key is dot-separated).",
                    card.Id));
            }
        }

        // 3. 一个 ID 在**一张牌**上只能定义一次：读它的那条效果要按"定义它的效果编号"拼键，
        //    重复定义时不知道读哪一个（生成侧取第一个，静默偏一半）。
        foreach (IGrouping<string, EffectDef> group in card.Effects
                     .Where(e => !string.IsNullOrWhiteSpace(e.SetId))
                     .GroupBy(e => e.SetId!, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            int[] numbers = card.Effects
                .Select((e, i) => (e, n: i + 1))
                .Where(t => string.Equals(t.e.SetId, group.Key, StringComparison.Ordinal))
                .Select(t => t.n)
                .ToArray();

            issues.Add(new ProjectIssue(
                "error",
                $"Card '{card.Id}' defines temp card set '{group.Key}' more than once (effects " +
                $"{string.Join(", ", numbers)}) — a reader could not tell which one it means.",
                card.Id));
        }

        // 4. 读的集合必须有人定义（同一张牌上）。**这是最要紧的一条**：指错了生成出来的是
        //    空集合，游戏里什么都不做，而卡面照写——不拦就等于让作者自己猜。
        foreach (EffectDef effect in card.Effects.Where(e => e.Pick.Source == PickSource.Set))
        {
            if (string.IsNullOrWhiteSpace(effect.Pick.SetId))
            {
                issues.Add(new ProjectIssue(
                    "error",
                    $"Card '{card.Id}' reads a temp card set but no set id is chosen.",
                    card.Id));
                continue;
            }

            if (CardSetCatalog.DefinitionNumber(card.Effects, effect.Pick.SetId) == 0)
            {
                issues.Add(new ProjectIssue(
                    "error",
                    $"Card '{card.Id}' reads temp card set '{effect.Pick.SetId}', but no effect on this card " +
                    "defines it. Sets live for one card play and only within the same card, so the defining " +
                    "effect must be on this card and run before this one.",
                    card.Id));
            }
        }
    }

    private static void ValidateCalculatedVars(
        ModProject project,
        CardDef card,
        List<ProjectIssue> issues)
    {
    }

    /// <summary>
    /// 检查多次升级（等级表）的配置。
    ///
    /// 等级表是**增量模型之外**的另一条路：填了它，生成器就按等级绝对赋值，不再叠加
    /// <see cref="EffectUpgrade"/> 里的增量。所以两处都填时必须说清楚——否则作者会以为
    /// 两者会叠加，实际是等级表覆盖掉增量（那是静默的语义差，不是报错）。
    ///
    /// 另外如实说明一件事：**卡面描述只有"升级前 / 升级后"两态**。
    /// 游戏的描述里能用的开关是 <c>{IfUpgraded:升级后|升级前}</c>，它分不出第几级
    /// （原版一张多级升级的牌都没有，这是游戏没做的部分）。所以多次升级时，
    /// 描述里引用变量只显示**当前等级**的数，看不到"下一级是多少"。
    /// </summary>
    private static void ValidateUpgradeLevels(
        ModProject project,
        CardDef card,
        List<ProjectIssue> issues)
    {
        int max = card.MaxUpgradeLevel;

        if (max < 0)
        {
            issues.Add(new ProjectIssue(
                "error",
                $"Card '{card.Id}' has a negative MaxUpgradeLevel, which the game would reject.",
                card.Id));
            return;
        }

        IReadOnlyList<EffectDef> resolved = EffectResolver.Resolve(project, card.Effects);
        bool anyLevels = resolved.Any(e => e.HasLevels);

        // 填了等级表但只能升一次：等级表的第 2 项起永远读不到，等于白填。
        if (anyLevels && max <= 1)
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"Card '{card.Id}' has per-level values but MaxUpgradeLevel is {max}, so only the first "
                + "entry is ever used. Raise the upgrade limit to use the rest.",
                card.Id));
        }

        // 说了能升多次，却没有一条效果给等级表：那会升一次就到头（默认值不变），
        // 与"能升 2 次"的设定不符——升第 2 级时什么也不会发生。
        if (max > 1 && !anyLevels)
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"Card '{card.Id}' can be upgraded {max} times but no effect has per-level values, "
                + "so the 2nd upgrade would change nothing.",
                card.Id));
        }

        foreach (EffectDef effect in resolved)
        {
            if (!effect.HasLevels)
            {
                continue;
            }

            if (effect.Upgrade.IsSet)
            {
                issues.Add(new ProjectIssue(
                    "warn",
                    $"Effect {effect.Kind} of card '{card.Id}' has both per-level values and an upgrade "
                    + "delta. The per-level table wins; the delta is ignored.",
                    card.Id));
            }

            // 固定值效果的等级表改不了"来源/系数"：那两个东西属于算式，而"这张牌是固定值还是
            // 算出来的"由 0 级决定，整张牌的代码形状跟着它走，一张牌里两种都有不了。
            // 填了会静默失效，所以要说出来（想换算式就把 0 级也设成计算型）。
            if (!effect.IsCalculated &&
                effect.Levels!.Any(l => l is { Source: not null } or { PerUnit: not null }))
            {
                issues.Add(new ProjectIssue(
                    "warn",
                    $"Effect {effect.Kind} of card '{card.Id}' sets a formula source or per-unit at some "
                    + "level, but the effect's own value is fixed (no source). Whether an effect is "
                    + "calculated is decided at level 0, so those entries do nothing.",
                    card.Id));
            }

            // 等级表比"能升的级数"短时，多出来的级沿用最后一项——
            // 这是刻意的行为（作者不必把每级写满），但要说一声，免得以为是漏了。
            if (effect.Levels!.Count < max)
            {
                issues.Add(new ProjectIssue(
                    "info",
                    $"Effect {effect.Kind} of card '{card.Id}' lists {effect.Levels.Count} level(s) but the "
                    + $"card can be upgraded {max} times; later levels reuse the last entry.",
                    card.Id));
            }
        }
    }

    private static void ValidatePower(
        ModProject project,
        PowerDef power,
        VanillaCatalog? catalog,
        List<ProjectIssue> issues,
        Dictionary<string, string> typeNames,
        Dictionary<string, string> modelIds)
    {
        if (string.IsNullOrWhiteSpace(power.Id))
        {
            issues.Add(new ProjectIssue("error", "A power is missing its id."));
            return;
        }

        RegisterType(project.PowerTypeName(power), $"power {power.Id}", issues, typeNames);
        RegisterModelId($"POWER.{ModelIds.Slugify(project.PowerTypeName(power))}", $"power {power.Id}", catalog, issues, modelIds);

        if (power.Stack == PowerStack.Single && power.DamageModifier == DamageModifier.Additive)
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"Power '{power.Id}' uses Single stacking, so its Amount is always 1 and the additive " +
                "damage modifier will always add exactly 1. Use Counter to scale with stacks.",
                power.Id));
        }

        if (power.DamageModifier == DamageModifier.Multiplicative && power.Kind == PowerKind.Buff)
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"Power '{power.Id}' multiplies damage by 1+Amount%, which for a buff grows very fast. " +
                "Consider Additive, or keep amounts small.",
                power.Id));
        }

        if (power.Decay == DecayMode.Never && power.Kind == PowerKind.Buff &&
            power.DamageModifier == DamageModifier.Additive)
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"Power '{power.Id}' never decays, so its damage bonus accumulates for the whole combat.",
                power.Id));
        }

        if (power.Description.IsEmpty)
        {
            issues.Add(new ProjectIssue(
                "error",
                $"Power '{power.Id}' has no description, which would leave an empty tooltip.",
                power.Id));
        }
        else if (power.Stack == PowerStack.Counter &&
                 !power.Description.GetOrFallback(GameLanguages.Fallback).Contains("{Amount", StringComparison.Ordinal))
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"Power '{power.Id}' stacks a visible amount but its description never shows {{Amount}}.",
                power.Id));
        }

        // 状态没有"打出时选定的目标"（那是目标那张牌的东西）。旧数据里这一档是存在的，
        // 生成时按旧口径落地（造物轴＝所有敌人、玩家轴＝自己，见 TargetEmitter 的说明），
        // 所以**不会什么也不做**——但那一档的名字在这里名不副实，提示一句让人改成明确的档。
        //
        // 例外：「目标的奥斯提」在状态里**不是**"按旧口径落地"，而是真的落不了地
        // （状态没有出牌目标，也就没有"那名玩家"，宠物取不到）——生成出来是一句
        // "没有可用目标"，等于这条效果白写。这一种报错，并指出该改成哪几档。
        //
        // 「目标」那一档则保留旧口径（造物轴＝所有敌人、玩家轴＝自己），所以只提醒。
        foreach (TriggeredEffect trigger in power.Triggers)
        {
            EffectDef effect = EffectResolver.Resolve(project, new[] { trigger.Effect }).FirstOrDefault()
                ?? trigger.Effect;

            if (effect.Target == EffectTarget.TargetPet)
            {
                issues.Add(new ProjectIssue(
                    "error",
                    $"Power '{power.Id}' has a trigger effect aimed at the played target's pet, but powers " +
                    "have no played target. Pick SelfPet / AllAlliesPet / RandomAllyPet to name a pet.",
                    power.Id));
                continue;
            }

            if (effect.Target != EffectTarget.Target)
            {
                continue;
            }

            issues.Add(new ProjectIssue(
                "warn",
                $"Power '{power.Id}' has a trigger effect targeting the played target, but powers have no " +
                "played target — that effect falls back to all enemies (creature effects) or yourself " +
                "(player effects). Pick a concrete target to say what you mean.",
                power.Id));
        }

        foreach (TriggeredEffect trigger in power.Triggers)
        {
            // 状态没有动态变量表，所以它的数值只能是固定的。
            //
            // 计算型的伤害/格挡在生成时会走 `DynamicVars.CalculatedDamage.Calculate(...)`
            // ——那条路只有卡牌成立（卡牌通过 CanonicalVars 声明那些变量）。状态侧发出的类
            // 不声明任何变量，这句会在运行时抛 KeyNotFoundException；而且是在画状态说明、
            // 也就是战斗刚开始时就抛。
            //
            // 计算型的其它种类（施加状态的量、牌堆张数……）会去调一个取值方法，
            // 而方法只在卡牌的生成路径里发出——那种是编译不过，响得比这个早，但提示不明确，
            // 所以两类一起在这里拦下。
            EffectDef triggerEffect = trigger.Effect;

            if (triggerEffect.IsCalculated || triggerEffect.CalculatedRepeat)
            {
                issues.Add(new ProjectIssue(
                    "error",
                    $"Power '{power.Id}' has a calculated effect in its '{trigger.When}' trigger, but powers "
                    + "have no dynamic variables to calculate from. Use a fixed value, or make it a card effect.",
                    power.Id));
            }

            ValidateEffect(project, triggerEffect, issues, power.Id, isCard: false);
        }
    }

    private static void ValidatePowerReference(
        EffectDef effect,
        HashSet<string> knownPowerEntries,
        List<ProjectIssue> issues,
        string where)
    {
        // "状态层数"来源也要引用一个状态（source.PowerId），一并在这里查。
        foreach (ValueSource source in new[] { effect.Source, effect.RepeatSource })
        {
            if (source.Kind != ValueSourceKind.PowerStacks)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(source.PowerId))
            {
                issues.Add(new ProjectIssue(
                    "error",
                    $"{where}: a \"power stacks\" value source needs a power selected.",
                    where));
                continue;
            }

            ValidatePowerEntry(source.PowerId, knownPowerEntries, issues, where, "value source");
        }

        if (effect.Kind is not (EffectKind.ApplyPower or EffectKind.RemovePower))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(effect.PowerId))
        {
            // Already reported by ValidateEffect.
            return;
        }

        ValidatePowerEntry(effect.PowerId, knownPowerEntries, issues, where, "effect");
    }

    /// <summary>引用的状态是不是原版的、或本工程定义的。</summary>
    private static void ValidatePowerEntry(
        string powerId,
        HashSet<string> knownPowerEntries,
        List<ProjectIssue> issues,
        string where,
        string what)
    {
        string powerEntry = powerId.Contains('.')
            ? powerId[(powerId.LastIndexOf('.') + 1)..]
            : powerId;

        if (!knownPowerEntries.Contains(powerEntry))
        {
            issues.Add(new ProjectIssue(
                "error",
                $"{where}: {what} references power '{powerId}', which is not a vanilla power and is not " +
                "defined in this project.",
                where));
        }
    }

    /// <summary>这个卡牌 id 是不是本工程自己生成的卡（形如 CARD.NEW_CARD）。</summary>
    private static bool IsOurCardId(ModProject project, string cardId) => project.Cards
        .Any(c => string.Equals($"CARD.{Naming.ToEntry(c.Id)}", cardId, StringComparison.OrdinalIgnoreCase));

    /// <param name="cardType">
    /// 这条效果所在卡牌的类型；状态与共享效果定义处为 null。
    ///
    /// 用来拦下"能力牌却反应别人出牌"这种组合——它不会报错，只是永远不触发（见下面的说明）。
    /// </param>
    private static void ValidateEffect(
        ModProject project,
        EffectDef effect,
        List<ProjectIssue> issues,
        string where,
        bool isCard,
        CardType? cardType = null)
    {
        // 时机与牌堆范围的组合检查（"自动打出这张牌"缺时机、计数器阈值 < 1、能力牌反应出牌…）
        // 在时机模块里，与钩子的发出读同一份时机字段——两处各判一遍的话，
        // 改了"什么算依赖钩子派发"就会出现一处放行、一处报错。
        foreach (TimingRules.Issue timingIssue in TimingRules.Check(effect, where, cardType))
        {
            issues.Add(new ProjectIssue(timingIssue.Severity, timingIssue.Message, where));
        }

        // "自动打出这张牌"检查完就到此为止：它的数值 / 费用 / 挑牌那些字段都不参与生成
        // （生成的是"把这张牌打出去"，不是它的效果量），继续往下判只会误报。
        if (effect.IsSelfAutoPlay)
        {
            return;
        }

        // 费用类效果有几处组合会生成出没意义的代码，逐个拦下来：
        //
        //   - 降费/加费的数值为 0：Add* 对 0 会直接不加任何东西（引擎里就是 `if (amount != 0)`），
        //     整条效果等于不存在。
        //   - "免费"配"直到被打出"：游戏只有"本回合"和"本场战斗"两种接口，
        //     没有"免费直到打出"，所以这个组合没法生成（界面上也不会给出，这里是防手改 JSON）。
        //   - 随机/选择时张数 < 1：取不到牌，效果空转。
        if (effect.Kind == EffectKind.ModifyCardCost && effect.Amount == 0)
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"{where}: a {effect.Kind} effect has an amount of 0, so the cost does not change.",
                where));
        }

        // 铸造 0：ForgeCmd.Forge 仍会生成君王之剑，但不加任何伤害——
        // 大概率是把数值忘了填，而不是"只要剑不要伤害"，所以提醒一句。
        if (effect.Kind == EffectKind.Forge && effect.Amount == 0 && !effect.IsCalculated)
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"{where}: a {effect.Kind} effect has an amount of 0; it creates the Sovereign Blade "
                + "but adds no damage to it.",
                where));
        }

        if (effect.Kind == EffectKind.SetCardsFree && effect.CostDuration == CostDuration.UntilPlayed)
        {
            issues.Add(new ProjectIssue(
                "error",
                $"{where}: a {effect.Kind} effect cannot last \"until played\"; the game only offers "
                + "this turn or this combat (use \"reduce the cost\" if you need it to wear off when played).",
                where));
        }

        // 会用"牌堆 + 全部/随机/选择 N 张"那套表单的效果。
        //
        // 判据是效果自己的策略（<see cref="CardPickPolicies"/>），不是手写的名单——名单曾经只列了
        // 4 条，而实际会挑牌的有 9 条，于是另外 5 条配错了也不报错。
        bool picksFromPile = CardPickPolicies.UsesPicker(effect.Kind);

        // 这里原先还有一条"勾了计算型却没选来源"的 error。现在那个状态**表达不出来**了：
        // 计算型由来源推出（见 EffectDef.CalculatedRepeat），没有来源就是固定次数。
        // 少一条校验，是因为少了一种可能出错的状态——比多一条兜底更好。

        // 次数可能算出 0 或负数（例如"每失去一次生命就多打一次"在本场战斗没失去过时，
        // 基础值填 0 就是 0 次）。那本身是合法的玩法，但要明确是**有意**的，所以提示一句。
        if (effect.CalculatedRepeat && effect.Repeat <= 0)
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"{where}: the {effect.Kind} effect can repeat 0 times (base {effect.Repeat} plus "
                + "a per-unit count); it does nothing when the source is 0.",
                where));
        }

        // 次数是固定值（没有变量来源）时，"次数的变量系数"没有东西可乘——填了它不会有任何
        // 效果，因为生成的循环次数根本不读来源。这属于"填了不生效"，要说出来。
        if (!effect.CalculatedRepeat && effect.Upgrade.RepeatPerUnitDelta != 0)
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"{where}: the {effect.Kind} effect sets an upgrade delta for the repeat's per-unit "
                + "coefficient, but its repeat count is a fixed value with no variable to scale.",
                where));
        }

        // "升级后换用的变量"只对**计算型**有意义：固定值没有算式，也就没有变量可换。
        if (!effect.IsCalculated && effect.Upgrade.Source is not null)
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"{where}: the {effect.Kind} effect swaps its formula source on upgrade, but its own "
                + "value is a fixed one (no source). Whether an effect is calculated is decided at "
                + "level 0, so the swap does nothing.",
                where));
        }

        if (!effect.CalculatedRepeat && effect.Upgrade.RepeatSource is not null)
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"{where}: the {effect.Kind} effect swaps its repeat formula source on upgrade, but its "
                + "repeat count is a fixed value.",
                where));
        }

        // 两个费用阈值：基础值与变量系数**各占一个变量**（EnergyAmount / EnergyAmountExtra），
        // 来源在算式里用哪一套也能逐级换——所以升级与等级表都改得到它们，这一块没有
        // "填了不生效"要提示的。（以前系数是算式里的字面量，那时才需要警告。）

        // 有等级表时整条效果的升级由表说了算，"升级后换用的变量"（增量块里那一项）会被忽略。
        //
        // **四个角色都算**：来源只从"增量块"与"等级表"二选一里取（见 EffectEmitter 的
        // SourceVariants 与 ThresholdSourceVariants），费用阈值也一样，所以任一处填了都要说。
        if (effect.HasLevels && EffectUpgrade.AllRoles.Any(role => effect.Upgrade.SourceOf(role) is not null))
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"{where}: the {effect.Kind} effect has per-level values, so the source swap written on "
                + "the upgrade delta is ignored. Put it in the per-level table instead.",
                where));
        }

        // 最大生命：数值 0 等于没做任何事；负数会直接抛异常
        // （CreatureCmd.GainMaxHp / LoseMaxHp 都要求非负，负数是"用另一个方法"的意思）。
        if (effect.Kind is EffectKind.GainMaxHp or EffectKind.LoseMaxHp && effect.Amount < 0 && !effect.IsCalculated)
        {
            issues.Add(new ProjectIssue(
                "error",
                $"{where}: a {effect.Kind} effect takes a non-negative amount (it is {effect.Amount}); "
                + "use the other max-HP effect to go the other way.",
                where));
        }

        // "这张牌自己"只有卡牌能用：状态里没有"这张牌"，生成出来会引用不存在的 this。
        if (effect.Pick.Source == PickSource.ThisCard && !isCard)
        {
            issues.Add(new ProjectIssue(
                "error",
                $"{where}: a {effect.Kind} effect picking \"this card\" can only be used on a card, "
                + "not on a power (a power has no 'this card').",
                where));
        }

        // 张数只对随机/选择有意义："全部"与"这张牌自己"都不需要。
        //
        // （早年这里还有两条"永久增伤/重放只能作用于本 mod 自己的卡"的提醒——
        // 那是因为存档只认目标牌自己类上的 SavedProperty。原版牌现在走**组件**：
        // 数值随存档 props 往返、进战斗时应用（见运行时的 CardComponents），
        // 限制不复存在，两条提醒随之删除。）

        // 按牌名筛时必须填是哪张牌，否则筛不到任何东西。
        if (picksFromPile && effect.Pick.CardIds.Count > 0 &&
            effect.Pick.CardIds.All(string.IsNullOrWhiteSpace))
        {
            issues.Add(new ProjectIssue(
                "error",
                $"{where}: a {effect.Kind} effect is set to apply to a named card but none is chosen, "
                + "so it applies to nothing.",
                where));
        }

        // 张数至少要 1（随机 / 选择 / 至多都要求）。
        //
        // 张数从哪儿读由效果自己的策略决定（与生成器同一判据）：消耗牌这类"base 值就是张数"的效果
        // 读它自己的数值，其余效果读选牌器那一格。写死读 pick.Count 会让前者明明填了 3 张
        // 却被判成"一张都挑不到"（反过来也一样）。
        if (picksFromPile && effect.Pick.Mode is PickMode.Random or PickMode.Choose or PickMode.AtMost)
        {
            bool countFromEffect = CardPickPolicies.For(effect.Kind).CountFromEffect;

            decimal countBase = countFromEffect ? effect.Amount : effect.Pick.Count.Base;
            bool countCalculated = countFromEffect ? effect.Source.IsUsed : effect.Pick.Count.IsCalculated;

            if ((int)countBase < 1 && !countCalculated)
            {
                issues.Add(new ProjectIssue(
                    "error",
                    $"{where}: a {effect.Kind} effect with pick mode {effect.Pick.Mode} needs a count of at least 1 "
                    + $"(it is {countBase}), otherwise it picks nothing. "
                    + (countFromEffect
                        ? "The count is this effect's own value (its <base>)."
                        : "The count is the picker's own setting (<pickCount>)."),
                    where));
            }
        }

        // 变化选了"指定牌"却没给牌：生成时会退回随机变化。这不致命，但与原意不符，要提醒。
        if (effect.Kind == EffectKind.TransformCardsInPile &&
            effect.TransformInto == TransformInto.SpecificCard &&
            string.IsNullOrWhiteSpace(effect.CardId))
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"{where}: a {effect.Kind} effect is set to transform into a specific card but none is chosen, "
                + "so it will transform randomly instead.",
                where));
        }

        // 固定值（没选来源）且数值为 0 才是"什么都不做"；计算型的基础值可以为 0
        // （原版"全身撞击"就是 0 + 1 × 格挡）。
        //
        // 两个例外，都是"数值本来就该是负数/零"的效果：
        //   移除状态   —— 0 层是合法配置（"整个移除"那一档根本不用数值）；
        //   降费/加费  —— 它的数值是**带符号的增量**（-1＝便宜 1 点，原版"精密瞄准"就是），
        //                 按"数值 ≤ 0 就没用"判会误报每一条正常的降费。
        if (!effect.IsCalculated && effect.Amount <= 0 &&
            effect.Kind is not (EffectKind.RemovePower or EffectKind.ModifyCardCost) &&
            !IsPoolChooseIgnoringAmount(effect))
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"{where}: a {effect.Kind} effect has an amount of {effect.Amount}, so it does nothing.",
                where));
        }

        if (effect.Repeat < 1)
        {
            issues.Add(new ProjectIssue(
                "error",
                $"{where}: a {effect.Kind} effect has Repeat {effect.Repeat}; it must be at least 1.",
                where));
        }

        if (effect.Kind is EffectKind.ApplyPower or EffectKind.RemovePower &&
            string.IsNullOrWhiteSpace(effect.PowerId))
        {
            // 不过"移除状态"选"所有正面/负面"时**不必**指定是哪个状态——那两档本来就是
            // 按类别扫的，没有单一的"这个状态"。所以只有"按 id 指定"那一档才要填。
            bool needsPowerId = effect.Kind == EffectKind.ApplyPower
                || effect.Selection == PowerSelection.PowerId;

            if (needsPowerId)
            {
                issues.Add(new ProjectIssue(
                    "error",
                    $"{where}: a {effect.Kind} effect needs a power selected.",
                    where));
            }
        }

        // "整个移除"时数值不参与（整层移除没有"减几层"可言），界面上那几格也隐去——
        // 文件里还留着数值属于"填了不生效"，说出来比留着强。
        if (effect.Kind == EffectKind.RemovePower
            && effect.Removal == PowerRemoval.All
            && (effect.Amount != 0 || effect.IsCalculated))
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"{where}: this RemovePower effect removes an entire power, so its value is not "
                + "used. Switch it to partial removal to take off a number of stacks.",
                where));
        }

        // ---- 加入指定卡牌：两种生成方式各自的规则（见 AddCardMode）----
        if (effect.Kind == EffectKind.AddNamedCardToPile)
        {
            if (effect.AddMode == AddCardMode.ChooseFromPool)
            {
                // 游戏那条选牌界面候选超过 3 张直接抛 ArgumentException——这里拦在编辑器里。
                if (effect.Candidates is < 1 or > 3)
                {
                    issues.Add(new ProjectIssue(
                        "error",
                        $"{where}: this AddNamedCardToPile effect offers {effect.Candidates} candidates, "
                        + "but the game's choose-a-card screen throws for more than 3. Use 1-3.",
                        where));
                }

                // 候选数 ≥ 2 时"加入张数"不参与（每一轮界面固定加入一张选中的牌）。
                if (effect.Candidates >= 2 && (effect.IsCalculated || effect.Amount != 1))
                {
                    issues.Add(new ProjectIssue(
                        "warn",
                        $"{where}: with 2+ candidates this AddNamedCardToPile effect adds exactly one "
                        + "chosen card per round, so its value is not used. Set candidates to 1 to add "
                        + "a number of random cards instead.",
                        where));
                }
            }
            else if (string.IsNullOrWhiteSpace(effect.CardId))
            {
                issues.Add(new ProjectIssue(
                    "warn",
                    $"{where}: this AddNamedCardToPile effect adds a specific card but none is chosen, "
                    + "so it does nothing.",
                    where));
            }
        }

        // ---- 启用条件：静态可判的组合直接说出来 ----
        if (effect.Condition is { } condition)
        {
            // 两侧都是固定值时，条件成立与否是**静态可知**的——恒不成立意味着这条效果
            // 永远不会执行（多半是比错了方向），恒成立意味着条件是摆设（多半忘了配来源）。
            if (!condition.LeftSource.IsUsed && !condition.RightSource.IsUsed)
            {
                int l = condition.LeftAmount;
                int r = condition.RightAmount;

                bool holds = condition.Comparison switch
                {
                    ConditionComparison.AtLeast => l >= r,
                    ConditionComparison.AtMost => l <= r,
                    ConditionComparison.GreaterThan => l > r,
                    ConditionComparison.LessThan => l < r,
                    ConditionComparison.Equal => l == r,
                    _ => l != r,
                };

                issues.Add(new ProjectIssue(
                    holds ? "warn" : "error",
                    $"{where}: this effect's condition compares two constants and is "
                    + (holds ? "always true (the condition does nothing; pick a variable source "
                        + "for one side, or disable it)" : "never true, so the effect never runs")
                    + ".",
                    where));
            }

            // 变量系数（及它的升级增量）乘在来源上：没有来源时它没有意义——
            // 与效果数值那边的"填了不生效"是同一条规矩。
            foreach ((bool isLeft, ValueSource source, decimal perUnitUpgrade) in new[]
            {
                (true, condition.LeftSource, condition.LeftPerUnitUpgrade),
                (false, condition.RightSource, condition.RightPerUnitUpgrade),
            })
            {
                if (!source.IsUsed && perUnitUpgrade != 0)
                {
                    issues.Add(new ProjectIssue(
                        "warn",
                        $"{where}: the condition's {(isLeft ? "left" : "right")} side has no variable "
                        + "source, so its coefficient upgrade does nothing.",
                        where));
                }
            }
        }
    }

    /// <summary>
    /// "从卡池 N 选一"（候选数 ≥ 2）不读效果的数值——每一轮固定加入一张选中的牌。
    /// 这条组合要豁免"数值 ≤ 0 就没用"的误报（见调用处的说明）。
    /// </summary>
    private static bool IsPoolChooseIgnoringAmount(EffectDef effect) =>
        effect.Kind == EffectKind.AddNamedCardToPile &&
        effect.AddMode == AddCardMode.ChooseFromPool &&
        effect.Candidates >= 2;

    /// <summary>
    /// Warns when a description references a dynamic value the card will not define,
    /// which renders as a missing-value error in game.
    /// </summary>
    private static void ValidateDescriptionVars(
        ModProject project,
        CardDef card,
        List<ProjectIssue> issues,
        VanillaCatalog? catalog)
    {
        // 每种语言单独检查：不同语言的描述可能引用不同的数值，用的占位符也可能不同，
        // 所以不能只看英语那一份。
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string language in card.Description.Languages.OrderBy(l => l, StringComparer.Ordinal))
        {
            string text = card.Description[language];

            // 该语言的数值提示按该语言取状态名，报告读起来才顺。
            var available = new HashSet<string>(
                DescriptionPlaceholders.For(project, card, language).Select(p => p.Placeholder),
                StringComparer.OrdinalIgnoreCase);

            foreach (string token in DescriptionPlaceholders.Extract(text))
            {
                // 有对应数值就是正常的，只有真正缺失才报告。
                if (available.Contains(token))
                {
                    continue;
                }

                // 同一种缺失只报一次，避免一份文本在多种语言下重复刷屏。
                if (!reported.Add($"{language}:{token}"))
                {
                    continue;
                }

                string options = available.Count > 0
                    ? string.Join(", ", available.OrderBy(a => a, StringComparer.OrdinalIgnoreCase))
                    : "none (the card has no fixed numbers yet)";

                issues.Add(new ProjectIssue(
                    "warn",
                    $"Card '{card.Id}' 的 {language} 描述引用了 {{{token}}}，但没有对应的数值，" +
                    $"游戏内会格式化失败。可用：{options}。",
                    card.Id));
            }
        }

        // 英语是游戏其它语言的兜底，缺了它未翻译的语言会显示空白。
        if (!card.Description.Has(GameLanguages.Fallback))
        {
            issues.Add(new ProjectIssue(
                "warn",
                $"Card '{card.Id}' 没有英语描述。英语是游戏的兜底语言，缺失时其它语言也会显示空白。",
                card.Id));
        }
    }

    private static void RegisterType(
        string typeName,
        string where,
        List<ProjectIssue> issues,
        Dictionary<string, string> typeNames)
    {
        if (typeNames.TryGetValue(typeName, out string? existing))
        {
            issues.Add(new ProjectIssue(
                "error",
                $"'{where}' and '{existing}' both generate the C# type {typeName}. Rename one of them.",
                where));
        }
        else
        {
            typeNames[typeName] = where;
        }
    }

    private static void RegisterModelId(
        string modelId,
        string where,
        VanillaCatalog? catalog,
        List<ProjectIssue> issues,
        Dictionary<string, string> modelIds)
    {
        if (modelIds.TryGetValue(modelId, out string? existing))
        {
            issues.Add(new ProjectIssue(
                "error",
                $"'{where}' and '{existing}' both resolve to {modelId}. Rename one of them.",
                where));
        }
        else
        {
            modelIds[modelId] = where;
        }

        if (catalog is not null && catalog.Entries.Any(e => $"{e.Category}.{e.Entry}" == modelId))
        {
            issues.Add(new ProjectIssue(
                "error",
                $"'{where}' resolves to {modelId}, which already exists in the game and would crash it at " +
                "startup. Rename it.",
                where));
        }
    }
}
