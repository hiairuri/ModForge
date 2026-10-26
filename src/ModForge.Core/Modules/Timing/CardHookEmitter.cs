using System.Text;
using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.CardSets;
using ModForge.Core.Modules.Effects;
using ModForge.Core.Modules.Targets;

namespace ModForge.Core.Modules.Timing;

/// <summary>
/// 把 <see cref="CardTimingPlan"/> 发成卡牌类里的钩子方法。
///
/// 发出的**顺序是固定的**（计数器字段 → 打出时 → 打出后 → 抽到 → 弃掉 → 消耗 →
/// ShouldReceiveCombatHooks → 回合结束时在手 → 自动打出 → 回合开始 → 回合结束），
/// 因为生成结果要逐字节稳定；这个顺序也是阅读顺序，与游戏里一轮战斗的推进一致。
///
/// 参数一律拼成一行：这是卡牌侧一直以来的写法。状态侧对长签名是多行的，两边**不一致**——
/// 那是现状，搬迁时原样保留（统一它是另一件事，会改变生成结果）。
/// </summary>
internal static class CardHookEmitter
{
    /// <summary>
    /// <c>OnPlay</c> 里那个"局内的牌地址"局部量的名字。出码处（<c>EffectEmitter</c>）也用它，
    /// 所以只有一个出处。
    /// </summary>
    internal const string SetAddressName = "setAddress";

    public static void Emit(
        StringBuilder sb,
        CardTimingPlan plan,
        EffectEmitter body,
        EmitContext cardCtx,
        CardDef card)
    {
        EmitCounterFields(sb, plan);
        EmitOnPlay(sb, plan, body, cardCtx, card);
        EmitAfterCardPlayed(sb, plan, body, cardCtx);
        EmitOwnCardHook(sb, plan.OnDraw, body, cardCtx, HookKind.AfterCardDrawn);
        EmitOwnCardHook(sb, plan.OnDiscard, body, cardCtx, HookKind.AfterCardDiscarded);
        EmitOwnCardHook(sb, plan.OnExhaust, body, cardCtx, HookKind.AfterCardExhausted);
        EmitCombatHooksFlag(sb, plan);
        EmitEndOfTurnInHand(sb, plan, body, cardCtx);
        EmitSelfAutoPlay(sb, plan, body);
        EmitSideTurnStart(sb, plan, body, cardCtx);
        EmitSideTurnEnd(sb, plan, body, cardCtx);
    }

    /// <summary>
    /// 计数器字段：这是这张牌自己在战斗里的临时状态，不进存档也不需要进——战斗牌堆里的牌
    /// 本来就是临时副本，战斗结束自然消失，"本场战斗"这个范围由它自己兜住。
    /// </summary>
    private static void EmitCounterFields(StringBuilder sb, CardTimingPlan plan)
    {
        for (int i = 0; i < plan.CardPlayed.Count; i++)
        {
            if (plan.PlayedCounters[i] is not string counterField)
            {
                continue;
            }

            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// 打出过多少张符合条件的牌；攒够 {plan.CardPlayed[i].CounterTarget} 张触发一次并归零。");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine($"    private int {counterField};");
        }
    }

    private static void EmitOnPlay(
        StringBuilder sb,
        CardTimingPlan plan,
        EffectEmitter body,
        EmitContext cardCtx,
        CardDef card)
    {
        EmitMethodOpen(sb, HookKind.OnPlay, HookOwner.ForCard, leadingBlank: true);

        // 临时牌集合：一次出牌结算一个"局内的牌地址"，结算结束时把它下面的集合清掉。
        //
        // 为什么在 OnPlay 里（而不是某个更晚的钩子）：OnPlay **就是**一次出牌结算
        // （重放的每一遍各调一次，各有各的 CardPlay），而且它是唯一能收 CardPlay 的钩子。
        // 放在 try/finally 里是为了异常也清干净——不然那张牌的集合会一直留着，
        // 而键里的地址不会再被用到，等于泄漏。
        //
        // 只有真的用集合的牌才发这两行：不然每张牌的生成结果都会变（多一层缩进），
        // "既有工程逐字节不变"也就不成立了。
        bool usesSets = plan.OnPlay.Any(e =>
            CardSetCatalog.Stores(e.Kind) && !string.IsNullOrWhiteSpace(e.SetId)
            || (e.Pick.Source == PickSource.Set && !string.IsNullOrWhiteSpace(e.Pick.SetId)));

        string indent = "        ";

        if (usesSets)
        {
            sb.AppendLine($"        string {SetAddressName} = {CardSetEmitter.Open(body.CardEntry ?? card.Id)};");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            indent = "            ";
        }

        // 单目标牌（要选一个目标的那些）+ 有一条以"打出时选定的目标"为对象的效果：目标可能为空，
        // 先拦一下，免得后面各条效果各自去空引用。
        //
        // 判据问目标模块：哪几档卡牌目标会给出一个 `cardPlay.Target`
        // （任意敌人 / 任意友方——就是游戏强制要求传目标的那两档），以及哪几条目标档要用它
        // （「目标」与「目标的奥斯提」——后者还要再取那名玩家的 `Player`）。
        if (TargetCatalog.ProvidesPlayedTarget(card.Target) &&
            plan.OnPlay.Any(e => TargetCatalog.UsesPlayedTarget(e.Target)))
        {
            sb.AppendLine($"{indent}ArgumentNullException.ThrowIfNull(cardPlay.Target, nameof(cardPlay.Target));");
        }

        foreach (EffectDef effect in plan.OnPlay)
        {
            body.Emit(sb, effect, cardCtx, indent);
        }

        if (plan.OnPlay.Count == 0)
        {
            sb.AppendLine($"{indent}await Task.CompletedTask;");
        }

        if (usesSets)
        {
            sb.AppendLine("        }");
            sb.AppendLine("        finally");
            sb.AppendLine("        {");
            sb.AppendLine($"            {CardSetEmitter.Clear(SetAddressName)}");
            sb.AppendLine("        }");
        }

        EmitMethodClose(sb);
    }

    /// <summary>
    /// "打出后"与"打出一张牌时"由游戏的**同一个**钩子派发（<c>AfterCardPlayed</c>），
    /// 所以必须合成一个覆写：各发一个的话后一个会把前一个覆盖掉（C# 里同名 override
    /// 重复定义是编译错误，于是这里只能有一个方法，两种语义在里面各判各的）。
    ///
    /// 语义差别只在于"看哪张牌"：
    ///   打出后      —— 只看这张牌自己（<c>cardPlay.Card != this</c> 就返回）
    ///   打出一张牌时 —— 看**任意**被打出的牌，用 CardFilter 决定哪些算数
    /// </summary>
    private static void EmitAfterCardPlayed(
        StringBuilder sb,
        CardTimingPlan plan,
        EffectEmitter body,
        EmitContext cardCtx)
    {
        if (plan.AfterPlayed.Count == 0 && plan.CardPlayed.Count == 0)
        {
            return;
        }

        EmitMethodOpen(sb, HookKind.AfterCardPlayed, HookOwner.ForCard, leadingBlank: true);

        if (plan.AfterPlayed.Count > 0)
        {
            // 只对"这张牌自己被打出"生效，否则别人的牌也会触发。
            //
            // 注意这个早退**不能**在两种效果同时存在时也把下面那半段挡掉，所以当
            // cardPlayed 非空时不早退，改成把自身那部分包进 if 里（见下）。
            if (plan.CardPlayed.Count == 0)
            {
                sb.AppendLine("        if (cardPlay.Card != this)");
                sb.AppendLine("        {");
                sb.AppendLine("            return;");
                sb.AppendLine("        }");
                sb.AppendLine();

                foreach (EffectDef effect in plan.AfterPlayed)
                {
                    body.Emit(sb, effect, cardCtx);
                }
            }
            else
            {
                sb.AppendLine("        if (cardPlay.Card == this)");
                sb.AppendLine("        {");

                foreach (EffectDef effect in plan.AfterPlayed)
                {
                    body.Emit(sb, effect, cardCtx, "            ");
                }

                sb.AppendLine("        }");
            }
        }

        for (int i = 0; i < plan.CardPlayed.Count; i++)
        {
            EmitOtherCardPlayed(sb, body, plan.CardPlayed[i], cardCtx, plan.PlayedCounters[i]);
        }

        EmitMethodClose(sb);
    }

    /// <summary>
    /// "打出一张牌时"的一条效果：按筛选条件决定算不算数，可选计数器。
    /// </summary>
    private static void EmitOtherCardPlayed(
        StringBuilder sb,
        EffectEmitter body,
        EffectDef effect,
        EmitContext ctx,
        string? counterField)
    {
        sb.AppendLine();
        sb.AppendLine($"        // 打出一张牌时：{(effect.CardFilter == CardFilterKind.Any ? "任意牌" : effect.CardFilter.ToString())}。");

        if (effect.CardFilter == CardFilterKind.SpecificCard && string.IsNullOrWhiteSpace(effect.CardId))
        {
            // 选了"指定卡"却没填是哪张。校验器会报错；这里不生成任何代码，因为怎么写都是错的：
            // 默认成"任意牌"等于静默改成另一个效果，而写 `if (false)` 会引出一段
            // 永远不会执行的代码（CS0162 警告，且读起来像是有意的）。
            sb.AppendLine("        // 选了指定的牌却没指定是哪张，这条效果不会触发（见校验提示）。");
            return;
        }

        if (effect.CardFilter == CardFilterKind.SpecificCard)
        {
            string typeRef = body.CardModelTypeRef(effect.CardId!);
            sb.AppendLine($"        if (cardPlay.Card is {typeRef} && cardPlay.Card.Owner == Owner)");
        }
        else
        {
            string filter = CardFilters.PredicateOn("cardPlay.Card", effect.CardFilter);

            sb.AppendLine(effect.CardFilter == CardFilterKind.Any
                ? "        if (cardPlay.Card.Owner == Owner)"
                : $"        if (cardPlay.Card.Owner == Owner && {filter})");
        }

        sb.AppendLine("        {");

        if (counterField is not null)
        {
            sb.AppendLine($"            {counterField}++;");
            sb.AppendLine();
            sb.AppendLine($"            if ({counterField} >= {effect.CounterTarget})");
            sb.AppendLine("            {");
            sb.AppendLine($"                {counterField} = 0;");
            sb.AppendLine();
            body.Emit(sb, effect, ctx, "                ");
            sb.AppendLine("            }");
        }
        else
        {
            body.Emit(sb, effect, ctx, "            ");
        }

        sb.AppendLine("        }");
    }

    /// <summary>抽到 / 弃掉 / 消耗三个钩子形状一样：都只看"这张牌自己"。</summary>
    private static void EmitOwnCardHook(
        StringBuilder sb,
        IReadOnlyList<EffectDef> effects,
        EffectEmitter body,
        EmitContext cardCtx,
        HookKind kind)
    {
        if (effects.Count == 0)
        {
            return;
        }

        HookSpec spec = HookSpec.For(kind);

        EmitMethodOpen(sb, kind, HookOwner.ForCard, leadingBlank: true);
        EmitGuard(sb, spec, HookOwner.ForCard, "        ");
        sb.AppendLine();

        foreach (EffectDef effect in effects)
        {
            body.Emit(sb, effect, cardCtx with { CardTarget = "null" });
        }

        EmitMethodClose(sb);
    }

    /// <summary>
    /// 让牌在牌堆里也能收到战斗事件。算这个标记的规则见 <see cref="CardTimingPlan.NeedsCombatHooks"/>。
    /// </summary>
    private static void EmitCombatHooksFlag(StringBuilder sb, CardTimingPlan plan)
    {
        if (!plan.NeedsCombatHooks)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("    // 这张牌在牌堆里时也要响应战斗事件。");
        sb.AppendLine("    public override bool ShouldReceiveCombatHooks => true;");
    }

    /// <summary>
    /// "回合结束时这张牌还在手牌里时触发"：用游戏为此提供的
    /// <c>HasTurnEndInHandEffect</c> + <c>OnTurnEndInHand</c>。它在手牌被弃掉**之前**执行，
    /// 正好是那个语义；原版的"凋萎/燃烧/疑虑"这类诅咒牌用的就是它。
    ///
    /// 不用加牌堆判断：引擎只会对手牌里的牌调用它，而调用期间牌被临时移进 Play 牌堆，
    /// 写了 <c>Pile?.Type == Hand</c> 反而永远不会成立（这正是先前那个 bug）。
    /// </summary>
    private static void EmitEndOfTurnInHand(
        StringBuilder sb,
        CardTimingPlan plan,
        EffectEmitter body,
        EmitContext cardCtx)
    {
        if (plan.EndOfTurnInHand.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("    // 回合结束时这张牌还在手牌里时触发（由引擎逐张手牌调用）。");
        sb.AppendLine("    public override bool HasTurnEndInHandEffect => true;");
        sb.AppendLine();
        sb.AppendLine($"    {HookSpec.For(HookKind.EndOfTurnInHand).Access} override async Task OnTurnEndInHand(PlayerChoiceContext choiceContext)");
        sb.AppendLine("    {");

        foreach (EffectDef effect in plan.EndOfTurnInHand)
        {
            body.Emit(sb, effect, cardCtx with { CardTarget = "null" });
        }

        EmitMethodClose(sb);
    }

    /// <summary>
    /// "回合开始时"：钩子不带 PlayerChoiceContext，而抽牌、施加状态都需要它，
    /// 所以照状态那边的做法自己造一个——引擎在派发钩子时也是这么包的。
    ///
    /// 同时这里也挂"本回合计数器的归零"：两种情况共用一个钩子。
    /// 归零排在效果**之后**——"本回合打出了几张"在回合开始时已经过期（那是上一回合的数），
    /// 先归零才对；放前面还是后面都不影响本回合的效果，但归零本身要发生。
    /// </summary>
    private static void EmitSideTurnStart(
        StringBuilder sb,
        CardTimingPlan plan,
        EffectEmitter body,
        EmitContext cardCtx)
    {
        if (plan.StartOfTurn.Count == 0 && plan.TurnResetCounters.Count == 0)
        {
            return;
        }

        if (plan.NeedsHookContext)
        {
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// 回合开始的钩子不带 PlayerChoiceContext，而这类效果往往需要一个，");
            sb.AppendLine("    /// 所以照引擎派发钩子时的做法自己包一个。");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    private PlayerChoiceContext HookContext() =>");
            sb.AppendLine("        new HookPlayerChoiceContext(this, LocalContext.NetId ?? 0UL, Owner.Creature.CombatState!, GameActionType.Combat);");
        }

        EmitMethodOpen(sb, HookKind.TurnStart, HookOwner.ForCard, leadingBlank: true);
        EmitGuard(sb, HookSpec.For(HookKind.TurnStart), HookOwner.ForCard, "        ");
        sb.AppendLine();

        // 牌堆范围由每条效果自己判断（见 EmitScopedBody 的说明）。
        // 这里**不能**再加一句"整体牌堆检查"：同一时机下各条效果的范围可以不同，
        // 用第一条的范围做整体早退，会把范围不同的其它效果整段变成死代码。
        foreach (EffectDef effect in plan.StartOfTurn)
        {
            EmitScopedBody(sb, body, effect, cardCtx with { ChoiceContext = "HookContext()" });
        }

        EmitCounterResets(sb, plan.TurnResetCounters);
        EmitMethodClose(sb);
    }

    /// <summary>回合结束时（不含"还在手牌里"那一档）。与回合开始一样要挂计数器归零。</summary>
    private static void EmitSideTurnEnd(
        StringBuilder sb,
        CardTimingPlan plan,
        EffectEmitter body,
        EmitContext cardCtx)
    {
        if (plan.EndOfTurn.Count == 0 && plan.TurnResetCounters.Count == 0)
        {
            return;
        }

        EmitMethodOpen(sb, HookKind.TurnEnd, HookOwner.ForCard, leadingBlank: true);
        EmitGuard(sb, HookSpec.For(HookKind.TurnEnd), HookOwner.ForCard, "        ");
        sb.AppendLine();

        foreach (EffectDef effect in plan.EndOfTurn)
        {
            EmitScopedBody(sb, body, effect, cardCtx);
        }

        EmitCounterResets(sb, plan.TurnResetCounters);
        EmitMethodClose(sb);
    }

    /// <summary>
    /// 让这张牌自己在满足条件时被自动打出（原版"轰击"那种）。
    ///
    /// 引擎为这类效果给了专门的相位钩子：
    ///   回合开始时 -&gt; AfterAutoPrePlayPhaseEnteredEarly
    ///   回合结束时 -&gt; AfterAutoPostPlayPhaseEntered
    /// 用这两个而不是普通的回合钩子，是因为"自动打出的牌"归引擎的这两个相位管：
    /// 自动打出的牌在回合开始时是在**抽牌之后**才出来的，而这正是 AutoPrePlay 相位。
    /// </summary>
    private static void EmitSelfAutoPlay(
        StringBuilder sb,
        CardTimingPlan plan,
        EffectEmitter body)
    {
        if (plan.SelfPlayOnStart.Count > 0)
        {
            EmitSelfAutoPlayHook(sb, HookKind.AutoPlayPhaseStart, plan.SelfPlayOnStart, body);
        }

        if (plan.SelfPlayOnEnd.Count > 0)
        {
            EmitSelfAutoPlayHook(sb, HookKind.AutoPlayPhaseEnd, plan.SelfPlayOnEnd, body);
        }
    }

    /// <summary>
    /// 一次自动打出的相位钩子。这个钩子带 PlayerChoiceContext（与普通的回合钩子不同），
    /// 可以直接用。
    /// </summary>
    private static void EmitSelfAutoPlayHook(
        StringBuilder sb,
        HookKind kind,
        IReadOnlyList<EffectDef> effects,
        EffectEmitter body)
    {
        EmitMethodOpen(sb, kind, HookOwner.ForCard, leadingBlank: true);

        foreach (EffectDef effect in effects)
        {
            EmitSelfAutoPlayBody(sb, effect, "        ", "Owner", body);
        }

        EmitMethodClose(sb);
    }

    /// <summary>
    /// 自动打出一条的语句。
    ///
    /// 条件是 <c>Pile?.Type</c>（等价于原版写的 <c>base.Pile is { Type: 某牌堆 }</c>），
    /// 另外还要求这一轮的行动者是自己的主人——多人战斗里别的玩家的回合不该触发。
    ///
    /// **牌堆条件只判一次，重复打在循环里**：这就是「重复打出次数」的语义——
    /// "若在消耗堆中，将此牌重复打出 2 次"是打出两次，不是"每轮都回到消耗堆里再判一次"
    /// （打完第一张牌就离开那个牌堆了，每轮重判会导致第二次不触发）。
    /// </summary>
    private static void EmitSelfAutoPlayBody(
        StringBuilder sb,
        EffectDef effect,
        string indent,
        string playerExpr,
        EffectEmitter body)
    {
        string pile = $"PileType.{PileScopeToPileType(effect.PileScope)}";

        sb.AppendLine($"{indent}// 这张牌在{ pile }里时才自己出来（原版轰击的做法）。");
        sb.AppendLine($"{indent}if (player == {playerExpr} && Pile?.Type == {pile})");
        sb.AppendLine($"{indent}{{");

        string inner = indent + "    ";

        // 次数取法与别处共用（固定 / 可升级 / 计算型三种都在里面）。
        var ctx = EmitContext.ForCard() with { CardTarget = null };
        (IReadOnlyList<string> setup, string count) = body.RepeatCount(effect, ctx, inner);

        foreach (string line in setup)
        {
            sb.AppendLine(line);
        }

        if (count == "1")
        {
            sb.AppendLine($"{inner}await CardCmd.AutoPlay(choiceContext, this, null);");
        }
        else
        {
            sb.AppendLine($"{inner}for (int i = 0; i < {count}; i++)");
            sb.AppendLine($"{inner}{{");
            sb.AppendLine($"{inner}    await CardCmd.AutoPlay(choiceContext, this, null);");
            sb.AppendLine($"{inner}}}");
        }

        sb.AppendLine($"{indent}}}");
    }

    /// <summary>
    /// 一条效果的方法体；限定了牌堆范围的会包一层判断。
    ///
    /// 范围判断**逐条**做，不做整体的：同一时机下各条效果的范围可以不同（"在抽牌堆里抽 1 张；
    /// 在手牌里获得格挡"），按其中一条整段早退会把范围不同的其它效果变成死代码——编译正常，
    /// 只是永远不生效。newfx2 里有一张卡专门盯着这件事。
    /// </summary>
    private static void EmitScopedBody(
        StringBuilder sb,
        EffectEmitter body,
        EffectDef effect,
        EmitContext ctx)
    {
        bool scoped = effect.PileScope != PileScope.Any;

        if (scoped)
        {
            sb.AppendLine($"        if (Pile?.Type == PileType.{PileScopeToPileType(effect.PileScope)})");
            sb.AppendLine("        {");
        }

        body.Emit(sb, effect, ctx with { CardTarget = "null" });

        if (scoped)
        {
            sb.AppendLine("        }");
        }
    }

    /// <summary>
    /// "本回合"的计数器在回合结束与回合开始各归零一次。
    ///
    /// 两处都要：只靠回合开始归零的话，本回合末到下次回合开始之间读到的还是上一回合的累计值；
    /// 只靠回合结束归零的话，回合外的时机（例如打出牌时的其它判定）看到的是过期数字。
    /// 原版"精密瞄准"用 <c>HappenedThisTurn</c> 直接查战斗历史，天然没有这个问题；我们用的是
    /// 自己的计数器，就得自己把边界清干净。
    /// </summary>
    private static void EmitCounterResets(StringBuilder sb, IReadOnlyList<string> counters)
    {
        if (counters.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("        // 本回合的计数到这里就过期了。");

        foreach (string counter in counters)
        {
            sb.AppendLine($"        {counter} = 0;");
        }
    }

    /// <summary>开一个钩子方法的方法头（含签名）。</summary>
    private static void EmitMethodOpen(
        StringBuilder sb,
        HookKind kind,
        HookOwner owner,
        bool leadingBlank)
    {
        HookSpec spec = HookSpec.For(kind);

        if (leadingBlank)
        {
            sb.AppendLine();
        }

        sb.AppendLine($"    {spec.Access} override async Task {spec.Method}({string.Join(" ", spec.Parameters)})");
        sb.AppendLine("    {");
    }

    private static void EmitMethodClose(StringBuilder sb) => sb.AppendLine("    }");

    /// <summary>
    /// 早退判断。写成"条件不成立就 return"而不是把主体包进 if 里——这样每种钩子只有一种形状，
    /// 而且与游戏派发钩子的语义一致（钩子是广播，不感兴趣的直接返回）。
    /// </summary>
    private static void EmitGuard(StringBuilder sb, HookSpec spec, HookOwner owner, string indent)
    {
        string? condition = spec.Guard switch
        {
            HookGuard.None => null,
            HookGuard.OwnCard => "card != this",
            HookGuard.OwnPlayedCard => "cardPlay.Card != this",
            HookGuard.OwnSide => $"side != {owner.Side}",
            _ => null,
        };

        if (condition is null)
        {
            return;
        }

        sb.AppendLine($"{indent}if ({condition})");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    return;");
        sb.AppendLine($"{indent}}}");
    }

    /// <summary>牌堆范围的 C# 枚举名（与选牌模块共用同一份映射）。</summary>
    private static string PileScopeToPileType(PileScope scope) => scope switch
    {
        PileScope.Hand => "Hand",
        PileScope.DrawPile => "Draw",
        PileScope.DiscardPile => "Discard",
        PileScope.ExhaustPile => "Exhaust",
        _ => "Hand",
    };
}
