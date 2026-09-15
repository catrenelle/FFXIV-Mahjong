using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using ff14bot.AClasses;
using ff14bot.Behavior;
using ff14bot.Helpers;
using ff14bot.Managers;
using FFXIVMahjong.Addon;
using FFXIVMahjong.Core;
using FFXIVMahjong.Engine;
using FFXIVMahjong.Policy;
using FFXIVMahjong.Rules;
using TreeSharp;
using RBCore = ff14bot.Core; // "Core" alone collides with our own FFXIVMahjong.Core namespace

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
    private readonly ICallPolicy _callPolicy = new HeuristicCallPolicy();
    private readonly IRiichiPolicy _riichiPolicy = new StandardRiichiPolicy();
    private readonly IRuleSet _ruleSet = new DomanRuleSet();
    private readonly MeldTracker _meldTracker = new();

    /// <summary>Drives <see cref="MeldTracker.Reset"/> — self discard count only ever increases within a hand, so a drop means a new hand started.</summary>
    private int _lastSelfDiscardCountForMeldTracking = -1;
    private bool _meldCountMismatchLogged;

    private string _lastActedHandSignature = "";

    /// <summary>Hand signature we last logged a TSUMO/RIICHI pause for — logs once per hand, not every Pulse tick, while auto-discard stays paused on repeat ticks regardless.</summary>
    private string? _lastTsumoLoggedSignature;
    private string? _lastRiichiLoggedSignature;
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
    /// Deployed alongside the botbase's own source (see AGENTS/project memory on the one-way
    /// dev→live copy) — Assets/ isn't part of the historical .cs-only deploy set, so it needs
    /// copying there too whenever this changes.
    /// </summary>
    private const string TileReferenceDirectory = @"F:\Files\RebornBuddy\BotBases\FFXIVMahjong\Assets\TileReference";

    /// <summary>Dev tree, not the live deploy — Claude can read these directly to debug a detection without a separate manual console snippet.</summary>
    private const string DebugCaptureDirectory = @"C:\Projects\FFXIVMahjong\.scratch";

    private readonly ScreenTileMatcher? _tileMatcher = TryLoadTileMatcher();

    private static ScreenTileMatcher? TryLoadTileMatcher()
    {
        try
        {
            return new ScreenTileMatcher(TileReferenceDirectory);
        }
        catch (Exception ex)
        {
            Logging.Write($"[FFXIVMahjong] ScreenTileMatcher failed to load reference images from {TileReferenceDirectory}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Read-only diagnostic bridge to Claude: drop a command file in <see cref="DebugCaptureDirectory"/>
    /// (same dev-tree scratch folder the screen-match debug captures already write to, already
    /// directly readable without a human relaying RebornConsole output) and this reads it back
    /// out as a response file, once per Pulse tick. Deliberately read-only — no dispatch actions
    /// here. The reference project's own history has three separate incidents where a
    /// speculative *write* opcode corrupted the game into an unrecoverable state (see the
    /// dispatch-protocol hypotheses section of docs/addon-capture-log.md); a read-only bridge
    /// carries none of that risk, since the worst case is failing to answer, not acting wrongly.
    /// One-shot: the command file is deleted immediately after being read (before dispatch), so
    /// a stale command left over from a previous session is never silently reprocessed.
    /// </summary>
    private void ProcessClaudeBridgeCommand()
    {
        string commandPath = Path.Combine(DebugCaptureDirectory, "claude_command.txt");
        if (!File.Exists(commandPath))
            return;

        string action = "";
        var args = new Dictionary<string, string>();
        try
        {
            foreach (var line in File.ReadAllLines(commandPath))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                string key = line[..eq].Trim();
                string value = line[(eq + 1)..].Trim();
                if (key == "action")
                    action = value;
                else
                    args[key] = value;
            }
        }
        finally
        {
            File.Delete(commandPath); // one-shot regardless of whether parsing/dispatch below succeeds
        }

        var lines = new List<string> { $"action={action}" };
        bool success = true;
        try
        {
            if (action == "TryWindowByName")
            {
                // Doesn't need "Emj" itself to be resolvable — the whole point is checking for a
                // *different* window name when Emj isn't found (2026-09-15: switching the in-game
                // "High Resolution" layout setting made "Emj" stop resolving entirely, and
                // EmjOffsets.WindowName's own doc comment already flags an unverified "EmjL"
                // variant that was never actually tested against a real client).
                AppendBridgeTryWindowByName(lines, args);
            }
            else if (!_reader.TryGetWindow(out AtkAddonControl window))
            {
                lines.Add("windowFound=false");
            }
            else
            {
                lines.Add("windowFound=true");
                switch (action)
                {
                    case "DumpAll":
                        AppendBridgeSummary(lines, window);
                        break;
                    case "DumpAtkValues":
                        AppendBridgeAtkValues(lines, window);
                        break;
                    case "ReadHand":
                        AppendBridgeHand(lines, window);
                        break;
                    case "DumpAgentInfo":
                        AppendBridgeAgentInfo(lines, window);
                        break;
                    case "DumpRawMemory":
                        AppendBridgeRawMemory(lines, window);
                        break;
                    case "DumpAgentRawMemory":
                        AppendBridgeAgentRawMemory(lines, window);
                        break;
                    case "ListAgents":
                        AppendBridgeAgentList(lines, window);
                        break;
                    case "ListWindowAgents":
                        AppendBridgeWindowAgentList(lines);
                        break;
                    case "DumpAgentChecksums":
                        AppendBridgeAgentChecksums(lines, args);
                        break;
                    case "DumpAgentsRaw":
                        AppendBridgeAgentsRaw(lines, args);
                        break;
                    case "SearchAgentsForValue":
                        AppendBridgeAgentValueSearch(lines, args);
                        break;
                    case "FindPattern":
                        AppendBridgeFindPattern(lines, args);
                        break;
                    case "DumpBytesAtAddress":
                        AppendBridgeBytesAtAddress(lines, args);
                        break;
                    case "ScanAgentsForPointerAt":
                        AppendBridgeScanAgentsForPointerAt(lines, args);
                        break;
                    default:
                        success = false;
                        lines.Add($"error=unknown action '{action}'");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            success = false;
            lines.Add($"error={ex.Message}");
        }
        lines.Insert(0, $"success={success}");

        File.WriteAllLines(Path.Combine(DebugCaptureDirectory, "claude_response.txt"), lines);
    }

    private void AppendBridgeHand(List<string> lines, AtkAddonControl window)
    {
        var hand = _reader.ReadSelfHand(window);
        lines.Add($"hand={string.Join(",", hand.Concealed)}");
    }

    private void AppendBridgeSummary(List<string> lines, AtkAddonControl window)
    {
        lines.Add($"stateCode={_reader.ReadStateCode(window)}");
        lines.Add($"callPromptActive={_reader.IsCallPromptLikelyActive(window)}");
        AppendBridgeHand(lines, window);
        lines.Add($"selfScore={_reader.ReadSelfScore(window)}");
        lines.Add($"shimochaScore={_reader.ReadShimochaScore(window)}");
        lines.Add($"toimenScore={_reader.ReadToimenScore(window)}");
        lines.Add($"kamichaScore={_reader.ReadKamichaScore(window)}");
        lines.Add($"selfDiscardCount={_reader.ReadSelfDiscardCount(window)}");
        var dora = _reader.ReadDoraIndicator(window);
        lines.Add($"doraIndicator={(dora.HasValue ? dora.Value.ToString() : "(none)")}");
    }

    private void AppendBridgeAtkValues(List<string> lines, AtkAddonControl window)
    {
        var snapshot = _reader.DumpAtkValueSnapshot(window);
        for (int i = 0; i < snapshot.Length; i++)
        {
            var v = snapshot[i];
            lines.Add($"atk[{i}]={v.Type}:{v.Text ?? v.Int.ToString()}");
        }
    }

    /// <summary>
    /// Full raw-memory dump (the addon's own ~0x3000-byte struct, not AtkValues), annotated the
    /// same way the old auto-logging (removed this session, see <see cref="DecodeTileGuess"/>
    /// history) used to: flags anything that plausibly decodes as a tile id, bare 0-33 or
    /// texture-offset. Added 2026-09-14 to hunt for opponent discard-pile tile arrays — pull two
    /// of these a discard apart and diff offline to look for a value that changed by exactly one
    /// new tile id, the same methodology that found the self-hand array and score offsets.
    /// </summary>
    private void AppendBridgeRawMemory(List<string> lines, AtkAddonControl window)
    {
        var raw = _reader.DumpRawMemorySnapshot(window);
        for (int i = 0; i < raw.Length; i++)
        {
            int offset = i * 4;
            string note = DecodeTileGuess(raw[i]);
            lines.Add($"raw[0x{offset:X4}]={raw[i]}{note}");
        }
    }

    /// <summary>
    /// Same idea as <see cref="AppendBridgeRawMemory"/> but for the resolved AgentEmj structure
    /// (game-logic backing store, not the UI addon's own struct) — added 2026-09-14 because the
    /// only prior AgentEmj testing (2026-09-08) was narrowly scoped to short before/after windows
    /// around a single call prompt for the offered-tile hunt, never a broad multi-round diff like
    /// the one that found the discard-count bytes in the window struct. Melds/piles are
    /// persistent game state, not transient UI flicker, so the agent (not the window) is the more
    /// likely home for them if they're readable at all.
    /// </summary>
    private void AppendBridgeAgentRawMemory(List<string> lines, AtkAddonControl window)
    {
        var raw = _reader.DumpAgentEmjRawMemorySnapshot(window);
        if (raw is null)
        {
            lines.Add("agentResolved=false");
            return;
        }
        for (int i = 0; i < raw.Length; i++)
        {
            int offset = i * 4;
            string note = DecodeTileGuess(raw[i]);
            lines.Add($"agentRaw[0x{offset:X4}]={raw[i]}{note}");
        }
    }

    /// <summary>
    /// Enumerates every currently-active agent (not just the one bound to the Emj window) —
    /// added 2026-09-14 after <see cref="AppendBridgeAgentRawMemory"/> showed the resolved agent
    /// (id 328) has zero per-round mutable state at all, meaning <c>TryFindAgentInterface()</c>
    /// may simply be resolving the wrong agent for live game data (e.g. a settings/UI-toggle
    /// agent rather than the actual mahjong game-logic one). First attempt used
    /// <c>FindAgentIdByVtable</c>/<c>GetAgentInterfaceById</c> per the documented lookup path, but
    /// that resolved id=-1 (with an identical garbage pointer) for all 509 live vtables — it's
    /// evidently only wired up for RB's own known/registered agent types, useless for an obscure
    /// addon like Doman Mahjong's. Pivoted to reading <c>RealAgentVtables</c>/<c>AgentPointers</c>
    /// directly as parallel index-aligned lists instead, which sidesteps the broken id lookup
    /// entirely — we only need a pointer to read memory from, not a resolved id. Also reports
    /// which list index matches our already-confirmed agent 328's known
    /// vtable/pointer (see <see cref="AppendBridgeAgentInfo"/>), to validate the index-pairing
    /// assumption against known-good ground truth before trusting any of the other entries.
    /// </summary>
    private void AppendBridgeAgentList(List<string> lines, AtkAddonControl window)
    {
        try
        {
            var vtables = AgentModule.RealAgentVtables;
            var pointers = AgentModule.AgentPointers;
            lines.Add($"vtableCount={vtables.Count} pointerCount={pointers.Count}");

            var known = _reader.TryDescribeResolvedAgent(window);
            if (known is { } k)
            {
                int idx = vtables.FindIndex(v => v == k.VTable);
                lines.Add($"knownAgent id={k.Id} vtable={k.VTable:X} pointer={k.Pointer:X} foundAtIndex={idx}"
                    + (idx >= 0 && idx < pointers.Count ? $" pairedPointerAtIndex={pointers[idx]:X} pairingMatches={pointers[idx] == k.Pointer}" : ""));
            }

            int count = Math.Min(vtables.Count, pointers.Count);
            for (int i = 0; i < count; i++)
                lines.Add($"agent[{i}] vtable={vtables[i]:X} pointer={pointers[i]:X}");
        }
        catch (Exception ex)
        {
            lines.Add($"agentListError={ex.Message}");
        }
    }

    /// <summary>
    /// Enumerates every currently-*visible* addon window and whatever agent each one resolves
    /// to via <c>TryFindAgentInterface()</c> — a much smaller, more relevant set than all 509
    /// live agents (<see cref="AppendBridgeAgentList"/>), most of which are presumably dormant/
    /// unrelated background systems. If the actual mahjong game-state agent is bound to some
    /// *other* currently-visible window (not necessarily "Emj" itself), this is how we'd spot it
    /// without guessing an index range to brute-force. <c>AtkAddonControl</c> doesn't expose a
    /// name string via the managed API, so windows are only identified by pointer/bounds here —
    /// cross-reference against <see cref="EmjAddonReader.TryGetWindow"/>'s own pointer to at
    /// least confirm which entry is the already-known "Emj" window.
    /// </summary>
    private void AppendBridgeWindowAgentList(List<string> lines)
    {
        try
        {
            RaptureAtkUnitManager.Update();
            int total = 0, visible = 0, withAgent = 0;
            foreach (var control in RaptureAtkUnitManager.Controls)
            {
                total++;
                if (control is not { IsVisible: true })
                    continue;
                visible++;
                AgentInterface? agent;
                try
                {
                    agent = control.TryFindAgentInterface();
                }
                catch
                {
                    agent = null;
                }
                if (agent is not { IsValid: true })
                    continue;
                withAgent++;
                lines.Add($"window pointer={control.Pointer:X} bounds={control.Bounds} -> agentPointer={agent.Pointer:X} agentVTable={agent.VTable:X}");
            }
            lines.Add($"totalWindows={total} visibleWindows={visible} visibleWithResolvedAgent={withAgent}");
        }
        catch (Exception ex)
        {
            lines.Add($"windowAgentListError={ex.Message}");
        }
    }

    /// <summary>
    /// Cheap per-agent fingerprint for a range of agent indices (default 300-360, near our
    /// known-good agent 328) — a rolling hash of each agent's raw memory rather than a full dump,
    /// so two of these taken a real game-state change apart can be diffed to find which few
    /// agents (if any) actually mutated, before spending a full <see cref="AppendBridgeAgentList"/>-
    /// style dump on just those candidates. Added 2026-09-14 after both the bound-window agent
    /// (328, zero mutation) and the visible-window-agent enumeration (no distinct mahjong-data
    /// window) came up empty — this is the next cheapest way to search the remaining ~500 mostly-
    /// dormant agents without an enormous dump. Pass <c>start=</c>/<c>end=</c>/<c>size=</c> (byte
    /// count per agent, default 0x800) command-file args to adjust the range.
    /// </summary>
    private void AppendBridgeAgentChecksums(List<string> lines, Dictionary<string, string> args)
    {
        try
        {
            var pointers = AgentModule.AgentPointers;
            int start = args.TryGetValue("start", out var s) ? int.Parse(s) : 300;
            int end = args.TryGetValue("end", out var e) ? int.Parse(e) : 360;
            int sizeBytes = args.TryGetValue("size", out var sz) ? int.Parse(sz) : 0x800;
            end = Math.Min(end, pointers.Count - 1);

            for (int i = start; i <= end; i++)
            {
                if (pointers[i] == IntPtr.Zero)
                {
                    lines.Add($"agent[{i}] pointer=0 checksum=0");
                    continue;
                }
                try
                {
                    var raw = RBCore.Memory.ReadArray<int>(pointers[i], sizeBytes / 4);
                    long checksum = 0;
                    foreach (int v in raw)
                        checksum = unchecked(checksum * 31 + v);
                    lines.Add($"agent[{i}] pointer={pointers[i]:X} checksum={checksum:X}");
                }
                catch (Exception ex)
                {
                    lines.Add($"agent[{i}] pointer={pointers[i]:X} error={ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            lines.Add($"agentChecksumError={ex.Message}");
        }
    }

    /// <summary>
    /// Full annotated raw-memory dump for a specific, small list of agent indices (comma-
    /// separated <c>indices=</c> arg) rather than a whole range — the natural follow-up once
    /// <see cref="AppendBridgeAgentChecksums"/> narrows ~500 dormant agents down to a handful
    /// that actually mutated across a real game-state change. <c>size=</c> arg controls bytes
    /// read per agent (default 0x800, matching the checksum default so offsets line up).
    /// </summary>
    private void AppendBridgeAgentsRaw(List<string> lines, Dictionary<string, string> args)
    {
        try
        {
            var pointers = AgentModule.AgentPointers;
            int sizeBytes = args.TryGetValue("size", out var sz) ? int.Parse(sz) : 0x800;
            var indices = (args.TryGetValue("indices", out var idx) ? idx : "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(int.Parse);

            foreach (int i in indices)
            {
                if (i < 0 || i >= pointers.Count || pointers[i] == IntPtr.Zero)
                {
                    lines.Add($"agent[{i}] unavailable");
                    continue;
                }
                try
                {
                    var raw = RBCore.Memory.ReadArray<int>(pointers[i], sizeBytes / 4);
                    for (int j = 0; j < raw.Length; j++)
                    {
                        int offset = j * 4;
                        string note = DecodeTileGuess(raw[j]);
                        lines.Add($"agent[{i}][0x{offset:X4}]={raw[j]}{note}");
                    }
                }
                catch (Exception ex)
                {
                    lines.Add($"agent[{i}] error={ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            lines.Add($"agentsRawError={ex.Message}");
        }
    }

    /// <summary>
    /// Exact-value search across every currently-active agent (all ~509, not a guessed range —
    /// an exact int32 match is cheap and precise enough that there's no reason to narrow it).
    /// Far stronger than <see cref="DecodeTileGuess"/>'s range heuristic: given a *specific*
    /// known real tile, its exact encoded value(s) (bare id via <c>values=</c>, or precomputed
    /// texture-offset/aka-dora candidates the caller works out from <see cref="EmjAddonReader.EncodeTileRaw"/>)
    /// either are or aren't present somewhere in memory — no false positives from coincidental
    /// small integers, no reliance on only the two encodings we already know about if the caller
    /// passes other candidate values to try. <c>values=</c> is a comma-separated list of int32s
    /// (decimal or 0x-prefixed hex); <c>size=</c> controls bytes scanned per agent (default
    /// 0x800).
    /// </summary>
    private void AppendBridgeAgentValueSearch(List<string> lines, Dictionary<string, string> args)
    {
        try
        {
            var pointers = AgentModule.AgentPointers;
            int sizeBytes = args.TryGetValue("size", out var sz) ? int.Parse(sz) : 0x800;
            var targets = (args.TryGetValue("values", out var v) ? v : "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToInt32(t, 16)
                    : int.Parse(t))
                .ToHashSet();

            lines.Add($"searching {pointers.Count} agents, {sizeBytes} bytes each, for values: {string.Join(",", targets)}");
            int hits = 0;
            for (int i = 0; i < pointers.Count; i++)
            {
                if (pointers[i] == IntPtr.Zero)
                    continue;
                int[] raw;
                try
                {
                    raw = RBCore.Memory.ReadArray<int>(pointers[i], sizeBytes / 4);
                }
                catch
                {
                    continue; // unreadable region — skip, same as the checksum/raw dump paths
                }
                for (int j = 0; j < raw.Length; j++)
                {
                    if (!targets.Contains(raw[j]))
                        continue;
                    hits++;
                    lines.Add($"MATCH agent[{i}] pointer={pointers[i]:X} offset=0x{j * 4:X4} value={raw[j]}");
                }
            }
            lines.Add($"totalMatches={hits}");
        }
        catch (Exception ex)
        {
            lines.Add($"agentSearchError={ex.Message}");
        }
    }

    /// <summary>
    /// Checks whether a specific addon name resolves to a visible window right now, independent
    /// of "Emj" itself — <c>RaptureAtkUnitManager.GetWindowByName</c> takes a name directly, no
    /// enumeration needed. <c>name=</c> command-file arg (default "EmjL" if omitted).
    /// </summary>
    private void AppendBridgeTryWindowByName(List<string> lines, Dictionary<string, string> args)
    {
        string name = args.TryGetValue("name", out var n) ? n : "EmjL";
        try
        {
            RaptureAtkUnitManager.Update();
            var candidate = RaptureAtkUnitManager.GetWindowByName(name);
            if (candidate is not { IsVisible: true })
            {
                lines.Add($"windowName={name} found=false");
                return;
            }
            lines.Add($"windowName={name} found=true pointer={candidate.Pointer:X} bounds={candidate.Bounds}");
            var agent = candidate.TryFindAgentInterface();
            if (agent is { IsValid: true })
                lines.Add($"agentPointer={agent.Pointer:X} agentVTable={agent.VTable:X}");
        }
        catch (Exception ex)
        {
            lines.Add($"tryWindowByNameError={ex.Message}");
        }
    }

    /// <summary>
    /// Read-only byte-pattern scan via RB's own bundled GreyMagic library (<c>ff14bot.Core.Memory</c>
    /// is a <c>GreyMagic.ExternalProcessMemory</c>, which is-a <c>MemoryBase</c> — the exact type
    /// <c>PatternFinder</c>'s constructor wants). Added 2026-09-15 to check whether the reference
    /// project's published discard-handler signature (found via Dalamud's own sigscanner) also
    /// matches on this client build — just locates an address, never writes or executes anything,
    /// so this stays in the same read-only risk category as every other bridge action. <c>pattern=</c>
    /// is a space-separated hex byte pattern (GreyMagic's own wildcard syntax, if any, TBD from
    /// results — the reference signature has no wildcard bytes to test that with).
    /// </summary>
    private void AppendBridgeFindPattern(List<string> lines, Dictionary<string, string> args)
    {
        try
        {
            string pattern = args.TryGetValue("pattern", out var p) ? p : "";
            if (string.IsNullOrWhiteSpace(pattern))
            {
                lines.Add("findPatternError=missing 'pattern' arg");
                return;
            }
            using var finder = new GreyMagic.PatternFinder(RBCore.Memory);
            IntPtr addr = finder.Find(pattern);
            IntPtr moduleBase = RBCore.Memory.Process.MainModule?.BaseAddress ?? IntPtr.Zero;
            lines.Add($"pattern={pattern}");
            lines.Add($"address={addr:X}");
            lines.Add($"moduleBase={moduleBase:X}");
            lines.Add(addr == IntPtr.Zero
                ? "matched=false"
                : $"matched=true rva={(long)addr - (long)moduleBase:X}");
        }
        catch (Exception ex)
        {
            lines.Add($"findPatternError={ex.Message}");
        }
    }

    /// <summary>
    /// Read-only raw byte dump around an address, for offline disassembly (e.g. via a proper
    /// disassembler like Capstone, not hand-decoding) rather than modifying/executing anything in
    /// the game process. Accepts either <c>address=</c> (absolute hex) or <c>rva=</c> (relative to
    /// the main module's base — matches what <see cref="AppendBridgeFindPattern"/> reports), plus
    /// <c>before=</c>/<c>after=</c> byte counts (defaults 256/64) to bracket a found instruction
    /// and trace register provenance backward through the function.
    /// </summary>
    private void AppendBridgeBytesAtAddress(List<string> lines, Dictionary<string, string> args)
    {
        try
        {
            IntPtr moduleBase = RBCore.Memory.Process.MainModule?.BaseAddress ?? IntPtr.Zero;
            IntPtr center;
            if (args.TryGetValue("rva", out var rvaStr))
                center = moduleBase + Convert.ToInt32(rvaStr, 16);
            else if (args.TryGetValue("address", out var addrStr))
                center = new IntPtr(Convert.ToInt64(addrStr, 16));
            else
            {
                lines.Add("dumpBytesError=need 'rva' or 'address' arg");
                return;
            }

            int before = args.TryGetValue("before", out var b) ? int.Parse(b) : 256;
            int after = args.TryGetValue("after", out var a) ? int.Parse(a) : 64;
            IntPtr start = center - before;
            int total = before + after;

            byte[] bytes = RBCore.Memory.ReadBytes(start, total);
            lines.Add($"startAddress={start:X}");
            lines.Add($"centerAddress={center:X}");
            lines.Add($"centerOffsetInDump={before}");
            lines.Add($"moduleBase={moduleBase:X}");
            lines.Add($"bytes={Convert.ToHexString(bytes)}");
        }
        catch (Exception ex)
        {
            lines.Add($"dumpBytesError={ex.Message}");
        }
    }

    /// <summary>
    /// Scans every currently-active agent for a plausible (non-null, heap-range-looking) pointer
    /// value at a fixed byte offset — added 2026-09-15 after Ghidra decompilation of the discard
    /// handler's callers showed <c>R14 = *(param_1 + 0x13E8)</c> consistently across three
    /// functions, but agent 328 (Emj) itself reads zero there, meaning <c>param_1</c> is some
    /// other object entirely. <c>offset=</c> hex byte offset (default 0x13E8).
    ///
    /// A bare "is this pointer-shaped" filter on offset 0x13E8 alone returned ~100 hits (too
    /// noisy — plenty of unrelated classes coincidentally have *some* pointer at that offset).
    /// Added a second-level check matching the specific pattern found live: the real R14 pool
    /// has a small discard-count int at <c>+0x1000</c> (confirmed via the discard-handler asm) —
    /// so <c>followOffset=</c>/<c>followMax=</c> (defaults 0x1000/200) follow each candidate
    /// pointer one level deeper and only report ones where that nested value is small and
    /// plausible, not more pointer-shaped garbage.
    /// </summary>
    private void AppendBridgeScanAgentsForPointerAt(List<string> lines, Dictionary<string, string> args)
    {
        try
        {
            var pointers = AgentModule.AgentPointers;
            long offset = args.TryGetValue("offset", out var o) ? Convert.ToInt64(o, 16) : 0x13E8;
            long followOffset = args.TryGetValue("followOffset", out var fo) ? Convert.ToInt64(fo, 16) : 0x1000;
            int followMax = args.TryGetValue("followMax", out var fm) ? int.Parse(fm) : 200;
            lines.Add($"scanning {pointers.Count} agents at offset 0x{offset:X}, following +0x{followOffset:X} for a value in [0,{followMax}]");

            int hits = 0;
            for (int i = 0; i < pointers.Count; i++)
            {
                if (pointers[i] == IntPtr.Zero)
                    continue;
                long candidate;
                try
                {
                    candidate = RBCore.Memory.Read<long>(pointers[i] + (int)offset);
                }
                catch
                {
                    continue;
                }
                if (candidate is <= 0x10000 or >= 0x7FFFFFFFFFFF)
                    continue; // not pointer-shaped

                int followed;
                try
                {
                    followed = RBCore.Memory.Read<int>(new IntPtr(candidate) + (int)followOffset);
                }
                catch
                {
                    continue; // candidate pointer doesn't even resolve to readable memory
                }
                if (followed < 0 || followed > followMax)
                    continue;

                hits++;
                lines.Add($"agent[{i}] pointer={pointers[i]:X} valueAtOffset={candidate:X} followedValue={followed}");
            }
            lines.Add($"totalHits={hits}");
        }
        catch (Exception ex)
        {
            lines.Add($"scanAgentsForPointerAtError={ex.Message}");
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

    /// <summary>One-off verification (2026-09-14): the "offered-tile hunt exhausted" conclusion from 2026-09-08 read <c>AgentInterface.Pointer</c> but never actually logged <c>.Id</c> — this reports what we're really resolving, to cross-check against an id the user found through an external tool before trusting that old negative result.</summary>
    private void AppendBridgeAgentInfo(List<string> lines, AtkAddonControl window)
    {
        var info = _reader.TryDescribeResolvedAgent(window);
        if (info is not { } agent)
        {
            lines.Add("agentResolved=false");
            return;
        }
        lines.Add("agentResolved=true");
        lines.Add($"agentId={agent.Id}");
        lines.Add($"agentPointer={agent.Pointer:X}");
        lines.Add($"agentVTable={agent.VTable:X}");
    }

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
        ProcessClaudeBridgeCommand();

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
                var tileGuess = TryIdentifyGlowingTile(window);
                Logging.Write(tileGuess is { } guess
                    ? $"[FFXIVMahjong] screen-match guess: {guess.Name} (score={guess.Score:F1}, lower=better)"
                    : "[FFXIVMahjong] screen-match: no confident region found");

                // Log-only recommendation, not dispatched — the reference library only covers
                // one tile/position combo so far (2026-09-08), not enough to trust auto-clicking
                // yet. Once coverage and match confidence are good, this is the hook point to
                // wire in a real accept/pass dispatch instead of just logging.
                if (tileGuess is { } identified && ParseTileName(identified.Name) is { } offeredTile)
                {
                    var rawHand = _reader.ReadSelfHand(window);
                    ObserveHandForMeldTracking(rawHand.Concealed, window);

                    // A call decision (not our discard turn) means we're holding 13 minus 3
                    // tiles per prior call — 13/10/7/4/1 — same meld-count-from-concealed-count
                    // inference as the discard path (line ~361), applied here too. Without it,
                    // a hand with an existing meld silently ran the policy 3 tiles short: a real
                    // 02:11:42 live capture (West/kamicha discarding 7p onto a 10-concealed-tile
                    // hand) computed ShouldCallPon/Chi against a phantom 10-"tile" hand instead
                    // of the true 13, which the yaku/shanten heuristics were never designed for.
                    int callConcealedCount = rawHand.Concealed.Count;
                    Hand? currentHand = callConcealedCount != 0 && callConcealedCount % 3 == 1
                        ? BuildHandForCallDecision(rawHand, (13 - callConcealedCount) / 3)
                        : null;

                    if (currentHand is null)
                    {
                        Logging.Write($"[FFXIVMahjong] call recommendation: skipped (concealed count {callConcealedCount} doesn't fit a call-decision shape)");
                    }
                    else
                    {
                        // Seat/round wind aren't read from the addon yet (only affects the minor
                        // "does a wind pair still give yakuhai" nuance in ImprovesShantenWithYakuPotential)
                        // — East/East is a placeholder, not a confirmed read. Fine for a log-only
                        // recommendation a human reviews, not for real dispatch.
                        var seatWind = WindTile.East;
                        var roundWind = WindTile.East;

                        bool ponRecommended = _callPolicy.ShouldCallPon(currentHand, offeredTile, seatWind, roundWind);
                        var chiTiles = _callPolicy.ShouldCallChi(currentHand, offeredTile, seatWind, roundWind);

                        string recommendation = (ponRecommended, chiTiles) switch
                        {
                            (true, _) => "PON",
                            (false, { } tiles) => $"CHI ({string.Join("+", tiles)})",
                            _ => "PASS",
                        };
                        Logging.Write($"[FFXIVMahjong] call recommendation: {recommendation} on {offeredTile} (hand: {string.Join(",", currentHand.Concealed)})");
                    }
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
        }

        int stateCode = _reader.ReadStateCode(window);

        if (stateCode == EmjOffsets.StateHandResultNext)
        {
            DateTime resultFirstSeen = _handResultFirstSeenAt ?? DateTime.UtcNow;
            _handResultFirstSeenAt = resultFirstSeen;

            bool stable = DateTime.UtcNow - resultFirstSeen >= HandResultStabilityWindow;
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

        if (stateCode != EmjOffsets.StateOurTurnDiscard
            && stateCode != EmjOffsets.StatePostDrawOrCallDiscard
            && stateCode != EmjOffsets.StatePostCallDiscard)
            return;

        var hand = _reader.ReadSelfHand(window);
        ObserveHandForMeldTracking(hand.Concealed, window);

        // Our turn to discard whenever the concealed count is 14 minus a multiple of 3
        // (14/11/8/5/2) — each prior call (chi/pon/kan) takes 3 tile-slots out of the
        // 14-tile-equivalent total. EmjAddonReader doesn't read melds directly off the addon
        // struct (confirmed live 2026-09-08: a post-Chi hand reads as an 11-tile fully-concealed
        // hand, not a hand+meld) — but MeldTracker (2026-09-15) infers the real called tiles by
        // watching the concealed hand shrink, so ResolveMelds below prefers that over a
        // placeholder whenever its count actually matches what the addon's concealed-count says
        // to expect.
        int concealedCount = hand.Concealed.Count;
        if (concealedCount == 0 || concealedCount % 3 != 2)
            return; // mid-transition read, not a genuine discard-turn shape

        int meldCount = (EmjOffsets.HandSize - concealedCount) / 3;
        var handForPolicy = meldCount == 0 ? hand : new Hand(hand.Concealed, ResolveMelds(meldCount));

        string signature = string.Join(",", hand.Concealed.Select(t => t.Id).OrderBy(id => id));
        if (signature == _lastActedHandSignature)
            return; // already dispatched for this exact hand — waiting for the game to catch up

        var discard = _discardPolicy.ChooseDiscard(handForPolicy);

        // Win/riichi gating only applies to a genuinely closed hand (meldCount == 0): the
        // placeholder melds built above are the right *count* for shanten/discard purposes (see
        // the comment above PlaceholderMelds) but the wrong *tiles*, and Scorer needs real tile
        // identities to compute yaku correctly. An open hand just falls through to the normal
        // auto-discard flow below, same as before this feature existed.
        if (meldCount == 0)
        {
            if (TryScoreClosedTsumo(handForPolicy, window) is { } tsumoScore)
            {
                if (signature != _lastTsumoLoggedSignature)
                {
                    _lastTsumoLoggedSignature = signature;
                    Logging.Write($"[FFXIVMahjong] TSUMO available! {tsumoScore.TotalHan} han / {tsumoScore.Points} points ({string.Join(", ", tsumoScore.Yaku.Select(y => y.Name))}) — auto-discard paused, declare it yourself in-game.");
                }
                return; // never auto-discard a winning hand — leave it entirely to the human
            }

            var afterDiscard = handForPolicy.WithDiscard(discard);
            if (_riichiPolicy.ShouldDeclareRiichi(afterDiscard, _reader.ReadSelfScore(window)))
            {
                if (signature != _lastRiichiLoggedSignature)
                {
                    _lastRiichiLoggedSignature = signature;
                    string waits = string.Join("/", Ukeire.UsefulTileIds(afterDiscard).Select(id => new Tile(id).ToString()));
                    var doraIndicator = _reader.ReadDoraIndicator(window);
                    var doraList = doraIndicator is { } d ? new List<Tile> { d } : new List<Tile>();
                    string reason = Scorer.HasYakuWithoutRiichi(afterDiscard, WindTile.East, WindTile.East, doraList, _ruleSet)
                        ? "hand already scores without declaring, so Damaten is a real option, but Riichi still adds guaranteed extra han and ura dora access"
                        : "no yaku exists without declaring, so Riichi is the only way to enable a Ron win here (Tsumo would still work via Menzen Tsumo alone)";
                    Logging.Write($"[FFXIVMahjong] RIICHI recommended (tenpai after discarding {discard}, waiting on {waits}) — {reason}. Auto-discard paused, declare it yourself in-game.");
                }
                return; // leave the discard (and the riichi decision) to the human
            }
        }

        if (DateTime.UtcNow - _lastDispatchAt < MinInterActionGap)
            return;

        int slotIndex = FindSlotForTile(window, discard);
        if (slotIndex < 0)
            return;

        _dispatcher.Discard(window, slotIndex, _reader.ReadHandSlotRaw(window, slotIndex));
        _lastActedHandSignature = signature;
        _lastDispatchAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Returns a real score (not just a structural shanten==-1 check) if this closed 14-tile
    /// hand can legally Tsumo right now — i.e. Scorer finds at least one yaku, per the Doman
    /// ruleset's "no yaku, no win" requirement. Seat/round wind use the same East/East
    /// placeholder as the call-recommendation path above (not read from the addon yet); this
    /// only affects the yakuhai/dealer nuance, and since <c>MenzenTsumoRule</c> alone guarantees
    /// at least 1 han for any closed self-draw regardless of which tile is treated as the
    /// winning one, WinningTile is picked arbitrarily (only Pinfu's ryanmen-wait check cares
    /// about it, and it isn't the only path to a valid score here). Log-only informational use —
    /// not accurate enough for a real score submission (no riichi/ippatsu/ura-dora tracking).
    /// </summary>
    private ScoreResult? TryScoreClosedTsumo(Hand hand, AtkAddonControl window)
    {
        if (Shanten.Calculate(hand) != -1)
            return null;

        var doraIndicator = _reader.ReadDoraIndicator(window);
        var context = new WinContext(
            SeatWind: WindTile.East,
            RoundWind: WindTile.East,
            WinningTile: hand.Concealed[0],
            IsTsumo: true,
            IsRiichi: false,
            IsDoubleRiichi: false,
            IsIppatsu: false,
            IsHaitei: false,
            IsHoutei: false,
            IsChankan: false,
            IsRinshan: false,
            DoraIndicators: doraIndicator is { } d ? new List<Tile> { d } : new List<Tile>(),
            UraDoraIndicators: new List<Tile>());

        return Scorer.Score(hand, context, _ruleSet);
    }

    private const int ScreenMatchFrameCount = 5;
    private static readonly TimeSpan ScreenMatchFrameDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Captures the game window's own content across several frames (~150ms apart) and finds
    /// the play-area window with the highest per-pixel brightness range over the whole sample —
    /// self-locating, no node IDs or hardcoded offsets needed (see <see cref="ScreenTileMatcher"/>).
    /// Uses <c>PrintWindow</c> (via the game's own hwnd) rather than a raw screen-coordinate
    /// grab, so it isn't corrupted by whatever else is on top of the game on screen — live-caught
    /// a bad match caused by exactly that (a browser window overlapping the game, 2026-09-08).
    /// A first version used only 2 frames; live testing caught it occasionally locking onto a
    /// tile neighboring the real one. Sampling more frames and using range-over-all-frames
    /// (rather than a single pairwise diff) is meant to fix that — see docs/addon-capture-log.md
    /// for the synthetic test that validated it before this touched the live game again. Blocks
    /// Pulse() for ~600ms, but only once per newly-seen prompt (gated by the caller's
    /// `justAppeared` check), same tradeoff already accepted elsewhere (e.g. the 3.5s hand-result
    /// stability wait).
    /// </summary>
    private (string Name, double Score)? TryIdentifyGlowingTile(AtkAddonControl window)
    {
        if (_tileMatcher is null)
            return null;
        var playFrames = new List<Bitmap>();
        Bitmap? firstFullFrame = null;
        Bitmap? lastFullFrame = null;
        try
        {
            if (window.Bounds is not { Width: > 0, Height: > 0 } boundsF)
                return null;
            var screenBounds = Rectangle.Round(boundsF);
            IntPtr hwnd = EmjAddonReader.GetGameWindowHandle();

            Rectangle addonInClient = default;
            Rectangle playArea = default;
            for (int i = 0; i < ScreenMatchFrameCount; i++)
            {
                if (i > 0)
                    Thread.Sleep(ScreenMatchFrameDelay);

                using var client = ScreenTileMatcher.CaptureWindowClient(hwnd, out Point origin);
                if (client is null)
                    return null;

                if (i == 0)
                {
                    addonInClient = new Rectangle(screenBounds.X - origin.X, screenBounds.Y - origin.Y, screenBounds.Width, screenBounds.Height);
                    if (addonInClient.X < 0 || addonInClient.Y < 0
                        || addonInClient.Right > client.Width || addonInClient.Bottom > client.Height)
                        return null; // addon reports being outside the captured client area — stale bounds, don't trust the crop

                    // Restrict the search to the play area (discard piles/compass), excluding the
                    // player-portrait strips on the left/right — live-caught 2026-09-08 (via the
                    // debug marker below) that a portrait's own idle animation changes far more
                    // between frames than the actual tile pulse, so an unrestricted search
                    // reliably lands on a portrait instead. Percentages tuned against the same
                    // 1008x560 capture that showed the compass area roughly centered, ~58% of
                    // width / ~80% of height, starting ~15% in.
                    playArea = new Rectangle(
                        (int)(addonInClient.Width * 0.15),
                        0,
                        (int)(addonInClient.Width * 0.58),
                        (int)(addonInClient.Height * 0.80));
                }

                using var full = client.Clone(addonInClient, client.PixelFormat);
                if (i == 0)
                    firstFullFrame = new Bitmap(full);
                if (i == ScreenMatchFrameCount - 1)
                    lastFullFrame = new Bitmap(full);
                playFrames.Add(full.Clone(playArea, full.PixelFormat));
            }

            // Re-measured (2026-09-08) directly from real captured pixel data, not a visual
            // estimate: an ASCII color-map of an actual crop showed the tile only filled the top
            // ~60% of the window, the rest pure green felt — the previous /10 divisor (~44 tall)
            // was still nearly double the real ~26px tile height. Confirmed as a real improvement
            // offline (not just a guess) by cropping the real tile tightly and rescoring against
            // the reference: score dropped from 235 to 194 just from this. Width stayed ~34,
            // giving a wide-short ~1.3 ratio — consistent with this specific pile position
            // rendering tiles wider than tall (rotated relative to our upright reference art).
            int windowHeight = Math.Max(16, playArea.Height / 17);
            int windowWidth = Math.Max(16, playArea.Height / 13);

            // The central wall-count/turn indicator (the "61" diamond, its 4 dots, and the star)
            // has its own idle animation too — user-caught 2026-09-08 via side-by-side crops.
            // It sits roughly centered in the play area, well clear of the discard-tile clusters
            // around it, so a modest centered exclusion zone removes it as a competing signal
            // without risking clipping any real tile.
            var centerExclusion = new Rectangle(
                (int)(playArea.Width * 0.40), (int)(playArea.Height * 0.40),
                (int)(playArea.Width * 0.20), (int)(playArea.Height * 0.20));
            var tileRegion = ScreenTileMatcher.FindMostVariableRegion(playFrames, windowWidth, windowHeight, [centerExclusion]);
            if (tileRegion is not { } region)
                return null;

            // Match against every sampled frame's crop at this position, not just the last one,
            // and keep the best-scoring result — the tile is actively pulsing/glowing, so
            // whichever single frame we grabbed could be caught at a brightness extreme that
            // doesn't resemble the flat, non-glowing reference art. Trying all 5 "exposures"
            // hedges against picking an unlucky one.
            Bitmap? bestCrop = null;
            (string Name, double Score) result = ("(none)", double.MaxValue);
            foreach (var frame in playFrames)
            {
                var candidateCrop = frame.Clone(region, frame.PixelFormat);
                var candidateResult = _tileMatcher.MatchTile(candidateCrop);
                if (candidateResult.Score < result.Score)
                {
                    bestCrop?.Dispose();
                    bestCrop = candidateCrop;
                    result = candidateResult;
                }
                else
                {
                    candidateCrop.Dispose();
                }
            }
            using var crop = bestCrop ?? playFrames[^1].Clone(region, playFrames[^1].PixelFormat);
            var regionInFullPanel = new Rectangle(playArea.X + region.X, playArea.Y + region.Y, region.Width, region.Height);

            // Always dump the last detection for inspection — cheap, overwrites each time, and
            // has already been essential for debugging (caught the oversized-region bug and the
            // portrait-animation bug this way 2026-09-08) without needing a separate manual
            // console snippet. The marked copy draws the chosen window (offset back into full-
            // panel coordinates) directly on the last full frame for an at-a-glance sanity check.
            try
            {
                Directory.CreateDirectory(DebugCaptureDirectory);
                firstFullFrame?.Save(Path.Combine(DebugCaptureDirectory, "auto_regionA.png"));
                lastFullFrame?.Save(Path.Combine(DebugCaptureDirectory, "auto_regionB.png"));
                crop.Save(Path.Combine(DebugCaptureDirectory, "auto_crop.png"));
                if (lastFullFrame is not null)
                {
                    using var marked = new Bitmap(lastFullFrame);
                    using (var mg = Graphics.FromImage(marked))
                    {
                        using var playAreaPen = new Pen(Color.Yellow, 1);
                        mg.DrawRectangle(playAreaPen, playArea);
                        using var excludePen = new Pen(Color.DeepSkyBlue, 1);
                        mg.DrawRectangle(excludePen, new Rectangle(
                            playArea.X + centerExclusion.X, playArea.Y + centerExclusion.Y,
                            centerExclusion.Width, centerExclusion.Height));
                        using var pen = new Pen(Color.Red, 2);
                        mg.DrawRectangle(pen, regionInFullPanel);
                    }
                    marked.Save(Path.Combine(DebugCaptureDirectory, "auto_regionB_marked.png"));
                }
            }
            catch { /* best-effort diagnostic only */ }

            Logging.Write($"[FFXIVMahjong] screen-match region: {regionInFullPanel} (window {windowWidth}x{windowHeight}, frames={playFrames.Count}, guess={result.Name} score={result.Score:F1})");
            return result;
        }
        catch (Exception ex)
        {
            // The addon window (or its underlying memory) can go stale mid-capture if the
            // prompt closes/changes while we're mid-screenshot — fail safe rather than crash
            // Pulse() instead of throwing.
            Logging.Write($"[FFXIVMahjong] screen-match capture failed: {ex.Message}");
            return null;
        }
        finally
        {
            foreach (var f in playFrames)
                f.Dispose();
            firstFullFrame?.Dispose();
            lastFullFrame?.Dispose();
        }
    }

    /// <summary>Maps a <see cref="ScreenTileMatcher.MatchTile"/> result name (the reference filename, position suffix already stripped) back to a <see cref="Tile"/> — e.g. "7p", "north", "green".</summary>
    private static Tile? ParseTileName(string name)
    {
        if (name.Length == 2 && char.IsDigit(name[0]))
        {
            int rank = name[0] - '0';
            Suit? suit = name[1] switch
            {
                'm' => Suit.Man,
                'p' => Suit.Pin,
                's' => Suit.Sou,
                _ => null,
            };
            return suit is { } s && rank is >= 1 and <= 9 ? Tile.FromSuitRank(s, rank) : null;
        }
        return name switch
        {
            "east" => Tile.FromSuitRank(Suit.Wind, 1),
            "south" => Tile.FromSuitRank(Suit.Wind, 2),
            "west" => Tile.FromSuitRank(Suit.Wind, 3),
            "north" => Tile.FromSuitRank(Suit.Wind, 4),
            "white" => Tile.FromSuitRank(Suit.Dragon, 1),
            "green" => Tile.FromSuitRank(Suit.Dragon, 2),
            "red" => Tile.FromSuitRank(Suit.Dragon, 3),
            _ => null,
        };
    }

    private static IReadOnlyList<Meld> PlaceholderMelds(int count)
    {
        var placeholder = Meld.Pon(Tile.FromSuitRank(Suit.Man, 1), RelativeSeat.Kamicha);
        return Enumerable.Repeat(placeholder, count).ToList();
    }

    /// <summary>
    /// Feeds <see cref="_meldTracker"/> the current concealed hand plus each opponent's live
    /// discard count, so it can infer a newly-called meld from a hand-size delta. Called from
    /// both the discard-turn and call-decision hand reads (2026-09-15), since a meld can be
    /// observed to have formed at either polling point depending on which Pulse tick first
    /// catches the shrink. Also drives the tracker's own hand-boundary reset: self discard count
    /// only ever increases within a hand, so a drop means a fresh deal.
    /// </summary>
    private void ObserveHandForMeldTracking(IReadOnlyList<Tile> concealed, AtkAddonControl window)
    {
        int selfDiscardCount = _reader.ReadSelfDiscardCount(window);
        if (selfDiscardCount < _lastSelfDiscardCountForMeldTracking)
        {
            _meldTracker.Reset();
            _meldCountMismatchLogged = false;
            Logging.Write("[FFXIVMahjong] meld tracker reset (new hand detected via self discard count decreasing)");
        }
        _lastSelfDiscardCountForMeldTracking = selfDiscardCount;

        var inferred = _meldTracker.ObserveHand(
            concealed,
            _reader.ReadShimochaDiscardCount(window),
            _reader.ReadToimenDiscardCount(window),
            _reader.ReadKamichaDiscardCount(window));

        if (inferred is { } meld)
            Logging.Write($"[FFXIVMahjong] meld tracker inferred {meld.Type} ({string.Join(",", meld.Tiles)}) called from {meld.CalledFrom}");
    }

    /// <summary>
    /// Prefers <see cref="MeldTracker"/>'s real inferred melds over the count-only placeholder,
    /// whenever the tracker's own count actually matches what the addon's concealed-tile-count
    /// implies — falls back to the placeholder otherwise (e.g. the bot was started mid-hand with
    /// a meld already called before it started watching, so the tracker never saw it form).
    /// </summary>
    private IReadOnlyList<Meld> ResolveMelds(int meldCount)
    {
        if (_meldTracker.Melds.Count == meldCount)
            return _meldTracker.Melds;

        if (!_meldCountMismatchLogged)
        {
            _meldCountMismatchLogged = true;
            Logging.Write($"[FFXIVMahjong] meld tracker count mismatch (tracked {_meldTracker.Melds.Count}, expected {meldCount}) — falling back to placeholder melds for this hand");
        }
        return PlaceholderMelds(meldCount);
    }

    /// <summary>
    /// Same placeholder-meld-count trick as <see cref="PlaceholderMelds"/>/the discard path
    /// above, but for a hand not on its own discard turn (13 tiles minus 3 per prior call,
    /// i.e. concealed count 13/10/7/4/1) rather than the discard-turn shape (14 minus 3 per
    /// call). Prefers <see cref="MeldTracker"/>'s real melds the same way <see cref="ResolveMelds"/>
    /// does for the discard path; falls back to the same looser placeholder otherwise.
    /// </summary>
    private Hand BuildHandForCallDecision(Hand rawHand, int meldCount) =>
        meldCount == 0 ? rawHand : new Hand(rawHand.Concealed, ResolveMelds(meldCount));

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
