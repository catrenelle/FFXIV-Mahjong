using System;
using System.Linq;
using System.Collections.Generic;

namespace FFXIVMahjong.Core;

public enum MeldType
{
    Chi,
    Pon,

    /// <summary>Kan called from another player's discard, or upgraded from an existing pon (added kan) — both are open.</summary>
    OpenKan,

    /// <summary>Kan formed entirely from tiles in hand, declared on our own draw.</summary>
    ClosedKan,
}

/// <summary>A completed set taken out of the concealed hand: a chi/pon/kan.</summary>
public sealed record Meld(MeldType Type, IReadOnlyList<Tile> Tiles, RelativeSeat? CalledFrom, Tile? CalledTile)
{
    public bool IsOpen => Type != MeldType.ClosedKan;

    public bool IsKan => Type is MeldType.OpenKan or MeldType.ClosedKan;

    public static Meld Chi(Tile a, Tile b, Tile calledTile, RelativeSeat calledFrom)
        => new(MeldType.Chi, new List<Tile> { a, b, calledTile }, calledFrom, calledTile);

    public static Meld Pon(Tile tile, RelativeSeat calledFrom)
        => new(MeldType.Pon, new List<Tile> { tile, tile, tile }, calledFrom, tile);

    public static Meld OpenKan(Tile tile, RelativeSeat calledFrom)
        => new(MeldType.OpenKan, new List<Tile> { tile, tile, tile, tile }, calledFrom, tile);

    public static Meld ClosedKan(Tile tile)
        => new(MeldType.ClosedKan, new List<Tile> { tile, tile, tile, tile }, null, null);
}
