using System;
using System.Collections.Generic;
using System.Linq;
using ff14bot.AClasses;
using ff14bot.Behavior;
using ff14bot.Helpers;
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

    /// <summary>
    /// AtkValues snapshot from the most recent Pulse where no call prompt was active — kept
    /// fresh every tick so that the instant a prompt appears, we can diff against a snapshot
    /// from milliseconds earlier instead of guessing at a single dump in isolation. Tonight's
    /// RE session got fooled twice by values that looked like a match but were actually stale
    /// contents left over from a *previous* prompt (see docs/addon-capture-log.md) — a real
    /// diff catches that automatically, since a stale value won't show up as "changed".
    /// </summary>
    private EmjAddonReader.AtkValueSnapshot[]? _lastNormalAtkSnapshot;

    /// <summary>Same idea as <see cref="_lastNormalAtkSnapshot"/> but for the addon's raw struct memory — covers fields that never go through AtkValues at all (confirmed live 2026-09-08: a Chi's offered tile changed nothing in a 109-entry AtkValues diff, so it must live here instead).</summary>
    private int[]? _lastNormalRawSnapshot;

    /// <summary>Same idea again, but for the separate "AgentEmj"-equivalent backing structure (via <see cref="EmjAddonReader.DumpAgentEmjRawMemorySnapshot"/>) rather than the UI addon's own memory — tried after both AtkValues and the addon's raw struct memory came up completely clean on real diffs (2026-09-08).</summary>
    private int[]? _lastNormalAgentSnapshot;
    private bool _agentResolutionLogged;

    /// <summary>
    /// Snapshots taken the instant a call prompt first appears (not just the pre-prompt
    /// baseline), kept so we can diff again a bit later — catches a field that gets written
    /// asynchronously (e.g. a delayed network round-trip) rather than in the same tick the
    /// prompt itself becomes visible, which the immediate justAppeared diff would miss.
    /// </summary>
    private EmjAddonReader.AtkValueSnapshot[]? _callPromptAppearedAtkSnapshot;
    private int[]? _callPromptAppearedRawSnapshot;
    private int[]? _callPromptAppearedAgentSnapshot;
    private bool _callPromptDelayedDiffLogged;
    private static readonly TimeSpan CallPromptDelayedDiffDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>Snapshot taken the instant state 29 (hand-result screen) first appears, so we can diff it against the snapshot right as <see cref="HandResultStabilityWindow"/> elapses — hunting for whatever flag flips when the Next button visibly becomes clickable (user-observed 2026-09-08).</summary>
    private EmjAddonReader.AtkValueSnapshot[]? _handResultFirstSeenSnapshot;
    private bool _handResultReadyDiffLogged;

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
        _lastNormalAtkSnapshot = null;
        _lastNormalRawSnapshot = null;
        _lastNormalAgentSnapshot = null;
        _callPromptAppearedAtkSnapshot = null;
        _callPromptAppearedRawSnapshot = null;
        _callPromptAppearedAgentSnapshot = null;
        _callPromptDelayedDiffLogged = false;
        _handResultFirstSeenSnapshot = null;
        _handResultReadyDiffLogged = false;
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
            bool justAppeared = _callPromptFirstSeenAt is null;
            DateTime firstSeen = _callPromptFirstSeenAt ?? DateTime.UtcNow;
            _callPromptFirstSeenAt = firstSeen;

            if (justAppeared)
            {
                _callPromptDelayedDiffLogged = false;

                if (_lastNormalAtkSnapshot is { } before)
                {
                    var after = _reader.DumpAtkValueSnapshot(window);
                    LogAtkValueDiff("call-prompt appeared", before, after);
                    _callPromptAppearedAtkSnapshot = after;
                }
                if (_lastNormalRawSnapshot is { } rawBefore)
                {
                    var rawAfter = _reader.DumpRawMemorySnapshot(window);
                    LogRawMemoryDiff("call-prompt appeared (addon)", rawBefore, rawAfter);
                    _callPromptAppearedRawSnapshot = rawAfter;
                }
                if (_lastNormalAgentSnapshot is { } agentBefore)
                {
                    var agentAfter = _reader.DumpAgentEmjRawMemorySnapshot();
                    if (agentAfter is not null)
                        LogRawMemoryDiff("call-prompt appeared (AgentEmj)", agentBefore, agentAfter);
                    _callPromptAppearedAgentSnapshot = agentAfter;
                }
            }
            else if (!_callPromptDelayedDiffLogged && DateTime.UtcNow - firstSeen >= CallPromptDelayedDiffDelay)
            {
                // Catches a field that gets written a moment after the prompt itself becomes
                // visible (e.g. an async network round-trip) instead of in the very same tick.
                _callPromptDelayedDiffLogged = true;
                if (_callPromptAppearedAtkSnapshot is { } atkBase)
                    LogAtkValueDiff("call-prompt +1.5s", atkBase, _reader.DumpAtkValueSnapshot(window));
                if (_callPromptAppearedRawSnapshot is { } rawBase)
                    LogRawMemoryDiff("call-prompt +1.5s (addon)", rawBase, _reader.DumpRawMemorySnapshot(window));
                if (_callPromptAppearedAgentSnapshot is { } agentBase)
                {
                    var agentNow = _reader.DumpAgentEmjRawMemorySnapshot();
                    if (agentNow is not null)
                        LogRawMemoryDiff("call-prompt +1.5s (AgentEmj)", agentBase, agentNow);
                }
            }

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
            _callPromptAppearedAtkSnapshot = null;
            _callPromptAppearedRawSnapshot = null;
            _callPromptAppearedAgentSnapshot = null;
            _callPromptDelayedDiffLogged = false;
            _lastNormalAtkSnapshot = _reader.DumpAtkValueSnapshot(window);
            _lastNormalRawSnapshot = _reader.DumpRawMemorySnapshot(window);
            _lastNormalAgentSnapshot = _reader.DumpAgentEmjRawMemorySnapshot();
            if (!_agentResolutionLogged)
            {
                _agentResolutionLogged = true;
                Logging.Write(_lastNormalAgentSnapshot is null
                    ? "[FFXIVMahjong] AgentEmj (id=5) did not resolve — hypothesis may be wrong for this client"
                    : "[FFXIVMahjong] AgentEmj (id=5) resolved successfully, will diff it at the next call prompt");
            }
        }

        int stateCode = _reader.ReadStateCode(window);

        if (stateCode == EmjOffsets.StateHandResultNext)
        {
            bool justAppeared = _handResultFirstSeenAt is null;
            DateTime resultFirstSeen = _handResultFirstSeenAt ?? DateTime.UtcNow;
            _handResultFirstSeenAt = resultFirstSeen;

            if (justAppeared)
            {
                _handResultFirstSeenSnapshot = _reader.DumpAtkValueSnapshot(window);
                _handResultReadyDiffLogged = false;
            }

            bool stable = DateTime.UtcNow - resultFirstSeen >= HandResultStabilityWindow;
            if (stable && !_handResultReadyDiffLogged && _handResultFirstSeenSnapshot is { } beforeReady)
            {
                var afterReady = _reader.DumpAtkValueSnapshot(window);
                LogAtkValueDiff("hand-result stability window elapsed", beforeReady, afterReady);
                _handResultReadyDiffLogged = true;
            }

            if (!stable)
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
        _handResultFirstSeenSnapshot = null;
        _handResultReadyDiffLogged = false;

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

    /// <summary>Logs every AtkValues index that differs between two snapshots — used to catch what actually changed at a state transition instead of guessing from one dump in isolation.</summary>
    private static void LogAtkValueDiff(string label, EmjAddonReader.AtkValueSnapshot[] before, EmjAddonReader.AtkValueSnapshot[] after)
    {
        Logging.Write($"[FFXIVMahjong] atk diff ({label}): before.Length={before.Length} after.Length={after.Length}");
        int max = Math.Max(before.Length, after.Length);
        for (int i = 0; i < max; i++)
        {
            var b = i < before.Length ? before[i] : default;
            var a = i < after.Length ? after[i] : default;
            if (b.Type != a.Type || b.Int != a.Int || b.Text != a.Text)
                Logging.Write($"[FFXIVMahjong]   [{i}] {Describe(b)} -> {Describe(a)}");
        }
    }

    private static string Describe(EmjAddonReader.AtkValueSnapshot v) =>
        v.Text is not null ? $"{v.Type}:\"{v.Text}\"" : $"{v.Type}:{v.Int}";

    /// <summary>Logs every raw-memory int32 that differs between two snapshots, byte-offset-addressed to line up with EmjOffsets constants. Annotates anything that plausibly decodes as a tile id (bare 0-33, or texture-offset) so a match doesn't need manual arithmetic to spot.</summary>
    private static void LogRawMemoryDiff(string label, int[] before, int[] after)
    {
        Logging.Write($"[FFXIVMahjong] raw diff ({label}): before.Length={before.Length} after.Length={after.Length}");
        int max = Math.Min(before.Length, after.Length);
        for (int i = 0; i < max; i++)
        {
            if (before[i] == after[i])
                continue;
            int offset = i * 4;
            string note = DecodeTileGuess(after[i]);
            Logging.Write($"[FFXIVMahjong]   +0x{offset:X4} {before[i]} -> {after[i]}{note}");
        }
    }

    private static string DecodeTileGuess(int raw)
    {
        if (raw is >= 0 and < Tile.KindCount)
            return $" (bare tile id {raw} = {new Tile(raw)})";
        int textureOffsetId = raw - EmjOffsets.TileTextureBase;
        if (textureOffsetId is >= 0 and < Tile.KindCount)
            return $" (texture-offset tile id {textureOffsetId} = {new Tile(textureOffsetId)})";
        return "";
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
