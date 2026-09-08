using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;

namespace FFXIVMahjong.Engine;

/// <summary>
/// Tile acceptance: which of the 34 kinds would reduce this hand's shanten by one if
/// drawn. Pure hand-shape analysis — it doesn't know how many copies are still live (in
/// the wall vs. visible in discards/melds); the policy layer weights these by remaining
/// count using what it can see on the table.
/// </summary>
public static class Ukeire
{
    public static IReadOnlyList<int> UsefulTileIds(Hand hand)
    {
        int current = Shanten.Calculate(hand);
        var baseCounts = hand.TileCounts();
        var useful = new List<int>();

        for (int id = 0; id < Tile.KindCount; id++)
        {
            if (baseCounts[id] >= 4)
                continue; // we already hold every copy

            var concealedWithDraw = new List<Tile>(hand.Concealed) { new(id) };
            var candidate = new Hand(concealedWithDraw, hand.Melds);
            if (Shanten.Calculate(candidate) < current)
                useful.Add(id);
        }

        return useful;
    }
}
