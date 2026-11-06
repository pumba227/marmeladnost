using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Marmelad.Core;
using Marmelad.Native;

namespace Marmelad.Views;

/// <summary>Covers one monitor with its frozen picture and lets the user drag out a region.</summary>
public partial class SelectionWindow : Window
{
    private const double MinSelectionSize = 6;

    private readonly PxRect _bounds;
    private Point _start;
    private bool _dragging;

    public SelectionWindow(MonitorShot shot, bool showHint)
    {
        InitializeComponent();

        _bounds = shot.Bounds;
        Shot.Source = shot.ToImageSource();
        Hint.Visibility = showHint ? Visibility.Visible : Visibility.Collapsed;

        SourceInitialized += (_, _) =>
        {
            WindowEffects.DisableTransitions(this);
            CoverMonitor();
        };

        // Crossing into a monitor with another DPI can resize the window, so fit it once more.
        Loaded += (_, _) => CoverMonitor();
        SizeChanged += (_, _) => DimArea.Rect = new Rect(0, 0, ActualWidth, ActualHeight);
    }

    /// <summary>The region in screen pixels, once the user has released the mouse.</summary>
    public event Action<SelectionWindow, PxRect>? Completed;

    public event Action? Cancelled;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _start = e.GetPosition(this);
        _dragging = true;
        Hint.Visibility = Visibility.Collapsed;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        var area = CurrentArea(e.GetPosition(this));
        DimHole.Rect = area;
        Canvas.SetLeft(Frame, area.X - Frame.BorderThickness.Left);
        Canvas.SetTop(Frame, area.Y - Frame.BorderThickness.Top);
        Frame.Width = area.Width + Frame.BorderThickness.Left * 2;
        Frame.Height = area.Height + Frame.BorderThickness.Top * 2;
        Frame.Visibility = Visibility.Visible;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;

        _dragging = false;
        ReleaseMouseCapture();

        var area = CurrentArea(e.GetPosition(this));
        if (area.Width < MinSelectionSize || area.Height < MinSelectionSize)
        {
            // A stray click keeps the overlay open, as the system snipping tool does.
            DimHole.Rect = Rect.Empty;
            Frame.Visibility = Visibility.Collapsed;
            return;
        }

        Completed?.Invoke(this, ToScreenPixels(area));
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        Cancelled?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Cancelled?.Invoke();
    }

    private Rect CurrentArea(Point cursor)
    {
        var x = Math.Clamp(cursor.X, 0, ActualWidth);
        var y = Math.Clamp(cursor.Y, 0, ActualHeight);
        return new Rect(_start, new Point(x, y));
    }

    private PxRect ToScreenPixels(Rect area)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new PxRect(
            _bounds.X + (int)Math.Round(area.X * dpi.DpiScaleX),
            _bounds.Y + (int)Math.Round(area.Y * dpi.DpiScaleY),
            (int)Math.Round(area.Width * dpi.DpiScaleX),
            (int)Math.Round(area.Height * dpi.DpiScaleY));
    }

    private void CoverMonitor() =>
        NativeMethods.SetWindowPos(
            new WindowInteropHelper(this).Handle, NativeMethods.HWND_TOPMOST,
            _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, NativeMethods.SWP_NOACTIVATE);
}
