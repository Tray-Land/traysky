using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Traysky.ViewModels.Items;

namespace Traysky.Helpers;

/// <summary>Picks the person row or the list row on the Blocked &amp; muted screen, whose tabs show either.</summary>
public sealed class ManagedRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ActorTemplate { get; set; }
    public DataTemplate? ListTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        ActorItem => ActorTemplate,
        ListItem => ListTemplate,
        _ => base.SelectTemplateCore(item)
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
