using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Traysky.Services;

/// <summary>
/// Saved threads on this PC: <c>LocalFolder\threads\{id}\thread.json</c>, with each picked image or
/// video beside it as its own file, so the JSON stays small and Bluesky drafts (which can't hold media)
/// aren't the limit. Plain files, not encrypted: nothing here is a credential. Every call returns a
/// failure value and logs rather than throwing; a thread that can't be read is skipped, not fatal.
/// </summary>
public static class SavedThreadStore
{
    private const string ThreadFile = "thread.json";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static string Root => Path.Combine(ApplicationData.Current.LocalFolder.Path, "threads");

    private static string FolderOf(string id) => Path.Combine(Root, id);

    /// <summary>Every saved thread, newest first. Media bytes aren't loaded (see <see cref="LoadMediaAsync"/>).</summary>
    public static async Task<List<SavedThread>> ListAsync()
    {
        await Gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                List<SavedThread> threads = [];
                if (!Directory.Exists(Root))
                    return threads;

                foreach (string folder in Directory.EnumerateDirectories(Root))
                {
                    try
                    {
                        string file = Path.Combine(folder, ThreadFile);
                        if (!File.Exists(file))
                            continue;

                        SavedThread? thread = JsonSerializer.Deserialize(File.ReadAllBytes(file), SavedThreadJsonContext.Default.SavedThread);
                        if (thread is not null && string.Equals(thread.Id, Path.GetFileName(folder), StringComparison.Ordinal))
                            threads.Add(thread);
                    }
                    catch (Exception ex)
                    {
                        LogService.Warn("SavedThreads", $"Skipping unreadable thread '{Path.GetFileName(folder)}': {ex.Message}");
                    }
                }

                return threads.OrderByDescending(t => t.UpdatedAt).ToList();
            });
        }
        catch (Exception ex)
        {
            LogService.Warn("SavedThreads", $"Listing saved threads failed: {ex.Message}");
            return [];
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Writes the thread and the bytes of every media item that has them, removing files the thread no
    /// longer refers to. Media with no bytes keeps the file it already has. False when it couldn't be written.
    /// </summary>
    public static async Task<bool> SaveAsync(SavedThread thread)
    {
        await Gate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                string folder = FolderOf(thread.Id);
                Directory.CreateDirectory(folder);

                HashSet<string> keep = new(StringComparer.OrdinalIgnoreCase) { ThreadFile };
                for (int p = 0; p < thread.Posts.Count; p++)
                {
                    List<SavedMedia> media = thread.Posts[p].Media;
                    for (int m = 0; m < media.Count; m++)
                    {
                        SavedMedia item = media[m];
                        if (item.Bytes is { } bytes)
                        {
                            // Named by position, so re-saving an edited thread overwrites; leftovers are swept below.
                            item.StoredAs = SavedThreadPolicy.MediaFileName(p, m);
                            File.WriteAllBytes(Path.Combine(folder, item.StoredAs), bytes);
                        }

                        if (!string.IsNullOrEmpty(item.StoredAs))
                            keep.Add(item.StoredAs);
                    }
                }

                // Write the JSON last and atomically, so a crash leaves the previous version readable.
                string json = Path.Combine(folder, ThreadFile);
                string temp = json + ".tmp";
                File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(thread, SavedThreadJsonContext.Default.SavedThread));
                File.Move(temp, json, overwrite: true);

                foreach (string file in Directory.EnumerateFiles(folder))
                {
                    if (!keep.Contains(Path.GetFileName(file)))
                        File.Delete(file);
                }
            });
            return true;
        }
        catch (Exception ex)
        {
            LogService.Warn("SavedThreads", $"Saving thread '{thread.Id}' failed: {ex.Message}");
            return false;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Rewrites only the thread's JSON (its schedule, progress and errors), leaving the media files as
    /// they are. False when it couldn't be written, or the thread has been deleted meanwhile.
    /// </summary>
    public static async Task<bool> SaveDetailsAsync(SavedThread thread)
    {
        await Gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                string folder = FolderOf(thread.Id);
                if (!Directory.Exists(folder))
                    return false;

                string json = Path.Combine(folder, ThreadFile);
                string temp = json + ".tmp";
                File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(thread, SavedThreadJsonContext.Default.SavedThread));
                File.Move(temp, json, overwrite: true);
                return true;
            });
        }
        catch (Exception ex)
        {
            LogService.Warn("SavedThreads", $"Saving details of thread '{thread.Id}' failed: {ex.Message}");
            return false;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>One saved thread by id (media bytes not loaded), or null when there's no such thread.</summary>
    public static async Task<SavedThread?> FindAsync(string id) =>
        (await ListAsync()).FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    /// <summary>Reads each media file's bytes into <see cref="SavedMedia.Bytes"/>; a missing file drops that item. Returns false if any were missing.</summary>
    public static async Task<bool> LoadMediaAsync(SavedThread thread)
    {
        await Gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                bool complete = true;
                string folder = FolderOf(thread.Id);
                foreach (SavedPost post in thread.Posts)
                {
                    foreach (SavedMedia item in post.Media.ToList())
                    {
                        string file = Path.Combine(folder, item.StoredAs);
                        if (string.IsNullOrEmpty(item.StoredAs) || !File.Exists(file))
                        {
                            post.Media.Remove(item);
                            complete = false;
                            continue;
                        }

                        item.Bytes = File.ReadAllBytes(file);
                    }
                }

                return complete;
            });
        }
        catch (Exception ex)
        {
            LogService.Warn("SavedThreads", $"Loading media for '{thread.Id}' failed: {ex.Message}");
            return false;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<bool> DeleteAsync(string id)
    {
        await Gate.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                string folder = FolderOf(id);
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            });
            return true;
        }
        catch (Exception ex)
        {
            LogService.Warn("SavedThreads", $"Deleting thread '{id}' failed: {ex.Message}");
            return false;
        }
        finally
        {
            Gate.Release();
        }
    }
}
