using FFXIVMahjong.Core;
using FFXIVMahjong.Engine;
using Xunit;

namespace FFXIVMahjong.Tests;

public class ShantenTests
{
    private static Tile M(int rank) => Tile.FromSuitRank(Suit.Man, rank);
    private static Tile P(int rank) => Tile.FromSuitRank(Suit.Pin, rank);
    private static Tile S(int rank) => Tile.FromSuitRank(Suit.Sou, rank);
    private static Tile Wind(int rank) => Tile.FromSuitRank(Suit.Wind, rank);
    private static Tile Dragon(int rank) => Tile.FromSuitRank(Suit.Dragon, rank);

    [Fact]
    public void CompleteStandardHand_IsShantenMinusOne()
    {
        // 123m 456p 789s EastEastEast 99m
        List<Tile> tiles =
        [
            M(1), M(2), M(3),
            P(4), P(5), P(6),
            S(7), S(8), S(9),
            Wind(1), Wind(1), Wind(1),
            M(9), M(9),
        ];
        var hand = new Hand(tiles);
        Assert.Equal(-1, Shanten.Calculate(hand));
    }

    [Fact]
    public void TenpaiTankiWait_IsShantenZero()
    {
        // Same shape minus one 9m — waiting to pair 9m.
        List<Tile> tiles =
        [
            M(1), M(2), M(3),
            P(4), P(5), P(6),
            S(7), S(8), S(9),
            Wind(1), Wind(1), Wind(1),
            M(9),
        ];
        var hand = new Hand(tiles);
        Assert.Equal(0, Shanten.Calculate(hand));
    }

    [Fact]
    public void SevenDistinctPairs_IsChiitoitsuWin()
    {
        List<Tile> tiles =
        [
            M(1), M(1), M(9), M(9),
            P(1), P(1), P(9), P(9),
            S(1), S(1),
            Wind(1), Wind(1),
            Dragon(1), Dragon(1),
        ];
        var hand = new Hand(tiles);
        Assert.Equal(-1, Shanten.Calculate(hand));
    }

    [Fact]
    public void ThirteenOrphansWithPair_IsKokushiWin()
    {
        List<Tile> tiles =
        [
            M(1), M(1), M(9),
            P(1), P(9),
            S(1), S(9),
            Wind(1), Wind(2), Wind(3), Wind(4),
            Dragon(1), Dragon(2), Dragon(3),
        ];
        var hand = new Hand(tiles);
        Assert.Equal(-1, Shanten.Calculate(hand));
    }

    [Fact]
    public void CalledMelds_CountTowardCompletedSets()
    {
        // Two called pons (East, White) + a concealed 123m 456p + waiting pair on 99s (tanki).
        var melds = new List<Meld>
        {
            Meld.Pon(Wind(1), RelativeSeat.Kamicha),
            Meld.Pon(Dragon(1), RelativeSeat.Toimen),
        };
        List<Tile> concealed = [M(1), M(2), M(3), P(4), P(5), P(6), S(9)];
        var hand = new Hand(concealed, melds);
        Assert.Equal(0, Shanten.Calculate(hand));
    }

    [Fact]
    public void FreshRandomishHand_HasPositiveShanten()
    {
        List<Tile> tiles =
        [
            M(1), M(4), M(7),
            P(2), P(5), P(8),
            S(3), S(6), S(9),
            Wind(1), Wind(2), Wind(3), Wind(4),
        ];
        var hand = new Hand(tiles);
        Assert.True(Shanten.Calculate(hand) > 3);
    }
}
