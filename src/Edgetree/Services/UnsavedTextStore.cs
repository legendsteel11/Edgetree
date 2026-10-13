using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SidebarExplorer.App.Services;

// Edited text the panel has not saved yet, kept on disk (2026-10-11, the
// second step of the edit round) so that a crash, a Windows shutdown or a
// restart loses nothing. The panel's text view keeps the edits in memory as
// before; this is the copy that outlives the process.
//
// One pair of files per edited file, in %APPDATA%\Edgetree\unsaved: the text
// as UTF-8, and a small JSON with where it belongs and how to write it back
// (encoding, BOM, and the size and time the original had when it was read, so
// a save after a restart still notices a file changed meanwhile). A pair per
// entry rather than one index means no two writes ever touch the same file.
// The JSON is written AFTER the text, and an entry counts only once its JSON
// is there, so a write cut off halfway leaves either the previous entry or
// nothing - never a JSON pointing at a short text.
//
// Plain .NET, no AvalonEdit: MainWindow reads the list at startup to mark the
// tree rows, and nothing about that may load the editor.
//
// Writes and deletes run in order on one background chain, so a 10MB text is
// never written on the UI thread and a delete can never overtake the write it
// follows. Flush waits for the chain at exit.
public sealed class UnsavedTextStore
{
    public sealed record Entry(
        string Path,
        int CodePage,
        bool HasBom,
        long StampLength,
        DateTime StampLastWriteUtc,
        bool Markdown);

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _folder;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _chainLock = new();
    private Task _chain = Task.CompletedTask;

    public UnsavedTextStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgetree", "unsaved"))
    {
    }

    // Any folder: what a test harness points it at, so it never touches the
    // real one.
    public UnsavedTextStore(string folder)
    {
        _folder = folder;
    }

    // UI thread only, like everything here except the chain's bodies.
    public IReadOnlyCollection<string> Paths => _entries.Keys;

    public bool Contains(string path) => _entries.ContainsKey(path);

    // Startup: what the previous session left. An entry whose text is missing
    // (the write never finished) is cleared away rather than offered.
    public void Load()
    {
        _entries.Clear();
        if (!Directory.Exists(_folder))
        {
            return;
        }

        foreach (string json in Directory.EnumerateFiles(_folder, "*.json"))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(json));
                string text = Path.ChangeExtension(json, ".txt");
                if (entry is null || string.IsNullOrEmpty(entry.Path) || !File.Exists(text))
                {
                    TryDelete(json);
                    TryDelete(text);
                    continue;
                }
                _entries[entry.Path] = entry;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                ExitLog.Record($"unsaved text: could not read {Path.GetFileName(json)}: {ex.Message}");
            }
        }
    }

    // The text for an entry, read on the caller's thread - it is asked for
    // when the file is opened, and a local read of even 10MB is a few
    // milliseconds. Waits for the chain first, so it never reads a text that
    // a queued write is about to replace.
    public string? ReadText(string path, out Entry? entry)
    {
        entry = null;
        if (!_entries.TryGetValue(path, out var found))
        {
            return null;
        }

        WaitForChain(TimeSpan.FromSeconds(5));
        try
        {
            entry = found;
            return File.ReadAllText(TextPath(path), Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ExitLog.Record($"unsaved text: could not read the text for {path}: {ex.Message}");
            entry = null;
            return null;
        }
    }

    public void Write(Entry entry, string text)
    {
        _entries[entry.Path] = entry;
        string textPath = TextPath(entry.Path);
        string jsonPath = Path.ChangeExtension(textPath, ".json");
        Enqueue(() =>
        {
            Directory.CreateDirectory(_folder);
            WriteReplacing(textPath, text);
            WriteReplacing(jsonPath, JsonSerializer.Serialize(entry));
        });
    }

    public void Delete(string path)
    {
        if (!_entries.Remove(path))
        {
            return;
        }

        string textPath = TextPath(path);
        string jsonPath = Path.ChangeExtension(textPath, ".json");
        Enqueue(() =>
        {
            TryDelete(jsonPath);
            TryDelete(textPath);
        });
    }

    // A file the app itself renamed or moved: the entry follows it under its
    // new name. The pair's file names come from the path, so this is a write
    // of the new pair and a delete of the old one, in that order.
    public void Move(string oldPath, string newPath)
    {
        if (!_entries.TryGetValue(oldPath, out var entry))
        {
            return;
        }

        string oldText = TextPath(oldPath);
        string oldJson = Path.ChangeExtension(oldText, ".json");
        string newText = TextPath(newPath);
        string newJson = Path.ChangeExtension(newText, ".json");
        var moved = entry with { Path = newPath };
        _entries.Remove(oldPath);
        _entries[newPath] = moved;
        Enqueue(() =>
        {
            File.Copy(oldText, newText, overwrite: true);
            WriteReplacing(newJson, JsonSerializer.Serialize(moved));
            TryDelete(oldJson);
            TryDelete(oldText);
        });
    }

    // Where a path ends up when oldPath moves to newPath, or null if it is
    // not oldPath or beneath it - a folder move carries every path under it.
    // Here, in plain .NET, so the rename path can ask without loading the
    // editor.
    public static string? MovedPath(string path, string oldPath, string newPath)
    {
        if (string.Equals(path, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            return newPath;
        }

        string prefix = oldPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(newPath, path[prefix.Length..])
            : null;
    }

    // Exit: everything queued reaches the disk before the process goes.
    public void Flush() => WaitForChain(TimeSpan.FromSeconds(10));

    private void WaitForChain(TimeSpan limit)
    {
        Task chain;
        lock (_chainLock)
        {
            chain = _chain;
        }
        if (!chain.Wait(limit))
        {
            ExitLog.Record($"unsaved text: writes still running after {limit.TotalSeconds:F0}s");
        }
    }

    private void Enqueue(Action work)
    {
        lock (_chainLock)
        {
            _chain = _chain.ContinueWith(_ =>
            {
                try
                {
                    work();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    ExitLog.Record($"unsaved text: {ex.GetType().Name} {ex.Message}");
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    // Written beside the target and moved over it, so a reader - or the next
    // start after a crash - sees the old file or the new one, never half.
    private static void WriteReplacing(string path, string content)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, content, Utf8NoBom);
        File.Move(temp, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // A file name from the path: its hash, so any path - long, or holding
    // characters a file name cannot - maps to a short safe one, and the same
    // path always to the same one. Upper-cased first, as Windows paths are
    // compared without case.
    private string TextPath(string path)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()));
        return Path.Combine(_folder, Convert.ToHexString(hash, 0, 10) + ".txt");
    }
}
