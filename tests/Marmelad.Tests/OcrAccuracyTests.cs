using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Threading.Tasks;
using Marmelad.Core;
using Xunit;
using Xunit.Abstractions;

namespace Marmelad.Tests;

/// <summary>Runs the real Windows engines on rendered text, the way a selection reaches them.</summary>
public class OcrAccuracyTests
{
    private static readonly string[] Words =
    {
        "Bright", "Download", "Привет", "Настройки", "Файл", "cat", "Go", "Да", "window",
        "Перевод", "Save", "сохранить", "Language", "мир", "Откройте",
    };

    private static readonly (string Font, float Size, Color Text, Color Back)[] Styles =
    {
        ("Segoe UI", 13, Color.Black, Color.White),
        ("Segoe UI", 16, Color.Black, Color.White),
        ("Segoe UI", 24, Color.Black, Color.White),
        ("Segoe UI", 13, Color.FromArgb(225, 225, 225), Color.FromArgb(32, 32, 32)),
        ("Arial", 16, Color.FromArgb(225, 225, 225), Color.FromArgb(32, 32, 32)),
        ("Tahoma", 14, Color.White, Color.FromArgb(0, 100, 200)),
        ("Verdana", 14, Color.FromArgb(90, 90, 90), Color.FromArgb(240, 240, 240)),
    };

    private readonly ITestOutputHelper _output;
    private readonly OcrService _ocr = new();

    public OcrAccuracyTests(ITestOutputHelper output) => _output = output;

    private static Bitmap Render(string text, (string Font, float Size, Color Text, Color Back) style, int margin, int width = 0)
    {
        using var font = new Font(style.Font, style.Size, GraphicsUnit.Pixel);
        using var probe = new Bitmap(1, 1);
        using var measure = Graphics.FromImage(probe);
        var limit = width > 0 ? width : 4000;
        var size = measure.MeasureString(text, font, limit, StringFormat.GenericTypographic);

        var bitmap = new Bitmap((int)size.Width + margin * 2 + 2, (int)size.Height + margin * 2, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(style.Back);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var brush = new SolidBrush(style.Text);
        g.DrawString(text, font, brush, new RectangleF(margin, margin, limit, size.Height + 2), StringFormat.GenericTypographic);
        return bitmap;
    }

    private async Task<int> CountWordsReadAsync(int margin)
    {
        var misses = new List<string>();
        var total = 0;
        foreach (var style in Styles)
        {
            foreach (var word in Words)
            {
                total++;
                using var image = Render(word, style, margin);
                var outcome = await _ocr.RecognizeAsync(image, Languages.Auto, "ru", default);
                var readings = new[] { outcome.Best?.Text, outcome.Alternative?.Text };
                if (!readings.Any(r => string.Equals(r, word, StringComparison.OrdinalIgnoreCase)))
                    misses.Add($"{style.Font} {style.Size}: {word} -> {outcome.Best?.Text ?? "(нет)"} / {outcome.Alternative?.Text ?? "-"}");
            }
        }

        _output.WriteLine($"margin {margin}: {total - misses.Count}/{total}");
        misses.ForEach(_output.WriteLine);
        return total - misses.Count;
    }

    [Fact]
    public async Task Reads_single_words_from_a_tight_selection() =>
        Assert.True(await CountWordsReadAsync(3) >= 90, "Меньше 90 из 105 слов");

    [Fact]
    public async Task Reads_single_words_from_a_loose_selection() =>
        Assert.True(await CountWordsReadAsync(45) >= 90, "Меньше 90 из 105 слов");

    [Theory]
    [InlineData("en", "The quick brown fox jumps over the lazy dog. Settings are saved automatically when you close this window.")]
    [InlineData("ru", "Съешь же ещё этих мягких французских булок, да выпей чаю. Настройки сохраняются автоматически.")]
    [InlineData("ru", "Откройте файл config.json и нажмите Save. Version 2.1 поддерживает Windows 11.")]
    [InlineData("en", "Die Einstellungen werden automatisch gespeichert, wenn Sie dieses Fenster schließen.")]
    public async Task Picks_the_right_engine_for_sentences(string expectedEngine, string text)
    {
        foreach (var style in Styles)
        {
            using var image = Render(text, style, 6, width: 560);
            var outcome = await _ocr.RecognizeAsync(image, Languages.Auto, "ru", default);

            _output.WriteLine($"{style.Font} {style.Size}: {outcome.Best}");
            _output.WriteLine($"  alternative: {outcome.Alternative}");
            Assert.NotNull(outcome.Best);
            Assert.Equal(expectedEngine, outcome.Best!.Language);
            Assert.True(Similarity(outcome.Best.Text, text) > 0.93, $"{style.Font} {style.Size}: {outcome.Best.Text}");
        }
    }

    [Fact]
    public async Task An_empty_area_gives_no_text()
    {
        using var image = new Bitmap(300, 120, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(image)) g.Clear(Color.White);

        var outcome = await _ocr.RecognizeAsync(image, Languages.Auto, "ru", default);

        Assert.Null(outcome.Best);
    }

    [Fact]
    public async Task A_forced_language_uses_only_that_engine()
    {
        using var image = Render("Настройки сохраняются автоматически", Styles[1], 6);
        var outcome = await _ocr.RecognizeAsync(image, "en", "ru", default);

        Assert.Equal("en", outcome.Best?.Language);
        Assert.Null(outcome.Alternative);
    }

    /// <summary>1 minus normalised edit distance.</summary>
    private static double Similarity(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var diagonal = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var above = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                diagonal = above;
            }
        }
        return 1 - (double)row[b.Length] / Math.Max(a.Length, b.Length);
    }
}
