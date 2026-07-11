using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using STTmini.App.ViewModels;

namespace STTmini.App;

/// <summary>
/// 按 ViewModel 名称匹配同命名空间下同名 View 的数据模板。
/// 约定：STTmini.App.ViewModels.FooViewModel → STTmini.App.Views.FooView。
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public bool SupportsRecycling => false;

    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
        }

        var fullName = data.GetType().FullName!;
        var viewName = fullName.Replace("ViewModel", "View", StringComparison.Ordinal)
                               .Replace(".ViewModels.", ".Views.", StringComparison.Ordinal);
        var type = Type.GetType(viewName);
        if (type is null)
        {
            return new TextBlock { Text = $"未找到视图：{viewName}" };
        }

        return (Control)Activator.CreateInstance(type)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
