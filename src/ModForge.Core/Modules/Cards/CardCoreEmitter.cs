using ModForge.Core.Model;

namespace ModForge.Core.Modules.Cards;

/// <summary>
/// 卡牌固有属性 → C# 代码。**唯一**出码处：构造器签名、关键词、X 费、X 星、星辰费用、
/// 费用类升级、重放属性都在这里，生成器只按固定顺序调它。
///
/// 顺序很重要：生成的源码要逐字节稳定，所以每个方法对应生成类里的一个固定位置，
/// 由调用方按原顺序调用（见 <c>CodeGenerator.GenerateCard</c>）。
/// </summary>
public static class CardCoreEmitter
{
    /// <summary>构造器要传给基类的四个参数。</summary>
    public static string BaseCall(CardCore core) =>
        $"base({core.ConstructorEnergyCost}, CardType.{core.Type}, CardRarity.{core.Rarity}, TargetType.{core.Target})";

    /// <summary>
    /// 星辰费用的声明（`CanonicalStarCost`）。
    ///
    /// 游戏用虚属性表达"这张牌要几点星辰"，<c>-1</c> 表示不要（原版 40 多张星费卡都是这么写的，
    /// 例如 AstralPulse 是 3）。以前这一项**从来没被生成过**——字段与界面都有，填了却没反应。
    /// </summary>
    public static IEnumerable<string> StarCost(CardCore core)
    {
        if (!core.HasStarCost)
        {
            yield break;
        }

        yield return string.Empty;
        yield return "    /// <summary>这张牌要消耗的星辰。</summary>";
        yield return $"    public override int CanonicalStarCost => {core.StarCost};";
    }

    /// <summary>关键词（消耗 / 虚无 / 固有…）。</summary>
    public static IEnumerable<string> Keywords(CardCore core)
    {
        if (core.Keywords.Count == 0)
        {
            yield break;
        }

        string keywords = string.Join(", ", core.Keywords.Select(k => $"CardKeyword.{k}"));

        yield return string.Empty;
        yield return $"    public override IEnumerable<CardKeyword> CanonicalKeywords => new CardKeyword[] {{ {keywords} }};";
    }

    /// <summary>
    /// X 费与 X 星。费用在打出时按消耗量决定，游戏靠这两个属性识别（卡面显示 X），
    /// 所以它们不是"数值"，而是两个开关。
    ///
    /// **两个属性的访问修饰符不一样**，这是游戏自己定的、很容易写错：
    ///   <c>HasEnergyCostX</c> 是 <c>protected virtual</c>（CardModel.cs:373）
    ///   <c>HasStarCostX</c>   是 <c>public virtual</c>（CardModel.cs:467）
    /// 写成 <c>protected override</c> 会报 CS0507"无法更改访问修饰符"。以前这里一直按
    /// protected 发，而**没有任何工具用过 X 星**，所以这个错一直潜伏着——界面上一勾 X 星
    /// 就会生成编译不过的卡（cardtest 第一次跑就抓到了）。
    /// </summary>
    public static IEnumerable<string> XCostFlags(CardCore core)
    {
        if (core.EnergyCostX)
        {
            yield return string.Empty;
            yield return "    /// <summary>费用是 X：打出时消耗全部能量，效果按消耗量结算。</summary>";
            yield return "    protected override bool HasEnergyCostX => true;";
        }

        if (core.StarCostX)
        {
            yield return string.Empty;
            yield return "    /// <summary>星辰费用是 X。</summary>";
            yield return "    public override bool HasStarCostX => true;";
        }
    }

    /// <summary>
    /// 升级时要改的费用（能量与星辰各一条）。
    ///
    /// 两条都要走增量语义（`UpgradeBy`），因为 <c>UpgradeCostDelta</c> / <c>UpgradeStarDelta</c>
    /// 表达的就是"每升一级便宜多少"。等级表那一套（每级绝对赋值）对费用不适用：
    /// 游戏没有"把费用设成某个绝对值"的升级接口。
    ///
    /// 星辰那条要注意两件事：
    ///   * <c>UpgradeStarCostBy</c> 在 X 星时会**抛异常**（游戏源码里写死的），校验器会拦下；
    ///   * 只改星辰的卡也必须有 `OnUpgrade` —— 调用方要把它算进"要不要发 OnUpgrade"的判断，
    ///     否则填了完全没反应（<see cref="CardCore.HasCostUpgrade"/> 就是给那个判断用的）。
    /// </summary>
    public static IEnumerable<string> UpgradeLines(CardCore core)
    {
        if (core.UpgradeCostDelta != 0)
        {
            yield return $"EnergyCost.UpgradeBy({core.UpgradeCostDelta});";
        }

        if (core.UpgradeStarDelta != 0 && !core.StarCostX)
        {
            yield return $"UpgradeStarCostBy({core.UpgradeStarDelta});";
        }
    }

    /// <summary>
    /// 按玩家人数的限制（界面上叫「多人游戏卡牌」）。
    ///
    /// 对应游戏的 <c>CardModel.MultiplayerConstraint</c> 虚属性，游戏在**生成与商店**那一侧
    /// 过滤它（<c>CardFactory</c> / <c>CardPoolModel.GetUnlockedCards</c> /
    /// <c>MerchantInventory</c>）：<c>MultiplayerOnly</c> 的牌单人游戏里根本不会出现
    /// （原版"我相信你""恶魔之盾""生命赞歌"这几张需要队友的牌就是它）。
    ///
    /// 默认 <c>None</c>（＝两种模式都能出现）**不写**：那是基类的默认值，
    /// 写出来等于给每一张已有的卡加一行废话，"逐字节不变"也不再成立。
    /// </summary>
    public static IEnumerable<string> Multiplayer(CardCore core)
    {
        if (core.Multiplayer == CardMultiplayerConstraint.None)
        {
            yield break;
        }

        yield return string.Empty;
        yield return "    /// <summary>";
        yield return core.Multiplayer == CardMultiplayerConstraint.MultiplayerOnly
            ? "    /// 只在多人合作里出现：单人游戏的卡池与商店都不会给出这张牌。"
            : "    /// 只在单人游戏里出现：多人合作的卡池与商店都不会给出这张牌。";
        yield return "    /// </summary>";
        yield return $"    public override CardMultiplayerConstraint MultiplayerConstraint => CardMultiplayerConstraint.{core.Multiplayer};";
    }

    /// <summary>
    /// 这张牌**自己的重放次数**（0 = 不重放）——每张卡都有这一条。
    ///
    /// 游戏侧的重放是 <c>CardModel.BaseReplayCount</c>，一个**普通字段**：存档格式只认
    /// 「卡牌 id + 升级等级 + **卡牌类自己声明的 SavedProperty** + 附魔」，字段本身不进存档。
    /// 所以要让它跨战斗、跨读档保留，必须用 <c>[SavedProperty]</c> 自己记一份——
    /// 原版 <c>SpoilsMap</c> 就是这个形状（SavedProperty 存值 + AfterCreated 给新建时的默认值）。
    ///
    /// **这里顺手修掉一个真问题**：以前把标准值直接写在 <c>AfterCreated()</c> 里，而游戏源码
    /// 明确注明 AfterCreated **不在读档时调用**（读档走 <c>FromSerializable</c> →
    /// <c>AfterDeserialized</c>），于是读档后重放次数就丢了。现在值存在存档字段里，
    /// 读档由存档回填，AfterCreated 只负责给"新建的牌"一个标准值。
    ///
    /// 只发 AfterCreated 的情形见下：标准值为 0 时不必发（字段本来就是 0）。
    /// </summary>
    public static IEnumerable<string> Replay(CardCore core)
    {
        yield return string.Empty;
        yield return "    /// <summary>";
        yield return "    /// 这张牌自己的重放次数（0 = 不重放）：被打出时整张牌多执行这么多次。";
        yield return "    ///";
        yield return "    /// 对应 CardModel.BaseReplayCount，但那是个普通字段、不进存档，所以这里用";
        yield return "    /// [SavedProperty] 记一份——存档格式只认卡牌类自己声明的字段。setter 把它写进";
        yield return "    /// BaseReplayCount，读档回填时（存档系统反射调 setter）就自动生效了。";
        yield return "    /// </summary>";
        yield return "    [SavedProperty]";
        yield return "    public int ReplayCount";
        yield return "    {";
        yield return "        get => _replayCount;";
        yield return "        set";
        yield return "        {";
        yield return "            AssertMutable();";
        yield return "            _replayCount = value;";
        yield return "            BaseReplayCount = value;";
        yield return "        }";
        yield return "    }";
        yield return string.Empty;
        yield return "    private int _replayCount;";

        if (core.ReplayCount == 0)
        {
            // 标准值就是 0，而字段初始值本来就是 0，不必发 AfterCreated。
            yield break;
        }

        yield return string.Empty;
        yield return "    /// <summary>这张牌天生额外打出若干次（标准值，非升级得来）。</summary>";
        yield return "    public override void AfterCreated()";
        yield return "    {";
        yield return "        // 只在这一份还没被赋过值时给标准值。";
        yield return "        //";
        yield return "        // 读档不经过这里（走 AfterDeserialized，值由存档回填）；但战斗内的副本是";
        yield return "        // 克隆出来的（CloneCard 不调 AfterCreated）、跑级/加成的结果也可能带在这里，";
        yield return "        // 无脑覆盖会把它们抹掉。";
        yield return "        if (_replayCount == 0)";
        yield return "        {";
        yield return $"            ReplayCount = {core.ReplayCount};";
        yield return "        }";
        yield return "    }";
    }
}
