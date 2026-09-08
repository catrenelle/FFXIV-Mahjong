using System;
using System.Collections.Generic;
using System.Linq;
namespace FFXIVMahjong.Engine;

/// <summary>Sets and partial sets found in a decomposition, before the final shanten formula caps and scores them.</summary>
public readonly record struct BlockResult(int Sets, int Partials)
{
    public int Value => Sets * 2 + Partials;
}

/// <summary>
/// Decomposes a 34-kind tile-count array into complete sets (triplets/sequences) and
/// partial sets (a pair held as a proto-triplet, or a two-tile proto-sequence),
/// maximizing 2*sets + partials.
///
/// Always resolves the smallest-index tile with a nonzero count first. Once every index
/// below i is zero, any block touching tile i must start at i (a smaller index would be
/// needed for i to be the middle or end of a sequence), so trying every option for the
/// smallest remaining tile and recursing explores every decomposition without loss.
/// </summary>
public static class HandDecomposer
{
    private const int KindCount = Core.Tile.KindCount; // 34

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BlockResult> Memo = new();

    /// <summary>Best (sets, partials) achievable with no tile reserved as the hand's head pair.</summary>
    public static BlockResult BestNoHead(int[] counts)
    {
        string key = PackKey(counts);
        if (Memo.TryGetValue(key, out var cached))
            return cached;

        int i = 0;
        while (i < KindCount && counts[i] == 0)
            i++;
        if (i == KindCount)
            return Memo[key] = new BlockResult(0, 0);

        bool suited = i < 27;
        int suitEnd = i < 9 ? 8 : i < 18 ? 17 : 26;
        var best = new BlockResult(0, 0);

        if (counts[i] >= 3)
            best = MaxOf(best, ReduceOne(counts, i, 3, setsDelta: 1, partialsDelta: 0));
        if (suited && i + 2 <= suitEnd && counts[i + 1] > 0 && counts[i + 2] > 0)
            best = MaxOf(best, ReduceSequence(counts, i, setsDelta: 1));
        if (counts[i] >= 2)
            best = MaxOf(best, ReduceOne(counts, i, 2, setsDelta: 0, partialsDelta: 1));
        if (suited && i + 1 <= suitEnd && counts[i + 1] > 0)
            best = MaxOf(best, ReducePair(counts, i, i + 1, partialsDelta: 1));
        if (suited && i + 2 <= suitEnd && counts[i + 2] > 0)
            best = MaxOf(best, ReducePair(counts, i, i + 2, partialsDelta: 1));
        best = MaxOf(best, ReduceOne(counts, i, 1, setsDelta: 0, partialsDelta: 0));

        return Memo[key] = best;
    }

    /// <summary>
    /// Every viable (block, hasHeadPair) option: never reserving a pair, plus — for every
    /// tile kind held at least twice — reserving that pair as the head and decomposing the
    /// rest. The caller (<see cref="Shanten"/>) scores each option under the real formula
    /// (which depends on already-called melds) and keeps the best, rather than guessing
    /// here which reservation is superior.
    /// </summary>
    public static IEnumerable<(BlockResult Block, bool HasPair)> Options(int[] counts)
    {
        yield return (BestNoHead(counts), false);

        for (int idx = 0; idx < KindCount; idx++)
        {
            if (counts[idx] < 2)
                continue;
            var next = (int[])counts.Clone();
            next[idx] -= 2;
            yield return (BestNoHead(next), true);
        }
    }

    private static BlockResult ReduceOne(int[] counts, int index, int amount, int setsDelta, int partialsDelta)
    {
        var next = (int[])counts.Clone();
        next[index] -= amount;
        var rest = BestNoHead(next);
        return new BlockResult(rest.Sets + setsDelta, rest.Partials + partialsDelta);
    }

    private static BlockResult ReducePair(int[] counts, int a, int b, int partialsDelta)
    {
        var next = (int[])counts.Clone();
        next[a] -= 1;
        next[b] -= 1;
        var rest = BestNoHead(next);
        return new BlockResult(rest.Sets, rest.Partials + partialsDelta);
    }

    private static BlockResult ReduceSequence(int[] counts, int start, int setsDelta)
    {
        var next = (int[])counts.Clone();
        next[start] -= 1;
        next[start + 1] -= 1;
        next[start + 2] -= 1;
        var rest = BestNoHead(next);
        return new BlockResult(rest.Sets + setsDelta, rest.Partials);
    }

    private static BlockResult MaxOf(BlockResult a, BlockResult b) => b.Value > a.Value ? b : a;

    private static string PackKey(int[] counts)
    {
        Span<char> chars = stackalloc char[KindCount];
        for (int k = 0; k < KindCount; k++)
            chars[k] = (char)('0' + counts[k]);
        return new string(chars);
    }
}
