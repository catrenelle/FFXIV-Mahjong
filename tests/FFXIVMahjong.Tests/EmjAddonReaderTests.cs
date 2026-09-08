using FFXIVMahjong.Addon;
using FFXIVMahjong.Core;
using Xunit;

namespace FFXIVMahjong.Tests;

public class EmjAddonReaderTests
{
    [Fact]
    public void EncodeTileRaw_NormalFiveMan_MatchesConfirmedLiveValue()
    {
        var tile = Tile.FromSuitRank(Suit.Man, 5);
        Assert.Equal(76045, EmjAddonReader.EncodeTileRaw(tile));
    }

    [Fact]
    public void EncodeTileRaw_RedFiveMan_MatchesConfirmedLiveValue()
    {
        // Confirmed live 2026-09-07: a red 5m read as raw 76075, i.e. normal 5m (76045) + 30.
        var tile = Tile.FromSuitRank(Suit.Man, 5, isRedFive: true);
        Assert.Equal(76075, EmjAddonReader.EncodeTileRaw(tile));
    }

    [Theory]
    [InlineData(5, false, 76045)]
    [InlineData(5, true, 76075)]
    public void EncodeTileRaw_RoundTripsForFiveMan(int rank, bool isRed, int expectedRaw)
    {
        var tile = Tile.FromSuitRank(Suit.Man, rank, isRed);
        Assert.Equal(expectedRaw, EmjAddonReader.EncodeTileRaw(tile));
    }
}
