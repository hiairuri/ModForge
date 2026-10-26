using System.Net;
using System.Text;
using System.Text.Json;
using ModForge.Core;
using ModForge.Core.Catalog;
using ModForge.Core.Export;
using ModForge.Core.Model;
using ModForge.Core.Modules.Descriptions;
using ModForge.Core.Modules.Effects;
using ModForge.Core.Modules.Files;
using ModForge.Core.Skin;
using ModForge.Core.Spine;
using ModForge.Core.Validation;

namespace ModForge.App;

/// <summary>
/// A dependency-free local HTTP server hosting the editor.
///
/// Everything runs on the user's machine: the catalog is read from the local game
/// install and generated mods are written locally. No network access is required.
/// </summary>
public sealed class EditorServer : IDisposable
{
    /// <summary>
    /// 监听器。构造时创建（见构造函数里"每次尝试都新建"的说明），所以不是 readonly。
    /// </summary>
    private HttpListener _listener = new();
    private readonly string _uiDir;

    /// <summary>
    /// 语言资源目录（程序根目录下的 language/），界面文案与描述句式都在这里。
    ///
    /// 它是 ui/ 的**兄弟目录**，所以静态服务要单独放行一条 /language/ 前缀：
    /// 只放行 ui/ 的话，index.html 里的 &lt;script src="../language/ui.js"&gt; 会被
    /// 越界检查挡下（那正是这个检查该做的事），文案就加载不出来。
    /// </summary>
    private readonly string _languageDir;
    private readonly Workspace _workspace;
    private readonly ModForgeSettings _settings;

    /// <summary>游戏目录。改了设置之后会变，所以不是 readonly。</summary>
    private string _gameDir;

    private ModProject _project;

    /// <summary>
    /// 当前这份工程**是不是已经认领了一个工程文件夹**（新建或打开算认领，启动时的空工程不算）。
    ///
    /// 这一位是防"空工程覆盖真工程"的闸：编辑器启动时不载入任何工程，而工作目录还指着上次那个
    /// 工程文件夹——不做这个区分的话，"保存/导出/打开另一个工程"都会先把启动时那份空工程写进去，
    /// **把那边的定义文件按"已经删掉的实体"清掉**。这个坑踩过两次（第二次删掉了用户的卡）。
    /// </summary>
    private bool _projectBound;

    private VanillaCatalog? _catalog;
    private readonly List<string> _log = new();

    private ModForge.Core.GameTerms? _terms;
    private ModForge.Core.Gfx.CardArtLibrary? _artLibrary;
    private ModForge.Core.Gfx.AssetExtractor? _assets;
    private ModForge.Core.Gfx.WebpConverter? _webp;
    private ModForge.Core.Gfx.WebpConverter? _png;
    private string? _assetCacheDir;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Options for project payloads. Enums are written as names so the editor UI can
    /// bind them directly to dropdowns and a saved project stays readable.
    /// </summary>
    private static readonly JsonSerializerOptions ProjectJson = new(ModProject.JsonOptions);

    /// <summary>
    /// 编辑器只监听回环地址，端口由构造时从 preferredPort 起找第一个可用的。
    /// 这里单独暴露端口（而不是只给拼好的 URL），因为打开浏览器那一步要拿它来构造地址——
    /// 用整数拼出来的地址不存在"用户可控字符串被当成命令"的可能。
    /// </summary>
    public int Port { get; private set; }

    /// <summary>编辑器的本地地址，形如 http://127.0.0.1:17890/。</summary>
    public string Url => $"http://127.0.0.1:{Port}/";

    /// <summary>
    /// settings.json 的位置。路径设置（游戏/导出/卡面/皮肤）存在这里，界面上可改。
    /// </summary>
    public string SettingsPath { get; }

    public EditorServer(
        ModForgeSettings settings,
        string uiDir,
        Workspace workspace,
        string SettingsPath,
        int preferredPort = 17890)
    {
        _settings = settings;
        _gameDir = settings.GameDirectory;
        _uiDir = uiDir;
        _languageDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(uiDir)) ?? uiDir, "language");
        _workspace = workspace;
        this.SettingsPath = SettingsPath;

        _workspace.EnsureDirectories();

        // 打开编辑器时**不载入任何工程**，直接从一个空工程开始。
        //
        // 工程文件目前只是测试素材（见 README），自动载入反而带来两个真实麻烦：
        //   1. 工程读不进来时，界面看起来一切正常、其实什么都没载入（只有日志里有一行）；
        //   2. 那之后任何一个会写盘的端点（导出 / 保存）都会把这个空工程覆盖回磁盘
        //      —— 真丢过两次数据。
        // 要继续用某个工程，在「项目」页显式打开。
        _project = new ModProject();

        // 当前这份空工程**还没有认领任何文件夹**：在用户真的新建/打开之前，
        // 不许把它写进一个已经有工程的文件夹（那正是上面第 2 条）。
        _projectBound = false;

        int port = preferredPort;
        Exception? lastError = null;

        for (int attempt = 0; attempt < 20; attempt++)
        {
            // 每次尝试都新建一个 HttpListener。
            //
            // 不能复用一个实例：Start() 失败（端口被占）之后，.NET 会把这个 listener 内部
            // 置为已释放，下一轮再碰它的 Prefixes 就抛 ObjectDisposedException——那不是
            // HttpListenerException，漏掉就会让启动脚本以一个看不懂的异常崩掉。
            var listener = new HttpListener();

            try
            {
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();
                _listener = listener;
                Port = port;
                return;
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                lastError = ex;
                listener.Close();
                port++;
            }
        }

        // 全试完都占不上：最常见的原因就是已经有一个编辑器在跑。
        throw new IOException(
            $"无法为编辑器绑定本地端口（{preferredPort}..{port - 1}）。" +
            "通常是因为已经开着一个 ModForge 了——请先关掉那个窗口再运行。" +
            (lastError is null ? string.Empty : $"（最后一次错误：{lastError.Message}）"));
    }

    public void Run()
    {
        // The catalog scan takes a few seconds; do it once up front.
        Log("Scanning the game for vanilla content...");
        try
        {
            _catalog = VanillaCatalog.Build(_gameDir);
            Log($"Catalog ready: {_catalog.Entries.Count} entries (game {_catalog.GameVersion}).");
        }
        catch (Exception ex)
        {
            Log($"Catalog scan failed: {ex.Message}. Collision checks will be skipped.");
        }

        PreparePresentationAssets();

        // 句式模块：编辑器根目录的 language/sentences/ 文件夹，一种语言一个文件。它的位置由
        // SentenceLibrary 自己向上探测（工作区里放一份也能覆盖），这里只记录一下。
        Log($"Sentences loaded from {SentenceLibrary.Folder} ({string.Join(", ", SentenceLibrary.AvailableLanguages())}).");

        // 让描述生成能写出游戏当前语言下的原版名称（如"打击"而不是"StrikeIronclad"）。
        DescriptionModule.Names = new VanillaNames(_gameDir);

        // 界面里的游戏术语（类型、稀有度、关键词）也取自游戏本地化表，用词与游戏一致。
        _terms = new ModForge.Core.GameTerms(_gameDir);

        // 可选的卡图来源：素材库（用户攒图的地方）+ 游戏本体的原版卡图。
        // 工程自己的 cardart 由导出流程直接读，不经过这个库。
        _artLibrary = new ModForge.Core.Gfx.CardArtLibrary(_gameDir, _workspace.LibraryDirectory);

        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = _listener.GetContext();
            }
            catch (HttpListenerException)
            {
                break;
            }

            try
            {
                Handle(ctx);
            }
            catch (Exception ex)
            {
                // 单个请求出问题不能让整个编辑器停下来——那意味着一个畸形请求就能把用户
                // 正在编辑的会话弄没。这里只记录并回一个错误，循环继续。
                Log($"Request failed: {ex.Message}");
                TryWriteJson(ctx, 500, new { error = ex.Message });
            }
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        string path = ctx.Request.Url?.AbsolutePath ?? "/";
        string method = ctx.Request.HttpMethod;

        // 卡面图片直接回二进制，不走 JSON。
        if (path.StartsWith("/api/art/local/", StringComparison.Ordinal) ||
            path.StartsWith("/api/art/vanilla/", StringComparison.Ordinal))
        {
            ServeArtImage(ctx, path);
            return;
        }

        if (path.StartsWith("/api/", StringComparison.Ordinal))
        {
            HandleApi(ctx, path, method);
            return;
        }

        ServeStatic(ctx, path);
    }

    /// <summary>
    /// Sets up the Godot-backed helpers: image conversion (the game requires WebP art)
    /// and extraction of the game's own card frames for the preview.
    /// </summary>
    private void PreparePresentationAssets()
    {
        string scriptDir = FindHelperScriptDirectory();
        string godotHint = Environment.GetEnvironmentVariable("MODFORGE_GODOT") ?? @"D:\Tools";

        _webp = ModForge.Core.Gfx.WebpConverter.Discover(
            godotHint,
            Path.Combine(scriptDir, "convert_to_webp.gd"));

        // 同一个机制、换成 PNG：mod 封面固定叫 About/Preview.png，选进来的其它格式要转成
        // 真正的 PNG（不然扩展名就是骗人的，谁打开都看到一张坏图）。
        _png = ModForge.Core.Gfx.WebpConverter.DiscoverPng(
            godotHint,
            Path.Combine(scriptDir, "convert_to_png.gd"));

        if (_webp.IsAvailable)
        {
            Log("Godot found; PNG card art will be converted to WebP automatically.");
        }
        else
        {
            Log("No Godot installation found. Card art must be supplied as .webp.");
        }

        _assetCacheDir = _workspace.PresenterDirectory;
        _assets = new ModForge.Core.Gfx.AssetExtractor(
            _gameDir,
            _webp.GodotExecutable,
            Path.Combine(scriptDir, "export_assets.gd"));

        if (_assets.IsAvailable)
        {
            _assets.EnsureExtracted(_assetCacheDir, Log);
        }
    }

    /// <summary>
    /// Locates the Godot helper scripts, which live beside the editor or in the source
    /// tree when running from a build output folder.
    /// </summary>
    private static string FindHelperScriptDirectory()
    {
        string? probe = AppContext.BaseDirectory;

        for (int i = 0; i < 8 && probe is not null; i++)
        {
            string candidate = Path.Combine(probe, "tools", "godot-helpers");
            if (File.Exists(Path.Combine(candidate, "export_assets.gd")))
            {
                return candidate;
            }

            probe = Path.GetDirectoryName(probe);
        }

        return AppContext.BaseDirectory;
    }

    private void HandleApi(HttpListenerContext ctx, string path, string method)
    {
        switch (path, method)
        {
            case ("/api/state", "GET"):
                TryWriteJson(ctx, 200, new
                {
                    project = System.Text.Json.JsonSerializer.SerializeToElement(_project, ProjectJson),
                    schema = EditorSchema.Build(),
                    gameDir = _gameDir,
                    // 界面要显示"哪些路径是什么"，所以状态里也带一份设置与相关目录。
                    settings = _settings.Describe(),
                    settingsPath = SettingsPath,
                    projectPath = _workspace.Layout.AboutFile,
                    projectRoot = _workspace.Root,
                    projectName = _project.Name,

                    // 当前这份工程有没有落到一个文件夹上。启动时是空工程（false），界面据此说清
                    // "现在还没有打开的工程"，而不是让路径那一栏看起来像已经打开了什么。
                    projectBound = _projectBound,
                    lastProjectDirectory = _settings.ProjectDirectory,
                    cardsDirectory = _workspace.Layout.CardsDirectory,
                    sourcesDirectory = _workspace.Layout.SourceDirectory,
                    modDirectory = _workspace.ModDirectory,
                    catalogReady = _catalog is not null,
                    catalogCount = _catalog?.Entries.Count ?? 0,
                    gameVersion = _catalog?.GameVersion ?? "unknown",
                    // 游戏支持的语言，供界面语言与内容语言两个下拉框使用。
                    languages = GameLanguages.All.Select(l => new
                    {
                        code = l.Code,
                        name = l.Display("zh"),
                        nameEn = l.NameEn,
                        nameZh = l.NameZh,
                    }).ToList(),
                    fallbackLanguage = GameLanguages.Fallback,

                    // 自动生成描述的句式表。这是独立于程序的模块：文件夹里一种语言一个文件，
                    // 加语言只要往里面丢一个 json，不必重新编译。
                    sentences = new
                    {
                        folder = SentenceLibrary.Folder,
                        available = SentenceLibrary.AvailableLanguages(),
                    },

                    // 界面术语（卡牌类型、稀有度、关键词）在各语言下的名称，取自游戏本地化表。
                    // 一次下发全部语言，界面切换语言时无需再请求。
                    terms = GameLanguages.All.ToDictionary(
                        l => l.Code,
                        l => _terms is null ? null : BuildTerms(l.Code)),

                    log = RecentLog(60),
                });
                break;

            case ("/api/catalog", "GET"):
                TryWriteJson(ctx, 200, new
                {
                    cards = Slice("CARD"),
                    powers = Slice("POWER"),
                    relics = Slice("RELIC"),
                });
                break;

            case ("/api/skins", "GET"):
                TryWriteJson(ctx, 200, new { installed = DiscoverSkinPacks() });
                break;

            case ("/api/presenter", "GET"):
                TryWriteJson(ctx, 200, new { available = ListPresenterAssets() });
                break;

            case ("/api/cardart/choose", "POST"):
                TryWriteJson(ctx, 200, ChooseCardArt(ReadBody(ctx)));
                break;

            case ("/api/skins/read", "POST"):
                TryWriteJson(ctx, 200, ReadSkin(ReadBody(ctx)));
                break;

            case ("/api/suggest-description", "POST"):
                TryWriteJson(ctx, 200, SuggestDescription(ReadBody(ctx)));
                break;

            // 把编辑中的效果列表写成文字。界面每行的摘要与"自动填充"因此说法一致，
            // 因为两者都走描述模块（DescriptionModule）。
            case ("/api/describe-effects", "POST"):
                TryWriteJson(ctx, 200, DescribeEffects(ReadBody(ctx)));
                break;

            // 描述里可以引用的占位符。由后端算，界面不必自己再实现一套，
            // 也就不会出现"编辑器给了标签、生成时却没有该变量"的情况。
            case ("/api/art/list", "GET"):
                TryWriteJson(ctx, 200, new
                {
                    localDir = _artLibrary?.LocalDirectory,
                    local = _artLibrary?.ListLocal() ?? (IReadOnlyList<ModForge.Core.Gfx.CardArtSource>)Array.Empty<ModForge.Core.Gfx.CardArtSource>(),
                    vanilla = _artLibrary?.ListVanilla(QueryLanguage(ctx)) ?? (IReadOnlyList<ModForge.Core.Gfx.CardArtSource>)Array.Empty<ModForge.Core.Gfx.CardArtSource>(),
                });
                break;

            case ("/api/art/use-vanilla", "POST"):
                TryWriteJson(ctx, 200, UseVanillaArt(ReadBody(ctx)));
                break;

            case ("/api/placeholders", "POST"):
                TryWriteJson(ctx, 200, DescribePlaceholders(ReadBody(ctx)));
                break;

            case ("/api/project", "POST"):
                string body = ReadBody(ctx);
                _project = ModProject.FromJson(body);
                SaveProject();
                TryWriteJson(ctx, 200, new { ok = true });
                break;

            // ---- 路径设置 ----------------------------------------------------------

            case ("/api/settings", "GET"):
                TryWriteJson(ctx, 200, new
                {
                    settings = _settings.Describe(),
                    settingsPath = SettingsPath,
                    projectPath = _workspace.Layout.AboutFile,
                    sourcesDirectory = _workspace.Layout.SourceDirectory,
                    modDirectory = _workspace.ModDirectory,
                    buildDirectory = _workspace.BuildDirectory,
                });
                break;

            // 改路径。卡面目录与游戏目录会牵动已经建立好的那些辅助对象，所以要重建它们。
            case ("/api/settings", "POST"):
                TryWriteJson(ctx, 200, ApplySettings(ReadBody(ctx)));
                break;

            // ---- 原生文件夹选择对话框 ----------------------------------------------
            //
            // 浏览器给不了真实路径（<input type=file> 只给文件内容），所以"选文件夹"
            // 只能由服务端进程弹系统对话框，把路径回给界面。
            //
            // 这个请求**不在这里回响应**：对话框要人操作，可能开着几十秒，而这个循环是
            // 串行处理请求的——就地等待会把整个编辑器冻住（实测：对话框开着时连 /api/state
            // 都不响应）。所以交给后台任务去弹，循环继续服务其它请求，由那个任务最后写响应。
            case ("/api/pick-folder", "POST"):
                BeginPick(ctx, ReadBody(ctx));
                return;

            // 同上，只是选一个图片文件（mod 封面）。
            case ("/api/pick-image", "POST"):
                BeginPick(ctx, ReadBody(ctx), image: true);
                return;

            // ---- 工程：保存 / 另存为 / 打开 / 新建 ---------------------------------

            // 保存：用到的贴图收进 cardart、清掉没用的、写出 project.json 与生成源码。
            case ("/api/project/save", "POST"):
                body = ReadBody(ctx);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    _project = ModProject.FromJson(body);
                }

                TryWriteJson(ctx, 200, SaveProjectBundle());
                break;

            case ("/api/project/save-as", "POST"):
                TryWriteJson(ctx, 200, SaveProjectAs(ReadBody(ctx)));
                break;

            case ("/api/project/open", "POST"):
                TryWriteJson(ctx, 200, OpenProject(ReadBody(ctx)));
                break;

            case ("/api/project/new", "POST"):
                TryWriteJson(ctx, 200, NewProject(ReadBody(ctx)));
                break;

            // 新建/另存为前先问一句：目标文件夹已经存在吗？
            // 界面据此决定要不要弹"是否覆盖"的确认框——不直接覆盖用户的东西。
            case ("/api/project/exists", "POST"):
                TryWriteJson(ctx, 200, ProjectFolderExists(ReadBody(ctx)));
                break;

            // 在文件资源管理器里打开某个文件夹（项目页的"在资源管理器中查看"）。
            case ("/api/project/reveal", "POST"):
                TryWriteJson(ctx, 200, RevealFolder(ReadBody(ctx)));
                break;

            // 把当前编辑内容落盘（不切目录）。关页面前的自动保存用。
            case ("/api/project/flush", "POST"):
                body = ReadBody(ctx);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    _project = ModProject.FromJson(body);
                }

                if (!MayWriteWorkspace(out string flushWhy))
                {
                    TryWriteJson(ctx, 200, new { ok = false, error = flushWhy });
                    break;
                }

                SaveProject();
                TryWriteJson(ctx, 200, new { ok = true, projectPath = ProjectPath });
                break;

            case ("/api/validate", "POST"):
                body = ReadBody(ctx);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    _project = ModProject.FromJson(body);
                }

                var issues = ProjectValidator.Validate(_project, _catalog)
                    .Select(i => new { severity = i.Severity, message = i.Message, where = i.Where })
                    .ToList();
                TryWriteJson(ctx, 200, new { issues });
                break;

            case ("/api/export", "POST"):
                body = ReadBody(ctx);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    _project = ModProject.FromJson(body);
                }

                // 没打开工程时不能导出：手里那份空工程导出来是个空 mod，
                // 而且它会把产物写进工作目录（上次那个工程）的 Mod/ 里。
                if (!MayWriteWorkspace(out string exportWhy))
                {
                    TryWriteJson(ctx, 200, new { ok = false, messages = new[] { exportWhy } });
                    break;
                }

                SaveProject();
                RunExport(ctx);
                break;

            case ("/api/save", "POST"):
                body = ReadBody(ctx);
                _project = ModProject.FromJson(body);

                if (!MayWriteWorkspace(out string saveWhy))
                {
                    TryWriteJson(ctx, 200, new { ok = false, error = saveWhy });
                    break;
                }

                SaveProject();
                TryWriteJson(ctx, 200, new { ok = true, path = ProjectPath });
                break;

            case ("/api/log", "GET"):
                TryWriteJson(ctx, 200, new { log = RecentLog(200) });
                break;

            default:
                TryWriteJson(ctx, 404, new { error = $"Unknown endpoint {method} {path}" });
                break;
        }
    }

    private void RunExport(HttpListenerContext ctx)
    {
        IReadOnlyList<ProjectIssue> issues = ProjectValidator.Validate(_project, _catalog);
        if (issues.Any(i => i.Severity == "error"))
        {
            TryWriteJson(ctx, 200, new
            {
                ok = false,
                messages = issues.Select(i => $"[{i.Severity}] {i.Message}").ToList(),
            });
            return;
        }

        Log($"Exporting {_project.Id}...");
        var exporter = new Exporter(_gameDir) { WebpConverter = _webp, Workspace = _workspace };

        // 产物落在工程自己的 Mod/ 里：导出与工程是配套的，放在工程里才找得到、才跟得上
        // 工程被搬走。设置里另给的目录是"再拷一份过去"（见 CopyExport），不是主产物位置。
        ExportResult result = exporter.Export(_project, _workspace.ModDirectory);

        if (result.Success)
        {
            CopyExport(result);
        }

        foreach (string message in result.Messages)
        {
            Log(message);
        }

        // Validate the produced assembly the same way the game will load it.
        bool ok = result.Success;
        if (ok)
        {
            string dll = result.ArtifactPaths.First(p => p.EndsWith(".dll", StringComparison.Ordinal));
            var vanillaIds = _catalog?.Entries
                .Select(e => $"{e.Category}.{e.Entry}")
                .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);

            var check = ModValidator.Validate(dll, _gameDir, vanillaIds);
            foreach (ValidationIssue issue in check.Issues)
            {
                Log($"[assembly:{issue.Severity}] {issue.Message}");
            }

            ok = !check.HasErrors;
            Log($"Assembly contains {check.CardCount} card(s) and {check.PowerCount} power(s).");
        }

        TryWriteJson(ctx, 200, new
        {
            ok,
            outputDirectory = result.OutputDirectory,
            messages = result.Messages,
        });
    }

    /// <summary>
    /// 把导出好的 mod 文件夹再复制一份到设置里指定的目录。
    ///
    /// 设置留空时什么都不做——产物就只在工程的 <c>Mod/</c> 里。把它填成游戏的 mods 目录，
    /// 导出就等于装好，省掉每次手动拷贝（装到游戏里也是这个功能存在的唯一理由）。
    /// 复制是**整份替换**：先把目标里同名的旧文件夹删掉再拷，免得删过的文件在目标里留着。
    /// </summary>
    private void CopyExport(ExportResult result)
    {
        if (string.IsNullOrWhiteSpace(_settings.ExportDirectory) || result.OutputDirectory is null)
        {
            return;
        }

        string target = Path.Combine(
            Path.GetFullPath(_settings.ExportDirectory),
            Path.GetFileName(result.OutputDirectory));

        try
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            foreach (string file in Directory.EnumerateFiles(result.OutputDirectory, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(target, Path.GetRelativePath(result.OutputDirectory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }

            Log($"已复制一份到 {target}");
        }
        catch (Exception ex)
        {
            Log($"[warn] 复制到 {target} 失败：{ex.Message}");
        }
    }

    private List<object> Slice(string category)
    {
        if (_catalog is null)
        {
            return new List<object>();
        }

        return _catalog.ByCategory(category)
            .Select(e => (object)new
            {
                id = $"{e.Category}.{e.Entry}",
                entry = e.Entry,
                nameEn = e.NameEn,
                nameZh = e.NameZh,
            })
            .ToList();
    }

    /// <summary>
    /// Lists the extracted game assets the card preview uses, keyed by the role they
    /// play (frame per card type, banner, energy icon per character).
    /// </summary>
    private Dictionary<string, string> ListPresenterAssets()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (_assetCacheDir is null || !Directory.Exists(_assetCacheDir))
        {
            return result;
        }

        foreach (string file in Directory.EnumerateFiles(_assetCacheDir, "*.png"))
        {
            result[Path.GetFileNameWithoutExtension(file)] = $"/presenter/{Path.GetFileName(file)}";
        }

        return result;
    }

    /// <summary>
    /// Converts a chosen image to the WebP the game requires, so the editor can report
    /// immediately whether the art will load rather than at export time.
    /// </summary>
    private object ChooseCardArt(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        string? path = doc.RootElement.TryGetProperty("path", out JsonElement p) ? p.GetString() : null;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new { ok = false, error = "Image file not found." };
        }

        bool alreadyWebp = path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

        if (alreadyWebp)
        {
            return new { ok = true, path, converted = false, note = "WebP art is used as is." };
        }

        if (_webp is not { IsAvailable: true })
        {
            return new
            {
                ok = false,
                error = "This image needs converting to WebP, but no Godot installation was found. " +
                        "Supply a .webp file, or set the MODFORGE_GODOT environment variable to your " +
                        "Godot folder.",
            };
        }

        try
        {
            string converted = _webp.Convert(path);
            Log($"Converted {Path.GetFileName(path)} to WebP for use as card art.");
            return new { ok = true, path = converted, converted = true };
        }
        catch (Exception ex)
        {
            return new { ok = false, error = ex.Message };
        }
    }

    /// <summary>
    /// 回送一张卡面图片。
    ///
    ///   /api/art/local/<相对路径>       工程 cardart 或素材库里的文件
    ///   /api/art/vanilla/<pool>/<stem>  游戏本体的原版卡图
    ///
    /// 原版卡图取出的是 .ctex 里内嵌的 WebP 流，直接交给浏览器显示，不需要解码像素。
    /// </summary>
    private void ServeArtImage(HttpListenerContext ctx, string path)
    {
        if (_artLibrary is null)
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        const string localPrefix = "/api/art/local/";
        const string vanillaPrefix = "/api/art/vanilla/";

        try
        {
            if (path.StartsWith(localPrefix, StringComparison.Ordinal))
            {
                string id = Uri.UnescapeDataString(path[localPrefix.Length..]);

                // 走 Workspace 的解析，而不是只查素材库：卡图的引用可能指向**工程自己的
                // cardart**（从原版导入的图就落在那里），也可能指向素材库。Workspace 的
                // 顺序是"工程 → 素材库"，与卡面预览、保存、导出完全一致。
                //
                // 这里曾经只查素材库，于是刚导入的图正好不在库里、只在工程里，预览就 404
                // ——表现成"导入的卡面没有预览"。
                string? file = _workspace.ResolveCardArt(id);

                if (file is null)
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                WriteImage(ctx, File.ReadAllBytes(file), ContentTypeFor(file));
                return;
            }

            string[] parts = path[vanillaPrefix.Length..].Split('/');
            if (parts.Length < 2)
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.Close();
                return;
            }

            byte[]? webp = _artLibrary.ReadVanillaImage(
                Uri.UnescapeDataString(parts[0]),
                Uri.UnescapeDataString(parts[1]));

            if (webp is null)
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            WriteImage(ctx, webp, "image/webp");
        }
        catch (Exception ex)
        {
            Log($"卡面图片读取失败：{ex.Message}");
            ctx.Response.StatusCode = 500;
            ctx.Response.Close();
        }
    }

    private static void WriteImage(HttpListenerContext ctx, byte[] bytes, string contentType)
    {
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes.Length;

        // 卡图不会变，允许缓存，翻列表时不会反复传输。
        ctx.Response.Headers["Cache-Control"] = "public, max-age=3600";
        ctx.Response.OutputStream.Write(bytes);
        ctx.Response.OutputStream.Close();
    }

    /// <summary>请求里带的语言参数，缺省用回退语言。</summary>
    private static string QueryLanguage(HttpListenerContext ctx)
    {
        string? raw = ctx.Request.QueryString["language"];
        return GameLanguages.Normalize(raw) ?? GameLanguages.Fallback;
    }

    /// <summary>
    /// 把一张原版卡图取出来存进**工程的 cardart**，然后作为卡牌的立绘。
    ///
    /// 存进工程而不是素材库：这张图是这张牌实际要用的，保存时就该在工程里。
    /// （素材库是用户自己攒图的地方，编辑器不往里写。）
    ///
    /// 之所以复制而不是直接引用游戏内部路径：生成 mod 时立绘要打包进 mod 的 PCK，
    /// 需要一份真实文件；而且用户随后可以在 cardart 里替换它。
    /// </summary>
    private object UseVanillaArt(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        string? pool = doc.RootElement.TryGetProperty("pool", out JsonElement p) ? p.GetString() : null;
        string? stem = doc.RootElement.TryGetProperty("stem", out JsonElement s2) ? s2.GetString() : null;

        if (_artLibrary is null || string.IsNullOrWhiteSpace(pool) || string.IsNullOrWhiteSpace(stem))
        {
            return new { ok = false, error = "缺少卡图参数。" };
        }

        _workspace.EnsureCreated();

        string? saved = _artLibrary.MaterializeVanillaImage(pool, stem, _workspace.Layout.CardsDirectory);
        if (saved is null)
        {
            return new { ok = false, error = "这张原版卡图取不出来。" };
        }

        Log($"取用原版卡图：{pool}/{stem}");

        // 返回相对 Cards/ 的引用，而不是绝对路径：工程换位置后引用仍然有效。
        return new
        {
            ok = true,
            path = Path.GetRelativePath(_workspace.Layout.CardsDirectory, saved).Replace('\\', '/'),
            fileName = Path.GetFileName(saved),
        };
    }

    /// <summary>
    /// 列出描述里可引用的占位符。
    ///
    /// 用请求里的效果构造一张临时卡牌再交给描述模块，这样正在编辑、尚未保存的
    /// 改动也能反映出来，并且与生成时的判断完全一致。
    /// </summary>
    private object DescribePlaceholders(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        string? language = doc.RootElement.TryGetProperty("language", out JsonElement l) ? l.GetString() : null;
        string resolved = GameLanguages.Normalize(language) ?? GameLanguages.Fallback;

        var card = new CardDef { Id = "preview" };

        if (doc.RootElement.TryGetProperty("effects", out JsonElement effects) &&
            effects.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in effects.EnumerateArray())
            {
                EffectDef? parsed = element.Deserialize<EffectDef>(ModProject.JsonOptions);
                if (parsed is not null)
                {
                    card.Effects.Add(parsed);
                }
            }
        }

        IReadOnlyList<EffectDef> resolvedEffects = EffectResolver.Resolve(_project, card.Effects);

        // 除了名字与说明，还把**每个占位符当前的值**一起下发。
        //
        // 界面上的卡面预览要用它替换 {Damage:diff()} 这类占位符。预览若自己再算一遍
        // （以前就是：在 JS 里又写了一份"取同类最大值"），两边逻辑必然分叉——
        // 服务端改了槽位分配，预览还按老规则算，卡面就会显示一个游戏里不存在的数。
        IReadOnlyDictionary<string, int> values = EffectResolver.DynamicVars(_project, resolvedEffects);

        var list = DescriptionPlaceholders.For(_project, card, resolved)
            .Select(p => new
            {
                placeholder = p.Placeholder,
                label = p.Label,
                value = values.TryGetValue(p.Placeholder, out int v) ? (int?)v : null,
            })
            .ToList();

        return new { ok = true, placeholders = list };
    }

    /// <summary>
    /// 某语言下的界面术语表。查不到的项由界面回退到英文枚举名。
    /// </summary>
    private Dictionary<string, string>? BuildTerms(string language)
    {
        if (_terms is null)
        {
            return null;
        }

        // 键由 GameTerms 自己给出，避免两处各维护一份列表而漏掉新增项。
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string key in ModForge.Core.GameTerms.AllKeys)
        {
            string? value = _terms.Lookup(key, language);
            if (!string.IsNullOrWhiteSpace(value))
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// 把请求里的效果列表逐条写成文字，供界面显示行摘要。
    ///
    /// 效果是编辑器正在编辑、尚未保存的对象，所以这里直接用请求里的内容，
    /// 而不是从工程里取——否则未保存的改动不会反映到摘要上。
    /// </summary>
    private object DescribeEffects(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        string? language = doc.RootElement.TryGetProperty("language", out JsonElement l) ? l.GetString() : null;
        string resolved = GameLanguages.Normalize(language) ?? GameLanguages.Fallback;

        if (!doc.RootElement.TryGetProperty("effects", out JsonElement effects) ||
            effects.ValueKind != JsonValueKind.Array)
        {
            return new { ok = false, error = "没有效果可描述。" };
        }

        // 反序列化时沿用工程本身的选项，枚举才会按名字解析。
        var list = new List<EffectDef>();
        foreach (JsonElement element in effects.EnumerateArray())
        {
            EffectDef? parsed = element.Deserialize<EffectDef>(ModProject.JsonOptions);
            if (parsed is not null)
            {
                list.Add(parsed);
            }
        }

        return new
        {
            ok = true,
            summaries = list.Select(e => DescriptionModule.Describe(_project, e, resolved)).ToList(),
        };
    }

    /// <summary>
    /// 按卡牌当前的效果生成一段描述，供编辑器"按效果自动填充"使用。
    ///
    /// 句式按请求的语言给出，所以中文用户拿到的是中文句子，而不是英文模板。
    /// </summary>
    private object SuggestDescription(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        string? id = doc.RootElement.TryGetProperty("id", out JsonElement v) ? v.GetString() : null;
        string? requested = doc.RootElement.TryGetProperty("language", out JsonElement l) ? l.GetString() : null;

        string language = GameLanguages.Normalize(requested) ?? GameLanguages.Fallback;

        // 优先用请求里带上来的、正在编辑的效果。
        //
        // 只按 id 去工程里找会用已保存的版本，于是编辑器里刚改的东西不生效——必须点一次
        // 校验或生成（把工程存过来）才"匹配"，这正是预览不便的原因。
        CardDef? card = null;

        if (doc.RootElement.TryGetProperty("effects", out JsonElement effects) &&
            effects.ValueKind == JsonValueKind.Array)
        {
            card = new CardDef { Id = id ?? "preview" };

            foreach (JsonElement element in effects.EnumerateArray())
            {
                EffectDef? parsed = element.Deserialize<EffectDef>(ModProject.JsonOptions);
                if (parsed is not null)
                {
                    card.Effects.Add(parsed);
                }
            }

            // 关键词会影响描述（例如"消耗"），所以也带上。
            if (doc.RootElement.TryGetProperty("keywords", out JsonElement keywords) &&
                keywords.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement keyword in keywords.EnumerateArray())
                {
                    if (Enum.TryParse(keyword.GetString(), out CardKeyword parsed))
                    {
                        card.Keywords.Add(parsed);
                    }
                }
            }
        }

        // 没有带效果时（例如从别处调用）退回工程里的那张卡。
        card ??= _project.Cards.FirstOrDefault(
            c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

        if (card is null)
        {
            return new { ok = false, error = "找不到这张卡牌。" };
        }

        string description = DescriptionPlaceholders.Suggest(_project, card, language);
        return string.IsNullOrWhiteSpace(description)
            ? new { ok = false, error = "这张卡还没有可描述的效果。" }
            : new { ok = true, description, language };
    }

    /// <summary>
    /// Lists installed skin packs from the game's mod folders and every Steam library,
    /// so the editor can offer them without the user hunting for file paths.
    /// </summary>
    private List<object> DiscoverSkinPacks()
    {
        var results = new List<object>();

        var roots = new List<string>
        {
            Path.Combine(_gameDir, "mods"),
            Path.Combine(_gameDir, "mods_disabled"),
        };

        // 设置里额外指定的皮肤目录，让放在别处的皮肤包也能被找到。
        if (!string.IsNullOrWhiteSpace(_settings.SkinDirectory) &&
            Directory.Exists(_settings.SkinDirectory))
        {
            roots.Add(_settings.SkinDirectory);
        }

        roots.AddRange(WorkshopRoots());

        foreach (string root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> packs;
            try
            {
                packs = Directory.EnumerateFiles(root, "*.pck", SearchOption.AllDirectories);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (string pack in packs)
            {
                try
                {
                    SkinPack skin = SkinPackReader.Read(pack);

                    // Skip packs that provide no animation routes; they are not skins.
                    if (skin.Assets.All(a => a.Kind == SkinTargetKind.Other) &&
                        skin.AnimationCandidates.Count == 0)
                    {
                        continue;
                    }

                    string modDir = Path.GetDirectoryName(pack) ?? root;
                    (string? name, string? id) = SkinPackReader.ReadManifest(modDir);

                    results.Add(new
                    {
                        path = pack,
                        name = name ?? Path.GetFileNameWithoutExtension(pack),
                        id = id ?? Path.GetFileNameWithoutExtension(pack),
                        targets = skin.Targets,
                        assetCount = skin.Assets.Count,
                        animationCount = skin.AnimationCandidates.Count,
                    });
                }
                catch (Exception)
                {
                    // A pack we cannot parse is simply not offered.
                }
            }
        }

        return results
            .OrderBy(r => r.GetType().GetProperty("name")!.GetValue(r)?.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Locates this game's Workshop content folder in every Steam library.
    ///
    /// The library list lives in libraryfolders.vdf beside the installed game, and
    /// Workshop content for an app is under
    /// "&lt;library&gt;/steamapps/workshop/content/&lt;appid&gt;". Deriving this from the game's
    /// own path means an install on any drive is found.
    /// </summary>
    private IEnumerable<string> WorkshopRoots()
    {
        const string appId = "2868840";

        // Layout is <steam>/steamapps/common/<game>.
        string? steamapps = Directory.GetParent(_gameDir)?.Parent?.FullName;
        string? steamRoot = steamapps is null ? null : Directory.GetParent(steamapps)?.FullName;
        if (steamapps is null || steamRoot is null)
        {
            yield break;
        }

        var libraries = new List<string> { steamRoot };

        string libraryFile = Path.Combine(steamapps, "libraryfolders.vdf");
        if (File.Exists(libraryFile))
        {
            try
            {
                foreach (string line in File.ReadLines(libraryFile))
                {
                    if (!line.Contains("\"path\"", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string[] parts = line.Split('"');
                    if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]))
                    {
                        libraries.Add(parts[3].Replace("\\\\", "\\"));
                    }
                }
            }
            catch (IOException)
            {
                // A missing library file simply means one fewer root to scan.
            }
        }

        foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string content = Path.Combine(library, "steamapps", "workshop", "content", appId);
            if (Directory.Exists(content))
            {
                yield return content;
            }
        }
    }

    /// <summary>
    /// Reads one skin pack in full and proposes slot bindings, which the UI then
    /// presents for the user to confirm or change.
    /// </summary>
    private object ReadSkin(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        string? path = doc.RootElement.TryGetProperty("path", out JsonElement p) ? p.GetString() : null;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new { ok = false, error = "Skin pack not found." };
        }

        SkinPack skin = SkinPackReader.Read(path);
        (string? name, string? id) = SkinPackReader.ReadManifest(Path.GetDirectoryName(path) ?? "");

        IReadOnlyList<string> candidates = skin.AnimationCandidates;
        Dictionary<string, string> suggested = AnimationBinder.SuggestAll(candidates);

        return new
        {
            ok = true,
            sourcePath = path,
            name = name ?? skin.Name,
            id = id ?? skin.Name,
            targets = skin.Targets,
            resources = skin.Assets
                .Where(a => a.Kind != SkinTargetKind.Other)
                .Select(a => new { route = a.Route, kind = a.Kind.ToString(), target = a.Target })
                .ToList(),
            animations = candidates,
            suggested,
            slots = SpineSkeletonReader.StandardSlots
                .Select(s => new { key = s.Key, label = s.Label, description = s.Description })
                .ToList(),
        };
    }

    private static string ReadBody(HttpListenerContext ctx)    {
        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 写一个 JSON 响应。
    ///
    /// 名字里的 "Try" 是认真的：这个 helper 在**出错处理里**也会被调用，所以它自己绝不能
    /// 再抛异常。曾经它只挡 HttpListenerException，而当时刚才那条响应已经因为写入超出
    /// Content-Length 坏掉了，于是这里的第二次写又抛 ProtocolViolationException，
    /// 一路冒到 Run 之外，整个编辑器进程就没了——一个畸形请求换来一次崩溃。
    /// 现在任何写入失败都只丢掉这条连接（Abort），进程继续服务其它请求。
    /// </summary>
    private static void TryWriteJson(HttpListenerContext ctx, int status, object payload)
    {
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;

            // HEAD 与静态文件同理：只要头部，不能写 body。
            if (ctx.Request.HttpMethod != "HEAD")
            {
                ctx.Response.OutputStream.Write(bytes);
            }

            ctx.Response.OutputStream.Close();
        }
        catch (Exception)
        {
            // 响应已经坏了（或者客户端跑了）。把这条连接掐掉，别让它拖垮进程。
            try
            {
                ctx.Response.Abort();
            }
            catch (Exception)
            {
                // Abort 都失败就没什么可做的了。
            }
        }
    }

    private void ServeStatic(HttpListenerContext ctx, string path)
    {
        if (path is "/" or "")
        {
            path = "/index.html";
        }

        // Extracted game art is served from the workspace cache rather than the UI folder.
        if (path.StartsWith("/presenter/", StringComparison.Ordinal))
        {
            ServePresenterAsset(ctx, path);
            return;
        }

        // 语言资源是 ui/ 的兄弟目录，走各自的根目录与别名。
        string root = _uiDir;
        string relative = path.TrimStart('/');

        if (relative.StartsWith("language/", StringComparison.Ordinal))
        {
            root = _languageDir;
            relative = relative["language/".Length..];
        }

        // Keep requests inside the directory being served.
        if (relative.Split('/').Any(seg => seg == ".."))
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
            return;
        }

        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        byte[] bytes = File.ReadAllBytes(full);
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = ContentTypeFor(full);
        ctx.Response.ContentLength64 = bytes.Length;

        // 界面文件一律不缓存。
        //
        // 编辑器是本地工具，重新生成后刷新页面就该看到新版本。没有缓存头时浏览器会用
        // 启发式缓存（按 Last-Modified 推算），于是改了 app.js / styles.css 后用户拿到的
        // 还是旧文件，看起来像"改了不起作用"。图片仍然允许缓存（见 WriteImage）。
        ctx.Response.Headers["Cache-Control"] = "no-store, must-revalidate";

        // HEAD 请求（curl -I、某些健康检查、浏览器的预检）只要头部。
        // 照常写 body 的话，.NET 会因为"写入超过声明的 Content-Length"抛
        // ProtocolViolationException——而那个异常会把整个编辑器进程带下去。
        if (ctx.Request.HttpMethod != "HEAD")
        {
            ctx.Response.OutputStream.Write(bytes);
        }

        ctx.Response.OutputStream.Close();
    }

    /// <summary>Serves one extracted presenter asset, confined to the cache directory.</summary>
    private void ServePresenterAsset(HttpListenerContext ctx, string path)
    {
        if (_assetCacheDir is null)
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        string name = path["/presenter/".Length..];

        // Only a bare file name is accepted, so the request cannot escape the cache.
        if (name.Contains('/') || name.Contains("..") || name.Contains('\\'))
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
            return;
        }

        string full = Path.Combine(_assetCacheDir, name);
        if (!File.Exists(full))
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        byte[] bytes = File.ReadAllBytes(full);
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "image/png";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes);
        ctx.Response.OutputStream.Close();
    }

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "application/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        // Card art is WebP, and it must be served as an image type: with
        // application/octet-stream the browser treats it as a download and the card face
        // shows an empty frame.
        ".webp" => "image/webp",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        _ => "application/octet-stream",
    };

    /// <summary>工程的信息文件。工程文件夹认它，界面上的"工程路径"也是它。</summary>
    private string ProjectPath => _workspace.Layout.AboutFile;

    /// <summary>
    /// 把当前工程落到磁盘：About.xml、语言、Create/ 里的定义，以及用到的素材。
    ///
    /// **不含源码**：生成 C# 要跑一遍代码生成器，而这一步在很多地方只是"别丢掉改动"
    /// （切工程前的兜底、关页面前的 flush）。要连源码一起写的走 <see cref="SaveProjectBundle"/>。
    /// </summary>
    private void SaveProject()
    {
        if (!MayWriteWorkspace(out string why))
        {
            Log($"跳过保存：{why}");
            return;
        }

        _workspace.Save(_project, null, ToPng, Log);
    }

    /// <summary>
    /// 现在能不能把当前这份工程写进工作目录？
    ///
    /// 两道判断，缺一不可：
    ///   1. **已经认领了工程文件夹**（新建 / 打开 / 另存为过）——那就随便写，写的就是它；
    ///   2. 还没认领时，只有**空文件夹**能写。里面若已经有工程（About/About.xml 在），
    ///      说明那是别人的（或用户上次那份）工程，而我们现在手里只有一份空工程——
    ///      写进去等于把它按"实体都删光了"清空。
    ///
    /// 这条闸是为了"启动时不载入工程"这个设计：工作目录还指着上次的工程，
    /// 而手里的工程是空的，任何一次"先保存再做别的"都会毁掉那边。这个坑踩过两次。
    /// </summary>
    private bool MayWriteWorkspace(out string reason)
    {
        if (_projectBound || !Workspace.LooksLikeProject(_workspace.Root))
        {
            reason = string.Empty;
            return true;
        }

        reason =
            $"当前还没有打开工程，而 {_workspace.Root} 里已经有一份工程——直接保存会把它清空。" +
            "请先在「项目」页里打开它，或另存为一个新工程。";

        return false;
    }

    /// <summary>
    /// 把一张图转成 PNG（封面用）；Godot 不在时返回 null，由调用方决定怎么降级。
    /// </summary>
    private string? ToPng(string path)
    {
        if (_png is not { IsAvailable: true })
        {
            return null;
        }

        try
        {
            return _png.Convert(path);
        }
        catch (Exception ex)
        {
            Log($"[warn] 封面转 PNG 失败：{ex.Message}");
            return null;
        }
    }

    /* ---------------------------------------------------------------- 路径设置 */

    /// <summary>
    /// 改设置。返回新的路径与一句提示。
    ///
    /// 有三件事要连带做，否则界面上改了路径、实际行为却还是旧的：
    ///   1. 卡面目录变了 → 重建 CardArtLibrary，并规整工程里的卡图引用
    ///   2. 游戏目录变了 → 重建内容目录、术语表、句式名称、卡面素材提取器
    ///   3. 皮肤目录只是被扫描的根之一，下次请求 /api/skins 才用得到，不用重建
    ///
    /// 卡图引用规整是必需的：引用是"相对 cardart"的，换了 cardart 目录之后，
    /// 原来存的文件名要重新去新目录里找（找不到就保留原样，界面会显示"未设置立绘"）。
    /// </summary>
    private object ApplySettings(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement;

        var messages = new List<string>();

        string? game = Text(root, "gameDirectory");
        string? export = Text(root, "exportDirectory");
        string? cardArt = Text(root, "cardArtDirectory");
        string? skins = Text(root, "skinDirectory");
        string? projectsRoot = Text(root, "projectsRoot");

        bool gameChanged = false;

        if (game is not null && !string.Equals(game, _settings.GameDirectory, StringComparison.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(game))
            {
                return new { ok = false, error = $"游戏目录不存在：{game}" };
            }

            _settings.GameDirectory = game;
            gameChanged = true;
        }

        if (export is not null)
        {
            _settings.ExportDirectory = export;
        }

        if (skins is not null)
        {
            _settings.SkinDirectory = skins;
        }

        // 新建项目的父目录。允许留空（那就回退到程序旁边的 projects/）。
        if (projectsRoot is not null)
        {
            _settings.ProjectsRoot = projectsRoot.Trim();
        }

        bool libraryChanged = false;

        // 卡面路径 = 素材库（用户攒图的地方），不是工程的 cardart。
        // 工程自己的 cardart 跟着工程走，不在这里改。
        if (cardArt is not null &&
            !string.Equals(cardArt, _workspace.LibraryDirectory ?? "", StringComparison.OrdinalIgnoreCase))
        {
            _settings.CardArtDirectory = string.IsNullOrWhiteSpace(cardArt)
                ? ""
                : Path.GetFullPath(cardArt);

            _workspace.SetLibrary(_settings.CardArtDirectory);
            libraryChanged = true;
        }

        _settings.Save(SettingsPath);
        messages.Add($"设置已保存到 {SettingsPath}");

        if (libraryChanged)
        {
            _artLibrary = new ModForge.Core.Gfx.CardArtLibrary(_gameDir, _workspace.LibraryDirectory);

            // 换了素材库之后不必改引用：引用是相对各自文件夹的路径，解析时先看工程自己的
            // 文件夹、再看素材库，所以指向新库里的图仍然找得到。下次保存才会把它们收进工程。
            messages.Add(string.IsNullOrWhiteSpace(_settings.CardArtDirectory)
                ? "素材库路径已清空"
                : $"素材库已切换：{_settings.CardArtDirectory}");
        }

        if (gameChanged)
        {
            ReloadGameContent(messages);
        }

        Log(messages[^1]);

        return new
        {
            ok = true,
            messages,
            settings = _settings.Describe(),
            projectPath = _workspace.Layout.AboutFile,
            sourcesDirectory = _workspace.Layout.SourceDirectory,
        };
    }

    /// <summary>
    /// 游戏目录变了之后，把所有"从游戏里读东西"的对象重建一遍。
    ///
    /// 这些对象都在启动时构造并缓存了游戏内容，不重建的话改完路径界面上还是旧数据——
    /// 看起来像"设置没生效"。
    /// </summary>
    private void ReloadGameContent(List<string> messages)
    {
        _gameDir = _settings.GameDirectory;

        try
        {
            _catalog = VanillaCatalog.Build(_gameDir);
            messages.Add($"已重新读取游戏内容：{_catalog.Entries.Count} 项（{_catalog.GameVersion}）");
        }
        catch (Exception ex)
        {
            _catalog = null;
            messages.Add($"[警告] 游戏内容读取失败：{ex.Message}");
        }

        _terms = new ModForge.Core.GameTerms(_gameDir);
        DescriptionModule.Names = new VanillaNames(_gameDir);
        _artLibrary = new ModForge.Core.Gfx.CardArtLibrary(_gameDir, _workspace.LibraryDirectory);

        _assetCacheDir = _workspace.PresenterDirectory;
        _assets = new ModForge.Core.Gfx.AssetExtractor(
            _gameDir,
            _webp?.GodotExecutable ?? "",
            Path.Combine(FindHelperScriptDirectory(), "export_assets.gd"));

        if (_assets.IsAvailable)
        {
            _assets.EnsureExtracted(_assetCacheDir, Log);
        }

        Log(messages[^1]);
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /* ---------------------------------------------------------------- 工程读写 */

    /// <summary>
    /// 保存工程：整套文件夹（About / Language / Create / Cards…）加上用到的素材，
    /// 再把生成的 C# 源码写进 <c>Source/</c>。
    ///
    /// 这样一份工程可以拷给别人、可以用编辑器再打开、也可以直接翻生成的 C# 与每个实体的 XML。
    /// **它不是可加载的 mod**：那个要经过编译与打包，走"导出"。
    /// </summary>
    private object SaveProjectBundle()
    {
        if (!MayWriteWorkspace(out string why))
        {
            Log($"跳过保存：{why}");
            return new { ok = false, error = why };
        }

        try
        {
            IReadOnlyDictionary<string, string> sources =
                new ModForge.Core.Generate.CodeGenerator(_project).GenerateAll();

            SaveReport report = _workspace.Save(_project, sources, ToPng, Log);

            // 收图/清图之后引用变了，界面要重新拉一次工程。
            var notes = new List<string> { $"已保存到 {_workspace.Root}" };

            if (report.Imported > 0)
            {
                notes.Add($"收进 {report.Imported} 个用到的素材");
            }

            if (report.Removed > 0)
            {
                notes.Add($"清掉 {report.Removed} 个不再引用的素材");
            }

            notes.Add($"源码在 {_workspace.Layout.SourceDirectory}");

            return new
            {
                ok = true,
                project = System.Text.Json.JsonSerializer.SerializeToElement(_project, ProjectJson),
                projectPath = ProjectPath,
                projectRoot = _workspace.Root,
                cardsDirectory = _workspace.Layout.CardsDirectory,
                sourcesDirectory = _workspace.Layout.SourceDirectory,
                modDirectory = _workspace.ModDirectory,
                importedArt = report.Imported,
                removedArt = report.Removed,
                messages = notes,
            };
        }
        catch (Exception ex)
        {
            Log($"保存失败：{ex.Message}");
            return new { ok = false, error = ex.Message };
        }
    }

    /// <summary>
    /// 另存为：在给定父目录下建一个以工程名命名的新文件夹，复制过去并切过去。
    ///
    /// 父目录由界面用原生对话框选（浏览器给不了路径），也可以手输。
    /// </summary>
    private object SaveProjectAs(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        string? parent = Text(doc.RootElement, "parentDirectory");
        string? name = Text(doc.RootElement, "name");
        bool overwrite = doc.RootElement.TryGetProperty("overwrite", out JsonElement o) &&
                         o.ValueKind == JsonValueKind.True;

        if (string.IsNullOrWhiteSpace(parent))
        {
            return new { ok = false, error = "请选择父目录。" };
        }

        try
        {
            string folderName = string.IsNullOrWhiteSpace(name)
                ? Workspace.FolderNameFor(_project)
                : name.Trim();

            string target;

            if (_projectBound)
            {
                // 先把当前编辑内容落到磁盘，再复制——否则拷的是上一次保存的版本。
                SaveProjectBundle();

                target = _workspace.SaveAs(parent, folderName, overwrite, Log);
            }
            else
            {
                // 还没认领文件夹（启动时那份空工程）：另存为就是把它**写**到新位置。
                //
                // 不能走"复制工作目录"那条路：工作目录里现在放着的是别人的（或用户上次那份）
                // 工程，复制它等于把那份工程另存了一份，而用户手里这份空工程根本没被保存。
                target = Workspace.UniqueProjectFolder(parent, folderName, overwrite);
                _workspace.Retarget(target);
                _projectBound = true;

                SaveProjectBundle();
                Log($"工程已另存到 {target}");
            }

            _settings.ProjectDirectory = _workspace.Root;
            _settings.Save(SettingsPath);

            _artLibrary = new ModForge.Core.Gfx.CardArtLibrary(_gameDir, _workspace.LibraryDirectory);

            return new
            {
                ok = true,
                projectPath = ProjectPath,
                projectRoot = _workspace.Root,
                cardsDirectory = _workspace.Layout.CardsDirectory,
                sourcesDirectory = _workspace.Layout.SourceDirectory,
                modDirectory = _workspace.ModDirectory,
                settings = _settings.Describe(),
                messages = new[] { $"已另存到 {target}" },
            };
        }
        catch (Exception ex)
        {
            Log($"另存失败：{ex.Message}");
            return new { ok = false, error = ex.Message };
        }
    }

    /// <summary>打开另一个工程文件夹。</summary>
    private object OpenProject(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        string? dir = Text(doc.RootElement, "directory");

        if (string.IsNullOrWhiteSpace(dir))
        {
            return new { ok = false, error = "请选择工程文件夹。" };
        }

        string full = Path.GetFullPath(dir);

        // 选到的是工程文件夹本身，或者它的上一层（用户可能选了项目的容器目录）。
        if (!Workspace.LooksLikeProject(full))
        {
            string? nested = TryFindProjectInside(full);

            if (nested is null)
            {
                return new { ok = false, error = $"{full} 里没有 About/About.xml，不是工程文件夹。" };
            }

            full = nested;
        }

        try
        {
            // 先把当前这份存好，避免"打开另一个工程"顺手丢掉还没保存的编辑。
            // （没认领过文件夹时这一步会跳过——那时手里是空工程，往哪写都是破坏。）
            SaveProjectBundle();

            // 读之前先切过去：素材引用是相对各自文件夹的路径，读出来之后要靠新位置解析。
            _workspace.Retarget(full);

            var loaded = ModForge.Core.Modules.Files.ProjectStore.Load(_workspace.Layout, Log);
            _project = loaded.Project;
            _projectBound = true;

            // 读取时发现的问题（写错的字段、读不动的效果、整个坏掉的文件）在这里交代给界面：
            // 工程照常打开（局部损坏不影响其余），但每一处都列出来——不弹的话，
            // "我明明写了却没生效"就成了一场猜谜。
            var issues = loaded.Issues
                .Select(i => new
                {
                    file = Path.GetFileName(i.File),
                    where = i.Where,
                    message = i.Message,
                    fatal = i.Fatal,
                })
                .ToList();

            if (issues.Count > 0)
            {
                Log($"打开时发现 {issues.Count} 处问题（已按兜底值继续）：");

                foreach (var issue in issues.Take(40))
                {
                    Log($"  [读取] {issue.file}（{issue.where}）: {issue.message}");
                }
            }

            _settings.ProjectDirectory = _workspace.Root;
            _settings.Save(SettingsPath);

            _artLibrary = new ModForge.Core.Gfx.CardArtLibrary(_gameDir, _workspace.LibraryDirectory);

            Log($"已打开工程 {_workspace.Root}");

            return new
            {
                ok = true,
                project = System.Text.Json.JsonSerializer.SerializeToElement(_project, ProjectJson),
                projectPath = ProjectPath,
                projectRoot = _workspace.Root,
                cardsDirectory = _workspace.Layout.CardsDirectory,
                sourcesDirectory = _workspace.Layout.SourceDirectory,
                modDirectory = _workspace.ModDirectory,
                settings = _settings.Describe(),
                issues,
                messages = new[] { $"已打开 {_workspace.Root}" },
            };
        }
        catch (Exception ex)
        {
            Log($"打开工程失败：{ex.Message}");
            return new { ok = false, error = ex.Message };
        }
    }

    /// <summary>
    /// 用户选了一个容器目录时，往里找一层有没有工程。
    ///
    /// 这是常见操作失误：工程放在 <c>D:\Mods\我的工程\</c>，用户把 <c>D:\Mods</c> 选上了。
    /// 只找一层，不递归——递归下去可能在无关目录里翻出别的工程。
    /// </summary>
    private static string? TryFindProjectInside(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            return Directory
                .EnumerateDirectories(directory)
                .FirstOrDefault(Workspace.LooksLikeProject);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// 新建工程：在给定父目录下建一个以工程名命名的文件夹，里面放一份空工程。
    ///
    /// 父目录与名字都由界面给（父目录走原生对话框）。
    /// </summary>
    private object NewProject(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        string? parent = Text(doc.RootElement, "parentDirectory");
        string? name = Text(doc.RootElement, "name");
        string? id = Text(doc.RootElement, "id");
        bool overwrite = doc.RootElement.TryGetProperty("overwrite", out JsonElement o) &&
                         o.ValueKind == JsonValueKind.True;

        if (string.IsNullOrWhiteSpace(parent))
        {
            return new { ok = false, error = "请选择父目录。" };
        }

        try
        {
            // 新建工程也是**空**工程：以前它拿样例工程当模板（连带几张演示卡），
            // 而工程文件只是测试素材，自动塞样例内容反而要人先删干净。
            _project = new ModProject();

            if (!string.IsNullOrWhiteSpace(name))
            {
                _project.Name = name.Trim();
            }

            if (!string.IsNullOrWhiteSpace(id))
            {
                _project.Id = id.Trim();
            }

            string folderName = Workspace.FolderNameFor(_project);
            string target = Workspace.UniqueProjectFolder(parent, folderName, overwrite);

            // 从现在起，这份工程认领了这个文件夹：之后的保存就往这里写。
            _workspace.Retarget(target);
            _projectBound = true;

            IReadOnlyDictionary<string, string> sources =
                new ModForge.Core.Generate.CodeGenerator(_project).GenerateAll();
            _workspace.Save(_project, sources, ToPng, Log);

            _settings.ProjectDirectory = _workspace.Root;
            _settings.Save(SettingsPath);

            _artLibrary = new ModForge.Core.Gfx.CardArtLibrary(_gameDir, _workspace.LibraryDirectory);

            Log($"已新建工程 {target}");

            return new
            {
                ok = true,
                project = System.Text.Json.JsonSerializer.SerializeToElement(_project, ProjectJson),
                projectPath = ProjectPath,
                projectRoot = _workspace.Root,
                cardsDirectory = _workspace.Layout.CardsDirectory,
                sourcesDirectory = _workspace.Layout.SourceDirectory,
                modDirectory = _workspace.ModDirectory,
                settings = _settings.Describe(),
                messages = new[] { $"已新建工程 {target}" },
            };
        }
        catch (Exception ex)
        {
            Log($"新建工程失败：{ex.Message}");
            return new { ok = false, error = ex.Message };
        }
    }

    /* ---------------------------------------------------------------- 原生选择对话框 */

    /// <summary>
    /// 新建/另存为的目标文件夹是否已经存在。
    ///
    /// 界面拿它决定要不要弹"是否覆盖"的确认框。放在服务端算，是因为"项目文件夹叫什么名"
    /// 这件事只有服务端知道（名字由项目名推导，见 <see cref="Workspace.FolderNameFor"/>），
    /// 界面自己拼一遍迟早会与服务端不一致。
    /// </summary>
    private object ProjectFolderExists(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        string? parent = Text(doc.RootElement, "parentDirectory");
        string? name = Text(doc.RootElement, "name");

        if (string.IsNullOrWhiteSpace(parent))
        {
            return new { ok = false, error = "请选择父目录。" };
        }

        string folderName = string.IsNullOrWhiteSpace(name)
            ? Workspace.FolderNameFor(_project)
            : name.Trim();

        string target = Path.Combine(Path.GetFullPath(parent), folderName);

        // 只看文件夹在不在；里面有没有 project.json 由打开流程判断。
        return new
        {
            ok = true,
            exists = Directory.Exists(target),
            directory = target,
        };
    }

    /// <summary>
    /// 在文件资源管理器里打开一个文件夹。
    ///
    /// 空路径表示"当前项目文件夹"；路径不存在就不做（避免弹出一个空窗口）。
    /// </summary>
    private object RevealFolder(string body)
    {
        using JsonDocument doc = JsonDocument.Parse(body);
        string? directory = Text(doc.RootElement, "directory");

        string target = string.IsNullOrWhiteSpace(directory)
            ? _workspace.Root
            : Path.GetFullPath(directory);

        if (!Directory.Exists(target))
        {
            return new { ok = false, error = $"文件夹不存在：{target}" };
        }

        try
        {
            // explorer.exe 只接受一个参数；用 ArgumentList 而不是拼字符串，
            // 路径里的引号/空格就不会被当成额外的开关。
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = false,
            };

            startInfo.ArgumentList.Add(target);
            System.Diagnostics.Process.Start(startInfo);

            Log($"已在资源管理器中打开 {target}");
            return new { ok = true, path = target };
        }
        catch (Exception ex)
        {
            Log($"打开资源管理器失败：{ex.Message}");
            return new { ok = false, error = ex.Message };
        }
    }

    /// <summary>
    /// 在后台弹文件夹选择对话框，完成后把结果写进响应。
    ///
    /// 请求体在这里（请求线程上）先读掉：HttpListener 的输入流只在处理该请求期间有效，
    /// 等对话框关掉再读就拿不到了。
    ///
    /// 选择器只读入参、不碰 _project 之类共享状态，所以放到另一个线程上没有竞态问题。
    /// </summary>
    private void BeginPick(HttpListenerContext ctx, string body, bool image = false)
    {
        string? startAt = null;
        string? title = null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;

            startAt = Text(root, "startAt");
            title = Text(root, "title");
        }
        catch (JsonException)
        {
            // 没有请求体（或坏掉了）也能照样弹，只是没有起点目录。
        }

        _ = Task.Run(() =>
        {
            FolderPicker.PickResult result;

            try
            {
                result = image
                    ? FolderPicker.PickImage(startAt, title ?? "选择图片")
                    : FolderPicker.PickFolder(startAt, title ?? "选择文件夹");
            }
            catch (Exception ex)
            {
                result = new FolderPicker.PickResult(false, null, ex.Message);
            }

            if (!result.Ok)
            {
                Log($"选择对话框失败：{result.Error}");
                TryWriteJson(ctx, 200, new { ok = false, error = result.Error });
                return;
            }

            // 取消：ok=true 但 path 为空，界面据此什么也不做。
            TryWriteJson(ctx, 200, new { ok = true, path = result.Path, cancelled = result.Path is null });
        });
    }

    private void Log(string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss}  {message}";

        // 选择对话框在后台线程上跑，也可能会调到这里，所以加锁。
        lock (_log)
        {
            _log.Add(line);
        }

        Console.WriteLine(line);
    }

    /// <summary>最近若干条日志。加锁读，因为日志可能正被后台线程写入。</summary>
    private List<string> RecentLog(int count)
    {
        lock (_log)
        {
            return _log.TakeLast(count).ToList();
        }
    }

    public void Dispose() => _listener.Close();
}
