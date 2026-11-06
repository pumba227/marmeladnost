using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Marmelad.Native;

namespace Marmelad.Core;

/// <summary>A frozen picture of one monitor, so the text cannot change while it is being selected.</summary>
public sealed class MonitorShot : IDisposable
{
    public MonitorShot(Bitmap image, PxRect bounds, PxRect workArea)
    {
        Image = image;
        Bounds = bounds;
        WorkArea = workArea;
    }

    public Bitmap Image { get; }
    public PxRect Bounds { get; }
    public PxRect WorkArea { get; }

    public BitmapSource ToImageSource()
    {
        var area = new Rectangle(0, 0, Image.Width, Image.Height);
        var data = Image.LockBits(area, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            var source = BitmapSource.Create(
                Image.Width, Image.Height, 96, 96, PixelFormats.Pbgra32, null,
                data.Scan0, data.Stride * Image.Height, data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            Image.UnlockBits(data);
        }
    }

    /// <summary>Copies a region given in screen coordinates.</summary>
    public Bitmap Crop(PxRect region)
    {
        var local = new Rectangle(region.X - Bounds.X, region.Y - Bounds.Y, region.Width, region.Height);
        local.Intersect(new Rectangle(0, 0, Image.Width, Image.Height));
        return Image.Clone(local, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
    }

    public void Dispose() => Image.Dispose();
}

public static class ScreenCapture
{
    public static List<MonitorShot> CaptureAll()
    {
        var shots = new List<MonitorShot>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (NativeMethods.GetMonitorInfo(monitor, ref info)) shots.Add(Capture(info));
            return true;
        }, IntPtr.Zero);
        return shots;
    }

    private static MonitorShot Capture(NativeMethods.MONITORINFO monitor)
    {
        var bounds = ToPxRect(monitor.rcMonitor);
        var image = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.CopyFromScreen(
                bounds.X, bounds.Y, 0, 0, new Size(bounds.Width, bounds.Height), CopyPixelOperation.SourceCopy);
        }

        return new MonitorShot(image, bounds, ToPxRect(monitor.rcWork));
    }

    private static PxRect ToPxRect(NativeMethods.RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}
