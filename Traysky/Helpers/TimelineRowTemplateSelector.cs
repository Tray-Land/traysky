using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Traysky.ViewModels.Items;

namespace Traysky.Helpers;

/// <summary>Picks a post card or the "Load more" gap row for an item in the timeline list.</summary>
public sealed class TimelineRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? PostTemplate { get; set; }
    public DataTemplate? GapTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        PostItem => PostTemplate,
        ThreadGapItem => GapTemplate,
        _ => base.SelectTemplateCore(item)
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
