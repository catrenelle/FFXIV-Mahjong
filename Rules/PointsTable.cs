using System;
using System.Collections.Generic;
using System.Linq;
namespace FFXIVMahjong.Rules;

/// <summary>Doman Mahjong's fixed han-to-points table (no fu calculation), per the official ruleset.</summary>
public static class PointsTable
{
    public static int PointsForHan(int han) => han switch
    {
        <= 0 => 0,
        1 => 1000,
        2 => 2000,
        3 => 3900,
        4 or 5 => 8000,
        6 or 7 => 12000,
        8 or 9 or 10 => 16000,
        11 or 12 => 24000,
        // 13+: yakuman. Multiple stacked yakuman (e.g. daisangen + tsuuiisou on the same hand)
        // multiply rather than flatten — every full 13-han block is its own yakuman payout.
        _ => 32000 * Math.Max(1, han / 13),
    };
}
