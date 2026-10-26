using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Descriptions;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 加入指定卡牌：把**新生成的牌**放进某个牌堆。两种生成方式（<see cref="AddCardMode"/>）：
///
///   Named —— 加指定的那张（原版"愤怒"：把这张牌的复制品加入弃牌堆）。先
///            <c>RunState.CreateCard&lt;T&gt;</c> 造一张，可升级，再交给
///            <c>CardPileCmd.AddGeneratedCardToCombat</c>——走这个接口而不是直接塞进牌堆，
///            是因为生成出来的牌要登记进战斗记录（"本场战斗生成的牌"这类计数与钩子都靠它）。
///
///   ChooseFromPool —— 从**出牌者自己的角色卡池**里按多维筛选筛出候选（原版"发现"的取法：
///            <c>owner.Character.CardPool.GetUnlockedCards</c>），用原版工厂
///            <c>CardFactory.GetForCombat / GetDistinctForCombat</c> 随机取：
///            候选数 1 → 直接加入（可加多张，可重复，"随机生成一张攻击牌"的写法）；
///            候选数 ≥ 2 → 每张弹一次游戏自己的选牌界面
///            （<c>CardSelectCmd.FromChooseACardScreen</c>，原版"发现"的三选一）让玩家挑。
///            游戏对"战斗中生成"自有一道门槛（工厂内部排除基础 / 远古 / 事件稀有度与
///            声明了不可生成的牌），走工厂方法自动生效。
///
/// 筛选与选牌器那份多维筛选**同一个谓词生成处**（<c>CardPickFilter</c>），差别只在范围：
/// 这里没有牌堆 / 卡组那几个运行时范围，候选来自卡池；费用阈值用筛选自带的常数
/// （卡池里都是卡面印着的费用，用不到变量槽）。
///
/// **这一条是写侧的集合来源，不是读侧**：生成的牌可以存进临时牌集合
/// （"生成三选一的那张，然后让它免费"），但它自己没有选牌器、谈不上"作用于某个集合"。
/// </summary>
internal sealed class AddNamedCardToPileBehavior : IEffectBehavior
{
    public EffectKind Kind => EffectKind.AddNamedCardToPile;

    public void Emit(EffectEmit a)
    {
        if (a.Effect.AddMode == AddCardMode.ChooseFromPool)
        {
            EmitFromPool(a);
            return;
        }

        EmitNamed(a);
    }

    /// <summary>指定牌那一路。与搬迁前逐字节同形（含集合存储与空牌名防御）。</summary>
    private void EmitNamed(EffectEmit a)
    {
        if (string.IsNullOrWhiteSpace(a.Effect.CardId))
        {
            a.Line("// 未指定要加入的卡牌");
            return;
        }

        string modelType = a.Emitter.CardModelTypeRef(a.Effect.CardId);

        // 要存进临时牌集合时就把生成的牌攒起来（"加入 3 张打击，然后让这 3 张免费"）。
        string? collected = a.Emitter.SetStoreLine(a.Effect, a.Ctx, Placeholder) is null
            ? null
            : "created" + a.Emitter.NextLocalIndex();

        if (collected is not null)
        {
            a.Line($"var {collected} = new List<CardModel>();");
        }

        a.Line($"for (int n = 0; n < {a.Amount}; n++)");
        a.Line("{");
        a.Inner($"var created = {a.Ctx.Player}.RunState.CreateCard<{modelType}>({a.Ctx.Player});");

        if (a.Effect.Upgraded)
        {
            a.Inner("created.UpgradeInternal();");
        }

        // 加到哪个牌堆：与"复制牌并加入牌堆"同一个字段（目的地，不是选牌来源）。
        a.Inner($"await CardPileCmd.AddGeneratedCardToCombat(created, PileType.{PileName(a.Effect.TargetPile)}, {a.Ctx.Player}, CardPilePosition.{a.Effect.Placement});");

        if (collected is not null)
        {
            a.Inner($"{collected}.Add(created);");
        }

        a.Line("}");

        if (collected is not null && a.Emitter.SetStoreLine(a.Effect, a.Ctx, collected) is string store)
        {
            a.Line(store);
        }
    }

    /// <summary>从卡池筛 N 选一那一路。</summary>
    private void EmitFromPool(EffectEmit a)
    {
        string player = a.Ctx.Player;

        // 筛选谓词与选牌器同一处生成（判的全是卡面属性，对卡池的牌一样成立）。
        // 阈值是筛选自己的常数（卡池里没有"手牌数"这类运行时来源可算）。
        CardFilter filter = a.Effect.PoolFilter;
        string? predicate = CardPickFilter.PredicateOn(
            "c", filter,
            role => filter.ThresholdOf(role)
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            a.Emitter.CardModelTypeRef);

        string pool =
            $"{player}.Character.CardPool.GetUnlockedCards({player}.UnlockState, {player}.RunState.CardMultiplayerConstraint)";
        string source = predicate is null ? pool : $"{pool}.Where(c => {predicate})";

        // 候选数 ≥ 2 才弹界面；游戏的选牌界面候选超过 3 张直接抛异常，
        // 所以 Candidates 的合法范围在界面与校验器两头都拦（这里生成时 Candidates 已合法）。
        bool choose = a.Effect.Candidates >= 2;

        string sfx = a.Emitter.NextPickSuffix("Pool");
        string pile = $"PileType.{PileName(a.Effect.TargetPile)}";

        // 生成的牌（不管几轮、选中与否）统一攒进一个表，最后存集合。
        string? collected = a.Emitter.SetStoreLine(a.Effect, a.Ctx, Placeholder) is null
            ? null
            : "created" + a.Emitter.NextLocalIndex();

        if (collected is not null)
        {
            a.Line($"var {collected} = new List<CardModel>();");
        }

        if (choose)
        {
            a.Line($"var poolCandidates{sfx} = CardFactory.GetDistinctForCombat({player}, {source}.ToList(), {a.Effect.Candidates}, {player}.RunState.Rng.CombatCardGeneration).ToList();");

            // 筛过的池可能是空的：空候选进游戏那条界面会报"软锁"，这里直接跳过。
            a.Line($"if (poolCandidates{sfx}.Count > 0)");
            a.Line("{");
            a.Inner($"var poolChosen{sfx} = await CardSelectCmd.FromChooseACardScreen({a.Ctx.ChoiceContext}, poolCandidates{sfx}, {player});");
            a.Inner($"if (poolChosen{sfx} is not null)");
            a.Inner("{");
            a.Inner($"    await CardPileCmd.AddGeneratedCardToCombat(poolChosen{sfx}, {pile}, {player}, CardPilePosition.{a.Effect.Placement});");

            if (collected is not null)
            {
                a.Inner($"    {collected}.Add(poolChosen{sfx});");
            }

            a.Inner("}");
            a.Line("}");

            if (collected is not null && a.Emitter.SetStoreLine(a.Effect, a.Ctx, collected) is string chooseStore)
            {
                a.Line(chooseStore);
            }

            return;
        }

        // 候选数 1：不弹界面，"随机生成"——可加多张、可重复（原版"变身"同款工厂）。
        a.Line($"var poolDrawn{sfx} = CardFactory.GetForCombat({player}, {source}, {a.Amount}, {player}.RunState.Rng.CombatCardGeneration).ToList();");
        a.Line($"foreach (var added{sfx} in poolDrawn{sfx})");
        a.Line("{");
        a.Inner($"await CardPileCmd.AddGeneratedCardToCombat(added{sfx}, {pile}, {player}, CardPilePosition.{a.Effect.Placement});");

        if (collected is not null)
        {
            a.Inner($"{collected}.Add(added{sfx});");
        }

        a.Line("}");

        if (collected is not null && a.Emitter.SetStoreLine(a.Effect, a.Ctx, collected) is string store)
        {
            a.Line(store);
        }
    }

    private const string Placeholder = "__set_cards__";

    private static string PileName(PileKind pile) => CardPickCollector.PileTypeName(pile);

    public string Describe(EffectDescribe a)
    {
        // {where} 是**目的地**（"加入哪个牌堆"）：手牌 / 抽牌堆顶…。
        // 不是选牌来源——这条效果不挑牌，a.AddPileVars() 给的 where 念的是"从哪儿挑"，
        // 会把"加到手牌"说成"加入所有牌堆"（旧句子的既有毛病，这次重写一并修掉）。
        a.With("where", PickerWording.TargetPileLabel(a.Effect, a.Sentences));

        if (a.Effect.AddMode == AddCardMode.ChooseFromPool)
        {
            // 没筛任何维度时 {what} 用"任意牌"；筛了就用筛选的定语链
            // （阈值念筛选自己的常数，与"按条件数牌"同一套）。兜底词**也是一条句子**：
            // 要从 sentences 段取（Word() 只查 words 段，取不到会静默变空——踩过）。
            string what = PickerWording.FilterPhrase(
                a.Project,
                a.Effect.PoolFilter,
                a.Sentences,
                role => a.Effect.PoolFilter.ThresholdOf(role)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture))
                ?? a.Render("addFromPoolAnyWord");

            a.With("what", what).With("candidates", a.Effect.Candidates.ToString());

            if (a.Effect.Candidates >= 2)
            {
                return a.Render("addFromPoolChoose");
            }

            if (a.Effect.IsCalculated)
            {
                a.AddCalculationVars();
                return a.Render("addFromPoolRandomCalc");
            }

            return a.Render("addFromPoolRandom");
        }

        a.With("card", DescriptionNames.CardName(a.Project, a.Effect.CardId, a.Sentences.Language));
        a.With("upgraded", a.Effect.Upgraded ? a.Word("upgraded") : string.Empty);

        return a.Render(a.Effect.IsCalculated ? "addNamedCardCalc" : "addNamedCard");
    }

    // needsCard（指定牌那一格）在"从卡池 N 选一"模式不显示——由界面按 addMode 分支。
    // hasAddMode 告诉界面画"生成方式"那个下拉（这条效果独有的形状，别的效果没有）。
    public EffectSchemaEntry Schema => new(
        kind: "", labelZh: "加入指定卡牌", labelEn: "Add a named card",
        scope: TargetScope.Cards, amount: true, needsCard: true, needsTargetPile: true,
        hasAddMode: true);
}
