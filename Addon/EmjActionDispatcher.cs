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

    public void CallPon(AtkAddonControl window) =>
        throw new NotSupportedException("Pon dispatch opcode not yet captured — see docs/addon-capture-log.md.");

    public void CallChi(AtkAddonControl window, int variantIndex) =>
        throw new NotSupportedException("Chi dispatch opcode not yet captured — see docs/addon-capture-log.md.");

    public void CallKan(AtkAddonControl window) =>
        throw new NotSupportedException("Kan dispatch opcode not yet captured — see docs/addon-capture-log.md.");

    public void DeclareRiichi(AtkAddonControl window) =>
        throw new NotSupportedException("Riichi dispatch opcode not yet captured — see docs/addon-capture-log.md.");

    public void DeclareTsumo(AtkAddonControl window) =>
        throw new NotSupportedException("Tsumo dispatch opcode not yet captured — see docs/addon-capture-log.md.");

    public void DeclareRon(AtkAddonControl window) =>
        throw new NotSupportedException("Ron dispatch opcode not yet captured — see docs/addon-capture-log.md.");

    public void Pass(AtkAddonControl window) =>
        throw new NotSupportedException("Pass dispatch opcode not yet captured — see docs/addon-capture-log.md.");
}
