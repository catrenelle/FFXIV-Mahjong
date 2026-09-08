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
/// what's confirmed vs. still unmapped): reads our own hand, discards efficiently every turn
/// (including after a manually-accepted call — see the meld-count inference in
/// <see cref="Pulse"/>), passes on any call prompt (Chi/Pon/etc.) rather than accepting one,
/// and dismisses the hand-result "Next" screen. It doesn't yet accept calls, declare riichi,
/// or claim tsumo/ron itself — accepting a call needs knowing which tile is being offered,
/// which isn't mapped yet (only the discard-side meld *count* is inferred, not what the
/// called tiles actually are).
/// </summary>
public sealed class FFXIVMahjongBot : BotBase
{
    private readonly EmjAddonReader _reader = new();
    private readonly EmjActionDispatcher _dispatcher = new();
    private readonly IDiscardPolicy _discardPolicy = new EfficiencyDiscardPolicy();

    private string _lastActedHandSignature = "";
    private DateTime _lastNextClickAttempt = DateTime.MinValue;
    private static readonly TimeSpan NextClickRetryInterval = TimeSpan.FromSeconds(1);

    /// <summary>When we first saw <see cref="EmjOffsets.StateHandResultNext"/>, so we can require it to hold steady before acting (see <see cref="HandResultStabilityWindow"/>).</summary>
    private DateTime? _handResultFirstSeenAt;

    /// <summary>
    /// State 29 (hand-result "Next" screen) must persist this long before we click Next. The
    /// reference FFXIV-AutoMahjongSolver project — automating the same "Emj" addon — documented
    /// firing this click during the result-modal's animation phase (i.e. right after a
    /// Chi/Pon/Tsumo/etc. resolves into the result screen) stranding the addon in a stuck state
    /// 32 that accepted no further input. Matches their empirically-tuned 3.5s window.
    /// </summary>
    private static readonly TimeSpan HandResultStabilityWindow = TimeSpan.FromSeconds(3.5);

    /// <summary>
    /// Minimum gap enforced between any two dispatched actions (Pass, Discard, Next — regardless
    /// of type). Without this, a fast state-code transition (e.g. a call prompt resolving
    /// straight into the hand-result screen) could dispatch two clicks back-to-back within the
    /// same or adjacent Pulse ticks, which is what was observed breaking the in-game UI.
    /// </summary>
    private DateTime _lastDispatchAt = DateTime.MinValue;
    private static readonly TimeSpan MinInterActionGap = TimeSpan.FromMilliseconds(500);

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

    /// <summary>
    /// Temporarily disabled (2026-09-08) for a live RE session on Chi/Pon/Kan/Riichi/Tsumo/Ron
    /// acceptance: leave call prompts untouched instead of auto-passing them, so the user can
    /// manually trigger and observe each one (which tile is offered, what dispatch it takes)
    /// instead of the bot clicking Pass before they get the chance. Re-enable once accept
    /// dispatch for those actions is implemented and confirmed live.
    /// </summary>
    private static readonly bool AutoPassCallPrompts = false;

    /// <summary>
    /// Disabled 2026-09-08 after observing state 32 ("stuck, no inputs accepted") live during
    /// real play, right after our own opcode-14 `SendAction` dispatch. The reference project
    /// documented the identical failure signature from the identical mechanism — their fix was
    /// routing through a native `ReceiveEvent(ButtonClick)` call on the button node instead of
    /// `FireCallback`, which our confirmed-live 2026-09-07 test apparently didn't rule out (it
    /// likely just got lucky on timing). Leave the "Next" screen for manual clicks until we
    /// replicate their safer dispatch mechanism (needs checking whether RB's AtkAddonControl
    /// exposes the native struct access that requires) and confirm it doesn't reproduce this.
    /// </summary>
    private static readonly bool AutoClickNext = false;

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
        _lastNextClickAttempt = DateTime.MinValue;
        _handResultFirstSeenAt = null;
        _lastDispatchAt = DateTime.MinValue;
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
                if (AutoPassCallPrompts
                    && DateTime.UtcNow - _lastPassAttempt >= PassRetryInterval
                    && DateTime.UtcNow - _lastDispatchAt >= MinInterActionGap)
                {
                    _dispatcher.Pass(window);
                    _lastPassAttempt = DateTime.UtcNow;
                    _lastDispatchAt = DateTime.UtcNow;
                }
                return; // left for manual interaction while AutoPassCallPrompts is off
            }
            // Timed out without the flag clearing — treat as a likely false positive and fall
            // through to the normal discard check below instead of blocking forever.
        }
        else
        {
            _callPromptFirstSeenAt = null;
        }

        int stateCode = _reader.ReadStateCode(window);

        if (stateCode == EmjOffsets.StateHandResultNext)
        {
            DateTime resultFirstSeen = _handResultFirstSeenAt ?? DateTime.UtcNow;
            _handResultFirstSeenAt = resultFirstSeen;

            if (DateTime.UtcNow - resultFirstSeen < HandResultStabilityWindow)
                return; // still settling — let the result-modal animation finish before clicking

            if (AutoClickNext
                && DateTime.UtcNow - _lastNextClickAttempt >= NextClickRetryInterval
                && DateTime.UtcNow - _lastDispatchAt >= MinInterActionGap)
            {
                _dispatcher.ClickNext(window);
                _lastNextClickAttempt = DateTime.UtcNow;
                _lastDispatchAt = DateTime.UtcNow;
            }
            return;
        }
        _handResultFirstSeenAt = null;

        if (stateCode != EmjOffsets.StateOurTurnDiscard
            && stateCode != EmjOffsets.StatePostDrawOrCallDiscard
            && stateCode != EmjOffsets.StatePostCallDiscard)
            return;

        var hand = _reader.ReadSelfHand(window);

        // Our turn to discard whenever the concealed count is 14 minus a multiple of 3
        // (14/11/8/5/2) — each prior call (chi/pon/kan) takes 3 tile-slots out of the
        // 14-tile-equivalent total. EmjAddonReader doesn't read melds (confirmed live
        // 2026-09-08: a post-Chi hand reads as an 11-tile fully-concealed hand, not a
        // hand+meld), but Shanten/Ukeire only ever consult hand.Melds.Count, never the
        // melds' actual tiles (see Engine/Shanten.cs, Engine/Ukeire.cs) — so placeholder
        // melds of the right *count* are enough to get a correct discard choice without
        // knowing which tiles were actually called.
        int concealedCount = hand.Concealed.Count;
        if (concealedCount == 0 || concealedCount % 3 != 2)
            return; // mid-transition read, not a genuine discard-turn shape

        int meldCount = (EmjOffsets.HandSize - concealedCount) / 3;
        var handForPolicy = meldCount == 0 ? hand : new Hand(hand.Concealed, PlaceholderMelds(meldCount));

        string signature = string.Join(",", hand.Concealed.Select(t => t.Id).OrderBy(id => id));
        if (signature == _lastActedHandSignature)
            return; // already dispatched for this exact hand — waiting for the game to catch up

        if (DateTime.UtcNow - _lastDispatchAt < MinInterActionGap)
            return;

        var discard = _discardPolicy.ChooseDiscard(handForPolicy);
        int slotIndex = FindSlotForTile(window, discard);
        if (slotIndex < 0)
            return;

        _dispatcher.Discard(window, slotIndex, _reader.ReadHandSlotRaw(window, slotIndex));
        _lastActedHandSignature = signature;
        _lastDispatchAt = DateTime.UtcNow;
    }

    private static IReadOnlyList<Meld> PlaceholderMelds(int count)
    {
        var placeholder = Meld.Pon(Tile.FromSuitRank(Suit.Man, 1), RelativeSeat.Kamicha);
        return Enumerable.Repeat(placeholder, count).ToList();
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
