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
        // Confirmed live 2026-09-07: a red 5m read as raw 76075 = textureBase(76041) + 34,
        // i.e. aka-dora ids extend sequentially right after the normal 0-33 range.
        var tile = Tile.FromSuitRank(Suit.Man, 5, isRedFive: true);
        Assert.Equal(76075, EmjAddonReader.EncodeTileRaw(tile));
    }

    [Fact]
    public void EncodeTileRaw_RedFiveSou_MatchesConfirmedLiveValue()
    {
        // Confirmed live 2026-09-07: a drawn red 5s read as raw 76077 = textureBase + 36,
        // alongside an already-held normal 5s — this broke an earlier "+30 from the normal
        // tile's raw value" guess (22+30=52, not 36) and is what prompted the fix to the
        // simpler sequential-extension formula (34/35/36 = red 5m/5p/5s).
        var tile = Tile.FromSuitRank(Suit.Sou, 5, isRedFive: true);
        Assert.Equal(76077, EmjAddonReader.EncodeTileRaw(tile));
    }

    [Theory]
    [InlineData(Suit.Man, 5, false, 76045)]
    [InlineData(Suit.Man, 5, true, 76075)]
    [InlineData(Suit.Pin, 5, true, 76076)] // inferred from the pattern, not yet independently confirmed live
    [InlineData(Suit.Sou, 5, true, 76077)]
    public void EncodeTileRaw_RoundTripsForRedFives(Suit suit, int rank, bool isRed, int expectedRaw)
    {
        var tile = Tile.FromSuitRank(suit, rank, isRed);
        Assert.Equal(expectedRaw, EmjAddonReader.EncodeTileRaw(tile));
    }
}
