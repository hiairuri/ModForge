using System.Diagnostics;
using System.Text;

namespace ModForge.Core.Gfx;

/// <summary>
/// Extracts the game's own card presentation assets (frames, banners, energy icons) as
/// PNG files, using a local Godot installation.
///
/// These assets live in VRAM-compressed atlases stored as BC7, which ordinary image
/// decoders cannot read. The engine can, so it is used to read them and write plain
/// PNGs. Results are cached because the extraction is slow (a few seconds) and the
/// assets never change for a given game version.
/// </summary>
public sealed class AssetExtractor
{
    /// <summary>The presentation assets a card preview needs.</summary>
    private static readonly string[] RequestedAssets =
    {
        "res://images/atlases/ui_atlas.sprites/card/card_frame_attack_s.tres",
        "res://images/atlases/ui_atlas.sprites/card/card_frame_skill_s.tres",
        "res://images/atlases/ui_atlas.sprites/card/card_frame_power_s.tres",
        "res://images/atlases/ui_atlas.sprites/card/card_banner.tres",

        // 立绘内框。游戏按类型换一张（攻击/技能/能力），状态、诅咒、任务与 None 都映射到
        // **技能**那一张（见 CardModel.PortraitBorderPath），所以只有三张。
        // 少了它，卡面看起来就只是"图贴在框里"，没有游戏里那圈内边。
        "res://images/atlases/ui_atlas.sprites/card/card_portrait_border_attack_s.tres",
        "res://images/atlases/ui_atlas.sprites/card/card_portrait_border_skill_s.tres",
        "res://images/atlases/ui_atlas.sprites/card/card_portrait_border_power_s.tres",

        // 类型牌下面的"底片"。没有它，类型文字是直接压在卡面上的。
        "res://images/ui/cards/card_portrait_border_plaque2.png",

        // 先古稀有度是完全另一套外观：立绘铺满整张卡 + 特殊外框 + 文字底衬 + 加冠饰的暗色
        // 绸带。NCard.Reload 里先古分支把 _frame / _banner / _portraitBorder / _portrait
        // 全部隐藏，换用下面的 _ancient* 那几样。
        //
        // 不要取 `card_frame_ancient_s`：它在整份 PCK 里没有任何场景或脚本引用（搜过，
        // 只出现在 atlas 清单与 uid 缓存里），先古卡不画外框。早先把它当外框用，整张卡
        // 会被一层灰色图块盖住。
        //
        // 注意路径：游戏里经 ImageHelper.GetImagePath 转换过，实际是
        // images/atlases/compressed.sprites/...（不是 compressed_atlas.sprites）。
        "res://images/atlases/compressed.sprites/card_template/ancient_card_border.tres",
        "res://images/atlases/compressed.sprites/card_template/ancient_card_text_bg_attack.tres",
        "res://images/atlases/compressed.sprites/card_template/ancient_card_text_bg_skill.tres",
        "res://images/atlases/compressed.sprites/card_template/ancient_card_text_bg_power.tres",
        "res://images/atlases/compressed.sprites/card_template/ancient_portrait_mask_large.tres",
        "res://images/atlases/ui_atlas.sprites/card/ancient_banner.tres",

        "res://images/atlases/ui_atlas.sprites/card/energy_ironclad.tres",
        "res://images/atlases/ui_atlas.sprites/card/energy_silent.tres",
        "res://images/atlases/ui_atlas.sprites/card/energy_defect.tres",
        "res://images/atlases/ui_atlas.sprites/card/energy_regent.tres",
        "res://images/atlases/ui_atlas.sprites/card/energy_necrobinder.tres",
        "res://images/atlases/ui_atlas.sprites/card/energy_colorless.tres",
    };

    private readonly string _godotExe;
    private readonly string _scriptPath;
    private readonly string _gameDir;

    public AssetExtractor(string gameDir, string godotExe, string scriptPath)
    {
        _gameDir = gameDir;
        _godotExe = godotExe;
        _scriptPath = scriptPath;
    }

    public bool IsAvailable => File.Exists(_godotExe) && File.Exists(_scriptPath);

    /// <summary>
    /// Extracts every requested asset into <paramref name="cacheDir"/>, skipping work
    /// when the cache is already populated. Returns the number of files available.
    /// </summary>
    public int EnsureExtracted(string cacheDir, Action<string>? log = null)
    {
        Directory.CreateDirectory(cacheDir);

        // A marker file keeps the check cheap: the assets only change when the game does.
        string marker = Path.Combine(cacheDir, ".extracted");
        int existing = Directory.EnumerateFiles(cacheDir, "*.png").Count();

        if (File.Exists(marker) && existing >= RequestedAssets.Length)
        {
            log?.Invoke($"Presenter assets already extracted ({existing} files).");
            return existing;
        }

        if (!IsAvailable)
        {
            log?.Invoke("Godot not available; card preview artwork will be limited.");
            return existing;
        }

        string pck = Path.Combine(_gameDir, "SlayTheSpire2.pck");
        if (!File.Exists(pck))
        {
            log?.Invoke("Game pack not found; card preview artwork will be limited.");
            return existing;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _godotExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("--headless");
        startInfo.ArgumentList.Add("--script");
        startInfo.ArgumentList.Add(_scriptPath);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(pck);
        startInfo.ArgumentList.Add(cacheDir);
        foreach (string asset in RequestedAssets)
        {
            startInfo.ArgumentList.Add(asset);
        }

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start Godot.");

            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.StandardError.ReadToEnd();
            process.WaitForExit(180_000);

            int produced = Directory.EnumerateFiles(cacheDir, "*.png").Count();
            log?.Invoke($"Extracted {produced} card presentation asset(s) from the game.");

            if (produced > 0)
            {
                File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
            }

            return produced;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Asset extraction failed: {ex.Message}");
            return existing;
        }
    }
}
