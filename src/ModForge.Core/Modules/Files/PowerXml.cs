using System.Xml.Linq;
using ModForge.Core.Model;

namespace ModForge.Core.Modules.Files;

/// <summary>
/// 一个状态的 XML：<c>Create/Powers/&lt;状态 id&gt;.xml</c>，一个状态一个文件。
///
/// <code>
/// &lt;power&gt;
///   &lt;id&gt;FORGE_MIGHT&lt;/id&gt;
///   &lt;kind&gt;Buff&lt;/kind&gt;
///   &lt;stack&gt;Counter&lt;/stack&gt;
///   &lt;damageModifier&gt;Additive&lt;/damageModifier&gt;
///   &lt;cardFilter&gt;TagStrike&lt;/cardFilter&gt;
///   &lt;decay&gt;Never&lt;/decay&gt;
///   &lt;icon&gt;forge_might.png&lt;/icon&gt;
///   &lt;triggers&gt;
///     &lt;trigger&gt;
///       &lt;when&gt;TurnStart&lt;/when&gt;
///       &lt;effect&gt;…&lt;/effect&gt;
///     &lt;/trigger&gt;
///   &lt;/triggers&gt;
/// &lt;/power&gt;
/// </code>
///
/// 名字与说明同样在语言文件夹里（键为 <c>&lt;类型名&gt;.title</c> / <c>.description</c>）。
/// 图标放在 <c>Textures/</c>，这里只记文件名。
/// </summary>
public static class PowerXml
{
    private static readonly PowerDef Default = new();

    public static void Write(string path, PowerDef power)
    {
        XmlIo.Write(path, ToDocument(power));
    }

    public static XDocument ToDocument(PowerDef power)
    {
        var root = new XElement("power",
            new XElement("id", power.Id),
            new XElement("kind", power.Kind.ToString()),
            new XElement("stack", power.Stack.ToString()));

        XmlIo.SetIf(root, "damageModifier", power.DamageModifier, Default.DamageModifier);
        XmlIo.SetIf(root, "cardFilter", power.CardFilter, Default.CardFilter);
        XmlIo.SetIf(root, "filterCardId", power.FilterCardId, null);
        XmlIo.SetIf(root, "decay", power.Decay, Default.Decay);
        XmlIo.SetIf(root, "icon", power.IconFile, null);

        if (power.Triggers.Count > 0)
        {
            var triggers = new XElement("triggers");

            foreach (TriggeredEffect trigger in power.Triggers)
            {
                triggers.Add(new XElement("trigger",
                    new XElement("when", trigger.When.ToString()),
                    EffectXml.ToXml(trigger.Effect, "effect")));
            }

            root.Add(triggers);
        }

        return XmlIo.Document(root);
    }

    /// <summary>读一个状态；没有 id 时返回 null（那一份整份跳过，其余文件照读）。</summary>
    public static PowerDef? Read(string path, ReadLog log)
    {
        XDocument document = XmlIo.Read(path);

        using (log.Scope("power"))
        {
            XElement root = XmlIo.Root(document, path, "power");

            if (string.IsNullOrWhiteSpace(XmlIo.Str(root, "id")))
            {
                log.Fatal("缺 <id>：没有 id 就不知道这是哪个状态，整份文件已跳过。");
                return null;
            }

            return Parse(root, log);
        }
    }

    public static PowerDef Parse(XElement root, ReadLog log)
    {
        XmlIo.RejectUnknown(log, root, PowerElements);

        var power = new PowerDef
        {
            Id = XmlIo.Str(root, "id") ?? "",
            Kind = XmlIo.EnumValue(log, root, "kind", Default.Kind),
            Stack = XmlIo.EnumValue(log, root, "stack", Default.Stack),
            DamageModifier = XmlIo.EnumValue(log, root, "damageModifier", Default.DamageModifier),
            CardFilter = XmlIo.EnumValue(log, root, "cardFilter", Default.CardFilter),
            FilterCardId = XmlIo.Str(root, "filterCardId"),
            Decay = XmlIo.EnumValue(log, root, "decay", Default.Decay),
            IconFile = XmlIo.Str(root, "icon"),
        };

        if (root.Element("triggers") is XElement triggers)
        {
            int index = 0;

            foreach (XElement element in triggers.Elements())
            {
                index++;

                using (log.Scope("triggers", index))
                {
                    if (!element.Name.LocalName.Equals("trigger", StringComparison.Ordinal))
                    {
                        log.Error($"只应有 <trigger>，实际是 <{element.Name.LocalName}>，这一项已跳过。");
                        continue;
                    }

                    var trigger = new TriggeredEffect
                    {
                        When = XmlIo.EnumValue(log, element, "when", PowerTrigger.TurnStart),
                    };

                    if (element.Element("effect") is XElement effect)
                    {
                        using (log.Scope("effect"))
                        {
                            if (EffectXml.TryFromXml(effect, log) is EffectDef parsed)
                            {
                                trigger.Effect = parsed;
                            }
                            else
                            {
                                continue;   // 这条触发没有效果可执行，整个触发没有意义
                            }
                        }
                    }

                    power.Triggers.Add(trigger);
                }
            }
        }

        return power;
    }

    private static readonly string[] PowerElements =
    {
        "id", "kind", "stack", "damageModifier", "cardFilter", "filterCardId", "decay",
        "icon", "triggers",
    };
}
