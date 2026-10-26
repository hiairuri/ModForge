using System.Xml.Linq;
using ModForge.Core.Model;

namespace ModForge.Core.Modules.Files;

/// <summary>
/// 一张卡的 XML：<c>Create/Cards/&lt;卡 id&gt;.xml</c>，一张卡一个文件。
///
/// <code>
/// &lt;card&gt;
///   &lt;id&gt;FORGE_STRIKE&lt;/id&gt;
///   &lt;pool&gt;ironclad&lt;/pool&gt;
///   &lt;type&gt;Attack&lt;/type&gt;
///   &lt;rarity&gt;Common&lt;/rarity&gt;
///   &lt;target&gt;AnyEnemy&lt;/target&gt;
///   &lt;energyCost&gt;1&lt;/energyCost&gt;
///   &lt;starCost&gt;-1&lt;/starCost&gt;
///   &lt;maxUpgradeLevel&gt;1&lt;/maxUpgradeLevel&gt;
///   &lt;upgradeCostDelta&gt;-1&lt;/upgradeCostDelta&gt;
///   &lt;keywords&gt;&lt;li&gt;Exhaust&lt;/li&gt;&lt;/keywords&gt;
///   &lt;art&gt;forge_strike.webp&lt;/art&gt;
///   &lt;effects&gt;
///     &lt;li&gt;
///       &lt;effect&gt;DealDamage&lt;/effect&gt;
///       &lt;target&gt;AnyEnemy&lt;/target&gt;
///       &lt;base&gt;&lt;amount&gt;6&lt;/amount&gt;&lt;/base&gt;
///     &lt;/li&gt;
///   &lt;/effects&gt;
/// &lt;/card&gt;
/// </code>
///
/// **名字与描述不在这个文件里**，它们在 <c>Language/&lt;语言&gt;/Localization.xml</c>——
/// 与 RimWorld 一样，定义放结构与数值、文本放语言文件夹，改翻译不必碰定义。
///
/// 身份那一组（牌池 / 类型 / 稀有度 / 目标 / 费用 / 能不能 X / 升级次数 / 重放）**总是写出来**，
/// 剩下那些（关键词、升级增量、立绘、效果）与默认相同就不写。这是刻意的区别：
/// 打开一个卡文件应当是"一眼看出这是张什么牌"，而不是要去查默认值才知道。
///
/// 效果那一块的写法见 <see cref="EffectXml"/>（一个效果一个 <c>&lt;li&gt;</c>）。
/// </summary>
public static class CardXml
{
    private static readonly CardDef Default = new();

    public static void Write(string path, CardDef card)
    {
        XmlIo.Write(path, ToDocument(card));
    }

    public static XDocument ToDocument(CardDef card)
    {
        var root = new XElement("card",
            new XElement("id", card.Id),
            new XElement("pool", card.Pool),
            new XElement("type", card.Type.ToString()),
            new XElement("rarity", card.Rarity.ToString()),
            new XElement("target", card.Target.ToString()),
            new XElement("energyCost", card.EnergyCost.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new XElement("starCost", card.StarCost.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new XElement("energyCostX", card.EnergyCostX ? "true" : "false"),
            new XElement("starCostX", card.StarCostX ? "true" : "false"),
            new XElement("maxUpgradeLevel", card.MaxUpgradeLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new XElement("replayCount", card.ReplayCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        XmlIo.SetIf(root, "upgradeCostDelta", card.UpgradeCostDelta, Default.UpgradeCostDelta);
        XmlIo.SetIf(root, "upgradeStarDelta", card.UpgradeStarDelta, Default.UpgradeStarDelta);

        // 按玩家人数的限制（"多人游戏卡牌"）：默认 None（两种模式都能出现）＝不写，
        // 这样已有的卡文件一个字节都不动。
        XmlIo.SetIf(root, "multiplayer", card.Multiplayer, Default.Multiplayer);

        if (card.Keywords.Count > 0)
        {
            XmlIo.SetList(root, "keywords", card.Keywords.Select(k => k.ToString()));
        }

        XmlIo.SetIf(root, "art", card.ArtFile, null);

        // 效果按顺序排列，列表项统一用 <li>（与关键词、等级表一致）。
        if (card.Effects.Count > 0)
        {
            var effects = new XElement("effects");

            foreach (EffectDef effect in card.Effects)
            {
                effects.Add(EffectXml.ToXml(effect));
            }

            root.Add(effects);
        }

        return XmlIo.Document(root);
    }

    /// <summary>
    /// 读一张卡。名字与描述**不在这里**——调用方拿 id 去语言表里取（见 <see cref="ProjectStore"/>）。
    ///
    /// 读不动的地方记进 <paramref name="log"/> 并退回默认值，不中断：一张卡里某个字段写错，
    /// 不该让整张卡（更不该让整个工程）打不开。只有"没有 id"是例外——没有 id 就无从知道
    /// 这是哪张卡，那一张整份丢掉。
    /// </summary>
    public static CardDef? Read(string path, ReadLog log)
    {
        XDocument document = XmlIo.Read(path);

        using (log.Scope("card"))
        {
            XElement root = XmlIo.Root(document, path, "card");

            if (string.IsNullOrWhiteSpace(XmlIo.Str(root, "id")))
            {
                log.Fatal("缺 <id>：没有 id 就不知道这是哪张卡，整份文件已跳过。");
                return null;
            }

            return Parse(root, log);
        }
    }

    public static CardDef Parse(XElement root, ReadLog log)
    {
        XmlIo.RejectUnknown(log, root, CardElements);

        var card = new CardDef
        {
            Id = XmlIo.Str(root, "id") ?? "",
            Pool = XmlIo.Str(root, "pool") ?? Default.Pool,
            Type = XmlIo.EnumValue(log, root, "type", Default.Type),
            Rarity = XmlIo.EnumValue(log, root, "rarity", Default.Rarity),
            Target = XmlIo.EnumValue(log, root, "target", Default.Target),
            EnergyCost = XmlIo.Int(log, root, "energyCost", Default.EnergyCost),
            StarCost = XmlIo.Int(log, root, "starCost", Default.StarCost),
            EnergyCostX = XmlIo.Bool(log, root, "energyCostX", Default.EnergyCostX),
            StarCostX = XmlIo.Bool(log, root, "starCostX", Default.StarCostX),
            MaxUpgradeLevel = XmlIo.Int(log, root, "maxUpgradeLevel", Default.MaxUpgradeLevel),
            ReplayCount = XmlIo.Int(log, root, "replayCount", Default.ReplayCount),
            UpgradeCostDelta = XmlIo.Int(log, root, "upgradeCostDelta"),
            UpgradeStarDelta = XmlIo.Int(log, root, "upgradeStarDelta"),
            Multiplayer = XmlIo.EnumValue(log, root, "multiplayer", Default.Multiplayer),
            ArtFile = XmlIo.Str(root, "art"),
        };

        foreach (string keyword in XmlIo.List(root, "keywords"))
        {
            using (log.Scope("keywords"))
            {
                if (System.Enum.TryParse(keyword, ignoreCase: true, out CardKeyword parsed) &&
                    System.Enum.IsDefined(parsed))
                {
                    card.Keywords.Add(parsed);
                }
                else
                {
                    log.Error(
                        $"\"{keyword}\" 不是合法的关键词，已忽略。合法值：" +
                        string.Join(" / ", System.Enum.GetNames<CardKeyword>()));
                }
            }
        }

        if (root.Element("effects") is XElement effects)
        {
            int index = 0;

            foreach (XElement element in effects.Elements())
            {
                index++;

                using (log.Scope("effects", index))
                {
                    if (!element.Name.LocalName.Equals("li", StringComparison.Ordinal))
                    {
                        log.Error($"只应有 <li>，实际是 <{element.Name.LocalName}>，这一条已跳过。");
                        continue;
                    }

                    // 一条效果读不动只丢这一条：其余的效果照读（"局部损坏不失兼容"）。
                    if (EffectXml.TryFromXml(element, log) is EffectDef effect)
                    {
                        card.Effects.Add(effect);
                    }
                }
            }
        }

        return card;
    }

    /// <summary>一张卡接受的全部子元素（其余的名字多半是拼错，报出来）。</summary>
    private static readonly string[] CardElements =
    {
        "id", "pool", "type", "rarity", "target", "energyCost", "starCost",
        "energyCostX", "starCostX", "maxUpgradeLevel", "replayCount",
        "upgradeCostDelta", "upgradeStarDelta", "multiplayer", "keywords", "art", "effects",
    };
}
