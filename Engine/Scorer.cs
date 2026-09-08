using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;
using FFXIVMahjong.Rules;

namespace FFXIVMahjong.Engine;

public sealed record YakuResult(string Name, int Han, bool IsYakuman);

public sealed record ScoreResult(int TotalHan, int Points, IReadOnlyList<YakuResult> Yaku, HandShape Shape);

/// <summary>
/// Scores a completed hand: tries every valid decomposition (see <see cref="WinningHandEnumerator"/>),
/// sums the ruleset's applicable yaku for each, and keeps whichever decomposition scores highest —
/// the standard way real mahjong resolves shape ambiguity. Returns null if no decomposition has any
/// yaku at all (an illegal/yaku-less hand shape can't win).
/// </summary>
public static class Scorer
{
    public static ScoreResult? Score(Hand hand, WinContext context, IRuleSet ruleSet)
    {
        ScoreResult? best = null;

        foreach (var candidate in WinningHandEnumerator.Enumerate(hand, context))
        {
            var yaku = new List<YakuResult>();
            int han = 0;
            foreach (var rule in ruleSet.YakuRules)
            {
                int h = rule.HanFor(candidate);
                if (h <= 0)
                    continue;
                yaku.Add(new YakuResult(rule.Name, h, rule.IsYakuman));
                han += h;
            }

            if (yaku.Count == 0)
                continue;

            // Dora doesn't apply on top of a yakuman hand.
            if (!yaku.Any(y => y.IsYakuman))
            {
                han += DoraCount(hand, context.DoraTiles);
                han += hand.Concealed.Count(t => t.IsRedFive);
                if (context.IsRiichi)
                    han += DoraCount(hand, context.UraDoraTiles);
            }

            int points = PointsTable.PointsForHan(han);
            if (best is null || points > best.Points)
                best = new ScoreResult(han, points, yaku, candidate.Shape);
        }

        return best;
    }

    private static int DoraCount(Hand hand, IEnumerable<Tile> doraTiles)
    {
        var doraIds = doraTiles.Select(t => t.Id).ToHashSet();
        int count = 0;
        foreach (var t in hand.Concealed)
            if (doraIds.Contains(t.Id))
                count++;
        foreach (var meld in hand.Melds)
            foreach (var t in meld.Tiles)
                if (doraIds.Contains(t.Id))
                    count++;
        return count;
    }
}
