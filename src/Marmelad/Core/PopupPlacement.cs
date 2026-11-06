using System;

namespace Marmelad.Core;

/// <summary>A rectangle in physical screen pixels. Coordinates can be negative on a left-hand monitor.</summary>
public readonly record struct PxRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

public static class PopupPlacement
{
    private static readonly PopupSide[][] Orders =
    {
        new[] { PopupSide.Right, PopupSide.Left, PopupSide.Bottom, PopupSide.Top },
        new[] { PopupSide.Left, PopupSide.Right, PopupSide.Bottom, PopupSide.Top },
        new[] { PopupSide.Bottom, PopupSide.Top, PopupSide.Right, PopupSide.Left },
        new[] { PopupSide.Top, PopupSide.Bottom, PopupSide.Right, PopupSide.Left },
    };

    /// <summary>
    /// Puts the popup on the first side of the selection where it fits. When it fits nowhere
    /// (a selection across the whole screen), it stays on the preferred side, inside the work area.
    /// </summary>
    public static (int X, int Y) Place(PxRect selection, int width, int height, PxRect work, PopupSide preferred, int gap)
    {
        foreach (var side in Orders[(int)preferred])
        {
            var (x, y) = Candidate(selection, width, height, side, gap);
            var fits = side is PopupSide.Right or PopupSide.Left
                ? x >= work.X && x + width <= work.Right
                : y >= work.Y && y + height <= work.Bottom;
            if (fits) return Clamp(x, y, width, height, work);
        }

        var (fallbackX, fallbackY) = Candidate(selection, width, height, preferred, gap);
        return Clamp(fallbackX, fallbackY, width, height, work);
    }

    private static (int X, int Y) Candidate(PxRect selection, int width, int height, PopupSide side, int gap) => side switch
    {
        PopupSide.Right => (selection.Right + gap, selection.Y),
        PopupSide.Left => (selection.X - gap - width, selection.Y),
        PopupSide.Bottom => (selection.X, selection.Bottom + gap),
        _ => (selection.X, selection.Y - gap - height),
    };

    private static (int X, int Y) Clamp(int x, int y, int width, int height, PxRect work) =>
        (Math.Max(work.X, Math.Min(x, work.Right - width)),
         Math.Max(work.Y, Math.Min(y, work.Bottom - height)));
}
