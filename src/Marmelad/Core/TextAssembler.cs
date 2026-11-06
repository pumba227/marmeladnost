using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Marmelad.Core;

public readonly record struct LineBox(string Text, double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
}

/// <summary>
/// Turns OCR lines back into paragraphs. A sentence wrapped over three lines translates far
/// better as one line, while separate list items must stay apart.
/// </summary>
public static partial class TextAssembler
{
    private const double ParagraphGapRatio = 0.8;
    private const double ShortLineRatio = 0.7;

    [GeneratedRegex(@"(?<=[぀-ヿ㐀-鿿가-힯])\s+(?=[぀-ヿ㐀-鿿가-힯])")]
    private static partial Regex CjkGap();

    public static string Assemble(IReadOnlyList<LineBox> lines)
    {
        if (lines.Count == 0) return string.Empty;

        var ordered = lines.OrderBy(l => l.Top).ToList();
        var typicalHeight = ordered.Select(l => l.Height).OrderBy(h => h).ElementAt(ordered.Count / 2);
        var widest = ordered.Max(l => l.Width);

        var text = new StringBuilder(Clean(ordered[0].Text));
        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1];
            var current = ordered[i];
            var next = Clean(current.Text);

            var farApart = current.Top - previous.Bottom > typicalHeight * ParagraphGapRatio;
            var previousEndedEarly = previous.Width < widest * ShortLineRatio;

            if (farApart || previousEndedEarly)
            {
                text.Append('\n').Append(next);
            }
            else if (text.Length > 1 && text[^1] == '-' && char.IsLetter(text[^2]) && next.Length > 0 && char.IsLower(next[0]))
            {
                text.Length--; // A word split by a line-end hyphen.
                text.Append(next);
            }
            else
            {
                text.Append(' ').Append(next);
            }
        }

        return text.ToString().Trim();
    }

    private static string Clean(string line) => CjkGap().Replace(line.Trim(), string.Empty);
}
