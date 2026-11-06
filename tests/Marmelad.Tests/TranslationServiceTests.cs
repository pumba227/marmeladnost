using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Marmelad.Core;
using Marmelad.Translators;
using Xunit;

namespace Marmelad.Tests;

public class TranslationServiceTests
{
    private static readonly TranslationOptions ToRussian = new(Languages.Auto, "ru", "en");

    private sealed class FakeTranslator : ITranslator
    {
        private readonly Func<string, string, string, Translation> _answer;

        public FakeTranslator(string name, bool detects, Func<string, string, string, Translation> answer)
        {
            Name = name;
            DetectsLanguage = detects;
            _answer = answer;
        }

        public string Name { get; }
        public bool DetectsLanguage { get; }
        public List<(string Text, string Source, string Target)> Calls { get; } = new();

        public Task<Translation> TranslateAsync(string text, string source, string target, CancellationToken ct)
        {
            lock (Calls) Calls.Add((text, source, target));
            return Task.FromResult(_answer(text, source, target));
        }
    }

    private static Translation Answer(string text, string source, string target, double? confidence = 1) =>
        new(text, source, target, confidence, Array.Empty<DictionaryEntry>(), "Fake");

    private static OcrCandidate Candidate(string language, string text) =>
        new(language, text, new TextScore(text.Length, 1, 1, text.Length));

    /// <summary>Answers with the language and confidence Google really reported for these strings.</summary>
    private static FakeTranslator GoogleLike() => new("Google", true, (text, _, target) => text switch
    {
        "Mup" => Answer("Муп", "hr", target, 0.79),
        "мир" => Answer("world", "ru", target, 0.59),
        "HACTPOVIKVI" => Answer("ХАКТПОВИКВИ", "fi", target, 0.38),
        "НАСТРОЙКИ" => Answer("SETTINGS", "ru", target, 0.53),
        "HOME" => Answer("ДОМ", "en", target, 1.0),
        "НОМЕ" => Answer("НОМ", "ky", target, 0.70),
        "OK" => Answer("ХОРОШО", "en", target, 0.91),
        "ОК" => Answer("ХОРОШО", "ky", target, 0.34),
        "you" => Answer("ты", "en", target, 1.0),
        "уои" => Answer("ух ты", "tg", target, 1.0),
        _ => throw new InvalidOperationException(text),
    });

    [Theory]
    [InlineData("en", "Mup", "ru", "мир", "мир")]
    [InlineData("en", "HACTPOVIKVI", "ru", "НАСТРОЙКИ", "НАСТРОЙКИ")]
    [InlineData("en", "HOME", "ru", "НОМЕ", "HOME")]
    [InlineData("en", "OK", "ru", "ОК", "OK")]
    [InlineData("en", "you", "ru", "уои", "you")]
    [InlineData("ru", "НОМЕ", "en", "HOME", "HOME")]
    public async Task The_reading_whose_language_matches_its_engine_wins(
        string bestLanguage, string bestText, string otherLanguage, string otherText, string expected)
    {
        var service = new TranslationService(new[] { GoogleLike() });
        var outcome = new OcrOutcome(Candidate(bestLanguage, bestText), Candidate(otherLanguage, otherText));

        var result = await service.TranslateRecognizedAsync(outcome, ToRussian, default);

        Assert.Equal(expected, result.SourceText);
    }

    [Fact]
    public async Task Russian_text_goes_to_the_second_language()
    {
        var google = new FakeTranslator("Google", true, (text, _, target) => Answer(text, "ru", target));
        var service = new TranslationService(new[] { google });

        var result = await service.TranslateAsync("Привет", "ru", ToRussian, default);

        Assert.Equal("en", result.TargetLanguage);
        Assert.Equal(2, google.Calls.Count);
    }

    [Fact]
    public async Task Same_target_and_second_language_does_not_loop()
    {
        var google = new FakeTranslator("Google", true, (text, _, target) => Answer(text, "ru", target));
        var service = new TranslationService(new[] { google });

        await service.TranslateAsync("Привет", "ru", new TranslationOptions(Languages.Auto, "ru", "ru"), default);

        Assert.Single(google.Calls);
    }

    [Fact]
    public async Task Falls_back_with_the_ocr_language_when_google_fails()
    {
        var broken = new FakeTranslator("Google", true, (_, _, _) => throw new HttpRequestException("429"));
        var reserve = new FakeTranslator("MyMemory", false, (text, source, target) => Answer("перевод", source, target, null));
        var service = new TranslationService(new ITranslator[] { broken, reserve });

        var result = await service.TranslateAsync("hello", "en", ToRussian, default);

        Assert.Equal("перевод", result.Text);
        Assert.Equal("en", reserve.Calls.Single().Source);
    }

    [Fact]
    public async Task Reports_failure_when_every_service_is_down()
    {
        var broken = new FakeTranslator("Google", true, (_, _, _) => throw new HttpRequestException("down"));
        var service = new TranslationService(new[] { broken });

        await Assert.ThrowsAsync<TranslationFailedException>(() => service.TranslateAsync("hello", "en", ToRussian, default));
    }

    [Fact]
    public async Task A_failed_alternative_does_not_lose_the_primary_reading()
    {
        var google = new FakeTranslator("Google", true, (text, _, target) =>
            text == "bad" ? throw new HttpRequestException("500") : Answer("хорошо", "en", target));
        var service = new TranslationService(new[] { google });
        var outcome = new OcrOutcome(Candidate("en", "good"), Candidate("ru", "bad"));

        var result = await service.TranslateRecognizedAsync(outcome, ToRussian, default);

        Assert.Equal("good", result.SourceText);
    }

    [Fact]
    public async Task Very_long_text_is_cut_to_the_service_limit()
    {
        var google = new FakeTranslator("Google", true, (text, _, target) => Answer(text, "en", target));
        var service = new TranslationService(new[] { google });

        await service.TranslateAsync(new string('a', 9000), "en", ToRussian, default);

        Assert.Equal(5000, google.Calls.Single().Text.Length);
    }

    [Fact]
    public void Parses_a_real_google_answer()
    {
        const string json = """
            {"sentences":[{"trans":"Быстрая лиса. ","orig":"The quick fox. "},{"trans":"Яркий день.","orig":"A bright day."},
            {"translit":"Bystraya"}],
            "dict":[{"pos":"имя прилагательное","terms":["яркий","светлый","ясный","блестящий","живой","прозрачный","способный"]}],
            "src":"en","confidence":0.9}
            """;

        var result = GoogleTranslator.Parse(json, "auto", "ru");

        Assert.Equal("Быстрая лиса. Яркий день.", result.Text);
        Assert.Equal("en", result.SourceLanguage);
        Assert.Equal(0.9, result.Confidence);
        Assert.Equal("имя прилагательное", result.Dictionary.Single().PartOfSpeech);
        Assert.Equal(6, result.Dictionary.Single().Terms.Count);
    }
}
