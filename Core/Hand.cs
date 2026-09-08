using System;
using System.Collections.Generic;
using System.Linq;

namespace FFXIVMahjong.Core;

/// <summary>A player's hand: concealed tiles plus any melds taken out of it.</summary>
public sealed class Hand
{
    public IReadOnlyList<Tile> Concealed { get; }
    public IReadOnlyList<Meld> Melds { get; }

    public Hand(IReadOnlyList<Tile> concealed, IReadOnlyList<Meld>? melds = null)
    {
        Concealed = concealed;
        Melds = melds ?? new List<Meld>();
    }

    /// <summary>Closed (fully concealed) hand — a self-declared closed kan does not open it.</summary>
    public bool IsClosed => Melds.All(m => m.Type == MeldType.ClosedKan);

    /// <summary>Concealed tile count, counting one drawn tile if present (14 after draw, 13 otherwise).</summary>
    public int ConcealedCount => Concealed.Count;

    /// <summary>Count of each of the 34 tile kinds among the concealed tiles.</summary>
    public int[] TileCounts()
    {
        var counts = new int[Tile.KindCount];
        foreach (var tile in Concealed)
            counts[tile.Id]++;
        return counts;
    }

    public Hand WithDiscard(Tile discarded)
    {
        var remaining = new List<Tile>(Concealed.Count - 1);
        bool removed = false;
        foreach (var tile in Concealed)
        {
            if (!removed && tile.Id == discarded.Id && tile.IsRedFive == discarded.IsRedFive)
            {
                removed = true;
                continue;
            }
            remaining.Add(tile);
        }
        if (!removed)
            throw new InvalidOperationException($"{discarded} is not in hand.");
        return new Hand(remaining, Melds);
    }
}
