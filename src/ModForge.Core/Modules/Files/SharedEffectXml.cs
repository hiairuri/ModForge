using System.Xml.Linq;
using ModForge.Core.Model;

namespace ModForge.Core.Modules.Files;

/// <summary>
/// 一个共享效果的 XML：<c>Create/Effects/&lt;效果 id&gt;.xml</c>，一个效果一个文件。
///
/// <code>
/// &lt;sharedEffect&gt;
///   &lt;id&gt;BurnAll&lt;/id&gt;
///   &lt;name&gt;全体灼烧&lt;/name&gt;
///   &lt;note&gt;给卡与状态共用&lt;/note&gt;
///   &lt;effect&gt;…&lt;/effect&gt;
/// &lt;/sharedEffect&gt;
/// </code>
///
/// <c>&lt;name&gt;</c> 与 <c>&lt;note&gt;</c> 是**给作者看的**（界面上挑效果时的名字），
/// 不是玩家看的话——玩家看的是卡面描述，那些在语言文件夹里。
/// </summary>
public static class SharedEffectXml
{
    public static void Write(string path, SharedEffectDef shared)
    {
        XmlIo.Write(path, ToDocument(shared));
    }

    public static XDocument ToDocument(SharedEffectDef shared)
    {
        var root = new XElement("sharedEffect",
            new XElement("id", shared.Id),
            new XElement("name", shared.Name));

        XmlIo.SetIf(root, "note", shared.Note, null);
        root.Add(EffectXml.ToXml(shared.Effect, "effect"));

        return XmlIo.Document(root);
    }

    /// <summary>读一个共享效果；没有 id 或没有内容时返回 null（那一份跳过，其余照读）。</summary>
    public static SharedEffectDef? Read(string path, ReadLog log)
    {
        XDocument document = XmlIo.Read(path);

        using (log.Scope("sharedEffect"))
        {
            XElement root = XmlIo.Root(document, path, "sharedEffect");

            if (string.IsNullOrWhiteSpace(XmlIo.Str(root, "id")))
            {
                log.Fatal("缺 <id>：没有 id 就不知道这是什么效果，整份文件已跳过。");
                return null;
            }

            if (root.Element("effect") is not XElement effect)
            {
                log.Fatal("没有 <effect>：共享效果得有实际行为，整份文件已跳过。");
                return null;
            }

            XmlIo.RejectUnknown(log, root, new[] { "id", "name", "note", "effect" });

            string id = XmlIo.Str(root, "id")!;

            using (log.Scope("effect"))
            {
                if (EffectXml.TryFromXml(effect, log) is not EffectDef parsed)
                {
                    return null;
                }

                return new SharedEffectDef
                {
                    Id = id,
                    Name = XmlIo.Str(root, "name") ?? id,
                    Note = XmlIo.Str(root, "note") ?? "",
                    Effect = parsed,
                };
            }
        }
    }
}
