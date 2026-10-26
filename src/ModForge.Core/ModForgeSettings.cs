using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModForge.Core;

/// <summary>
/// 编辑器的路径设置，存在程序根目录的 settings.json 里。
///
/// 这四个路径是程序与外界打交道的全部出入口，所以单独拿出来让用户能改：
///
///   GameDirectory   游戏本体（读原版卡图、本地化、程序集）
///   ExportDirectory 导出的可加载 mod 写到哪
///   CardArtDirectory 散装卡图放在哪（工程的 cardart 文件夹）
///   SkinDirectory   额外扫描皮肤包的目录（游戏 mod 目录与创意工坊之外）
///
/// 为什么不把这些塞进 project.json：它们描述的是"这台机器上东西在哪"，
/// 而工程描述的是"要做成什么"。换台机器、换个 Steam 库，工程本身没变，
/// 所以两者分开存——工程可以原样分享，路径各自配。
/// </summary>
public sealed class ModForgeSettings
{
    /// <summary>Slay the Spire 2 安装目录。</summary>
    public string GameDirectory { get; set; } = "";

    /// <summary>导出的可加载 mod 写到哪个目录。</summary>
    public string ExportDirectory { get; set; } = "";

    /// <summary>
    /// 素材库：用户自己攒图的地方，编辑器只读不写。
    /// 保存工程时只把真正引用到的图收进工程的 cardart，所以库里的图随便放。
    /// </summary>
    public string CardArtDirectory { get; set; } = "";

    /// <summary>额外扫描皮肤包的目录，可以为空。</summary>
    public string SkinDirectory { get; set; } = "";

    /// <summary>当前打开的工程文件夹（里面是 project.json + cardart + sources）。</summary>
    public string ProjectDirectory { get; set; } = "";

    /// <summary>
    /// 新建/另存为时的默认父目录：工程文件夹都建在它下面，以工程名命名。
    /// </summary>
    public string ProjectsRoot { get; set; } = "";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static ModForgeSettings FromJson(string json) =>
        JsonSerializer.Deserialize<ModForgeSettings>(json, Options) ?? new ModForgeSettings();

    /// <summary>
    /// 读设置。文件不存在或读坏了都返回一份带默认值的设置，而不是抛异常——
    /// 设置只是路径，缺了就用默认的，不该让程序启动不起来。
    /// </summary>
    public static ModForgeSettings Load(string path, Action<string>? log = null)
    {
        if (!File.Exists(path))
        {
            return new ModForgeSettings();
        }

        try
        {
            return FromJson(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            log?.Invoke($"设置文件读取失败，改用默认值：{ex.Message}");
            return new ModForgeSettings();
        }
    }

    public void Save(string path) => File.WriteAllText(path, ToJson());

    /// <summary>
    /// 用默认值补上没填的项。默认值都指向程序根目录下的文件夹，这样开箱即用。
    /// </summary>
    public void ApplyDefaults(string programRoot, string? gameDir = null)
    {
        if (string.IsNullOrWhiteSpace(GameDirectory))
        {
            GameDirectory = gameDir ?? "";
        }

        if (string.IsNullOrWhiteSpace(ProjectsRoot))
        {
            ProjectsRoot = Path.Combine(programRoot, "projects");
        }

        if (string.IsNullOrWhiteSpace(CardArtDirectory))
        {
            // 素材库默认与工程平级，这样它不属于任何一份工程，也就不会被"保存"当成工程内容。
            CardArtDirectory = Path.Combine(programRoot, "cardart");
        }

        if (string.IsNullOrWhiteSpace(ExportDirectory))
        {
            ExportDirectory = Path.Combine(programRoot, "build");
        }
    }

    /// <summary>一份用于界面显示的设置（路径一律给绝对值，方便用户核对）。</summary>
    public object Describe()
    {
        return new
        {
            gameDirectory = GameDirectory,
            exportDirectory = ExportDirectory,
            cardArtDirectory = CardArtDirectory,
            skinDirectory = SkinDirectory,
            projectDirectory = ProjectDirectory,
            projectsRoot = ProjectsRoot,
        };
    }
}
