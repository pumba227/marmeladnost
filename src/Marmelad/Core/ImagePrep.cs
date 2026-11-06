using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace Marmelad.Core;

/// <summary>
/// Windows OCR misses screen-sized text and anything touching the image edge: on 105 rendered
/// word samples a plain crop found almost nothing, while a tight crop that is enlarged and
/// surrounded by background found 89, and a few such variants together found 96.
/// </summary>
public static class ImagePrep
{
    private const int MaxSide = 4200;
    private const int ContrastThreshold = 32;
    private const int TrimMargin = 4;

    private static readonly (double Scale, int Pad)[] Variants =
    {
        (2, 60), (5, 120), (2, 0), (2, 20), (4, 60),
    };

    public static IReadOnlyList<(double Scale, int Pad)> Plan(int width, int height)
    {
        var limit = Math.Max(1, MaxSide / (double)Math.Max(width, height));
        var plan = new List<(double Scale, int Pad)>();
        foreach (var (scale, pad) in Variants)
        {
            var step = (Scale: Math.Min(scale, limit), Pad: pad);
            if (!plan.Contains(step)) plan.Add(step);
        }
        return plan;
    }

    /// <summary>Cuts empty background around the text and reports the background colour.</summary>
    public static Bitmap Trim(Bitmap source, out Color background)
    {
        var (pixels, stride) = ReadPixels(source);
        int width = source.Width, height = source.Height;

        var bg = EdgeMedian(pixels, stride, width, height);
        background = Color.FromArgb(bg.R, bg.G, bg.B);

        int left = width, top = height, right = -1, bottom = -1;
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * 4;
                if (Math.Abs(pixels[i] - bg.B) <= ContrastThreshold
                    && Math.Abs(pixels[i + 1] - bg.G) <= ContrastThreshold
                    && Math.Abs(pixels[i + 2] - bg.R) <= ContrastThreshold) continue;

                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }

        var box = right < 0
            ? new Rectangle(0, 0, width, height)
            : Rectangle.FromLTRB(
                Math.Max(0, left - TrimMargin),
                Math.Max(0, top - TrimMargin),
                Math.Min(width, right + TrimMargin + 1),
                Math.Min(height, bottom + TrimMargin + 1));

        return source.Clone(box, PixelFormat.Format32bppPArgb);
    }

    public static Bitmap Prepare(Bitmap source, double scale, int pad, Color background)
    {
        var width = (int)Math.Round(source.Width * scale);
        var height = (int)Math.Round(source.Height * scale);

        var result = new Bitmap(width + pad * 2, height + pad * 2, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(background);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(pad, pad, width, height));
        return result;
    }

    public static SoftwareBitmap ToSoftwareBitmap(Bitmap bitmap)
    {
        var (pixels, _) = ReadPixels(bitmap);
        return SoftwareBitmap.CreateCopyFromBuffer(
            pixels.AsBuffer(), BitmapPixelFormat.Bgra8, bitmap.Width, bitmap.Height, BitmapAlphaMode.Premultiplied);
    }

    private static (byte[] Pixels, int Stride) ReadPixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var pixels = new byte[data.Stride * bitmap.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return (pixels, data.Stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static (byte R, byte G, byte B) EdgeMedian(byte[] pixels, int stride, int width, int height)
    {
        var reds = new List<byte>();
        var greens = new List<byte>();
        var blues = new List<byte>();

        void Sample(int x, int y)
        {
            var i = y * stride + x * 4;
            blues.Add(pixels[i]);
            greens.Add(pixels[i + 1]);
            reds.Add(pixels[i + 2]);
        }

        for (var x = 0; x < width; x++)
        {
            Sample(x, 0);
            Sample(x, height - 1);
        }
        for (var y = 0; y < height; y++)
        {
            Sample(0, y);
            Sample(width - 1, y);
        }

        static byte Median(List<byte> values) => values.OrderBy(v => v).ElementAt(values.Count / 2);
        return (Median(reds), Median(greens), Median(blues));
    }
}
