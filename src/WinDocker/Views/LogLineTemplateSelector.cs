using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinDocker.Core.Models;

namespace WinDocker.Views;

/// <summary>Picks the template for a log line: stderr lines get their own, red one.</summary>
public sealed class LogLineTemplateSelector : DataTemplateSelector
{
    public DataTemplate Normal { get; set; } = null!;

    public DataTemplate Error { get; set; } = null!;

    protected override DataTemplate SelectTemplateCore(object item) =>
        item is LogLine { IsError: true } ? Error : Normal;

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
