using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Engine;

namespace FFXIVMahjong.Rules;

public interface IYakuRule
{
    string Name { get; }

    bool IsYakuman { get; }

    /// <summary>Han contributed by this yaku for the given hand — 0 if absent. Most rules are 0-or-fixed-value; yakuhai-style rules may return a multiple (e.g. two stacked wind triplets).</summary>
    int HanFor(WinningHand hand);
}
