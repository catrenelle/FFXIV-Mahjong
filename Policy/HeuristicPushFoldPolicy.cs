using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;

namespace FFXIVMahjong.Policy;

/// <summary>
/// Folds once an opponent has declared riichi and we're not close to tenpai ourselves —
/// the standard beginner-safe push/fold threshold. This doesn't model bet size (our hand's
/// value vs. the riichi threat's likely value), which a stronger policy would weigh before
/// committing to push a cheap tenpai into a scary riichi.
/// </summary>
public sealed class HeuristicPushFoldPolicy : IPushFoldPolicy
{
    private const int FoldShantenThreshold = 2;

    public bool ShouldFold(int ownShanten, bool anyOpponentRiichi) =>
        anyOpponentRiichi && ownShanten >= FoldShantenThreshold;

    public Tile ChooseSafeDiscard(Hand hand, IReadOnlyCollection<Tile> genbutsu)
    {
        var genbutsuIds = genbutsu.Select(t => t.Id).ToHashSet();
        var safe = hand.Concealed.Where(t => genbutsuIds.Contains(t.Id)).ToList();
        if (safe.Count > 0)
            return safe[0];

        // No confirmed-safe tile: terminals/honors are statistically caught less often than
        // middle tiles, so lean on that as a last resort rather than a real safety read.
        return hand.Concealed
            .OrderByDescending(t => t.IsTerminalOrHonor)
            .ThenByDescending(t => t.IsSuited ? Math.Abs(t.Rank - 5) : 0)
            .First();
    }
}
