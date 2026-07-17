using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace STTmini.App.ViewModels;

/// <summary>
/// 批量列表行状态 → 状态圆点颜色（AGENTS.md §4.5 / §6.3）。
/// Pending=灰、Running=主色、Done=绿、Failed=红。复用 AppTheme 的色板 token。
/// </summary>
public sealed class BatchStatusToBrushConverter : IValueConverter
{
    /// <summary>单例，XAML 里用 {x:Static vm:BatchStatusToBrushConverter.Instance} 引用。</summary>
    public static readonly BatchStatusToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is BatchItemStatus status)
        {
            return status switch
            {
                BatchItemStatus.Pending => GetBrush("PendingBrush"),
                BatchItemStatus.Running => GetBrush("RunningBrush"),
                BatchItemStatus.Done => GetBrush("SuccessBrush"),
                BatchItemStatus.Failed => GetBrush("DangerBrush"),
                _ => GetBrush("PendingBrush"),
            };
        }
        return GetBrush("PendingBrush");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>从应用级资源取色板 token（与 AppTheme.axaml 的 Styles.Resources 同名），找不到则回退灰。</summary>
    private static IBrush GetBrush(string key)
    {
        if (Application.Current is not null
            && ((IResourceHost)Application.Current).TryFindResource(key, out var resource)
            && resource is IBrush brush)
        {
            return brush;
        }
        return Brushes.Gray;
    }
}
