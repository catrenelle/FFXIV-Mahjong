using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;
using FFXIVMahjong.Engine;

namespace FFXIVMahjong.Rules.YakuRules;

/// <summary>Yaku that depend on the specific set/pair grouping of a completed standard-shape hand.</summary>
public sealed class YakuhaiRule : IYakuRule
{
    public string Name => "Yakuhai";
    public bool IsYakuman => false;

    public int HanFor(WinningHand hand)
    {
        if (hand.Shape == HandShape.Kokushi)
            return 0;
        int han = 0;
        foreach (var g in hand.AllSets.Where(g => g.Kind == GroupKind.Triplet))
        {
            var tile = new Tile(g.BaseTileId);
            if (tile.Suit == Suit.Dragon)
                han += 1;
            else if (tile.Suit == Suit.Wind)
            {
                if (tile.AsWind == hand.Context.SeatWind) han += 1;
                if (tile.AsWind == hand.Context.RoundWind) han += 1;
            }
        }
        return han;
    }
}

public sealed class PinfuRule : IYakuRule
{
    public string Name => "Pinfu";
    public bool IsYakuman => false;

    public int HanFor(WinningHand hand)
    {
        if (hand.Shape != HandShape.Standard || hand.Melds.Count > 0)
            return 0;
        if (hand.AllSets.Any(g => g.Kind != GroupKind.Sequence))
            return 0;
        if (hand.Pair is null)
            return 0;

        var pairTile = new Tile(hand.Pair.BaseTileId);
        if (pairTile.Suit == Suit.Dragon)
            return 0;
        if (pairTile.Suit == Suit.Wind && (pairTile.AsWind == hand.Context.SeatWind || pairTile.AsWind == hand.Context.RoundWind))
            return 0;

        var winningSeq = hand.ConcealedSets.FirstOrDefault(g =>
            g.Kind == GroupKind.Sequence && g.Tiles.Any(t => t.Id == hand.Context.WinningTile.Id));
        if (winningSeq is null || !IsRyanmenWait(winningSeq, hand.Context.WinningTile))
            return 0;

        return 1;
    }

    private static bool IsRyanmenWait(CompletedGroup seq, Tile winningTile)
    {
        int a = seq.BaseTileId;
        int winId = winningTile.Id;
        if (winId == a + 1)
            return false; // kanchan (gap wait)
        var baseTile = new Tile(a);
        if (winId == a && baseTile.Rank == 7)
            return false; // 789 waiting only on 7 — penchan
        if (winId == a + 2 && baseTile.Rank == 1)
            return false; // 123 waiting only on 3 — penchan
        return true;
    }
}

public sealed class IipeikoRule : IYakuRule
{
    public string Name => "Iipeiko";
    public bool IsYakuman => false;

    public int HanFor(WinningHand hand)
    {
        if (hand.Shape != HandShape.Standard || hand.Melds.Count > 0)
            return 0;
        var seqBaseIds = hand.ConcealedSets.Where(g => g.Kind == GroupKind.Sequence).Select(g => g.BaseTileId).ToList();
        return CountDuplicatePairs(seqBaseIds) == 1 ? 1 : 0;
    }

    internal static int CountDuplicatePairs(List<int> seqBaseIds) =>
        seqBaseIds.GroupBy(x => x).Sum(g => g.Count() / 2);
}

public sealed class RyanpeikouRule : IYakuRule
{
    public string Name => "Ryanpeiko";
    public bool IsYakuman => false;

    public int HanFor(WinningHand hand)
    {
        if (hand.Shape != HandShape.Standard || hand.Melds.Count > 0)
            return 0;
        var seqBaseIds = hand.ConcealedSets.Where(g => g.Kind == GroupKind.Sequence).Select(g => g.BaseTileId).ToList();
        return IipeikoRule.CountDuplicatePairs(seqBaseIds) >= 2 ? 3 : 0;
    }
}

public sealed class SanshokuDoujunRule : IYakuRule
{
    public string Name => "Sanshoku Doujun";
    public bool IsYakuman => false;

    public int HanFor(WinningHand hand)
    {
        if (hand.Shape != HandShape.Standard)
            return 0;
        var seqs = hand.AllSets.Where(g => g.Kind == GroupKind.Sequence).Select(g => new Tile(g.BaseTileId)).ToList();
        bool match = seqs.Where(t => t.Suit == Suit.Man).Select(t => t.Rank)
            .Intersect(seqs.Where(t => t.Suit == Suit.Pin).Select(t => t.Rank))
            .Intersect(seqs.Where(t => t.Suit == Suit.Sou).Select(t => t.Rank))
            .Any();
        return match ? (hand.IsClosed ? 2 : 1) : 0;
    }
}

public sealed class IttsuRule : IYakuRule
{
    public string Name => "Ittsu";
    public bool IsYakuman => false;

    public int HanFor(WinningHand hand)
    {
        if (hand.Shape != HandShape.Standard)
            return 0;
        var seqs = hand.AllSets.Where(g => g.Kind == GroupKind.Sequence).Select(g => new Tile(g.BaseTileId)).ToList();
        foreach (var suit in new[] { Suit.Man, Suit.Pin, Suit.Sou })
        {
            var ranks = seqs.Where(t => t.Suit == suit).Select(t => t.Rank).ToHashSet();
            if (ranks.Contains(1) && ranks.Contains(4) && ranks.Contains(7))
                return hand.IsClosed ? 2 : 1;
        }
        return 0;
    }
}

public sealed class ChantaRule : IYakuRule
{
    public string Name => "Chanta";
    public bool IsYakuman => false;

    public int HanFor(WinningHand hand)
    {
        if (!AllGroupsContainTerminalOrHonor(hand))
            return 0;
        if (!hand.AllTiles.Any(t => t.IsHonor))
            return 0; // no honors at all — that shape is Junchan's, not this
        return hand.IsClosed ? 2 : 1;
    }

    internal static bool AllGroupsContainTerminalOrHonor(WinningHand hand)
    {
        if (hand.Shape != HandShape.Standard || hand.Pair is null)
            return false;
        return hand.AllSets.Append(hand.Pair).All(g => g.Tiles.Any(t => t.IsTerminalOrHonor));
    }
}

public sealed class JunchanRule : IYakuRule
{
    public string Name => "Junchan";
    public bool IsYakuman => false;

    public int HanFor(WinningHand hand)
    {
        if (!ChantaRule.AllGroupsContainTerminalOrHonor(hand))
            return 0;
        if (hand.AllTiles.Any(t => t.IsHonor))
            return 0;
        return hand.IsClosed ? 3 : 2;
    }
}

public sealed class ToitoiRule : IYakuRule
{
    public string Name => "Toitoi";
    public bool IsYakuman => false;
    public int HanFor(WinningHand hand) =>
        hand.Shape == HandShape.Standard && hand.AllSets.All(g => g.Kind == GroupKind.Triplet) ? 2 : 0;
}

public sealed class SanankouRule : IYakuRule
{
    public string Name => "Sanankou";
    public bool IsYakuman => false;
    public int HanFor(WinningHand hand) =>
        hand.Shape == HandShape.Standard && hand.ConcealedSets.Count(g => g.Kind == GroupKind.Triplet) >= 3 ? 2 : 0;
}

public sealed class SanshokuDoukouRule : IYakuRule
{
    public string Name => "Sanshoku Doukou";
    public bool IsYakuman => false;

    public int HanFor(WinningHand hand)
    {
        if (hand.Shape != HandShape.Standard)
            return 0;
        var triplets = hand.AllSets.Where(g => g.Kind == GroupKind.Triplet)
            .Select(g => new Tile(g.BaseTileId)).Where(t => t.IsSuited).ToList();
        bool match = triplets.Where(t => t.Suit == Suit.Man).Select(t => t.Rank)
            .Intersect(triplets.Where(t => t.Suit == Suit.Pin).Select(t => t.Rank))
            .Intersect(triplets.Where(t => t.Suit == Suit.Sou).Select(t => t.Rank))
            .Any();
        return match ? 2 : 0;
    }
}

public sealed class SankantsuRule : IYakuRule
{
    public string Name => "Sankantsu";
    public bool IsYakuman => false;
    public int HanFor(WinningHand hand) => hand.Melds.Count(m => m.IsKan) == 3 ? 2 : 0;
}
