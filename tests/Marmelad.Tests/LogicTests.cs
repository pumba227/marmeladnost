using System.Linq;
using System.Text;
using System.Windows.Input;
using Marmelad.Core;
using Marmelad.Translators;
using Xunit;

namespace Marmelad.Tests;

public class TextScorerTests
{
    private static OcrCandidate Reading(string language, string text) =>
        new(language, text, TextScorer.Score(text, Languages.ScriptOf(language)));

    [Theory]
    // Real output of the Windows engines, captured while building the app.
    [InlineData("The quick brown fox jumps over the lazy dog.", "The quick brown foxjumps over the Iazy dog. уои cIose", "en")]
    [InlineData("HacTp0'7K'-1 coxpaHAOTCA aaTOMawqecK/.", "Настройки сохраняются автоматически.", "ru")]
    [InlineData("OTKP0iTe •asin config.json YI HaXMVITe Save. Version 2.1 nonep)KhBaeT Windows 11.",
                "Откройте файл config.json и нажмите Save. Version 2.1 поддерживает Windows 11.", "ru")]
    [InlineData("OTKpoire •arn config.json VI HaxtvUITe Save. Version 2.1 nopaepxweaer Windows 11.",
                "Откройте файл conig.json и нажмите Save. Version 25 поддерживает Windows 11.", "ru")]
    [InlineData("public static void Main(string[] args) { return 0; }",
                "pub1ic static void Main(string[] args) { return 0; }", "en")]
    public void Picks_the_engine_that_read_its_own_alphabet(string english, string russian, string expected)
    {
        var outcome = TextScorer.Pick(new[] { Reading("en", english), Reading("ru", russian) }, "ru");
        Assert.Equal(expected, outcome.Best!.Language);
    }

    [Fact]
    public void A_tie_goes_against_the_target_language()
    {
        var outcome = TextScorer.Pick(new[] { Reading("ru", "ОК"), Reading("en", "OK") }, "ru");
        Assert.Equal("en", outcome.Best!.Language);
    }

    [Fact]
    public void Lookalike_readings_are_kept_as_an_alternative()
    {
        var outcome = TextScorer.Pick(new[] { Reading("en", "HOME"), Reading("ru", "НОМЕ") }, "ru");
        Assert.Equal("ru", outcome.Alternative!.Language);
    }

    [Fact]
    public void A_latin_reading_from_the_russian_engine_is_no_alternative()
    {
        var outcome = TextScorer.Pick(
            new[] { Reading("en", "Settings are saved"), Reading("ru", "Settings are saved") }, "ru");
        Assert.Null(outcome.Alternative);
    }

    [Fact]
    public void Long_text_is_settled_without_asking_the_translator()
    {
        var outcome = TextScorer.Pick(new[]
        {
            Reading("en", "HACTPOVIKVI COXPAHEHbl"),
            Reading("ru", "НАСТРОЙКИ СОХРАНЕНЫ"),
        }, "ru");

        Assert.Equal("ru", outcome.Best!.Language);
        Assert.Null(outcome.Alternative);
    }

    [Fact]
    public void No_candidates_means_no_text() =>
        Assert.Null(TextScorer.Pick(Enumerable.Empty<OcrCandidate>(), "ru").Best);
}

public class TextAssemblerTests
{
    private static LineBox Line(string text, double top, double width = 400) => new(text, 0, top, width, top + 20);

    [Fact]
    public void Wrapped_lines_become_one_paragraph()
    {
        var text = TextAssembler.Assemble(new[] { Line("The quick brown", 0), Line("fox jumps", 24) });
        Assert.Equal("The quick brown fox jumps", text);
    }

    [Fact]
    public void A_large_gap_starts_a_new_paragraph()
    {
        var text = TextAssembler.Assemble(new[] { Line("First", 0), Line("Second", 60) });
        Assert.Equal("First\nSecond", text);
    }

    [Fact]
    public void A_short_line_ends_a_paragraph()
    {
        var text = TextAssembler.Assemble(new[]
        {
            Line("A long first line of text", 0),
            Line("Short end.", 24, width: 100),
            Line("Next paragraph", 48),
        });
        Assert.Equal("A long first line of text Short end.\nNext paragraph", text);
    }

    [Fact]
    public void A_line_end_hyphen_rejoins_the_word()
    {
        var text = TextAssembler.Assemble(new[] { Line("inter-", 0), Line("national", 24) });
        Assert.Equal("international", text);
    }

    [Fact]
    public void Spaces_between_cjk_characters_are_removed() =>
        Assert.Equal("日本語のテキスト", TextAssembler.Assemble(new[] { Line("日 本 語 の テ キ ス ト", 0) }));

    [Fact]
    public void Lines_are_read_top_to_bottom()
    {
        var text = TextAssembler.Assemble(new[] { Line("second", 24), Line("first", 0) });
        Assert.Equal("first second", text);
    }
}

public class PopupPlacementTests
{
    private static readonly PxRect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void Prefers_the_chosen_side() =>
        Assert.Equal((512, 100), PopupPlacement.Place(new PxRect(100, 100, 400, 50), 300, 200, Work, PopupSide.Right, 12));

    [Fact]
    public void Flips_when_the_chosen_side_has_no_room() =>
        Assert.Equal((1288, 100), PopupPlacement.Place(new PxRect(1600, 100, 300, 50), 300, 200, Work, PopupSide.Right, 12));

    [Fact]
    public void Stays_inside_the_work_area_near_the_bottom() =>
        Assert.Equal((512, 840), PopupPlacement.Place(new PxRect(100, 1000, 400, 30), 300, 200, Work, PopupSide.Right, 12));

    [Fact]
    public void Works_on_a_monitor_with_negative_coordinates()
    {
        var left = new PxRect(-1920, 0, 1920, 1040);
        Assert.Equal((-1488, 100), PopupPlacement.Place(new PxRect(-1900, 100, 400, 50), 300, 200, left, PopupSide.Right, 12));
    }

    [Fact]
    public void A_selection_across_the_whole_screen_keeps_the_popup_on_screen()
    {
        var (x, y) = PopupPlacement.Place(new PxRect(0, 0, 1920, 1040), 300, 200, Work, PopupSide.Right, 12);
        Assert.InRange(x, 0, 1620);
        Assert.InRange(y, 0, 840);
    }
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Alt+Shift+S", ModifierKeys.Alt | ModifierKeys.Shift, Key.S)]
    [InlineData("Ctrl+D1", ModifierKeys.Control, Key.D1)]
    [InlineData("F9", ModifierKeys.None, Key.F9)]
    public void Parses_and_round_trips(string text, ModifierKeys modifiers, Key key)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(new Hotkey(modifiers, key), hotkey);
        Assert.Equal(text, hotkey.ToString());
    }

    [Theory]
    [InlineData("S")]
    [InlineData("Alt+Shift")]
    [InlineData("Alt+Nonsense")]
    [InlineData("")]
    public void Rejects_unusable_combinations(string text) => Assert.False(Hotkey.TryParse(text, out _));

    [Fact]
    public void Shows_digits_as_digits() => Assert.Equal("Ctrl + 1", new Hotkey(ModifierKeys.Control, Key.D1).DisplayText);
}

public class LanguagesTests
{
    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("zh-Hans-CN", "zh")]
    [InlineData("iw", "he")]
    [InlineData("nb", "no")]
    public void Normalises_codes(string code, string expected) => Assert.Equal(expected, Languages.Base(code));

    [Theory]
    [InlineData("en", "Английский")]
    [InlineData("en-US", "Английский")]
    [InlineData("zh-CN", "Китайский (упрощённый)")]
    [InlineData("zh-Hans", "Китайский")]
    public void Names_languages_in_russian(string code, string expected) => Assert.Equal(expected, Languages.NameOf(code));
}

public class MyMemorySplitTests
{
    [Fact]
    public void Short_text_stays_whole() => Assert.Equal(new[] { "Hello. World." }, MyMemoryTranslator.Split("Hello. World."));

    [Fact]
    public void Every_chunk_fits_the_byte_limit_and_no_word_is_lost()
    {
        var text = string.Join(" ", Enumerable.Repeat("Длинное предложение на русском языке без точки", 60));
        var chunks = MyMemoryTranslator.Split(text);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(Encoding.UTF8.GetByteCount(c) <= 480));
        Assert.Equal(text.Split(' ').Length, chunks.Sum(c => c.Split(' ').Length));
    }

    [Fact]
    public void A_single_endless_word_is_still_cut()
    {
        var chunks = MyMemoryTranslator.Split(new string('я', 1000));
        Assert.All(chunks, c => Assert.True(Encoding.UTF8.GetByteCount(c) <= 480));
        Assert.Equal(1000, chunks.Sum(c => c.Length));
    }
}

public class SettingsTests
{
    [Fact]
    public void Normalize_repairs_a_hand_edited_file()
    {
        var settings = new AppSettings { HotkeyText = "broken", TextSize = 500, Transparency = -3, AutoCloseSeconds = -1 };
        settings.Normalize();

        Assert.Equal(Hotkey.Default.ToString(), settings.HotkeyText);
        Assert.Equal(AppSettings.MaxTextSize, settings.TextSize);
        Assert.Equal(0, settings.Transparency);
        Assert.Equal(0, settings.AutoCloseSeconds);
    }
}
