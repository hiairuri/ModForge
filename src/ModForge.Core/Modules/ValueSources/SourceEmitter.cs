using ModForge.Core.Model;
using ModForge.Core.Modules.Counting;

namespace ModForge.Core.Modules.ValueSources;

/// <summary>
/// 变量来源模块的出码处：把 <see cref="ValueSource"/> 变成 C# 的取值语句。
///
/// **所有"要算出计算型数值的变量"的地方都走这里**——数值、次数、选牌张数、两个费用阈值，
/// 五个角色共用同一套读法（调用方只管把结果拼进自己的算式）。以前这段代码在
/// <c>Generate/ValueSourceEmitter</c> 里，与生成器混在一起；现在它与它依赖的两张名单
/// （本模块的行、计数模块的二十种）住在一个文件夹里，读法不会再各自长出来一份。
///
/// 收的是三个表达式的组合（<see cref="Context"/>）而不是"卡牌"，因为卡牌与状态能拿到的
/// 上下文不同：卡牌的 Owner 是 Player，状态的 Owner 是 Creature。把这两者显式传进来，
/// 两种场合就能共用同一套取值逻辑。
/// </summary>
public static class SourceEmitter
{
    /// <summary>取值所需的上下文表达式。</summary>
    /// <param name="Player">持有者的 Player 表达式（牌堆、资源从这里读）。</param>
    /// <param name="SelfCreature">作为"自身"的 Creature 表达式。</param>
    /// <param name="Target">目标 Creature 表达式，可为 <c>null</c> 字面量或 null。</param>
    /// <param name="Card">卡牌实例表达式。X 费卡取"消耗了多少"需要它；没有卡牌上下文时为 null。</param>
    public sealed record Context(string Player, string SelfCreature, string Target, string? Card = null)
    {
        /// <summary>
        /// 把状态模型 id 解析成 C# 类型引用（"状态层数"来源要用）。
        ///
        /// 由调用方注入——生成器知道工程（本工程定义的状态走本项目的命名空间），
        /// 而这里不该知道工程的存在。没注入时退回"按原版命名空间猜"。
        /// </summary>
        public Func<string, string>? PowerTypeFor { get; init; }

        /// <summary>
        /// 把卡牌模型 id 解析成 C# 类型引用（"按条件数牌的牌 ID 那一维"要用，判定是
        /// <c>c is X</c>）。同样由调用方注入，理由同 <see cref="PowerTypeFor"/>；
        /// 没用到"牌 ID"那一维时不必给。
        /// </summary>
        public Func<string, string>? CardTypeFor { get; init; }

        /// <summary>卡牌实例方法里的上下文（Owner 是 Player）。</summary>
        public static Context ForCard(string cardVar = "this") => new(
            Player: $"{cardVar}.Owner",
            SelfCreature: $"{cardVar}.Owner.Creature",
            Target: "target",
            Card: cardVar);

        /// <summary>状态实例方法里的上下文（Owner 是 Creature）。状态没有"这张牌"，X 值用不了。</summary>
        public static Context ForPower(string powerVar = "this") => new(
            Player: $"{powerVar}.Owner.Player",
            SelfCreature: $"{powerVar}.Owner",
            Target: "target");

        /// <summary>
        /// 静态方法里的上下文：游戏只传进来卡牌实例与目标，所以用参数名取数。
        /// </summary>
        public static Context ForStaticCard(string cardVar = "card") => new(
            Player: $"{cardVar}.Owner",
            SelfCreature: $"{cardVar}.Owner.Creature",
            Target: "target",
            Card: cardVar);
    }

    /// <summary>从模型 id 里取出条目名（POWER.STRENGTH_POWER → STRENGTH_POWER）。</summary>
    private static string PowerEntry(string powerId) =>
        powerId.Contains('.') ? powerId[(powerId.LastIndexOf('.') + 1)..] : powerId;

    /// <summary>
    /// 生成"求出来源值"的语句，最后一行把结果写进 <paramref name="resultVar"/> 并返回。
    ///
    /// 用语句而不是单个表达式，因为取状态层数需要先判空；调用方把这些语句放进一个方法体里。
    /// </summary>
    public static IEnumerable<string> Emit(ValueSource source, Context ctx, string resultVar = "value")
    {
        foreach (string line in EmitValue(source, ctx, resultVar))
        {
            yield return line;
        }

        yield return $"return {resultVar};";
    }

    /// <summary>
    /// 生成"求出来源值"的语句，结果写进 <paramref name="resultVar"/>，**不返回**。
    ///
    /// 与 <see cref="Emit"/> 的区别在取不到来源时的处理：这里写 0 而不是提前 return。
    /// 因为计算型的最终值是 <c>基础值 + 变量 × 变量系数</c>，变量为 0 时结果应当是
    /// 基础值本身；提前返回会把基础值也一起丢掉。
    /// </summary>
    public static IEnumerable<string> EmitValue(ValueSource source, Context ctx, string resultVar = "value")
    {
        yield return $"decimal {resultVar} = 0m;";

        foreach (string line in Assign(source, ctx, resultVar))
        {
            yield return line;
        }
    }

    /// <summary>
    /// 只发"把来源值写进 <paramref name="resultVar"/>"的那几句（**不含**声明）。
    ///
    /// 给"同一个变量按等级用不同算式"那种场合用：调用方自己声明一次结果变量，然后在
    /// 每个分支里各调一次本方法——这样每支的局部量（<c>player</c> / <c>who</c> /
    /// <c>powerOwner</c>）落在各自的块作用域里，不会互相撞名。
    /// <see cref="EmitValue"/> 就是"声明 + 本方法"，所以两者的语句顺序完全一样。
    /// </summary>
    public static IEnumerable<string> Assign(ValueSource source, Context ctx, string resultVar = "value")
    {
        switch (SourceCatalog.Read(source.Kind))
        {
            // 不取值（固定值）。调用方只在"算得出来"时才来取，所以这一支是兜底：
            // 变量就是 0，结果等于基础值本身。
            case SourceReader.None:
                yield return "// 没有选变量来源，按 0 处理。";
                yield break;

            case SourceReader.PlayerScoped:
                foreach (string line in PlayerValue(source, ctx, resultVar))
                {
                    yield return line;
                }

                yield break;

            case SourceReader.PowerStacks:
                foreach (string line in PowerStacksValue(source, ctx, resultVar))
                {
                    yield return line;
                }

                yield break;

            case SourceReader.CreatureStat:
                foreach (string line in CreatureValue(source, ctx, resultVar))
                {
                    yield return line;
                }

                yield break;

            // 计数：读法在计数模块里（那二十种的口径与表达式都是它的）。
            case SourceReader.Counter:
                yield return $"var player = {ctx.Player};";
                yield return "if (player?.PlayerCombatState is not null)";
                yield return "{";
                yield return $"    {resultVar} = {CounterExpression(source, ctx)};";
                yield return "}";
                yield break;
        }
    }

    /// <summary>
    /// 玩家维度（资源、X 值、金币、回合数、计数）的取值。
    ///
    /// 一律先落一个 <c>player</c> 局部量再判空：这些数据都挂在战斗状态上，
    /// 战斗外（卡面预览、牌组界面）读不到，那时变量取 0 而不是抛异常。
    /// </summary>
    private static IEnumerable<string> PlayerValue(ValueSource source, Context ctx, string resultVar)
    {
        // X 值只有卡牌才有；状态里误用时给出说明而不是发出编译不过的代码。
        if (SourceCatalog.Find(source.Kind) is { NeedsCard: true } && ctx.Card is null)
        {
            yield return "// X 值只在卡牌里可用（状态没有对应的实例）。";
            yield break;
        }

        yield return $"var player = {ctx.Player};";
        yield return "if (player?.PlayerCombatState is not null)";
        yield return "{";
        yield return $"    {resultVar} = {PlayerExpression(source, ctx)};";
        yield return "}";
    }

    /// <summary>
    /// 玩家维度的取值表达式：本模块那一行给出的表达式，或转给计数模块。
    /// </summary>
    private static string PlayerExpression(ValueSource source, Context ctx) =>
        SourceCatalog.Find(source.Kind)?.Expression is string template
            ? Fill(template, player: "player", who: ctx.SelfCreature, card: ctx.Card)
            : CounterExpression(source, ctx);

    /// <summary>计数类来源的表达式（读法在计数模块里）。</summary>
    private static string CounterExpression(ValueSource source, Context ctx) =>
        CounterCatalog.Find(source.Kind) is Counter counter
            ? CounterEmitter.Expression(counter, source, ctx.Card, ctx.CardTypeFor)
            : "0m";

    /// <summary>
    /// 状态层数：读的是**指定状态**（<see cref="ValueSource.PowerId"/>）的层数，
    /// 没有该状态时为 0。
    ///
    /// 状态类型由调用方注入的解析器给出（本工程定义的状态走本项目命名空间，原版的走游戏
    /// 命名空间），所以自定义状态也能当来源用；没注入时按原版命名空间猜。
    /// </summary>
    private static IEnumerable<string> PowerStacksValue(ValueSource source, Context ctx, string resultVar)
    {
        if (string.IsNullOrWhiteSpace(source.PowerId))
        {
            // 没选状态时挑不到层数。校验器会报错，这里给 0 避免生成出坏代码。
            yield return "// 选了\"状态层数\"却没选是哪个状态，按 0 处理。";
            yield return $"{resultVar} = 0m;";
            yield break;
        }

        string powerOwner = source.Actor == ValueSourceActor.Target
            ? ctx.Target
            : ctx.SelfCreature;

        string powerType = ctx.PowerTypeFor is not null
            ? ctx.PowerTypeFor(source.PowerId)
            : $"MegaCrit.Sts2.Core.Models.Powers.{Naming.ToPascal(PowerEntry(source.PowerId))}";

        yield return $"var powerOwner = {powerOwner};";
        yield return "if (powerOwner is not null)";
        yield return "{";
        yield return $"    {resultVar} = powerOwner.GetPower<{powerType}>()?.Amount ?? 0;";
        yield return "}";
    }

    /// <summary>
    /// 生物属性：取自谁由 Actor 决定（自身 / 目标），取不到时为 0。
    ///
    /// 表达式里用的是**那个生物表达式本身**（<c>target!.CurrentHp</c>），不是上面那个
    /// 局部量：局部量只为判空，读数直接对着生物表达式写——这是原本的写法，改掉它会让
    /// 每一个用到生物属性的产物都变一行（纯重构不该有这种差异）。
    /// </summary>
    private static IEnumerable<string> CreatureValue(ValueSource source, Context ctx, string resultVar)
    {
        string owner = source.Actor == ValueSourceActor.Target ? ctx.Target : ctx.SelfCreature;
        string template = SourceCatalog.Find(source.Kind)?.Expression ?? "0m";

        yield return $"var who = {owner};";
        yield return "if (who is not null)";
        yield return "{";
        yield return $"    {resultVar} = {Fill(template, player: ctx.Player, who: owner, card: ctx.Card)};";
        yield return "}";
    }

    /// <summary>
    /// 把行上的表达式模板填成真表达式。只有三个占位符（见 <see cref="SourceSpec"/>），
    /// 所以就是三次替换；缺的参数填 <c>null</c>（X 值在状态里就是这样，调用方已先拦下）。
    /// </summary>
    private static string Fill(string template, string player, string who, string? card) =>
        template
            .Replace("{player}", player, StringComparison.Ordinal)
            .Replace("{who}", who, StringComparison.Ordinal)
            .Replace("{card}", card ?? "null", StringComparison.Ordinal);
}
