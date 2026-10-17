using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Traysky.ViewModels.Items;

namespace Traysky.Helpers;

/// <summary>Picks the row for whichever list a profile tab is showing: a post, a feed, a starter pack or a list.</summary>
public sealed class ProfileRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? PostTemplate { get; set; }
    public DataTemplate? FeedTemplate { get; set; }
    public DataTemplate? StarterPackTemplate { get; set; }
    public DataTemplate? ListTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        ListItem => ListTemplate,
        PostItem => PostTemplate,
        FeedSuggestionItem => FeedTemplate,
        StarterPackItem => StarterPackTemplate,
        _ => base.SelectTemplateCore(item)
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
