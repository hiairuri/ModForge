using System.Text;
using ModForge.Core.Generate;
using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;
using ModForge.Core.Modules.Targets;

namespace ModForge.Core.Modules.Effects;

/// <summary>
/// 一个效果要实现的那几个**面**。每个效果一个文件（<c>Kinds/</c>），只写自己有的面。
///
/// 实测下来一种效果最多牵动六处，所以接口就是这几处：
///
///   <see cref="Emit"/>     —— 生成 C# 语句（每种效果都有）
///   <see cref="Describe"/> —— 写成一句话（每种效果都有）
///   <see cref="Schema"/>   —— 界面元数据（每种效果都有）
///   变量槽 / 选牌策略 / 校验 —— 只有一部分效果涉及，见各自的接口
///
/// **共用层不重复**：目标循环、数值表达式、选牌出码留在 <c>EffectEmitter</c> 里，
/// 句式用词（"手牌中所有牌"…）在**描述模块**（<c>Modules/Descriptions/</c>）里，
/// 行为文件只写差异。实测那些辅助被引用得很密（<c>PickCardsLines</c> 12 处、
/// <c>PickLabel</c> 11 处），所以搬进各文件的只有真正不同的那几行。
///
/// **时机不是这里的一个面**：同一个效果可以挂在任何时机上（见 <c>EffectTrigger</c>），
/// 所以时机是共用层的一条独立轴，由**时机模块**（<c>Modules/Timing/</c>）负责规划与发出。
/// </summary>
internal interface IEffectBehavior
{
    /// <summary>这条行为对应哪个效果种类。</summary>
    EffectKind Kind { get; }

    /// <summary>生成这条效果的语句。共用层已经算好数值表达式（<see cref="EffectEmit.Amount"/>）。</summary>
    void Emit(EffectEmit args);

    /// <summary>
    /// 写成一句话。描述模块已经备好 <c>who / num / kind</c> 三个词，
    /// 时机前缀与"重复 N 次"后缀也由它加（这里只管这一句本身）。
    /// 上下文记录本身在描述模块里（<c>EffectDescribe</c>）——它由那一侧定义、这一侧实现。
    /// </summary>
    string Describe(EffectDescribe args);

    /// <summary>界面元数据（标签、可选目标、要不要填数值…）。</summary>
    EffectSchemaEntry Schema { get; }
}

/// <summary>发一条效果所需的全部上下文。共用层把能算的都算好了，行为只管写语句。</summary>
/// <param name="Sb">输出目标。</param>
/// <param name="Effect">这条效果。</param>
/// <param name="Ctx">卡牌 / 状态两种上下文的表达式（自己、玩家、目标、选择上下文…）。</param>
/// <param name="Indent">方法体内最外层的缩进。</param>
/// <param name="Amount">数值表达式，已由 <c>EffectEmitter.AmountExpr</c> 算好。</param>
/// <param name="Emitter">生成器本身（登记就地计算方法、解析类型引用时要用）。</param>
internal sealed record EffectEmit(
    StringBuilder Sb,
    EffectDef Effect,
    EmitContext Ctx,
    string Indent,
    string Amount,
    EffectEmitter Emitter)
{
    /// <summary>按当前缩进写一行。</summary>
    public void Line(string text) => Sb.AppendLine(Indent + text);

    /// <summary>在当前缩进下再进一层写一行。</summary>
    public void Inner(string text) => Sb.AppendLine(Indent + "    " + text);

    /// <summary>写一行空行（生成物里用来分段）。</summary>
    public void Blank() => Sb.AppendLine();

    /// <summary>
    /// 按**作用对象**把一条语句发出去（"造成伤害/获得格挡/回复生命"这一族都这么写）。
    ///
    /// 目标循环本身在目标模块（<c>Modules/Targets/</c>，见 <c>EffectEmitter.EmitAgainstTargets</c>）
    /// ——自己就直接写、队友与敌人就 foreach、随机就先挑一个。各效果只写"对每个目标做什么"，
    /// 那句里的目标用回调给的变量名（<c>target</c> / <c>foe</c> / <c>ally</c> / <c>everyone</c> /
    /// <c>picked</c>…）。
    ///
    /// **哪一个轴**（落到生物上，还是落到玩家上）由这条效果声明的 <see cref="TargetScope"/>
    /// 决定——所以玩家类的效果（能量 / 星辰 / 抽牌 / 充能球 / 铸造 / 召唤）也用这一个方法，
    /// 回调收到的是**玩家**表达式而不是生物表达式。
    /// </summary>
    /// <param name="statement">把目标表达式填进一句 C# 语句里（接收目标变量名，返回整句）。</param>
    public void AgainstTargets(Func<string, string> statement) =>
        Emitter.EmitAgainstTargets(Sb, Effect, Ctx, Indent, statement);

    /// <summary>
    /// 同 <see cref="AgainstTargets(Func{string, string})"/>，但"对每个目标做什么"是**一段**
    /// （多行）。回调收"目标变量名"与"这一层的缩进"，返回该层要写的几行（每行自带缩进）。
    ///
    /// "拿一个状态再判空再动它"这类要多行的效果用它——目标循环的骨架只有一个，
    /// 单行与多行两种用法都走它（见 <c>EmitAgainstTargets</c> 的说明）。
    /// </summary>
    public void AgainstTargetsBlock(Func<string, string, IEnumerable<string>> body) =>
        Emitter.EmitAgainstTargets(Sb, Effect, Ctx, Indent, body);
}

/// <summary>
/// 界面元数据。对应 <c>EditorSchema</c> 里原来那条 <c>Describe(...)</c> 调用。
///
/// 用命名记录而不是匿名对象，是为了让"界面表单长什么样"也能被快照对比（见
/// <c>tools/gensnapshot</c> 的 <c>_schema.json</c>）——匿名对象没法跨类型传递，而搬进各效果
/// 文件之后元数据必须是个能返回的类型。
///
/// **属性名刻意用 camelCase**（与 C# 惯例相反）：这样序列化结果**与命名策略无关**。
/// 原来的匿名对象字段就叫这些名字，所以换成记录后 JSON 逐字节不变；换成 PascalCase 再靠
/// camelCase 策略去转，就多了一条"策略被人改掉就悄悄变样"的隐患。
///
/// 可选项都有默认值（与原来的 <c>Describe(...)</c> 一致），所以各效果文件只写自己用到的那几项。
/// <paramref name="kind"/> 由 <c>EffectCatalog</c> 统一填，行为里不用管。
///
/// 公开是因为验收工具（<c>kindtest</c>）要按字段断言——与变量模块、选牌模块、卡牌核心一样，
/// 模块的模型与查询是公开的，具体行为实现（<c>Kinds/</c> 里的类）留在内部。
/// </summary>
/// <param name="scope">
/// 这条效果的**作用域**（造物 / 玩家 / 牌，见 <see cref="TargetScope"/>）。
///
/// 声明它、而不是自己列一串候选：候选由目标模块按作用域给（见 TargetCatalog.Options），
/// 出码也从同一处展开。以前各写各的，于是同一族效果的目标候选互相对不上
/// （移除状态少了三档、回复生命与造成伤害各少一档）。
/// </param>
public sealed record EffectSchemaEntry(
    string kind,
    string labelZh,
    string labelEn,
    TargetScope scope,
    bool amount,
    bool needsPower = false,
    bool needsCard = false,
    bool needsOrb = false,
    bool needsEnchantment = false,
    bool hasPermanent = false,
    string[]? durations = null,
    bool needsTransform = false,
    bool needsTargetPile = false,
    bool targetPileIsAdd = false,

    /// <summary>激发充能球要选"激发哪些"（最右 / 最左 / 全部 / 随机）。</summary>
    bool needsEvokeTarget = false,

    /// <summary>激发充能球可以筛**球的种类**（多选，空 = 不限）。</summary>
    bool needsOrbFilter = false,

    /// <summary>
    /// 这条效果有一个"生成方式"下拉（目前只有"加入指定卡牌"：指定牌 / 从卡池 N 选一）。
    /// 界面据此画那个下拉，并按取值切换后面几格（指定牌名 ↔ 候选数与筛选）。
    /// </summary>
    bool hasAddMode = false,

    /// <summary>
    /// 这条效果多出"移除方式 / 移除哪一类"两个下拉（只对"移除状态"有意义）。
    ///
    /// 它同时意味着"哪个状态"那一格**可能不需要**：选"所有正面/负面"时不必指定状态，
    /// 所以界面按这两格的取值决定隐不隐它（见 ui/app.js）——与"填了不生效的格一律不显示"
    /// 是同一条规矩。
    /// </summary>
    bool hasPowerRemoval = false,

    /// <summary>
    /// 这条效果会不会**产出一组牌**（界面上给它一格「临时牌集合ID」，填了就把那几张牌存起来，
    /// 供同一张牌后面的效果再动它们）。见 <c>Modules/CardSets</c>。
    ///
    /// 由种类算出（<c>CardSetCatalog.Stores</c>），不是每条行为自己声明：判据是"它的结果是不是
    /// 一组牌"，与行为没关系。放在这里是为了让界面不必自己维护一份种类名单。
    /// </summary>
    bool canStoreSet = false,

    /// <summary>
    /// 会挑牌的效果的选牌策略（十步里允许哪几项）。
    /// 不挑牌的效果是 null，界面据此不画选牌那一段。
    /// </summary>
    EffectPickSchema? pick = null)
{
    /// <summary>
    /// 这个作用域能选哪几档目标（界面的下拉就照它画）。
    ///
    /// 是**算出来的**而不是存的：存一份就多一份会与目标模块对不上的名单，
    /// 而"对不上"这件事已经发生过一次（各效果自己列候选）。
    /// </summary>
    public string[] targets => TargetCatalog.Names(scope);

    /// <summary>
    /// 与 <see cref="targets"/> 一一对应的**界面标签**。
    ///
    /// 由后端下发而不是让界面查词表：同一档在两轴上的名字不同（"所有友方"在玩家轴上是
    /// "所有玩家"、"目标"在玩家轴上是"任意一名友方"），界面自己判轴就会多出一份会与
    /// 目标模块对不上的映射。
    /// </summary>
    public string[] targetLabels => TargetCatalog.Labels(scope);

    /// <summary>
    /// **哪几档需要这张牌给出一个对象，以及各自接受哪几种卡牌目标**
    /// （见 <see cref="TargetCatalog.PlayedTargetSources"/>；键是档名，值是卡牌目标名）。
    ///
    /// 界面拿它对一下就知道某一档此刻能不能用（用不了就不显示它）；校验器用的是同一份数据
    /// （<c>TargetCatalog.AcceptsPlayedTarget</c>），所以两侧不会分叉。
    /// </summary>
    public IReadOnlyDictionary<string, string[]> playedTargetSources =>
        TargetCatalog.PlayedTargetSources(scope);

    public string defaultTarget => TargetCatalog.DefaultFor(scope).ToString();
}

/// <summary>
/// 一条效果的选牌策略投影（供界面用）。与 <see cref="Modules.CardPicking.CardPickPolicy"/>
/// 是同一份数据：那边是模块内部的策略，这边是它的 JSON 形状。
/// </summary>
/// <param name="sources">流程一允许哪几档（此牌 / 牌堆 / 卡组）。</param>
/// <param name="dimensions">
/// 允许按哪几维筛（类型 / 牌 ID / 标签 / 关键词 / 稀有度 / 能量消耗 / 星辰消耗）。
/// 界面据此决定"筛选维度"那个多值框里有哪些候选——维度可以同时生效。
/// </param>
/// <param name="singlePile">流程二只能选一个牌堆（消耗 / 自动打出这类）。</param>
/// <param name="usesMode">有没有流程九（全部 / 选择 / 随机 / 至多）。</param>
/// <param name="usesPrompt">这条效果会不会打开选择界面（导出时必须写提示语）。</param>
/// <param name="constraint">合法性约束（黑名单），界面用来解释"为什么某些牌不出现在候选里"。</param>
/// <param name="selfNoAmount">"这张牌自己"那一档不需要数值。</param>
/// <param name="countFromEffect">
/// "挑几张"就是这条效果自己的数值（<c>&lt;base&gt;</c> 三件套），所以界面上**不画**流程十那一组
/// ——数值那三格本身就是张数（见 <c>CardPickPolicy.CountFromEffect</c>，消耗牌走这条）。
/// </param>
public sealed record EffectPickSchema(
    string[] sources,
    string[] dimensions,
    bool singlePile,
    bool usesMode,
    bool usesPrompt,
    string constraint,
    bool selfNoAmount,
    bool countFromEffect);
