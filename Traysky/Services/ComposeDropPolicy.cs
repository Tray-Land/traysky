namespace Traysky.Services;

public enum ComposeDropKind
{
    None,
    Media,
    Text
}

/// <summary>
/// What a drag-and-drop onto the compose box should do, from the formats the drag carries. Media
/// wins over text: a picture dragged out of a browser also carries its URL and alt text, and the
/// user means the picture. Pure .NET so it's testable without WinRT.
/// </summary>
public static class ComposeDropPolicy
{
    public static ComposeDropKind Classify(bool hasStorageItems, bool hasBitmap, bool hasText, bool hasWebLink)
    {
        if (hasStorageItems || hasBitmap)
            return ComposeDropKind.Media;

        return hasText || hasWebLink ? ComposeDropKind.Text : ComposeDropKind.None;
    }

    public static string Caption(ComposeDropKind kind) => kind switch
    {
        ComposeDropKind.Media => "Attach to post",
        ComposeDropKind.Text => "Add text to post",
        _ => string.Empty
    };
}
