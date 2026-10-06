using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SidebarExplorer.App.Services;

// Persists a scanned search index so reopening the app doesn't re-walk the
// whole scope. This exists for network shares specifically: a NAS scope was
// measured at ~1,700 files/sec no matter what the client does (running eight
// folder listings concurrently produced exactly no speedup - see
// FileSearchService.MaxParallelDirectories), which put a real 610k-file share
// at ~6 minutes per scan. Since the walk can't be made faster, the only
// way to remove the wait is to not walk at all.
//
// NOT re-scanned on every launch. Doing that silently would spend those 6
// minutes of network traffic on a share the user may not even search that
// session. The cache is used as it is and its age is shown (see
// Strings.SearchStatusCached). Since 2026-09-16 opening a search on the folder
// may refresh it behind the results - always for a local folder, on a network
// drive only once the index is a day old or a change was seen - which is the
// moment the traffic is asked for; see MainWindow.RefreshSearchIndexIfDue. The
// button still refreshes on demand. A stale hit is a failure mode people already know from
// Explorer - the file opens and Windows says it's gone. A stale MISS is the
// dangerous one (a file created since the last scan simply doesn't appear, with
// nothing to explain why), which is exactly why showing the age is not optional.
public static class SearchIndexCache
{
    // Bumped if the shape below ever changes; a file written by a different
    // version is ignored rather than misread.
    private const int FormatVersion = 1;

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Edgetree",
        "search-cache");

    // Stored grouped by folder - one folder path, then its file names and write
    // times as parallel arrays - rather than as a flat list of full paths. A
    // flat list repeats each folder path once per file it holds, which for the
    // 610k-file share above is tens of megabytes of nothing but repetition.
    // Grouping also rebuilds FileSearchService's memory optimization for free on
    // load: every file in a folder ends up referencing that folder's single
    // path string, the same sharing the scan itself produces.
    private sealed class CacheFile
    {
        public int Version { get; set; }
        public string Scope { get; set; } = string.Empty;
        public long SavedAtUtcTicks { get; set; }
        public List<CacheFolder> Folders { get; set; } = new();
    }

    private sealed class CacheFolder
    {
        public string Path { get; set; } = string.Empty;
        public List<string> Names { get; set; } = new();
        // Parallel to Names. UTC ticks, so restoring can't be thrown off by the
        // machine's time zone or a DST boundary between sessions.
        public List<long> Times { get; set; } = new();
        // Parallel to Names too, since 2026-10-06. Added without bumping
        // FormatVersion: a file written before it simply has none, and loads
        // with every size unknown (-1) rather than being thrown away - which on
        // a share would have cost a walk of minutes per saved scope, for a
        // tint on duplicate rows. The next walk fills them in. An older build
        // reading a newer file ignores the list it does not know.
        public List<long> Sizes { get; set; } = new();
    }

    // One file per scope, named by a hash of the scope path rather than the path
    // itself - a folder path contains characters a file name can't hold, and is
    // easily longer than a file name may be.
    private static string CacheFilePath(string scope)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeScope(scope)));
        return Path.Combine(CacheDir, Convert.ToHexString(hash, 0, 8) + ".json");
    }

    private static string NormalizeScope(string scope)
        => scope.TrimEnd('\\').ToLowerInvariant();

    public static void Save(string scope, IReadOnlyList<FileSearchService.SearchEntry> entries)
    {
        try
        {
            var folders = new Dictionary<string, CacheFolder>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (!folders.TryGetValue(entry.DirectoryPath, out var folder))
                {
                    folder = new CacheFolder { Path = entry.DirectoryPath };
                    folders[entry.DirectoryPath] = folder;
                }
                folder.Names.Add(entry.FileName);
                folder.Times.Add(entry.LastWriteTime.ToUniversalTime().Ticks);
                folder.Sizes.Add(entry.Length);
            }

            var payload = new CacheFile
            {
                Version = FormatVersion,
                Scope = scope,
                SavedAtUtcTicks = DateTime.UtcNow.Ticks,
                Folders = folders.Values.ToList()
            };

            Directory.CreateDirectory(CacheDir);
            // Written to a temporary file and moved into place, so a crash or a
            // full disk mid-write leaves the previous cache intact instead of a
            // truncated file that would fail to load on next launch.
            string finalPath = CacheFilePath(scope);
            string tempPath = finalPath + ".tmp";
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, payload);
            }
            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // A cache that can't be written just means the next launch scans -
            // never worth surfacing an error over.
        }
    }

    // Returns null when there's no usable cache for this scope, in which case
    // the caller scans as before.
    public static (List<FileSearchService.SearchEntry> Entries, DateTime SavedAtUtc)? TryLoad(string scope)
    {
        try
        {
            string path = CacheFilePath(scope);
            if (!File.Exists(path))
            {
                return null;
            }

            CacheFile? payload;
            using (var stream = File.OpenRead(path))
            {
                payload = JsonSerializer.Deserialize<CacheFile>(stream);
            }

            if (payload is null || payload.Version != FormatVersion)
            {
                return null;
            }

            var entries = new List<FileSearchService.SearchEntry>();
            foreach (var folder in payload.Folders)
            {
                // Names and Times are written in lockstep; a mismatch means a
                // corrupted file, so stop rather than index past the end.
                int count = Math.Min(folder.Names.Count, folder.Times.Count);
                // Sizes are trusted only when there is one per name: none at
                // all is a file from before they were kept (see CacheFolder).
                bool hasSizes = folder.Sizes.Count == folder.Names.Count;
                for (int i = 0; i < count; i++)
                {
                    entries.Add(new FileSearchService.SearchEntry(
                        folder.Path,
                        folder.Names[i],
                        new DateTime(folder.Times[i], DateTimeKind.Utc).ToLocalTime(),
                        hasSizes ? folder.Sizes[i] : -1));
                }
            }

            return (entries, new DateTime(payload.SavedAtUtcTicks, DateTimeKind.Utc));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
