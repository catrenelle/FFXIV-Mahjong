using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVMahjong.Core;

namespace FFXIVMahjong.Engine;

/// <summary>
/// Infers our own open melds (Chi/Pon/MinKan) from closed-hand deltas, since the Emj addon
/// exposes no direct meld record — confirmed both by this project's own exhaustive native-memory
/// RE session (see docs/addon-capture-log.md and the FFXIVMahjong native-RE memory) and,
/// independently, by the XeldarAlz/FFXIV-AutoMahjongSolver reference project's own MeldTracker
/// (read as a hypothesis for the algorithm shape, not vendored — consistent with this project's
/// clean-room approach elsewhere). Call <see cref="ObserveHand"/> every Pulse tick with the
/// current concealed hand and each opponent's live discard count; it fires whenever the
/// concealed hand shrinks by 2 or 3 tiles in one step (a chi/pon, or a minkan) and attributes
/// the meld to whichever opponent's discard count just advanced.
///
/// Known limitation carried over from the reference project: a 2-tile removal with adjacent
/// ranks (e.g. 4m+5m) is genuinely ambiguous — it could complete either {3,4,5} or {4,5,6} — and
/// defaults to assuming the missing tile extends downward when both are in-suit. This can be
/// wrong; it only affects the exact identity of the called tile within an already-known suit,
/// not the meld's type or suit.
/// </summary>
public sealed class MeldTracker
{
    private static readonly RelativeSeat[] OpponentSeats =
        [RelativeSeat.Shimocha, RelativeSeat.Toimen, RelativeSeat.Kamicha];

    private readonly List<Meld> _melds = new();
    private readonly Dictionary<RelativeSeat, int> _lastDiscardCounts = OpponentSeats.ToDictionary(s => s, _ => -1);
    private IReadOnlyList<Tile>? _lastHand;

    public IReadOnlyList<Meld> Melds => _melds;

    /// <summary>Clears all tracked state — call whenever a new hand is detected (e.g. self discard count drops back to 0).</summary>
    public void Reset()
    {
        _melds.Clear();
        _lastHand = null;
        foreach (var seat in OpponentSeats)
            _lastDiscardCounts[seat] = -1;
    }

    /// <summary>
    /// Observes the current concealed hand and each opponent's live discard count. Returns the
    /// newly-inferred meld if one was detected this call, else null. Safe to call every Pulse
    /// tick — an unchanged hand is a no-op, and the very first call after construction/Reset
    /// only primes the baseline (nothing to compare against yet).
    /// </summary>
    public Meld? ObserveHand(IReadOnlyList<Tile> currentHand, int shimochaDiscardCount, int toimenDiscardCount, int kamichaDiscardCount)
    {
        var discardCounts = new Dictionary<RelativeSeat, int>
        {
            [RelativeSeat.Shimocha] = shimochaDiscardCount,
            [RelativeSeat.Toimen] = toimenDiscardCount,
            [RelativeSeat.Kamicha] = kamichaDiscardCount,
        };

        Meld? inferred = null;
        if (_lastHand is { } lastHand)
        {
            int delta = lastHand.Count - currentHand.Count;
            if (delta is 2 or 3)
            {
                var removed = DiffRemoved(lastHand, currentHand);
                if (removed.Count == delta && FindAdvancedSeat(discardCounts) is { } fromSeat)
                {
                    inferred = delta == 2 ? InferChiOrPon(removed, fromSeat) : InferMinKan(removed, fromSeat);
                    if (inferred is not null)
                        _melds.Add(inferred);
                }
            }
        }

        _lastHand = currentHand;
        foreach (var (seat, count) in discardCounts)
            _lastDiscardCounts[seat] = count;
        return inferred;
    }

    private RelativeSeat? FindAdvancedSeat(Dictionary<RelativeSeat, int> current)
    {
        RelativeSeat? found = null;
        foreach (var seat in OpponentSeats)
        {
            if (_lastDiscardCounts[seat] >= 0 && current[seat] > _lastDiscardCounts[seat])
                found = seat;
        }
        return found;
    }

    private static List<Tile> DiffRemoved(IReadOnlyList<Tile> before, IReadOnlyList<Tile> after)
    {
        var counts = new int[Tile.KindCount];
        foreach (var t in before)
            counts[t.Id]++;
        foreach (var t in after)
            counts[t.Id]--;

        var removed = new List<Tile>();
        for (int id = 0; id < Tile.KindCount; id++)
            for (int i = 0; i < counts[id]; i++)
                removed.Add(new Tile(id));
        return removed;
    }

    private static Meld? InferChiOrPon(List<Tile> removed, RelativeSeat fromSeat)
    {
        var a = removed[0];
        var b = removed[1];

        if (a.Id == b.Id)
            return Meld.Pon(a, fromSeat);

        if (!a.IsSuited || a.Suit != b.Suit)
            return null; // can't be a chi across suits, or involving honors

        int diff = b.Id - a.Id;
        if (diff is not (1 or 2))
            return null;

        Tile low;
        if (diff == 2)
        {
            low = a;
        }
        else
        {
            var down = new Tile(a.Id - 1);
            low = (a.Rank > 1 && down.Suit == a.Suit) ? down : a;
        }

        Tile called = FindCalledTile(low, a, b);
        return Meld.Chi(a, b, called, fromSeat);
    }

    private static Tile FindCalledTile(Tile low, Tile a, Tile b)
    {
        var t0 = low;
        var t1 = new Tile(low.Id + 1);
        var t2 = new Tile(low.Id + 2);
        if (t0.Id != a.Id && t0.Id != b.Id)
            return t0;
        if (t1.Id != a.Id && t1.Id != b.Id)
            return t1;
        return t2;
    }

    private static Meld? InferMinKan(List<Tile> removed, RelativeSeat fromSeat) =>
        removed[0].Id == removed[1].Id && removed[1].Id == removed[2].Id
            ? Meld.OpenKan(removed[0], fromSeat)
            : null;
}
