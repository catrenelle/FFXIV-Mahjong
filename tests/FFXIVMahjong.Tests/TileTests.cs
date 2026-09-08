using FFXIVMahjong.Core;
using Xunit;

namespace FFXIVMahjong.Tests;

public class TileTests
{
    [Theory]
    [InlineData(Suit.Man, 1, 0)]
    [InlineData(Suit.Man, 9, 8)]
    [InlineData(Suit.Pin, 1, 9)]
    [InlineData(Suit.Sou, 9, 26)]
    [InlineData(Suit.Wind, 1, 27)]
    [InlineData(Suit.Dragon, 3, 33)]
    public void FromSuitRank_ProducesExpectedId(Suit suit, int rank, int expectedId)
    {
        var tile = Tile.FromSuitRank(suit, rank);
        Assert.Equal(expectedId, tile.Id);
        Assert.Equal(suit, tile.Suit);
        Assert.Equal(rank, tile.Rank);
    }

    [Fact]
    public void NextForDora_WrapsWithinSuit()
    {
        var nine = Tile.FromSuitRank(Suit.Man, 9);
        Assert.Equal(Tile.FromSuitRank(Suit.Man, 1), nine.NextForDora());
    }

    [Fact]
    public void NextForDora_WrapsWithinWinds()
    {
        var north = Tile.FromSuitRank(Suit.Wind, 4);
        Assert.Equal(Tile.FromSuitRank(Suit.Wind, 1), north.NextForDora());
    }

    [Fact]
    public void RedFive_OnlyValidOnSuitedFive()
    {
        _ = Tile.FromSuitRank(Suit.Man, 5, isRedFive: true);
        Assert.Throws<ArgumentException>(() => Tile.FromSuitRank(Suit.Man, 4, isRedFive: true));
    }

    [Fact]
    public void TerminalAndHonorClassification()
    {
        Assert.True(Tile.FromSuitRank(Suit.Man, 1).IsTerminal);
        Assert.True(Tile.FromSuitRank(Suit.Wind, 1).IsHonor);
        Assert.False(Tile.FromSuitRank(Suit.Man, 5).IsTerminalOrHonor);
    }
}
