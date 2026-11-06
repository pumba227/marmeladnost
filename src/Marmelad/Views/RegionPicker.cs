using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marmelad.Core;
using Marmelad.Native;

namespace Marmelad.Views;

public sealed record PickedRegion(MonitorShot Shot, PxRect Area);

/// <summary>
/// Shows one overlay per monitor. They stay open after the pick until <see cref="Close"/>, so the
/// popup can open on top of them and take over keyboard focus without a gap.
/// </summary>
public sealed class RegionPicker
{
    private readonly List<SelectionWindow> _windows = new();

    public Task<PickedRegion?> PickAsync(IReadOnlyList<MonitorShot> shots)
    {
        var result = new TaskCompletionSource<PickedRegion?>();

        NativeMethods.GetCursorPos(out var cursor);
        var active = shots.FirstOrDefault(s => Contains(s.Bounds, cursor)) ?? shots[0];

        foreach (var shot in shots)
        {
            var window = new SelectionWindow(shot, showHint: shot == active);
            window.Completed += (_, area) => result.TrySetResult(new PickedRegion(shot, area));
            window.Cancelled += () => result.TrySetResult(null);
            _windows.Add(window);
            window.Show();
        }

        // The window under the cursor gets the keyboard, so Esc works right away.
        _windows[shots.ToList().IndexOf(active)].Activate();
        return result.Task;
    }

    public void Close()
    {
        foreach (var window in _windows) window.Close();
        _windows.Clear();
    }

    private static bool Contains(PxRect rect, NativeMethods.POINT point) =>
        point.X >= rect.X && point.X < rect.Right && point.Y >= rect.Y && point.Y < rect.Bottom;
}
