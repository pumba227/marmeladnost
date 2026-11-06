using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Marmelad.Core;

namespace Marmelad.Translators;

/// <summary>
/// Public free API, used as a reserve. Quality is lower than Google's and anonymous use is
/// limited to a few thousand characters a day, which is enough to survive an outage.
/// </summary>
public sealed partial class MyMemoryTranslator : ITranslator
{
    private const string Endpoint = "https://api.mymemory.translated.net/get";

    /// <summary>The API limits a request to 500 bytes; Cyrillic takes two bytes per character.</summary>
    private const int MaxChunkBytes = 480;

    private readonly HttpClient _http;

    public MyMemoryTranslator(HttpClient http) => _http = http;

    public string Name => "MyMemory";
    public bool DetectsLanguage => false;

    [GeneratedRegex(@"(?<=[.!?…\n])\s+")]
    private static partial Regex SentenceEnd();

    public async Task<Translation> TranslateAsync(string text, string source, string target, CancellationToken ct)
    {
        if (Languages.Same(source, target))
            return new Translation(text, source, target, null, Array.Empty<DictionaryEntry>(), Name);

        var parts = await Task.WhenAll(Split(text).Select(chunk => TranslateChunkAsync(chunk, source, target, ct)));
        return new Translation(string.Join(" ", parts), source, target, null, Array.Empty<DictionaryEntry>(), Name);
    }

    private async Task<string> TranslateChunkAsync(string chunk, string source, string target, CancellationToken ct)
    {
        var pair = Uri.EscapeDataString($"{Languages.Base(source)}|{Languages.Base(target)}");
        var url = $"{Endpoint}?q={Uri.EscapeDataString(chunk)}&langpair={pair}";

        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = document.RootElement;

        // The status is a number on success and a string on some errors.
        var status = root.GetProperty("responseStatus").ToString();
        if (status != "200") throw new HttpRequestException($"MyMemory answered {status}");

        return root.GetProperty("responseData").GetProperty("translatedText").GetString() ?? "";
    }

    public static IReadOnlyList<string> Split(string text)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length == 0) return;
            chunks.Add(current.ToString().Trim());
            current.Clear();
        }

        foreach (var sentence in SentenceEnd().Split(text))
        {
            foreach (var piece in HardSplit(sentence))
            {
                var candidate = current.Length == 0 ? piece : current + " " + piece;
                if (Encoding.UTF8.GetByteCount(candidate) > MaxChunkBytes) Flush();
                if (current.Length > 0) current.Append(' ');
                current.Append(piece);
            }
        }
        Flush();
        return chunks;
    }

    /// <summary>Cuts a sentence that alone exceeds the limit, at word boundaries where possible.</summary>
    private static IEnumerable<string> HardSplit(string sentence)
    {
        var rest = sentence;
        while (Encoding.UTF8.GetByteCount(rest) > MaxChunkBytes)
        {
            var cut = MaxChunkBytes / 2;
            var space = rest.LastIndexOf(' ', cut);
            if (space > 0) cut = space;
            yield return rest[..cut].Trim();
            rest = rest[cut..].TrimStart();
        }
        if (rest.Length > 0) yield return rest;
    }
}
