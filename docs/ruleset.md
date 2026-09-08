# Doman Mahjong ruleset

Source: official Lodestone Doman Mahjong guide and yaku list
(na.finalfantasyxiv.com/lodestone/playguide/contentsguide/goldsaucer/doman-mahjong/).
This is the authoritative ruleset to implement — it is standard riichi
mahjong with a simplified fixed han→points table (no fu calculation).

## Structure

- 4 players, 25000 starting points. Full match = 8 hands (East+South
  rounds), quick match = 4 hands (East only).
- Dealer plays first, turns pass right (shimocha). Dealer keeps their seat
  on a dealer win; otherwise it rotates right each hand.
- Tsumo: all three opponents pay, dealer's win pays 1.5x split across
  opponents; non-dealer's loss-to-dealer-tsumo pays double.
- Ron: only the discarder pays. Dealer ron still gets the 1.5x bonus.
- Draw (exhaustive/all tiles drawn, no winner): no points change hands.

## Scoring — han to points (no fu)

| Han | Points |
|---|---|
| 1 | 1,000 |
| 2 | 2,000 |
| 3 | 3,900 |
| 4-5 | 8,000 |
| 6-7 | 12,000 |
| 8-10 | 16,000 |
| 11-12 | 24,000 |
| 13+ (yakuman) | 32,000 |

## Dora

- One dora indicator flipped per hand; the dora is the indicator's
  successor tile (see `Tile.NextForDora`). Each dora tile in hand = +1 han.
- Aka dora: one red 5 per suit, always +1 han when held, independent of
  the dora indicator.
- Ura dora: only revealed on a riichi win — same successor-tile rule
  against a separate hidden indicator.
- Kan flips an additional dora indicator (kan dora).

## Calls

- Pon: claim any player's discard to complete a triplet.
- Chi: claim only kamicha's (preceding player's) discard to complete a
  sequence.
- Kan: closed (self-declared from a concealed quad) or open (claimed
  discard, or upgraded from an existing pon — "added kan" /
  shouminkan). Each kan draws a replacement tile from the dead wall and
  flips a new dora indicator.
- Calling chi or pon locks out riichi for that hand (hand is open).

## Riichi

- Declare when tenpai with a closed hand; pay 1,000 points to the table.
  Counts as a yaku itself (1 han).
- Drawn tiles auto-discard (tsumogiri) after declaring, until the winning
  tile is drawn or the hand ends.
- Double riichi: declared on your very first discard, uninterrupted by any
  call — 2 han instead of 1.
- Ippatsu: win within one uninterrupted go-around after declaring riichi
  (no calls in between) — +1 han.
- Ura dora only applies to riichi wins.

## Yaku (han values as shown in-game)

Values below are for a **closed** hand; where a lower value is listed for
open, that's the open-hand han value for the same yaku.

| Yaku | Closed han | Open han | Notes |
|---|---|---|---|
| Riichi | 1 | — | closed only |
| Double riichi | 2 | — | closed only, first discard, no calls before it |
| Menzen tsumo | 1 | — | closed only |
| Ippatsu | 1 | — | closed only |
| Yakuhai (seat/round wind or dragon triplet) | 1 | 1 | per matching triplet |
| Tanyao (no terminals/honors) | 1 | 1 | kuitan (open tanyao) allowed |
| Pinfu | 1 | — | closed only |
| Sanshoku doujun (mixed triple chi) | 2 | 1 | |
| Ittsu (pure straight, 123-456-789 one suit) | 2 | 1 | |
| Iipeiko (pure double chi) | 1 | — | closed only |
| Ryanpeiko (twice pure double chi) | 3 | — | closed only |
| Chanta (outside hand) | 2 | 1 | |
| Junchan (terminals in all groups) | 3 | 2 | |
| Honroutou (all terminals and honors) | 2 | 2 | triplets/kan only |
| Honitsu (half flush) | 3 | 2 | |
| Chinitsu (full flush) | 6 | 5 | |
| Shousangen (little three dragons) | 2 | 2 | + yakuhai for the dragon triplets |
| Toitoi (all pon/triplets) | 2 | 2 | |
| Sanankou (three concealed triplets) | 2 | 2 | the 3 triplets must be self-formed |
| Sanshoku doukou (triple pon, same number 3 suits) | 2 | 2 | |
| Sankantsu (three kans) | 2 | 2 | |
| Chiitoitsu (seven pairs) | 2 | — | closed only |
| Rinshan kaihou (win off kan replacement draw) | 1 | 1 | |
| Chankan (robbing a kan) | 1 | 1 | |
| Haitei raoyue (win on last drawn tile) | 1 | 1 | |
| Houtei raoyui (win on last discard) | 1 | 1 | |
| Nagashi mangan (all own discards terminal/honor, exhaustive draw) | 4 | — | treated as a draw-time win |

### Yakuman (13 han)

Tenhou/chiihou (blessing of heaven/earth), suuankou (four concealed pon),
daisangen (big three dragons), shousuushii/daisuushii (little/big four
winds), tsuuiisou (all honors), chinroutou (all terminals), ryuuiisou (all
green), chuurenpoutou (nine gates, closed only), kokushi musou (thirteen
orphans, closed only), suukantsu (four kans).

## Open source (from research, still to verify against our client)

- The FFXIV mahjong addon exposes an in-game "Rules" reference the client
  itself uses — if the han/points table above ever looks off in practice,
  re-check it in-game rather than trusting this doc blindly.
