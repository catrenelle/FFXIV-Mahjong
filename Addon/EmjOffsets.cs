using System;
using System.Collections.Generic;
using System.Linq;
namespace FFXIVMahjong.Addon;

/// <summary>
/// Byte offsets into the "Emj" addon's <c>AtkUnitBase*</c>, confirmed live against this
/// client build 2026-09-07 via a RebornConsole capture cross-checked against the actual
/// on-screen hand — see docs/addon-capture-log.md for the full session notes. Unverified
/// for the "EmjL" variant or other regions.
/// </summary>
internal static class EmjOffsets
{
    public const string WindowName = "Emj";

    public const int SelfScore = 0x0500;
    public const int ShimochaScore = 0x07E0;
    public const int ToimenScore = 0x0AC0;
    public const int KamichaScore = 0x0DA0;
    public const int SelfDiscardCount = 0x04FE;
    public const int HandArrayStart = 0x0DB8;
    public const int DoraIndicator = 0x0FD8;

    /// <summary>Current (patch-tracked) offsets — NOT the stale 0x160/0x1CA the bundled 2022-era LlamaPlugins UITester uses, which read back garbage on this client.</summary>
    public const int AtkValuesPointer = 0x178;
    public const int AtkValuesCount = 0x1E2;

    public const int StateCodeAtkValueIndex = 0;

    /// <summary>
    /// Single-scenario hypothesis, captured live 2026-09-07 (see docs/addon-capture-log.md):
    /// this AtkValue is Int (arbitrary value) on a plain discard turn, flips to Bool=true
    /// while a Chi/Pass call-prompt modal is open, and reverts to Int right after the call
    /// is resolved. Indices 6-8 also flip to String8 (button label text) during the same
    /// window, consistent with a classic button-row popup. Needs re-confirming across pon/
    /// kan/riichi prompts before fully trusting it.
    /// </summary>
    public const int CallPromptFlagAtkValueIndex = 13;

    /// <summary>Subtract this from a hand-slot or dora-indicator raw int to get the 0-33 tile id.</summary>
    public const int TileTextureBase = 76041;

    public const int HandSize = 14;

    public const int StateOurTurnDiscard = 30;

    /// <summary>
    /// Observed immediately after our own discard commits. Originally guessed to mean "a call
    /// prompt is being shown to us," but a later capture showed an actual Chi/Pass prompt
    /// targeting us while the state code still read 30 — so this is more likely a generic
    /// "waiting on the table" idle state than specifically a call prompt. Use
    /// <see cref="CallPromptFlagAtkValueIndex"/> to detect an actual call prompt.
    /// </summary>
    public const int StateAfterOurDiscard = 15;

    /// <summary>Hand-result "Next" screen (fu/han/score breakdown). Confirmed live 2026-09-07: flips to 2 immediately after clicking Next.</summary>
    public const int StateHandResultNext = 29;

    /// <summary>
    /// Previously believed to only mean "post-accepted-call, discard from your reduced hand"
    /// (matching the third-party research's "selfDeclareList" guess). A live capture
    /// 2026-09-07 showed state 6 with a FULL 14-tile hand (no reduction) — i.e. it can also
    /// mean a plain "you need to discard now" moment, same as state 30. Likely the addon
    /// flickers between 6 and 30 for the same discard turn; a bot that only checks 30 can
    /// appear to freeze if its polling keeps landing on 6. FFXIVMahjongBot now treats this the
    /// same as <see cref="StateOurTurnDiscard"/>, gated on actually reading 14 tiles (to stay
    /// safe on the genuine reduced-hand case, which we can't correctly evaluate yet since
    /// melds aren't tracked).
    /// </summary>
    public const int StatePostDrawOrCallDiscard = 6;

    /// <summary>
    /// Confirmed live 2026-09-08: observed with a full 11-tile concealed hand right after
    /// manually accepting a Chi (13 - 2 tiles consumed into the meld = 11) — a genuine
    /// "discard now" turn following a call, distinct from both <see cref="StateOurTurnDiscard"/>
    /// and <see cref="StatePostDrawOrCallDiscard"/>.
    /// </summary>
    public const int StatePostCallDiscard = 3;
}
