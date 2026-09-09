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
    /// Aka-dora (red five) ids extend sequentially right after the normal 0-33 range: 34 = red
    /// 5m, 35 = red 5p, 36 = red 5s. Corrected 2026-09-07 from an earlier "+30 from the normal
    /// tile's raw value" guess that only fit red 5m by coincidence (4+30 happens to equal 34);
    /// a second live capture of a drawn red 5s (raw = textureBase+36, alongside an already-held
    /// normal 5s) broke that formula (22+30=52, not 36) and fits this simpler one instead. Red
    /// 5m (34) and red 5s (36) are both confirmed live; red 5p (35) is inferred from the pattern,
    /// not yet independently confirmed.
    /// </summary>
    private static readonly Dictionary<int, int> AkaDoraIdToBaseId = new() { [34] = 4, [35] = 13, [36] = 22 };
    private static readonly Dictionary<int, int> BaseIdToAkaDoraId = new() { [4] = 34, [13] = 35, [22] = 36 };

    /// <summary>Inverse of <see cref="DecodeTile"/> — the raw hand-array value this tile would appear as.</summary>
    public static int EncodeTileRaw(Tile tile) =>
        EmjOffsets.TileTextureBase + (tile.IsRedFive ? BaseIdToAkaDoraId[tile.Id] : tile.Id);

    private static Tile? DecodeTile(int raw)
    {
        if (raw == 0)
            return null; // empty slot
        int id = raw - EmjOffsets.TileTextureBase;
        if (id is >= 0 and < Tile.KindCount)
            return new Tile(id);
        return AkaDoraIdToBaseId.TryGetValue(id, out int baseId)
            ? new Tile(baseId, isRedFive: true)
            : null; // out-of-range: don't feed garbage into the engine
    }

    private static TwoInt[] ReadAtkValues(AtkAddonControl window)
    {
        ushort count = RBCore.Memory.Read<ushort>(window.Pointer + EmjOffsets.AtkValuesCount);
        if (count == 0 || count > 200)
            return [];
        nint ptr = RBCore.Memory.Read<nint>(window.Pointer + EmjOffsets.AtkValuesPointer);
        return RBCore.Memory.ReadArray<TwoInt>(ptr, count);
    }

    /// <summary>
    /// A lightweight per-index snapshot of the AtkValues array, for diffing between two points
    /// in time (e.g. right before vs. right after a call prompt appears) instead of eyeballing
    /// one dump in isolation — see <see cref="FFXIVMahjongBot"/>'s call-prompt diff capture.
    /// </summary>
    public readonly record struct AtkValueSnapshot(AtkValueType Type, int Int);

    public AtkValueSnapshot[] DumpAtkValueSnapshot(AtkAddonControl window)
    {
        var values = ReadAtkValues(window);
        var snapshot = new AtkValueSnapshot[values.Length];
        for (int i = 0; i < values.Length; i++)
            snapshot[i] = new AtkValueSnapshot(values[i].AtkValueType, values[i].Int);
        return snapshot;
    }

    /// <summary>
    /// Bytes of <c>AtkUnitBase*</c>-relative memory covered by a raw snapshot — matches the
    /// reference project's own "production reads max out at DoraIndicator (+0x0FD8)" budget
    /// with headroom, since we don't know exactly where an as-yet-unmapped field (e.g. a
    /// call's offered tile) might live.
    /// </summary>
    private const int RawMemorySnapshotSize = 0x3000;

    /// <summary>Raw int32s across the addon's struct memory (not the separately-allocated AtkValues array), for diffing the same way as <see cref="DumpAtkValueSnapshot"/> when a field isn't published through AtkValues at all.</summary>
    public int[] DumpRawMemorySnapshot(AtkAddonControl window) =>
        RBCore.Memory.ReadArray<int>(window.Pointer, RawMemorySnapshotSize / 4);

    /// <summary>
    /// Internal RB "Agent" id for the Doman Mahjong backing structure — distinct from the "Emj"
    /// UI addon's own <c>AtkUnitBase</c> memory. Matches the reference project's own AgentId=5
    /// for the same client feature (their <c>AgentModule.GetAgentByInternalId((AgentId)5)</c>).
    /// Base-game agent ids are typically stable across client builds/regions since they're part
    /// of the core client rather than addon-specific, but this hasn't been independently
    /// confirmed for our client — <see cref="DumpAgentEmjRawMemorySnapshot"/> returns null if it
    /// doesn't resolve, so a wrong id fails safe instead of reading garbage.
    /// </summary>
    private const int EmjAgentId = 5;

    private const int AgentMemorySnapshotSize = 0x2000;

    /// <summary>
    /// Raw int32s from the "AgentEmj"-equivalent backing structure via RB's own
    /// <see cref="AgentModule.GetAgentInterfaceById"/>, rather than the UI addon's own memory —
    /// worth checking since a real diff ruled out both AtkValues and the addon's own struct
    /// memory for the offered-tile field (confirmed live 2026-09-08, see docs/addon-capture-log.md).
    /// </summary>
    public int[]? DumpAgentEmjRawMemorySnapshot()
    {
        try
        {
            var agent = AgentModule.GetAgentInterfaceById(EmjAgentId);
            if (agent is not { IsValid: true } || agent.Pointer == IntPtr.Zero)
                return null;
            return RBCore.Memory.ReadArray<int>(agent.Pointer, AgentMemorySnapshotSize / 4);
        }
        catch
        {
            return null; // unverified id/API shape — fail safe rather than crash the bot's Pulse loop
        }
    }
}
