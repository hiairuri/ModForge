using System.Xml.Linq;
using ModForge.Core.Model;

namespace ModForge.Core.Modules.Files;

/// <summary>
/// <c>About/About.xml</c>：一份工程的元信息，文件形状参考 RimWorld 的 mod。
///
/// <code>
/// &lt;ModMetaData&gt;
///   &lt;name&gt;鎏金工坊&lt;/name&gt;
///   &lt;author&gt;某人&lt;/author&gt;
///   &lt;packageId&gt;MyMod&lt;/packageId&gt;
///   &lt;description&gt;……&lt;/description&gt;
///   &lt;modVersion&gt;1.0.0&lt;/modVersion&gt;
///   &lt;supportedVersions&gt;&lt;li&gt;0.107.1&lt;/li&gt;&lt;/supportedVersions&gt;
///   &lt;affectsGameplay&gt;true&lt;/affectsGameplay&gt;
///   &lt;selfCheckOnStartup&gt;false&lt;/selfCheckOnStartup&gt;
///   &lt;typePrefix&gt;Kx&lt;/typePrefix&gt;
///   &lt;preview&gt;Preview.png&lt;/preview&gt;
/// &lt;/ModMetaData&gt;
/// </code>
///
/// 名字沿用 RimWorld 的叫法（<c>name</c> / <c>author</c> / <c>packageId</c> / <c>description</c> /
/// <c>modVersion</c> / <c>supportedVersions</c>），因为那几个概念一一对应、没有理由另起名字：
/// RimWorld 的 <c>supportedVersions</c> 是"支持的**游戏**版本"，这里正好放最低游戏版本。
///
/// 后面三项是编辑器自己的：<c>affectsGameplay</c> 与 <c>typePrefix</c> 是生成 mod 时用的，
/// <c>selfCheckOnStartup</c> 是启动自检开关，<c>preview</c> 是封面文件名——
/// 游戏的 mod 清单里没有封面这一项，它是给创意工坊与人看的，所以只存在于工程里。
/// </summary>
public static class AboutXml
{
    /// <summary>About.xml 里的全部信息。</summary>
    public sealed record AboutData(
        string Id,
        string Name,
        string Author,
        string Description,
        string Version,
        string MinGameVersion,
        bool AffectsGameplay,
        string TypePrefix,
        bool SelfCheckOnStartup,
        string? Preview);

    private const string RootName = "ModMetaData";

    /// <summary>About.xml 读不出来时用的那份（整份文件坏了的情况，例如 XML 语法错）。</summary>
    public static AboutData Default { get; } =
        new("", "", "", "", "1.0.0", "0.107.1", AffectsGameplay: true, "", SelfCheckOnStartup: false, Preview: null);

    public static void Write(string path, ModProject project)
    {
        var root = new XElement(RootName,
            new XElement("name", project.Name),
            new XElement("packageId", project.Id),
            new XElement("modVersion", project.Version),
            new XElement("supportedVersions", new XElement("li", project.MinGameVersion)),
            new XElement("affectsGameplay", project.AffectsGameplay ? "true" : "false"),
            new XElement("selfCheckOnStartup", project.SelfCheckOnStartup ? "true" : "false"));

        // 空的项**不写**（`<author></author>` 这种空壳既占地方，也看不出是"没填"还是"填坏了"）。
        // 名字、id、版本三项必定写：一份工程没有它们就没有意义。
        WriteIfAny(root, "author", project.Author);
        WriteIfAny(root, "description", project.Description);
        WriteIfAny(root, "typePrefix", project.TypePrefix);

        // 封面：选了才写。没写就是"这份工程没有封面"——不写一个并不存在的默认文件名，
        // 否则打开工程的人会以为 About/ 里该有一张 Preview.png。
        WriteIfAny(root, "preview", project.PreviewFile);

        XmlIo.Write(path, XmlIo.Document(root));
    }

    /// <summary>有内容才写一个元素的文本（多行文本原样保存，缩进不串进内容里）。</summary>
    private static void WriteIfAny(XElement root, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            root.Add(new XElement(name, new XText(value)));
        }
    }

    /// <summary>
    /// 读 About.xml。
    ///
    /// 没有 <c>&lt;packageId&gt;</c> 时**不拦**：那只是少了一个名字，工程照样能打开
    /// （文件夹名就是它原来的名字），把 id 退回"文件夹名"并报一条即可——因为一个字段没填
    /// 就让整份工程打不开，代价太大。
    /// </summary>
    public static AboutData Read(string path, ReadLog log)
    {
        XDocument document = XmlIo.Read(path);

        using (log.Scope("ModMetaData"))
        {
            XElement root = XmlIo.Root(document, path, RootName);

            XmlIo.RejectUnknown(log, root, AboutElements);

            // 支持的版本写成列表是 RimWorld 的形状；这里只取第一项（我们只有一个最低版本），
            // 但保留列表形式，将来真要写多个也不必换格式。
            List<string> versions = XmlIo.List(root, "supportedVersions");

            string id = XmlIo.Str(root, "packageId") ?? "";

            if (id.Length == 0)
            {
                log.Error("缺 <packageId>：已用工程文件夹的名字代替。");
            }

            return new AboutData(
                Id: id,
                Name: XmlIo.Str(root, "name") ?? id,
                Author: XmlIo.Str(root, "author") ?? "",
                Description: XmlIo.Str(root, "description") ?? "",
                Version: XmlIo.Str(root, "modVersion") ?? "1.0.0",
                MinGameVersion: versions.Count > 0 ? versions[0] : "0.107.1",
                AffectsGameplay: XmlIo.Bool(log, root, "affectsGameplay", fallback: true),
                TypePrefix: XmlIo.Str(root, "typePrefix") ?? "",
                SelfCheckOnStartup: XmlIo.Bool(log, root, "selfCheckOnStartup"),
                Preview: XmlIo.Str(root, "preview"));
        }
    }

    private static readonly string[] AboutElements =
    {
        "name", "author", "packageId", "description", "modVersion", "supportedVersions",
        "affectsGameplay", "typePrefix", "selfCheckOnStartup", "preview",
    };

    /// <summary>把读出来的信息写进工程。</summary>
    public static void Apply(AboutData about, ModProject project)
    {
        project.Id = about.Id;
        project.Name = about.Name;
        project.Author = about.Author;
        project.Description = about.Description;
        project.Version = about.Version;
        project.MinGameVersion = about.MinGameVersion;
        project.AffectsGameplay = about.AffectsGameplay;
        project.TypePrefix = about.TypePrefix;
        project.SelfCheckOnStartup = about.SelfCheckOnStartup;
        project.PreviewFile = about.Preview;
    }
}
