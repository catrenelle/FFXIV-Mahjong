using FFXIVMahjong.Core;
using FFXIVMahjong.Engine;
using Xunit;

namespace FFXIVMahjong.Tests;

public class UkeireTests
{
    private static Tile M(int rank) => Tile.FromSuitRank(Suit.Man, rank);
    private static Tile P(int rank) => Tile.FromSuitRank(Suit.Pin, rank);
    private static Tile S(int rank) => Tile.FromSuitRank(Suit.Sou, rank);
    private static Tile Wind(int rank) => Tile.FromSuitRank(Suit.Wind, rank);

    [Fact]
    public void TankiWait_AcceptsOnlyThePairingTile()
    {
        List<Tile> tiles =
        [
            M(1), M(2), M(3),
            P(4), P(5), P(6),
            S(7), S(8), S(9),
            Wind(1), Wind(1), Wind(1),
            M(9),
        ];
        var hand = new Hand(tiles);
        var useful = Ukeire.UsefulTileIds(hand);
        Assert.Equal([M(9).Id], useful);
    }

    [Fact]
    public void TwoSidedWait_AcceptsBothCompletingTiles()
    {
        // 123m 456p 99s EastEastEast + 45m two-sided wait (needs 3m or 6m).
        List<Tile> tiles =
        [
            M(1), M(2), M(3),
            M(4), M(5),
            P(4), P(5), P(6),
            S(9), S(9),
            Wind(1), Wind(1), Wind(1),
        ];
        var hand = new Hand(tiles);
        var useful = Ukeire.UsefulTileIds(hand);
        Assert.Contains(M(3).Id, useful);
        Assert.Contains(M(6).Id, useful);
    }
}
