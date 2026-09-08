using System;
using System.Collections.Generic;
using System.Linq;
namespace FFXIVMahjong.Core;

/// <summary>Live wall/dora state as observed from the table, not a simulated deck.</summary>
public sealed record WallState(int RemainingTiles, IReadOnlyList<Tile> DoraIndicators)
{
    public IEnumerable<Tile> DoraTiles => DoraIndicators.Select(t => t.NextForDora());
}
