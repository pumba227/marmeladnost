using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Marmelad.Translators;

/// <summary>
/// The free endpoint used by Google's own widgets. It needs no key and is not an official API,
/// so it can start refusing requests; <see cref="TranslationService"/> falls back when it does.
/// </summary>
public sealed class GoogleTranslator : ITranslator
{
    private const string Endpoint =
        "https://translate.googleapis.com/translate_a/single?client=gtx&dt=t&dt=bd&dj=1&hl=ru&ie=UTF-8&oe=UTF-8";

    private const int MaxDictionaryEntries = 3;
    private const int MaxTermsPerEntry = 6;

    private readonly HttpClient _http;

    public GoogleTranslator(HttpClient http) => _http = http;

    public string Name => "Google";
    public bool DetectsLanguage => true;

    public async Task<Translation> TranslateAsync(string text, string source, string target, CancellationToken ct)
    {
        var url = $"{Endpoint}&sl={Uri.EscapeDataString(source)}&tl={Uri.EscapeDataString(target)}";

        // POST: a long selection does not fit into a URL.
        using var body = new FormUrlEncodedContent(new[] { KeyValuePair.Create("q", text) });
        using var response = await _http.PostAsync(url, body, ct);
        response.EnsureSuccessStatusCode();

        return Parse(await response.Content.ReadAsStringAsync(ct), source, target);
    }

    public static Translation Parse(string json, string source, string target)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var text = new StringBuilder();
        foreach (var sentence in root.GetProperty("sentences").EnumerateArray())
        {
            if (sentence.TryGetProperty("trans", out var part)) text.Append(part.GetString());
        }

        var detected = root.TryGetProperty("src", out var src) ? src.GetString() : null;
        double? confidence = root.TryGetProperty("confidence", out var c) ? c.GetDouble() : null;

        var dictionary = new List<DictionaryEntry>();
        if (root.TryGetProperty("dict", out var entries))
        {
            foreach (var entry in entries.EnumerateArray().Take(MaxDictionaryEntries))
            {
                var partOfSpeech = entry.TryGetProperty("pos", out var pos) ? pos.GetString() ?? "" : "";
                var terms = entry.GetProperty("terms").EnumerateArray()
                    .Select(t => t.GetString() ?? "")
                    .Where(t => t.Length > 0)
                    .Take(MaxTermsPerEntry)
                    .ToList();
                if (terms.Count > 0) dictionary.Add(new DictionaryEntry(partOfSpeech, terms));
            }
        }

        return new Translation(text.ToString().Trim(), detected ?? source, target, confidence, dictionary, "Google");
    }
}
