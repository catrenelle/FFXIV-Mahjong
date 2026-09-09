using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace FFXIVMahjong.Addon;

/// <summary>
/// Identifies the highlighted/glowing tile in a call prompt (Chi/Pon/etc.) by screen capture
/// instead of memory reads — built 2026-09-08 after passive memory polling was conclusively
/// ruled out for this field (AtkValues, addon struct memory, AgentEmj all checked, all negative
/// across 6 real captures — see docs/addon-capture-log.md).
///
/// The highlighted tile pulses (a brightness/glow animation), and nothing else on the frozen
/// discard/hand display changes while a call decision is pending — so diffing two screen
/// captures taken a fraction of a second apart self-locates it with no need to know its
/// position ahead of time (no node IDs, no hardcoded offsets).
/// </summary>
public sealed class ScreenTileMatcher
{
    private readonly Dictionary<string, Bitmap> _references;

    public ScreenTileMatcher(string referenceDirectory)
    {
        _references = new Dictionary<string, Bitmap>();
        foreach (var file in Directory.GetFiles(referenceDirectory, "*.png"))
            _references[Path.GetFileNameWithoutExtension(file)] = new Bitmap(file);
    }

    /// <summary>
    /// Screen-coordinate capture — grabs whatever is visually on top at those pixels, browser
    /// window included. Kept for the case there's no window handle to work with, but
    /// <see cref="CaptureWindowClient"/> should be preferred whenever a hwnd is available (user
    /// caught a real live capture corrupted by an overlapping browser window, 2026-09-08).
    /// </summary>
    public static Bitmap CaptureRegion(Rectangle region)
    {
        var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(region.Location, Point.Empty, region.Size);
        return bmp;
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private const uint PW_CLIENTONLY = 0x1;
    private const uint PW_RENDERFULLCONTENT = 0x2; // needed for DWM-composited/hardware-accelerated windows (Windows 8.1+) — unconfirmed whether FFXIV's render mode actually honors this, first live test will tell

    /// <summary>
    /// Captures a specific window's client-area content directly via <c>PrintWindow</c>,
    /// immune to whatever else is on top of it on screen (unlike <see cref="CaptureRegion"/>,
    /// which is a raw screen-coordinate grab). <paramref name="clientScreenOrigin"/> is the
    /// client area's screen-space top-left, needed to convert a screen-coordinate rectangle
    /// (e.g. from <c>AtkAddonControl.Bounds</c>) into a crop rectangle within the returned
    /// bitmap. Returns null if the window can't be captured (minimized, invalid handle, etc.).
    /// </summary>
    public static Bitmap? CaptureWindowClient(IntPtr hwnd, out Point clientScreenOrigin)
    {
        clientScreenOrigin = default;
        if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out RECT rect))
            return null;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            return null;

        var origin = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hwnd, ref origin))
            return null;
        clientScreenOrigin = new Point(origin.X, origin.Y);

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        IntPtr hdc = g.GetHdc();
        try
        {
            if (!PrintWindow(hwnd, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT))
            {
                bmp.Dispose();
                return null;
            }
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }
        return bmp;
    }

    /// <summary>
    /// Bounding box of pixels that differ by more than <paramref name="threshold"/> between two
    /// same-size captures, or null if nothing changed enough. Kept for the synthetic-image test
    /// case (no ambient noise there) — <b>do not use against real captures</b>: live-caught
    /// (2026-09-08) <c>PrintWindow</c> introduces low-level rendering noise spread across the
    /// *entire* frame, not just at the real pulsing tile, which balloons a naive bounding box to
    /// nearly the whole window. Use <see cref="FindMostChangedRegion"/> for real captures.
    /// </summary>
    public static Rectangle? FindChangedRegion(Bitmap a, Bitmap b, int threshold = 24)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            return null;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                Color pa = a.GetPixel(x, y);
                Color pb = b.GetPixel(x, y);
                int diff = Math.Abs(pa.R - pb.R) + Math.Abs(pa.G - pb.G) + Math.Abs(pa.B - pb.B);
                if (diff <= threshold)
                    continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }
        return maxX < 0 ? null : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    /// <summary>
    /// Finds the <paramref name="windowWidth"/>x<paramref name="windowHeight"/> sub-window with
    /// the highest total pixel-difference between two same-size captures — robust to ambient
    /// noise spread evenly across the whole frame (which a naive changed-pixel bounding box is
    /// not: one noisy pixel far from the real pulse balloons the box to include everything in
    /// between). The real pulsing tile concentrates a lot of change into a small area, so it
    /// should score far higher than any noise-only window of the same size. O(W*H) after the
    /// summed-area-table precompute, so window sliding itself is O(1) per position.
    /// </summary>
    public static Rectangle? FindMostChangedRegion(Bitmap a, Bitmap b, int windowWidth, int windowHeight)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            return null;
        if (windowWidth <= 0 || windowHeight <= 0 || windowWidth > a.Width || windowHeight > a.Height)
            return null;

        int w = a.Width, h = a.Height;
        var sat = new long[h + 1, w + 1];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color pa = a.GetPixel(x, y);
                Color pb = b.GetPixel(x, y);
                int diff = Math.Abs(pa.R - pb.R) + Math.Abs(pa.G - pb.G) + Math.Abs(pa.B - pb.B);
                sat[y + 1, x + 1] = diff + sat[y, x + 1] + sat[y + 1, x] - sat[y, x];
            }
        }

        long WindowSum(int x0, int y0, int x1, int y1) =>
            sat[y1, x1] - sat[y0, x1] - sat[y1, x0] + sat[y0, x0];

        long bestSum = -1;
        int bestX = 0, bestY = 0;
        for (int y = 0; y <= h - windowHeight; y++)
        {
            for (int x = 0; x <= w - windowWidth; x++)
            {
                long sum = WindowSum(x, y, x + windowWidth, y + windowHeight);
                if (sum <= bestSum)
                    continue;
                bestSum = sum;
                bestX = x;
                bestY = y;
            }
        }
        return bestSum <= 0 ? null : new Rectangle(bestX, bestY, windowWidth, windowHeight);
    }

    /// <summary>
    /// Finds the window with the highest total per-pixel brightness <i>range</i> (max-min of R+G+B
    /// across every sampled frame, not just one pair) — more robust than a single 2-frame diff
    /// against occasional wrong-neighbor lock-on (live-observed 2026-09-08). A first attempt at
    /// fixing this used pairwise-consecutive-frame "voting", but a synthetic test caught a real
    /// flaw in that design: a gradually-ramping real pulse loses to a single sharp one-off glitch,
    /// since voting only counts how many *pairs* show a strong jump, not the total signal over
    /// the whole window. Range-over-all-frames doesn't have that timing-alignment problem — a
    /// genuinely animating tile stays high-range across the whole sample regardless of exactly
    /// when in the capture window its brightness swings, while a one-off artifact only shows up
    /// in the one or two frames it actually occurred in and contributes far less total range.
    /// <paramref name="excludeRegions"/> zeroes out range contribution from those pixels first —
    /// user-caught 2026-09-08: the central wall-count/turn-indicator (dots + star around the
    /// "61" counter) has its own idle animation too, a second legitimate competing signal inside
    /// the play area that isn't the tile we're after.
    /// </summary>
    public static Rectangle? FindMostVariableRegion(
        IReadOnlyList<Bitmap> frames, int windowWidth, int windowHeight, IReadOnlyList<Rectangle>? excludeRegions = null)
    {
        if (frames.Count < 2)
            return null;
        int w = frames[0].Width, h = frames[0].Height;
        foreach (var f in frames)
            if (f.Width != w || f.Height != h)
                return null;
        if (windowWidth <= 0 || windowHeight <= 0 || windowWidth > w || windowHeight > h)
            return null;

        var sat = new long[h + 1, w + 1];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int rangeValue = 0;
                bool excluded = excludeRegions is not null && excludeRegions.Any(r => r.Contains(x, y));
                if (!excluded)
                {
                    int min = int.MaxValue, max = int.MinValue;
                    foreach (var f in frames)
                    {
                        Color c = f.GetPixel(x, y);
                        int sum = c.R + c.G + c.B;
                        if (sum < min) min = sum;
                        if (sum > max) max = sum;
                    }
                    rangeValue = max - min;
                }
                sat[y + 1, x + 1] = rangeValue + sat[y, x + 1] + sat[y + 1, x] - sat[y, x];
            }
        }

        long WindowSum(int x0, int y0, int x1, int y1) =>
            sat[y1, x1] - sat[y0, x1] - sat[y1, x0] + sat[y0, x0];

        long bestSum = -1;
        int bestX = 0, bestY = 0;
        for (int y = 0; y <= h - windowHeight; y++)
        {
            for (int x = 0; x <= w - windowWidth; x++)
            {
                long sum = WindowSum(x, y, x + windowWidth, y + windowHeight);
                if (sum <= bestSum)
                    continue;
                bestSum = sum;
                bestX = x;
                bestY = y;
            }
        }
        return bestSum <= 0 ? null : new Rectangle(bestX, bestY, windowWidth, windowHeight);
    }

    private static readonly RotateFlipType[] Rotations =
    [
        RotateFlipType.RotateNoneFlipNone,
        RotateFlipType.Rotate90FlipNone,
        RotateFlipType.Rotate180FlipNone,
        RotateFlipType.Rotate270FlipNone,
    ];

    /// <summary>
    /// Reference filenames suffixed with one of these (e.g. <c>north_shimocha.png</c>) are
    /// position-specific captures — a real confirmed screenshot from that discard-pile position,
    /// not flat promotional art. Added 2026-09-08 after discovering flat reference art scores
    /// dramatically worse against opponent-pile tiles than a same-position real capture does
    /// (~132 vs ~17 in an offline test) — the table is rendered in 3D perspective, so tiles on
    /// the sides render trapezoidal/skewed in a way a pure rotation can't correct, but a
    /// reference captured from the *same* position already has the same skew baked in. Stripped
    /// from the returned name so callers see the plain tile identity regardless of which variant
    /// (flat or position-specific) actually won the match. Build this library incrementally from
    /// future confirmed live captures — one sample (north/shimocha) as of 2026-09-08.
    /// </summary>
    private static readonly string[] PositionSuffixes = ["_kamicha", "_toimen", "_shimocha"];

    private static string NormalizeTileName(string rawName)
    {
        foreach (var suffix in PositionSuffixes)
        {
            if (rawName.EndsWith(suffix, StringComparison.Ordinal))
                return rawName[..^suffix.Length];
        }
        return rawName;
    }

    /// <summary>
    /// Best-guess tile name for a cropped region, scored by mean per-pixel RGB difference
    /// against every reference (resized to match) — lower score is a better match. Tries the
    /// crop at all 4 rotations, not just as-captured: discard piles are rotated to face each
    /// seat around the table (a tile in the East pile renders sideways relative to our upright
    /// reference art), which a raw pixel comparison would score badly even against a perfect
    /// crop — user-caught 2026-09-08, missed in the original design.
    /// </summary>
    public (string Name, double Score) MatchTile(Bitmap crop)
    {
        string bestName = "(none)";
        double bestScore = double.MaxValue;
        foreach (var rotation in Rotations)
        {
            using var rotated = new Bitmap(crop);
            if (rotation != RotateFlipType.RotateNoneFlipNone)
                rotated.RotateFlip(rotation);

            foreach (var (name, reference) in _references)
            {
                using var resized = new Bitmap(rotated, reference.Size);
                double score = MeanPixelDifference(resized, reference);
                if (score >= bestScore)
                    continue;
                bestScore = score;
                bestName = name;
            }
        }
        return (NormalizeTileName(bestName), bestScore);
    }

    /// <summary>
    /// Compares only the central ~67% of each image, skipping a margin around the outside.
    /// Every mahjong tile shares the same cream background and black border regardless of its
    /// value — a whole-tile comparison spends most of its weight matching that shared border
    /// (which matches almost equally well against every candidate) and dilutes the one part
    /// that's actually discriminative, the character/pattern in the middle. Live-caught
    /// 2026-09-08: a crop that was correctly located and cleanly cropped still matched wrong
    /// until this was added.
    /// </summary>
    private static double MeanPixelDifference(Bitmap a, Bitmap b)
    {
        int marginX = a.Width / 6;
        int marginY = a.Height / 6;
        int x0 = marginX, y0 = marginY;
        int x1 = Math.Max(x0 + 1, a.Width - marginX);
        int y1 = Math.Max(y0 + 1, a.Height - marginY);

        long total = 0;
        int count = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                Color pa = a.GetPixel(x, y);
                Color pb = b.GetPixel(x, y);
                total += Math.Abs(pa.R - pb.R) + Math.Abs(pa.G - pb.G) + Math.Abs(pa.B - pb.B);
                count++;
            }
        }
        return count == 0 ? double.MaxValue : (double)total / count;
    }
}
