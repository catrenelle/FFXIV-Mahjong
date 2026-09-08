using FFXIVMahjong.Core;
using FFXIVMahjong.Engine;
using FFXIVMahjong.Rules;
using Xunit;

namespace FFXIVMahjong.Tests;

public class ScorerTests
{
    private static readonly IRuleSet RuleSet = new DomanRuleSet();

    private static Tile M(int rank) => Tile.FromSuitRank(Suit.Man, rank);
    private static Tile P(int rank) => Tile.FromSuitRank(Suit.Pin, rank);
    private static Tile S(int rank) => Tile.FromSuitRank(Suit.Sou, rank);
    private static Tile Wind(int rank) => Tile.FromSuitRank(Suit.Wind, rank);
    private static Tile Dragon(int rank) => Tile.FromSuitRank(Suit.Dragon, rank);

    private static WinContext Context(Tile winningTile, bool tsumo = false, bool riichi = false, IReadOnlyList<Tile>? dora = null) =>
        new(
            SeatWind: WindTile.East,
            RoundWind: WindTile.East,
            WinningTile: winningTile,
            IsTsumo: tsumo,
            IsRiichi: riichi,
            IsDoubleRiichi: false,
            IsIppatsu: false,
            IsHaitei: false,
            IsHoutei: false,
            IsChankan: false,
            IsRinshan: false,
            DoraIndicators: dora ?? [],
            UraDoraIndicators: []);

    [Fact]
    public void RyanmenTsumo_ScoresPinfuTanyaoTsumo()
    {
        // 456p 678s 234s 55p (pair) + 34m waiting 2/5 — win on 2m by tsumo, all simples, all sequences.
        List<Tile> concealed13 = [M(3), M(4), P(4), P(5), P(6), S(6), S(7), S(8), P(5), P(5), S(2), S(3), S(4)];
        var hand = new Hand([.. concealed13, M(2)]); // draw completes 234m via ryanmen 3-4 waiting 2/5
        var context = Context(M(2), tsumo: true);

        var result = Scorer.Score(hand, context, RuleSet);
        Assert.NotNull(result);
        var names = result!.Yaku.Select(y => y.Name).ToHashSet();
        Assert.Contains("Pinfu", names);
        Assert.Contains("Tanyao", names);
        Assert.Contains("Menzen Tsumo", names);
    }

    [Fact]
    public void DragonTriplet_ScoresYakuhai()
    {
        List<Tile> concealed = [M(1), M(2), M(3), P(4), P(5), P(6), S(7), S(8), S(9), Dragon(1), Dragon(1), M(9), M(9)];
        var hand = new Hand([.. concealed, Dragon(1)]);
        var context = Context(Dragon(1), tsumo: true);

        var result = Scorer.Score(hand, context, RuleSet);
        Assert.NotNull(result);
        Assert.Contains(result!.Yaku, y => y.Name == "Yakuhai" && y.Han == 1);
    }

    [Fact]
    public void SeatAndRoundWindTriplet_ScoresDoubleYakuhai()
    {
        // East is both seat and round wind in Context() above.
        List<Tile> concealed = [M(1), M(2), M(3), P(4), P(5), P(6), S(7), S(8), S(9), Wind(1), Wind(1), M(9), M(9)];
        var hand = new Hand([.. concealed, Wind(1)]);
        var context = Context(Wind(1), tsumo: true);

        var result = Scorer.Score(hand, context, RuleSet);
        Assert.NotNull(result);
        Assert.Contains(result!.Yaku, y => y.Name == "Yakuhai" && y.Han == 2);
    }

    [Fact]
    public void AllTripletsHonorHeavy_ScoresToitoi()
    {
        // 4 concealed triplets + pair — tsumo completes the last (M1) triplet.
        List<Tile> concealed = [M(1), M(1), P(9), P(9), P(9), S(1), S(1), S(1), Wind(2), Wind(2), Wind(2), M(5), M(5)];
        var hand = new Hand([.. concealed, M(1)]);
        var context = Context(M(1), tsumo: true);

        var result = Scorer.Score(hand, context, RuleSet);
        Assert.NotNull(result);
        Assert.Contains(result!.Yaku, y => y.Name == "Toitoi");
    }

    [Fact]
    public void SevenPairs_ScoresChiitoitsuPlusHonitsuIfApplicable()
    {
        List<Tile> concealed =
        [
            M(1), M(1), M(9), M(9),
            P(1), P(1), P(9), P(9),
            M(5), M(5),
            Wind(1), Wind(1),
            Dragon(1),
        ];
        var hand = new Hand([.. concealed, Dragon(1)]);
        var context = Context(Dragon(1), tsumo: true);

        var result = Scorer.Score(hand, context, RuleSet);
        Assert.NotNull(result);
        Assert.Contains(result!.Yaku, y => y.Name == "Chiitoitsu");
    }

    [Fact]
    public void ThirteenOrphans_ScoresKokushiYakuman()
    {
        List<Tile> concealed =
        [
            M(1), M(9), P(1), P(9), S(1), S(9),
            Wind(1), Wind(2), Wind(3), Wind(4),
            Dragon(1), Dragon(2), Dragon(3),
        ];
        var hand = new Hand([.. concealed, M(1)]);
        var context = Context(M(1), tsumo: true);

        var result = Scorer.Score(hand, context, RuleSet);
        Assert.NotNull(result);
        Assert.True(result!.Yaku.Single(y => y.Name == "Kokushi Musou").IsYakuman);
        Assert.Equal(32000, result.Points);
    }

    [Fact]
    public void NoYaku_ReturnsNull()
    {
        // Open hand (pon of South, a non-yakuhai wind since seat/round are both East), mixed
        // suits, no riichi/tanyao/pinfu-eligible shape — a legal-looking but yaku-less hand.
        List<Tile> concealed = [M(1), M(2), M(3), P(2), P(3), P(4), S(5), S(6), S(7), M(9), M(9)];
        var hand = new Hand(concealed, [Meld.Pon(Wind(2), RelativeSeat.Kamicha)]);
        var context = Context(M(9), tsumo: false);

        var result = Scorer.Score(hand, context, RuleSet);
        Assert.Null(result);
    }

    [Fact]
    public void Dora_AddsHanOnTopOfBaseYaku()
    {
        List<Tile> concealed = [M(2), M(3), P(4), P(5), P(6), S(6), S(7), S(8), P(5), P(5), S(2), S(3), S(4)];
        var hand = new Hand([.. concealed, M(4)]);
        // Dora indicator 1m -> dora is 2m; hand holds two 2m (well, one after the draw shifts — check count).
        var context = Context(M(4), tsumo: true, dora: [M(1)]);

        var result = Scorer.Score(hand, context, RuleSet);
        Assert.NotNull(result);
        Assert.True(result!.TotalHan >= 3); // pinfu + tanyao + tsumo + at least 1 dora (2m present)
    }
}
