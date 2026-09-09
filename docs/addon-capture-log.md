# Addon capture log — "Emj"

Captured live 2026-09-07 via a RebornConsole (F4) C# snippet reading/writing
`ff14bot.Managers.RaptureAtkUnitManager.GetWindowByName("Emj")`, cross-checked
against the player's actual on-screen hand. Offsets below are confirmed
against this client build; not yet verified for "EmjL" or other regions.

## Confirmed offsets (bytes into `AtkUnitBase*`)

| Field | Offset | Notes |
|---|---|---|
| selfScore | 0x0500 | int32, confirmed reading 25000 at hand start |
| shimochaScore | 0x07E0 | int32 |
| toimenScore | 0x0AC0 | int32 |
| kamichaScore | 0x0DA0 | int32 |
| selfDiscardCount | 0x04FE | byte, incremented 0→1 after our test discard |
| handArrayStart | 0x0DB8 | 14 × int32, one per concealed-hand slot |
| doraIndicator | 0x0FD8 | int32, same texture-id encoding as hand slots |
| AtkValues pointer | 0x178 | matches current (patch-tracked) FFXIVClientStructs `AtkUnitBase.AtkValues` — NOT the 0x160 used by the bundled 2022-era LlamaPlugins UITester, which read back garbage (count=0, ptr=0xFFFFFFFF) here |
| AtkValuesCount | 0x1E2 | ushort, matches current FFXIVClientStructs, not UITester's stale 0x1CA |

Hand-slot and dora-indicator raw values are a `textureId`; subtract
`tileTextureBase` (76041 — matches the one public data point we started from,
unverified whether it varies by client) to get the 0-33 tile id.

## Confirmed AtkValues

- `atkValues[0]` = state code. Observed `30` while it was our turn to discard,
  transitioning to `15` immediately after a discard was dispatched — consistent
  with "ourTurnDiscard" / "callPrompt" semantics.
- AtkValueType `3` = a plain int32 (used for hand-slot mirrors, scores, and the
  state code itself in this dump).

## Confirmed action dispatch

`ff14bot.Managers.AtkAddonControl.SendAction(int pairCount, ulong[] values)`
wraps the native FireCallback mechanism. **`values.Length` must equal
`pairCount * 2`**, laid out as `[type0, data0, type1, data1, ...]` — passing
just the raw values (no type tags) throws `ArgumentException` from RB's own
`AtkAddonControl__SendAction`. This wasn't documented anywhere we found before
capturing it live here.

Discard is a confirmed-working two-step handshake:

1. `SendAction(2, [3, 15, 3, rawTileValue])` — "select" the tile by its raw
   hand-array value (not slot index).
2. `SendAction(2, [3, 7, 3, slotIndex])` — "commit" the discard by slot index
   (0-13).

Verified live: discarding slot 13 (raw value 76070) removed the tile from the
addon's hand array (slot zeroed), incremented `selfDiscardCount`, and flipped
the state code 30→15.

## Call-prompt detection (2026-09-07, second capture session)

The base state code does **not** change when a call-prompt modal (observed: Chi/Pass) opens
on top of the normal discard surface — it kept reading 30 ("our turn to discard") the whole
time the prompt was up. This matters: a bot that only checks the state code could try to
discard while a call decision is pending. Captured a before/click-Chi/after diff:

- `atkValues[13]` was `Int` (arbitrary value, e.g. 6 or 7) on a plain discard turn, flipped to
  **`Bool` = true** while the Chi/Pass prompt was open, and reverted to `Int` immediately after
  clicking Chi. The *type* itself changing (not just the value) is a strong signal, but it's
  from a single scenario (Chi only) — not yet confirmed for pon/kan/riichi prompts.
- `atkValues[6..8]` flipped from zero/`Int` to `String8` during the same window — almost
  certainly the "Chi"/"Pass" button label text, consistent with a classic button-row popup.
- Implemented as `EmjAddonReader.IsCallPromptLikelyActive` (checks index 13 for
  `AtkValueType.Bool && true`) and wired into `FFXIVMahjongBot.Pulse()` as a gate before any
  discard dispatch. Needs re-confirming across other call types before trusting it fully.
- Still don't know the dispatch opcode to *trigger* chi/pon/etc. ourselves — before/after
  state snapshots show the result of a click, not the click's own native call arguments.
  Getting that would need hooking the native call in real time, not just memory reads.

## Hand-result "Next" screen (2026-09-07, third capture session)

Before/after around clicking the "Next" button on the hand-result screen:

- `atkValues[0]` was **29** while the result screen (fu/han/score breakdown) was showing, and
  flipped to **2** immediately after clicking Next. `StateHandResultNext = 29` — simple to
  detect the same way as the discard state.
- This also retroactively clarified an earlier finding: state 15 (previously guessed to mean
  "a call prompt is being shown to us") was actually observed only right after *our own*
  discard — a real Chi/Pass prompt targeting us was later observed with the state code still
  at 30. Renamed to `StateAfterOurDiscard`; it's more likely a generic "waiting on the table"
  idle state. The actual call-prompt signal is `CallPromptFlagAtkValueIndex` (see above).
- As with chi, we only captured the *result* of clicking Next, not the dispatch opcode to
  trigger it ourselves.
  - First attempt (`SendAction(2, [3, 11, 3, 0])`) was inconclusive — the Next screen had
    already resolved on its own before the dispatch fired, so the test never actually caught
    state 29.
  - **Confirmed not working**, second attempt 2026-09-07: a polling script caught state 29
    directly (0ms wait — it was already showing), dispatched the same opcode-11 pattern, and
    state stayed at 29 afterward. This rules out the classic-button-row guess for Next.
  - Checked RB's own bundled bot-base source (readable, ships as `.cs`) for how other addons
    click buttons: `AtkAddonControl.FindButton(nodeId)` is only ever used to check
    `Clickable`/`IsValid`, never to actually click — every real interaction still goes through
    `SendAction`. So there's no generic "click this button" shortcut in RB's public API; Next
    had to be a `SendAction` opcode we just hadn't tried.
  - **SOLVED**, third attempt 2026-09-07: an automated opcode sweep (poll for state 29, then
    try `SendAction(2, [3, opcode, 3, 0])` for opcode 0-25, checking state after each) found
    it — opcodes 0-13 were harmless no-ops, **opcode 14 moved the state off 29 and was
    visually confirmed live to actually click Next**. `EmjActionDispatcher.ClickNext`, wired
    into `FFXIVMahjongBot.Pulse()` with a 1-second retry throttle, same pattern as Pass.
  - (Note: the state after clicking read back as 27 in the sweep script, not the 2 seen after
    a manual click earlier — almost certainly just a transient state caught mid-transition
    given the sweep's tight 400ms polling; not investigated further since the visual result
    was confirmed correct.)
- Also confirmed real-world scoring: a dealer menzen-tsumo win for 30 fu / 1 han paid out 1500
  total / 500 each, which matches our `PointsTable`/`PaymentCalculator` output exactly (1000
  base × 1.5 dealer bonus ÷ 3 = 500). The game does track fu internally for display, but our
  flat table happens to agree with the real fu-based formula at the common 30-fu case.

## Aka-dora (red five) encoding + "stuck, won't discard" bug (2026-09-07)

User reported the bot sometimes sits on a genuine discard turn without discarding, requiring
a manual click. Root-caused via a live dump taken right after: the hand held both a normal 5m
(raw 76045, decodes to id 4) and a red 5m (raw 76075) — 76075 didn't fit the normal 0-33 id
range, so `DecodeTile` silently dropped it, undercounting the hand by one tile. Fixed in
`EmjAddonReader.DecodeTile`/`EncodeTileRaw`.

**Correction (later same day):** the first fix assumed aka-dora raw = normal tile's raw + 30
(fit the one red-5m data point: 4+30=34). A second capture caught a drawn red 5s (raw 76077,
alongside an already-held normal 5s) that broke this formula — 22+30=52, not 36. The actual
pattern is simpler: aka-dora ids extend **sequentially right after the normal 0-33 range** —
34/35/36 = red 5m/5p/5s. Red 5m (34) and red 5s (36) are confirmed live; red 5p (35) is
inferred from the pattern, not yet independently confirmed. This second bug had the identical
symptom (hand undercounted by one, discard silently skipped) since the first fix only covered
the tile ids the +30 formula happened to produce.

This alone wouldn't fully explain a *total* freeze (the policy would still discard some other
tracked tile most of the time), so as a second, independent safety net: `FFXIVMahjongBot.Pulse()`
now times out the call-prompt gate after 15 continuous seconds. The call-prompt flag
(`CallPromptFlagAtkValueIndex`) is a single-scenario heuristic — if it's ever wrong on a genuine
discard turn (stuck reading Bool=true when no prompt is actually showing), the bot no longer
freezes indefinitely; it discards anyway once the timeout elapses.

## Call-prompt auto-pass (2026-09-07)

`FFXIVMahjongBot` now automatically passes on any detected call prompt rather than leaving it
for the user to click manually. Two important caveats:

- The dispatch opcode (`SendAction(2, [3, 11, 3, 1])`, opcode 11 = classic button-row,
  option 1 = rightmost/Pass) is an **untested hypothesis** — guessed by analogy with the
  confirmed discard opcode (7), not captured from an actual click. Low risk to try since Pass
  is something you'd otherwise do manually anyway; needs live confirmation.
- The call-prompt check now runs *before* the discard-state check, not gated behind it —
  confirmed live that call prompts can appear at more than one base state code (seen at both
  30 and 15), so checking it only under `state == 30` would miss prompts at 15.
- Deliberately **not** wired to accept calls (Chi/Pon/etc.), even though
  `EmjActionDispatcher.AcceptCall` exists (same opcode, option 0). Two blockers: (1) we don't
  know where the offered tile lives in memory, so we can't decide which call makes sense: (2)
  `EmjAddonReader` doesn't parse melds, so accepting would silently desync our internal hand
  model from the game (it would keep treating the hand as fully concealed after a meld forms).

## State 6 also means "discard now", not just "post-call reduced hand" (2026-09-07)

User reported the bot again appearing to freeze on a discard turn, this time suspecting it
needed a mouse hover over a tile to "see" the hand. A live dump taken with no hover
disproved that directly: state code was **6** (not 30), but all 14 hand slots held valid tile
data — a complete, unreduced hand. State 6 had only been seen before after accepting a call
with a *reduced* hand, hence the original "post-call discard" label. The likely explanation:
the addon flickers between 6 and 30 for the same discard turn, and a bot that only acts on 30
can look frozen if its polling keeps landing on 6 — the "only works after I hover" theory was
probably just the user's mouse movement coincidentally lining up with a poll that caught 30.

Fixed: `FFXIVMahjongBot.Pulse()` now treats state 6 the same as state 30, but only when the
read hand has the full 14 tiles (`EmjOffsets.HandSize`) — the genuine post-call reduced-hand
case is left alone, since we can't correctly evaluate it yet (melds aren't tracked, so a
reduced hand would be misread as the entire hand).

## Post-call discard turn: new state 3 + meld-count inference (2026-09-08)

User reported auto-discard stopped working after manually accepting a Chi (opponent discard
5m, called with 3m+4m — a captured Chi/Pass prompt confirmed via a full AtkValues dump and a
memory scan, see below). Diagnostic dump right after accepting: `state=3 handCount=11` — a
previously-unmapped state code, and a concealed hand smaller than the required 14 (bot's
discard trigger required an exact 14-tile hand, so it silently declined to act).

Fix didn't need real meld tracking: `Shanten.Calculate`/`Ukeire.UsefulTileIds` only ever
consult `hand.Melds.Count`, never the melds' actual tiles (confirmed by reading
`Engine/Shanten.cs`/`Engine/Ukeire.cs`). The meld *count* is derivable arithmetically from the
concealed tile total alone — 14/11/8/5/2 tiles = 0/1/2/3/4 prior calls, since each call takes 3
tile-slots out of the 14-tile-equivalent total. `FFXIVMahjongBot.Pulse()` now builds a `Hand`
with that many placeholder `Meld` objects (arbitrary tile content — never inspected) whenever
`concealedCount % 3 == 2`, and added `EmjOffsets.StatePostCallDiscard = 3` to the recognized
discard-trigger states. Verified live that this produces the identical discard choice a real
meld would, using the actual captured post-Chi hand as a test case.

Note this only fixes the *discard* side. Accepting calls ourselves (Chi/Pon/Kan/Riichi/
Tsumo/Ron) still needs the real offered-tile location and is unaffected by this fix — see below.

## Offered-tile location for Chi (2026-09-08) — still unmapped, first attempt was wrong

Captured a real Chi/Pass prompt: opponent (South, our kamicha) discarded 5m, hand held
3m+4m+8m/4p+8p+9p/2s+4s+5s/West/發發發. A full 50-entry AtkValues dump around the prompt
showed nothing decoding to 5m in either raw-id or texture-offset form — the offered tile is
**not published as a plain AtkValue** in this popup, unlike what the reference project's
button-row scan assumes for their client.

First hypothesis (`atkValues[12] = 1`, decoding as bare id 1 = 2m) was **wrong** — confirmed
live the actual discarded tile was 5m, not 2m; `atkValues[12]` was a coincidence, not the real
field. A full-memory scan (0x0000-0x3000, stepping by 4 bytes, matching against 76045 [5m
texture-offset] and 4 [bare id]) found exactly one hit: `+0x00B8 = 4` (bare 0-33 id, no texture
offset) — a single clean match, no noise at the time.

**Second hypothesis also wrong.** A live Pon prompt (East discarded Green Dragon, id 32) let us
retest `+0x00B8` before touching anything: it still read `4` — stale, not tracking the new tile.
The same Pon capture's full 109-entry AtkValues dump showed `[4]` and `[21]` both reading 76073
(Green Dragon, texture-offset) — a tempting match, and `[21]` sits right at the edge of the
reference project's own `ponClaimScanLo=16..ponClaimScanHi=21` window for their client. But a
third capture (a fresh Chi, East discarded 9m) disproved this too: `[21]` still read the *old*
76073 from the previous Pon, not 9m's value (76049) — confirming both `+0x00B8` and `[21]` are
stale/reused buffer contents that don't get cleared between prompts, not live fields.

**Third candidate, unconfirmed.** That same 9m-Chi capture's full dump showed `[18] = 8` — a
bare 0-33 id that's exactly 9m. But indices 16-21 don't line up structurally between the two
Chi captures at all: the first (5m offered) had texture-offset values in every one of those
slots with no bare id anywhere and never matched 5m anywhere in the array; this one has a `-1`
sentinel mixed with a bare id. That drift suggests the layout in this window depends on how
many candidate melds/buttons the game is juggling for a given hand shape, not a fixed slot with
a fixed meaning — so `[18]=8` might be real, or might be the third coincidence tonight. Given
two prior "confirmed" hypotheses both died on a second test, **treat this as unconfirmed** until
tested against a clearly unrelated tile value with a fresh capture, ideally with the full dump's
`[16..21]` (or wider) window scanned for *any* bare-id match rather than assuming a fixed index.

If passive AtkValues/memory polling keeps producing stale-buffer false positives, the offered
tile may only be available by hooking the native call in real time (as flagged since the very
first "Not yet mapped" note in this doc) rather than reading memory snapshots after the fact —
worth considering before sinking more live-session time into further guesses.

**AtkValues ruled out entirely, methodologically this time (2026-09-08, later same session).**
Built proper before/after diff capture directly into `FFXIVMahjongBot` (`_lastNormalAtkSnapshot`,
refreshed every Pulse while no call prompt is active; diffed against the instant one appears —
see `LogAtkValueDiff`) instead of eyeballing single dumps that can't distinguish a genuinely new
value from stale leftover data. First real test: a fresh Chi (North discarded 7p, id 15,
texture-offset 76056) against a clean 109-entry diff. Only indices 2, 3, 5-8, 12, 13 changed —
all previously-seen UI/state bookkeeping (13 is the known call-prompt flag; 12 has now read
exactly `1` on every Chi capture regardless of which tile was offered, so it's a generic
"a call is available" flag, not tile data) — **nothing anywhere in the array changed to 76056 or
15.** This retroactively explains the `[4]`/`[21]`/`[18]` near-misses above: none of those values
actually changed when their respective prompts opened, they were just already sitting there.
AtkValues is not where this field lives, confirmed rather than assumed.

Extended the same diffing to a full 0x3000-byte raw-memory snapshot of the addon's own struct
(`DumpRawMemorySnapshot`/`LogRawMemoryDiff`), taken at the same transition point, since the field
must live outside the AtkValues array if it's published at all.

**Raw addon struct memory also ruled out (2026-09-08, three more real Chi captures: 7m, 9p, 6m,
all North→self kamicha).** Every capture showed the identical tiny set of changed offsets —
`+0x00F8`/`+0x00FC` (some unrelated counter/pointer pair, same both times), `+0x0D9C` (matches
the reference project's kamicha-discard-count-byte pattern — see the seat-block note below, not
tile data), and `+0x12E8..+0x12F4`. That last one looked promising once (`+0x12F4` decoded as
"bare tile id 3 = 4m") but **the same offset read exactly `3` on both the 7m and the 6m capture**
— a constant, not tracking the actual offered tile at all. Nothing in the full 3072-int scan ever
matched the real tile (76047/6 for 7m, 76058/17 for 9p, 76046/5 for 6m) in either encoding.

**AgentEmj (RB's native `AgentModule.GetAgentInterfaceById(5)`, matching the reference project's
own `AgentId=5`) also ruled out.** Resolved successfully (a valid pointer, confirmed via a
one-time startup log), but its full 2048-int/8KB snapshot showed **zero byte changes** across two
real call-prompt transitions, while the AtkValues diff captured a real, correct transition at the
exact same moment. A live, per-hand-relevant structure should show *something* changing when a
major state transition happens; zero changes strongly suggests `AgentId=5` is either the wrong
agent for our client or isn't the one backing per-hand mahjong state at all — not a useful lead.

**Delayed (+1.5s) diff also empty across all three sources**, ruling out an asynchronous/delayed
write (e.g. a network round-trip) as the explanation for not finding it in the instant-of-
appearance diff.

**Conclusion: this field is not reachable through passive memory polling** — not AtkValues, not
the addon's own struct memory, not the AgentEmj backing structure, immediate or delayed. Three
sources × two time points × six real distinct-tile captures (5m/2m-guess, Green Dragon/Pon, 9m,
7p, 9p, 7m, 6m), all methodologically sound (before/after diffs, not single-snapshot guessing),
all negative. What's left are two fundamentally different techniques, both bigger undertakings
than anything built so far:
1. **Real-time native call/event hooking** — intercept the actual game function that populates
   the popup, rather than reading its aftermath from memory. Flagged as a possibility since the
   project's very first RE session; now the most likely remaining path.
2. **UI node-tree reading instead of flat memory** — the visually "glowing" tile might be purely
   a rendering property (icon/texture reference) on a child node of the discard pile itself,
   never copied into a separate scalar field anywhere. Would need `GetNodeById`/component-tree
   walking (the same capability the `ReceiveEvent`-based Next-button fix also needs — see below),
   reading the highlighted node's icon id directly rather than scanning for an int.

Both need new capability-building, not more guessing at existing offsets — good stopping point
for this specific hunt. Discard automation (including post-call hands) is unaffected either way.

**Side-finding kept for later**: `+0x0D9C`'s changes line up almost exactly with the reference
project's kamicha-discard-count-byte offset (`0x0D9E`, 2 bytes off — consistent with reading a
1-byte counter inside a 4-byte-aligned window), and matches the `-2 from score` pattern already
confirmed on our own `KamichaScore=0x0DA0`. Not the tile field, but a real, well-grounded offset
worth keeping for future opponent-seat meld-inference work.

**Two more improvements, then a final retest — still negative (2026-09-08, same session).**
Found `TwoInt.AsUTF8String` (RB's own `TwoInt` struct) — we'd been reading String8-typed
AtkValues via `.Int` this whole time, which is meaningless packed bytes for a string entry;
we'd never actually seen what button labels say, only inferred "probably a label" from the
type. Wired it in (`EmjAddonReader.DumpAtkValueSnapshot` now populates a `Text` field). Also
found our client's confirmed offsets (`SelfScore=0x0500`, `KamichaScore=0x0DA0`,
`HandArrayStart=0x0DB8`) are byte-identical to the reference project's own "Emj" (EU) layout —
same structural client — and that `AtkAddonControl.TryFindAgentInterface()` resolves the agent
actually *bound to this window*, a more reliable path than guessing `AgentId=5`. Retested Agent
with the corrected resolution on a real Pon (East discarded North Wind): **still zero byte
changes** across the full snapshot, while AtkValues correctly captured the same transition
(`[6]`/`[7]`/`[8]` decoded as `"Pass"`/`"Pon"`/`"Pass"` — confirms English client, confirms the
string decode works). This settles it: AgentEmj isn't a wrong-id artifact, it's just not tied to
per-hand mahjong state at all on this client. Delayed diff empty again too. Conclusion from the
prior section stands, now on a 4th/5th/6th real capture with better tooling, not weaker evidence.

## Screenshot + template-match: a different approach entirely (2026-09-08)

Since every passive-memory avenue is exhausted, pivoted to a fundamentally different idea: crop
a screenshot of the highlighted/glowing tile and compare it against known tile art instead of
reading a value out of memory at all. Two blockers, one solved:

- **Reference images**: solved. `Assets/TileReference/` now has 32 of 34 tile kinds (missing
  White Dragon, which renders blank), pixel-boundary-sliced from the official Square Enix
  Lodestone Doman Mahjong guide page — clean, official Doman-style art (not the "Traditional"
  style also shown on that page, which the game doesn't actually render), not screenshots of our
  own gameplay (which would need many hands played to naturally encounter all 34 kinds). See
  `Assets/TileReference/README.md` for provenance and filenames.
- **Locating the crop region on screen**: solved differently than planned, and better. User
  pointed out the highlighted tile *pulses* (a brightness animation) while nothing else on the
  frozen discard/hand display changes during a call decision — so diffing two screen captures
  ~200ms apart self-locates its bounding box automatically, no node IDs or hardcoded offsets
  needed at all. Implemented as `Addon/ScreenTileMatcher.cs` (`FindChangedRegion` for the diff,
  `MatchTile` for comparing the crop against the 32 references by mean pixel difference), using
  `AtkAddonControl.Bounds` (confirmed via RB reflection) to scope the capture to just the addon
  window. No native RB screenshot API — uses standard `Graphics.CopyFromScreen`.

**Built and wired in, log-only (2026-09-08, same session)**. Added `System.Drawing.Common` to
the csproj for `Bitmap`/`Graphics`. Validated the algorithm against synthetic images and the
real reference set *before* touching the live game: exact region isolation on a synthetic pulse
patch, and 39/39 correct self-identification across every reference image (including all 32
real tiles) — see `.scratch/TileMatcherCheck` (not preserved, trivial to redo). Wired into
`FFXIVMahjongBot.Pulse()` as a log-only diagnostic on the instant a call prompt appears
(`TryIdentifyGlowingTile`), not dispatching on it yet, same verify-before-trusting discipline as
everything else tonight. Deployed dev→live including `Assets/TileReference/*.png` (not
historically part of the .cs-only deploy set — needs to be from now on whenever those change).

**Real risk, unconfirmed**: `RectangleF` (via `AtkAddonControl.Bounds`) already works live, so
`System.Drawing.Common` is very likely already loaded in RB's process — but RB compiles
botbases from loose source with its own on-the-fly compiler, which has already been pickier
than our own `dotnet build` about several things (see the top of this doc: no implicit usings,
no collection-expression-to-interface, etc.). Whether it resolves `Bitmap`/`Graphics` the same
way is unconfirmed until the next restart — first signal will be whether the bot compiles at
all, before we even get to whether the screen-match itself works.

**Live testing, three real bugs found and fixed in sequence, then a confirmed correct match
(2026-09-08, same session).** `System.Drawing.Common` compiled fine under RB's own compiler —
that risk was unfounded. Three real, distinct bugs found via live testing, each root-caused
before attempting a fix rather than guessing:

1. **Browser occlusion.** First live attempt (`guess=north score=343.4`, actually correct by
   coincidence — see below) was captured with a browser window overlapping the game.
   `Graphics.CopyFromScreen` grabs whatever's visually on top at those screen coordinates, not
   the game's actual content. Fixed by switching to `PrintWindow` via the game's own hwnd
   (`ff14bot.Core.Memory.Process.MainWindowHandle`, found via RB reflection) — renders the
   window's content directly regardless of what else is on screen. Matters for a bot meant to
   run while the user alt-tabs away, not just for testing.
2. **Whole-panel capture.** With occlusion fixed, `live_crop.png` showed the "region" was nearly
   the *entire* addon panel (880x501 of a 1008x560 capture), not a tile — `PrintWindow`
   introduces low-level rendering noise spread across the whole frame between two captures, and
   a naive bounding-box-of-all-changed-pixels approach includes all of it. Fixed with
   `FindMostChangedRegion`: slides a tile-proportional window across a summed-area table of
   pixel differences and picks the window with the highest *concentration* of change, not the
   full extent of any change. Validated offline against a synthetic reproduction of the exact
   failure (ambient noise everywhere + one real localized patch) before the next live test:
   naive bbox grabbed the whole synthetic frame, densest-window found the real patch exactly.
3. **Portrait animation.** Region size was now correct, but two live attempts landed on
   different, definitely-wrong tiles (`green`/`7p`, then `green`/`9m`-ish). Added a debug
   marker (draws the chosen window directly on the full panel capture) — it showed the region
   landing squarely on the Moogle player portrait. FFXIV player portraits have their own idle
   animation (subtle bust movement) that changes far more between frames than any tile's glow,
   so an unrestricted search reliably prefers a portrait over the real target. Fixed by
   restricting the search to a play-area sub-rectangle (~58% width/~80% height, centered,
   excluding the portrait strips) before running `FindMostChangedRegion`.

**After all three fixes: correct match, confirmed against ground truth.** Live capture (East
discarded North Wind, Pon prompt) — `region={X=645,Y=188,Width=48,Height=56} guess=north
score=320.7`, marker showed the box sitting directly on a tile visibly reading 北, and the user
confirmed that's the actual tile that was glowing. A second attempt 0.9s later (same
still-open prompt) drifted onto a neighboring tile and guessed wrong (`9m`) — so this isn't
fully reliable yet, but the fundamental mechanism (self-locating via pulse-diff, then template
match) is now proven to work end-to-end at least once. Remaining work before this could drive
real accept/pass dispatch: consistency across repeated attempts on the same prompt (the jitter
suggests either the window size still isn't quite tile-sized, or the diff needs averaging over
more than 2 frames), and a proper confidence-threshold calibration (scores are consistently in
the 150-340 range even for correct matches, far from the near-0 baseline from clean reference-
vs-reference comparisons — live rendering differs enough from the flat Lodestone art that
absolute score alone isn't yet a reliable "trust this" signal). Good stopping point for tonight
— next session should focus on repeat-and-average sampling and score calibration, not further
region-location debugging.

## Screen-match tile identification: region-finding solved, matching needed a real pivot (2026-09-08, later same session)

Continued past the "confirmed correct once, jittery" state above. Region-finding got fully
solved through a sequence of validated fixes: sampling 5 frames instead of 2 and using
per-pixel brightness *range* across all of them (`FindMostVariableRegion`) instead of a single
pairwise diff (a first attempt using pairwise-consecutive-frame "voting" had a real flaw caught
by a synthetic test — a gradual real pulse loses to one sharp glitch); excluding the central
wall-count/turn indicator, which the user caught also has its own idle animation (a second
legitimate competing signal, distinct from the portrait animation fixed earlier); and window
size tuned from **directly measuring real captured pixel data** (an ASCII color-map of an
actual crop) rather than visual estimates — this alone dropped a real failing match's score
from 235 to 194. End state: region-finding lands *exactly* on the real glowing tile,
confirmed repeatedly against user ground truth.

**But matching still failed even with a perfect crop.** Ranked all 32 references against the
real failing crop — the correct answer (`north`) placed **dead last**, 32nd of 33, not just
"close but wrong." Systematically ruled out causes before finding the real one:
- Reference PNG alpha transparency (real, confirmed via pixel inspection) — flattening onto an
  opaque background barely moved the score. Not the main cause.
- Wrong rotation angle — a fine-grained 5°-step search only improved 239→235. Not the main
  cause.
- Shear (in addition to rotation) — tested a rotation×shear grid search offline; best found
  was still worse than the winning wrong answer. Not sufficient either.

**Root cause: genuine 3D perspective distortion**, confirmed visually (a zoomed side-by-side
comparison showed the live tile is visibly trapezoidal — narrower at top than bottom) — the
table is rendered in 3D, so a side-positioned discard pile doesn't just rotate the tile, it
warps it. No amount of rotation or simple shear correction closed this gap.

**Pivot, validated offline before deploying**: instead of correcting live captures to match
flat reference art, capture reference art *from the same position* so it already has the same
warp baked in. Tested first: a real captured tile compared against another real capture of
itself (via resize/resample to simulate frame-to-frame noise) scored ~17, versus ~132 against
the flat reference — an 8x difference. Seeded `Assets/TileReference/north_shimocha.png` with
today's confirmed capture (North Wind, appearing in the "shimocha" relative-seat position).
`ScreenTileMatcher.MatchTile` now strips a recognized `_kamicha`/`_toimen`/`_shimocha` suffix
from whichever reference wins, so callers see the plain tile identity regardless of which
variant (flat or position-specific) matched. Confirmed offline: the exact real crop that
previously matched wrong (`9p`, then `2s`) now matches `north` at score 0.0 against this seed.

**This is a library to grow incrementally, not a one-shot fix.** Only `north` at the
`shimocha` position is covered so far — up to 34 tiles × 3 relative positions (kamicha/toimen/
shimocha; self's own hand isn't relevant here) = 102 possible entries for full coverage, though
partial coverage (common tiles first) is still useful as it accumulates. Each future confirmed
live capture (user states the ground-truth tile, same discipline as tonight) can be dropped
into `Assets/TileReference/{tile}_{position}.png` directly — no code changes needed, `MatchTile`
already searches every file in the directory.

**Generalization confirmed (2026-09-08, immediately after deploying).** A fresh live capture
(genuinely new screen grab, not a replay of the saved reference file) of the same North Wind
tile at the same position scored **6.3** — in the same range as clean reference-vs-reference
self-matches, and nowhere near the ~132-235 range flat art produced. This wasn't a fluke of
matching a file against itself; the approach demonstrably works on independent captures. Path
forward is now just building coverage: capture and confirm more tile/position combinations
during normal play, same one-line-per-file process, no further code changes anticipated unless
a genuinely new failure mode shows up.

## Next-button dispatch downgraded: reproduces the reference project's "stuck state 32" (2026-09-08)

Live-hit the exact failure the reference project documented for the identical mechanism: our
`ClickNext` (`SendAction(2,[3,14,3,0])`, "confirmed working" 2026-09-07 via an opcode sweep +
visual check) put the addon into **state 32**, right after firing. Confirmed live: state 32
reads `callPromptFlag(type=String, bool=True)` — a bogus/garbage read, not a real flag — and
**manual clicking (Next, anywhere on screen, Escape) did nothing; recovery required a full
game client restart.** Not a timing issue we can fix with a longer stability window — this is
the game addon itself desyncing, matching the reference project's own conclusion for the same
opcode-14-via-FireCallback approach. Their fix: route through a native `ReceiveEvent(ButtonClick)`
call on the actual button node (id 97) + its collision node (id 4) instead of `FireCallback`
opcode 14 — see `Mahjong.Plugin.Dalamud/Actions/InputDispatcher.cs::DispatchHandResultNext` in
their repo. We haven't replicated this yet; unclear whether RB's `AtkAddonControl` exposes the
native struct access (`GetNodeById`, `UldManager.SearchNodeById`, `ReceiveEvent`) that requires,
or whether it needs to go through `ff14bot.Core.Memory.CallInjected64` instead (seen as a
fallback path during the original 2026-09-07 opcode sweep, never used).

**`AutoClickNext = false` in `FFXIVMahjongBot.cs` as of this finding — do not flip back to
true without first confirming a non-FireCallback dispatch mechanism actually avoids state 32**,
given the failure cost is now confirmed to be a full game-client restart, not just a stuck bot.

**Lead for next capture session**: user observed the Next button is visibly disabled/grayed
until the result screen finishes settling, then becomes clickable — the game itself signals
readiness rather than us having to guess a fixed wait. Our `HandResultStabilityWindow` (3.5s)
is a copied estimate, not a measured value, and animation length likely varies (network lag,
etc.), so a timer alone could still click too early on a slower hand. Next time on that screen:
dump the button node's state (likely node 97, per the reference project's node ID — see above)
while grayed out vs. right after it becomes clickable, and diff for an enabled/interactable/
alpha flag. Same node-access work needed for the `ReceiveEvent` dispatch fix above, so worth
doing together.

## Not yet mapped

Pon, chi (and its variant-select sub-popup), kan (open/closed/added), riichi
declaration, tsumo, ron, and the call-prompt/pass button layout are all
unverified for this client. Capture each opportunistically during play: dump
AtkValues immediately before and after triggering the action, diff them, and
confirm the visible game state actually changed before trusting an opcode —
a call that "returns" success without moving game state is a real failure
mode worth checking for explicitly, not just assuming success.

## Call-recommendation meld-count bug found and fixed (2026-09-08 later)

The log-only Pon/Chi recommendation (added in `1e5bc66`) fed `_reader.ReadSelfHand(window)`
straight into `HeuristicCallPolicy` with no melds at all, unlike the discard path a few lines
below it, which already infers a meld *count* from the concealed-tile count (`FFXIVMahjongBot.cs`
~line 361) because `ReadSelfHand` never populates `Hand.Melds`. A real capture caught this going
wrong: 02:11:42, West (kamicha) discarded 7p onto a 10-concealed-tile hand (`6m,7m,8m,5pr,6p,7p,
7p,9p,5s,7s` — 10 = 13 − 3×1, meaning one meld already exists), and the bot logged
`call recommendation: PASS on Green` (the "Green" was also wrong — see the screen-match section
above, pre-dates that reference being seeded). Rebuilt the exact hand in `.scratch/
CallPolicyCheck` and ran it both ways: with 0 melds (matching what the live code actually did),
`ShouldCallPon`/`ShouldCallChi` both come back negative (shanten 2, no legal-looking improvement).
With a 1-meld placeholder (matching the true concealed-tile count), shanten drops to 1 and
`ShouldCallChi` returns **5p+6p** (a real Chi, using the held red 5p) — a legitimate call the
buggy code silently missed.

Fixed by adding `BuildHandForCallDecision` (mirrors `PlaceholderMelds`, but for the call-decision
tile count 13/10/7/4/1 rather than the discard-turn count 14/11/8/5/2) and applying it before
calling into `_callPolicy`. Caveat noted in code: unlike the discard path, `HeuristicCallPolicy`'s
yaku-potential heuristic reads placeholder meld *type* (always Pon) and *suit* (always Man) for
its toitoi/honitsu reachability checks, not just meld count — so this is a looser approximation
than the discard path's exactly-equivalent one. Acceptable for a human-reviewed log line, not
sufficient to drive real accept dispatch. 40/40 tests pass, deployed dev→live (`diff -rq` clean),
committed `1665ae7`. **Live-confirmed working** minutes later in the same session: a re-poll of
the same West/kamicha 7p prompt correctly logged `CHI (5p+6p) on 7p` instead of the earlier bug's
PASS.

## Third position-specific reference: 7p at toimen, and a clean PASS confirmation (2026-09-08 later)

User accepted the Chi (5p+6p+7p) recommended above, discarded 9p, then South discarded another
7p, opening a new call prompt. Screen-match correctly guessed `7p` from flat art alone (score
86.1 — no toimen-specific reference existed yet; a real but low-confidence match, well below the
"wrong" range of 130-235 but well above the "clean" position-specific range of 2-9 seen for
kamicha/shimocha). South = toimen for this hand: turn order E-S-W-N with self=North and West
already confirmed as kamicha (see the second-reference section above) puts South two seats over
= toimen, matching this being a third, screen-region-distinct capture (~479,150, vs kamicha's
~338,364 and shimocha's ~525,253). Seeded `Assets/TileReference/7p_toimen.png` — all three
relative-seat shapes now have at least one real sample.

Recommendation logged `PASS on 7p` for hand `6m,7m,8m,7p,7p,5s,7s` (7 concealed = 13-3×2, two
melds: the real Chi just made, plus one still-genuinely-unknown meld from before this session's
observation window). Verified correct via `.scratch/CallPolicyCheck`: this hand is already
0-shanten (tenpai) — `7p,7p` is the pair, `5s,7s` waits on 6s — so Pon-ing the offered 7p would
consume the pair for no shanten gain. Checked both with the placeholder melds the live code
actually used and with the one meld we know for real substituted in (the Chi); same answer
either way, so the meld-type-blindness caveat flagged in the previous section didn't bite here.

## Second kamicha-position tile (9s), and a real policy-vs-human divergence, not a bug (2026-09-08 later)

New hand (fresh deal — 13 concealed, 0 melds), South discarded 9s (South = kamicha for this
hand — seat winds rotate hand to hand, kamicha's *screen region* doesn't; region ~338,336 here
vs. ~338,364 for the earlier West-kamicha capture, close enough in X and a plausible different
discard-pile slot in Y). Screen-match guessed **7s** (score 88.3 — no kamicha-side sou reference
existed yet at all, so it fell back to flat art at low confidence). User confirmed live: real
tile was 9s, called Chi with 7s+8s. Hand: `4m,5m,8m,8m,9m,2p,3p,5p,5pr,3s,7s,7s,8s`.

Checked both tiles through `.scratch/CallPolicyCheck`: fed the wrong tile (7s), the policy says
PON — matching exactly what got logged, confirming the recommendation *code* isn't buggy, just
its input. Fed the real tile (9s), `ShouldCallChi` returns **null (PASS)**, even though the call
does improve shanten (3→2): `HasOpenYakuPotential` finds no path — no yakuhai, toitoi's out
(it's a Chi), honitsu/chinitsu's out (hand still spans all three suits), and tanyao's out because
9s is a terminal. This is the heuristic working as designed (documented in
`Policy/HeuristicCallPolicy.cs`: shape improvement alone isn't enough without a yaku path), not
a bug — a genuine divergence between the conservative heuristic and the human's live call, which
may have been reading an angle (sanshoku, forward planning) the heuristic's yakuhai/toitoi/
honitsu/tanyao checklist doesn't model. Seeded `9s_kamicha.png` — second tile now covered at the
kamicha position (7p, 9s).

## Third kamicha-position tile (3m); recommendation lands on the right call type by pure coincidence (2026-09-08 later)

Same hand continues (the 9s Chi above was evidently passed on, not accepted — this capture's
hand swaps the old `3s` for a newly-drawn `6p`, no meld reduction, still 13 concealed/0 melds).
South (kamicha again) discarded 3m; screen-match guessed **7p** (score 88.7, no kamicha-side man
reference existed yet). User confirmed the real tile was 3m, calling Chi with 4m+5m.

Checked via `.scratch/CallPolicyCheck`: fed 7p, the policy says `CHI (5p+6p)` — matching the
live log exactly. Fed the real 3m, the policy *also* says CHI, but correctly with **4m+5m** —
matching the user's actual call. Notable: the wrong tile happened to produce the right call
*type* (CHI) by coincidence, but with completely wrong tiles (5p+6p, which aren't even a valid
response to a 3m discard) — a concrete reminder that a superficially-plausible-looking logged
recommendation can't be trusted even when the verb matches, until tile identification is solid.
Seeded `3m_kamicha.png` — third tile now covered at kamicha (7p, 9s, 3m).

## Kamicha/toimen skew profiles not yet well-separated at n=1 (2026-09-08 later)

Another South/kamicha discard (7p again, region 300,254) came back correctly identified as `7p`,
but a direct per-reference score check (`.scratch/CallPolicyCheck`, ad hoc — not committed)
showed the *actual* winning reference internally was `7p_toimen` (82.3) over `7p_kamicha` (97.0),
despite this genuinely being a kamicha discard (user-confirmed). Harmless here only because both
position variants normalize to the same tile name after suffix-stripping — the underlying risk
is real: with just one sample per position, kamicha's and toimen's skew profiles aren't yet
well-separated, so two *different* tiles at those two positions could eventually cross-match once
their raw pixel patterns happen to be similar. Noted, not fixed — no multi-sample-per-position
scheme exists yet (would need e.g. a `_kamicha_2` numbering convention and a `NormalizeTileName`
update); deferred until coverage-building actually surfaces a real misidentification from it,
rather than building the infrastructure preemptively.

Also observed: South's kamicha discards now span two visibly different screen-X clusters
(~336-338 for the 9s/3m captures, ~300 for the two 7p/1p captures after them) — consistent with
a discard pile arranged in a growing grid (multiple discards per row before wrapping), not a
single fixed slot per seat. Don't use raw region coordinates alone to judge which relative seat a
capture belongs to; trust the user's stated seat identity.

Next capture (South, 1p, region 300,308) matched correctly too (score 100.6) and was seeded as
`1p_kamicha.png` — kamicha now has 4 tiles covered (7p, 9s, 3m, 1p).

## Second toimen tile (8m) — a real missed Pon caught (2026-09-08 later)

East discarded 8m. East = toimen for this hand: South is confirmed kamicha, and in E-S-W-N turn
order that puts self at West (kamicha is the seat immediately before self), making shimocha=North
and toimen=East. Consistent with region (348,152) — Y near-identical to the established toimen
Y (~150), X shifted (~348 vs ~479) the same way kamicha's X has shifted between discards from a
growing pile.

Screen-match guessed **4s** (score 152.1, squarely in the "wrong" range — no toimen reference for
any man tile existed). Hand `8m,8m,2p,3p,7s,7s,8s` (7 concealed, 2 melds — matches what the live
`BuildHandForCallDecision` fix already infers). Checked via `.scratch/CallPolicyCheck`: fed 4s,
policy says PASS — matches the live log exactly (fix behaving consistently with bad input again).
Fed the real 8m, `ShouldCallPon` returns **true** — a real call the coverage gap caused to be
missed, not a policy bug. Seeded `8m_toimen.png` — toimen now has 2 tiles covered (7p, 8m).

## Fifth kamicha tile (4p), new hand, position resolved by pixel-exact match over score ambiguity (2026-09-08 later)

New hand (fresh deal, 13 concealed/0 melds — seat winds have rotated again). East discarded 4p,
calling Chi; screen-match guessed 7p (score 115.5). Region (300,308) is a **pixel-exact** match
to the previous hand's confirmed South/kamicha 1p capture — but a direct per-reference score
check came out ambiguous (`7p_toimen` 115.5 vs `7p_kamicha` 124.8, only 9 apart — the same n=1
fragility flagged two sections up). Went with **kamicha** on the strength of the exact-pixel
match: the whole position-specific-library premise is that the game renders each relative seat's
discard area in a fixed screen zone regardless of which wind sits there, so an identical pixel
region is stronger evidence than a marginal score difference between two still-sparse references.

Ground truth via `.scratch/CallPolicyCheck`: fed 7p → PASS (matches the live log exactly). Fed
the real 4p → `ShouldCallChi` returns **2p+3p**, matching the user's actual call. Seeded
`4p_kamicha.png` — kamicha now has 5 tiles covered (7p, 9s, 3m, 1p, 4p).

## A genuine region-finder false positive, not a coverage gap (2026-09-08 later)

Same hand continues (East/kamicha discarded 8m for a Pon this time; hand `2m,2m,4m,5m,5m,8m,8m,
6p,4s,5s` matches the post-4p-Chi hand minus a Green discard). Screen-match guessed `6s` at score
248.9 — deep in the "wrong" range, and this time genuinely wrong, not just under-covered:
`auto_regionB_marked.png` shows the detected region sitting right at the play area's left
boundary, immediately next to Mandragora's (East's) portrait, not on any tile at all. The logged
region X (151) lands almost exactly on `playArea.X` (`addonInClient.Width * 0.15` ≈ 151 for a
~1008px-wide client) — i.e. `region.X` relative to the play area is ~0, right at the edge the
15% left-margin is supposed to clear of the portrait strip. Didn't seed anything from this one —
the crop itself isn't a tile, seeding it would poison the library with garbage, unlike every
prior "wrong guess" so far which were all real tile crops just missing a reference.

Ground truth (8m, Pon) checked anyway via `.scratch/CallPolicyCheck`: fed the garbage 6s, policy
says `CHI (4s+5s)` (matches the live log exactly). Fed the real 8m, `ShouldCallPon` returns
**true** — another real missed call, same shape as the earlier 8m/toimen case.

Not fixed yet — one data point isn't enough to safely retune the 15% play-area margin (moving it
could just shift the same edge-effect problem elsewhere without evidence). Worth revisiting if
this recurs: either widen the left margin a few more percent, or reject any detected region that
lands within a few pixels of the play area's own boundary before trusting it.
