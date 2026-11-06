using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Marmelad.Views;

/// <summary>A settings line: title and hint on the left, the control on the right.</summary>
public class SettingRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingRow));

    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(SettingRow));

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Hint
    {
        get => (string?)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }
}

public sealed record Choice(object Value, string Label);

/// <summary>Binds a radio button to one value of an enum property.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}

/// <summary>Sizes secondary text relative to the main text size.</summary>
public sealed class ScaleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Convert.ToDouble(value, CultureInfo.InvariantCulture)
        * double.Parse(parameter.ToString()!, CultureInfo.InvariantCulture);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
