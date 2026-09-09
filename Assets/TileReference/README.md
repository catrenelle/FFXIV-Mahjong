# Doman tile reference images

32 of the 34 tile kinds (missing White Dragon, which renders blank — no source art
to crop), sliced from the official Square Enix Lodestone Doman Mahjong guide page:
https://na.finalfantasyxiv.com/lodestone/playguide/contentsguide/goldsaucer/doman-mahjong/

Cropped from two guide screenshots (Doman-style tile art only — the guide also shows
"Traditional" art for reference but that's not what the game itself renders) using
pixel-content boundary detection (`.scratch/TileReference/` had the working slicer
script, not preserved — trivial to redo from the two source screenshots if needed).

Filenames: `{1-9}{m,p,s}.png` for Man/Pin/Sou, `{east,south,west,north}.png` for winds,
`{green,red}.png` for dragons.

Intended as match templates for a screen-capture-based tile-identification approach —
see the "screenshot + template match" discussion in `docs/addon-capture-log.md` (2026-09-08)
for why: passive memory polling was conclusively ruled out for finding an offered call's
tile identity, across every source RB exposes (AtkValues, addon struct memory, AgentEmj).
