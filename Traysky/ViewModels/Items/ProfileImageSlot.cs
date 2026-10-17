using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using Traysky.Services;

namespace Traysky.ViewModels.Items;

/// <summary>
/// The avatar or the banner in the profile editor: what the account has now, plus the pending
/// change (keep, replace with a chosen picture, or remove) and a preview of the result.
/// </summary>
public sealed partial class ProfileImageSlot : ObservableObject
{
    private Uri? _current;

    public ProfileImageChange Change { get; private set; }

    /// <summary>The picture to upload; set only while <see cref="Change"/> is <see cref="ProfileImageChange.Replace"/>.</summary>
    public ProfileImageUpload? Upload { get; private set; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; private set; }

    public bool HasImage => Change switch
    {
        ProfileImageChange.Replace => true,
        ProfileImageChange.Remove => false,
        _ => _current is not null
    };

    public bool IsChanged => Change != ProfileImageChange.Keep;

    /// <summary>Resets to the account's current picture (null when it has none).</summary>
    public void Load(Uri? current)
    {
        _current = current;
        Reset();
    }

    public void Replace(ProfileImageUpload upload, ImageSource preview)
    {
        Change = ProfileImageChange.Replace;
        Upload = upload;
        Preview = preview;
        Notify();
    }

    public void Remove()
    {
        Change = ProfileImageChange.Remove;
        Upload = null;
        Preview = null;
        Notify();
    }

    /// <summary>Drops any pending change and shows the account's current picture again.</summary>
    public void Reset()
    {
        Change = ProfileImageChange.Keep;
        Upload = null;
        Preview = _current is null ? null : new BitmapImage(_current);
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(IsChanged));
    }
}
