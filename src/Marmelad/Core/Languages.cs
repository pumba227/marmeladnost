using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Marmelad.Core;

public enum Script { Latin, Cyrillic, Cjk, Hangul, Arabic, Hebrew, Greek, Devanagari, Thai, Other }

public sealed record LanguageInfo(string Code, string Name);

public static class Languages
{
    public const string Auto = "auto";

    public static IReadOnlyList<LanguageInfo> All { get; } = new LanguageInfo[]
    {
        new("ru", "Русский"),
        new("en", "Английский"),
        new("uk", "Украинский"),
        new("be", "Белорусский"),
        new("kk", "Казахский"),
        new("de", "Немецкий"),
        new("fr", "Французский"),
        new("es", "Испанский"),
        new("it", "Итальянский"),
        new("pt", "Португальский"),
        new("nl", "Нидерландский"),
        new("pl", "Польский"),
        new("cs", "Чешский"),
        new("sk", "Словацкий"),
        new("sl", "Словенский"),
        new("hr", "Хорватский"),
        new("bs", "Боснийский"),
        new("sr", "Сербский"),
        new("bg", "Болгарский"),
        new("ro", "Румынский"),
        new("hu", "Венгерский"),
        new("el", "Греческий"),
        new("sv", "Шведский"),
        new("da", "Датский"),
        new("no", "Норвежский"),
        new("fi", "Финский"),
        new("tr", "Турецкий"),
        new("ar", "Арабский"),
        new("he", "Иврит"),
        new("hi", "Хинди"),
        new("th", "Тайский"),
        new("vi", "Вьетнамский"),
        new("id", "Индонезийский"),
        new("ja", "Японский"),
        new("ko", "Корейский"),
        new("zh-CN", "Китайский (упрощённый)"),
        new("zh-TW", "Китайский (традиционный)"),
    };

    /// <summary>"en-US" → "en"; Google's legacy "iw" → "he"; Windows' "nb" → "no".</summary>
    public static string Base(string code)
    {
        var head = code.Split('-')[0].ToLowerInvariant();
        return head switch { "iw" => "he", "nb" or "nn" => "no", _ => head };
    }

    public static bool Same(string a, string b) => Base(a) == Base(b);

    public static string NameOf(string code)
    {
        var exact = All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.Name;

        var head = Base(code);
        if (head == "zh") return "Китайский";
        return All.FirstOrDefault(l => Base(l.Code) == head)?.Name ?? SystemName(head);
    }

    /// <summary>The translator can report languages that are not offered as a target.</summary>
    private static string SystemName(string code)
    {
        try
        {
            var name = CultureInfo.GetCultureInfo(code).DisplayName;
            return name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : code.ToUpperInvariant();
        }
        catch (CultureNotFoundException)
        {
            return code.ToUpperInvariant();
        }
    }

    public static Script ScriptOf(string code) => Base(code) switch
    {
        "ru" or "uk" or "be" or "bg" or "sr" or "mk" or "kk" or "ky" or "tg" or "mn" => Script.Cyrillic,
        "zh" or "ja" => Script.Cjk,
        "ko" => Script.Hangul,
        "ar" or "fa" or "ur" => Script.Arabic,
        "he" => Script.Hebrew,
        "el" => Script.Greek,
        "hi" => Script.Devanagari,
        "th" => Script.Thai,
        _ => Script.Latin,
    };

    public static Script ScriptOf(char c) => c switch
    {
        >= 'A' and <= 'Z' or >= 'a' and <= 'z' => Script.Latin,
        >= 'À' and <= 'ɏ' or >= 'Ḁ' and <= 'ỿ' => Script.Latin,
        >= 'Ͱ' and <= 'Ͽ' => Script.Greek,
        >= 'Ѐ' and <= 'ԯ' => Script.Cyrillic,
        >= '֐' and <= '׿' => Script.Hebrew,
        >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ' => Script.Arabic,
        >= 'ऀ' and <= 'ॿ' => Script.Devanagari,
        >= '฀' and <= '๿' => Script.Thai,
        >= 'ᄀ' and <= 'ᇿ' or >= '가' and <= '힯' => Script.Hangul,
        >= '぀' and <= 'ヿ' or >= '㐀' and <= '䶿' or >= '一' and <= '鿿' => Script.Cjk,
        _ => Script.Other,
    };
}
