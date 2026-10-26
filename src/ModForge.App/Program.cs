using ModForge.App;
using ModForge.Core;
using ModForge.Core.Model;

// ModForge: an external editor for Slay the Spire 2 mods.
//
// Starts a local web UI in the default browser. Paths (game, export, card art, skins)
// come from settings.json beside the executable and are editable in the UI's settings
// page; the command line can still override them for one-off runs.

// 必须在创建任何窗口之前声明 DPI 感知，否则不生效。
//
// 这是个控制台程序，默认 DPI 不感知；于是在 250% 缩放的显示器上，Windows 会把我们弹出的
// 选择对话框当低分辨率位图整个放大——文字明显比浏览器自己的弹窗模糊。声明之后由系统按
// 真实像素渲染，对话框与资源管理器里的一致清晰。
FolderPicker.EnablePerMonitorDpi();

string? gameDir = null;
string? projectDir = null;
bool noBrowser = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--game" when i + 1 < args.Length:
            gameDir = args[++i];
            break;
        case "--project" when i + 1 < args.Length:
            projectDir = args[++i];
            break;
        case "--no-browser":
            noBrowser = true;
            break;
        case "--selftest-picker":
            // 验证选择对话框的 COM 接口声明（vtable 槽位）在这台机器上是对的。
            return FolderPicker.SelfTest();
        case "--help" or "-h":
            PrintHelp();
            return 0;
    }
}

string programRoot = FindProgramRoot();

string uiDir = Path.Combine(programRoot, "ui");

if (!File.Exists(Path.Combine(uiDir, "index.html")))
{
    Console.Error.WriteLine($"Editor UI not found (looked in {uiDir}).");
    return 1;
}

// 路径设置放在程序旁边，界面上可改。命令行参数优先，因为那是一次性的显式指定，
// 比存下来的值更权威。
string settingsPath = Path.Combine(programRoot, "settings.json");
ModForgeSettings settings = ModForgeSettings.Load(settingsPath, Console.WriteLine);

if (gameDir is not null)
{
    settings.GameDirectory = gameDir;
}

if (projectDir is not null)
{
    // --project 可以指到工程文件夹本身，也可以指到装着工程的父目录。
    string full = Path.GetFullPath(projectDir);

    if (!Workspace.LooksLikeProject(full))
    {
        string? nested = Directory.Exists(full)
            ? Directory.EnumerateDirectories(full).FirstOrDefault(Workspace.LooksLikeProject)
            : null;

        if (nested is not null)
        {
            full = nested;
        }
    }

    settings.ProjectDirectory = full;
}

settings.ApplyDefaults(programRoot, FindGameDirectory());

// 首次运行时把默认值落盘，用户就能在程序旁边找到这个文件直接改。
if (!File.Exists(settingsPath) && !string.IsNullOrWhiteSpace(settings.GameDirectory))
{
    settings.Save(settingsPath);
}

if (string.IsNullOrWhiteSpace(settings.GameDirectory) || !Directory.Exists(settings.GameDirectory))
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("  Could not find the Slay the Spire 2 installation.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  Pass it once on the command line, or set it in the editor's settings page:");
    Console.Error.WriteLine(@"    ModForge.exe --game ""D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2""");
    Console.Error.WriteLine();
    return 1;
}

if (!string.IsNullOrWhiteSpace(settings.ProjectDirectory) &&
    !Directory.Exists(settings.ProjectDirectory))
{
    // 设置里记的工程被删了/移走了：当作没打开过。
    settings.ProjectDirectory = "";
}

// 工程是一份独立文件夹（About / Language / Textures / Skins / Cards / Source / Mod / Create），
// 以工程名命名，放在 ProjectsRoot 下。
//
// 编辑器启动时**不打开任何工程**（见 EditorServer 的构造函数），所以这里选的只是一个
// "还没新建也没打开时，保存往哪写"的落点。
if (string.IsNullOrWhiteSpace(settings.ProjectDirectory))
{
    settings.ProjectDirectory = ModForge.Core.Workspace.UniqueProjectFolder(
        settings.ProjectsRoot,
        "MyMod",
        overwrite: false);

    Directory.CreateDirectory(settings.ProjectDirectory);
}

// 缓存（卡面素材、数据快照）与默认导出目录都属于"这台机器"，不属于某一份工程，
// 所以放在程序旁边而不是工程文件夹里。
var workspace = new Workspace(settings.ProjectDirectory, settings.CardArtDirectory, programRoot);
workspace.EnsureDirectories();

// 立刻把工程位置落盘。不存的话每次重启都会找不到"上次的工程"，于是又新建一个
// MyMod-2、MyMod-3……（实测踩过）。
settings.Save(settingsPath);

EditorServer server;

try
{
    server = new EditorServer(settings, uiDir, workspace, settingsPath);
}
catch (IOException ex)
{
    // 最常见的原因是端口被占（通常就是已经开着一个编辑器了）。给出能照着做的话，
    // 而不是让异常直接冲出来——启动脚本里那会显示成一堆看不懂的调用栈。
    Console.Error.WriteLine();
    Console.Error.WriteLine($"  {ex.Message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  提示：如果是端口被占，最可能是已经有一个 ModForge 在运行。");
    Console.Error.WriteLine("        关掉那个窗口再运行本程序即可。");
    Console.Error.WriteLine();
    return 1;
}

using (server)
{
    Console.WriteLine();
    Console.WriteLine("  ModForge - Slay the Spire 2 mod editor");
    Console.WriteLine($"  game      : {settings.GameDirectory}");
    Console.WriteLine($"  project   : {workspace.Root}");
    Console.WriteLine($"  cards     : {workspace.Layout.CardsDirectory}  (this project only)");
    Console.WriteLine($"  mod       : {workspace.ModDirectory}");
    Console.WriteLine($"  library   : {workspace.LibraryDirectory ?? "(none)"}");
    Console.WriteLine($"  copy to   : {(string.IsNullOrWhiteSpace(settings.ExportDirectory) ? "(not set)" : settings.ExportDirectory)}");
    Console.WriteLine($"  settings  : {settingsPath}");
    Console.WriteLine($"  editor    : {server.Url}");
    Console.WriteLine();
    Console.WriteLine("  Press Ctrl+C to stop.");
    Console.WriteLine();

    if (!noBrowser)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = server.Url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Could not open a browser automatically ({ex.Message}); open the URL above.");
        }
    }

    server.Run();
}

return 0;

/// <summary>
/// 程序根目录：含有 ui/index.html 的那一层。
///
/// 从可执行文件所在目录向上找，所以无论从仓库根、构建输出目录还是发布目录运行都能命中，
/// 而且工程文件与卡图稳定地落在同一个位置（程序旁边），不会随运行方式改变。
/// 找不到时退回可执行文件所在目录。
/// </summary>
static string FindProgramRoot()
{
    string? probe = AppContext.BaseDirectory;

    for (int i = 0; i < 8 && probe is not null; i++)
    {
        if (File.Exists(Path.Combine(probe, "ui", "index.html")))
        {
            return probe;
        }

        probe = Path.GetDirectoryName(probe);
    }

    return AppContext.BaseDirectory;
}

static void PrintHelp()
{
    Console.WriteLine("ModForge - Slay the Spire 2 mod editor");
    Console.WriteLine();
    Console.WriteLine("usage: ModForge.exe [--game <path>] [--project <path>] [--no-browser]");
    Console.WriteLine();
    Console.WriteLine("  --game <path>      Slay the Spire 2 install directory (overrides settings.json).");
    Console.WriteLine("  --project <path>   Project folder to open (holds project.json + cardart/).");
    Console.WriteLine("  --no-browser       Do not open a browser automatically.");
    Console.WriteLine("  --selftest-picker  Check the native dialog COM interface, then exit.");
    Console.WriteLine();
    Console.WriteLine("Paths are also editable in the editor's settings page; they are stored in");
    Console.WriteLine("settings.json beside the executable.");
}

/// <summary>Probes the usual Steam install locations for the game.</summary>
static string? FindGameDirectory()
{
    var candidates = new List<string>
    {
        @"C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2",
        @"C:\Program Files\Steam\steamapps\common\Slay the Spire 2",
        @"D:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2",
        @"D:\Steam\steamapps\common\Slay the Spire 2",
        @"E:\Steam\steamapps\common\Slay the Spire 2",
    };

    // Any additional Steam library folders recorded by Steam itself.
    foreach (string root in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam", @"D:\Steam" })
    {
        string libraryFile = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFile))
        {
            continue;
        }

        try
        {
            foreach (string line in File.ReadLines(libraryFile))
            {
                int key = line.IndexOf("\"path\"", StringComparison.Ordinal);
                if (key < 0)
                {
                    continue;
                }

                string[] parts = line.Split('"');
                if (parts.Length >= 4)
                {
                    candidates.Add(Path.Combine(parts[3], "steamapps", "common", "Slay the Spire 2"));
                }
            }
        }
        catch (IOException)
        {
            // Ignore unreadable library files.
        }
    }

    return candidates.FirstOrDefault(path =>
        Directory.Exists(path) &&
        File.Exists(Path.Combine(path, "data_sts2_windows_x86_64", "sts2.dll")));
}
