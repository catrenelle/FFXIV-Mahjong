using FFXIVMahjong.Core;
using FFXIVMahjong.Policy;
using Xunit;

namespace FFXIVMahjong.Tests;

public class PolicyTests
{
    private static Tile M(int rank) => Tile.FromSuitRank(Suit.Man, rank);
    private static Tile P(int rank) => Tile.FromSuitRank(Suit.Pin, rank);
    private static Tile S(int rank) => Tile.FromSuitRank(Suit.Sou, rank);
    private static Tile Wind(int rank) => Tile.FromSuitRank(Suit.Wind, rank);

    [Fact]
    public void DiscardPolicy_DropsTheFloatingHonorOverBreakingAGoodShape()
    {
        // 123m 456p 789s 99m(pair) 45m(ryanmen) + a single lone North wind (dead tile) — 14 tiles.
        List<Tile> tiles = [M(1), M(2), M(3), P(4), P(5), P(6), S(7), S(8), S(9), M(9), M(9), M(4), M(5), Wind(4)];
        var hand = new Hand(tiles);

        var policy = new EfficiencyDiscardPolicy();
        var discard = policy.ChooseDiscard(hand);

        Assert.Equal(Wind(4).Id, discard.Id);
    }

    [Fact]
    public void RiichiPolicy_DeclaresWhenTenpaiClosedAndAffordable()
    {
        List<Tile> tiles = [M(1), M(2), M(3), P(4), P(5), P(6), S(7), S(8), S(9), Wind(1), Wind(1), Wind(1), M(9)];
        var hand = new Hand(tiles);
        var policy = new StandardRiichiPolicy();

        Assert.True(policy.ShouldDeclareRiichi(hand, pointsRemaining: 25000));
        Assert.False(policy.ShouldDeclareRiichi(hand, pointsRemaining: 500));
    }

    [Fact]
    public void RiichiPolicy_RefusesWhenNotTenpai()
    {
        List<Tile> tiles = [M(1), M(4), M(7), P(2), P(5), P(8), S(3), S(6), S(9), Wind(1), Wind(2), Wind(3), Wind(4)];
        var hand = new Hand(tiles);
        var policy = new StandardRiichiPolicy();

        Assert.False(policy.ShouldDeclareRiichi(hand, pointsRemaining: 25000));
    }

    [Fact]
    public void CallPolicy_CallsPonOnDragonTripletEvenThoughItOpensTheHand()
    {
        // Shanten-1 hand (2 sets, a dragon pair, an S7 pair, an M4-M5 ryanmen, one dead
        // tile) — ponning the dragon reaches tenpai (S7 pair becomes the head, M4-M5
        // waits on 3/6m), and the dragon triplet itself guarantees yakuhai.
        var dragon = Tile.FromSuitRank(Suit.Dragon, 1);
        List<Tile> tiles = [M(1), M(2), M(3), P(4), P(5), P(6), dragon, dragon, S(7), S(7), M(4), M(5), Wind(4)];
        var hand = new Hand(tiles);
        var policy = new HeuristicCallPolicy();

        Assert.True(policy.ShouldCallPon(hand, dragon, WindTile.East, WindTile.East));
    }

    [Fact]
    public void CallPolicy_RefusesChiWithNoSequenceToComplete()
    {
        // Already tenpai on a tanki wait (4 sequences + a lone P5) — no held tiles adjacent
        // to a P9 discard, so there's nothing to chi regardless of yaku potential.
        List<Tile> tiles = [M(2), M(3), M(4), P(4), P(5), P(6), S(2), S(3), S(4), S(6), S(7), S(8), P(5)];
        var hand = new Hand(tiles);
        var policy = new HeuristicCallPolicy();

        // Hand is already tenpai/near-optimal; claiming an unrelated tile shouldn't improve shanten.
        var unrelated = Tile.FromSuitRank(Suit.Pin, 9);
        var chi = policy.ShouldCallChi(hand, unrelated, WindTile.East, WindTile.East);
        Assert.Null(chi);
    }

    [Fact]
    public void CallPolicy_RefusesChiWhenExistingTilesElsewhereAlreadyKillTanyao()
    {
        // 4m4m 7m8m9m 3p4p 7p8p9p 3s5s7s — calling Chi(3p,4p) on a 5p discard forms a
        // terminal-free new meld (3p4p5p) and does improve shanten, but the 9m/9p already
        // sitting in the rest of the hand make tanyao unreachable regardless, and there's no
        // yakuhai/honitsu/toitoi path either (Chi rules out toitoi outright). Regression test
        // for a bug where the tanyao check only inspected the new meld's own tiles instead of
        // the whole post-call hand, live-caught 2026-09-14.
        List<Tile> tiles = [M(4), M(4), M(7), M(8), M(9), P(3), P(4), P(7), P(8), P(9), S(3), S(5), S(7)];
        var hand = new Hand(tiles);
        var policy = new HeuristicCallPolicy();

        var chi = policy.ShouldCallChi(hand, P(5), WindTile.East, WindTile.East);
        Assert.Null(chi);
    }

    [Fact]
    public void PushFoldPolicy_FoldsFarFromTenpaiAgainstRiichi()
    {
        var policy = new HeuristicPushFoldPolicy();
        Assert.True(policy.ShouldFold(ownShanten: 3, anyOpponentRiichi: true));
        Assert.False(policy.ShouldFold(ownShanten: 0, anyOpponentRiichi: true));
        Assert.False(policy.ShouldFold(ownShanten: 3, anyOpponentRiichi: false));
    }

    [Fact]
    public void PushFoldPolicy_PrefersGenbutsuWhenAvailable()
    {
        List<Tile> tiles = [M(1), M(2), M(3), P(4), P(5), P(6), S(7), S(8), S(9), Wind(4), Wind(4), M(9), M(9)];
        var hand = new Hand(tiles);
        var policy = new HeuristicPushFoldPolicy();

        var discard = policy.ChooseSafeDiscard(hand, [Wind(4)]);
        Assert.Equal(Wind(4).Id, discard.Id);
    }
}
