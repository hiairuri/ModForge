using ModForge.Core.Model;
using ModForge.Core.Modules.Files;

namespace ModForge.Core;

/// <summary>
/// 一份打开的工程：它的文件夹布局、用户的素材库、以及这台机器上的缓存。
///
/// 文件夹布局本身在 <see cref="ProjectLayout"/> 里（哪八个文件夹、哪个文件放哪），
/// 定义怎么读写、素材怎么进出在 <see cref="ProjectStore"/> 与 <see cref="ProjectAssets"/>。
/// 这一层只做两件事：把三者按"当前打开的是哪个工程"接起来，以及**解析素材引用**。
///
/// 素材引用存的是相对各自文件夹的路径（卡面相对 <c>Cards/</c>、状态图标相对 <c>Textures/</c>……）。
/// 解析时先看工程自己的文件夹、再看素材库——所以编辑期间可以引用库里的图，保存时才收进工程。
/// 相对引用是"工程文件夹搬到哪都能打开"的关键。
///
/// 素材库（<see cref="LibraryDirectory"/>，设置里的"卡面路径"）是用户自己攒图的地方，
/// 编辑器**从不删它**；缓存（presenter/data/build）是这台机器的，不属于任何一份工程。
/// </summary>
public sealed class Workspace
{
    private ProjectLayout _layout;

    /// <summary>
    /// 打开一份工程文件夹。
    ///
    /// <paramref name="libraryDirectory"/> 是可选的素材库目录；
    /// <paramref name="cacheRoot"/> 是缓存根目录（presenter/data/build），不属于工程。
    /// </summary>
    public Workspace(string root, string? libraryDirectory = null, string? cacheRoot = null)
    {
        _layout = new ProjectLayout(root);
        LibraryDirectory = Clean(libraryDirectory);
        CacheRoot = string.IsNullOrWhiteSpace(cacheRoot) ? _layout.Root : Path.GetFullPath(cacheRoot);
    }

    /// <summary>工程文件夹布局。</summary>
    public ProjectLayout Layout => _layout;

    /// <summary>工程文件夹。</summary>
    public string Root => _layout.Root;

    /// <summary>
    /// 素材库（设置里的"卡面路径"）。用户攒图的地方，编辑器只读不写、更不删。未配置时为 null。
    /// </summary>
    public string? LibraryDirectory { get; private set; }

    /// <summary>
    /// 缓存根目录（presenter/data/build）。
    ///
    /// **不属于任何一份工程**：卡面素材是游戏的、数据快照是这台机器的，与"正在做哪个 mod"
    /// 无关。放进工程文件夹的话，换一个工程就要重新提取一遍卡面素材，而且工程文件夹里会多出
    /// 几个与 mod 无关的文件夹（拷给别人时也白带过去）。
    /// </summary>
    public string CacheRoot { get; private set; }

    public string PresenterDirectory => Path.Combine(CacheRoot, "presenter");

    public string DataDirectory => Path.Combine(CacheRoot, "data");

    public string BuildDirectory => Path.Combine(CacheRoot, "build");

    /// <summary>导出产物的落点：工程的 <c>Mod/</c> 文件夹。</summary>
    public string ModDirectory => _layout.ModDirectory;

    /* ------------------------------------------------------------------ 目录 */

    /// <summary>建立工程该有的八个文件夹（幂等）。</summary>
    public void EnsureCreated() => _layout.EnsureCreated();

    /// <summary>
    /// 建立运行期需要的全部目录，包括缓存。会写盘，只应在启动时与导出前调用。
    /// </summary>
    public void EnsureDirectories()
    {
        _layout.EnsureCreated();
        Directory.CreateDirectory(PresenterDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BuildDirectory);
    }

    /// <summary>
    /// 换到别的工程文件夹（"打开"与"另存为"都走这里）。
    ///
    /// 只改指向并建好工程该有的文件夹，不碰磁盘上已有的东西。
    /// </summary>
    public void Retarget(string root, string? libraryDirectory = null)
    {
        _layout = new ProjectLayout(root);

        if (!string.IsNullOrWhiteSpace(libraryDirectory))
        {
            LibraryDirectory = Clean(libraryDirectory);
        }

        // CacheRoot 不动：缓存是这台机器的，换工程不影响它。
        _layout.EnsureCreated();
    }

    /// <summary>设缓存根目录（程序启动时指到程序旁边）。</summary>
    public void SetCacheRoot(string cacheRoot)
    {
        CacheRoot = Path.GetFullPath(cacheRoot);
    }

    /// <summary>换素材库目录（设置页改"卡面路径"时用）。</summary>
    public void SetLibrary(string? libraryDirectory)
    {
        LibraryDirectory = Clean(libraryDirectory);
    }

    /// <summary>这个目录看起来是不是一份工程文件夹（认 About/About.xml）。</summary>
    public static bool LooksLikeProject(string directory) => ProjectLayout.LooksLikeProject(directory);

    /// <summary>把工程名变成能当文件夹名的形式。</summary>
    public static string FolderNameFor(ModProject project) => ProjectLayout.FolderNameFor(project);

    /// <summary>在父目录下给工程算一个不冲突的文件夹路径。</summary>
    public static string UniqueProjectFolder(string parent, string folderName, bool overwrite) =>
        ProjectLayout.UniqueProjectFolder(parent, folderName, overwrite);

    /* ------------------------------------------------------------------ 保存 */

    /// <summary>
    /// 保存工程：把用到的素材收进工程、清掉不再引用的、写出 About / Language / Create / Source。
    ///
    /// 返回（收进的素材数，清掉的素材数，写出的文件数）。
    /// </summary>
    public SaveReport Save(
        ModProject project,
        IReadOnlyDictionary<string, string>? sources = null,
        Func<string, string?>? toPng = null,
        Action<string>? log = null) =>
        ProjectStore.Save(project, _layout, ResolveAsset, sources, toPng, log);

    /// <summary>
    /// 把工程另存到 <paramref name="parentDirectory"/> 下的新文件夹并切过去。
    ///
    /// 文件夹名由调用方给定（一般是工程名，见 <see cref="FolderNameFor"/>）。
    /// **不复制 <c>Mod/</c>**：那是导出产物，重新导出一次就有，白拷一份可能几十 MB。
    /// </summary>
    public string SaveAs(string parentDirectory, string folderName, bool overwrite, Action<string>? log = null)
    {
        string target = ProjectLayout.UniqueProjectFolder(parentDirectory, folderName, overwrite);

        if (string.Equals(target, Root, StringComparison.OrdinalIgnoreCase))
        {
            log?.Invoke("目标就是当前工程文件夹，无需另存。");
            return target;
        }

        Directory.CreateDirectory(target);

        int copied = CopyTree(Root, target, skip: _layout.ModDirectory);

        log?.Invoke($"工程已另存到 {target}（{copied} 个文件）");

        // 切到新位置，之后的保存都写在这里。
        Retarget(target);
        return target;
    }

    /// <summary>递归复制目录，跳过 <paramref name="skip"/> 那棵子树。返回复制了多少个文件。</summary>
    private static int CopyTree(string from, string to, string? skip = null)
    {
        if (!Directory.Exists(from))
        {
            return 0;
        }

        string skipRoot = skip is null ? "" : Path.GetFullPath(skip) + Path.DirectorySeparatorChar;
        int copied = 0;

        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            if (skipRoot.Length > 0 &&
                Path.GetFullPath(file).StartsWith(skipRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(from, file);
            string destination = Path.Combine(to, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            copied++;
        }

        return copied;
    }

    /* ------------------------------------------------------------------ 素材解析 */

    /// <summary>
    /// 把一个素材引用解析成磁盘路径。
    ///
    /// 查找顺序：工程对应的文件夹（卡面找 <c>Cards/</c>、图标找 <c>Textures/</c>……）
    /// → 素材库。两处都找不到时返回 null（界面显示"找不到图"）。
    /// 绝对路径直接当磁盘路径用（编辑期间引用了库外的文件时会是这样）。
    /// </summary>
    /// <param name="reference">工程里存的引用。可以为 null（表示没设）。</param>
    /// <param name="directory">这个引用属于工程里的哪个文件夹。</param>
    public string? ResolveAsset(string? reference, string directory)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        if (Path.IsPathRooted(reference))
        {
            return File.Exists(reference) ? reference : null;
        }

        string relative = reference.Replace('/', Path.DirectorySeparatorChar);

        // 引用里可能带着文件夹名（手工写成 "Cards/xxx.png"），去掉以免重复拼。
        string prefix = Path.GetFileName(directory) + Path.DirectorySeparatorChar;

        if (relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            relative = relative[prefix.Length..];
        }

        string inProject = Path.Combine(directory, relative);

        if (ProjectLayout.IsInside(directory, inProject) && File.Exists(inProject))
        {
            return inProject;
        }

        if (LibraryDirectory is not null)
        {
            string inLibrary = Path.Combine(LibraryDirectory, relative);

            if (ProjectLayout.IsInside(LibraryDirectory, inLibrary) && File.Exists(inLibrary))
            {
                return inLibrary;
            }
        }

        return null;
    }

    /// <summary>卡图引用（<see cref="CardDef.ArtFile"/>）的磁盘路径。</summary>
    public string? ResolveCardArt(string? artFile) => ResolveAsset(artFile, _layout.CardsDirectory);

    /// <summary>状态图标的磁盘路径。</summary>
    public string? ResolvePowerIcon(string? icon) => ResolveAsset(icon, _layout.TexturesDirectory);

    /// <summary>皮肤源包的磁盘路径。</summary>
    public string? ResolveSkinPack(string? pack) => ResolveAsset(pack, _layout.SkinsDirectory);

    /// <summary>封面的磁盘路径。</summary>
    public string? ResolvePreview(string? name) => ResolveAsset(name, _layout.AboutDirectory);

    /* ------------------------------------------------------------------ 内部 */

    private static string? Clean(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
}
