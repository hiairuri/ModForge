using ModForge.Core;
using ModForge.Core.Export;
using ModForge.Core.Generate;
using ModForge.Core.Gfx;
using ModForge.Core.Model;
using ModForge.Core.Modules.Files;

// 验证"保存 / 打开 / 另存为 / 导出"这条路径。
//
// 用户的期望是：
//   保存 = 一份以工程名命名的独立文件夹，能被再次打开、能直接看
//   导出 = 可加载的 mod，落在工程自己的 Mod/ 里
// 所以这里把整条链路真的走一遍，检查每一环的产物是不是那么回事：
//
//   1. 保存：工程文件夹里正好是那八个文件夹（About/Language/Textures/Skins/Cards/
//      Source/Mod/Create），About.xml 与每张卡的 Create 文件都写出来了
//   2. 只带用到的图：没被任何卡牌引用的图不进工程；素材库本身不被删
//   3. 打开：换个目录读回来，内容与保存前一致，卡图仍然解析得到
//   4. 另存为：复制到新文件夹（以工程名命名），仍然是自足的
//   5. 导出：真的编译出 DLL + 清单 + PCK，且落在工程的 Mod/ 里
//
// 用法: workspacetest <gameDir> [工作目录]

string gameDir = args.Length > 0
    ? args[0]
    : @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2";

string sandbox = Sandbox(args.Length > 1 ? args[1] : null);

// 每次都从干净的地方开始，避免上一次的残留让结果看着像通过。
if (Directory.Exists(sandbox))
{
    Directory.Delete(sandbox, recursive: true);
}

Directory.CreateDirectory(sandbox);

string projects = Path.Combine(sandbox, "Projects");
string library = Path.Combine(sandbox, "Library");
string projectA = Path.Combine(projects, "ProjectA");

Directory.CreateDirectory(projects);
Directory.CreateDirectory(library);

Console.WriteLine($"sandbox : {sandbox}");
Console.WriteLine($"game    : {gameDir}");
Console.WriteLine();

var failures = new List<string>();

/* ---------- 1. 保存：用到的图收进工程，没用的不收 ---------- */

Console.WriteLine("== 1. 保存工程（八个文件夹 + 只带用到的贴图）==");

string usedArt = Path.Combine(library, "used_art.png");
string unusedArt = Path.Combine(library, "unused_art.png");
CreateTinyPng(usedArt);
CreateTinyPng(unusedArt);

var workspace = new Workspace(projectA, library, sandbox);   // 缓存放在 sandbox，不放工程里
workspace.EnsureCreated();

ModProject project = SampleProject.Create();
project.Id = "WsDemo";
project.Name = "工作区测试";
project.PreviewFile = CreateTinyPngAt(Path.Combine(library, "cover.png"));

// 第一张卡指向素材库里的一张图；其它卡不带立绘。
project.Cards[0].ArtFile = usedArt;

IReadOnlyDictionary<string, string> sources = new CodeGenerator(project).GenerateAll();
SaveReport report = workspace.Save(project, sources, log: Console.WriteLine);

Console.WriteLine($"  收进工程的素材：{report.Imported}，清掉的：{report.Removed}");

Check(report.Imported == 2, $"用到的 1 张卡图与封面被收进工程（实际 {report.Imported}）");
Check(File.Exists(workspace.Layout.AboutFile), "About/About.xml 已写出");
Check(Directory.Exists(workspace.Layout.SourceDirectory), "Source/ 已建立");

// 工程文件夹里**正好是那八个**：多一个都说明有东西放错了地方（缓存曾经就是这样跑进来的，
// 换个工程就要重新提取一次卡面素材，拷给别人也白带）。
string[] entries = Directory.GetFileSystemEntries(workspace.Root)
    .Select(Path.GetFileName)
    .OrderBy(x => x, StringComparer.Ordinal)
    .ToArray()!;

Console.WriteLine($"  工程文件夹内容：{string.Join(", ", entries)}");

string[] expected =
{
    "About", "Cards", "Create", "Language", "Mod", "Skins", "Source", "Textures",
};

Check(entries.SequenceEqual(expected),
    $"工程文件夹里正好是那八个：{string.Join(" / ", expected)}（实际 {entries.Length} 项）");

int sourceCount = Directory.EnumerateFiles(workspace.Layout.SourceDirectory, "*.cs", SearchOption.AllDirectories).Count();
Check(sourceCount > 0, $"Source/ 里有 {sourceCount} 个生成的源码文件");

// 定义是"一个实体一个文件"。
int cardFiles = Directory.EnumerateFiles(workspace.Layout.CreateCardsDirectory, "*.xml").Count();
Check(cardFiles == project.Cards.Count,
    $"Create/Cards 里一卡一个文件（{cardFiles} 个 / {project.Cards.Count} 张）");
Check(Directory.EnumerateFiles(workspace.Layout.CreatePowersDirectory, "*.xml").Any()
        == project.Powers.Any(),
    "Create/Powers 与工程里的状态一一对应");
Check(Directory.EnumerateFiles(workspace.Layout.CreateEffectsDirectory, "*.xml").Any()
        == project.SharedEffects.Any(),
    "Create/Effects 与工程里的共享效果一一对应");

// 语言：英语必定有一份（它是游戏的兜底语言），填过中文就该有中文那一份。
Check(File.Exists(workspace.Layout.LocalizationFile("eng")), "Language/eng/Localization.xml 已写出");
Check(File.Exists(workspace.Layout.LocalizationFile("zhs")) == AnyChineseText(project),
    "Language/zhs/Localization.xml 的有无与\"有没有填过中文\"一致");

// 翻译键就是游戏的键：元素名是 <条目>.title / <条目>.description。
string engText = File.ReadAllText(workspace.Layout.LocalizationFile("eng"));
string firstEntry = LanguageXml.CardEntry(project, project.Cards[0]);
Check(engText.Contains($"<{firstEntry}.title>", StringComparison.Ordinal),
    $"英语表用的是游戏的翻译键（{firstEntry}.title）");

// 散图必须被收进工程的 Cards/，并且引用改成相对路径——否则工程换台机器就丢图。
string artRef = project.Cards[0].ArtFile ?? "";
Check(!Path.IsPathRooted(artRef), $"卡图引用已改成相对路径（{artRef}）");
Check(workspace.ResolveCardArt(artRef) is not null, "相对引用能解析回真实文件");

// 预览取图必须也认工程自己的 Cards/。
//
// 卡面预览走的是后端 /api/art/local/，它曾经只查素材库——于是"从原版导入的图"（落在工程
// 里、素材库里没有）预览不出来，这正是用户报的"导入的卡面没有预览"。
// 这里把两条解析放在一起断言：只要 ResolveCardArt 认的，预览那条路径也必须认。
{
    string onlyInProject = "only_in_project.png";
    File.WriteAllBytes(Path.Combine(workspace.Layout.CardsDirectory, onlyInProject),
        new byte[] { 0x89, 0x50, 0x4E, 0x47 });

    Check(workspace.ResolveCardArt(onlyInProject) is not null,
        "只在工程里的图能被解析——预览取图也是走这条路");

    File.Delete(Path.Combine(workspace.Layout.CardsDirectory, onlyInProject));
}

Check(!File.Exists(Path.Combine(workspace.Layout.CardsDirectory, "unused_art.png")),
    "没被引用的图没有被收进工程");

// 关键：素材库是用户攒图的地方，保存不该清理它。
Check(File.Exists(unusedArt), "素材库里的图仍然在（保存只清理工程目录，不动素材库）");

/* ---------- 2. 保存时清掉工程里不再引用的图 ---------- */

Console.WriteLine();
Console.WriteLine("== 2. 清掉工程里不再引用的素材 ==");

// 再放一张图进工程的 Cards/，但不让任何卡牌引用它——模拟"换过立绘"留下的孤儿文件。
string orphan = Path.Combine(workspace.Layout.CardsDirectory, "orphan.png");
CreateTinyPng(orphan);

workspace.Save(project, sources, log: Console.WriteLine);

Check(!File.Exists(orphan), "刻意留下的孤儿贴图已被清理");
Check(File.Exists(Path.Combine(workspace.Layout.CardsDirectory, Path.GetFileName(artRef))),
    "仍然被引用的贴图没有被误删");

/* ---------- 3. 打开 ---------- */

Console.WriteLine();
Console.WriteLine("== 3. 打开工程 ==");

var reopened = new Workspace(projectA, library, sandbox);

Check(Workspace.LooksLikeProject(projectA), "ProjectA 被识别为工程文件夹");

ModProject loaded = ProjectStore.Load(reopened.Layout, Console.WriteLine).Project;

Check(loaded.Id == project.Id, $"工程 id 一致（{loaded.Id}）");
Check(loaded.Name == project.Name, $"工程名一致（{loaded.Name}）");
Check(loaded.PreviewFile == project.PreviewFile, $"封面文件名一致（{loaded.PreviewFile}）");
Check(loaded.Cards.Count == project.Cards.Count, $"卡牌数量一致（{loaded.Cards.Count}）");

// 按 id 找卡来比：工程里的卡是按文件名的顺序读回来的（一卡一个文件，文件名就是 id），
// 与内存里那份的排列顺序不一定相同——**顺序本身不是工程数据**，卡与卡之间没有先后关系。
CardDef before = project.Cards.Single(c => c.Id == project.Cards[0].Id);
CardDef after = loaded.Cards.Single(c => c.Id == before.Id);

Check(after.ArtFile == before.ArtFile, $"卡图引用一致（{after.ArtFile}）");
Check(after.Name.GetOrFallback("zhs") == before.Name.GetOrFallback("zhs"),
    $"卡牌名从语言文件里回填对了（{after.Name.GetOrFallback("zhs")}）");
Check(after.Description.GetOrFallback("eng") == before.Description.GetOrFallback("eng"),
    "卡牌描述从语言文件里回填对了");
Check(after.Effects.Count == before.Effects.Count, "卡上的效果条数一致");
Check(reopened.ResolveCardArt(after.ArtFile) is not null,
    "打开后卡图仍能解析（说明图跟着工程走）");
Check(reopened.ResolvePreview(loaded.PreviewFile) is not null, "打开后封面仍能解析");

/* ---------- 4. 另存为 ---------- */

Console.WriteLine();
Console.WriteLine("== 4. 另存为（以工程名命名的独立文件夹）==");

string folderName = Workspace.FolderNameFor(loaded);
Console.WriteLine($"  工程名 → 文件夹名：{folderName}");

string savedAs = reopened.SaveAs(projects, folderName, overwrite: false, Console.WriteLine);

Check(Directory.Exists(savedAs), $"另存目录已建立（{savedAs}）");
Check(Path.GetFileName(savedAs).StartsWith(folderName, StringComparison.Ordinal),
    "文件夹名以工程名开头");
Check(File.Exists(Path.Combine(savedAs, "About", "About.xml")), "另存目录里有 About/About.xml");
Check(Directory.EnumerateFiles(Path.Combine(savedAs, "Cards"), "*", SearchOption.AllDirectories).Any(),
    "另存目录里有卡图");
Check(Directory.EnumerateFiles(Path.Combine(savedAs, "Source"), "*.cs", SearchOption.AllDirectories).Any(),
    "另存目录里有源码");

var fromSaved = new Workspace(savedAs, library, sandbox);
ModProject savedData = ProjectStore.Load(fromSaved.Layout, Console.WriteLine).Project;

Check(fromSaved.ResolveCardArt(savedData.Cards.Single(c => c.Id == before.Id).ArtFile) is not null,
    "从另存目录打开时卡图仍能解析（存出来的工程是自足的）");

/* ---------- 5. 导出 ---------- */

Console.WriteLine();
Console.WriteLine("== 5. 导出到工程的 Mod/ ==");

// 卡图引用是相对路径，导出时要带上 Workspace 才能还原成磁盘路径，
// 与编辑器里 RunExport 的做法一致。少了它就会报"找不到卡图"。
//
// WebpConverter 也要给：游戏只认 WebP，PNG 得先转换。第二个参数是 Godot 脚本的路径。
var webp = WebpConverter.Discover(
    Environment.GetEnvironmentVariable("MODFORGE_GODOT") ?? @"D:\Tools",
    Path.Combine(FindGodotHelperDirectory(), "convert_to_webp.gd"));

Console.WriteLine($"  Godot 可用：{webp.IsAvailable}");

var exporter = new Exporter(gameDir) { WebpConverter = webp, Workspace = fromSaved };
ExportResult result = exporter.Export(savedData, fromSaved.ModDirectory);

foreach (string message in result.Messages)
{
    Console.WriteLine("  " + message);
}

Check(result.Success, "导出成功");
Check(!result.Messages.Any(m => m.Contains("Art file for", StringComparison.OrdinalIgnoreCase)),
    "导出时没有\"找不到素材文件\"的警告");

string modDir = result.OutputDirectory ?? Path.Combine(fromSaved.ModDirectory, savedData.Id);

Check(File.Exists(Path.Combine(modDir, savedData.Id + ".dll")), "导出了 DLL");
Check(File.Exists(Path.Combine(modDir, savedData.Id + ".json")), "导出了清单");
Check(File.Exists(Path.Combine(modDir, savedData.Id + ".pck")), "导出了 PCK");

// mod 文件夹就落在工程的 Mod/ 下面，没有再套一层。
Check(string.Equals(Path.GetDirectoryName(modDir), Path.GetFullPath(fromSaved.ModDirectory),
        StringComparison.OrdinalIgnoreCase),
    "mod 文件夹就落在工程的 Mod/ 里");

// 导出的 mod 与工程的 Cards/ 是两回事：一个是要给游戏加载的产物，一个是工程素材。
Check(!Directory.EnumerateFiles(modDir, "*.png", SearchOption.AllDirectories).Any(),
    "Mod/ 里没有散装 PNG（卡图打进 PCK 里了）");

/* ---------- 结果 ---------- */

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
Console.WriteLine("  保存 = 独立工程文件夹（八个文件夹，只带用到的素材，可再打开）；导出 = 可加载的 mod，落在工程 Mod/ 里。");
return 0;

void Check(bool condition, string what)
{
    Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");

    if (!condition)
    {
        failures.Add(what);
    }
}

/// <summary>
/// 取沙箱目录：**只允许系统临时目录下的路径**。
///
/// 这个工具开头就会递归删掉沙箱，所以这条必须是硬性拒绝而不是提醒。工具没法知道调用者
/// 想干什么，只能守住一条：我删掉的东西，必须明摆着是一份用完就丢的临时垃圾。
///
/// 2026-09-21：同目录下的 filetest 被传了游戏安装目录，整份游戏被删空。那次事故之后加的闸。
/// </summary>
static string Sandbox(string? requested)
{
    string temp = Path.GetFullPath(Path.GetTempPath());
    string full = Path.GetFullPath(requested ?? Path.Combine(temp, "modforge-workspace-test"));

    if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"沙箱目录必须在系统临时目录下：{temp}");
        Console.Error.WriteLine($"  拒绝使用：{full}");
        Console.Error.WriteLine("  （这个工具会递归删掉沙箱目录，所以不接受任意路径）");
        Environment.Exit(2);
    }

    return full;
}

/// <summary>
/// 找 Godot 辅助脚本目录（tools/godot-helpers）。从可执行文件向上探测，与
/// EditorServer.FindHelperScriptDirectory 同一套逻辑。
/// </summary>
static string FindGodotHelperDirectory()
{
    string? probe = AppContext.BaseDirectory;

    for (int i = 0; i < 8 && probe is not null; i++)
    {
        string candidate = Path.Combine(probe, "tools", "godot-helpers");

        if (File.Exists(Path.Combine(candidate, "convert_to_webp.gd")))
        {
            return candidate;
        }

        probe = Path.GetDirectoryName(probe);
    }

    return AppContext.BaseDirectory;
}

/// <summary>
/// 写一张 1×1 的合法 PNG 并返回文件名。真实卡图不需要更大——这条链路验证的是
/// "文件被收进来、引用被改对"，与像素内容无关，而小文件让测试跑得快。
/// </summary>
static string CreateTinyPngAt(string path)
{
    // 1×1 红点，PNG 的结构（签名 + IHDR + IDAT + IEND）都是固定字节。
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
    return Path.GetFileName(path);
}

static void CreateTinyPng(string path) => CreateTinyPngAt(path);

/// <summary>样例工程里哪些卡填了中文，用来决定语言文件该不该在。</summary>
static bool AnyChineseText(ModProject project) =>
    project.Cards.Any(c => c.Name.Has("zhs") || c.Description.Has("zhs")) ||
    project.Powers.Any(p => p.Name.Has("zhs") || p.Description.Has("zhs"));
