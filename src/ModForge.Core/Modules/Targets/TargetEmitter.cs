using System.Text;
using ModForge.Core.Model;

namespace ModForge.Core.Modules.Targets;

/// <summary>
/// 发一条效果的**作用对象**时需要的表达式。由效果共用层从 <c>EmitContext</c> 转过来——
/// 本模块只说"要哪些表达式"，不说"卡牌与状态分别怎么写"（那是共用层的事）。
///
/// 注意 <paramref name="Player"/> 同时是"玩家轴上的自己"与"宠物挂在谁身上"的答案：
/// 宠物属于玩家，所以宠物那一组一律从**玩家**表达式出发（卡牌里是 <c>Owner</c>、
/// 状态里是 <c>Owner.Player!</c>），与造物轴上的"自己"（一个 Creature）不是一回事。
/// </summary>
/// <param name="Self">造物轴上的"自己"：卡牌里是 <c>Owner.Creature</c>，状态里是 <c>Owner</c>。</param>
/// <param name="Player">玩家轴上的"自己"：卡牌里是 <c>Owner</c>，状态里是 <c>Owner.Player!</c>。</param>
/// <param name="CardTarget">打出时选定的目标（生物）；状态里没有，为 null。</param>
/// <param name="Enemies">敌人集合（<c>HittableEnemies</c>：打不到的已经滤掉）。</param>
/// <param name="PlayerCreatures">
/// **友方**集合：玩家阵营的**玩家生物**（<c>CombatState.PlayerCreatures</c>）。
///
/// 用它而不是 <c>CombatState.Allies</c>：后者把**宠物**（以及将来任何玩家阵营的非玩家生物）
/// 也算进去，于是"所有友方"会顺手作用到奥斯提上——原版不是这么做的
/// （原版"所有友方"的牌用 <c>PlayerCreatures</c>，见 <c>LegionOfBone</c>；"任意友方"的候选
/// 也带 <c>c.IsPlayer</c> 过滤）。想作用到宠物上得显式选宠物那一档。
/// </param>
/// <param name="CombatState">战斗状态（取 <c>Creatures</c> / <c>Players</c> 用）。</param>
/// <param name="Rng">随机数用的那一路（<c>Rng.CombatTargets</c>）——随机目标必须走游戏自己的 RNG，
/// 它进战斗存档的随机序列；自己 new 一个 Random 会让同样的种子打出不同的局面。</param>
/// <param name="NextIndex">
/// 发一个**新**的卡内唯一编号（随机/宠物目标要先落到局部变量上，同一条效果里两处就会重名、
/// 编译不过，所以名字带上编号）。
///
/// 给的是取编号的**办法**而不是编号本身：只有真需要变量那几档才调它。若在这里传一个已经
/// 算好的数，那么"这条效果是第几条"就会混进去——生成物里出现 <c>picked16</c> 而这张牌上
/// 只有一条随机效果，读的人会以为哪里漏了东西。
/// </param>
internal sealed record TargetContext(
    string Self,
    string Player,
    string? CardTarget,
    string Enemies,
    string PlayerCreatures,
    string CombatState,
    string Rng,
    Func<int> NextIndex);

/// <summary>
/// 把"作用对象"展开成代码——**唯一的一处**。
///
/// 三件事在这里定：目标怎么落到一个变量上（自己/选定目标是直接写，一批是 <c>foreach</c>，
/// 随机是先用游戏 RNG 挑一个再判空），局部变量叫什么，以及"没目标"时写什么。
/// 各效果只说"对每个目标做什么"（回调收变量名，返回那几行）。
///
/// 卡牌与状态两种上下文共用它：差别只在表达式（见 <see cref="TargetContext"/>），
/// 结构一模一样——所以 <c>foreach</c> 的骨架、判空、缩进只有这一份。
///
/// **两条轴各一套**（见 <see cref="TargetScope"/>）：造物轴落到 <c>Creature</c>、
/// 玩家轴落到 <c>Player</c>。同一档在两轴上落到的集合不同，这张表就是全部差异
/// （也是为什么界面上的名字与句子用词都必须**分轴**，见 <see cref="TargetCatalog.LabelOf"/>）：
///
///   | 档            | 造物轴                              | 玩家轴                          |
///   |---------------|-------------------------------------|---------------------------------|
///   | Self          | <c>Owner.Creature</c>               | 出牌的那个玩家                   |
///   | Target        | <c>cardPlay.Target</c>              | <c>cardPlay.Target.Player!</c>   |
///   | AllEnemies    | <c>HittableEnemies</c> 逐个          | —                               |
///   | RandomEnemy   | 敌人里随机一个                       | —                               |
///   | AllAllies     | <c>PlayerCreatures</c> 逐个（不含宠物）| <c>Players</c> 逐个              |
///   | 宠物组六档     | 先读玩家再取 <c>Osty</c>（自己那只 / 目标那只 / 每只各一次 / 随机一只；"其他"两档不含自己）||
///   | AllOtherAllies| 同上，去掉自己                       | 同上，去掉自己                    |
///   | RandomAlly    | 从 AllAllies 里随机一个（含自己）      | 随机一名玩家（含自己）            |
///   | RandomOtherAlly| 从 AllOtherAllies 里随机（不含自己）  | 随机一名其他玩家（不含自己）      |
///   | RandomPlayer  | —                                   | 所有玩家里随机一个（含自己）      |
///   | All           | <c>Creatures</c> 逐个                 | —                               |
///
/// **玩家宠物那一组四档**（<see cref="TargetCatalog.IsPetBand"/>）是**从玩家派生**的：
/// 先把目标读成**玩家**，再取那个玩家的 <c>Osty</c>。宠物是 <c>Creature?</c>
/// （不在场就是 null，见 <c>Player.Osty</c> 的注释），所以这一组一律带判空——不写就是空引用。
/// 只对造物轴成立（宠物是 Creature；收 Player 的命令接不住）。
/// </summary>
internal static class TargetEmitter
{
    internal static void Emit(
        StringBuilder sb,
        TargetScope scope,
        EffectTarget target,
        TargetContext ctx,
        string indent,
        Func<string, string, IEnumerable<string>> body)
    {
        var emit = new Emitter(sb, ctx, indent, body);

        // 宠物那一组先处理：它是"先读玩家、再取宠物"，与两条轴的分派不是一回事。
        if (TargetCatalog.IsPetBand(target))
        {
            emit.Pet(target);
            return;
        }

        // 状态里没有"打出时选定的目标"（那是目标那张牌的东西），而旧数据里这一档是存在的——
        // 那时由共用层的一个 switch 统一落地，于是两侧各有一个**隐含**口径：
        //   造物轴的效果落成"所有敌人"（旧 switch 把 Target 与 AllEnemies 并成同一支）
        //   玩家轴的效果**根本不看目标**，落成"自己"（它写死 ctx.Player）
        // 这两条旧口径保留在这里，是为了**不改老工程的行为**：换个说法就等于把人家写好的
        // 状态效果悄悄改成不做事。想明确指定，就用真正的档（自身 / 所有敌人 / …）。
        if (target == EffectTarget.Target && ctx.CardTarget is null)
        {
            if (scope == TargetScope.Player)
            {
                emit.One(ctx.Player);
            }
            else
            {
                emit.Each("Creature", "foe", ctx.Enemies);
            }

            return;
        }

        if (scope == TargetScope.Player)
        {
            emit.Player(target);
            return;
        }

        emit.Creature(target);
    }

    /// <summary>
    /// 展开本身。写成一个小类而不是一堆局部函数，是为了让"两条轴 × 几种形状"读得出来——
    /// 它们共用 <see cref="One"/> / <see cref="Each"/> / <see cref="PickOne"/> 三个形状。
    /// </summary>
    private sealed class Emitter(
        StringBuilder sb,
        TargetContext ctx,
        string indent,
        Func<string, string, IEnumerable<string>> body)
    {
        /// <summary>对这一个目标做（自己、选定的目标、旧口径的玩家）。</summary>
        internal void One(string variable) => Write(variable, indent);

        /// <summary>一批目标：foreach 一层，循环内部才是"对每个目标做什么"。</summary>
        internal void Each(string type, string variable, string items)
        {
            sb.AppendLine($"{indent}foreach ({type} {variable} in {items}.ToList())");
            sb.AppendLine($"{indent}{{");
            Write(variable, indent + "    ");
            sb.AppendLine($"{indent}}}");
        }

        /// <summary>
        /// 随机一个：先挑出来再判空。空集时 <c>NextItem</c> 返回 null，所以判空是必需的，
        /// 不是防御性代码——场上一个敌人都没有（或友方那边只剩自己）时真的会走到。
        /// </summary>
        private void PickOne(string type, string items)
        {
            string variable = $"picked{ctx.NextIndex()}";

            sb.AppendLine($"{indent}{type}? {variable} = {ctx.Rng}.NextItem({items});");
            sb.AppendLine($"{indent}if ({variable} is not null)");
            sb.AppendLine($"{indent}{{");
            Write(variable, indent + "    ");
            sb.AppendLine($"{indent}}}");
        }

        /// <summary>没目标可用：写一句注释，别让生成物看起来像"漏了"。</summary>
        internal void None() => sb.AppendLine($"{indent}// 没有可用目标");

        // ---------------------------------------------------------------- 造物轴

        internal void Creature(EffectTarget target)
        {
            switch (target)
            {
                case EffectTarget.Self:
                    One(ctx.Self);
                    return;

                case EffectTarget.Target when ctx.CardTarget is not null:
                    One(ctx.CardTarget);
                    return;

                case EffectTarget.AllEnemies:
                    Each("Creature", "foe", ctx.Enemies);
                    return;

                case EffectTarget.RandomEnemy:
                    PickOne("Creature", ctx.Enemies);
                    return;

                case EffectTarget.AllAllies:
                    Each("Creature", "ally", ctx.PlayerCreatures);
                    return;

                // 随机那一档抽的是"这一档的全集"（与"随机一名敌人"从全体敌人里抽同一个口径），
                // 所以"随机一名友方"**含自己**。
                case EffectTarget.RandomAlly:
                    PickOne("Creature", ctx.PlayerCreatures);
                    return;

                // "其他友方"＝不含自己。这条 `.Where(c => c != self)` 与原版"任意友方"的候选
                // 口径一致（卡牌侧它是 `Allies.Where(c => c != Owner.Creature)`，见 CardCmd）。
                case EffectTarget.AllOtherAllies:
                    Each("Creature", "ally", $"{ctx.PlayerCreatures}.Where(c => c != {ctx.Self})");
                    return;

                case EffectTarget.RandomOtherAlly:
                    PickOne("Creature", $"{ctx.PlayerCreatures}.Where(c => c != {ctx.Self})");
                    return;

                case EffectTarget.All:
                    Each("Creature", "everyone", $"{ctx.CombatState}.Creatures");
                    return;
            }

            None();
        }

        // ---------------------------------------------------------------- 玩家轴

        internal void Player(EffectTarget target)
        {
            switch (target)
            {
                case EffectTarget.Self:
                    One(ctx.Player);
                    return;

                case EffectTarget.Target when ctx.CardTarget is not null:
                    One($"{ctx.CardTarget}.Player!");
                    return;

                case EffectTarget.AllAllies:
                    Each("Player", "ally", $"{ctx.CombatState}.Players");
                    return;

                case EffectTarget.RandomPlayer:
                    PickOne("Player", $"{ctx.CombatState}.Players");
                    return;

                // "其他玩家"＝不含自己（"随机一名友方"这一档在玩家轴上与"随机一名玩家"同集，
                // 所以候选里只有"随机一名玩家"与"随机其他玩家"这两档）。
                case EffectTarget.AllOtherAllies:
                    Each("Player", "ally", $"{ctx.CombatState}.Players.Where(p => p != {ctx.Player})");
                    return;

                case EffectTarget.RandomOtherAlly:
                    PickOne("Player", $"{ctx.CombatState}.Players.Where(p => p != {ctx.Player})");
                    return;

                // 旧数据可能在玩家轴上留着 RandomAlly（它那时表示"除自己以外的随机一名玩家"）。
                // 照旧落地，不悄悄改成另一件事——校验器会提示改用 RandomOtherAlly。
                case EffectTarget.RandomAlly:
                    PickOne("Player", $"{ctx.CombatState}.Players.Where(p => p != {ctx.Player})");
                    return;
            }

            None();
        }

        // ---------------------------------------------------------------- 玩家宠物那一组（从玩家派生）

        /// <summary>
        /// "…的奥斯提"：先把目标读成**玩家**，再取那个玩家的宠物。
        ///
        /// 四种形状与玩家轴那几档一一对应，只是最后多取一次 <c>Osty</c>：
        ///   自己的奥斯提   —— <c>Owner.Osty</c>（判空）
        ///   目标的奥斯提   —— 卡牌选中那名玩家的宠物（<c>cardPlay.Target.Player?.Osty</c>，判空）
        ///   所有友方的奥斯提 —— 每个玩家各一只（没宠物 / 不在场的跳过）
        ///   随机一名友方的奥斯提 —— 除自己以外的随机一名玩家的宠物（判空）
        /// </summary>
        internal void Pet(EffectTarget target)
        {
            switch (target)
            {
                case EffectTarget.SelfPet:
                    OwnPet(ctx.Player);
                    return;

                case EffectTarget.TargetPet when ctx.CardTarget is not null:
                    TargetPet();
                    return;

                case EffectTarget.AllAlliesPet:
                    EachPet($"{ctx.CombatState}.Players");
                    return;

                // 与"所有友方的奥斯提"同一口径：抽的是"所有玩家的宠物"（含自己那只）。
                case EffectTarget.RandomAllyPet:
                    RandomPet($"{ctx.CombatState}.Players");
                    return;

                // "其他玩家的奥斯提"＝不含自己那只（与"所有其他友方"同一条 `.Where(p => p != …)`）。
                case EffectTarget.AllOtherAlliesPet:
                    EachPet($"{ctx.CombatState}.Players.Where(p => p != {ctx.Player})");
                    return;

                case EffectTarget.RandomOtherAllyPet:
                    RandomPet($"{ctx.CombatState}.Players.Where(p => p != {ctx.Player})");
                    return;
            }

            None();
        }

        /// <summary>自己那只。</summary>
        private void OwnPet(string player)
        {
            string pet = $"pet{ctx.NextIndex()}";

            sb.AppendLine($"{indent}if ({player}.Osty is Creature {pet})");
            sb.AppendLine($"{indent}{{");
            Write(pet, indent + "    ");
            sb.AppendLine($"{indent}}}");
        }

        /// <summary>卡牌选中的那名玩家的宠物（选定的目标一定是个生物，所以从这里取它的 Player）。</summary>
        private void TargetPet()
        {
            string pet = $"pet{ctx.NextIndex()}";

            sb.AppendLine($"{indent}if ({ctx.CardTarget}.Player?.Osty is Creature {pet})");
            sb.AppendLine($"{indent}{{");
            Write(pet, indent + "    ");
            sb.AppendLine($"{indent}}}");
        }

        /// <summary>每个玩家的宠物各一次（没宠物 / 不在场的跳过）。</summary>
        private void EachPet(string players)
        {
            int n = ctx.NextIndex();
            string owner = $"owner{n}";
            string pet = $"pet{n}";

            sb.AppendLine($"{indent}foreach (Player {owner} in {players}.ToList())");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    Creature? {pet} = {owner}.Osty;");
            sb.AppendLine($"{indent}    if ({pet} is not null)");
            sb.AppendLine($"{indent}    {{");
            Write(pet, indent + "        ");
            sb.AppendLine($"{indent}    }}");
            sb.AppendLine($"{indent}}}");
        }

        /// <summary>随机一名玩家的宠物（先随机玩家，再看他的宠物在不在）。</summary>
        private void RandomPet(string players)
        {
            int n = ctx.NextIndex();
            string owner = $"picked{n}";
            string pet = $"pet{n}";

            sb.AppendLine($"{indent}Player? {owner} = {ctx.Rng}.NextItem({players});");
            sb.AppendLine($"{indent}if ({owner}?.Osty is Creature {pet})");
            sb.AppendLine($"{indent}{{");
            Write(pet, indent + "    ");
            sb.AppendLine($"{indent}}}");
        }

        private void Write(string variable, string at)
        {
            foreach (string line in body(variable, at))
            {
                sb.AppendLine(line);
            }
        }
    }
}
