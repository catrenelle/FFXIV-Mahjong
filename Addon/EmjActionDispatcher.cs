using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ff14bot.Managers;

namespace FFXIVMahjong.Addon;

/// <summary>
/// Dispatches actions to the "Emj" addon. <see cref="AtkAddonControl.SendAction"/> wraps the
/// native FireCallback mechanism: <c>pairCount</c> is the number of AtkValues, and the
/// ulong[] must be <c>pairCount * 2</c> long, laid out as <c>[type0, data0, type1, data1, ...]</c>
/// — passing raw values with no type tags throws <see cref="System.ArgumentException"/> from
/// RB's own dispatcher. Confirmed live 2026-09-07 for discard only — see
/// docs/addon-capture-log.md. Pon/chi/kan/riichi/tsumo/ron opcodes are not yet mapped.
/// </summary>
public sealed class EmjActionDispatcher
{
    private const ulong IntType = 3;

    /// <summary>
    /// Two-step handshake confirmed live: select the tile by its raw hand-array value, then
    /// commit by slot index (0-13). Both calls fire every time — a single call alone was
    /// observed to do nothing during initial research this project deliberately did not reuse
    /// unverified, which is why this was re-confirmed from scratch on our own client.
    /// </summary>
    public void Discard(AtkAddonControl window, int slotIndex, int rawTileValue)
    {
        window.SendAction(2, [IntType, 15, IntType, (ulong)rawTileValue]);
        Thread.Sleep(300);
        window.SendAction(2, [IntType, 7, IntType, (ulong)slotIndex]);
    }

    /// <summary>
    /// UNTESTED hypothesis, not yet confirmed live: a classic button-row call prompt (the
    /// Chi/Pass modal — see docs/addon-capture-log.md) is a leftmost-button-is-0,
    /// rightmost-is-Pass layout, dispatched the same way discard's opcode-7 commit is —
    /// FireCallback with an Int opcode and an Int option index. Guessed by analogy with the
    /// confirmed discard opcode, not captured from an actual click. Verify against a real
    /// prompt (dump AtkValues before/after) before trusting this for anything but Pass, which
    /// is safe to try regardless since worst case it's a no-op you'd otherwise do manually.
    /// </summary>
    private const int CallPromptOpcode = 11;

    /// <summary>Accepts whatever call is currently offered (leftmost button). Do not wire this into automatic play yet: melds aren't read by <see cref="EmjAddonReader"/>, so accepting would silently desync our internal hand model from the game (it would keep treating the hand as fully concealed).</summary>
    public void AcceptCall(AtkAddonControl window) =>
        window.SendAction(2, [IntType, CallPromptOpcode, IntType, 0]);

    /// <summary>Passes on a call prompt (rightmost button) — safe to automate since it keeps the hand concealed, consistent with what we currently track.</summary>
    public void Pass(AtkAddonControl window) =>
        window.SendAction(2, [IntType, CallPromptOpcode, IntType, 1]);

    /// <summary>
    /// Dismisses the hand-result "Next" screen. Confirmed live 2026-09-07 via an opcode sweep
    /// (0-13 were no-ops; 14 moved the state off 29 and was visually confirmed to click Next).
    /// </summary>
    private const int HandResultNextOpcode = 14;

    public void ClickNext(AtkAddonControl window) =>
        window.SendAction(2, [IntType, HandResultNextOpcode, IntType, 0]);

    public void CallKan(AtkAddonControl window) =>
        throw new NotSupportedException("Kan dispatch opcode not yet captured — see docs/addon-capture-log.md.");

    public void DeclareRiichi(AtkAddonControl window) =>
        throw new NotSupportedException("Riichi dispatch opcode not yet captured — see docs/addon-capture-log.md.");

    public void DeclareTsumo(AtkAddonControl window) =>
        throw new NotSupportedException("Tsumo dispatch opcode not yet captured — see docs/addon-capture-log.md.");

    public void DeclareRon(AtkAddonControl window) =>
        throw new NotSupportedException("Ron dispatch opcode not yet captured — see docs/addon-capture-log.md.");
}
