using System.Text;
using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.Effects;

namespace ModForge.Core.Modules.Timing;

/// <summary>
/// 把 <see cref="PowerTimingPlan"/> 发成状态类里的钩子方法。
///
/// 与卡牌侧的不同：状态的钩子是**多行参数**（除了 <c>AfterCardPlayed</c> 那种短签名），
/// 而且回合开始/结束的"是不是我的回合"读的是 <c>Owner.Side</c>（状态的 Owner 就是生物）。
/// 两处差异都是现状，搬迁时原样保留——统一它们是另一件事，会改变生成结果。
/// </summary>
internal static class PowerHookEmitter
{
    public static void Emit(
        StringBuilder sb,
        PowerDef power,
        PowerTimingPlan plan,
        EffectEmitter emitter,
        EmitContext powerCtx,
        EmitContext powerHookCtx)
    {
        EmitHookContext(sb, plan);
        EmitTurnStart(sb, plan, emitter, powerHookCtx);
        EmitCardPlayed(sb, plan, emitter, powerCtx);
        EmitDamageTaken(sb, plan, emitter, powerCtx);
        EmitTurnEnd(sb, power, plan, emitter, powerCtx);
    }

    /// <summary>
    /// 不带 PlayerChoiceContext 的钩子（回合开始）里，凡是要"让玩家选择"的效果都得有一个，
    /// 所以发一个与引擎派发钩子时同款的构造。
    /// </summary>
    private static void EmitHookContext(StringBuilder sb, PowerTimingPlan plan)
    {
        if (!plan.NeedsHookContext)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Context for effects fired from a hook that does not receive one.");
        sb.AppendLine("    /// Mirrors how the engine builds a context around hook dispatch.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    private PlayerChoiceContext HookContext() =>");
        sb.AppendLine("        new HookPlayerChoiceContext(this, LocalContext.NetId ?? 0UL, Owner.CombatState!, GameActionType.Combat);");
    }

    private static void EmitTurnStart(
        StringBuilder sb,
        PowerTimingPlan plan,
        EffectEmitter emitter,
        EmitContext powerHookCtx)
    {
        if (plan.TurnStart.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("    /// <summary>Fires at the start of the owner's turn.</summary>");
        EmitSignatureMultiLine(sb, HookKind.TurnStart);
        EmitReturnGuard(sb, HookSpec.For(HookKind.TurnStart), HookOwner.ForPower);
        sb.AppendLine();

        foreach (TriggeredEffect trigger in plan.TurnStart)
        {
            emitter.Emit(sb, trigger.Effect, powerHookCtx);
        }

        EmitMethodClose(sb);
    }

    private static void EmitCardPlayed(
        StringBuilder sb,
        PowerTimingPlan plan,
        EffectEmitter emitter,
        EmitContext powerCtx)
    {
        if (plan.CardPlayed.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("    /// <summary>Fires whenever the owner plays a card.</summary>");
        EmitSignatureInline(sb, HookKind.PowerCardPlayed);
        EmitReturnGuard(sb, HookSpec.For(HookKind.PowerCardPlayed), HookOwner.ForPower);
        sb.AppendLine();

        foreach (TriggeredEffect trigger in plan.CardPlayed)
        {
            emitter.Emit(sb, trigger.Effect, powerCtx);
        }

        EmitMethodClose(sb);
    }

    private static void EmitDamageTaken(
        StringBuilder sb,
        PowerTimingPlan plan,
        EffectEmitter emitter,
        EmitContext powerCtx)
    {
        if (plan.DamageTaken.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("    /// <summary>Fires when the owner takes damage.</summary>");
        EmitSignatureMultiLine(sb, HookKind.PowerDamageReceived);
        EmitReturnGuard(sb, HookSpec.For(HookKind.PowerDamageReceived), HookOwner.ForPower);
        sb.AppendLine();

        foreach (TriggeredEffect trigger in plan.DamageTaken)
        {
            emitter.Emit(sb, trigger.Effect, powerCtx);
        }

        EmitMethodClose(sb);
    }

    /// <summary>回合结束：触发与衰减由同一个钩子驱动，所以"没有触发但有衰减"时也要发。</summary>
    private static void EmitTurnEnd(
        StringBuilder sb,
        PowerDef power,
        PowerTimingPlan plan,
        EffectEmitter emitter,
        EmitContext powerCtx)
    {
        if (!plan.NeedsTurnEndHook)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine("    /// <summary>Fires at the end of the owner's turn, and applies stack decay.</summary>");
        EmitSignatureMultiLine(sb, HookKind.TurnEnd);
        EmitReturnGuard(sb, HookSpec.For(HookKind.TurnEnd), HookOwner.ForPower);
        sb.AppendLine();

        foreach (TriggeredEffect trigger in plan.TurnEnd)
        {
            emitter.Emit(sb, trigger.Effect, powerCtx);
        }

        if (power.Decay == DecayMode.OnePerTurnEnd)
        {
            sb.AppendLine("        if (Amount > 0)");
            sb.AppendLine("        {");
            sb.AppendLine("            await PowerCmd.Decrement(this);");
            sb.AppendLine("        }");
        }
        else if (power.Decay == DecayMode.AllAtTurnEnd)
        {
            sb.AppendLine("        await PowerCmd.Remove(this);");
        }

        EmitMethodClose(sb);
    }

    /// <summary>
    /// 多行参数的签名（长签名一直这么发）。
    ///
    /// 注意**右括号跟在最后一个参数行尾**，不是单独一行——这是原写法，逐字节保留。
    /// </summary>
    private static void EmitSignatureMultiLine(StringBuilder sb, HookKind kind)
    {
        HookSpec spec = HookSpec.For(kind);

        sb.AppendLine($"    {spec.Access} override async Task {spec.Method}(");

        for (int i = 0; i < spec.Parameters.Length; i++)
        {
            bool last = i == spec.Parameters.Length - 1;

            sb.AppendLine($"        {spec.Parameters[i]}{(last ? ")" : string.Empty)}");
        }

        sb.AppendLine("    {");
    }

    /// <summary>单行参数的签名（短签名一直这么发）。</summary>
    private static void EmitSignatureInline(StringBuilder sb, HookKind kind)
    {
        HookSpec spec = HookSpec.For(kind);

        sb.AppendLine($"    {spec.Access} override async Task {spec.Method}({string.Join(" ", spec.Parameters)})");
        sb.AppendLine("    {");
    }

    private static void EmitMethodClose(StringBuilder sb) => sb.AppendLine("    }");

    private static void EmitReturnGuard(StringBuilder sb, HookSpec spec, HookOwner owner)
    {
        string? condition = spec.Guard switch
        {
            HookGuard.OwnSide => $"side != {owner.Side}",
            HookGuard.PowerOwnerPlayedCard => "cardPlay.Card.Owner?.Creature != Owner",
            HookGuard.PowerOwnerTarget => "target != Owner",
            _ => null,
        };

        if (condition is null)
        {
            return;
        }

        sb.AppendLine($"        if ({condition})");
        sb.AppendLine("        {");
        sb.AppendLine("            return;");
        sb.AppendLine("        }");
    }
}
