using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;

namespace FFXIVMahjong.Engine;

/// <summary>
/// Shanten = tiles away from tenpai (0 = tenpai, -1 = complete). Standard formula for the
/// regular (4 sets + pair) shape: 8 - 2*sets - partials - (hasPair ? 1 : 0), with partials
/// capped so sets+partials never exceeds 4 (there are only 4 non-head blocks to fill).
/// Chiitoitsu and kokushi have their own well-known closed-form formulas below.
/// </summary>
public static class Shanten
{
    public static int Calculate(Hand hand)
    {
        int[] counts = hand.TileCounts();
        int best = RegularShanten(counts, hand.Melds.Count);

        if (hand.Melds.Count == 0)
        {
            best = Math.Min(best, ChiitoitsuShanten(counts));
            best = Math.Min(best, KokushiShanten(counts));
        }

        return best;
    }

    public static int RegularShanten(int[] counts, int calledMelds)
    {
        int best = int.MaxValue;
        foreach (var (block, hasPair) in HandDecomposer.Options(counts))
        {
            int sets = block.Sets + calledMelds;
            int blockCap = Math.Max(0, 4 - sets);
            int cappedPartials = Math.Min(block.Partials, blockCap);
            int shanten = 8 - 2 * sets - cappedPartials - (hasPair ? 1 : 0);
            best = Math.Min(best, shanten);
        }
        return best;
    }

    public static int ChiitoitsuShanten(int[] counts)
    {
        int pairs = 0, kinds = 0;
        foreach (int c in counts)
        {
            if (c >= 1) kinds++;
            if (c >= 2) pairs++;
        }
        return 6 - pairs + Math.Max(0, 7 - kinds);
    }

    private static readonly int[] TerminalAndHonorIds =
    [
        0, 8, 9, 17, 18, 26, // 1m 9m 1p 9p 1s 9s
        27, 28, 29, 30, // winds
        31, 32, 33, // dragons
    ];

    public static int KokushiShanten(int[] counts)
    {
        int present = 0;
        bool hasPair = false;
        foreach (int id in TerminalAndHonorIds)
        {
            if (counts[id] >= 1) present++;
            if (counts[id] >= 2) hasPair = true;
        }
        return 13 - present - (hasPair ? 1 : 0);
    }
}
