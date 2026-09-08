# FFXIVMahjong

A RebornBuddy botbase that plays FFXIV's Doman Mahjong minigame with real
mahjong strategy (shanten-aware discards, yaku-aware calls, riichi timing,
push/fold vs. opponent danger) rather than legal-random moves.

This is an independent implementation. It is **not** derived from, ported
from, or dependent on any third-party mahjong plugin's source code. Public
facts used as a starting point (the addon's memory layout, the game's yaku
list and scoring, and general mahjong theory) are not copyrightable
expression, and no code from any other project is vendored here. Every
offset and dispatch opcode below is treated as a hypothesis to be verified
against a live capture on this client, not an assumption.

## Architecture

Layered so the strategy brain has zero dependency on RebornBuddy — only the
bottom `Addon/` layer touches `ff14bot`:

```
Addon/       RebornBuddy-specific: reads AtkUnitBase memory into a
             StateSnapshot, dispatches discard/call/riichi actions.
Policy/      Discard/call/riichi/push-fold decision-making.
Rules/       Yaku detection + scoring for the confirmed Doman ruleset.
Engine/      Hand decomposition, shanten calculator, ukeire enumeration,
             scoring orchestration.
Core/        Pure value types: Tile, Hand, Meld, Wall, Seat, Wind.
```

`Core` → `Engine` → `Rules` → `Policy` → `Addon`. Arrows point down;
`Core` knows nothing above it.

## Status

Strategy brain is complete and unit-tested offline (Core/Engine/Rules/Policy —
33 tests, all green). Remaining work needs a live client:

1. ~~Core value types~~ — done
2. ~~Confirm the actual in-game Doman ruleset~~ — done, see `docs/ruleset.md`
3. ~~Engine (shanten/ukeire/scoring)~~ — done
4. ~~Rules (yaku detection matching the confirmed ruleset)~~ — done (full
   yaku + yakuman list from the official guide)
5. ~~Policy (discard/call/riichi/push-fold heuristics)~~ — done (efficiency
   + yaku-potential discard, yakuhai/toitoi/honitsu-aware calling, always-
   riichi-when-affordable, shanten-threshold push/fold)
6. Reverse-engineer the live addon memory layout against this client
7. RebornBuddy addon reader + action dispatcher
8. Wire into a BotBase and test end-to-end

## Building & testing

```
dotnet build                                          # main botbase project
dotnet test tests/FFXIVMahjong.Tests/FFXIVMahjong.Tests.csproj
```

## Known open questions (from research, unverified)

- The addon (name `Emj` or `EmjL` depending on client) exposes hand tiles,
  scores, discard counts, and a dora indicator at fixed byte offsets into
  `AtkUnitBase*`, and accepts actions via `AtkUnitBase.FireCallback`. This
  needs to be captured and verified against our own client — offsets are
  known to vary by client variant.
- The discard action is reportedly a two-callback handshake (select, then
  commit). Pon/Chi/Kan/Riichi/Tsumo/Ron dispatch opcodes are not yet
  mapped and will need their own in-game capture session.
