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
/// what's confirmed vs. still unmapped): reads our own hand, discards efficiently every turn,
/// and passes on any call prompt (Chi/Pon/etc.) rather than accepting one. It doesn't yet
/// accept calls, declare riichi, or claim tsumo/ron — accepting a call needs two things we
/// don't have yet: knowing which tile is being offered, and meld tracking (so our internal
/// hand model doesn't desync once a meld exists).
/// </summary>
public sealed class FFXIVMahjongBot : BotBase
{
    private readonly EmjAddonReader _reader = new();
    private readonly EmjActionDispatcher _dispatcher = new();
    private readonly IDiscardPolicy _discardPolicy = new EfficiencyDiscardPolicy();

    private string _lastActedHandSignature = "";

    /// <summary>
    /// When we first saw <see cref="EmjAddonReader.IsCallPromptLikelyActive"/> go true. The
    /// flag is a single-scenario heuristic (see docs/addon-capture-log.md) — real call prompts
    /// resolve in a few seconds once we start clicking Pass, so if this has been active far
    /// longer than that, it's more likely a stale/misread flag on a genuine discard turn than
    /// an actual pending decision, and we stop trusting it rather than freezing indefinitely.
    /// </summary>
    private DateTime? _callPromptFirstSeenAt;

    private DateTime _lastPassAttempt = DateTime.MinValue;

    private static readonly TimeSpan CallPromptBlockTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PassRetryInterval = TimeSpan.FromSeconds(1);

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
        _callPromptFirstSeenAt = null;
        _lastPassAttempt = DateTime.MinValue;
    }

    public override void Pulse()
    {
        if (!_reader.TryGetWindow(out AtkAddonControl window))
            return;

        // Confirmed live 2026-09-07: call prompts (Chi/Pass) can appear at more than one base
        // state code (seen at both 30 and 15), so this is checked before the discard-state
        // check below, not gated behind it. Untested dispatch opcode (see
        // EmjActionDispatcher.Pass) — timed out below in case it's wrong or the flag itself
        // misreads on a genuine discard turn.
        if (_reader.IsCallPromptLikelyActive(window))
        {
            DateTime firstSeen = _callPromptFirstSeenAt ?? DateTime.UtcNow;
            _callPromptFirstSeenAt = firstSeen;

            if (DateTime.UtcNow - firstSeen < CallPromptBlockTimeout)
            {
                if (DateTime.UtcNow - _lastPassAttempt >= PassRetryInterval)
                {
                    _dispatcher.Pass(window);
                    _lastPassAttempt = DateTime.UtcNow;
                }
                return;
            }
            // Timed out without the flag clearing — treat as a likely false positive and fall
            // through to the normal discard check below instead of blocking forever.
        }
        else
        {
            _callPromptFirstSeenAt = null;
        }

        int stateCode = _reader.ReadStateCode(window);
        if (stateCode != EmjOffsets.StateOurTurnDiscard && stateCode != EmjOffsets.StatePostDrawOrCallDiscard)
            return;

        var hand = _reader.ReadSelfHand(window);

        // State 6 also covers the genuine post-call reduced-hand case, which we can't
        // correctly evaluate yet (melds aren't tracked, so a reduced hand would be misread as
        // the whole hand) — only act on it when we see a full, unreduced 14-tile hand. State
        // 30 doesn't need this guard since we've never observed it with anything but 14.
        if (hand.Concealed.Count != EmjOffsets.HandSize)
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
