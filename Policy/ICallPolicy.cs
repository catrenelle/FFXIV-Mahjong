using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;

namespace FFXIVMahjong.Policy;

public interface ICallPolicy
{
    bool ShouldCallPon(Hand hand, Tile discardedTile, WindTile seatWind, WindTile roundWind);

    /// <summary>Returns the two held tiles to complete the sequence, or null to pass. When more than one sequence is possible, returns the first one found worth calling.</summary>
    IReadOnlyList<Tile>? ShouldCallChi(Hand hand, Tile discardedTile, WindTile seatWind, WindTile roundWind);

    bool ShouldCallOpenKan(Hand hand, Tile discardedTile);
}
