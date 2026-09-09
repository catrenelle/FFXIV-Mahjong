using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

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

    public static Bitmap CaptureRegion(Rectangle region)
    {
        var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(region.Location, Point.Empty, region.Size);
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
