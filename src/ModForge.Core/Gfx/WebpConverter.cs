using System.Diagnostics;

namespace ModForge.Core.Gfx;

/// <summary>
/// Converts images with a local Godot installation.
///
/// Two uses, same mechanism (a headless Godot script doing the encode):
///
///   **转 WebP**（卡图）—— the game's exported engine ships only a WebP unpacker
///   (measured in game: a PNG payload in a .ctex fails to load, a WebP payload loads),
///   so custom art has to be WebP.
///
///   **转 PNG**（mod 封面）—— the cover is written under a fixed name
///   <c>About/Preview.png</c>, so a JPEG/WebP source has to become a real PNG; otherwise
///   the file's extension lies and whoever opens it sees a broken image.
///
/// This is a convenience, not a requirement: if Godot is absent, the editor says so and
/// the rest of the pipeline still works.
/// </summary>
public sealed class WebpConverter
{
    private readonly string _godotExe;
    private readonly string _scriptPath;
    private readonly string _outputExtension;

    private WebpConverter(string godotExe, string scriptPath, string outputExtension)
    {
        _godotExe = godotExe;
        _scriptPath = scriptPath;
        _outputExtension = outputExtension;
    }

    public bool IsAvailable => File.Exists(_godotExe) && File.Exists(_scriptPath);

    /// <summary>The Godot executable in use, or an empty string when none was found.</summary>
    public string GodotExecutable => _godotExe;

    /// <summary>
    /// Looks for a Godot executable and the conversion script.
    ///
    /// Candidate locations are checked in order of how explicit they are, so a user who
    /// installed Godot elsewhere can point at it with <paramref name="explicitGodotPath"/>.
    /// </summary>
    public static WebpConverter Discover(string? explicitGodotPath, string scriptPath) =>
        Discover(explicitGodotPath, scriptPath, ".webp");

    /// <summary>同一个机制，输出改成 PNG（给 mod 封面用，见类注释）。</summary>
    public static WebpConverter DiscoverPng(string? explicitGodotPath, string scriptPath) =>
        Discover(explicitGodotPath, scriptPath, ".png");

    private static WebpConverter Discover(string? explicitGodotPath, string scriptPath, string outputExtension)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(explicitGodotPath))
        {
            if (Directory.Exists(explicitGodotPath))
            {
                candidates.AddRange(Directory.EnumerateFiles(explicitGodotPath, "*godot*.exe")
                    .Where(IsConsoleOrEditor));
            }
            else
            {
                candidates.Add(explicitGodotPath);
            }
        }

        foreach (string root in new[] { @"D:\Tools", @"C:\Tools", @"D:\Godot", @"C:\Godot" })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            // 顶层与**一层子目录**都扫：Godot 常被装成 "D:\Tools\Godot\Godot_…_console.exe"
            // 这种套一层文件夹的形状（实测踩过——只扫顶层时探测失败，卡图被静默跳过）。
            candidates.AddRange(Directory.EnumerateFiles(root, "*godot*.exe", SearchOption.TopDirectoryOnly)
                .Where(IsConsoleOrEditor));

            foreach (string sub in Directory.EnumerateDirectories(root))
            {
                candidates.AddRange(Directory.EnumerateFiles(sub, "*godot*.exe", SearchOption.TopDirectoryOnly)
                    .Where(IsConsoleOrEditor));
            }
        }

        // A Godot already on PATH.
        string? onPath = FindOnPath();
        if (onPath is not null)
        {
            candidates.Add(onPath);
        }

        // Prefer the console build: the graphical build keeps a window and stdout is
        // less predictable there.
        string? chosen = candidates
            .Where(File.Exists)
            .OrderByDescending(c => Path.GetFileName(c).Contains("console", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        return new WebpConverter(chosen ?? string.Empty, scriptPath, outputExtension);
    }

    private static bool IsConsoleOrEditor(string path)
    {
        string name = Path.GetFileName(path);

        // 只排除导出模板运行时（template）。**不排除 mono / .NET 构建的编辑器**——
        // 那正是 C# 项目用户最可能装的形状（"Godot_v4.x-stable_mono_win64_console.exe"），
        // 排除它等于在只剩这一种安装的机器上探测失败（实测踩过）。
        // 无头脚本转换在编辑器的 console 版上跑得很好；console 版由上面的排序优先选中。
        return !name.Contains("template", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindOnPath()
    {
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
        {
            return null;
        }

        foreach (string dir in pathVar.Split(Path.PathSeparator))
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                string? hit = Directory.EnumerateFiles(dir, "*godot*.exe")
                    .FirstOrDefault(IsConsoleOrEditor);
                if (hit is not null)
                {
                    return hit;
                }
            }
            catch (IOException)
            {
                // An unreadable PATH entry is simply skipped.
            }
        }

        return null;
    }

    /// <summary>
    /// Converts one image to WebP, returning the new file's path.
    /// </summary>
    public string Convert(string imagePath)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("Godot is not available for image conversion.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _godotExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        // The script runs in a throwaway project directory so it never touches a user
        // project; Godot needs a writable path to resolve --path.
        string scratch = Path.Combine(Path.GetTempPath(), "modforge-webpconv");
        Directory.CreateDirectory(scratch);

        startInfo.ArgumentList.Add("--headless");
        startInfo.ArgumentList.Add("--path");
        startInfo.ArgumentList.Add(scratch);
        startInfo.ArgumentList.Add("--script");
        startInfo.ArgumentList.Add(_scriptPath);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(Path.GetFullPath(imagePath));

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Godot.");

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(120_000);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Godot failed to convert {Path.GetFileName(imagePath)}: {stderr.Trim()} {stdout.Trim()}");
        }

        string output = Path.ChangeExtension(imagePath, _outputExtension);
        if (!File.Exists(output))
        {
            throw new InvalidOperationException(
                $"Godot reported success but no file was produced for {Path.GetFileName(imagePath)}.");
        }

        return output;
    }
}
