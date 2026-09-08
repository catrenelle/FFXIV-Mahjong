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
  trigger it ourselves. Tried `SendAction(2, [3, 11, 3, 0])` live 2026-09-07: **inconclusive**,
  not confirmed working or broken. Both the before- and after-dispatch state reads came back
  30 ("our turn to discard") — the Next screen had already resolved on its own (likely an
  auto-advance/timeout, or waiting on other players) before the dispatch fired, so the test
  never actually caught state 29. Still worth trying again, next time closer to when the
  screen first appears.
- Also confirmed real-world scoring: a dealer menzen-tsumo win for 30 fu / 1 han paid out 1500
  total / 500 each, which matches our `PointsTable`/`PaymentCalculator` output exactly (1000
  base × 1.5 dealer bonus ÷ 3 = 500). The game does track fu internally for display, but our
  flat table happens to agree with the real fu-based formula at the common 30-fu case.

## Aka-dora (red five) encoding + "stuck, won't discard" bug (2026-09-07)

User reported the bot sometimes sits on a genuine discard turn without discarding, requiring
a manual click. Root-caused via a live dump taken right after: the hand held both a normal 5m
(raw 76045, decodes to id 4) and a red 5m (raw 76075) — 76075 didn't fit the normal 0-33 id
range, so `DecodeTile` silently dropped it, undercounting the hand by one tile. The relationship
is clean: **76075 = 76045 + 30**, i.e. a red five's raw value is the normal tile's raw value
plus 30. Only red 5m (relative id 34) is confirmed live; red 5p (43) and red 5s (52) are
inferred from the same +9-per-suit spacing normal ids already use, not yet independently
confirmed. Fixed in `EmjAddonReader.DecodeTile`/`EncodeTileRaw`.

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

## Not yet mapped

Pon, chi (and its variant-select sub-popup), kan (open/closed/added), riichi
declaration, tsumo, ron, and the call-prompt/pass button layout are all
unverified for this client. Capture each opportunistically during play: dump
AtkValues immediately before and after triggering the action, diff them, and
confirm the visible game state actually changed before trusting an opcode —
a call that "returns" success without moving game state is a real failure
mode worth checking for explicitly, not just assuming success.
