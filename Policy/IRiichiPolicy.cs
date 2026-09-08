using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;

namespace FFXIVMahjong.Policy;

public interface IRiichiPolicy
{
    /// <summary>Whether to declare riichi with this (already tenpai, closed) 13-tile hand.</summary>
    bool ShouldDeclareRiichi(Hand hand, int pointsRemaining);
}
