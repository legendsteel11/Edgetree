using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;

namespace SidebarExplorer.App.Services;

// In-memory file index over a chosen folder. ScanAsync walks it into a flat
// list the UI holds; filtering that list on every keystroke is what the user
// experiences as "the index". The list is kept on disk per folder
// (SearchIndexCache, since 2026-07-19) and walked again behind the results
// when a search opens on it and it is due (MainWindow.RefreshSearchIndexIfDue).
// A whole-PC scope existed once and was taken out; the roots parameter is a
// list only because it was.
public static class FileSearchService
{
    // One matched file. Stores the folder path (shared, see ScanAsync's dedupe)
    // + name + write time; FullPath is composed on demand rather than stored, so
    // a whole-PC index of ~900k files doesn't also carry a full path string per
    // file on top of the folder path it already shares with its siblings.
    // LastWriteTime comes from the FileInfo the enumeration already yields (no
    // extra stat call) and drives the results date sort.
    public sealed record SearchEntry(string DirectoryPath, string FileName, DateTime LastWriteTime)
    {
        public string FullPath => Path.Combine(DirectoryPath, FileName);
    }

    // Reported to the caller in chunks rather than one entry at a time so a
    // big walk streams into the UI in a handful of marshaled hops instead of
    // tens of thousands - see MainWindow's IProgress consumer, which appends
    // each batch on the UI thread (so the list it owns is never touched from
    // the scan thread).
    private const int BatchSize = 1024;

    // How a walk ended, for a caller deciding whether its listing can stand in
    // for the folder (2026-09-28). Every failure inside the walk used to be
    // swallowed folder by folder, so a share that dropped half way through
    // ended "successfully" with half a listing - and a refresh then put that
    // half in place of a good index and saved it over the good cache.
    public enum ScanResult
    {
        // Every folder that could be read was read. Folders this walk can
        // never read - access denied, a path too long, one deleted while the
        // walk was on its way - are left out, as they always were.
        Complete,

        // No root answered, so nothing was walked.
        RootMissing,

        // The storage stopped answering part way through; the batches already
        // reported are all there is. See StorageStoppedAnswering.
        Interrupted,
    }

    // Win32 errors that say the STORAGE went away, not that one folder is
    // unreadable: a share or its server that stopped answering, a network that
    // went down, a drive that was pulled. One of these ends the walk as
    // Interrupted at once - every folder after it would only wait out the same
    // timeout.
    private static readonly HashSet<int> StorageGoneErrors = new()
    {
        21,   // ERROR_NOT_READY
        31,   // ERROR_GEN_FAILURE
        51,   // ERROR_REM_NOT_LIST
        53,   // ERROR_BAD_NETPATH
        54,   // ERROR_NETWORK_BUSY
        55,   // ERROR_DEV_NOT_EXIST
        58,   // ERROR_BAD_NET_RESP
        59,   // ERROR_UNEXP_NET_ERR
        64,   // ERROR_NETNAME_DELETED
        67,   // ERROR_BAD_NET_NAME
        121,  // ERROR_SEM_TIMEOUT
        1117, // ERROR_IO_DEVICE
        1167, // ERROR_DEVICE_NOT_CONNECTED
        1222, // ERROR_NO_NET_OR_BAD_PATH
        1231, // ERROR_NETWORK_UNREACHABLE
        1232, // ERROR_HOST_UNREACHABLE
        1236, // ERROR_CONNECTION_ABORTED
        2250, // ERROR_NOT_CONNECTED
    };

    // How many folders are enumerated at once (see ScanAsync's level-by-level
    // walk). A network share's scan time is dominated by per-folder round-trip
    // latency rather than bandwidth or file count - measured on a NAS at ~6ms
    // per folder, which is round-trip territory, not throughput - so issuing
    // several listings concurrently is close to a straight division of the
    // wall-clock time. On a local disk the latency being overlapped is tiny, so
    // this neither helps nor hurts much there. Kept modest deliberately: the
    // gain flattens out once enough requests are in flight to cover the
    // latency, and a large worker count on a spinning disk would just add seek
    // contention.
    private const int MaxParallelDirectories = 8;

    // Enumerate every file under each root, reporting them to `onBatch` in
    // BatchSize chunks. IgnoreInaccessible walks past an access-denied folder
    // (e.g. "System Volume Information") instead of throwing; AttributesToSkip
    // drops Hidden/System (matching Explorer) and ReparsePoint so a junction/
    // symlink can't loop the walk back on itself or wander onto another volume.
    // Honors ct so a scope change / re-scan cancels a walk already in flight.
    //
    // The recursion is done here rather than by RecurseSubdirectories, so that
    // folders can be enumerated MaxParallelDirectories at a time - see that
    // constant for why concurrency is what matters on a network share. The walk
    // goes level by level (enumerate every folder at this depth, collect their
    // subfolders, repeat) rather than handing a shared work queue to a pool of
    // workers: the termination condition is then simply "this level produced no
    // subfolders", with no need to track whether an idle worker might still be
    // handed more work by a busy one. A level that happens to hold one enormous
    // folder serializes on it, which is the accepted cost of that simplicity.
    //
    // Says how it ended (ScanResult) rather than only that it ended - see there.
    public static Task<ScanResult> ScanAsync(
        IReadOnlyList<string> roots,
        IProgress<IReadOnlyList<SearchEntry>> onBatch,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint
            };

            var currentLevel = new List<DirectoryInfo>();
            foreach (var root in roots)
            {
                try
                {
                    var rootInfo = new DirectoryInfo(root);
                    if (rootInfo.Exists)
                    {
                        currentLevel.Add(rootInfo);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
                {
                }
            }

            // ASKED HERE, ON THE WALK'S OWN THREAD (2026-09-28). The caller used
            // to ask first, on the UI thread - and once opening a search could
            // start a refresh, a share that had stopped answering held the
            // window for as long as the question took.
            if (currentLevel.Count == 0)
            {
                return ScanResult.RootMissing;
            }

            // Written by several workers at once; any of them may be the one
            // that finds the storage gone.
            int interrupted = 0;

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxParallelDirectories,
                CancellationToken = ct
            };

            while (currentLevel.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var nextLevel = new ConcurrentBag<DirectoryInfo>();

                // Each worker accumulates across the folders it handles and
                // flushes at BatchSize, rather than reporting once per folder -
                // otherwise a tree of tens of thousands of small folders would
                // marshal a tiny batch to the UI thread for every one of them.
                // Progress<T> posts to the captured UI context, so reporting
                // from several threads at once is safe.
                Parallel.ForEach(
                    currentLevel,
                    parallelOptions,
                    () => new List<SearchEntry>(BatchSize),
                    (dir, loop, batch) =>
                    {
                        // Materialized once per folder and handed to every entry
                        // in it, so the files of one folder share a single path
                        // string. The old walk needed a dictionary to deduplicate
                        // FileInfo.DirectoryName for this; enumerating a folder
                        // explicitly means we already hold the string.
                        string dirPath = dir.FullName;

                        try
                        {
                            foreach (var file in dir.EnumerateFiles("*", options))
                            {
                                ct.ThrowIfCancellationRequested();

                                batch.Add(new SearchEntry(dirPath, file.Name, file.LastWriteTime));
                                if (batch.Count >= BatchSize)
                                {
                                    onBatch.Report(batch);
                                    batch = new List<SearchEntry>(BatchSize);
                                }
                            }
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                        {
                            if (StorageStoppedAnswering(e, roots))
                            {
                                Interlocked.Exchange(ref interrupted, 1);
                                loop.Stop();
                                return batch;
                            }
                        }

                        try
                        {
                            foreach (var sub in dir.EnumerateDirectories("*", options))
                            {
                                nextLevel.Add(sub);
                            }
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                        {
                            if (StorageStoppedAnswering(e, roots))
                            {
                                Interlocked.Exchange(ref interrupted, 1);
                                loop.Stop();
                            }
                        }

                        return batch;
                    },
                    batch =>
                    {
                        if (batch.Count > 0)
                        {
                            onBatch.Report(batch);
                        }
                    });

                if (Volatile.Read(ref interrupted) != 0)
                {
                    return ScanResult.Interrupted;
                }

                currentLevel = nextLevel.ToList();
            }

            return ScanResult.Complete;
        }, ct);
    }

    // Whether a folder that failed to list means the storage stopped answering,
    // rather than that this one folder cannot be read. That difference is the
    // whole of what a refresh needs to know: a folder the walk can never read is
    // left out, as it always was, while a share that dropped leaves out
    // everything after the moment it dropped. The error is asked first. A share
    // that drops can also surface as a plain "path not found" - the error a
    // folder deleted mid-walk gives too - so that, and anything else not
    // recognised, is settled by whether a root still answers.
    private static bool StorageStoppedAnswering(Exception e, IReadOnlyList<string> roots)
    {
        if (e is UnauthorizedAccessException or PathTooLongException)
        {
            return false;
        }

        if ((e.HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000) &&
            StorageGoneErrors.Contains(e.HResult & 0xFFFF))
        {
            return true;
        }

        return !roots.Any(Directory.Exists);
    }

    // Builds the per-query predicate matched against a filename. A query
    // containing '*' or '?' is treated as an anchored wildcard pattern (shell
    // semantics: "*.txt" ends with .txt, "report*" starts with report);
    // anything else is a plain case-insensitive substring match (부분일치).
    // Empty query matches nothing - the results list is blank until the user
    // actually types something. No regex mode by design (this searches names,
    // not contents).
    public static Func<string, bool> BuildMatcher(string query)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return static _ => false;
        }

        if (query.Contains('*') || query.Contains('?'))
        {
            // Escape everything, then re-open just the two wildcard chars back
            // into their regex equivalents, anchored so the pattern matches the
            // whole name rather than any substring of it.
            string pattern = "^" + Regex.Escape(query)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return name => regex.IsMatch(name);
        }

        return name => name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
