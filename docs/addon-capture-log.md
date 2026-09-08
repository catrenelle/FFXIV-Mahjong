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
texture-offset] and 4 [bare id]) found exactly one hit: **`+0x00B8 = 4`** (bare 0-33 id, no
texture offset) — a single clean match, no noise. This is our current best hypothesis for the
offered-tile field, but per the aka-dora lesson (a formula that fit one data point turned out
wrong on a second), **needs a second confirmation on a different tile/suit before trusting it**
— not yet wired into `EmjOffsets`/`EmjAddonReader`. Next call prompt: read `window.Pointer +
0x00B8` as a bare id and compare against whatever tile is actually glowing.

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

## Not yet mapped

Pon, chi (and its variant-select sub-popup), kan (open/closed/added), riichi
declaration, tsumo, ron, and the call-prompt/pass button layout are all
unverified for this client. Capture each opportunistically during play: dump
AtkValues immediately before and after triggering the action, diff them, and
confirm the visible game state actually changed before trusting an opcode —
a call that "returns" success without moving game state is a real failure
mode worth checking for explicitly, not just assuming success.
