using System;
using System.Collections.Generic;
using System.Linq;

namespace Marmelad.Core;

/// <param name="Value">Overall plausibility; grows with the amount of cleanly read text.</param>
/// <param name="Quality">Share of letters that sit in cleanly read words, 0..1.</param>
/// <param name="ScriptShare">Share of letters written in the engine's own script, 0..1.</param>
/// <param name="Letters">Total letters found.</param>
public readonly record struct TextScore(double Value, double Quality, double ScriptShare, int Letters);

public sealed record OcrCandidate(string Language, string Text, TextScore Score);

/// <param name="Best">The most plausible reading, or null when no text was found.</param>
/// <param name="Alternative">
/// A close reading in another script. Lookalike letters make "HOME" and "НОМЕ" equally clean,
/// so only the translator's language detection can settle these.
/// </param>
public sealed record OcrOutcome(OcrCandidate? Best, OcrCandidate? Alternative);

/// <summary>
/// Windows OCR reports no confidence, so a reading is judged by its shape: an engine fed the
/// wrong alphabet produces words with digits, stray symbols and capitals in the middle.
/// </summary>
public static class TextScorer
{
    private const string EdgePunctuation = ".,;:!?\"'`()[]{}<>«»„“”‘’…—–-•·*/\\|~_=+";
    private const string InnerPunctuation = "-'’._/:@#+%&";

    /// <summary>Latin words are normal inside non-Latin text, but weigh less than native ones.</summary>
    private const double ForeignWordWeight = 0.7;

    /// <summary>The user selects text to translate it, so a tie goes against the target language.</summary>
    private const double TargetLanguageBias = 0.97;

    private const double AlternativeMinRatio = 0.6;
    private const double AlternativeMinScriptShare = 0.5;

    /// <summary>Below this, lookalike letters can fool the shape checks; above it they cannot.</summary>
    private const int ShortTextLetters = 12;

    /// <summary>
    /// English read by the Russian engine stays under a few percent Cyrillic, so this much of a
    /// non-Latin alphabet means it is really on screen. The opposite reading can look spotless
    /// ("nopaepxweaer" for "поддерживает"), which is why shape alone does not decide here.
    /// </summary>
    private const double SolidTextMinScriptShare = 0.3;
    private const double SolidTextMinQuality = 0.8;
    private const double SolidTextBoost = 1.5;

    public static TextScore Score(string text, Script script)
    {
        double value = 0;
        int letters = 0, cleanLetters = 0, scriptLetters = 0;

        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var core = token.Trim(EdgePunctuation.ToCharArray());
            if (core.Length == 0)
            {
                value -= 0.5;
                continue;
            }

            int wordLetters = 0, native = 0, latin = 0, digits = 0, symbols = 0, innerCapitals = 0;
            for (var i = 0; i < core.Length; i++)
            {
                var ch = core[i];
                if (char.IsLetter(ch))
                {
                    wordLetters++;
                    var charScript = Languages.ScriptOf(ch);
                    if (charScript == script) native++;
                    else if (charScript == Script.Latin) latin++;
                    if (i > 0 && char.IsUpper(ch) && char.IsLower(core[i - 1])) innerCapitals++;
                }
                else if (char.IsDigit(ch)) digits++;
                else if (!InnerPunctuation.Contains(ch)) symbols++;
            }

            if (wordLetters == 0)
            {
                value += digits * 0.3;
                continue;
            }

            letters += wordLetters;
            scriptLetters += native;

            var mixedScripts = native > 0 && native < wordLetters;
            if (symbols > 0 || digits > 0 || mixedScripts)
            {
                value -= 0.5 * core.Length;
            }
            else if (innerCapitals > 0)
            {
                value += 0.3 * core.Length; // "YouTube" exists, but "HacTp" is far more common here.
            }
            else if (native == wordLetters)
            {
                value += wordLetters;
                cleanLetters += wordLetters;
            }
            else if (latin == wordLetters)
            {
                value += ForeignWordWeight * wordLetters;
                cleanLetters += wordLetters;
            }
        }

        // One CJK character carries about as much as a whole short word.
        if (script is Script.Cjk or Script.Hangul) value *= 2;

        return new TextScore(
            value,
            letters == 0 ? 0 : (double)cleanLetters / letters,
            letters == 0 ? 0 : (double)scriptLetters / letters,
            letters);
    }

    public static OcrOutcome Pick(IEnumerable<OcrCandidate> candidates, string targetLanguage)
    {
        double Rank(OcrCandidate c) =>
            c.Score.Value
            * (Languages.Same(c.Language, targetLanguage) ? TargetLanguageBias : 1)
            * (IsSolidNativeText(c) ? SolidTextBoost : 1);

        var ranked = candidates
            .Where(c => c.Score.Letters > 0 || c.Text.Any(char.IsDigit))
            .OrderByDescending(Rank)
            .ToList();

        if (ranked.Count == 0) return new OcrOutcome(null, null);

        var best = ranked[0];
        if (best.Score.Letters >= ShortTextLetters) return new OcrOutcome(best, null);

        var bestScript = Languages.ScriptOf(best.Language);
        var alternative = ranked.Skip(1).FirstOrDefault(c =>
            Languages.ScriptOf(c.Language) != bestScript
            && c.Score.ScriptShare >= AlternativeMinScriptShare
            && Rank(c) >= Rank(best) * AlternativeMinRatio);

        return new OcrOutcome(best, alternative);
    }

    private static bool IsSolidNativeText(OcrCandidate c) =>
        Languages.ScriptOf(c.Language) != Script.Latin
        && c.Score.Letters >= ShortTextLetters
        && c.Score.ScriptShare >= SolidTextMinScriptShare
        && c.Score.Quality >= SolidTextMinQuality;
}
