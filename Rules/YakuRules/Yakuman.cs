using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;
using FFXIVMahjong.Engine;

namespace FFXIVMahjong.Rules.YakuRules;

public sealed class KokushiRule : IYakuRule
{
    public string Name => "Kokushi Musou";
    public bool IsYakuman => true;
    public int HanFor(WinningHand hand) => hand.Shape == HandShape.Kokushi ? 13 : 0;
}

public sealed class SuuankouRule : IYakuRule
{
    public string Name => "Suuankou";
    public bool IsYakuman => true;
    public int HanFor(WinningHand hand) =>
        hand.Shape == HandShape.Standard && hand.ConcealedSets.Count(g => g.Kind == GroupKind.Triplet) == 4 ? 13 : 0;
}

public sealed class DaisangenRule : IYakuRule
{
    public string Name => "Daisangen";
    public bool IsYakuman => true;

    public int HanFor(WinningHand hand)
    {
        if (hand.Shape != HandShape.Standard)
            return 0;
        int dragonTriplets = hand.AllSets.Where(g => g.Kind == GroupKind.Triplet)
            .Select(g => new Tile(g.BaseTileId)).Where(t => t.Suit == Suit.Dragon)
            .Select(t => t.AsDragon).Distinct().Count();
        return dragonTriplets == 3 ? 13 : 0;
    }
}

public sealed class ShousuushiiRule : IYakuRule
{
    public string Name => "Shousuushii";
    public bool IsYakuman => true;

    public int HanFor(WinningHand hand)
    {
        if (hand.Shape != HandShape.Standard || hand.Pair is null)
            return 0;
        int windTriplets = hand.AllSets.Where(g => g.Kind == GroupKind.Triplet)
            .Select(g => new Tile(g.BaseTileId)).Count(t => t.Suit == Suit.Wind);
        var pairTile = new Tile(hand.Pair.BaseTileId);
        return windTriplets == 3 && pairTile.Suit == Suit.Wind ? 13 : 0;
    }
}

public sealed class DaisuushiiRule : IYakuRule
{
    public string Name => "Daisuushii";
    public bool IsYakuman => true;

    public int HanFor(WinningHand hand)
    {
        if (hand.Shape != HandShape.Standard)
            return 0;
        int windTriplets = hand.AllSets.Where(g => g.Kind == GroupKind.Triplet)
            .Select(g => new Tile(g.BaseTileId)).Count(t => t.Suit == Suit.Wind);
        return windTriplets == 4 ? 13 : 0;
    }
}

public sealed class TsuuiisouRule : IYakuRule
{
    public string Name => "Tsuuiisou";
    public bool IsYakuman => true;
    public int HanFor(WinningHand hand) => hand.Shape != HandShape.Kokushi && hand.AllTiles.All(t => t.IsHonor) ? 13 : 0;
}

public sealed class ChinroutouRule : IYakuRule
{
    public string Name => "Chinroutou";
    public bool IsYakuman => true;
    public int HanFor(WinningHand hand) => hand.Shape != HandShape.Kokushi && hand.AllTiles.All(t => t.IsTerminal) ? 13 : 0;
}

public sealed class RyuuiisouRule : IYakuRule
{
    public string Name => "Ryuuiisou";
    public bool IsYakuman => true;
    public int HanFor(WinningHand hand) => hand.Shape != HandShape.Kokushi && hand.AllTiles.All(IsGreenTile) ? 13 : 0;

    private static bool IsGreenTile(Tile t) =>
        (t.Suit == Suit.Sou && t.Rank is 2 or 3 or 4 or 6 or 8) ||
        (t.Suit == Suit.Dragon && t.AsDragon == DragonTile.Green);
}

public sealed class ChuurenpoutouRule : IYakuRule
{
    public string Name => "Chuurenpoutou";
    public bool IsYakuman => true;

    public int HanFor(WinningHand hand)
    {
        if (hand.Shape != HandShape.Standard || hand.Melds.Count > 0)
            return 0;
        if (!hand.AllTiles.All(t => t.IsSuited) || hand.AllTiles.Select(t => t.Suit).Distinct().Count() != 1)
            return 0;

        var counts = new int[10]; // rank 1-9
        foreach (var t in hand.AllTiles)
            counts[t.Rank]++;

        for (int extra = 1; extra <= 9; extra++)
        {
            var required = new int[10];
            required[1] = 3;
            required[9] = 3;
            for (int r = 2; r <= 8; r++)
                required[r] = 1;
            required[extra]++;

            bool match = true;
            for (int r = 1; r <= 9 && match; r++)
                match = counts[r] == required[r];
            if (match)
                return 13;
        }
        return 0;
    }
}

public sealed class SuukantsuRule : IYakuRule
{
    public string Name => "Suukantsu";
    public bool IsYakuman => true;
    public int HanFor(WinningHand hand) => hand.Melds.Count(m => m.IsKan) == 4 ? 13 : 0;
}

public sealed class TenhouRule : IYakuRule
{
    public string Name => "Tenhou";
    public bool IsYakuman => true;
    public int HanFor(WinningHand hand) => hand.Context.IsTenhou ? 13 : 0;
}

public sealed class ChiihouRule : IYakuRule
{
    public string Name => "Chiihou";
    public bool IsYakuman => true;
    public int HanFor(WinningHand hand) => hand.Context.IsChiihou ? 13 : 0;
}
