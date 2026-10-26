# 选牌筛选取值：多维度并存 + 费用条件（设计方案 · 第三稿）

> 状态：**已实现**（2026-09-22）。落地结论已并进 README 的「一套选择器：十步流程」一节；
> 六步实现顺序全部做完，测试计划那五行分别钉在 `picktest` / `kindtest` / `phrasetest` /
> `filetest` / `gensnapshot` 里。
>
> 第三稿按用户的三点修正定稿：
> ①**运算符放在 `pick`**（它是选牌的条件运算，不进变量模块；阈值仍在效果级）；
> ②采纳"按角色分组"的 `EffectUpgrade` / `EffectLevel` 重构（本轮同做，不再是可选项）；
> ③多值输入框**必须走现成的多值输入组件**（`ui/components.js` 的 `MFComponents`）。

## 目标

筛选取值从**单选一维**变成**多维度并存**：维度之间"与"、维度内部取值之间"或"。

> 能量消耗大于 2、星辰消耗大于 0 的 **稀有 攻击** 牌
> → `Rarity == Rare && Type == Attack && EnergyCost > 2 && StarCost > 0`

并新增两个**费用维度**：运算符六档单选（**存在选牌条件里**），阈值接**变量系统**
（基础值 + 变量 × 变量系数，**存在效果的数值上**），所以"能量消耗大于手牌数"也成立。

## 现状缺陷（本次一并修掉）

1. **维度与"需要星辰"是"或"而不是"与"。** `CardPickFilter.PredicateOn` 把各条拼进
   同一个 `||` 链。实测（`Dimension = CardType` + `Types = [Attack]` + `NeedsStarCost = true`）：

   ```
   c.Type == CardType.Attack || c.CanonicalStarCost >= 0 || c.HasStarCostX
   ```

   同一处的注释写的是"两者都是'与'"——**代码与注释相反**。这是本设计的直接动机之一。
2. **单选结构本身是瓶颈**："稀有攻击牌"只能靠一维（类型）+"按条件"那条老路
   （`CardFilterKind` 的扁平枚举，只为数牌 / 打牌时机保留），选牌器这边表达不了。
3. **费用条件只有一个布尔**（需要星辰），没有数值比较，也不接变量。

## 游戏 API 口径（已在 `decompiled/` 核实）

| 事实 | 依据 |
|---|---|
| 能量费用读 `card.EnergyCost.Canonical`（印在卡面上的"官方费用"） | `CardEnergyCost.Canonical`；`GetWithModifiers` 会算上战斗中的加减费，筛选**不能**用它 |
| X 费卡的 `Canonical` 是 **0** | 构造函数：`Canonical = CostsX ? 0 : canonicalCost`；另有 `CostsX` 标志 |
| 星辰费用读 `card.CanonicalStarCost`：`-1` = 没有星辰费用，`>= 0` = 有 | `CardModel.CanonicalStarCost => -1`（虚属性，具体卡覆写） |
| 星辰是 X 的牌看 `card.HasStarCostX` | `Stardust.cs`：`HasStarCostX => true` 且**不覆写** `CanonicalStarCost` → 它仍是 **-1** |

由此得到两处**必须在界面上写明**的口径：

- **能量与星辰在 X 上不对称**：X 费能量卡的 `Canonical` 是 0（"不小于 0"**包含**它），
  而 X 星辰卡的 `CanonicalStarCost` 是 -1（"不小于 0"**不包含**它，要用 `X` 那一档单独筛）。
  这是游戏数据本身的差别；"星辰与能量对齐"对齐的是**模型与界面**（同一套字段、同一套运算符）。
- 筛选读的是**印在卡面上的费用**，不是战斗中的实时费用：降费 / 加费效果不影响筛选结果。

## 职责划分（第三稿的关键决定）

| 东西 | 放在哪 | 为什么 |
|---|---|---|
| **运算符**（六档 + X） | `CardPick`（选牌侧） | 它是"选牌时的条件运算"，不参与变量模块的数值运算 |
| **阈值三件套** | `EffectDef`（效果数值侧） | 它要占变量槽、要能升级、要进等级表——这三件事整套机制挂在效果数值上 |

## 模型

### 选牌侧（`Modules/CardPicking/CardPick.cs`）

去掉 `Dimension`（`PickDimension` 枚举随之删除）与 `NeedsStarCost`（用户定的：开关废弃），
取值列表各自独立生效——空 = 不筛、非空 = 一条"或"组；两个费用的**运算符**住在这里：

```csharp
public sealed class CardPick
{
    public PickSource Source { get; set; } = PickSource.Piles;
    public List<PileKind> Piles { get; set; } = new();      // 流程二

    // 维度（空 = 不筛）。顺序即界面顺序，也是描述里定语链的顺序。
    public List<CardType> Types { get; set; } = new();
    public List<CardRarity> Rarities { get; set; } = new();
    public List<CardTag> Tags { get; set; } = new();
    public List<CardKeyword> Keywords { get; set; } = new();
    public List<string> CardIds { get; set; } = new();      // 牌 ID（原"按牌名"）

    /// 能量消耗这一维的运算符。None = 这一维不参与筛选（阈值也就不被读）。
    public CostComparison EnergyComparison { get; set; } = CostComparison.None;

    /// 星辰消耗这一维的运算符。
    public CostComparison StarComparison { get; set; } = CostComparison.None;

    public PickMode Mode { get; set; } = PickMode.Choose;
    public NumericValue Count { get; set; } = new(1, new ValueSource());
}

/// 费用运算符六档（用户定的顺序与措辞）。
public enum CostComparison
{
    None,          // 不筛选（默认；界面上下拉里是"不筛选"，不出现在启用后的默认值里）
    AtLeast,       // 不小于  >=
    AtMost,        // 不大于  <=
    GreaterThan,   // 大于    >
    LessThan,      // 小于    <
    NotEqual,      // 不等于  !=
    IsX,           // X（选它时阈值不参与）
}
```

### 效果侧（`Model/EffectDef.cs`）

阈值与 `Amount`（base）/ `Repeat` 并列，形状照它们（扁平两件：基础值 + 变量来源）：

```csharp
public sealed class EffectDef
{
    …
    /// 能量消耗条件的**阈值**（基础值）。只对"调用选牌控件"的效果有意义。
    public int EnergyAmount { get; set; }

    /// 能量阈值的变量来源（没选 = 固定值）。
    public ValueSource EnergySource { get; set; } = new();

    public int StarAmount { get; set; }
    public ValueSource StarSource { get; set; } = new();
}
```

读取走同一条路：`NumericValue` 再多两个工厂（`NumericValue.EnergyOf(effect, level)` /
`StarOf(...)`），与 `NumericValue.Of` / `RepeatOf` 并列；`EffectState`（等级取值）
多两个 `(int Amount, ValueSource Source)` 组。

### 升级与等级表（**本轮同做**：按角色分组）

第三稿采纳"按角色分组"，因为 `EffectUpgrade` 现在会长到 **6 对增量 + 4 个来源**，
而代码里已经有一条针对这种平行字段的教训注释（`EffectResolver.Merge`：
"这里必须跟着一起长：漏掉一项的表现是……生成结果里只有一部分"）。

```csharp
/// 一个数值部件的增量（升级用）：基础值增量 / 变量系数增量 / 升级后换用的变量。
public readonly record struct ValuePart(int AmountDelta, decimal PerUnitDelta, ValueSource? Source)
{
    public bool IsSet => AmountDelta != 0 || PerUnitDelta != 0 || Source is not null;
}

public sealed class EffectUpgrade
{
    public ValuePart Base { get; set; }        // 现在叫 AmountDelta / PerUnitDelta / Source
    public ValuePart Repeat { get; set; }      // 现在叫 RepeatDelta / RepeatPerUnitDelta / RepeatSource
    public ValuePart Energy { get; set; }      // 新
    public ValuePart Star { get; set; }        // 新

    public bool IsSet => Base.IsSet || Repeat.IsSet || Energy.IsSet || Star.IsSet;
}

public sealed class EffectLevel
{
    public int? Amount { get; set; }           // 现有（沿用上一级 = null）
    public decimal? PerUnit { get; set; }
    public ValueSource? Source { get; set; }
    public int? Repeat { get; set; }
    public decimal? RepeatPerUnit { get; set; }
    public ValueSource? RepeatSource { get; set; }
    public int? EnergyAmount { get; set; }     // 新
    public decimal? EnergyPerUnit { get; set; }
    public ValueSource? EnergySource { get; set; }
    public int? StarAmount { get; set; }
    public decimal? StarPerUnit { get; set; }
    public ValueSource? StarSource { get; set; }
}
```

`EffectDef` 自身的 base / repeat 两件**保持现有形状**（`Amount` + `Source` + `Repeat` +
`RepeatSource`），只是多了上面那两对费用阈值——这样工程 JSON 的既有部分不变，
而升级 / 等级表那两侧从此是"四份分组"而不是"每加一个数值补 6 行"。

**XML 形状不变**（仍按用户定的 `base` / `repeat` / `energyAmount` / `starAmount` 四块）。

## 谓词（`CardPickFilter`）

签名从"只收 pick"改成"收效果"（运算符在 pick、阈值在效果，两者都要）：

```csharp
public static string? PredicateOn(string variable, EffectDef effect, ThresholdResolver thresholds);
```

`ThresholdResolver` 是"把阈值三件套解成 C# 表达式"的回调（由 `EffectEmitter` 交给变量来源模块实现，
见下），于是**选牌模块仍然不认识"手牌数"这类来源**——依赖方向不变。

生成规则：

```
维度内部（同一列表内多个取值）：或
  类型     c.Type == CardType.Attack || c.Type == CardType.Skill
  稀有度   c.Rarity == CardRarity.Rare
  标签     c.Tags.Contains(CardTag.Strike) || …
  关键词   c.Keywords.Contains(CardKeyword.Exhaust) || …
  牌 ID    (c is StrikeIronclad || c is DefendIronclad)      ← 沿用现有的类型判定

费用（每维一条，运算符六档；阈值为 V）：
  AtLeast(>=)     c.EnergyCost.Canonical >= V
  AtMost(<=)      c.EnergyCost.Canonical <= V
  GreaterThan(>)  c.EnergyCost.Canonical >  V
  LessThan(<)     c.EnergyCost.Canonical <  V
  NotEqual(!=)    c.EnergyCost.Canonical != V
  IsX             c.EnergyCost.CostsX
  星辰把 c.EnergyCost.Canonical 换成 c.CanonicalStarCost、CostsX 换成 HasStarCostX

维度之间：与
  最终 = 各"或组"用 && 串起来（每组一个括号）
```

**阈值接变量系统**：固定值直接写字面量（`> 2`）；算出来的先按变量模块算一遍存进局部变量
（`pickEnergy0`、`pickStar1`…，命名与张数同一套序号规则），再 `> pickEnergy0`。

`CardPickEmitter.Lines(...)` 现在收 `CardPick pick` + 特例参数 `effectValue`；实现时改成收
**`EffectDef effect`**，张数与两个阈值都从它取（张数那侧仍按 `CountFromEffect` 判断读哪一个）。

**入口选择**（不变）：无筛选 + 单个战斗牌堆 → `FromCombatPile`；
有筛选 / 多牌堆 / 全战斗牌堆 → `FromSimpleGrid`；卡组 → `FromDeck*` 一族。

## 界面（`ui/app.js` 的 `pickForm`）

流程一那一列拆成三块：

1. **选择范围**（单选）：此牌 / 牌堆 / 卡组——只列策略允许的档。
2. **选择牌堆**（多值输入）。
3. **筛选维度**：一个多值输入框——它的值就是"启用的维度"：
   `类型 / 稀有度 / 标签 / 关键词 / 牌 ID / 能量消耗 / 星辰消耗`（候选只列策略允许的）。
   每启用一个，下面出现它自己那一行：

| 启用的维度 | 那一行 |
|---|---|
| 类型 / 稀有度 / 标签 / 关键词 / 牌 ID | 一个多值输入框（候选见下） |
| 能量消耗 / 星辰消耗 | **运算符单选**（不筛选 / 不小于 / 不大于 / 大于 / 小于 / 不等于 / X）+（非 X 时）阈值三件套 |

### 多值输入框一律走现成的组件（用户点名）

所有多值格（含新增的"筛选维度"与各维度取值）都用 `ui/components.js` 的
`MFComponents.multiCombo(...)` 渲染、用 `MFComponents.bindMultiCombo(...)` 接事件——
**不手写第二个多值输入**。接线方式照现有做法（`renderEffectRows` 里那一段）：

```js
// 渲染：field 决定 data-multi-field，items 是 {value,label} 列表
MFComponents.multiCombo({ field: 'pick.dimensions', values: enabledDims, items: dimItems(t), placeholder: t('multiPlaceholder') })

// 绑定：renderEffectRows 里统一接（新增字段只需在两处登记，见下）
row.querySelectorAll('[data-multi]').forEach((box) => {
  MFComponents.bindMultiCombo(box, {
    items: multiComboItemsFor(box.dataset.multiField),
    onChange: (values) => { writeMultiComboValue(eff, box.dataset.multiField, values); refreshDerived(); },
  });
});
```

要登记的只有两处（现有机制，不是新写）：

- `multiComboItemsFor(field)`（`ui/app.js:1630`）：**新增一条分支**——
  `field === 'pick.dimensions'` 返回维度清单（`{value:'CardType', label:t('pickOption_CardType')}`…）；
  现有的 `pick.piles`（`pileKindItems()`）与 `pick.*`（按字段名反查维度：
  `types→CardType`、`rarities→Rarity`、`tags→Tag`、`keywords→Keyword`、`cardIds→CardId`）照用；
- `writeMultiComboValue(eff, field, values)`（`ui/app.js:1649`）：`pick.` 前缀写进 `eff.pick[...]`，
  已有机制**一行不用改**。

组件契约（实现时别绕过）：`onChange` 给的是**模型值数组**（`['Attack']`），
显示文本是 label（"攻击"），失焦规范化、认不出的值在下面那行点出来；
`multiCombo` 渲染时必须再传 `items`（否则框里显示枚举名——组件内部把 value 翻成 label）。

### 启用状态的推导与"移除要清值"

- **取值维度**（类型 / 稀有度 / 标签 / 关键词 / 牌 ID）：启用 = 该列表非空；
  刚启用还没填值的先记在界面自己的会话表（`WeakMap`，按效果对象键，**不进工程 JSON**），
  刷新后"启用但空"的维度不保留。
- **费用维度**：启用 = 运算符 ≠ `None`（这样就**不需要**会话表——
  运算符本身可以存）。启用时默认给 **大于**（用户举的例子就是"能量消耗大于 2"），
  移除时把运算符设回 `None` **并清掉阈值**（阈值留着就是一条看不见的筛选）。
- **选 X**：阈值三件套整组隐去（"填了不生效"的格一律不显示）。
- 界面上给一行说明写清口径（按卡面费用、X 费与 X 星辰的差别）。

## 描述（`Modules/Descriptions/`）

定语链顺序固定（中文与英文各写各的模板，语序不硬拼）：稀有度 → 类型 → 标签 → 关键词 → 牌 ID；
多个取值之间用 `orJoin`（"或" / " or "）；牌 ID 写成卡名；中心词 `牌` / `cards`。
费用条件作为相对从句追加，运算符六档各一条词条：

| 运算符 | 中文 | 英文 |
|---|---|---|
| 不小于 | 能量消耗不小于 {n} 的 | with energy cost at least {n} |
| 不大于 | 能量消耗不大于 {n} 的 | with energy cost at most {n} |
| 大于 | 能量消耗大于 {n} 的 | with energy cost above {n} |
| 小于 | 能量消耗小于 {n} 的 | with energy cost below {n} |
| 不等于 | 能量消耗不等于 {n} 的 | with energy cost not equal to {n} |
| X | 能量消耗为 X 的 | with X energy cost |

（星辰同理，替换"能量"。多条费用条件用"且" / " and "连起来。）

示例（进 `phrasetest` 对照组）：

| 配置 | 中文 | 英文 |
|---|---|---|
| 稀有 + 攻击 + 能量>2 + 星辰>0 | 消耗 能量消耗大于 2 且星辰消耗大于 0 的稀有攻击牌。 | Exhaust Rare Attack cards with energy cost above 2 and star cost above 0. |
| 稀有 + 攻击（无费用） | 消耗 稀有攻击牌。 | Exhaust Rare Attack cards. |
| 能量不小于手牌数（算出来的） | 消耗 能量消耗不小于 {EnergyThreshold:diff()} 的牌。 | Exhaust cards with energy cost at least {EnergyThreshold:diff()}. |
| 星辰为 X | 消耗 星辰消耗为 X 的牌。 | Exhaust cards with X star cost. |

阈值是**算出来的**时候有变量名（它占变量槽），所以直接写占位符，不需要"念来源"那套。

> **落地后补的（2026-09-23）：句子分两套句式。** 上表那几行都落在"紧凑"那一档（每个取值维度
> 恰好一个值、且这类维度 ≤ 2 个），所以措辞一字未改。同一维有多个取值、或三个取值维度以上时
> 改用**显式从句**（每个维度点明标签词，从句之间「且」、同维多值「、」，费用从句挪到最后）：
>
> | 情况 | 中文 |
> |---|---|
> | 紧凑（上表那几档） | `能量消耗大于 2 的稀有攻击牌` |
> | 显式（同维多值 / ≥3 维） | `类型为攻击、技能、能力的且带有保留、消耗的且名字中带有打击、防御的且能量消耗不大于 2 的牌` |
>
> 英文对称（紧凑 `Rare Attack cards with …`；显式 `cards with type …, that have …`）。
> 判据在 `PickerWording.UseShortForm`（一处，`kindtest` 把边界钉死），细节见 README 的
> 「一套选择器：十步流程」一节。**紧凑那一侧与旧输出逐字节相同**（每维一个值时连接符都用不上）。

## XML（`Modules/Files/EffectXml.cs`）

运算符在 `<pick>` 里，阈值在效果级（与 `base` / `repeat` 并列）——照职责划分：

```xml
<li>
  <kind>ExhaustCards</kind>
  <target>Self</target>
  <base>…</base>
  <repeat>…</repeat>
  <energyAmount>                  <!-- 效果级：阈值三件套（与 base / repeat 同形） -->
    <count>2</count>
    <source>HandCount</source>    <!-- 可选；写了就是算出来的 -->
    <coefficient>0.5</coefficient>
  </energyAmount>
  <starAmount>
    <count>0</count>
  </starAmount>
  <upgrade>
    <li>                          <!-- 与 base / repeat 同构 -->
      <energyAmount><count>1</count></energyAmount>
      <starAmount><coefficient>0.5</coefficient></starAmount>
      <base>…</base>
    </li>
  </upgrade>
  <pick>
    <source>Piles</source>
    <mode>Choose</mode>
    <piles><li>Hand</li></piles>
    <types><li>Attack</li></types>
    <rarities><li>Rare</li></rarities>
    <energyComparison>GreaterThan</energyComparison>   <!-- 选牌条件：运算符六档 -->
    <starComparison>AtLeast</starComparison>
  </pick>
</li>
```

- `<dimension>`、`<needsStarCost>` **删除**（不做兼容：工程文件是测试材料）。
- `<energyComparison>` / `<starComparison>` 写 `None` 等于没写（与默认相同就不写）。
- 三条"填了不生效"的提示（都要报出来，不能静默）：
  - 有 `<energyAmount>`/`<starAmount>` 的阈值，却没有对应的 `<xxxComparison>`（或写 `None`）；
  - 运算符是 `IsX`，却写了非默认的阈值；
  - 这两个块出现在**不调用选牌控件**的效果上（判据 `CardPickPolicies.UsesPicker`，
    一处定义）。
- 读取仍走 `XmlIo` 那一套（**解析前拒绝 DOCTYPE / ENTITY、不启用外部实体**，
  认不出的元素记一条并忽略），新增元素名要登记进 `KnownElements` / `PickElements` 白名单。

## 校验（`ProjectValidator`）

- 费用阈值写了变量来源却没写来源的额外参数 → 沿用现有规则（`ReadSource` 那套）；
- 阈值来源是"状态层数"却没指定状态 → 已有 `ValidateValueSources` 统一查；
- 运算符 `IsX` + 非默认阈值 / 只写阈值不写运算符 → 与 XML 侧同两条提示（读写两侧同一份判据，
  由 `EffectXml` 与校验器共用一个小判定函数，避免两处各写一遍）。

## 限制（要写进 README 的「已知缺口」）

- **卡组的三个专用入口不吃额外谓词**（`FromDeckForUpgrade / Transformation / Enchantment`）。
  所以"卡组 + 费用条件 / 多维度"只有 `FromDeckGeneric` 能表达；升级 / 变化 / 附魔 + 这些条件
  要么退化成通用入口（丢专用预览），要么不支持——**建议先不支持**（界面上那三个效果的
  费用行隐去），等有需要再单独讨论。
- **单运算符**：一维一个运算符，所以"能量消耗大于 2 **或** 是 X 费"表达不了（要两条效果，或换口径）。
  日后需要可以给运算符加"或 X"的变体。

## 测试计划

| 工具 | 要钉住什么 |
|---|---|
| `picktest` | 维度并存的谓词形状（`&&` 串"或组"）；运算符六档 × 能量/星辰的谓词；阈值两种（字面量 / 变量）；**真程序集编译** |
| `kindtest` | 描述：定语链顺序、六档费用从句、多条件的"且"、算出来的阈值用占位符 |
| `phrasetest` | 对照表逐句断言（中英各一遍），`RequiredWordKeys` 补新键 |
| `filetest` | `<energyAmount>` / `<starAmount>` / `<energyComparison>` / `<starComparison>` 往返；三条提示各一条断言；`<dimension>` 与 `<needsStarCost>` 写成未知元素被报出来 |
| `gensnapshot` | 新夹具：多维度、费用固定、费用算出来的、X 费 / X 星辰、只写阈值不写运算符（要报提示） |
| 界面实跑 | 筛选维度多值框的启用/移除（移除要清值）、六档运算符、X 时阈值隐去、落盘 XML 与生成代码核对；**确认所有多值格都是 `MFComponents` 出来的**（DOM 上应带 `data-multi` 与组件生成的候选菜单） |

## 实现顺序

1. **`EffectUpgrade` / `EffectLevel` 按角色分组**（`ValuePart`），`Merge` / `IsSet` /
   等级表读写 / 校验跟着改——先做这步，后面加两个角色就是加两份分组。
2. 模型（`CardPick` 去 Dimension / NeedsStarCost、两个 `CostComparison`；
   `EffectDef` 加两对阈值 + `EffectState` / `NumericValue` 工厂）+ 谓词（含"与"的修复）
   + 发射器（阈值回调、`Lines` 改收 `EffectDef`）→ `picktest` 绿。
3. XML 读写（四块并列 + 运算符在 `<pick>` + 三条提示）+ 校验 → `filetest` 绿。
4. 变量槽（`EnergyThreshold` / `StarThreshold`）+ 描述（定语链与六档从句）→
   `kindtest` / `phrasetest` 绿。
5. 界面（范围 / 牌堆 / 筛选维度多值框 + 六档运算符，全部走 `MFComponents`）→ 浏览器实跑。
6. README 与记忆更新。

## 落地时补的两处（计划外，但"填了不生效"那类问题必须一次修净）

1. **阈值必须是变量，不能是字面量。** 计划里写的是"阈值接变量系统"，实现时把它做实了：
   筛了费用那一维，卡上就多一个变量（`EnergyAmount` / `StarAmount`，`VariableModule.ThresholdSlots`
   一处规划，描述那边读同一份），谓词读 `DynamicVars["EnergyAmount"].BaseValue`。
   只写字面量的话，`<upgrade>` 的阈值增量与等级表都会变成"填了不生效"——那正是这一轮要
   根除的那类缺陷（[[timing-module]] 记着这个坑）。`vartest` 把四个出口的文本钉住了。
2. ~~**改不到的项如实报出来。** 阈值只支持改**基础值**……~~
   **已改（同日后续）：系数与来源也做成生效的。** 阈值的**变量系数**同样占一个卡上变量
   （`EnergyAmountExtra`），来源也能逐级换（生成器按等级分支，见 README）。于是这一条
   "改不到"的限制消失了，三条校验器警告一并撤掉，界面上那几格也画回来了。
   `CostRoles.All` 是"哪些角色是费用角色"的唯一名单，变量模块、发射器、校验器、界面都读它。
3. **`VarRole` 与 `ValueRole` 合并成一个枚举。** 两个枚举本来就是一一对应的
   （Value / Repeat / Energy / Star），留着就是每次都要写一遍映射；合并后 `VarSlot` 直接用
   模型侧的角色，少一层翻译。
4. **阈值来源的取值发成方法**（`CalcPickEnergy0` 之类），不是就地铺语句：同一张卡上可能有多条
   效果在筛费用，就地铺开会让它们的 `var player` 撞名而**编译不过**（`picktest` 的编译网实测
   抓到过 CS0136）。这也是"两个计算型阈值"那种卡此前**本来就编不过**的原因。

