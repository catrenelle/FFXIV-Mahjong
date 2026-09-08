using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;
using FFXIVMahjong.Engine;

namespace FFXIVMahjong.Rules.YakuRules;

/// <summary>Yaku decided purely by which tile kinds are present, so they apply to standard and chiitoitsu shapes alike.</summary>
public sealed class TanyaoRule : IYakuRule
{
    public string Name => "Tanyao";
    public bool IsYakuman => false;
    public int HanFor(WinningHand hand) =>
        hand.Shape != HandShape.Kokushi && hand.AllTiles.All(t => !t.IsTerminalOrHonor) ? 1 : 0;
}

public sealed class HonitsuRule : IYakuRule
{
    public string Name => "Honitsu";
    public bool IsYakuman => false;
    public int HanFor(WinningHand hand)
    {
        if (hand.Shape == HandShape.Kokushi)
            return 0;
        var suits = hand.AllTiles.Where(t => t.IsSuited).Select(t => t.Suit).Distinct().ToList();
        bool hasHonor = hand.AllTiles.Any(t => t.IsHonor);
        return suits.Count == 1 && hasHonor ? (hand.IsClosed ? 3 : 2) : 0;
    }
}

public sealed class ChinitsuRule : IYakuRule
{
    public string Name => "Chinitsu";
    public bool IsYakuman => false;
    public int HanFor(WinningHand hand)
    {
        if (hand.Shape == HandShape.Kokushi)
            return 0;
        var suits = hand.AllTiles.Where(t => t.IsSuited).Select(t => t.Suit).Distinct().ToList();
        bool hasHonor = hand.AllTiles.Any(t => t.IsHonor);
        return suits.Count == 1 && !hasHonor ? (hand.IsClosed ? 6 : 5) : 0;
    }
}

public sealed class HonroutouRule : IYakuRule
{
    public string Name => "Honroutou";
    public bool IsYakuman => false;
    public int HanFor(WinningHand hand) =>
        hand.Shape != HandShape.Kokushi && hand.AllTiles.All(t => t.IsTerminalOrHonor) ? 2 : 0;
}
