using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;

namespace FFXIVMahjong.Engine;

public enum HandShape
{
    Standard,
    Chiitoitsu,
    Kokushi,
}

/// <summary>One candidate reading of a completed hand — a hand with multiple valid groupings (e.g. an ambiguous iipeiko-vs-toitoi shape) produces several of these, and the scorer keeps whichever scores highest, per standard rules.</summary>
public sealed record WinningHand(
    HandShape Shape,
    IReadOnlyList<CompletedGroup> ConcealedGroups,
    IReadOnlyList<Meld> Melds,
    WinContext Context,
    bool IsClosed)
{
    public IReadOnlyList<Tile> AllTiles =>
        ConcealedGroups.SelectMany(g => g.Tiles)
            .Concat(Melds.SelectMany(m => m.Tiles))
            .ToList();

    public IReadOnlyList<CompletedGroup> ConcealedSets => ConcealedGroups.Where(g => g.Kind != GroupKind.Pair).ToList();

    /// <summary>Null only for a malformed decomposition — every real winning hand has exactly one pair.</summary>
    public CompletedGroup? Pair => ConcealedGroups.FirstOrDefault(g => g.Kind == GroupKind.Pair);

    public IReadOnlyList<CompletedGroup> AllSets =>
        ConcealedSets
            .Concat(Melds.Select(m => new CompletedGroup(
                m.Type == MeldType.Chi ? GroupKind.Sequence : GroupKind.Triplet,
                m.Tiles.Select(t => t.Id).Min(),
                m.Tiles)))
            .ToList();
}
