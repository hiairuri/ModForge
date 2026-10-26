using ModForge.Core.Model;

namespace ModForge.Core.Modules.Files;

/// <summary>
/// 素材进出工程：把用到的图收进来、把不再用的删掉。
///
/// 工程里的贴图引用存的是**相对各自文件夹的路径**（<c>Cards/</c>、<c>Textures/</c>、
/// <c>Skins/</c>、<c>About/</c>），解析时先看工程自己的文件夹、再看用户的素材库——
/// 所以编辑期间可以引用库里的图，保存时才收进工程。相对引用是"工程文件夹搬到哪都能打开"
/// 的关键。
///
/// 与素材库分开：素材库（设置里的"卡面路径"）是用户自己攒图的地方，编辑器**从不删它**；
/// 保存时只把真正引用到的那些复制进工程。所以工程里不会有"试过但没用上"的图，
/// 用户的图库也不会被保存动作清理。
/// </summary>
public static class ProjectAssets
{
    /// <summary>（收进来几张，删掉几张）。</summary>
    public sealed record Report(int Imported, int Removed);

    /// <summary>
    /// 把工程引用到的、但不在工程里的素材复制进来，并把引用改成相对路径。
    ///
    /// <paramref name="resolve"/> 把"引用 + 该找哪个文件夹"变成磁盘路径（找不到给 null）。
    /// 解析不到时**保持原样**，让界面继续显示"找不到图"，而不是悄悄把引用清掉——
    /// 那样用户就不知道自己的图丢了。
    /// </summary>
    /// <param name="toPng">
    /// 把一张图转成 PNG 的办法（给封面用）。给了就保证 <c>About/Preview.png</c> 名副其实；
    /// 没给（或转失败）就按原字节放进去，同时记一条日志——文件名固定这件事不打折。
    /// </param>
    public static Report Import(
        ModProject project,
        ProjectLayout layout,
        Func<string?, string, string?> resolve,
        Func<string, string?>? toPng = null,
        Action<string>? log = null)
    {
        int imported = 0;

        foreach (CardDef card in project.Cards)
        {
            (string? art, bool moved) = Import(card.ArtFile, layout.CardsDirectory, resolve);

            if (moved)
            {
                card.ArtFile = art;
                imported++;
            }
        }

        foreach (PowerDef power in project.Powers)
        {
            (string? icon, bool moved) = Import(power.IconFile, layout.TexturesDirectory, resolve);

            if (moved)
            {
                power.IconFile = icon;
                imported++;
            }
        }

        foreach (SkinDef skin in project.Skins)
        {
            (string? pack, bool moved) = Import(skin.SourcePack, layout.SkinsDirectory, resolve);

            if (moved)
            {
                skin.SourcePack = pack ?? "";
                imported++;
            }
        }

        // 封面固定叫 About/Preview.png（名字写死在 ProjectLayout 里），换封面时旧的那张会被
        // Clean 清掉，不会留下两份。
        (string? preview, bool previewMoved) = ImportCover(project.PreviewFile, layout, resolve, toPng, log);

        if (previewMoved)
        {
            project.PreviewFile = preview;
            imported++;
        }

        if (imported > 0)
        {
            log?.Invoke($"已把 {imported} 个用到的素材收进工程");
        }

        return new Report(imported, 0);
    }

    /// <summary>
    /// 删掉工程素材文件夹里不再被引用的文件。
    ///
    /// 用户的要求是"工程只保存 mod 用到的贴图"：换过图、试过别的素材之后，文件夹里会留下
    /// 没人引用的文件，它们会跟着工程被拷来拷去，也让人以为还在用。这里保存时清掉。
    ///
    /// 只动工程自己的那几个文件夹，**不碰素材库**。
    /// </summary>
    public static int Clean(ModProject project, ProjectLayout layout, Action<string>? log = null)
    {
        var keep = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [layout.CardsDirectory] = Names(project.Cards.Select(c => c.ArtFile)),
            [layout.TexturesDirectory] = Names(project.Powers.Select(p => p.IconFile)),
            [layout.SkinsDirectory] = Names(project.Skins.Select(s => s.SourcePack)),

            // 封面没设时不保留任何图：About/ 里除了 About.xml 与封面，不该有别的东西。
            [layout.AboutDirectory] = project.PreviewFile is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : Names(new[] { ProjectLayout.PreviewFileName }),
        };

        int removed = 0;

        foreach ((string directory, HashSet<string> used) in keep)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                // About/ 里有 About.xml，它不是"素材"，永远不删。
                if (directory.Equals(layout.AboutDirectory, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(file).Equals("About.xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (used.Contains(Relative(directory, file)))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (IOException ex)
                {
                    log?.Invoke($"清理 {Path.GetFileName(file)} 失败：{ex.Message}");
                }
            }
        }

        if (removed > 0)
        {
            log?.Invoke($"清掉了 {removed} 个不再被引用的素材");
        }

        return removed;
    }

    /// <summary>
    /// 收封面：**名字固定** <c>About/Preview.png</c>（见 <see cref="ProjectLayout.PreviewFile"/>）。
    ///
    /// 源不是 PNG 时先转（<paramref name="toPng"/>），转不了就按原字节放进去——
    /// 文件名固定这件事不打折：读取一侧只看这个文件在不在，多一种扩展名就多一处能对不上的地方。
    /// </summary>
    private static (string? Reference, bool Moved) ImportCover(
        string? reference,
        ProjectLayout layout,
        Func<string?, string, string?> resolve,
        Func<string, string?>? toPng,
        Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return (reference, false);
        }

        string? resolved = resolve(reference, layout.AboutDirectory);

        if (resolved is null)
        {
            return (reference, false);
        }

        Directory.CreateDirectory(layout.AboutDirectory);

        string target = layout.PreviewFile;
        string source = resolved;

        if (!Path.GetExtension(resolved).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            string? converted = toPng?.Invoke(resolved);

            if (converted is not null)
            {
                source = converted;
            }
            else
            {
                log?.Invoke(
                    $"[warn] 封面不是 PNG，也没找到 Godot 来转换：已按原样放进 Preview.png" +
                    $"（{Path.GetFileName(resolved)}）。");
            }
        }

        if (!PathsEqual(source, target))
        {
            File.Copy(source, target, overwrite: true);
        }

        // 转出来的中间文件（Preview.png 之外的 .webp/.jpg）不该留在 About/ 里。
        if (!PathsEqual(source, resolved) && File.Exists(source))
        {
            try
            {
                File.Delete(source);
            }
            catch (IOException)
            {
                // 删不掉也没关系：下一次保存的清理会收掉它。
            }
        }

        string name = Path.GetFileName(target);

        return string.Equals(name, reference, StringComparison.Ordinal) ? (reference, false) : (name, true);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 处理一个引用：返回新的引用，以及"是否真的变了"。
    ///
    /// 已经在目标文件夹里的只规范一下写法；在别处的复制进来，重名且内容不同时加后缀。
    /// </summary>
    private static (string? Reference, bool Moved) Import(
        string? reference,
        string directory,
        Func<string?, string, string?> resolve)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return (reference, false);
        }

        string? resolved = resolve(reference, directory);

        if (resolved is null)
        {
            return (reference, false);
        }

        Directory.CreateDirectory(directory);

        if (ProjectLayout.IsInside(directory, resolved) && File.Exists(resolved))
        {
            string inside = Relative(directory, resolved);

            return string.Equals(inside, reference, StringComparison.Ordinal)
                ? (reference, false)
                : (inside, true);
        }

        string? target = UniqueTarget(directory, Path.GetFileName(resolved), resolved);

        return target is null ? (reference, false) : (Relative(directory, target), true);
    }

    /// <summary>在目标文件夹里给一个外来文件找落脚的路径；同名不同内容时加 -2、-3……</summary>
    private static string? UniqueTarget(string directory, string fileName, string sourceFullPath)
    {
        string candidate = Path.Combine(directory, fileName);

        if (!File.Exists(candidate) || SameContent(candidate, sourceFullPath))
        {
            if (!File.Exists(candidate))
            {
                File.Copy(sourceFullPath, candidate, overwrite: false);
            }

            return candidate;
        }

        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        for (int n = 2; n < 1000; n++)
        {
            candidate = Path.Combine(directory, $"{stem}-{n}{extension}");

            if (!File.Exists(candidate) || SameContent(candidate, sourceFullPath))
            {
                if (!File.Exists(candidate))
                {
                    File.Copy(sourceFullPath, candidate, overwrite: false);
                }

                return candidate;
            }
        }

        return null;
    }

    /// <summary>两个文件是不是同一份内容（比长度就够：重名又同长度基本就是我们自己拷的）。</summary>
    private static bool SameContent(string a, string b)
    {
        try
        {
            return new FileInfo(a).Length == new FileInfo(b).Length;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string Relative(string directory, string path) =>
        Path.GetRelativePath(directory, path).Replace('\\', '/');

    private static HashSet<string> Names(IEnumerable<string?> references) =>
        new(references.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);
}
