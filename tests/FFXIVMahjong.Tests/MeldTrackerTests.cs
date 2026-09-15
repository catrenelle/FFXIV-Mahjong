using System.Linq;
using FFXIVMahjong.Core;
using FFXIVMahjong.Engine;
using Xunit;

namespace FFXIVMahjong.Tests;

public class MeldTrackerTests
{
    private static Tile M(int rank) => Tile.FromSuitRank(Suit.Man, rank);

    private static System.Collections.Generic.List<Tile> Hand13() =>
        [M(1), M(2), M(3), M(4), M(4), M(6), M(7), M(8), M(8), M(9), M(9), M(9), M(5)];

    [Fact]
    public void Pon_DetectedWhenTwoIdenticalTilesRemovedAndAnOpponentDiscardAdvances()
    {
        var tracker = new MeldTracker();
        var before = new System.Collections.Generic.List<Tile>
        { M(1), M(2), M(3), M(6), M(7), M(8), M(9), M(9), Tile.FromSuitRank(Suit.Pin, 4), Tile.FromSuitRank(Suit.Pin, 5), Tile.FromSuitRank(Suit.Pin, 6), Tile.FromSuitRank(Suit.Sou, 7), Tile.FromSuitRank(Suit.Sou, 8) };
        tracker.ObserveHand(before, shimochaDiscardCount: 3, toimenDiscardCount: 2, kamichaDiscardCount: 5);

        // Pon the 9m,9m pair away (shimocha discards a third 9m).
        var afterPon = new System.Collections.Generic.List<Tile>
        { M(1), M(2), M(3), M(6), M(7), M(8), Tile.FromSuitRank(Suit.Pin, 4), Tile.FromSuitRank(Suit.Pin, 5), Tile.FromSuitRank(Suit.Pin, 6), Tile.FromSuitRank(Suit.Sou, 7), Tile.FromSuitRank(Suit.Sou, 8) };
        var meld = tracker.ObserveHand(afterPon, shimochaDiscardCount: 4, toimenDiscardCount: 2, kamichaDiscardCount: 5);

        Assert.NotNull(meld);
        Assert.Equal(MeldType.Pon, meld!.Type);
        Assert.Equal(RelativeSeat.Shimocha, meld.CalledFrom);
        Assert.Equal(3, meld.Tiles.Count);
        Assert.All(meld.Tiles, t => Assert.Equal(9, t.Rank));
        Assert.Single(tracker.Melds);
    }

    [Fact]
    public void Chi_UnambiguousMiddleGap_DetectedCorrectly()
    {
        var tracker = new MeldTracker();
        var before = new System.Collections.Generic.List<Tile>
        { M(1), M(2), M(3), M(5), M(6), M(7), M(8), M(9), M(9), M(9), Tile.FromSuitRank(Suit.Pin, 4), Tile.FromSuitRank(Suit.Pin, 5), Tile.FromSuitRank(Suit.Pin, 6) };
        tracker.ObserveHand(before, 0, 0, 0);

        // Chi called from kamicha: our 3m+5m combine with kamicha's discarded 4m.
        var after = new System.Collections.Generic.List<Tile>
        { M(1), M(2), M(6), M(7), M(8), M(9), M(9), M(9), Tile.FromSuitRank(Suit.Pin, 4), Tile.FromSuitRank(Suit.Pin, 5), Tile.FromSuitRank(Suit.Pin, 6) };
        var meld = tracker.ObserveHand(after, shimochaDiscardCount: 0, toimenDiscardCount: 0, kamichaDiscardCount: 1);

        Assert.NotNull(meld);
        Assert.Equal(MeldType.Chi, meld!.Type);
        Assert.Equal(RelativeSeat.Kamicha, meld.CalledFrom);
        Assert.Equal(M(4).Id, meld.CalledTile!.Value.Id);
        Assert.Equal([3, 4, 5], meld.Tiles.Select(t => t.Rank).OrderBy(r => r));
    }

    [Fact]
    public void Chi_AdjacentAmbiguous_DefaultsToDownExtension()
    {
        var tracker = new MeldTracker();
        var hand = Hand13();
        tracker.ObserveHand(hand, 0, 0, 0);

        // Removing 4m+5m is genuinely ambiguous ({3,4,5} vs {4,5,6}) — expect the documented
        // down-extension default, i.e. called tile = 3m.
        var after = new System.Collections.Generic.List<Tile>
        { M(1), M(2), M(3), M(6), M(7), M(8), M(8), M(9), M(9), M(9), M(4) };
        var meld = tracker.ObserveHand(after, shimochaDiscardCount: 1, toimenDiscardCount: 0, kamichaDiscardCount: 0);

        Assert.NotNull(meld);
        Assert.Equal(MeldType.Chi, meld!.Type);
        Assert.Equal(3, meld.CalledTile!.Value.Rank);
    }

    [Fact]
    public void MinKan_DetectedWhenThreeIdenticalTilesRemovedAndDeltaIsThree()
    {
        var tracker = new MeldTracker();
        var hand = Hand13();
        tracker.ObserveHand(hand, 0, 0, 0);

        var after = new System.Collections.Generic.List<Tile>
        { M(1), M(2), M(3), M(4), M(4), M(6), M(7), M(8), M(8), M(5) };
        var meld = tracker.ObserveHand(after, shimochaDiscardCount: 0, toimenDiscardCount: 1, kamichaDiscardCount: 0);

        Assert.NotNull(meld);
        Assert.Equal(MeldType.OpenKan, meld!.Type);
        Assert.Equal(RelativeSeat.Toimen, meld.CalledFrom);
        Assert.Equal(4, meld.Tiles.Count);
        Assert.All(meld.Tiles, t => Assert.Equal(9, t.Rank));
    }

    [Fact]
    public void NoMeld_WhenHandShrinksByOneOrdinaryDiscard()
    {
        var tracker = new MeldTracker();
        var hand = Hand13();
        tracker.ObserveHand(hand, 0, 0, 0);

        var afterDiscard = hand.GetRange(0, hand.Count - 1);
        var meld = tracker.ObserveHand(afterDiscard, 0, 0, 0);

        Assert.Null(meld);
        Assert.Empty(tracker.Melds);
    }

    [Fact]
    public void NoMeld_WhenShrinkShapeMatchesButNoOpponentDiscardCountAdvanced()
    {
        var tracker = new MeldTracker();
        var hand = Hand13();
        tracker.ObserveHand(hand, 5, 5, 5);

        var after = new System.Collections.Generic.List<Tile>
        { M(1), M(2), M(3), M(4), M(4), M(6), M(7), M(8), M(8), M(5) };
        // Same discard counts as before — nothing actually advanced, so this shouldn't be trusted as a real call.
        var meld = tracker.ObserveHand(after, 5, 5, 5);

        Assert.Null(meld);
    }

    [Fact]
    public void FirstObservation_PrimesBaselineWithoutInferringAnything()
    {
        var tracker = new MeldTracker();
        var meld = tracker.ObserveHand(Hand13(), 3, 4, 5);

        Assert.Null(meld);
        Assert.Empty(tracker.Melds);
    }

    [Fact]
    public void Reset_ClearsMeldsAndBaseline()
    {
        var tracker = new MeldTracker();
        var hand = Hand13();
        tracker.ObserveHand(hand, 0, 0, 0);
        var after = new System.Collections.Generic.List<Tile>
        { M(1), M(2), M(3), M(4), M(4), M(6), M(7), M(8), M(8), M(5) };
        tracker.ObserveHand(after, 0, 0, 1);
        Assert.Single(tracker.Melds);

        tracker.Reset();
        Assert.Empty(tracker.Melds);

        // After reset, the tracker needs a fresh baseline before it can infer anything again.
        var meldRightAfterReset = tracker.ObserveHand(after, 0, 0, 2);
        Assert.Null(meldRightAfterReset);
    }
}
