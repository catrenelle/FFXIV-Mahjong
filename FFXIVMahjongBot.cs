using System;
using System.Collections.Generic;
using System.Linq;
using ff14bot.AClasses;
using ff14bot.Behavior;
using ff14bot.Managers;
using FFXIVMahjong.Addon;
using FFXIVMahjong.Core;
using FFXIVMahjong.Policy;
using TreeSharp;

namespace FFXIVMahjong;

/// <summary>
/// RebornBuddy botbase for Doman Mahjong. Current scope (see docs/addon-capture-log.md for
/// what's confirmed vs. still unmapped): reads our own hand and discards efficiently every
/// turn. It does not yet call pon/chi/kan, declare riichi, or claim tsumo/ron — those
/// dispatch opcodes haven't been captured yet, so those decisions are left to the game's
/// own auto-pass/timeout behavior for now.
/// </summary>
public sealed class FFXIVMahjongBot : BotBase
{
    private readonly EmjAddonReader _reader = new();
    private readonly EmjActionDispatcher _dispatcher = new();
    private readonly IDiscardPolicy _discardPolicy = new EfficiencyDiscardPolicy();

    private string _lastActedHandSignature = "";

    /// <summary>
    /// When we started being blocked by <see cref="EmjAddonReader.IsCallPromptLikelyActive"/>
    /// while otherwise ready to discard. The call-prompt flag is a single-scenario heuristic
    /// (see docs/addon-capture-log.md) — real call prompts resolve in a few seconds, so if this
    /// has been blocking far longer than that, it's more likely a stale/misread flag than an
    /// actual pending decision, and we self-correct rather than freezing indefinitely.
    /// </summary>
    private DateTime? _callPromptBlockedSince;

    private static readonly TimeSpan CallPromptBlockTimeout = TimeSpan.FromSeconds(15);

    public override string Name => "FFXIV Mahjong";
    public override PulseFlags PulseFlags => PulseFlags.All;
    public override bool IsAutonomous => true;
    public override bool RequiresProfile => false;
    public override bool WantButton => true;

    // Decision-making happens in Pulse(); Root only needs to satisfy BotBase's abstract member.
    public override Composite Root => new TreeSharp.Action(_ => { });

    public override void Start()
    {
        _lastActedHandSignature = "";
        _callPromptBlockedSince = null;
    }

    public override void Pulse()
    {
        if (!_reader.TryGetWindow(out AtkAddonControl window))
            return;

        if (_reader.ReadStateCode(window) != EmjOffsets.StateOurTurnDiscard)
        {
            _callPromptBlockedSince = null;
            return;
        }

        // The base state code alone doesn't move when a call-prompt modal (Chi/Pass, etc.) is
        // layered on top of the discard surface — confirmed live 2026-09-07, see
        // docs/addon-capture-log.md. Without this check we'd risk discarding while someone
        // else's call decision (or ours) is still pending. Timed out below in case the flag
        // itself is ever wrong on a genuine discard turn.
        if (_reader.IsCallPromptLikelyActive(window))
        {
            DateTime blockedSince = _callPromptBlockedSince ?? DateTime.UtcNow;
            _callPromptBlockedSince = blockedSince;
            if (DateTime.UtcNow - blockedSince < CallPromptBlockTimeout)
                return;
        }
        else
        {
            _callPromptBlockedSince = null;
        }

        var hand = _reader.ReadSelfHand(window);
        if (hand.Concealed.Count == 0)
            return;

        string signature = string.Join(",", hand.Concealed.Select(t => t.Id).OrderBy(id => id));
        if (signature == _lastActedHandSignature)
            return; // already dispatched for this exact hand — waiting for the game to catch up

        var discard = _discardPolicy.ChooseDiscard(hand);
        int slotIndex = FindSlotForTile(window, discard);
        if (slotIndex < 0)
            return;

        _dispatcher.Discard(window, slotIndex, _reader.ReadHandSlotRaw(window, slotIndex));
        _lastActedHandSignature = signature;
    }

    private int FindSlotForTile(AtkAddonControl window, Tile tile)
    {
        int expectedRaw = EmjAddonReader.EncodeTileRaw(tile);
        for (int i = 0; i < EmjOffsets.HandSize; i++)
        {
            if (_reader.ReadHandSlotRaw(window, i) == expectedRaw)
                return i;
        }
        return -1;
    }
}
