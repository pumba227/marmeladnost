using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marmelad.Translators;

public sealed record DictionaryEntry(string PartOfSpeech, IReadOnlyList<string> Terms)
{
    public string TermsText => string.Join(", ", Terms);
}

/// <param name="Confidence">How sure the service is about the source language, when it says.</param>
public sealed record Translation(
    string Text,
    string SourceLanguage,
    string TargetLanguage,
    double? Confidence,
    IReadOnlyList<DictionaryEntry> Dictionary,
    string Provider);

public interface ITranslator
{
    string Name { get; }

    /// <summary>False means the service needs a concrete source language, not "auto".</summary>
    bool DetectsLanguage { get; }

    Task<Translation> TranslateAsync(string text, string source, string target, CancellationToken ct);
}

public sealed class TranslationFailedException : Exception
{
    public TranslationFailedException(string message, Exception? inner) : base(message, inner) { }
}
