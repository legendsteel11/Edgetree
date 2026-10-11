using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using SidebarExplorer.App.Models;

namespace SidebarExplorer.App.Services;

public enum FileSortField
{
    Name,
    Date,
    Type,
    Size
}

// A folder's own remembered sort override ("정렬 -> 전역 정렬 따르기" not chosen
// for it) - see FileSystemService.SortOverrides and Models.AppSettings.
public readonly record struct FolderSortOverride(FileSortField Field, bool Descending);

public static class FileSystemService
{
    // Set from MainWindow's "정렬" options submenu; read here on every
    // (re)load, unless the folder being loaded has its own entry in
    // SortOverrides below. Folders are always grouped before files (see
    // LoadChildren) regardless of this - only the order within each group
    // changes.
    public static FileSortField SortField = FileSortField.Name;
    public static bool SortDescending = false;

    // Per-folder sort overrides, keyed by the folder's own NORMALIZED FullPath
    // (not its children's) - set from AppSettings.FolderSortOverrides at
    // startup and whenever a folder's own right-click "정렬" is used, same
    // "static mirror of settings, read at load time" pattern as SortField/
    // SortDescending above and FileSystemItem.DisplayCap. OrdinalIgnoreCase
    // since Windows paths are case-insensitive.
    //
    // A PUBLISHED DICTIONARY IS NEVER MUTATED. Folder listings read this from
    // Task.Run workers (ReadChildrenFromDisk), and since the ancestor cascade
    // a menu click writes it at the very moment those reads are likeliest to
    // be in flight - a Dictionary written under a concurrent reader can throw
    // or spin. Writers below build a copy and swap the reference (atomic);
    // a reader that wants a consistent view over several lookups takes one
    // local copy of the reference first, the way ResolveSortFor does. Writes
    // are menu clicks, so the copy is paid almost never.
    public static Dictionary<string, FolderSortOverride> SortOverrides { get; private set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static void SetSortOverride(string path, FolderSortOverride over)
    {
        var next = new Dictionary<string, FolderSortOverride>(SortOverrides, StringComparer.OrdinalIgnoreCase)
        {
            [NormalizeSortOverridePath(path)] = over
        };
        SortOverrides = next;
    }

    public static void RemoveSortOverride(string path)
    {
        var next = new Dictionary<string, FolderSortOverride>(SortOverrides, StringComparer.OrdinalIgnoreCase);
        next.Remove(NormalizeSortOverridePath(path));
        SortOverrides = next;
    }

    public static void ClearSortOverrides()
        => SortOverrides = new Dictionary<string, FolderSortOverride>(StringComparer.OrdinalIgnoreCase);


    // Windows Explorer's own "smart" name sort (digit runs compared as
    // numbers, so "file2" sorts before "file10") - plain ordinal/ordinal-
    // ignore-case comparison sorts "file10" before "file2" character by
    // character, which reads as broken sorting for any real folder of
    // sequentially-numbered files or folders. shlwapi's StrCmpLogicalW is the
    // exact function Explorer itself uses for this, so results match what
    // users already expect from Windows.
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    private sealed class NaturalStringComparer : IComparer<string?>
    {
        public static readonly NaturalStringComparer Instance = new();
        public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? string.Empty, y ?? string.Empty);
    }

    // The same Explorer-style natural name comparison LoadChildren uses,
    // exposed so the file-search results can sort names identically (see
    // MainWindow's results-only sort).
    public static IComparer<string?> NaturalNameComparer => NaturalStringComparer.Instance;

    public static string NormalizeSortOverridePath(string path) => path.TrimEnd('\\');

    // 부모 폴더 따르기 (2026-08-23). A folder with no override of
    // its own asks UP THE CHAIN before falling back to the app-wide default -
    // set 이름↑ on a music folder once and every album inside follows, instead
    // of being set one by one (the report that started this). A folder's own
    // override still wins over anything above it, which is the whole priority
    // rule in one sentence.
    //
    // The walk is dictionary lookups against path segments - no disk is
    // touched, and a tree five deep asks five times only when nobody between
    // has an answer.
    public static (FileSortField Field, bool Descending) ResolveSortFor(string path)
    {
        // One read of the reference for the whole walk - see SortOverrides.
        var overrides = SortOverrides;
        string current = NormalizeSortOverridePath(path);
        while (current.Length > 0)
        {
            if (overrides.TryGetValue(current, out var over))
            {
                return (over.Field, over.Descending);
            }

            if (Path.GetDirectoryName(current) is not { Length: > 0 } parent)
            {
                break;
            }

            current = NormalizeSortOverridePath(parent);
        }

        return (SortField, SortDescending);
    }

    // The row's sort icon, as vector geometry rather than the four hand-drawn
    // PNGs it replaces (aliginIcon{Name,Date}{Asc,Desc}.png plus their "_L"
    // light-mode twins). Those encoded the FIELD as well as the direction,
    // which stopped scaling the moment 유형/크기 were added - four fields would
    // have meant four more drawings in two themes. The icon's job is now:
    //
    //   sort glyph      - this folder just follows the app-wide default
    //   arrow to top    - it has its own sort, ascending
    //   arrow to bottom - it has its own sort, descending
    //
    // The field itself is named in words by the menu the icon opens, which is
    // where a fourth (or fifth) sort order costs one row and no artwork. Being
    // a path rather than an image, it takes the row's own foreground brush and
    // so follows the light/dark theme with no second asset.
    //
    // Material Symbols "sort" / "vertical_align_top" / "vertical_align_bottom"
    // on their 960 grid (exported by the user at 16dp, wght500). WPF's path
    // mini-language matches SVG's own `d`, so these are the exported strings
    // verbatim - do not hand-edit the coordinates.
    private static Geometry ParseFrozen(string path)
    {
        var geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }

    public static readonly Geometry FollowsGlobalSortGeometry = ParseFrozen(
        "M135.87-247.74v-82.76h252.2v82.76h-252.2Zm0-190.76v-82.76h446.11v82.76H135.87Zm0-190.76v-82.76h688.26v82.76H135.87Z");

    private static readonly Geometry AscendingSortGeometry = ParseFrozen(
        "M183.87-741.13v-83h592.26v83H183.87ZM438.5-135.87v-375.41l-99.5 99.5-58.65-58.65L480-670.09l199.65 199.66L621-411.78l-99.5-99.5v375.41h-83Z");

    private static readonly Geometry DescendingSortGeometry = ParseFrozen(
        "M183.87-135.87v-83h592.26v83H183.87ZM480-289.91 280.35-489.57 339-548.22l99.5 99.5v-375.41h83v375.41l99.5-99.5 58.65 58.65L480-289.91Z");

    public static Geometry SortOverrideGeometry(bool descending)
        => descending ? DescendingSortGeometry : AscendingSortGeometry;

    // The ToolTip naming whichever sort is active on this folder - the icon
    // shows the direction but never the field, so this is where "이름"/"크기"
    // actually gets said without opening the menu.
    public static string FormatSortTooltip(FileSortField field, bool descending)
        => string.Format(Strings.SortTooltipFormat, DescribeSort(field, descending));

    private static string DescribeSort(FileSortField field, bool descending)
    {
        string name = field switch
        {
            FileSortField.Date => Strings.MenuSortByDate,
            FileSortField.Type => Strings.MenuSortByType,
            FileSortField.Size => Strings.MenuSortBySize,
            _ => Strings.MenuSortByName
        };
        return $"{name} · {(descending ? Strings.MenuSortDescending : Strings.MenuSortAscending)}";
    }

    // Sort field <-> settings string. Stored by name rather than the old
    // "SortByDate" boolean so a third and fourth field didn't need a second
    // flag; the boolean is still written alongside for older builds.
    public static FileSortField ParseSortField(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "date" => FileSortField.Date,
        "type" => FileSortField.Type,
        "size" => FileSortField.Size,
        _ => FileSortField.Name
    };

    public static string FormatSortFieldName(FileSortField field) => field switch
    {
        FileSortField.Date => "date",
        FileSortField.Type => "type",
        FileSortField.Size => "size",
        _ => "name"
    };

    // Same, for the neutral icon's own state. A property rather than a cached
    // string so it picks up the English strings after Strings.Initialize.
    public static string NoSortOverrideTooltip
        => string.Format(Strings.SortTooltipFormat, Strings.SortModeFollowGlobal);

    // Static mirror of AppSettings.BookmarkPaths, same pattern as
    // SortOverrides/SortField: FileSystemItem instances are created lazily
    // (and re-created by refreshes), so each constructor consults this set to
    // decide its own IsBookmarked instead of anyone having to walk the tree
    // re-applying flags. MainWindow keeps it in sync with the settings list.
    public static readonly HashSet<string> BookmarkedPaths = new(StringComparer.OrdinalIgnoreCase);

    // Folders taken out of the tree by the user ("이 폴더 숨기기"), mirrored
    // from AppSettings.HiddenFolderPaths for the same reason as the set above -
    // ReadChildrenFromDisk consults it as it builds each level, which is the one
    // place every folder listing passes through.
    //
    // Trailing separators are stripped on the way in (NormalizeHiddenPath) so a
    // path recorded as "D:\Work\" still matches the "D:\Work" the enumerator
    // hands back.
    public static readonly HashSet<string> HiddenPaths = new(StringComparer.OrdinalIgnoreCase);

    // Except while a deliberate navigation is passing THROUGH a hidden folder:
    // a search result, bookmark or favorite inside one still has to be
    // reachable, and a jump that silently went nowhere would be the worst of
    // both. So the chain being revealed is exempted for as long as the user is
    // in there, and the folder returns to hidden once they leave it - one rule
    // covering every jump route rather than a decision per caller
    // (agreed 2026-08-02).
    public static readonly HashSet<string> TemporarilyVisiblePaths = new(StringComparer.OrdinalIgnoreCase);

    public static string NormalizeHiddenPath(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static bool IsHiddenByUser(string path)
    {
        if (HiddenPaths.Count == 0)
        {
            return false;
        }

        string normalized = NormalizeHiddenPath(path);
        return HiddenPaths.Contains(normalized) && !TemporarilyVisiblePaths.Contains(normalized);
    }

    // What Ctrl+X is currently holding, for the same reason as the set above:
    // a row that gets re-created by a watcher merge or a refresh while the cut
    // is pending has to come back still marked. Unlike bookmarks this is not
    // persisted - it dies with the paste, with Esc, or with the next copy.
    public static readonly HashSet<string> CutPaths = new(StringComparer.OrdinalIgnoreCase);

    // Text files with edits the panel's text view has not saved yet
    // (2026-10-11), for the same reason again: a row rebuilt while the edit is
    // pending comes back marked. MainWindow keeps it equal to what the editor
    // holds (SyncViewerTextUnsavedMarks).
    public static readonly HashSet<string> UnsavedTextPaths = new(StringComparer.OrdinalIgnoreCase);

    // Mirror of AppSettings.NetworkLocations, kept in step by MainWindow the
    // same way the two sets above are. A LIST, not a set: these are rows in a
    // tree and the order they were added in is the order they appear.
    //
    // NOT to be confused with the NetworkRoots set further down - that one is
    // the backoff bookkeeping for roots that have stopped answering, and it
    // takes these as members too.
    public static readonly List<string> UserRoots = new();

    // A bare share - "\\NAS\보관", server and share and nothing more. That is
    // the one added location that is a place in its own right rather than a
    // folder inside one, which is why it alone is drawn and named like a drive.
    private static bool IsShareRoot(string path)
    {
        string trimmed = path.TrimEnd('\\', '/');
        return trimmed.StartsWith(@"\\", StringComparison.Ordinal)
            // Server and share and nothing after them. "\\NAS" on its own is
            // one segment and is not a place that can be opened.
            && trimmed[2..].Split('\\', '/', StringSplitOptions.RemoveEmptyEntries).Length == 2;
    }

    // A share reads as "보관 (\\NAS)", which is what Explorer calls it.
    //
    // EVERYTHING ELSE KEEPS ITS WHOLE PATH, and that is worth a sentence,
    // because the first version shortened those too. A folder added from a
    // mapped drive came out as "작업 (Y:)" - character for character the shape
    // the drive rows use for their volume label, so it sat among them reading
    // as another drive (seen 2026-08-13). The full path is a little longer and
    // says exactly what the row is.
    private static string UserRootDisplayName(string path)
    {
        string trimmed = path.TrimEnd('\\', '/');
        if (!IsShareRoot(trimmed))
        {
            return trimmed;
        }

        int lastSeparator = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        return $"{trimmed[(lastSeparator + 1)..]} ({trimmed[..lastSeparator]})";
    }

    public static List<FileSystemItem> GetDriveRoots()
    {
        var roots = new List<FileSystemItem>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (TryBuildDriveRoot(drive) is { } row)
            {
                roots.Add(row);
            }
        }

        // AFTER the letters, never among them: the drives are what Windows
        // says is on this machine and their order is its answer, while these
        // are the user's own additions in the order they added them.
        //
        // EXISTENCE IS NOT ASKED HERE. This runs on the UI thread from every
        // ReloadRoots, and a sleeping NAS answers Directory.Exists in tens of
        // seconds - which is the shape of the 21~59s freezes v1.3.2 had. The
        // row is built either way and behaves exactly like a mapped network
        // drive whose server is away: it stays, and clicking it tries again.
        foreach (string path in UserRoots)
        {
            if (IsHiddenByUser(path))
            {
                continue;
            }

            // A UNC path is remote by its own shape; a lettered one inherits
            // from the drive it sits on, which the loop above has just read -
            // so this costs no second look at the disk and nothing at all over
            // the network.
            bool onNetwork = path.StartsWith(@"\\", StringComparison.Ordinal)
                || roots.Any(r => r.DriveKind == DriveType.Network
                    && path.StartsWith(r.FullPath, StringComparison.OrdinalIgnoreCase));

            if (onNetwork)
            {
                // Same bookkeeping the mapped drives get: it is what lets a
                // later read tell "the network went away" from "this folder is
                // slow" without asking anything.
                lock (UnreachableRootsUntil)
                {
                    NetworkRoots.Add(path);
                }
            }

            roots.Add(new FileSystemItem(UserRootDisplayName(path), path, isDirectory: true)
            {
                IsOnNetworkDrive = onNetwork,
                // Only a bare share is a drive-like place. A FOLDER someone put
                // at the top of the tree is still a folder and keeps the folder
                // glyph - the rule FileSystemItem.DriveKind states for exactly
                // this case, which the first version of this loop broke by
                // stamping every added root as a network drive.
                DriveKind = IsShareRoot(path) ? DriveType.Network : null,
            });
        }

        return roots;
    }

    // ONE drive's row, built exactly the way the loop above builds them.
    // Extracted so an arriving letter can be asked about ON ITS OWN: a device
    // message can land at any moment, and re-reading every drive to answer it
    // would put IsReady and a volume label for the whole machine - a sleeping
    // NAS among them - in front of whatever asked. Returns null for the two
    // cases the loop used to walk past.
    // THE VOLUME LABEL IS THE ONLY SIGNAL a cloud provider mounted as a drive
    // letter gives off. Measured against Google Drive on 2026-08-26: DriveType
    // reads Fixed, the root carries no attribute of its own, its own registry
    // keys never name the letter, and it is absent from the sync-root list
    // Windows keeps (SyncRootManager) because it mounts through a driver of its
    // own rather than the Cloud Files API. Providers that mount as a FOLDER
    // (OneDrive and the rest) are in that list, so MainWindow answers those and
    // this only has to cover the lettered kind.
    //
    // The list is a limit, not a design. A renamed label or a provider nobody
    // has heard of loses the mark; neither puts one in the wrong place. Replace
    // this the day a better signal turns up.
    private static readonly string[] CloudDriveLabels =
    {
        "Google Drive",
        "Dropbox",
        "iCloud Drive",
        "MEGA",
        "pCloud",
        "Box",
        "Nextcloud",
    };

    public static bool IsCloudDriveLabel(string? label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return false;
        }

        foreach (string provider in CloudDriveLabels)
        {
            if (label.StartsWith(provider, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static FileSystemItem? TryBuildDriveRoot(DriveInfo drive)
    {
        // A hidden drive is dropped exactly like a hidden folder is - one
        // rule rather than a second presentation of its own. A greyed,
        // inert row that stayed in place was designed first and dropped by
        // the user (2026-08-02): "어차피 목록에 들어가는데" - the list is
        // the way back either way, so the special case bought nothing and
        // would have collided with the offline-drive look besides.
        if (IsHiddenByUser(drive.Name))
        {
            return null;
        }

        bool isNetwork = drive.DriveType == DriveType.Network;
        // A mapped network drive KEEPS its place while the server is away.
        // Dropping it (which "not ready" used to do) is how both NAS drives
        // vanished from the tree the moment the NAS rebooted, and a refresh
        // couldn't bring them back either - it re-asked the same question
        // and got the same "not ready" (reported 2026-07-26). Explorer
        // shows exactly these as a crossed-out drive that reconnects when
        // clicked, and the row has to exist for that click to be possible.
        // Local drives still drop out when not ready: an empty card reader
        // slot is not a place to go.
        if (!drive.IsReady && !isNetwork)
        {
            return null;
        }

        string driveName = drive.Name.TrimEnd('\\');
        // Not readable while the drive is away - the letter alone stands in
        // until it answers again and a later refresh picks the label up.
        string? label = drive.IsReady ? TryGetVolumeLabel(drive) : null;
        string displayName = string.IsNullOrWhiteSpace(label) ? driveName : $"{label} ({driveName})";

        if (isNetwork)
        {
            // Recorded here so a later read can tell, without asking the
            // drive anything, whether a timeout means "the network went
            // away" (back off) or "this folder is slow" (don't).
            lock (UnreachableRootsUntil)
            {
                NetworkRoots.Add(drive.RootDirectory.FullName);
            }
        }

        return new FileSystemItem(displayName, drive.RootDirectory.FullName, isDirectory: true)
        {
            IsOnNetworkDrive = isNetwork,
            // Decided HERE because this is the one moment the volume label is
            // already read - see IsCloudDriveLabel for why a label is all there
            // is to go on.
            IsOnCloudDrive = IsCloudDriveLabel(label),
            // Recorded, not asked for again later: this is the one moment the
            // drive's type is already in hand, and the row's icon reads it
            // (see FileSystemItem.Icon).
            DriveKind = drive.DriveType
        };
    }

    // The same for a letter that has only just appeared. Constructing DriveInfo
    // reads nothing on its own, so every cost of this call is inside the
    // overload above - which is why the arrival path runs it off the UI thread.
    public static FileSystemItem? TryBuildDriveRoot(string letterRoot)
    {
        try
        {
            return TryBuildDriveRoot(new DriveInfo(letterRoot));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // The letter went away again between the message and the look, or
            // the volume refuses to be asked. Either way there is no row to add.
            return null;
        }
    }

    // Hidden/System items are skipped so the tree matches Windows Explorer's
    // default (and the file search, which already skips them). RecurseSub-
    // directories stays false - LoadChildren only ever loads one level.
    //
    // THE SKIP IS A SETTING SINCE 2026-08-19 (issue #1). A cloud or virtual
    // drive can mark its own top-level folders System, and this line then hid
    // the whole of it: the drive appeared and had nothing in it. Explorer's
    // default is still the default here; the option is the way out for the
    // people whose drive needs it.
    //
    // Two prebuilt objects rather than one built per call - this runs twice for
    // every folder the tree opens.
    private static readonly EnumerationOptions SkipHiddenEntryOptions = new()
    {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true
    };

    private static readonly EnumerationOptions AllEntryOptions = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true
    };

    // Pushed in from the settings once at startup and on every toggle, the same
    // way SortField is - this class is static and has no settings of its own.
    //
    // THE SEARCH IS DELIBERATELY NOT WIRED TO THIS. Its own enumeration drops
    // Hidden/System too, and following this setting there would pull Windows'
    // whole system tree into an index of C: - a large, slow index for a setting
    // that exists to make one virtual drive readable.
    public static bool ShowHiddenItems { get; set; }

    private static EnumerationOptions VisibleEntryOptions
        => ShowHiddenItems ? AllEntryOptions : SkipHiddenEntryOptions;

    // readFailed distinguishes "this folder is genuinely empty" from "the
    // read itself blew up" (sleeping/disconnected NAS, permissions, drive
    // yanked). Callers MUST treat a failed read as unknown, never as empty:
    // recording it as empty is how a network drive's root lost its expander
    // arrow for the rest of the session (2026-07-23 NAS report) - and a
    // background merge doing the same would strip every loaded row beneath
    // the drive because it blinked once.
    // Roots that just failed or crawled, and the tick they may be tried again.
    // A network drive that goes away does NOT fail fast: every enumeration
    // against it sits in the SMB client's own timeout, seconds at a time, and
    // LoadChildren runs on the UI thread from every expand, watcher merge and
    // restore - so a NAS being switched off froze the whole window, once per
    // folder, for as long as the tree kept asking (reported 2026-07-26 with a
    // Synology restarting).
    //
    // The first read after it disappears still pays that wait - there is no
    // way to ask "are you there?" that isn't itself the same call - but the
    // answer is remembered, so every read behind it returns instantly as a
    // read FAILURE, which callers already handle by keeping what is on screen
    // (see MergeChildrenFromDisk). The mark expires on its own, so nothing
    // needs to notice the drive coming back.
    private static readonly Dictionary<string, long> UnreachableRootsUntil = new(StringComparer.OrdinalIgnoreCase);
    private const int UnreachableBackoffMs = 15_000;
    private const int SlowReadMs = 500;

    // Only network roots get marked: a slow or refused read on a local disk is
    // a folder-level problem (permissions, a spinning-up drive), not a reason
    // to declare C: gone.
    private static readonly HashSet<string> NetworkRoots = new(StringComparer.OrdinalIgnoreCase);

    // Public since 2026-08-13: the delete path asks it too. A network location
    // has no Recycle Bin, so a delete there is final whatever was requested -
    // which is the one case the app still puts a question in front of.
    public static bool IsOnNetworkRoot(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }
        string? root = Path.GetPathRoot(path);
        lock (UnreachableRootsUntil)
        {
            return root is not null && NetworkRoots.Contains(root);
        }
    }

    // Cached "this root is not answering", written by the background poll and
    // by any read that timed out. Never a syscall of its own.
    private static readonly Dictionary<string, bool> NetworkRootOffline = new(StringComparer.OrdinalIgnoreCase);

    // Answers from memory ONLY. Asking the drive here - which an earlier
    // version did, via DriveInfo.IsReady - is the very thing this is supposed
    // to prevent: with the mapping still open and the server rebooting, that
    // question blocks as long as the read would, so the guard meant to cap a
    // 20-second freeze took 20 seconds to decide (2026-07-26). Anything that
    // has to actually ask runs on a worker (RefreshNetworkRootState).
    private static bool IsRootUnreachable(string path)
    {
        string? root = Path.GetPathRoot(path);
        if (root is null)
        {
            return false;
        }

        lock (UnreachableRootsUntil)
        {
            if (NetworkRootOffline.TryGetValue(root, out bool offline) && offline)
            {
                return true;
            }
            return UnreachableRootsUntil.TryGetValue(root, out long until) && Environment.TickCount64 < until;
        }
    }

    // The cached answer, for callers that need it before the next poll has
    // pushed it onto the rows. Memory only - safe from the UI thread.
    public static bool IsNetworkPathUnreachable(string path) => IsRootUnreachable(path);

    // Background only - this is the one place that asks the drive anything.
    // Returns, and caches, whether the root is out of touch: either the
    // mapping is disconnected (answered instantly) or a read against it has
    // just timed out and the backoff still stands.
    public static bool RefreshNetworkRootState(string root)
    {
        bool ready = IsNetworkRootReady(root);
        lock (UnreachableRootsUntil)
        {
            bool backedOff = UnreachableRootsUntil.TryGetValue(root, out long until) && Environment.TickCount64 < until;
            bool offline = !ready || backedOff;
            NetworkRootOffline[root] = offline;
            return offline;
        }
    }

    // Cheap when the mapping is disconnected, and it can still block when the
    // server is up but wedged - which is exactly the case the backoff mark
    // above covers, so the two together handle both shapes of "not there".
    //
    // DRIVEINFO UNDERSTANDS DRIVE LETTERS AND NOTHING ELSE. Handed anything
    // else it throws ArgumentException ("Object must be a root directory..."),
    // which the catch below read as "not ready" - so every root added by hand
    // was drawn greyed-out and inert from the moment it appeared, however well
    // it was answering. Both shapes an added root can take hit this: a share
    // (\\server\share) and a folder on a mapped drive (Y:\작업). Found on the
    // day the feature landed, 2026-08-13, and settled by asking DriveInfo
    // directly rather than by reasoning about what it would do.
    //
    // Directory.Exists is the same question for those, at a known cost: unlike
    // a disconnected MAPPING, which answers instantly, this one can sit in the
    // SMB timeout. That is affordable here and nowhere else - this method is
    // the one place allowed to ask the network anything, it is only ever
    // reached from the background poll, and a read that overruns is what the
    // backoff exists to catch.
    private static bool IsNetworkRootReady(string root)
    {
        try
        {
            return IsDriveLetterRoot(root) ? new DriveInfo(root).IsReady : Directory.Exists(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    // "Y:" or "Y:\" - the whole of a lettered drive, which is the only thing
    // DriveInfo will answer for. "Y:\강의" is not one, and neither is any UNC
    // path however short.
    public static bool IsDriveLetterRoot(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        string trimmed = path.TrimEnd('\\', '/');
        return trimmed.Length == 2 && trimmed[1] == ':' && char.IsLetter(trimmed[0]);
    }

    // How many times in a row a root has run past the deadline without ever
    // answering. A read that eventually came back clears it.
    private static readonly Dictionary<string, int> RootTimeouts = new(StringComparer.OrdinalIgnoreCase);

    // ONE slow read is not a dead drive. It used to be: a single read past the
    // 1.5s deadline greyed the whole root out for 15s, which is the right
    // answer for a NAS that has gone away and the wrong one for a NAS that is
    // merely busy - and this app is now capable of making it busy on its own
    // (the filmstrip's thumbnails). The symptom was a mapped drive dropping out
    // of the tree while nothing was wrong with it but timing (2026-08-11).
    //
    // A HARD failure still condemns the root on the first try: an IOException
    // from the SMB stack is the drive answering that it is not there, which is
    // evidence a timeout never is.
    private static void MarkRootUnreachable(string path, bool hardFailure = true)
    {
        if (Path.GetPathRoot(path) is not { } root || !IsOnNetworkRoot(path))
        {
            return;
        }

        lock (UnreachableRootsUntil)
        {
            if (!hardFailure)
            {
                RootTimeouts.TryGetValue(root, out int strikes);
                RootTimeouts[root] = ++strikes;
                if (strikes < TimeoutsBeforeUnreachable)
                {
                    return;
                }
            }

            RootTimeouts.Remove(root);
            UnreachableRootsUntil[root] = Environment.TickCount64 + UnreachableBackoffMs;
            // So the badge can turn red on the next poll tick without waiting
            // for a syscall to confirm what the timeout already established.
            NetworkRootOffline[root] = true;
        }
    }

    private const int TimeoutsBeforeUnreachable = 3;

    // A read that came back normally is the clearest possible evidence the
    // root is back - better than waiting out the backoff or the next poll.
    private static void MarkRootReachable(string path)
    {
        if (Path.GetPathRoot(path) is not { } root || !IsOnNetworkRoot(path))
        {
            return;
        }
        lock (UnreachableRootsUntil)
        {
            UnreachableRootsUntil.Remove(root);
            // The run of near-misses is over too. Without this, three slow
            // reads spread across an afternoon would add up to a verdict the
            // same way three in a row do.
            RootTimeouts.Remove(root);
            NetworkRootOffline[root] = false;
        }
    }

    // Does this path exist, without hanging the caller on a network root that
    // has already proved it won't answer? Used by anything that has to test a
    // remembered path (bookmarks) rather than list a folder. The wait is
    // remembered exactly as LoadChildren's is, so one dead root costs one
    // timeout, not one per remembered path.
    public static bool ProbeExists(string path, out bool isDirectory)
    {
        isDirectory = false;
        if (IsRootUnreachable(path))
        {
            return false;
        }

        long startedTicks = Environment.TickCount64;
        try
        {
            if (Directory.Exists(path))
            {
                isDirectory = true;
                return true;
            }
            return File.Exists(path);
        }
        finally
        {
            long elapsed = Environment.TickCount64 - startedTicks;
            if (elapsed >= SlowReadMs)
            {
                MarkRootUnreachable(path, hardFailure: false);
                LogSlowRead(path, elapsed, failed: false, "probe");
            }
        }
    }

    // How long the UI thread is willing to wait on a network folder before it
    // gives up and calls the read failed. A NAS that is REBOOTING - as opposed
    // to disconnected - still holds the mapping open, so the cheap "is the
    // drive ready" question answers yes and the enumeration behind it sits in
    // the SMB timeout: 21 seconds, measured, long enough for Windows to put up
    // "Edgetree.exe is not responding" (2026-07-26, one click while a Synology
    // was restarting).
    //
    // 1.5s is chosen against the other measurement from the same session: a
    // genuinely slow-but-alive NAS folder came back in 1.4s. Below that and
    // healthy-if-sluggish reads would start being thrown away; above it and
    // the freeze becomes noticeable again.
    private const int NetworkReadTimeoutMs = 1_500;

    // Raised on a THREAD-POOL thread when a read that missed the deadline
    // finishes cleanly anyway, carrying the folder it was for and what it
    // found. The subscriber owns getting to the UI thread - this class has no
    // dispatcher and should not grow one.
    //
    // An event rather than a longer timeout on purpose. The 1.5s is the ceiling
    // on how long the WINDOW can freeze, and it was measured against a
    // rebooting NAS holding a read for 21 seconds; raising it to cover big
    // folders would give that freeze back. This way the ceiling stays where it
    // is and a slow-but-alive folder simply arrives late, which is what every
    // other slow thing in this app already does.
    public static event Action<string, List<FileSystemItem>>? LateChildrenArrived;

    public static List<FileSystemItem> LoadChildren(string path, FileSystemItem parent, out bool readFailed, string origin = "?")
    {
        // Known-unreachable: answer immediately rather than queue behind
        // another multi-second timeout. Reported as a failure, never as an
        // empty folder - the caller keeps its rows.
        if (IsRootUnreachable(path))
        {
            readFailed = true;
            return new List<FileSystemItem>();
        }

        // A network read is done on a worker with a deadline. The work itself
        // can't be cancelled - the file system call blocks until the SMB stack
        // is done with it - but the CALLER can stop waiting, which is the part
        // the user feels. The abandoned task finishes into nothing; the root is
        // marked, so the reads behind it return instantly instead of queueing
        // up more of the same.
        if (IsOnNetworkRoot(path))
        {
            var read = Task.Run(() => ReadChildrenFromDisk(path, parent, origin));
            if (!read.Wait(NetworkReadTimeoutMs))
            {
                MarkRootUnreachable(path, hardFailure: false);
                LogSlowRead(path, NetworkReadTimeoutMs, failed: true, origin);
                // The abandoned read still finishes. If it comes back clean,
                // that is better evidence than the deadline was: the root is
                // alive and merely slow, so the strike is taken back before it
                // can join two others into a verdict.
                //
                // AND THE LISTING IS HANDED OVER (2026-08-12). Taking the strike
                // back was all this used to do; the items it was holding were
                // dropped on the floor. A folder that answers just past the
                // deadline therefore came up blank EVERY time - the read is
                // deterministic, so re-expanding it simply missed by the same
                // margin again, and a NAS folder of 2400 files at 1.6s was
                // permanently an empty row with nothing said about it.
                _ = read.ContinueWith(finished =>
                {
                    if (finished.Status == TaskStatus.RanToCompletion && !finished.Result.ReadFailed)
                    {
                        MarkRootReachable(path);
                        LateChildrenArrived?.Invoke(path, finished.Result.Items);
                    }
                }, TaskScheduler.Default);
                readFailed = true;
                return new List<FileSystemItem>();
            }

            readFailed = read.Result.ReadFailed;
            if (readFailed)
            {
                MarkRootUnreachable(path);
            }
            else
            {
                MarkRootReachable(path);
            }
            return read.Result.Items;
        }

        var local = ReadChildrenFromDisk(path, parent, origin);
        readFailed = local.ReadFailed;
        return local.Items;
    }

    private static (List<FileSystemItem> Items, bool ReadFailed) ReadChildrenFromDisk(string path, FileSystemItem parent, string origin)
    {
        bool readFailed;
        var startedTicks = Environment.TickCount64;
        readFailed = false;
        var result = new List<FileSystemItem>();
        int filteredOut = 0;
        int hiddenFolders = 0;
        var (field, descending) = ResolveSortFor(path);

        try
        {
            var directories = SortPaths(Directory.EnumerateDirectories(path, "*", VisibleEntryOptions), isDirectory: true, field, descending);

            foreach (var dir in directories)
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (IsHiddenByUser(dir))
                {
                    // COUNTED WHERE IT IS DROPPED, exactly like the filtered
                    // files below. Until 2026-08-26 it was dropped silently, so
                    // a folder holding nothing but a hidden subfolder showed
                    // 비어 있음 - the folder presenting itself as empty to
                    // whoever was about to delete it, which is the one thing
                    // that notice exists to prevent.
                    hiddenFolders++;
                    continue;
                }

                result.Add(new FileSystemItem(name, dir, isDirectory: true, parent));
            }
        }
        catch (UnauthorizedAccessException) { readFailed = true; }
        catch (IOException) { readFailed = true; }

        try
        {
            var files = SortPaths(Directory.EnumerateFiles(path, "*", VisibleEntryOptions), isDirectory: false, field, descending);

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                // The file-kind filter applies HERE and only here: folders are
                // listed above untouched (they are the way through the tree),
                // and the search scan has its own enumeration, so it keeps
                // finding everything - the same rule hidden folders follow.
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (FileTypeFilter.ShouldShowFile(name))
                {
                    result.Add(new FileSystemItem(name, file, isDirectory: false, parent));
                }
                else
                {
                    // COUNTED WHERE IT IS DROPPED, because this is the only pass
                    // that knows. A folder left with nothing to show reads as an
                    // empty folder, and an empty folder is one people delete -
                    // so the folder gets to say how much the filter took out of
                    // it (see FileSystemItem.PopulateCapped).
                    filteredOut++;
                }
            }
        }
        catch (UnauthorizedAccessException) { readFailed = true; }
        catch (IOException) { readFailed = true; }

        parent.FilteredOutCount = filteredOut;
        parent.HiddenFolderCount = hiddenFolders;

        // A failure on a network path, or a read that took long enough to be
        // felt as a freeze, closes the door on that root for a few seconds.
        long elapsed = Environment.TickCount64 - startedTicks;
        if (readFailed || elapsed >= SlowReadMs)
        {
            if (IsOnNetworkRoot(path))
            {
                MarkRootUnreachable(path, hardFailure: readFailed);
            }
            LogSlowRead(path, elapsed, readFailed, origin);
        }

        return (result, readFailed);
    }

    // Debug builds only: which folder read blocked, for how long, and whether
    // it failed outright. "The app froze" is otherwise a report with nowhere
    // to start - this names the path and the cost.
    // ORIGIN was added 2026-08-12 for a report the old line could not answer:
    // the same NAS folder read every two seconds, each one blocking the UI for
    // 1.5s. The path and the cost were both there; WHO KEPT ASKING was not, and
    // "the first load retrying", "the watcher merging" and "the two feeding each
    // other" all look identical without it.
    [System.Diagnostics.Conditional("DEBUG")]
    private static void LogSlowRead(string path, long elapsedMs, bool failed, string origin)
    {
        if (elapsedMs < SlowReadMs && !failed)
        {
            return;
        }

        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgetree");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "slowread.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {elapsedMs,6}ms {(failed ? "FAILED " : "       ")}{origin,-8}{path}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Explorer's own semantics, since that's the ordering people already have
    // in their hands: folders and files are sorted as separate blocks (the
    // caller lists folders first either way), and a folder has neither a size
    // nor a type of its own - Explorer shows them all as one kind - so those
    // two orders fall back to the name order for folders while the direction
    // still applies to the whole listing.
    private static IEnumerable<string> SortPaths(IEnumerable<string> paths, bool isDirectory, FileSortField field, bool descending)
    {
        var effective = isDirectory && field is FileSortField.Size or FileSortField.Type
            ? FileSortField.Name
            : field;

        IOrderedEnumerable<string> ordered = effective switch
        {
            FileSortField.Date => descending
                ? paths.OrderByDescending(p => GetLastWriteTime(p, isDirectory))
                : paths.OrderBy(p => GetLastWriteTime(p, isDirectory)),
            FileSortField.Size => descending
                ? paths.OrderByDescending(GetFileLength)
                : paths.OrderBy(GetFileLength),
            FileSortField.Type => descending
                ? paths.OrderByDescending(GetTypeKey, NaturalStringComparer.Instance)
                : paths.OrderBy(GetTypeKey, NaturalStringComparer.Instance),
            _ => descending
                ? paths.OrderByDescending(Path.GetFileName, NaturalStringComparer.Instance)
                : paths.OrderBy(Path.GetFileName, NaturalStringComparer.Instance)
        };

        // Equal sizes, types or timestamps fall back to the name order rather
        // than whatever order the file system happened to hand back, so a
        // folder full of same-type files doesn't reshuffle between reads.
        return effective == FileSortField.Name
            ? ordered
            : ordered.ThenBy(Path.GetFileName, NaturalStringComparer.Instance);
    }

    private static long GetFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    // The extension, lower-cased and without its dot. Explorer sorts by the
    // shell's registered type NAME ("PNG 파일"), which needs a per-file shell
    // lookup; the extension groups the same files together in the same order
    // for a fraction of the cost, and files with no extension come first.
    private static string GetTypeKey(string path)
        => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    private static DateTime GetLastWriteTime(string path, bool isDirectory)
        => isDirectory ? Directory.GetLastWriteTime(path) : File.GetLastWriteTime(path);

    private static string? TryGetVolumeLabel(DriveInfo drive)
    {
        try
        {
            return drive.VolumeLabel;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
