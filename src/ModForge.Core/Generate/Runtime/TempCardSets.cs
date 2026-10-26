using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;

namespace ModForge.Generated.Runtime;

/// <summary>
/// 临时牌集合：一次出牌结算期间，把"抽到的 / 生成的 / 选出的那些牌"存起来，
/// 供**同一张牌**的其它效果再动它们。
///
/// 键的形状（见 README「临时牌集合」一节）：
///
///     {局内的牌地址}.{效果编号}.{集合ID}
///
/// 由生成代码传进来三个零件，**拼法只在这一处**（生成侧不拼）——否则两侧各拼一套，
/// 改了分隔符就会静默地读不到。
///
/// 两个要点：
///
///   1. **NULL 防御**：<see cref="Cards"/> 找不到集合时返回**空列表**，不是 null、不抛异常。
///      集合很可能不存在（定义它的那条效果没走到——它在条件分支里、或者牌堆空了没抽到牌），
///      那是正常情况：用它的效果就按"没有牌可动"处理。
///   2. **清空**：生成代码在结算结束时调 <see cref="Clear"/>（放在 finally 里，异常也会清）。
///      所以集合不会跨牌、跨回合地留着。
/// </summary>
public static class TempCardSets
{
    private static readonly Dictionary<string, List<CardModel>> Sets = new(System.StringComparer.Ordinal);

    /// <summary>发号器：每次出牌结算一个地址（嵌套结算因此互不干扰）。</summary>
    private static int _serial;

    /// <summary>
    /// 开一个"局内的牌地址"。<paramref name="cardEntry"/> 只是为了让地址在日志里读得懂
    /// （形如 <c>MYMOD_NEW_CARD#7</c>）。
    /// </summary>
    public static string Open(string cardEntry) =>
        $"{cardEntry}#{++_serial}";

    /// <summary>把一组牌存进集合。地址或集合 ID 为空时什么也不做（生成侧不该发这种调用）。</summary>
    public static void Store(string? address, int effectNumber, string? setId, IEnumerable<CardModel>? cards)
    {
        if (string.IsNullOrEmpty(address) || string.IsNullOrEmpty(setId))
        {
            return;
        }

        // 存的是**当时那几张牌的引用**，不是副本：后面那条效果要动的正是这几个牌对象
        // （降费、附魔改的都是牌自己）。所以这里只滤掉 null，别的什么都不做。
        var list = new List<CardModel>();

        if (cards is not null)
        {
            foreach (CardModel card in cards)
            {
                if (card is not null)
                {
                    list.Add(card);
                }
            }
        }

        Sets[Key(address!, effectNumber, setId!)] = list;
    }

    /// <summary>
    /// 取集合里的牌。**没有就是空列表**（见类的说明），所以调用方可以无条件接
    /// <c>.Where(...)</c> / <c>.ToList()</c>。
    ///
    /// 返回的是**副本**：调用方可能把它当候选列表去改（挑牌器自己会 ToList、取法会截断），
    /// 让它改到我们存的这份，后面再取就少了几张。
    /// </summary>
    public static List<CardModel> Cards(string? address, int effectNumber, string? setId)
    {
        if (string.IsNullOrEmpty(address) || string.IsNullOrEmpty(setId))
        {
            return new List<CardModel>();
        }

        return Sets.TryGetValue(Key(address!, effectNumber, setId!), out List<CardModel>? found)
            ? new List<CardModel>(found)
            : new List<CardModel>();
    }

    /// <summary>结算结束：把这个地址下的**所有**集合清掉（这一轮的效果写的那几个）。</summary>
    public static void Clear(string? address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return;
        }

        string prefix = address + ".";

        // 先收拢要删的键再删：不能在遍历字典的同时改它。
        var doomed = new List<string>();

        foreach (string key in Sets.Keys)
        {
            if (key.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                doomed.Add(key);
            }
        }

        foreach (string key in doomed)
        {
            Sets.Remove(key);
        }
    }

    private static string Key(string address, int effectNumber, string setId) =>
        $"{address}.{effectNumber}.{setId}";
}
