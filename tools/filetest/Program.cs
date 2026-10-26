using System.Text.RegularExpressions;
using System.Xml.Linq;
using ModForge.Core;
using ModForge.Core.Model;
using ModForge.Core.Modules.CardPicking;
using ModForge.Core.Modules.Variables;
using ModForge.Core.Modules.Files;

// 文件模块的验证：工程文件夹的读写是不是"一个字段都不丢"。
//
// 这个模块最容易出的错不是崩，而是**静默丢字段**：某个字段忘了写出去，保存一次没事、
// 打开之后就没了，而且界面上看起来一切正常。所以这里的核心断言是：
//
//   1. 全字段往返：造一份**每个字段都不是默认值**的工程 → 保存 → 打开 → 两份的 JSON 逐字节相同
//      （ToJson 会把所有字段都写出来，所以字符串相同就等于字段全对上了）
//   2. 目录形状：正好八个文件夹；Create 下一实体一文件；一种语言一个文件夹
//   3. 语言文件：元素名就是游戏的翻译键；含 & < > 换行 的文本原样往返
//   4. 删掉一行 = 恢复默认值（"与默认相同的项不写"这条规则的逆向）
//   5. 工程里不再存在的实体，它的 XML 会被删掉（否则下次打开又"复活"）
//   6. **不信任 XML**：DOCTYPE / ENTITY 一律拒绝，外部实体不去解析
//   7. 枚举名写错当场报错，而不是悄悄退回默认值
//
// 用法: filetest [工作目录]

string sandbox = Sandbox(args.Length > 0 ? args[0] : null);

if (Directory.Exists(sandbox))
{
    Directory.Delete(sandbox, recursive: true);
}

Directory.CreateDirectory(sandbox);

var failures = new List<string>();
var log = new List<string>();

Console.WriteLine($"sandbox : {sandbox}");
Console.WriteLine();

/* ================================================================== 1. 全字段往返 */

Console.WriteLine("== 1. 全字段往返（每个字段都设成非默认值）==");

string projectRoot = Path.Combine(sandbox, "KitchenSink");
string library = Path.Combine(sandbox, "Library");
Directory.CreateDirectory(library);

CreateTinyPng(Path.Combine(library, "cover.png"));

var workspace = new Workspace(projectRoot, library, sandbox);
ModProject project = KitchenSink();

// 保存前的 JSON 要留一份：Save 会改素材引用（把库里的图收进工程），那份改动本身是对的。
SaveReport report = workspace.Save(project, sources: null, log: log.Add);
Console.WriteLine($"  写出 {report.SourcesWritten} 个文件，收进 {report.Imported} 个素材");

ProjectLayout layout = workspace.Layout;
ModProject loaded = ProjectStore.Load(layout, log.Add).Project;

// 两个缓存字段（可用动作、包内路由）**本来就不进工程**：它们是从源皮肤包里读出来的缓存，
// 下次在界面上重新读一次就有，存进工程只会让文件里多几百行与人无关的路由。
// 所以比对之前先把它们清掉——这是明确的取舍，不是漏了。
foreach (SkinDef skin in project.Skins)
{
    skin.AvailableAnimations.Clear();
    skin.Routes.Clear();
}

string beforeJson = project.ToJson();

// 比的是**规范化之后的 JSON**：对象里的键按名字排序、数组保持原序。
// 这正是数据的语义——字典（皮肤槽位绑定之类）本身没有顺序，而列表（效果顺序、等级表）有。
string beforeCanon = Canonical(beforeJson);
string afterCanon = Canonical(loaded.ToJson());

Check(afterCanon == beforeCanon, "保存再打开之后，全部字段与保存前完全一致");

if (afterCanon != beforeCanon)
{
    // 不一致时把第一处差异打出来，省得靠猜。
    int at = 0;

    while (at < beforeCanon.Length && at < afterCanon.Length && beforeCanon[at] == afterCanon[at])
    {
        at++;
    }

    Console.WriteLine($"  第一处差异在第 {at} 个字符附近：");
    Console.WriteLine($"    保存前: …{Snippet(beforeCanon, at)}…");
    Console.WriteLine($"    打开后: …{Snippet(afterCanon, at)}…");
}

/* ================================================================== 2. 目录形状 */

Console.WriteLine();
Console.WriteLine("== 2. 目录形状 ==");

string[] folders = { "About", "Language", "Textures", "Skins", "Cards", "Source", "Mod", "Create" };

foreach (string folder in folders)
{
    Check(Directory.Exists(Path.Combine(projectRoot, folder)), $"文件夹在：{folder}/");
}

Check(File.Exists(layout.AboutFile), "About/About.xml 在");

Check(Directory.EnumerateFiles(layout.CreateCardsDirectory, "*.xml").Count() == project.Cards.Count,
    $"Create/Cards 一卡一文件（{project.Cards.Count} 张）");
Check(Directory.EnumerateFiles(layout.CreateEffectsDirectory, "*.xml").Count() == project.SharedEffects.Count,
    $"Create/Effects 一效果一文件（{project.SharedEffects.Count} 个）");
Check(Directory.EnumerateFiles(layout.CreatePowersDirectory, "*.xml").Count() == project.Powers.Count,
    $"Create/Powers 一状态一文件（{project.Powers.Count} 个）");
Check(Directory.EnumerateFiles(layout.CreateSkinsDirectory, "*.xml").Count() == project.Skins.Count,
    $"Create/Skins 一皮肤一文件（{project.Skins.Count} 个）");

// About.xml：空的项不写空壳，封面没选就不写 <preview>（否则看的人以为该有那张图）。
{
    string about = File.ReadAllText(layout.AboutFile);

    Check(!Regex.IsMatch(about, @"<(\w+)></\1>"),
        "About.xml 里没有 <author></author> 这样的空壳（空的项直接不写）");
    // 封面会被收进 About/ 并统一改名成 Preview.<原扩展名>（用户要的就是"About 下放一张
    // Preview.png"），About.xml 里记的是改名之后的名字。
    Check(about.Contains("<preview>Preview.png</preview>", StringComparison.Ordinal),
        "About.xml 里写了封面文件名（选了封面，收进来时统一叫 Preview.*）");
    Check(File.Exists(Path.Combine(layout.AboutDirectory, "Preview.png")),
        "封面文件本身也在 About/ 里");

    // 一份全新的空工程：那些没填的项一个都不该出现。
    string bare = Path.Combine(sandbox, "Bare");
    var bareWorkspace = new Workspace(bare, library, sandbox);
    var empty = new ModProject { Id = "Bare", Name = "Bare" };
    bareWorkspace.Save(empty, sources: null, log: log.Add);

    string bareAbout = File.ReadAllText(bareWorkspace.Layout.AboutFile);
    Console.WriteLine("  空工程的 About.xml：");
    foreach (string line in bareAbout.Split('\n'))
    {
        Console.WriteLine("    " + line);
    }

    Check(!bareAbout.Contains("<author", StringComparison.Ordinal) &&
          !bareAbout.Contains("<description", StringComparison.Ordinal) &&
          !bareAbout.Contains("<preview", StringComparison.Ordinal),
        "空工程不写 <author> / <description> / <preview>（没内容就不写那一行）");
    Check(File.Exists(bareWorkspace.Layout.AboutFile) &&
          bareWorkspace.Layout.LayoutRootExists(),
        "只靠 About.xml 就能认出这是一份工程");

    // 手工放进 About/ 的封面也要认（否则下一次保存会把它当"没人引用的素材"删掉）。
    CreateTinyPng(Path.Combine(bareWorkspace.Layout.AboutDirectory, "Preview.png"));
    ModProject reopenedBare = ProjectStore.Load(bareWorkspace.Layout, log.Add).Project;

    Check(reopenedBare.PreviewFile == "Preview.png",
        "手工放进 About/ 的封面被认出来（不会在下次保存时被清掉）");

    bareWorkspace.Save(reopenedBare, sources: null, log: log.Add);

    Check(File.Exists(Path.Combine(bareWorkspace.Layout.AboutDirectory, "Preview.png")),
        "再保存一次，那张封面还在");
}

Check(File.Exists(layout.CardFile("ALPHA_CARD")), "卡文件名就是卡 id（ALPHA_CARD.xml）");
Check(File.Exists(layout.EffectFile("SHARED_BURN")), "共享效果文件名就是它的 id");
Check(File.Exists(layout.PowerFile("BETA_POWER")), "状态文件名就是它的 id");
Check(File.Exists(layout.SkinFile("regent")), "皮肤文件名是它换的那个角色");

// 语言：填过哪几种就只有哪几个文件夹，外加必定有的英语。
Check(Directory.Exists(Path.Combine(layout.LanguageDirectory, "eng")), "Language/eng/ 在");
Check(Directory.Exists(Path.Combine(layout.LanguageDirectory, "zhs")), "Language/zhs/ 在（填了中文）");
Check(Directory.Exists(Path.Combine(layout.LanguageDirectory, "jpn")), "Language/jpn/ 在（卡名填了日语）");
Check(!Directory.Exists(Path.Combine(layout.LanguageDirectory, "kor")),
    "Language/kor/ 不在（没填过韩语）");

/* ================================================================== 3. 语言文件 */

Console.WriteLine();
Console.WriteLine("== 3. 语言文件里的翻译键 ==");

string engXml = File.ReadAllText(layout.LocalizationFile("eng"));
string cardKey = LanguageXml.CardEntry(project, project.Cards[0]);
string powerKey = LanguageXml.PowerEntry(project, project.Powers[0]);

Check(engXml.Contains($"<{cardKey}.title>", StringComparison.Ordinal),
    $"翻译键就是游戏的键（{cardKey}.title）");
Check(engXml.Contains($"<{powerKey}.title>", StringComparison.Ordinal),
    $"状态的键在 powers 表里（{powerKey}.title）");

// 特殊字符必须原样往返：XML 转义与文本内容混在一起时最容易出错。
string tricky = loaded.Cards.Single(c => c.Id == "ALPHA_CARD").Description.GetOrFallback("eng");
Check(tricky.Contains("&", StringComparison.Ordinal) &&
      tricky.Contains("<", StringComparison.Ordinal) &&
      tricky.Contains(">", StringComparison.Ordinal),
    "描述里的 & < > 原样回来了");
Check(tricky.Contains('\n'), "描述里的换行原样回来了");

/* ================================================================== 4. 删一行 = 默认值 */

Console.WriteLine();
Console.WriteLine("== 4. 删掉一行就是恢复默认值 ==");

string cardFile = layout.CardFile("ALPHA_CARD");
string cardXml = File.ReadAllText(cardFile);
Check(cardXml.Contains("<rarity>Rare</rarity>", StringComparison.Ordinal), "卡文件里写着 <rarity>Rare</rarity>");

File.WriteAllText(cardFile, cardXml.Replace("<rarity>Rare</rarity>", "", StringComparison.Ordinal));

ModProject edited = ProjectStore.Load(layout, log.Add).Project;
Check(edited.Cards.Single(c => c.Id == "ALPHA_CARD").Rarity == CardRarity.Common,
    "把那一行删掉之后，稀有度回到默认值 Common");
Check(cardXml.Contains("<energyCost>", StringComparison.Ordinal),
    "身份那一组字段（费用等）总是写出来，不靠默认值隐式表达");

File.WriteAllText(cardFile, cardXml);

/* ================================================================== 5. 删实体 = 删文件 */

Console.WriteLine();
Console.WriteLine("== 5. 工程里没有了的实体，文件也要删掉 ==");

File.WriteAllText(Path.Combine(layout.CreateCardsDirectory, "GHOST.xml"),
    """
    <?xml version="1.0" encoding="utf-8"?>
    <card><id>GHOST</id></card>
    """);

Check(File.Exists(Path.Combine(layout.CreateCardsDirectory, "GHOST.xml")), "先放一个多出来的卡文件");

ModProject reloaded = ProjectStore.Load(layout, log.Add).Project;
Check(reloaded.Cards.Any(c => c.Id == "GHOST"), "打开时它被当成一张卡读进来了");

// 界面上把这张卡删掉，再保存——文件必须跟着走。
reloaded.Cards.RemoveAll(c => c.Id == "GHOST");
workspace.Save(reloaded, sources: null, log: log.Add);

Check(!File.Exists(Path.Combine(layout.CreateCardsDirectory, "GHOST.xml")),
    "删掉那张卡再保存，它的文件也删了（否则删掉的卡下次打开会复活）");

/* ================================================================== 6. 不信任 XML */

Console.WriteLine();
Console.WriteLine("== 6. 不信任 XML（DOCTYPE / ENTITY 一律拒绝）==");

Check(Rejects("""
    <?xml version="1.0"?>
    <!DOCTYPE card [<!ENTITY xxe SYSTEM "file:///c:/windows/win.ini">]>
    <card><id>&xxe;</id></card>
    """),
    "带外部实体的 XML 被拒绝（不会去读本地文件）");

Check(Rejects("""
    <?xml version="1.0"?>
    <!DOCTYPE card [<!ENTITY a "hi">]>
    <card><id>&a;</id></card>
    """),
    "带内部实体的 XML 也被拒绝（拒绝的是 DTD 本身，不是看它想干什么）");

Check(Rejects("""
    <!DOCTYPE lolz [
      <!ENTITY lol "lol">
      <!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
      <!ENTITY lol3 "&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;">
    ]>
    <card><id>&lol3;</id></card>
    """),
    "指数展开那类 XML 同样被拒绝");

Check(!Rejects("""<card><id>FINE</id></card>"""), "普通 XML 正常解析（拒绝的是 DTD，不是 XML）");

/* ================================================================== 7. 枚举写错要报 */

Console.WriteLine();
Console.WriteLine("== 7. 枚举名写错当场报错 ==");

string badCard = Path.Combine(layout.CreateCardsDirectory, "BAD.xml");
File.WriteAllText(badCard, """
    <card><id>BAD</id><type>NotACardType</type></card>
    """);

ReadLog badLog = new(badCard);
CardDef badCardDef = CardXml.Read(badCard, badLog)!;

string error = string.Join(" | ", badLog.Issues.Select(i => i.Describe()));

Console.WriteLine($"  读回来的卡：{badCardDef.Id}，类型 {badCardDef.Type}（兜底值）");
Console.WriteLine($"  记录的问题：{error}");

Check(badCardDef is not null, "写错一个值，卡**照样读了进来**（不让整份工程打不开）");
Check(error.Contains("NotACardType", StringComparison.Ordinal), "报错里指出了写错的那个值");
Check(error.Contains("BAD.xml", StringComparison.Ordinal), "报错里指出了是哪个文件");
Check(error.Contains("Attack", StringComparison.Ordinal), "报错里列出了合法值，照着改就行");
Check(error.Contains("type", StringComparison.Ordinal), "报错里指出了文件内的位置");

File.Delete(badCard);

/* ================================================================== 8. 识别与语言清理 */

Console.WriteLine();
Console.WriteLine("== 8. 工程识别与多余语言 ==");

Check(Workspace.LooksLikeProject(projectRoot), "有 About/About.xml 的目录被认作工程");
Check(!Workspace.LooksLikeProject(sandbox), "没有 About/About.xml 的目录不被认作工程");

string strayLanguage = Path.Combine(layout.LanguageDirectory, "deu");
Directory.CreateDirectory(strayLanguage);
File.WriteAllText(Path.Combine(strayLanguage, LanguageXml.FileName),
    """<LanguageData><cards><KX_ALPHA_CARD.title>Alte Karte</KX_ALPHA_CARD.title></cards></LanguageData>""");

ModProject withGerman = ProjectStore.Load(layout, log.Add).Project;
CardDef germanCard = withGerman.Cards.Single(c => c.Id == "ALPHA_CARD");

Check(germanCard.Name.Has("deu"), "手放进来的德语翻译会被读进来");

// 界面上把德语文本清掉（索引器赋空就是删除这一条），再保存——那一份语言文件必须消失，
// 否则下次打开又把它读回来，用户会觉得"翻译删不掉"。
germanCard.Name["deu"] = "";
workspace.Save(withGerman, sources: null, log: log.Add);

Check(!File.Exists(Path.Combine(strayLanguage, LanguageXml.FileName)),
    "工程里已经没有德语文本时，那份语言文件在保存时被删掉（否则删掉的翻译会复活）");

/* ================================================================== 8b. 空工程不许覆盖真工程 */

// 这个坑踩过两次：编辑器启动时是一份**空工程**（不自动载入上次的），而工作目录还指着
// 上次那个工程文件夹——那时候任何一次"先保存再做别的"都会把空工程写进去，
// 那边的定义文件会按"实体都删光了"被清掉（第二次删掉了用户的卡）。
//
// 闸门在 App 层（EditorServer.MayWriteWorkspace），判据是 Workspace.LooksLikeProject：
// 这一条把那个判据本身钉住——它得认得出"这个文件夹里已经有一份工程"。
Console.WriteLine();
Console.WriteLine("== 8b. 空工程不许覆盖真工程 ==");

{
    // 一份"别人的"工程：有 About.xml，有一张卡。
    string occupied = Path.Combine(sandbox, "Occupied");
    var occupiedLayout = new ProjectLayout(occupied);
    occupiedLayout.EnsureCreated();

    File.WriteAllText(occupiedLayout.AboutFile,
        """<ModMetaData><packageId>Occupied</packageId><name>Occupied</name></ModMetaData>""");
    File.WriteAllText(occupiedLayout.CardFile("KEEP_ME"),
        """<card><id>KEEP_ME</id><type>Attack</type></card>""");

    Check(Workspace.LooksLikeProject(occupied),
        "有 About.xml 的文件夹被认作\"已经有一份工程\"（App 层据此拒绝写入）");

    Check(!Workspace.LooksLikeProject(Path.Combine(sandbox, "EmptyFolder")),
        "空文件夹不算——往那儿写就是新建，不该被拦");

    // 反面：真把空工程存进去会怎样（这正是被拦下的那种操作）。
    var emptyProject = new ModProject { Id = "Empty", Name = "Empty" };
    new Workspace(occupied, library, sandbox).Save(emptyProject, sources: null, log: null);

    Check(!File.Exists(occupiedLayout.CardFile("KEEP_ME")),
        "（对照）不做保护时，空工程保存会把那张卡清掉——所以那道闸是必需的");

    File.WriteAllText(occupiedLayout.CardFile("KEEP_ME"),
        """<card><id>KEEP_ME</id><type>Attack</type></card>""");
}

/* ================================================================== 9. 效果的 XML 形状 */

Console.WriteLine();
Console.WriteLine("== 9. 效果的 XML 形状（三件套 + 升级的两条路）==");

// 固定值：只有基础值，没有来源与系数（系数是乘在变量上的，没变量就没它）。
string fixedXml = WriteEffect(new EffectDef
{
    Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 6,
});

Check(fixedXml.Contains("<count>6</count>", StringComparison.Ordinal), "固定值：写出基础值");
Check(!fixedXml.Contains("<source>", StringComparison.Ordinal) &&
      !fixedXml.Contains("<coefficient>", StringComparison.Ordinal),
    "固定值：不写来源与系数（那是计算型才有的两件）");

// 计算型：三件套齐全。
string calcXml = WriteEffect(new EffectDef
{
    Kind = EffectKind.DealDamage,
    Target = EffectTarget.Target,
    Amount = 6,
    Source = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 2m },
});

Check(calcXml.Contains("<source>HandCount</source>", StringComparison.Ordinal) &&
      calcXml.Contains("<coefficient>2</coefficient>", StringComparison.Ordinal),
    "计算型：来源与变量系数都写出来");

// 次数：固定次数只写一个数（默认 1 次时整块不出现）。
string repeatFixed = WriteEffect(new EffectDef { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 5 });
Check(!repeatFixed.Contains("<repeat>", StringComparison.Ordinal),
    "次数是默认的 1 次：整块不写（与默认相同的不写）");

string repeatCalc = WriteEffect(new EffectDef
{
    Kind = EffectKind.DealDamage,
    Target = EffectTarget.Target,
    Amount = 5,
    Repeat = 1,
    RepeatSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HpLostThisCombat, PerUnit = 0.5m },
});

Check(repeatCalc.Contains("<repeat>", StringComparison.Ordinal) &&
      repeatCalc.Contains("<count>1</count>", StringComparison.Ordinal) &&
      repeatCalc.Contains("<source>HpLostThisCombat</source>", StringComparison.Ordinal) &&
      repeatCalc.Contains("<coefficient>0.5</coefficient>", StringComparison.Ordinal),
    "次数是算出来的：次数三件套也写全");

// 升级两条路：有 <level> 的是等级表（绝对取值），没有的是增量（每级加多少）。
string upgradeXml = WriteEffect(new EffectDef
{
    Kind = EffectKind.DealDamage,
    Target = EffectTarget.Target,
    Amount = 6,
    Upgrade = new EffectUpgrade { AmountDelta = 3, RepeatDelta = 1, RepeatPerUnitDelta = 0.5m },
    Levels = new List<EffectLevel?> { null, new EffectLevel { Amount = 9 } },
});

Check(upgradeXml.Contains("<li>", StringComparison.Ordinal) &&
      upgradeXml.Contains("<level>2</level>", StringComparison.Ordinal),
    "等级表：每一项写明是第几级（缺的那一级不写，靠下标对齐）");
Check(Regex.Matches(upgradeXml, "<level>").Count == 1 &&
      upgradeXml.Contains("<count>3</count>", StringComparison.Ordinal),
    "等级表与增量同时存在：增量那一项没有 <level>，与等级表项分得开");

// 增量里的"次数变量系数"——填了必须原样回来（这正是之前被丢掉的那一项）。
EffectDef upgraded = ReadEffect(upgradeXml).Effect;
Check(upgraded.Upgrade.AmountDelta == 3 &&
      upgraded.Upgrade.RepeatDelta == 1 &&
      upgraded.Upgrade.RepeatPerUnitDelta == 0.5m,
    "增量：数值、次数、次数变量系数三项都读回来了");
Check(upgraded.Levels is { Count: 2 } && upgraded.Levels[0] is null && upgraded.Levels[1]?.Amount == 9,
    "等级表：中间缺的那一级仍然是\"沿用上一级\"（读回来保持位置）");

// 等级表里"缺项即沿用上一级"：只写系数，基础值不变。
string levelSparse = """
    <li>
      <kind>DealDamage</kind>
      <target>Target</target>
      <base><count>6</count><source>HandCount</source><coefficient>1</coefficient></base>
      <upgrade>
        <li><level>1</level><base><coefficient>2</coefficient></base></li>
      </upgrade>
    </li>
    """;

EffectDef sparse = ReadEffect(levelSparse).Effect;
Check(sparse.Levels is { Count: 1 } && sparse.Levels[0]?.PerUnit == 2m && sparse.Levels[0]?.Amount is null,
    "等级表：只写系数就是只改系数，基础值留空 = 沿用上一级");
Check(sparse.StateAt(1).Amount == 6 && sparse.StateAt(1).Source.PerUnit == 2m,
    "等级表：合成出来的那一级 = 新的系数 + 沿用下来的基础值");

// 等级表里显式写 <source>None</source>：这一级不要变量了（与"没写"是两件事）。
string levelClears = """
    <li>
      <kind>DealDamage</kind>
      <target>Target</target>
      <base><count>6</count><source>HandCount</source></base>
      <upgrade>
        <li><level>1</level><base><source>None</source></base></li>
      </upgrade>
    </li>
    """;

EffectDef cleared = ReadEffect(levelClears).Effect;
Check(cleared.Levels?[0]?.Source is { IsUsed: false },
    "等级表：<source>None</source> 明确表示这一级不要变量（不是\"没写\"）");

/* ---- 按条件数牌的多维筛选块 ---- */

// 写出来的形状：`<filter>` 是**块**（五个取值列表 + 两个运算符与阈值），不是单值元素。
{
    var counted = new EffectDef
    {
        Kind = EffectKind.DrawCards,
        Target = EffectTarget.Self,
        Amount = 0,
        Source = new ValueSource
        {
            Kind = ValueSourceKind.FilteredCardCount,
            CountScope = CountScope.Hand,
            Filter = new CardFilter
            {
                Types = { CardType.Attack },
                Rarities = { CardRarity.Rare },
                EnergyComparison = CostComparison.GreaterThan,
                EnergyThreshold = 2,
            },
        },
    };

    string xml = WriteEffect(counted);

    Check(xml.Contains("<filter>", StringComparison.Ordinal)
        && xml.Contains("<types>", StringComparison.Ordinal)
        && xml.Contains("<rarities>", StringComparison.Ordinal)
        && xml.Contains("<energyComparison>GreaterThan</energyComparison>", StringComparison.Ordinal)
        && xml.Contains("<energyThreshold>2</energyThreshold>", StringComparison.Ordinal),
        "多维筛选写成 <filter> 块（五个列表 + 运算符与阈值）");

    Check(xml.Contains("<scope>Hand</scope>", StringComparison.Ordinal),
        "「从哪个范围数」写在 <scope> 上（它属于来源，不属于筛选）");

    // 读回来逐维对得上（写与读走的是同一份形状）。
    (EffectDef back, ReadLog backLog) = ReadEffect(xml);
    CardFilter read = back.Source.Filter;

    Check(backLog.Issues.Count == 0
        && read.Types.SequenceEqual(new[] { CardType.Attack })
        && read.Rarities.SequenceEqual(new[] { CardRarity.Rare })
        && read.EnergyComparison == CostComparison.GreaterThan
        && read.EnergyThreshold == 2
        && back.Source.CountScope == CountScope.Hand,
        "多维筛选块往返之后每一维都还在");

    // 一个维度都没筛（没启用任何维度）时**不写**那一块：空块是噪音。
    var none = new EffectDef
    {
        Kind = EffectKind.DrawCards,
        Target = EffectTarget.Self,
        Amount = 0,
        Source = new ValueSource { Kind = ValueSourceKind.FilteredCardCount },
    };

    Check(!WriteEffect(none).Contains("<filter>", StringComparison.Ordinal),
        "一维都没筛时不写 <filter>（空块是噪音）");
}

// 旧的单值写法（<filter>打击牌</filter>）现在读不进来，要**明确报错**而不是静默当"不筛"。
Check(ReadError("""
    <li><kind>DrawCards</kind><target>Self</target>
      <base><count>1</count><source>FilteredCardCount</source><filter>TagStrike</filter></base>
    </li>
    """).Contains("多维筛选块", StringComparison.Ordinal),
    "旧的单值 <filter> 写法报错（静默读成「不筛」会让数值悄悄变掉）");

// 移除状态的两个选项（整层/减层、按 id/正面/负面）：**双方都与默认不同**时才写出，
// 而且是往返里最容易漏的一对（它们只在"移除状态"这一条效果上有意义）。
{
    var removal = new EffectDef
    {
        Kind = EffectKind.RemovePower,
        Target = EffectTarget.Target,
        PowerId = "POWER.STRENGTH_POWER",

        // 与模型默认相反的两档：默认是"整个移除 + 按 id 指定"。
        Removal = PowerRemoval.Partial,
        Selection = PowerSelection.Debuff,
        Amount = 2,
    };

    string xml = WriteEffect(removal);

    Check(xml.Contains("<removal>Partial</removal>", StringComparison.Ordinal),
        "写了 <removal>：部分移除要存下来（不写就会被读成「整个移除」，行为悄悄变掉）");
    Check(xml.Contains("<selection>Debuff</selection>", StringComparison.Ordinal),
        "写了 <selection>：按类别移除要存下来");

    (EffectDef back, ReadLog backLog) = ReadEffect(xml);

    Check(backLog.Issues.Count == 0
        && back.Removal == PowerRemoval.Partial
        && back.Selection == PowerSelection.Debuff
        && back.PowerId == "POWER.STRENGTH_POWER",
        "两个选项往返之后还在（读回来还是「部分移除 + 负面效果」）");

    // 默认值**不写**：那是这条效果从前的行为，写出来只是噪音。
    string plain = WriteEffect(new EffectDef
    {
        Kind = EffectKind.RemovePower,
        Target = EffectTarget.Self,
        PowerId = "POWER.STRENGTH_POWER",
    });

    Check(!plain.Contains("<removal>", StringComparison.Ordinal)
        && !plain.Contains("<selection>", StringComparison.Ordinal),
        "默认档（整个移除 + 按 id）不写那两个元素——与旧文件的形状一致");
}

// 目标选择器新加的那几档（随机友方 / 所有人、随机一名玩家、任意友方 / 所有友方）以及
// "作用于其奥斯提"与"多人游戏卡牌"：枚举值与 XML 是**按名字**对应的，名字打错或漏了
// 就只是读回默认档——行为悄悄变掉，所以要往返一遍。
{
    foreach (EffectTarget target in new[]
             {
                 EffectTarget.RandomAlly, EffectTarget.All, EffectTarget.RandomPlayer,
                 EffectTarget.AllOtherAllies, EffectTarget.RandomOtherAlly,
             })
    {
        var effect = new EffectDef
        {
            Kind = EffectKind.DealDamage,
            Target = target,
            Amount = 4,
        };

        string xml = WriteEffect(effect);
        (EffectDef back, ReadLog rtLog) = ReadEffect(xml);

        Check(rtLog.Issues.Count == 0 && back.Target == target,
            $"效果目标 {target} 往返之后还在（写成 <target>{target}</target>）");
    }

    // "作用于其奥斯提"：与作用对象配套的一项，漏写就等于"打自己"——句子与代码都变意思。
    {
        // 玩家宠物那一组：它是**目标档本身**（不是"某一档 + 一个布尔"），所以只验档名往返。
        foreach (EffectTarget band in new[]
                 {
                     EffectTarget.SelfPet, EffectTarget.TargetPet,
                     EffectTarget.AllAlliesPet, EffectTarget.RandomAllyPet,
                     EffectTarget.AllOtherAlliesPet, EffectTarget.RandomOtherAllyPet,
                 })
        {
            var pet = new EffectDef { Kind = EffectKind.ApplyPower, Target = band, Amount = 1 };
            (EffectDef petBack, ReadLog petLog) = ReadEffect(WriteEffect(pet));

            Check(petLog.Issues.Count == 0 && petBack.Target == band,
                $"{band} 往返之后还在（造物轴的效果都该能选宠物档）");
        }

        // 其余"对奥斯提有效"的造物轴效果同样要能往返——界面给得出、XML 存得下。
        foreach (EffectKind kind in new[]
                 {
                     EffectKind.LoseHp, EffectKind.GainMaxHp, EffectKind.LoseMaxHp,
                     EffectKind.RemovePower, EffectKind.TriggerDoom,
                 })
        {
            var petOther = new EffectDef { Kind = kind, Target = EffectTarget.SelfPet, Amount = 1 };
            (EffectDef otherBack, ReadLog otherLog) = ReadEffect(WriteEffect(petOther));

            Check(otherLog.Issues.Count == 0 && otherBack.Target == EffectTarget.SelfPet,
                $"{kind} 的宠物档往返之后还在");
        }
    }

    // 卡牌那两档走"保存 → 打开"（卡牌 XML 的读写只有这一条路）。
    var probe = new ModProject
    {
        Id = "TargetProbe",
        Name = "Target Probe",
        Version = "1.0.0",
        Description = "Test.",
    };

    foreach (CardTargetType target in new[] { CardTargetType.AnyAlly, CardTargetType.AllAllies })
    {
        probe.Cards.Add(new CardDef
        {
            Id = target.ToString().ToUpperInvariant(),
            Name = new LocalizedText("eng", target.ToString()),
            Description = "Test.",
            Pool = "ironclad",
            Type = CardType.Skill,
            Rarity = CardRarity.Common,
            Target = target,
        });
    }

    // "多人游戏卡牌"：默认档（None）不写，另两张各占一档——游戏在卡池与商店那一侧按它过滤，
    // 读丢了等于这张牌静默地出现在不该出现的地方。
    probe.Cards.Add(new CardDef
    {
        Id = "MULTIPLAYER_ONLY",
        Name = new LocalizedText("eng", "Multiplayer Only"),
        Description = "Test.",
        Pool = "ironclad",
        Type = CardType.Skill,
        Rarity = CardRarity.Common,
        Target = CardTargetType.Self,
        Multiplayer = CardMultiplayerConstraint.MultiplayerOnly,
    });
    probe.Cards.Add(new CardDef
    {
        Id = "SINGLEPLAYER_ONLY",
        Name = new LocalizedText("eng", "Single Player Only"),
        Description = "Test.",
        Pool = "ironclad",
        Type = CardType.Skill,
        Rarity = CardRarity.Common,
        Target = CardTargetType.Self,
        Multiplayer = CardMultiplayerConstraint.SingleplayerOnly,
    });

    var targetWorkspace = new Workspace(Path.Combine(sandbox, "TargetProbe"), library, sandbox);
    targetWorkspace.Save(probe, sources: null, log: null);
    var loadedProbe = ModForge.Core.Modules.Files.ProjectStore.Load(targetWorkspace.Layout, null);

    Check(loadedProbe.Project.Cards.Count == 4
        && loadedProbe.Project.Cards.Single(c => c.Id == "ANYALLY").Target == CardTargetType.AnyAlly
        && loadedProbe.Project.Cards.Single(c => c.Id == "ALLALLIES").Target == CardTargetType.AllAllies,
        "卡牌目标「任意友方 / 所有友方」往返之后还在");
    Check(loadedProbe.Project.Cards.Single(c => c.Id == "MULTIPLAYER_ONLY").Multiplayer
            == CardMultiplayerConstraint.MultiplayerOnly
        && loadedProbe.Project.Cards.Single(c => c.Id == "SINGLEPLAYER_ONLY").Multiplayer
            == CardMultiplayerConstraint.SingleplayerOnly,
        "「多人游戏卡牌」两档往返之后还在");

    string defaultCard = Path.Combine(targetWorkspace.Layout.CreateCardsDirectory, "ANYALLY.xml");
    Check(!File.ReadAllText(defaultCard).Contains("<multiplayer>", StringComparison.Ordinal),
        "默认档（两种模式都有）不写 <multiplayer>——旧文件形状不变");
}

// 临时牌集合：写侧（<setId>）与读侧（<pick><set>）都要往返。两处都**漏了就静默变意思**：
// 写侧丢了 → 那条效果不存集合（后面的效果读到空）；读侧丢了 → 取的是别的来源（比如整个手牌）。
{
    var writer = new EffectDef
    {
        Kind = EffectKind.DrawCards,
        Target = EffectTarget.Self,
        Amount = 2,
        SetId = "Drawn",
    };

    string xml = WriteEffect(writer);
    Check(xml.Contains("<setId>Drawn</setId>", StringComparison.Ordinal),
        "写了 <setId>：存集合要落盘（不写就变成「不存」，后面的效果读到空集合）");

    (EffectDef back, ReadLog rtLog) = ReadEffect(xml);
    Check(rtLog.Issues.Count == 0 && back.SetId == "Drawn", "写侧往返之后还在");

    var reader = new EffectDef
    {
        Kind = EffectKind.ModifyCardCost,
        Target = EffectTarget.Self,
        Amount = -1,
        Pick = new CardPick { Source = PickSource.Set, SetId = "Drawn", Mode = PickMode.All },
    };

    string readerXml = WriteEffect(reader);
    Check(readerXml.Contains("<source>Set</source>", StringComparison.Ordinal)
        && readerXml.Contains("<set>Drawn</set>", StringComparison.Ordinal),
        "写了来源 Set 与 <set>：读集合要落盘");

    (EffectDef readerBack, ReadLog readerLog) = ReadEffect(readerXml);
    Check(readerLog.Issues.Count == 0
        && readerBack.Pick.Source == PickSource.Set
        && readerBack.Pick.SetId == "Drawn",
        "读侧往返之后还在（来源 + 集合 ID）");

    // 不填集合 ID 时**不写** <set>：与旧文件形状一致。
    Check(!WriteEffect(new EffectDef
        {
            Kind = EffectKind.ModifyCardCost, Target = EffectTarget.Self, Amount = -1,
            Pick = new CardPick { Source = PickSource.Piles, Piles = { PileKind.Hand } },
        }).Contains("<set>", StringComparison.Ordinal),
        "没填集合 ID 时不写 <set>（旧文件形状不变）");
}

// 加入指定卡牌的"从卡池 N 选一"（<addMode> / <candidates> / <poolFilter>）也要往返：
// 任何一项丢了都会静默变成另一种生成方式（指定牌 / 候选数 3 / 不筛）。
{
    var writer = new EffectDef
    {
        Kind = EffectKind.AddNamedCardToPile,
        Target = EffectTarget.Self,
        AddMode = AddCardMode.ChooseFromPool,
        Candidates = 2,
        PoolFilter = new CardFilter
        {
            Rarities = { CardRarity.Rare },
            Types = { CardType.Attack },
            EnergyComparison = CostComparison.AtMost,
            EnergyThreshold = 2,
        },
        TargetPile = PileKind.Hand,
    };

    string xml = WriteEffect(writer);
    Check(xml.Contains("<addMode>ChooseFromPool</addMode>", StringComparison.Ordinal)
        && xml.Contains("<candidates>2</candidates>", StringComparison.Ordinal)
        && xml.Contains("<poolFilter>", StringComparison.Ordinal),
        "写了 <addMode> / <candidates> / <poolFilter>：生成方式要落盘");

    (EffectDef back, ReadLog poolLog) = ReadEffect(xml);
    Check(poolLog.Issues.Count == 0
        && back.AddMode == AddCardMode.ChooseFromPool
        && back.Candidates == 2
        && back.PoolFilter.Rarities.SequenceEqual(new[] { CardRarity.Rare })
        && back.PoolFilter.Types.SequenceEqual(new[] { CardType.Attack })
        && back.PoolFilter.EnergyComparison == CostComparison.AtMost
        && back.PoolFilter.EnergyThreshold == 2,
        "三种新元素往返之后还在（模式 + 候选数 + 筛选五维）");

    // 指定牌那一路（默认）三样都不写：与旧文件形状一致。
    Check(!WriteEffect(new EffectDef
        {
            Kind = EffectKind.AddNamedCardToPile, Target = EffectTarget.Self,
            CardId = "CARD.STRIKE_IRONCLAD",
        }).Contains("<addMode>", StringComparison.Ordinal),
        "默认档（指定牌）不写 <addMode>/<candidates>/<poolFilter>（旧文件形状不变）");
}

// 启用条件：<condition> 的比较方式、两侧三件套、四个升级增量都要往返；
// 未启用时整个元素不写（旧文件形状不变）。
{
    var writer = new EffectDef
    {
        Kind = EffectKind.DrawCards,
        Target = EffectTarget.Self,
        Condition = new EffectCondition
        {
            Comparison = ConditionComparison.LessThan,
            LeftAmount = 0,
            LeftSource = new ValueSource { Actor = ValueSourceActor.Self, Kind = ValueSourceKind.HandCount, PerUnit = 2 },
            RightAmount = 4,
            LeftUpgrade = 1,
            RightPerUnitUpgrade = 0.5m,
        },
    };

    string xml = WriteEffect(writer);
    Check(xml.Contains("<condition>", StringComparison.Ordinal)
        && xml.Contains("<comparison>LessThan</comparison>", StringComparison.Ordinal)
        && xml.Contains("<leftUpgrade>1</leftUpgrade>", StringComparison.Ordinal)
        && xml.Contains("<rightPerUnitUpgrade>0.5</rightPerUnitUpgrade>", StringComparison.Ordinal),
        "写了 <condition>：比较方式、两侧与升级增量都要落盘");

    (EffectDef back, ReadLog condLog) = ReadEffect(xml);
    Check(condLog.Issues.Count == 0 && back.Condition is { } cond
        && cond.Comparison == ConditionComparison.LessThan
        && cond.LeftAmount == 0
        && cond.LeftSource.Kind == ValueSourceKind.HandCount
        && cond.LeftSource.PerUnit == 2m
        && cond.RightAmount == 4
        && cond.LeftUpgrade == 1
        && cond.RightPerUnitUpgrade == 0.5m,
        "条件往返之后还在（比较方式 + 两侧三件套 + 升级增量）");

    // 未启用条件：整个 <condition> 不写——没条件的工程文件一个字节都不变。
    Check(!WriteEffect(new EffectDef { Kind = EffectKind.DrawCards, Target = EffectTarget.Self })
        .Contains("<condition>", StringComparison.Ordinal),
        "未启用条件时不写 <condition>（旧文件形状不变）");
}

/* ---- 读不动就报错，不静默丢弃 ---- */

Check(ReadError("<li><kind>DealDamage</kind><target>Target</target><bass><count>6</count></bass></li>")
        .Contains("bass", StringComparison.Ordinal),
    "拼错的元素名会报错（而不是被无视，让人以为写对了）");

Check(ReadError("""
    <li><kind>DealDamage</kind><target>Target</target>
      <upgrade><li><base><count>3</count></base></li><li><base><count>4</count></base></li></upgrade>
    </li>
    """).Contains("只能有一份", StringComparison.Ordinal),
    "两个增量块会报错（谁是\"每级加多少\"不该靠猜）");

// 增量块里的 <source> 是**升级后换用的变量**（不是"每级加多少"），所以它是对的用法：
// 只填来源、不填任何增量数，也照样是"升级后改用另一个变量"。
{
    (EffectDef swap, ReadLog swapLog) = ReadEffect("""
        <li><kind>DealDamage</kind><target>Target</target>
          <base><count>6</count><source>HandCount</source><coefficient>2</coefficient></base>
          <upgrade><li><base><source>DiscardPileCount</source></base></li></upgrade>
        </li>
        """);

    Check(swap.Upgrade.Source?.Kind == ValueSourceKind.DiscardPileCount && swapLog.Issues.Count == 0,
        "增量块里的 <source> = 升级后换用的变量（只换变量、不加数值，也是合法的）");

    Check(swap.StateAt(1).Source.Kind == ValueSourceKind.DiscardPileCount,
        "合成到第 1 级时用的是换过之后的变量");
}

Check(ReadError("""
    <li><kind>DealDamage</kind><target>Target</target>
      <upgrade><li><level>0</level><base><count>9</count></base></li></upgrade>
    </li>
    """).Contains("<level>", StringComparison.Ordinal),
    "<level>0 会报错（等级从 1 起算）");

Check(ReadError("<li><kind>NotAnEffect</kind><target>Target</target></li>")
        .Contains("NotAnEffect", StringComparison.Ordinal),
    "写错的效果种类会报错，并列出合法值");

/* ---- NULL 与 None 不是一个概念 ---- */

// 顶层：<source>NULL</source> 就是"没有值"，与没写一样 → 固定值。
{
    (EffectDef effect, ReadLog nullLog) = ReadEffect(
        "<li><kind>DealDamage</kind><target>Target</target><base><count>6</count><source>NULL</source></base></li>");

    Check(!effect.Source.IsUsed && nullLog.Issues.Count == 0,
        "顶层 <source>NULL</source> = 没有值（固定值），而且不算错误");
}

// 等级表：NULL 是"这一级沿用上一级"，None 是"这一级不要变量了"。
{
    (EffectDef byNull, ReadLog nullLog2) = ReadEffect("""
        <li><kind>DealDamage</kind><target>Target</target>
          <base><count>6</count><source>HandCount</source></base>
          <upgrade><li><level>1</level><base><source>NULL</source></base></li></upgrade>
        </li>
        """);

    Check(byNull.Levels?[0]?.Source is null && nullLog2.Issues.Count == 0,
        "等级表里的 NULL = 沿用上一级（这一级不换变量），也不算错误");

    Check(byNull.StateAt(1).Source.Kind == ValueSourceKind.HandCount,
        "等级表里的 NULL：合成出来的那一级仍然用上一级的变量");

    (EffectDef byNone, _) = ReadEffect("""
        <li><kind>DealDamage</kind><target>Target</target>
          <base><count>6</count><source>HandCount</source></base>
          <upgrade><li><level>1</level><base><source>None</source></base></li></upgrade>
        </li>
        """);

    Check(byNone.Levels?[0]?.Source is { IsUsed: false },
        "等级表里的 None = 这一级明确不要变量（换回固定值）——与 NULL 不是一回事");
}

/* ---- 局部损坏不失兼容 ---- */

// 用户草案里那种"什么都写了一点、还有故意写错的"文件，必须能加载：能读的读进来，
// 读不动的退回兜底并逐条报出来。这一段就是照着那份草案的形状写的。
{
    string draft = """
        <li>
          <kind>GainEnergy</kind>
          <target>Self</target>
          <base>
            <count>6</count>
            <source>Energy</source>
            <coefficient>2</coefficient>
          </base>
          <repeat>
            <count>1</count>
            <source>Stars</source>
            <coefficient>0.5</coefficient>
          </repeat>
          <upgrade>
            <li>
              <level>1</level>
              <base><count>10</count><source>Energy</source><coefficient>2</coefficient></base>
              <repeat><count>1</count><source>Stars</source><coefficient>0.5</coefficient></repeat>
            </li>
            <li>
              <base><count>3</count><source>Stars</source><coefficient>2</coefficient></base>
              <repeat><count>0</count><source>NULL</source><coefficient>0.5</coefficient></repeat>
            </li>
          </upgrade>
          <error>
            <count>9</count>
          </error>
        </li>
        """;

    (EffectDef draftEffect, ReadLog draftLog) = ReadEffect(draft);

    Check(draftEffect.Kind == EffectKind.GainEnergy && draftEffect.Amount == 6 &&
          draftEffect.Source.Kind == ValueSourceKind.Energy && draftEffect.Source.PerUnit == 2m,
        "草案那种文件能加载：数值那三件套照读");

    Check(draftEffect.CalculatedRepeat && draftEffect.RepeatSource.Kind == ValueSourceKind.Stars,
        "草案那种文件能加载：次数那三件套照读");

    Check(draftEffect.Levels is { Count: 1 } && draftEffect.Levels[0]?.Amount == 10,
        "草案那种文件能加载：等级表照读（那个没有 <level> 的增量不进等级表）");

    Check(draftEffect.Upgrade.RepeatPerUnitDelta == 0.5m && draftEffect.Upgrade.RepeatDelta == 0,
        "草案那种文件能加载：增量照读（次数系数 0.5）");

    Check(draftLog.Issues.Any(i => i.Message.Contains("<error>", StringComparison.Ordinal)),
        "多出来的那个 <error> 被报了名字（位置也能找到）");

    Console.WriteLine($"  草案文件记录的问题：{string.Join(" | ", draftLog.Issues.Select(i => i.Describe()))}");
}

/* ================================================================== 7b. 张数归谁：<base> 还是 <pickCount> */

// "张数就是效果数值"的效果（消耗牌）：张数写在 <base> 里，<pick> 里**不写** <pickCount>；
// 反过来，文件里写了 <pickCount> 要**报出来**（不是静默忽略——那看着像生效了）。
Console.WriteLine();
Console.WriteLine("== 7b. 张数归 <base> 还是 <pickCount> ==");

{
    var exhaust = new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        Target = EffectTarget.Self,
        Amount = 2,
        Pick = CardPick.InPile(PileKind.Hand, PickMode.Choose),
    };

    string xml = WriteEffect(exhaust);

    Check(!xml.Contains("<pickCount>", StringComparison.Ordinal),
        "消耗牌的 <pick> 里不写 <pickCount>（张数就是 <base>）");
    Check(xml.Contains("<count>2</count>", StringComparison.Ordinal),
        "消耗牌的张数写在 <base> 的 <count> 里");

    if (xml.Contains("<pick>", StringComparison.Ordinal))
    {
        int from = xml.IndexOf("<pick>", StringComparison.Ordinal);
        int to = xml.IndexOf("</pick>", StringComparison.Ordinal);
        string pickSection = to > from ? xml[from..to] : xml[from..];

        Check(!pickSection.Contains("pickCount", StringComparison.Ordinal) &&
              pickSection.Contains("<mode>Choose</mode>", StringComparison.Ordinal),
            "消耗牌的 <pick> 只写“挑哪些牌”（来源 / 牌堆 / 模式），不写张数");
    }

    // 反过来：另一种效果的张数**就是**它自己那一格，所以那份要照写。
    var upgrade = new EffectDef
    {
        Kind = EffectKind.UpgradeCardsInPile,
        Target = EffectTarget.Self,
        Pick = new CardPick
        {
            Source = PickSource.Piles,
            Piles = { PileKind.Hand },
            Mode = PickMode.Choose,
            Count = new NumericValue(2, new ValueSource()),
        },
    };

    Check(WriteEffect(upgrade).Contains("<pickCount>", StringComparison.Ordinal),
        "升级这类效果的张数写在 <pickCount> 里（它的数值另有含义）");

    // **不挑牌**的效果整块 <pick> 都不该写。界面曾经给每条新效果都塞一份选牌配置
    // （`piles=[Hand]` 不是模型默认的空），于是"获得能量"这种效果也会把一段没有作用的
    // <pick> 写进文件，读文件的人会以为它在用。
    var plain = new EffectDef
    {
        Kind = EffectKind.GainEnergy,
        Target = EffectTarget.Self,
        Amount = 2,
    };

    Check(!WriteEffect(plain).Contains("<pick", StringComparison.Ordinal),
        "不挑牌的效果不写 <pick>（界面上那份多余的选牌配置不落盘）");

    // 反过来：真的会挑牌的效果照样写（否则这条断言就成了"什么都不写"的假证明）。
    var picker = new EffectDef
    {
        Kind = EffectKind.ExhaustCards,
        Target = EffectTarget.Self,
        Amount = 2,
        Pick = new CardPick
        {
            Source = PickSource.Piles,
            Piles = { PileKind.Hand },
            Mode = PickMode.Choose,
        },
    };

    Check(WriteEffect(picker).Contains("<pick>", StringComparison.Ordinal),
        "会挑牌的效果照旧写 <pick>（模型默认值之外的部分）");

    // 手写文件里给消耗牌写了 <pickCount>：要报出来，不能静默忽略。
    string handWritten = """
        <li>
          <kind>ExhaustCards</kind>
          <target>Self</target>
          <base><count>2</count></base>
          <pick>
            <source>Piles</source>
            <mode>Choose</mode>
            <piles><li>Hand</li></piles>
            <pickCount><count>3</count></pickCount>
          </pick>
        </li>
        """;

    (EffectDef read, ReadLog pickCountLog) = ReadEffect(handWritten);

    Check(pickCountLog.Issues.Any(i => i.Message.Contains("pickCount", StringComparison.Ordinal)),
        "消耗牌的文件里写了 <pickCount>：报一条错误（张数由 <base> 给）");
    Check(read.Amount == 2,
        "报错之后张数照 <base> 读（2 张），没有被那份 <pickCount> 搅乱");
}

/* ================================================================== 结果 */

Console.WriteLine();

if (failures.Count > 0)
{
    Console.WriteLine($"RESULT: FAILED ({failures.Count} 项)");
    foreach (string f in failures)
    {
        Console.WriteLine("  - " + f);
    }

    return 1;
}

Console.WriteLine("RESULT: OK");
Console.WriteLine("  保存/打开 = 全字段往返；目录形状、翻译键、默认值语义、XML 安全都成立。");
return 0;

void Check(bool condition, string what)
{
    Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");

    if (!condition)
    {
        failures.Add(what);
    }
}

/// <summary>这段 XML 是不是会被当场拒绝（DOCTYPE / ENTITY 只要出现就该拒）。</summary>
static bool Rejects(string xml)
{
    try
    {
        XmlIo.Parse(xml, "test.xml");
        return false;
    }
    catch (ProjectXmlException)
    {
        return true;
    }
}

/// <summary>
/// 把 JSON 规范化：对象里的键按名字排序，数组保持原序。
///
/// 这样"两份工程数据是否相同"就与"序列化时字典的遍历顺序"无关——字典本身没有顺序，
/// 而数组（效果的先后、等级表的第几级）有顺序，一动就该被发现。
/// </summary>
static string Canonical(string json)
{
    System.Text.Json.Nodes.JsonNode? node = System.Text.Json.Nodes.JsonNode.Parse(json);
    return node is null ? json : Sort(node).ToJsonString();
}

static System.Text.Json.Nodes.JsonNode Sort(System.Text.Json.Nodes.JsonNode node)
{
    if (node is System.Text.Json.Nodes.JsonObject obj)
    {
        var sorted = new System.Text.Json.Nodes.JsonObject();

        foreach (string key in obj.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal))
        {
            System.Text.Json.Nodes.JsonNode? value = obj[key];
            sorted[key] = value is null ? null : Sort(value);
        }

        return sorted;
    }

    if (node is System.Text.Json.Nodes.JsonArray array)
    {
        var sorted = new System.Text.Json.Nodes.JsonArray();

        foreach (System.Text.Json.Nodes.JsonNode? item in array)
        {
            sorted.Add(item is null ? null : Sort(item));
        }

        return sorted;
    }

    return System.Text.Json.Nodes.JsonNode.Parse(node.ToJsonString())!;
}

/// <summary>把一条效果写成 XML（外层是卡的 &lt;li&gt; 那一层）。</summary>
static string WriteEffect(EffectDef effect) => EffectXml.ToXml(effect).ToString();

/// <summary>读回一条效果，并把读取时记下的问题一并交出来。</summary>
static (EffectDef Effect, ReadLog Log) ReadEffect(string xml)
{
    ReadLog read = new("probe.xml");
    XElement root = XmlIo.Parse(xml, "probe.xml").Root!;
    return (EffectXml.FromXml(root, read), read);
}

/// <summary>
/// 读一段效果的 XML，返回记录下来的问题（读得动读不动都会返回）。
/// 空串表示"什么都没记"，也就是这一段读得干干净净。
/// </summary>
static string ReadError(string xml) =>
    string.Join(" | ", ReadEffect(xml).Log.Issues.Select(i => i.Describe()));

static string Snippet(string text, int at)
{
    int from = Math.Max(0, at - 40);
    return text.Substring(from, Math.Min(80, text.Length - from)).Replace("\n", "\\n");
}

/// <summary>
/// 一份"每个字段都不是默认值"的工程。
///
/// 这是这个工具的全部着力点：只要有一个字段忘了写出去，往返之后的 JSON 就对不上，
/// 而差异位置会直接印出来。挑的值都尽量躲开默认值（枚举挑第二种、布尔挑相反、数字挑非零），
/// 并且用上等级表、共享效果引用、多语言这些容易被漏掉的结构。
/// </summary>
static ModProject KitchenSink()
{
    var shareSource = new ValueSource
    {
        Actor = ValueSourceActor.AllAllies,
        Kind = ValueSourceKind.CardsDiscardedThisCombat,
        PerUnit = 0.5m,
        // 多维筛选的每一维都塞上值（含两个费用维度与它们的阈值）：往返里漏写任何一维
        // 都会让下面的 JSON 比对现形。
        Filter = new CardFilter
        {
            Types = { CardType.Power, CardType.Skill },
            Rarities = { CardRarity.Rare },
            CardIds = { "CARD.STRIKE_IRONCLAD", "CARD.DEFEND_IRONCLAD" },
            Tags = { CardTag.Strike, CardTag.Shiv },
            Keywords = { CardKeyword.Exhaust, CardKeyword.Innate },
            EnergyComparison = CostComparison.GreaterThan,
            EnergyThreshold = 3,
            StarComparison = CostComparison.NotEqual,
            StarThreshold = 2,
        },
        CountScope = CountScope.ExhaustPile,
        PowerId = "POWER.STRENGTH_POWER",
    };

    var effect1 = new EffectDef
    {
        Kind = EffectKind.BoostCardDamage,
        Trigger = EffectTrigger.CardPlayed,
        Target = EffectTarget.AllAllies,
        Amount = 7,
        Source = shareSource.Clone(),
        PowerId = "POWER.FORGE_MIGHT_POWER",
        
        TargetPile = PileKind.ExhaustPile,
        Placement = PilePlacement.Bottom,
        CardId = "CARD.STRIKE_IRONCLAD",
        Upgraded = true,
        Repeat = 3,
        CalculatedRepeat = true,
        RepeatBase = 2,
        RepeatSource = shareSource.Clone(),
        Orb = OrbKind.Plasma,
        // 与默认相反的那一档（默认最右），这样往返才真的覆盖了这个新字段。
        // 球种筛选用两个值，多值那条路（<orbKinds><li>…）也就被覆盖了。
        EvokeTarget = OrbEvokeTarget.All,
        OrbKinds = { OrbKind.Dark, OrbKind.Glass },
        EnchantmentId = "ENCHANTMENT.SHARP",
        CardFilter = CardFilterKind.TypeAttack,
        PileScope = PileScope.Hand,
        CostDuration = CostDuration.Permanent,
        // 选牌器（<pick>）：每一格都填成**非默认**，往返才对每一格都真的验到。
        // 默认是"从全部战斗牌堆里挑 1 张、不筛"，所以这里填多牌堆 + 七个筛选维度全开
        //（五个取值 + 两个费用运算符）+ 至多 + 三件套张数（张数带变量，于是
        // <source>/<coefficient> 也被覆盖）。
        Pick = new CardPick
        {
            Source = PickSource.Piles,
            Piles = { PileKind.DrawPile, PileKind.DiscardPile },
            Types = { CardType.Attack, CardType.Skill },
            Rarities = { CardRarity.Uncommon },
            CardIds = { "CARD.STRIKE_IRONCLAD", "CARD.DEFEND_SILENT" },
            Tags = { CardTag.Strike, CardTag.Shiv },
            Keywords = { CardKeyword.Exhaust, CardKeyword.Ethereal },
            EnergyComparison = CostComparison.GreaterThan,
            StarComparison = CostComparison.AtLeast,
            Mode = PickMode.AtMost,
            Count = new NumericValue(2, shareSource.Clone()),
        },

        // 两个费用阈值：基础值 + 变量来源（与 base / repeat 同构，往返也要覆盖）。
        EnergyAmount = 2,
        EnergySource = shareSource.Clone(),
        StarAmount = 1,
        StarSource = shareSource.Clone(),
        
        UseCounter = true,
        CounterTarget = 4,
        CounterReset = CounterReset.ThisCombat,
        TransformInto = TransformInto.SpecificCard,
        Upgrade = new EffectUpgrade
        {
            AmountDelta = 2,
            PerUnitDelta = 0.25m,
            RepeatDelta = 1,
            // 两个费用角色的增量也各来一份（四角色逐角色读写，往返要都覆盖）：
            // 基础值与变量系数各一个——两个数都是卡上的变量，两个增量都得能存能读。
            EnergyDelta = 1,
            EnergyPerUnitDelta = 0.5m,
            StarDelta = -1,
            StarPerUnitDelta = 0.25m,
        },
        Levels = new List<EffectLevel?>
        {
            // 中间这一项是 null：它把后面那项顶到第 2 级上，是"这一级沿用上一级"的占位。
            null,
            new EffectLevel
            {
                Amount = 9,
                PerUnit = 1.5m,
                Repeat = 4,
                Source = shareSource.Clone(),
                EnergyAmount = 3,
                // 等级表里的**阈值来源**（逐级换算式："这一级改按弃牌堆数"）往返也要覆盖。
                EnergySource = new ValueSource
                {
                    Actor = ValueSourceActor.Target,
                    Kind = ValueSourceKind.DiscardPileCount,
                },
                StarPerUnit = 0.5m,
            },
        },
    };

    var effect2 = new EffectDef
    {
        Kind = EffectKind.MoveCardsBetweenPiles,
        Trigger = EffectTrigger.EndOfTurn,
        Target = EffectTarget.RandomEnemy,
        Amount = -3,
    };

    // 空等级表：与"没有等级表"是一回事，保存时会被归一成 null（见 ModProject.Normalize）。
    var effect3 = new EffectDef { Kind = EffectKind.Heal, Amount = 5, Levels = new List<EffectLevel?>() };

    // 纯引用：只指向一个共享效果，自己什么参数都不设。
    var effect4 = new EffectDef { SharedEffectId = "SHARED_BURN" };

    var card = new CardDef
    {
        Id = "ALPHA_CARD",
        Name = new LocalizedText("eng", "Alpha Card").With("zhs", "阿尔法").With("jpn", "アルファ"),
        Description = new LocalizedText("eng", "Deal {Damage:diff()} damage & gain <block>.\nSecond line.")
            .With("zhs", "造成伤害，并获得格挡。"),
        Pool = "silent",
        Type = CardType.Skill,
        Rarity = CardRarity.Rare,
        Target = CardTargetType.AllEnemies,
        EnergyCost = 3,
        StarCost = 2,
        EnergyCostX = true,
        StarCostX = true,
        ReplayCount = 2,
        Keywords = new List<CardKeyword> { CardKeyword.Innate, CardKeyword.Retain, CardKeyword.Sly },
        UpgradeCostDelta = -1,
        UpgradeStarDelta = -1,
        MaxUpgradeLevel = 3,
        Effects = new List<EffectDef> { effect1, effect2, effect3, effect4 },
        ArtFile = "alpha.webp",
    };

    var power = new PowerDef
    {
        Id = "BETA_POWER",
        Name = new LocalizedText("eng", "Beta Power").With("zhs", "贝塔"),
        Description = new LocalizedText("eng", "Lose {Amount} health."),
        Kind = PowerKind.Debuff,
        Stack = PowerStack.Single,
        DamageModifier = DamageModifier.Multiplicative,
        CardFilter = CardFilterKind.TagShiv,
        FilterCardId = "CARD.STRIKE_IRONCLAD",
        Decay = DecayMode.OnePerTurnEnd,
        IconFile = "beta.png",
        Triggers = new List<TriggeredEffect>
        {
            new()
            {
                When = PowerTrigger.DamageTaken,
                Effect = new EffectDef { Kind = EffectKind.LoseHp, Amount = 3, Target = EffectTarget.Self },
            },
        },
    };

    var shared = new SharedEffectDef
    {
        Id = "SHARED_BURN",
        Name = "Shared Burn",
        Note = "给两张卡共用",
        Effect = new EffectDef { Kind = EffectKind.TriggerDoom, Amount = 1, Target = EffectTarget.AllEnemies },
    };

    var skin = new SkinDef
    {
        Target = "regent",
        Name = "测试皮肤",
        SourcePack = "skin.pck",
        SlotBindings = new Dictionary<string, string>
        {
            ["idle"] = "Idle",
            ["attack"] = "Attack_1",
        },
        AvailableAnimations = new List<string> { "Idle", "Attack_1" },
        Routes = new List<string> { "regent/idle.spatlas" },
    };

    return new ModProject
    {
        Id = "KitchenSink",
        Name = "全字段工程",
        Author = "测试",
        Description = "每个字段都设了非默认值。",
        Version = "2.3.4",
        MinGameVersion = "0.108.0",
        AffectsGameplay = false,
        TypePrefix = "Kx",
        SelfCheckOnStartup = true,
        PreviewFile = "cover.png",
        Cards = new List<CardDef> { card },
        Powers = new List<PowerDef> { power },
        SharedEffects = new List<SharedEffectDef> { shared },
        Skins = new List<SkinDef> { skin },
    };
}

/// <summary>
/// 取沙箱目录：**只允许系统临时目录下的路径**。
///
/// 这个工具开头就会递归删掉沙箱，所以这条必须是硬性拒绝而不是提醒。工具没法知道调用者
/// 想干什么，只能守住一条：我删掉的东西，必须明摆着是一份用完就丢的临时垃圾。
///
/// 2026-09-21：把游戏安装目录当参数传了进来，整份游戏被删空（靠 Steam 验证完整性才恢复）。
/// 那次事故之后加的这道闸。
/// </summary>
static string Sandbox(string? requested)
{
    string temp = Path.GetFullPath(Path.GetTempPath());
    string full = Path.GetFullPath(requested ?? Path.Combine(temp, "modforge-file-test"));

    if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"沙箱目录必须在系统临时目录下：{temp}");
        Console.Error.WriteLine($"  拒绝使用：{full}");
        Console.Error.WriteLine("  （这个工具会递归删掉沙箱目录，所以不接受任意路径）");
        Environment.Exit(2);
    }

    return full;
}

static void CreateTinyPng(string path)
{
    byte[] png =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D,
        0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82,
    };

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, png);
}
