using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Engine;

namespace FFXIVMahjong.Rules.YakuRules;

public sealed class ChiitoitsuRule : IYakuRule
{
    public string Name => "Chiitoitsu";
    public bool IsYakuman => false;
    public int HanFor(WinningHand hand) => hand.Shape == HandShape.Chiitoitsu ? 2 : 0;
}
