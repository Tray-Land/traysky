using System;

namespace Traysky.Services;

public enum UploadPhase
{
    Idle,
    Uploading,
    Processing,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// The lifecycle of attaching media to a post: uploading the bytes, then (video only) waiting for
/// the server to transcode. Pure .NET so the transitions, status text and poll budget are testable
/// without WinRT. Transition methods return the new phase and ignore events that don't apply, so a
/// late "completed" after a cancel (or a second cancel) can't resurrect or double-finish the upload.
/// </summary>
public static class UploadProgressPolicy
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>Two minutes at <see cref="PollInterval"/>: covers all but exceptionally long clips.</summary>
    public const int MaxPollAttempts = 60;

    /// <summary>True while work is in flight (the post button is busy and the attachments are locked).</summary>
    public static bool IsActive(UploadPhase phase) => phase is UploadPhase.Uploading or UploadPhase.Processing;

    public static bool CanCancel(UploadPhase phase) => IsActive(phase);

    /// <summary>Attachments can be removed or replaced whenever nothing is uploading.</summary>
    public static bool CanEditAttachments(UploadPhase phase) => !IsActive(phase);

    public static UploadPhase Begin(UploadPhase phase) => IsActive(phase) ? phase : UploadPhase.Uploading;

    public static UploadPhase BeginProcessing(UploadPhase phase) => phase == UploadPhase.Uploading ? UploadPhase.Processing : phase;

    public static UploadPhase Complete(UploadPhase phase) => IsActive(phase) ? UploadPhase.Completed : phase;

    public static UploadPhase Fail(UploadPhase phase) => IsActive(phase) ? UploadPhase.Failed : phase;

    public static UploadPhase Cancel(UploadPhase phase) => IsActive(phase) ? UploadPhase.Cancelled : phase;

    /// <summary>True once <paramref name="attempts"/> status polls have been spent without the job finishing.</summary>
    public static bool IsPollBudgetSpent(int attempts) => attempts >= MaxPollAttempts;

    /// <summary>The status line next to the Post button, or null when there is nothing to say.</summary>
    public static string? StatusText(UploadPhase phase, bool isVideo, int attachmentCount) => phase switch
    {
        UploadPhase.Uploading when isVideo => "Uploading video…",
        UploadPhase.Uploading => attachmentCount > 1 ? "Uploading images…" : "Uploading image…",
        UploadPhase.Processing => "Processing video…",
        UploadPhase.Cancelled => "Upload cancelled",
        _ => null
    };
}
