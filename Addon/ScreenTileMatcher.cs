using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
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
    /// same-size captures, or null if nothing changed enough (e.g. the pulse animation happened
    /// to be at the same brightness in both frames — caller should retry).
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

    /// <summary>Best-guess tile name for a cropped region, scored by mean per-pixel RGB difference against every reference (resized to match) — lower score is a better match.</summary>
    public (string Name, double Score) MatchTile(Bitmap crop)
    {
        string bestName = "(none)";
        double bestScore = double.MaxValue;
        foreach (var (name, reference) in _references)
        {
            using var resized = new Bitmap(crop, reference.Size);
            double score = MeanPixelDifference(resized, reference);
            if (score >= bestScore)
                continue;
            bestScore = score;
            bestName = name;
        }
        return (bestName, bestScore);
    }

    private static double MeanPixelDifference(Bitmap a, Bitmap b)
    {
        long total = 0;
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                Color pa = a.GetPixel(x, y);
                Color pb = b.GetPixel(x, y);
                total += Math.Abs(pa.R - pb.R) + Math.Abs(pa.G - pb.G) + Math.Abs(pa.B - pb.B);
            }
        }
        return (double)total / (a.Width * a.Height);
    }
}
