using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Ocr;

namespace Marmelad.Core;

public sealed class OcrService
{
    /// <summary>
    /// A pass over a small selection takes milliseconds, so it gets every variant; on a large one
    /// each pass is slow and the text is big enough for the first variants to read it well.
    /// </summary>
    private static readonly TimeSpan VariantBudget = TimeSpan.FromMilliseconds(300);

    private Task<Dictionary<string, OcrEngine>>? _engines;

    /// <summary>Base codes of the recognition languages installed in Windows.</summary>
    public IReadOnlyList<string> InstalledLanguages =>
        OcrEngine.AvailableRecognizerLanguages.Select(l => Languages.Base(l.LanguageTag)).Distinct().ToList();

    /// <summary>Starts loading the engines, which hold their language models in memory.</summary>
    public void Warm() => _engines ??= Task.Run(LoadEngines);

    /// <summary>Lets the models go. The next capture loads them again, picking up new languages.</summary>
    public void Release() => _engines = null;

    private static Dictionary<string, OcrEngine> LoadEngines()
    {
        var engines = new Dictionary<string, OcrEngine>();
        foreach (var language in OcrEngine.AvailableRecognizerLanguages)
        {
            var engine = OcrEngine.TryCreateFromLanguage(language);
            if (engine is not null) engines.TryAdd(Languages.Base(language.LanguageTag), engine);
        }
        return engines;
    }

    public Task<OcrOutcome> RecognizeAsync(
        Bitmap image, string requestedLanguage, string targetLanguage, CancellationToken ct) =>
        Task.Run(() => RecognizeCoreAsync(image, requestedLanguage, targetLanguage, ct), ct);

    private async Task<OcrOutcome> RecognizeCoreAsync(
        Bitmap image, string requestedLanguage, string targetLanguage, CancellationToken ct)
    {
        Warm();
        var engines = SelectEngines(await _engines!, requestedLanguage);
        if (engines.Count == 0) return new OcrOutcome(null, null);

        using var trimmed = ImagePrep.Trim(image, out var background);
        var best = new Dictionary<string, OcrCandidate>();
        var timer = Stopwatch.StartNew();

        foreach (var (scale, pad) in ImagePrep.Plan(trimmed.Width, trimmed.Height))
        {
            ct.ThrowIfCancellationRequested();

            using var prepared = ImagePrep.Prepare(trimmed, scale, pad, background);
            using var softwareBitmap = ImagePrep.ToSoftwareBitmap(prepared);

            foreach (var (code, engine) in engines)
            {
                var result = await engine.RecognizeAsync(softwareBitmap).AsTask(ct);
                var text = TextAssembler.Assemble(ToLineBoxes(result));
                if (text.Length == 0) continue;

                var score = TextScorer.Score(text, Languages.ScriptOf(code));
                if (!best.TryGetValue(code, out var current) || score.Value > current.Score.Value)
                    best[code] = new OcrCandidate(code, text, score);
            }

            if (best.Count > 0 && timer.Elapsed > VariantBudget) break;
        }

        return TextScorer.Pick(best.Values, targetLanguage);
    }

    private static List<KeyValuePair<string, OcrEngine>> SelectEngines(
        Dictionary<string, OcrEngine> engines, string requestedLanguage)
    {
        if (requestedLanguage != Languages.Auto
            && engines.TryGetValue(Languages.Base(requestedLanguage), out var engine))
        {
            return new() { new(Languages.Base(requestedLanguage), engine) };
        }
        return engines.ToList();
    }

    private static List<LineBox> ToLineBoxes(OcrResult result)
    {
        var lines = new List<LineBox>();
        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0) continue;
            var boxes = line.Words.Select(w => w.BoundingRect).ToList();
            lines.Add(new LineBox(
                line.Text,
                boxes.Min(b => b.Left),
                boxes.Min(b => b.Top),
                boxes.Max(b => b.Right),
                boxes.Max(b => b.Bottom)));
        }
        return lines;
    }
}
