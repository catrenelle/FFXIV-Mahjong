using System;
using System.Collections.Generic;
using System.Linq;
namespace FFXIVMahjong.Core;

/// <summary>Everything about the table situation a yaku/scoring rule might need, beyond the hand itself.</summary>
public sealed record WinContext(
    WindTile SeatWind,
    WindTile RoundWind,
    Tile WinningTile,
    bool IsTsumo,
    bool IsRiichi,
    bool IsDoubleRiichi,
    bool IsIppatsu,
    bool IsHaitei,
    bool IsHoutei,
    bool IsChankan,
    bool IsRinshan,
    IReadOnlyList<Tile> DoraIndicators,
    IReadOnlyList<Tile> UraDoraIndicators,
    bool IsDealer = false,
    /// <summary>Dealer's uninterrupted-first-draw win. Caller determines eligibility from turn/call history.</summary>
    bool IsTenhou = false,
    /// <summary>Non-dealer's uninterrupted-first-draw win. Caller determines eligibility from turn/call history.</summary>
    bool IsChiihou = false)
{
    public IEnumerable<Tile> DoraTiles => DoraIndicators.Select(t => t.NextForDora());
    public IEnumerable<Tile> UraDoraTiles => UraDoraIndicators.Select(t => t.NextForDora());
}
