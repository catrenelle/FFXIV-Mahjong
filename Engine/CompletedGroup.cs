using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;

namespace FFXIVMahjong.Engine;

public enum GroupKind
{
    Sequence,
    Triplet,
    Pair,
}

/// <summary>One concrete set or pair in a specific winning-hand decomposition. <see cref="BaseTileId"/> is the lowest tile's id for a sequence, or the repeated tile's id for a triplet/pair.</summary>
public sealed record CompletedGroup(GroupKind Kind, int BaseTileId, IReadOnlyList<Tile> Tiles);
