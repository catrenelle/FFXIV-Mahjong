using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;

namespace FFXIVMahjong.Policy;

public interface IDiscardPolicy
{
    /// <summary>Chooses which tile to discard from a 14-tile (post-draw) hand. <paramref name="remainingCounts"/> optionally weights ukeire by how many copies of each tile kind are still unseen; omit to assume all 4 copies are live.</summary>
    Tile ChooseDiscard(Hand hand, IReadOnlyDictionary<int, int>? remainingCounts = null);
}
