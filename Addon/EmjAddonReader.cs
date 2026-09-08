using System;
using System.Collections.Generic;
using System.Linq;
using ff14bot.Enums;
using ff14bot.Managers;
using ff14bot.RemoteWindows;
using FFXIVMahjong.Core;
using RBCore = ff14bot.Core; // "Core" alone collides with our own FFXIVMahjong.Core namespace

namespace FFXIVMahjong.Addon;

/// <summary>
/// Reads the live "Emj" addon's memory into our domain types. Offsets confirmed against a
/// live client 2026-09-07 — see docs/addon-capture-log.md. Melds (pon/chi/kan) and opponent
/// discard piles are not read yet — see the same log for what's still unmapped.
/// </summary>
public sealed class EmjAddonReader
{
    public bool TryGetWindow(out AtkAddonControl window)
    {
        RaptureAtkUnitManager.Update();
        window = RaptureAtkUnitManager.GetWindowByName(EmjOffsets.WindowName);
        return window is { IsVisible: true };
    }

    public int ReadStateCode(AtkAddonControl window)
    {
        var values = ReadAtkValues(window);
        return values.Length > EmjOffsets.StateCodeAtkValueIndex
            ? values[EmjOffsets.StateCodeAtkValueIndex].Int
            : -1;
    }

    /// <summary>
    /// True when a call-prompt modal (Chi/Pass, and hypothetically pon/kan/riichi) appears to
    /// be open on top of the base discard surface — see <see cref="EmjOffsets.CallPromptFlagAtkValueIndex"/>
    /// for how confident this is. The base state code alone doesn't change for this (it can
    /// still read 30 while a prompt is up), which is why this check exists.
    /// </summary>
    public bool IsCallPromptLikelyActive(AtkAddonControl window)
    {
        var values = ReadAtkValues(window);
        int i = EmjOffsets.CallPromptFlagAtkValueIndex;
        return values.Length > i && values[i].AtkValueType == AtkValueType.Bool && values[i].Bool;
    }

    public Hand ReadSelfHand(AtkAddonControl window)
    {
        var tiles = new List<Tile>(EmjOffsets.HandSize);
        for (int i = 0; i < EmjOffsets.HandSize; i++)
        {
            var tile = DecodeTile(ReadHandSlotRaw(window, i));
            if (tile.HasValue)
                tiles.Add(tile.Value);
        }
        return new Hand(tiles); // melds not read yet — an open hand currently looks fully concealed
    }

    public int ReadHandSlotRaw(AtkAddonControl window, int slotIndex) =>
        RBCore.Memory.Read<int>(window.Pointer + EmjOffsets.HandArrayStart + slotIndex * 4);

    public int ReadSelfScore(AtkAddonControl window) => RBCore.Memory.Read<int>(window.Pointer + EmjOffsets.SelfScore);
    public int ReadShimochaScore(AtkAddonControl window) => RBCore.Memory.Read<int>(window.Pointer + EmjOffsets.ShimochaScore);
    public int ReadToimenScore(AtkAddonControl window) => RBCore.Memory.Read<int>(window.Pointer + EmjOffsets.ToimenScore);
    public int ReadKamichaScore(AtkAddonControl window) => RBCore.Memory.Read<int>(window.Pointer + EmjOffsets.KamichaScore);

    public byte ReadSelfDiscardCount(AtkAddonControl window) =>
        RBCore.Memory.Read<byte>(window.Pointer + EmjOffsets.SelfDiscardCount);

    public Tile? ReadDoraIndicator(AtkAddonControl window) =>
        DecodeTile(RBCore.Memory.Read<int>(window.Pointer + EmjOffsets.DoraIndicator));

    /// <summary>
    /// Aka-dora (red five) offset, confirmed live 2026-09-07: a red 5m read as raw value
    /// (normal 5m's raw) + 30 — i.e. relative id 34 instead of 4. Only 34 (red 5m) is
    /// independently confirmed; 43 (red 5p) and 52 (red 5s) are inferred from the same
    /// per-suit +9 spacing the normal tile ids already use, not yet confirmed live.
    /// </summary>
    private const int AkaDoraRelativeIdOffset = 30;

    /// <summary>Inverse of <see cref="DecodeTile"/> — the raw hand-array value this tile would appear as.</summary>
    public static int EncodeTileRaw(Tile tile) =>
        EmjOffsets.TileTextureBase + tile.Id + (tile.IsRedFive ? AkaDoraRelativeIdOffset : 0);

    private static Tile? DecodeTile(int raw)
    {
        if (raw == 0)
            return null; // empty slot
        int id = raw - EmjOffsets.TileTextureBase;
        if (id is >= 0 and < Tile.KindCount)
            return new Tile(id);
        int akaBaseId = id - AkaDoraRelativeIdOffset;
        if (akaBaseId is 4 or 13 or 22) // 5m, 5p, 5s
            return new Tile(akaBaseId, isRedFive: true);
        return null; // out-of-range: don't feed garbage into the engine
    }

    private static TwoInt[] ReadAtkValues(AtkAddonControl window)
    {
        ushort count = RBCore.Memory.Read<ushort>(window.Pointer + EmjOffsets.AtkValuesCount);
        if (count == 0 || count > 200)
            return [];
        nint ptr = RBCore.Memory.Read<nint>(window.Pointer + EmjOffsets.AtkValuesPointer);
        return RBCore.Memory.ReadArray<TwoInt>(ptr, count);
    }
}
