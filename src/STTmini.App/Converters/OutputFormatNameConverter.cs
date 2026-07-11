using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using STTmini.Core.Configuration;

namespace STTmini.App.Converters;

/// <summary>
/// <see cref="OutputFormat"/> → 中文名称，供 ComboBox 的 ItemTemplate 显示。
/// </summary>
public sealed class OutputFormatNameConverter : IValueConverter
{
    public static readonly OutputFormatNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is OutputFormat f ? OutputFormats.GetDisplayName(f) : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value;
}
