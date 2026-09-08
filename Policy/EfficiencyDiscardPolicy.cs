using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;
using FFXIVMahjong.Engine;

namespace FFXIVMahjong.Policy;

/// <summary>
/// Discards to minimize shanten, then maximize weighted ukeire (tile acceptance times how
/// many copies are still live), then leans toward ditching terminals/honors first as a tie
/// break — a cheap nudge toward tanyao/pinfu-shaped hands without a full yaku-value model.
/// A real EV-vs-yaku-value tradeoff (e.g. deliberately keeping a slower yakuhai pair over a
/// faster yaku-less shape) is a further refinement, not implemented here yet.
/// </summary>
public sealed class EfficiencyDiscardPolicy : IDiscardPolicy
{
    public Tile ChooseDiscard(Hand hand, IReadOnlyDictionary<int, int>? remainingCounts = null)
    {
        if (hand.Concealed.Count == 0)
            throw new InvalidOperationException("Hand has no concealed tiles to discard.");

        Tile? best = null;
        int bestShanten = int.MaxValue;
        int bestWeightedUkeire = -1;
        bool bestIsTerminalOrHonor = false;

        foreach (int id in hand.Concealed.Select(t => t.Id).Distinct())
        {
            var candidate = hand.Concealed.First(t => t.Id == id);
            var resultHand = hand.WithDiscard(candidate);
            int shanten = Shanten.Calculate(resultHand);
            int weighted = Ukeire.UsefulTileIds(resultHand).Sum(tileId => RemainingCount(tileId, remainingCounts));

            bool better =
                shanten < bestShanten ||
                (shanten == bestShanten && weighted > bestWeightedUkeire) ||
                (shanten == bestShanten && weighted == bestWeightedUkeire && candidate.IsTerminalOrHonor && !bestIsTerminalOrHonor);

            if (!better)
                continue;

            best = candidate;
            bestShanten = shanten;
            bestWeightedUkeire = weighted;
            bestIsTerminalOrHonor = candidate.IsTerminalOrHonor;
        }

        return best!.Value;
    }

    private static int RemainingCount(int tileId, IReadOnlyDictionary<int, int>? remaining) =>
        remaining is not null && remaining.TryGetValue(tileId, out int count) ? count : 4;
}
