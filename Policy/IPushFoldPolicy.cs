using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;

namespace FFXIVMahjong.Policy;

public interface IPushFoldPolicy
{
    /// <summary>Whether to fold (prioritize safety over hand progress) given the current shanten and whether any opponent has declared riichi.</summary>
    bool ShouldFold(int ownShanten, bool anyOpponentRiichi);

    /// <summary>Picks the safest discard: a known-safe (genbutsu) tile if one is held, otherwise the statistically safer of what's left.</summary>
    Tile ChooseSafeDiscard(Hand hand, IReadOnlyCollection<Tile> genbutsu);
}
