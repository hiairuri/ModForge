using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModForge.Core.Model;

/// <summary>A complete mod project: everything needed to generate a loadable mod.</summary>
public sealed class ModProject
{
    /// <summary>Mod id. Must equal the generated DLL and PCK file names.</summary>
    public string Id { get; set; } = "MyMod";

    public string Name { get; set; } = "My Mod";

    public string Author { get; set; } = "";

    public string Description { get; set; } = "";

    public string Version { get; set; } = "1.0.0";

    /// <summary>Minimum game version this mod supports, e.g. 0.107.1.</summary>
    public string MinGameVersion { get; set; } = "0.107.1";

    /// <summary>
    /// Whether the mod changes gameplay. The game requires every multiplayer peer to
    /// run the same gameplay-affecting mods, so this defaults to true.
    /// </summary>
    public bool AffectsGameplay { get; set; } = true;

    public List<CardDef> Cards { get; set; } = new();

    public List<PowerDef> Powers { get; set; } = new();

    /// <summary>
    /// Reusable effects, editable in one place and referenced by cards and powers.
    /// Effects appear in several contexts, so a shared definition avoids repeating the
    /// same behaviour and keeps the copies from drifting apart.
    /// </summary>
    public List<SharedEffectDef> SharedEffects { get; set; } = new();

    /// <summary>Skins this mod ships, each repacked from an existing skin package.</summary>
    public List<SkinDef> Skins { get; set; } = new();

    /// <summary>
    /// mod 封面在 <c>About/</c> 里的文件名（通常就是 Preview.png），没选封面时为 null。
    ///
    /// 只记文件名，不记路径：保存时封面会被拷进工程的 About/，工程搬到哪都还在。
    /// 游戏的 mod 清单里没有封面这一项，它是给创意工坊与人看的。
    /// </summary>
    public string? PreviewFile { get; set; }

    /// <summary>
    /// Prefix added to generated type names to avoid colliding with vanilla content.
    /// Empty means type names are exactly the card/power names.
    /// </summary>
    public string TypePrefix { get; set; } = "";

    /// <summary>
    /// Emit a startup self-check that logs each card's pool membership and each
    /// power's type. Useful when first installing a mod: it turns "the card never
    /// showed up" into a clear log line instead of a silent absence.
    /// </summary>
    public bool SelfCheckOnStartup { get; set; }

    /// <summary>
    /// Serialization options for projects. Enums are written as names so a project
    /// file and the editor API stay readable, and so the UI can bind them directly.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions Options = JsonOptions;

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// 从 JSON 读一个工程。
    ///
    /// 不做跨版本的兼容改写：枚举里改了名或删了项之后，旧文件里的那种写法会直接让
    /// <c>System.Text.Json</c> 抛异常（而不是被悄悄读成别的值）。与其维护一堆迁移，
    /// 不如让"读不进来"这件事当场暴露。
    /// </summary>
    public static ModProject FromJson(string json)
    {
        ModProject project = JsonSerializer.Deserialize<ModProject>(json, Options)
            ?? throw new InvalidDataException("Project JSON was empty.");

        // 载入后规整内部一致性：界面不给的非法组合，防手改过的 JSON。
        Normalize(project);
        return project;
    }

    /// <summary>
    /// 把"取自目标"改成"取自自身"——当这条效果并没有唯一目标时。
    ///
    /// 计算型数值要读某个生物身上的数（已损失生命、力量层数……）。"取自目标"读的是
    /// <c>cardPlay.Target</c>；目标写成"所有敌人 / 随机一名敌人 / 所有友方"时并没有那一个目标，
    /// 读到的会是不确定的牌面目标或空引用。游戏自己也这样约定——<c>CalculatedVar.Calculate</c>
    /// 的文档写着"作用于多个生物（或没有目标）的效果应当传 null"。
    ///
    /// 界面上这类目标根本不给出这一项（见 <c>actorChoicesFor</c>）；这里处理的是手改过的、
    /// 或旧版本留下的 JSON，保证内存里的模型与界面允许的组合一致，而不是发出一段读空目标的代码。
    /// </summary>
    /// <summary>
    /// 把工程规整成一份"标准形"：凡是能推出、能合并的地方都只保留一种写法。
    ///
    /// 读工程与写工程都调用它，所以磁盘上永远只有标准形——否则同一件事会有两种写法，
    /// 而两种写法在生成时不一定等价。
    /// </summary>
    public static void Normalize(ModProject project)
    {
        foreach (EffectDef effect in AllEffects(project))
        {
            NormalizeValueSourceActors(effect);
            NormalizeLevelCoefficients(effect);

            // 空等级表 = 没有等级表（两处判断都写的是 Count > 0），留一个空列表在这里
            // 只是让"这张牌有没有等级表"有两种说法。
            if (effect.Levels is { Count: 0 })
            {
                effect.Levels = null;
            }
        }
    }

    private static void NormalizeValueSourceActors(EffectDef effect)
    {
        if (effect.Target != EffectTarget.Target &&
            effect.Source is { Actor: ValueSourceActor.Target })
        {
            effect.Source.Actor = ValueSourceActor.Self;
        }
    }

    /// <summary>
    /// 把等级表里"来源自带的系数"并进这一级的系数，再把它清掉。
    ///
    /// 等级的算式来源只回答"这一级用哪个变量"；系数写在 <see cref="EffectLevel.PerUnit"/>。
    /// 但 <see cref="ValueSource"/> 自己带着一个系数（顶层三件套里它是有用的），复制来源时
    /// 会顺手带过来，于是同一件事有了两个存放处——而计算时只读其中一个，另一个就是**死数据**：
    /// 填了不生效，还看不出来。这里把它并过去（没填系数时以来源那个为准），从此只剩一处。
    /// </summary>
    private static void NormalizeLevelCoefficients(EffectDef effect)
    {
        if (effect.Levels is not { Count: > 0 })
        {
            return;
        }

        foreach (EffectLevel? step in effect.Levels)
        {
            if (step is null)
            {
                continue;
            }

            if (step.Source is ValueSource source)
            {
                step.PerUnit ??= source.PerUnit;
                source.PerUnit = 1m;
            }

            if (step.RepeatSource is ValueSource repeatSource)
            {
                step.RepeatPerUnit ??= repeatSource.PerUnit;
                repeatSource.PerUnit = 1m;
            }
        }
    }

    public void Save(string path) => File.WriteAllText(path, ToJson());

    public static ModProject Load(string path) => FromJson(File.ReadAllText(path));

    /// <summary>工程里所有效果：卡牌的、共享效果的、状态触发的。</summary>
    public static IEnumerable<EffectDef> AllEffects(ModProject project)
    {
        foreach (CardDef card in project.Cards)
        {
            foreach (EffectDef effect in card.Effects)
            {
                yield return effect;
            }
        }

        foreach (SharedEffectDef shared in project.SharedEffects)
        {
            yield return shared.Effect;
        }

        foreach (PowerDef power in project.Powers)
        {
            foreach (TriggeredEffect trigger in power.Triggers)
            {
                yield return trigger.Effect;
            }
        }
    }

    /// <summary>The generated C# type name for a card.</summary>
    public string CardTypeName(CardDef card) => TypePrefix + Naming.ToPascal(card.Id);

    /// <summary>The generated C# type name for a power.</summary>
    public string PowerTypeName(PowerDef power) => TypePrefix + Naming.ToPascal(power.Id) + "Power";
}
