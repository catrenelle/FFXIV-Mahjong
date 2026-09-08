using System;
using System.Collections.Generic;
using System.Linq;
namespace FFXIVMahjong.Core;

/// <summary>
/// A player's seat relative to us. Standard Japanese-mahjong terminology:
/// shimocha = to our right (acts after us), toimen = across, kamicha = to
/// our left (acts before us).
/// </summary>
public enum RelativeSeat
{
    Self,
    Shimocha,
    Toimen,
    Kamicha,
}

public static class RelativeSeatExtensions
{
    /// <summary>The seat that acts immediately after this one, in turn order.</summary>
    public static RelativeSeat Next(this RelativeSeat seat) => seat switch
    {
        RelativeSeat.Self => RelativeSeat.Shimocha,
        RelativeSeat.Shimocha => RelativeSeat.Toimen,
        RelativeSeat.Toimen => RelativeSeat.Kamicha,
        RelativeSeat.Kamicha => RelativeSeat.Self,
        _ => throw new ArgumentOutOfRangeException(nameof(seat)),
    };
}
