using System.Xml.Linq;
using ModForge.Core.Model;

namespace ModForge.Core.Modules.Files;

/// <summary>
/// 一个皮肤的 XML：<c>Create/Skins/&lt;角色&gt;.xml</c>。
///
/// <code>
/// &lt;skin&gt;
///   &lt;target&gt;regent&lt;/target&gt;
///   &lt;name&gt;雾锻婚纱&lt;/name&gt;
///   &lt;pack&gt;wuduihunsha.pck&lt;/pack&gt;
///   &lt;bindings&gt;
///     &lt;idle&gt;Idle&lt;/idle&gt;
///     &lt;attack&gt;Attack&lt;/attack&gt;
///   &lt;/bindings&gt;
/// &lt;/skin&gt;
/// </code>
///
/// 文件名用**目标角色**：游戏里一个角色同时只穿一套皮肤（导出时皮肤表就落在
/// <c>skins/&lt;角色&gt;.json</c>），所以角色就是它的身份，不必另立一个 id。
///
/// 源皮肤包本体放在 <c>Skins/</c>，这里只记文件名；<c>&lt;bindings&gt;</c> 是作者选的
/// "哪个动作演哪个槽位"（待机 / 攻击 / 施法 / 受击 / 死亡 / 入场），这是编辑器产出的、
/// 源包里没有的信息，所以要存。
/// </summary>
public static class SkinXml
{
    public static void Write(string path, SkinDef skin)
    {
        XmlIo.Write(path, ToDocument(skin));
    }

    public static XDocument ToDocument(SkinDef skin)
    {
        var root = new XElement("skin",
            new XElement("target", skin.Target),
            new XElement("name", skin.Name));

        XmlIo.SetIf(root, "pack", skin.SourcePack, null);

        if (skin.SlotBindings.Count > 0)
        {
            var bindings = new XElement("bindings");

            foreach ((string slot, string animation) in skin.SlotBindings.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(animation))
                {
                    bindings.Add(new XElement(slot, animation));
                }
            }

            if (bindings.HasElements)
            {
                root.Add(bindings);
            }
        }

        return XmlIo.Document(root);
    }

    /// <summary>读一个皮肤；没有 target 时返回 null（那一份跳过，其余照读）。</summary>
    public static SkinDef? Read(string path, ReadLog log)
    {
        XDocument document = XmlIo.Read(path);

        using (log.Scope("skin"))
        {
            XElement root = XmlIo.Root(document, path, "skin");

            if (string.IsNullOrWhiteSpace(XmlIo.Str(root, "target")))
            {
                log.Fatal("缺 <target>：皮肤要说明它换的是哪个角色，整份文件已跳过。");
                return null;
            }

            return Parse(root, log);
        }
    }

    public static SkinDef Parse(XElement root, ReadLog log)
    {
        XmlIo.RejectUnknown(log, root, new[] { "target", "name", "pack", "bindings" });

        string target = XmlIo.Str(root, "target")!;

        var skin = new SkinDef
        {
            Target = target,
            Name = XmlIo.Str(root, "name") ?? target,
            SourcePack = XmlIo.Str(root, "pack") ?? "",
        };

        if (root.Element("bindings") is XElement bindings)
        {
            foreach (XElement binding in bindings.Elements())
            {
                skin.SlotBindings[binding.Name.LocalName] = binding.Value.Trim();
            }
        }

        return skin;
    }
}
