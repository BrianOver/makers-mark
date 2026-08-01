using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PixelDiff;

/// <summary>
/// Tiny standalone pixel-diff console (U1, make-it-visible plan).
///
/// Exists because <c>tools/shoot.ps1</c> used to be run twice to produce a "before/after"
/// screenshot pair without anything rebuilding the DLL in between -- both shots rendered the
/// same stale binary, came out byte-identical, and were reported as proof a change landed.
/// Nothing in the old flow *measured* the claim. This tool is that measurement: load two
/// PNGs, count differing pixels, report a percentage and the bounding box of the changed
/// region. <c>receipt.ps1 -Diff</c> calls this and fails the whole receipt on a 0% result --
/// a change that alters nothing visible must fail loudly, not pass quietly.
///
/// Usage:
///   dotnet run --project tools/PixelDiff -- &lt;before.png&gt; &lt;after.png&gt;
///
/// Stdout (one key=value per line, parseable by receipt.ps1 or a human):
///   total_pixels=&lt;n&gt;
///   diff_pixels=&lt;n&gt;
///   diff_pct=&lt;float, 4dp&gt;
///   bbox=&lt;x0,y0,x1,y1&gt;  (or "none" when diff_pixels is 0)
///
/// Exit codes:
///   0 = a real (nonzero) diff was found -- the receipt's claim holds up.
///   1 = 0% diff -- the exact failure shape this tool exists to catch.
///   2 = usage/IO error (missing file, unreadable PNG, mismatched dimensions).
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: dotnet run --project tools/PixelDiff -- <before.png> <after.png>");
            return 2;
        }

        var beforePath = args[0];
        var afterPath = args[1];

        foreach (var path in new[] { beforePath, afterPath })
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"FAIL not found: {path}");
                return 2;
            }
        }

        // System.Drawing.Bitmap is Windows-only (see .csproj comment) -- fine here, this
        // whole capture pipeline already needs a Windows desktop GPU session.
        using var before = new Bitmap(beforePath);
        using var after = new Bitmap(afterPath);

        if (before.Width != after.Width || before.Height != after.Height)
        {
            Console.Error.WriteLine(
                $"FAIL size mismatch: before {before.Width}x{before.Height} vs after {after.Width}x{after.Height}");
            return 2;
        }

        int width = before.Width;
        int height = before.Height;
        var rect = new Rectangle(0, 0, width, height);
        BitmapData? beforeData = null;
        BitmapData? afterData = null;

        long diffPixels = 0;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;

        try
        {
            beforeData = before.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            afterData = after.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            int strideB = beforeData.Stride;
            int strideA = afterData.Stride;
            var bytesB = new byte[strideB * height];
            var bytesA = new byte[strideA * height];
            Marshal.Copy(beforeData.Scan0, bytesB, 0, bytesB.Length);
            Marshal.Copy(afterData.Scan0, bytesA, 0, bytesA.Length);

            for (int y = 0; y < height; y++)
            {
                int rowB = y * strideB;
                int rowA = y * strideA;
                for (int x = 0; x < width; x++)
                {
                    int ib = rowB + x * 4;
                    int ia = rowA + x * 4;
                    if (bytesB[ib] != bytesA[ia] || bytesB[ib + 1] != bytesA[ia + 1] ||
                        bytesB[ib + 2] != bytesA[ia + 2] || bytesB[ib + 3] != bytesA[ia + 3])
                    {
                        diffPixels++;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
        }
        finally
        {
            if (beforeData is not null) before.UnlockBits(beforeData);
            if (afterData is not null) after.UnlockBits(afterData);
        }

        long totalPixels = (long)width * height;
        double pct = totalPixels == 0 ? 0.0 : diffPixels * 100.0 / totalPixels;
        string bbox = diffPixels == 0 ? "none" : $"{minX},{minY},{maxX},{maxY}";

        Console.WriteLine($"total_pixels={totalPixels}");
        Console.WriteLine($"diff_pixels={diffPixels}");
        Console.WriteLine($"diff_pct={pct:F4}");
        Console.WriteLine($"bbox={bbox}");

        return diffPixels == 0 ? 1 : 0;
    }
}
