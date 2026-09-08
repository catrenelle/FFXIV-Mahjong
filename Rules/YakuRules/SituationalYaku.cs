using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Engine;

namespace FFXIVMahjong.Rules.YakuRules;

/// <summary>Yaku driven entirely by table-state flags on <see cref="Engine.WinningHand.Context"/>, independent of hand shape.</summary>
public sealed class RiichiRule : IYakuRule
{
    public string Name => "Riichi";
    public bool IsYakuman => false;
    public int HanFor(WinningHand hand) => hand.Context.IsRiichi && !hand.Context.IsDoubleRiichi ? 1 : 0;
}

public sealed class DoubleRiichiRule : IYakuRule
{
    public string Name => "Double Riichi";
    public bool IsYakuman => false;
    public int HanFor(WinningHand hand) => hand.Context.IsDoubleRiichi ? 2 : 0;
}

public sealed class MenzenTsumoRule : IYakuRule
{
    public string Name => "Menzen Tsumo";
    public bool IsYakuman => false;
    public int HanFor(WinningHand hand) => hand.IsClosed && hand.Context.IsTsumo ? 1 : 0;
}

public sealed class IppatsuRule : IYakuRule
{
    public string Name => "Ippatsu";
    public bool IsYakuman => false;
    public int HanFor(WinningHand hand) => hand.Context.IsIppatsu ? 1 : 0;
}

public sealed class HaiteiRule : IYakuRule
{
    public string Name => "Haitei Raoyue";
    public bool IsYakuman => false;
    public int HanFor(WinningHand hand) => hand.Context is { IsHaitei: true, IsTsumo: true } ? 1 : 0;
}

public sealed class HouteiRule : IYakuRule
{
    public string Name => "Houtei Raoyui";
    public bool IsYakuman => false;
    public int HanFor(WinningHand hand) => hand.Context is { IsHoutei: true, IsTsumo: false } ? 1 : 0;
}

public sealed class RinshanRule : IYakuRule
{
    public string Name => "Rinshan Kaihou";
    public bool IsYakuman => false;
    public int HanFor(WinningHand hand) => hand.Context.IsRinshan ? 1 : 0;
}

public sealed class ChankanRule : IYakuRule
{
    public string Name => "Chankan";
    public bool IsYakuman => false;
    public int HanFor(WinningHand hand) => hand.Context.IsChankan ? 1 : 0;
}
