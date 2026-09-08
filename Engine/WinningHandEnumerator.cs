using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;

namespace FFXIVMahjong.Engine;

/// <summary>
/// Enumerates every valid way to read a complete hand — the standard 4-sets-plus-pair
/// shape (possibly several ways when tiles are ambiguous), plus the special chiitoitsu
/// (seven pairs) and kokushi (thirteen orphans) shapes when the tiles happen to fit them.
/// The scorer evaluates every candidate under the ruleset and keeps the highest score,
/// which is how real mahjong resolves shape ambiguity.
/// </summary>
public static class WinningHandEnumerator
{
    private static readonly int[] TerminalAndHonorIds = new[] { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33 };

    public static IEnumerable<WinningHand> Enumerate(Hand hand, WinContext context)
    {
        int[] counts = hand.TileCounts();

        if (hand.Melds.Count == 0 && IsKokushiShape(counts))
            yield return new WinningHand(HandShape.Kokushi, new List<CompletedGroup>(), hand.Melds, context, true);

        if (hand.Melds.Count == 0 && IsChiitoitsuShape(counts))
            yield return new WinningHand(HandShape.Chiitoitsu, ChiitoitsuGroups(counts), hand.Melds, context, true);

        int setsNeeded = 4 - hand.Melds.Count;
        foreach (var decomposition in EnumerateExact(counts, setsNeeded, pairUsed: false))
            yield return new WinningHand(HandShape.Standard, decomposition, hand.Melds, context, hand.IsClosed);
    }

    private static bool IsKokushiShape(int[] counts)
    {
        foreach (int id in TerminalAndHonorIds)
            if (counts[id] == 0)
                return false;
        int total = 0;
        for (int id = 0; id < Tile.KindCount; id++)
            total += counts[id];
        return total == 14 && OnlyTerminalsAndHonors(counts);
    }

    private static bool OnlyTerminalsAndHonors(int[] counts)
    {
        var allowed = new HashSet<int>(TerminalAndHonorIds);
        for (int id = 0; id < Tile.KindCount; id++)
            if (counts[id] > 0 && !allowed.Contains(id))
                return false;
        return true;
    }

    private static bool IsChiitoitsuShape(int[] counts)
    {
        int pairs = 0;
        for (int id = 0; id < Tile.KindCount; id++)
        {
            if (counts[id] == 2) pairs++;
            else if (counts[id] != 0) return false;
        }
        return pairs == 7;
    }

    private static List<CompletedGroup> ChiitoitsuGroups(int[] counts)
    {
        var groups = new List<CompletedGroup>(7);
        for (int id = 0; id < Tile.KindCount; id++)
            if (counts[id] == 2)
                groups.Add(PairGroup(id));
        return groups;
    }

    private static IEnumerable<List<CompletedGroup>> EnumerateExact(int[] counts, int setsNeeded, bool pairUsed)
    {
        int i = FindFirstNonzero(counts);
        if (i == -1)
        {
            if (setsNeeded == 0 && pairUsed)
                yield return [];
            yield break;
        }

        bool suited = i < 27;
        int suitEnd = i < 9 ? 8 : i < 18 ? 17 : 26;

        if (setsNeeded > 0)
        {
            if (counts[i] >= 3)
            {
                var next = (int[])counts.Clone();
                next[i] -= 3;
                foreach (var rest in EnumerateExact(next, setsNeeded - 1, pairUsed))
                {
                    rest.Insert(0, TripletGroup(i));
                    yield return rest;
                }
            }
            if (suited && i + 2 <= suitEnd && counts[i + 1] > 0 && counts[i + 2] > 0)
            {
                var next = (int[])counts.Clone();
                next[i]--; next[i + 1]--; next[i + 2]--;
                foreach (var rest in EnumerateExact(next, setsNeeded - 1, pairUsed))
                {
                    rest.Insert(0, SequenceGroup(i));
                    yield return rest;
                }
            }
        }

        if (!pairUsed && counts[i] >= 2)
        {
            var next = (int[])counts.Clone();
            next[i] -= 2;
            foreach (var rest in EnumerateExact(next, setsNeeded, true))
            {
                rest.Insert(0, PairGroup(i));
                yield return rest;
            }
        }
    }

    private static int FindFirstNonzero(int[] counts)
    {
        for (int k = 0; k < counts.Length; k++)
            if (counts[k] > 0)
                return k;
        return -1;
    }

    private static CompletedGroup TripletGroup(int id) => new(GroupKind.Triplet, id, new List<Tile> { new(id), new(id), new(id) });
    private static CompletedGroup PairGroup(int id) => new(GroupKind.Pair, id, new List<Tile> { new(id), new(id) });
    private static CompletedGroup SequenceGroup(int start) => new(GroupKind.Sequence, start, new List<Tile> { new(start), new(start + 1), new(start + 2) });
}
