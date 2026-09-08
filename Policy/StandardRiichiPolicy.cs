using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;
using FFXIVMahjong.Engine;

namespace FFXIVMahjong.Policy;

/// <summary>
/// Riichi whenever tenpai, closed, and able to afford the 1,000-point stick — riichi is
/// close to always +EV in this simplified ruleset since it's a guaranteed yaku plus ura
/// dora access. Damaten (staying hidden on an already-valid cheap hand to dodge a
/// dangerous opponent) is a real refinement this doesn't attempt yet.
/// </summary>
public sealed class StandardRiichiPolicy : IRiichiPolicy
{
    private const int RiichiCost = 1000;

    public bool ShouldDeclareRiichi(Hand hand, int pointsRemaining) =>
        hand.IsClosed && pointsRemaining >= RiichiCost && Shanten.Calculate(hand) == 0;
}
