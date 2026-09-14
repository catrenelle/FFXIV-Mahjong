using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;
using FFXIVMahjong.Engine;

namespace FFXIVMahjong.Policy;

/// <summary>
/// Calls pon/chi/open-kan only when doing so both strictly improves shanten (after the
/// mandatory follow-up discard) and leaves at least one realistic path to a yaku — an open
/// hand with no yaku can never win, so improving shape alone isn't enough to justify
/// opening the hand. The yaku-potential check is a heuristic (yakuhai/toitoi/honitsu/tanyao
/// reachability), not a full search of every yaku.
/// </summary>
public sealed class HeuristicCallPolicy : ICallPolicy
{
    public bool ShouldCallPon(Hand hand, Tile discardedTile, WindTile seatWind, WindTile roundWind)
    {
        if (hand.Concealed.Count(t => t.Id == discardedTile.Id) < 2)
            return false;

        var meld = Meld.Pon(discardedTile, RelativeSeat.Kamicha);
        var consumed = hand.Concealed.Where(t => t.Id == discardedTile.Id).Take(2).ToList();
        return ImprovesShantenWithYakuPotential(hand, consumed, meld, seatWind, roundWind);
    }

    public IReadOnlyList<Tile>? ShouldCallChi(Hand hand, Tile discardedTile, WindTile seatWind, WindTile roundWind)
    {
        if (!discardedTile.IsSuited)
            return null;

        foreach (var (a, b) in ChiPartners(discardedTile))
        {
            if (!HasTile(hand, a) || !HasTile(hand, b))
                continue;

            var seqTiles = new[] { a, b, discardedTile }.OrderBy(t => t.Id).ToArray();
            var meld = Meld.Chi(seqTiles[0], seqTiles[1], discardedTile, RelativeSeat.Kamicha);
            if (ImprovesShantenWithYakuPotential(hand, new List<Tile> { a, b }, meld, seatWind, roundWind))
                return new List<Tile> { a, b };
        }

        return null;
    }

    public bool ShouldCallOpenKan(Hand hand, Tile discardedTile)
    {
        // A kan never changes shanten (triplet and quad both count as one set) and costs
        // future flexibility, but it does draw an extra dora indicator and a replacement
        // tile — only worth it when we're not breaking an already-closed hand's yaku path
        // (menzen tsumo/riichi/pinfu) for a marginal gain. Conservative default: only kan
        // when already open.
        return !hand.IsClosed && hand.Concealed.Count(t => t.Id == discardedTile.Id) == 3;
    }

    private static bool ImprovesShantenWithYakuPotential(
        Hand hand, IReadOnlyList<Tile> consumedFromConcealed, Meld meld, WindTile seatWind, WindTile roundWind)
    {
        int beforeShanten = Shanten.Calculate(hand);

        var remainingConcealed = new List<Tile>(hand.Concealed);
        foreach (var t in consumedFromConcealed)
        {
            int idx = remainingConcealed.FindIndex(x => x.Id == t.Id);
            remainingConcealed.RemoveAt(idx);
        }
        var afterCall = new Hand(remainingConcealed, new List<Meld>(hand.Melds) { meld });
        int afterShanten = BestShantenAfterDiscard(afterCall);

        return afterShanten < beforeShanten && HasOpenYakuPotential(afterCall, meld, seatWind, roundWind);
    }

    private static int BestShantenAfterDiscard(Hand fourteenTileEquivalent)
    {
        int best = int.MaxValue;
        foreach (int id in fourteenTileEquivalent.Concealed.Select(t => t.Id).Distinct())
        {
            var candidate = fourteenTileEquivalent.Concealed.First(t => t.Id == id);
            best = Math.Min(best, Shanten.Calculate(fourteenTileEquivalent.WithDiscard(candidate)));
        }
        return best;
    }

    private static bool HasOpenYakuPotential(Hand handAfterCall, Meld newMeld, WindTile seatWind, WindTile roundWind)
    {
        if (newMeld.Type != MeldType.Chi)
        {
            var tile = newMeld.Tiles[0];
            if (tile.Suit == Suit.Dragon)
                return true;
            if (tile.Suit == Suit.Wind && (tile.AsWind == seatWind || tile.AsWind == roundWind))
                return true;
        }

        if (handAfterCall.Melds.All(m => m.Type != MeldType.Chi))
            return true; // toitoi still reachable

        var allTiles = handAfterCall.Concealed.Concat(handAfterCall.Melds.SelectMany(m => m.Tiles)).ToList();
        var suits = allTiles.Where(t => t.IsSuited).Select(t => t.Suit).Distinct().ToList();
        if (suits.Count <= 1)
            return true; // honitsu/chinitsu still reachable

        if (allTiles.Count(t => t.IsTerminalOrHonor) <= 1)
            return true; // tanyao still reachable (at most one terminal/honor left to discard away)

        return false;
    }

    private static bool HasTile(Hand hand, Tile tile) => hand.Concealed.Any(t => t.Id == tile.Id);

    private static IEnumerable<(Tile A, Tile B)> ChiPartners(Tile discarded)
    {
        int r = discarded.Rank;
        var suit = discarded.Suit;
        if (r >= 3)
            yield return (Tile.FromSuitRank(suit, r - 2), Tile.FromSuitRank(suit, r - 1));
        if (r is >= 2 and <= 8)
            yield return (Tile.FromSuitRank(suit, r - 1), Tile.FromSuitRank(suit, r + 1));
        if (r <= 7)
            yield return (Tile.FromSuitRank(suit, r + 1), Tile.FromSuitRank(suit, r + 2));
    }
}
