using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marmelad.Core;

namespace Marmelad.Translators;

public sealed record TranslationOptions(string Source, string Target, string FallbackTarget);

public sealed record RecognizedTranslation(string SourceText, Translation Translation);

public sealed class TranslationService
{
    private const int MaxTextLength = 5000;

    private const double ConfidenceMargin = 0.02;

    private readonly IReadOnlyList<ITranslator> _providers;

    public TranslationService(IReadOnlyList<ITranslator> providers) => _providers = providers;

    public async Task<Translation> TranslateAsync(
        string text, string languageHint, TranslationOptions options, CancellationToken ct)
    {
        var first = await CallProvidersAsync(text, languageHint, options, ct);
        return await RedirectIfAlreadyTargetAsync(text, first, options, ct);
    }

    /// <summary>Translates an OCR outcome, letting language detection decide between two readings.</summary>
    public async Task<RecognizedTranslation> TranslateRecognizedAsync(
        OcrOutcome outcome, TranslationOptions options, CancellationToken ct)
    {
        var best = outcome.Best ?? throw new ArgumentException("Нет распознанного текста", nameof(outcome));

        if (outcome.Alternative is not { } alternative)
        {
            return new RecognizedTranslation(
                best.Text, await TranslateAsync(best.Text, best.Language, options, ct));
        }

        var bestCall = CallProvidersAsync(best.Text, best.Language, options, ct);
        var alternativeCall = CallProvidersAsync(alternative.Text, alternative.Language, options, ct);

        var primary = await bestCall;
        Translation? other = null;
        try
        {
            other = await alternativeCall;
        }
        catch (TranslationFailedException)
        {
            // The primary reading already has a translation, so this one is not needed.
        }

        var useOther = other is not null && IsBetterReading(other, alternative.Language, primary, best.Language);
        var (text, translation) = useOther ? (alternative.Text, other!) : (best.Text, primary);
        return new RecognizedTranslation(text, await RedirectIfAlreadyTargetAsync(text, translation, options, ct));
    }

    /// <summary>
    /// A misread word still gets a confident verdict ("Mup" is Croatian at 0.79, the real "мир"
    /// only Russian at 0.59), so the deciding sign is whether the detected language matches the
    /// OCR engine that produced the reading. Confidence only breaks ties.
    /// </summary>
    private static bool IsBetterReading(Translation other, string otherEngine, Translation primary, string primaryEngine)
    {
        var otherAgreement = Agreement(other, otherEngine);
        var primaryAgreement = Agreement(primary, primaryEngine);
        if (otherAgreement != primaryAgreement) return otherAgreement > primaryAgreement;

        return other.Confidence is { } otherConfidence
            && primary.Confidence is { } primaryConfidence
            && otherConfidence > primaryConfidence + ConfidenceMargin;
    }

    private static int Agreement(Translation translation, string engineLanguage)
    {
        if (Languages.Same(translation.SourceLanguage, engineLanguage)) return 2;
        return Languages.ScriptOf(translation.SourceLanguage) == Languages.ScriptOf(engineLanguage) ? 1 : 0;
    }

    /// <summary>Russian text translated "to Russian" is useless, so it goes to the second language.</summary>
    private async Task<Translation> RedirectIfAlreadyTargetAsync(
        string text, Translation translation, TranslationOptions options, CancellationToken ct)
    {
        if (!Languages.Same(translation.SourceLanguage, options.Target)
            || Languages.Same(options.FallbackTarget, options.Target))
        {
            return translation;
        }

        var redirected = options with { Source = translation.SourceLanguage, Target = options.FallbackTarget };
        return await CallProvidersAsync(text, translation.SourceLanguage, redirected, ct);
    }

    private async Task<Translation> CallProvidersAsync(
        string text, string languageHint, TranslationOptions options, CancellationToken ct)
    {
        if (text.Length > MaxTextLength) text = text[..MaxTextLength];

        Exception? lastError = null;
        foreach (var provider in _providers)
        {
            var source = options.Source == Languages.Auto && !provider.DetectsLanguage
                ? languageHint
                : options.Source;
            try
            {
                return await provider.TranslateAsync(text, source, options.Target, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                lastError = error;
                Log.Warn($"{provider.Name}: {error.Message}");
            }
        }

        throw new TranslationFailedException("Не удалось связаться с сервисами перевода", lastError);
    }
}
