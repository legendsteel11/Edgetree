using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using SidebarExplorer.App.Services;

namespace SidebarExplorer.App.Models;

public class FileSystemItem : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isMultiSelected;
    private bool _isDropTarget;
    private bool _isAncestorOfSelection;
    private bool _childrenLoaded;
    private bool _isEditing;
    private string _editingName = string.Empty;

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public bool IsPlaceholder { get; }
    public FileSystemItem? Parent { get; }
    // Mode-aware (PNG set vs. Windows shell icons - see ShellIconService).
    // RefreshIcon is handed in as the change callback: a per-file shell icon
    // (.exe 등) arriving from the background swaps in by re-raising this
    // property, VS the instant generic icon this getter returned meanwhile.
    public ImageSource? Icon => IsShowMore || IsPlaceholder
        ? null
        : IsDirectory
            ? DriveKind is { } drive
                ? ShellIconService.GetDriveIcon(drive, Name, IsExpanded)
                : ShellIconService.GetFolderIcon(Name, IsExpanded)
            : ShellIconService.GetFileIcon(Name, FullPath, RefreshIcon);

    // Set on drive roots by FileSystemService.GetDriveRoots and null everywhere
    // else, which is what makes it the test above: a root the user chose for
    // themselves (a folder pinned as the tree's top) is still a folder and keeps
    // the folder glyph. Not inherited downward the way IsOnNetworkDrive is -
    // this one is about the row being the drive itself.
    public DriveType? DriveKind { get; set; }

    // Also called by MainWindow.ApplyIconStyle when the icon mode toggles, so
    // every realized row re-reads Icon under the new mode without a reload.
    public void RefreshIcon() => OnPropertyChanged(nameof(Icon));

    // Only the first DisplayCap children are ever placed in Children at once;
    // the rest wait in _overflow behind a single "더 보기" row. Keeping the
    // virtualizing TreeView from ever holding thousands of realized rows is
    // what makes favorites reveal/navigation land in a single click no matter
    // how many files a folder has (see MainWindow.NavigateToPath), and it
    // keeps expanding a huge folder by hand from lagging the whole tree.
    // Mutable (not const) and user-configurable (옵션 메뉴, 1~50) - set from
    // AppSettings.MaxItemsPerFolder at startup and whenever changed, same
    // pattern as FileSystemService.SortField/SortDescending.
    public static int DisplayCap = 50;
    private readonly List<FileSystemItem> _overflow = new();
    private bool _showingAll;

    // The synthetic "… 더 보기 (N)" row (see CreateShowMore): rendered in place
    // of icon+name by the tree DataTemplate, and clicking it reveals the rest.
    public bool IsShowMore { get; }
    public int RemainingCount { get; }

    // THE SAME ROW AFTER THE REVEAL, pointing the other way. A folder showing
    // everything has no "더 보기" row - that slot is simply empty - and until
    // 2026-08-12 nothing put the folder back either: the reveal outlived every
    // collapse and only a favorites jump or a restart undid it. The two states
    // cannot both apply, so the slot carries whichever one is available.
    //
    // IsShowMore stays TRUE on this row so that every "not a real file" filter
    // in the tree - navigation, recap, expanded-descendant, refresh matching -
    // keeps excluding it without being taught about a second synthetic kind.
    // What differs is the label and what a click does.
    public bool IsShowLess { get; }

    // THE THIRD USE OF THE SAME SLOT, and the only one that is not a button.
    // A folder whose files are all taken out by the file-type filter shows no
    // rows at all, and a folder with no rows reads as an empty folder - which
    // is a folder people delete, taking with it what they could not see. This
    // row stands in that empty folder and says how much is really there.
    //
    // IsShowMore rides along for the same reason IsShowLess does: every "not a
    // real file" filter in the tree keeps excluding it without being taught a
    // new kind. What differs is the label, and that a click does NOTHING - it
    // is a note, not a way in (see TreeViewItem_PreviewMouseLeftButtonDown).
    public bool IsFilterNotice { get; private init; }

    // A NOTICE WITH A COUNT OF ZERO IS THE EMPTY FOLDER, which is why the two
    // notes share one flag rather than getting one each: they stand in the same
    // slot, refuse a click the same way, and differ only in whether anything is
    // being hidden.
    //
    // The notice reads its two counts off the PARENT rather than carrying them,
    // because it already holds a reference to the folder it stands in and a
    // second copy of a number is a second thing that can go stale.
    public string ShowMoreLabel
    {
        get
        {
            if (!IsFilterNotice)
            {
                return string.Format(
                    IsShowLess ? Strings.ShowLessFormat : Strings.ShowMoreFormat,
                    RemainingCount);
            }

            int filtered = Parent?.FilteredOutCount ?? 0;
            int hidden = Parent?.HiddenFolderCount ?? 0;

            // NAMING BOTH WHEN BOTH APPLY. One number would be honest about the
            // total and useless about the remedy - the filter is released at the
            // chips, a hidden folder from the options menu.
            if (filtered > 0 && hidden > 0)
            {
                return string.Format(Strings.FilterAndHiddenFormat, filtered, hidden);
            }

            if (hidden > 0)
            {
                return string.Format(Strings.HiddenFolderNoticeFormat, hidden);
            }

            if (filtered > 0)
            {
                return string.Format(Strings.FilterHiddenFormat, filtered);
            }

            return Strings.FolderEmptyLabel;
        }
    }

    // How many files the file-type filter took out of this FOLDER's own
    // listing, recorded where the filter is applied (FileSystemService's read)
    // so nothing has to enumerate a second time to find out.
    public int FilteredOutCount { get; set; }

    // And how many SUBFOLDERS the 숨긴 폴더 list took out of it. Counted for the
    // same reason and in the same pass: a folder whose only child was hidden was
    // announcing itself as 비어 있음 while that child held files (2026-08-26, on
    // report), which is the exact case the notice exists to prevent.
    public int HiddenFolderCount { get; set; }

    // WHAT A ROW'S TOOLTIP SAYS (2026-08-17). Its own path for a real row; for
    // the synthetic 더 보기 · 접기 row, the PARENT's - that row has no path of its
    // own, and the question someone hovers it with is which folder this list
    // belongs to. Two rows of the same label can sit two lines apart with
    // different indents (one folder's overflow under another's), and nested
    // folders of the SAME NAME made that unreadable; the answer is the path.
    //
    // Decided here rather than with a second tooltip style so the delay and the
    // duration stay written once - see RowPathTooltip in MainWindow.xaml, which
    // both rows now share.
    public string TooltipPath => IsShowMore ? Parent?.FullPath ?? string.Empty : FullPath;

    // The tooltip's second line (2026-10-06): size and last-modified time for a
    // file, the time alone for a folder. Null leaves the line out, which is
    // what the 더 보기 row and a row whose read failed show.
    //
    // READ WHEN THE TOOLTIP OPENS, not kept from the listing: the tree lists
    // names only (a date sort stats each entry and throws the answer away),
    // and even a date taken at listing time would go stale on the first save
    // in place - the drive watcher leaves LastWrite out on purpose (see
    // StartDriveWatchers), so nothing would ever correct it, and a wrong "last
    // modified" is worse than none. MainWindow.TreeRowTooltip_Opening fills
    // this; the previous answer stays up while the next read is out, so a
    // second hover on a slow share does not open one line short.
    private string? _tooltipDetail;
    public string? TooltipDetail
    {
        get => _tooltipDetail;
        set => SetField(ref _tooltipDetail, value);
    }

    // Drive roots (FileSystemService.GetDriveRoots) are the only items ever
    // constructed with no parent - used to bold their row (C:, D:, ...).
    public bool IsRoot => Parent is null;

    // Whether "더 보기" has been clicked and every overflow row is currently
    // appended to Children - lets a caller that's about to rebuild this
    // folder's Children from scratch (RefreshChildren via a sort-override
    // change or a background disk change) know it needs to re-reveal the rest
    // afterward too, not just re-set IsExpanded (see MainWindow's
    // CollectExpandedPaths/RefreshFolderPreservingState).
    public bool IsShowingAllChildren => _showingAll;

    // Bulk-capable: the three methods that rewrite this wholesale (the initial
    // capped fill, "더 보기", and the re-cap) do it in one notification rather
    // than one per row - see BulkObservableCollection for what that was
    // costing. Everything else still adds and removes normally.
    public BulkObservableCollection<FileSystemItem> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            // A FILE CANNOT BE EXPANDED, whoever asks (2026-08-31, reported: a
            // bookmarked .exe wearing a chevron over a 비어 있음 row). Refused
            // HERE rather than at the callers because the callers are why it
            // happened: the reveal walk expands its whole chain on the note
            // that favorites are always directories, and RestoreTreeState
            // replays whatever CollectAllExpandedPaths saved - so once the
            // walk marked a file, the state PERSISTED and every launch put it
            // back. EnsureChildrenLoaded has refused files all along (its own
            // IsDirectory guard); this setter was the half that never learned.
            // With the refusal here, the stale saved entry washes out on the
            // next save - the file is no longer expanded, so it is no longer
            // collected.
            if (value && !IsDirectory)
            {
                return;
            }

            bool changed = _isExpanded != value;

            // RECORDED BEFORE THE NOTIFICATION, and that ordering is the whole
            // point (measured 2026-08-17): SetField raises PropertyChanged, the
            // TwoWay binding pushes the value into the row's container, and the
            // container raises Expanded - all inside that one call. So the
            // accordion's reader runs before this setter's own tail. Written at
            // the tail it was one step late every single time, and every real
            // expansion was refused as a phantom (autocollapse.log 16:36:14 had
            // the gate line one millisecond BEFORE the write it was asking
            // about).
            if (changed && value)
            {
                if (_lastModelExpand is null)
                {
                    _lastModelExpand = new WeakReference<FileSystemItem>(this);
                }
                else
                {
                    _lastModelExpand.SetTarget(this);
                }

                LastModelExpandTicks = Environment.TickCount64;
            }

            if (SetField(ref _isExpanded, value) && IsDirectory)
            {
                OnPropertyChanged(nameof(Icon));
            }

            if (changed)
            {
                LogExpandedWrite(value);
            }
        }
    }

    // THE LAST FOLDER WHOSE MODEL ACTUALLY WENT FROM CLOSED TO OPEN, and when
    // (2026-08-17). Not an instrument - MainWindow's accordion reads it to tell
    // a real expansion from the Expanded event WPF raises when a container is
    // built (or rebound) for a row that was already expanded. That event carries
    // no model transition, because there is nothing to change; see
    // TreeViewItem_Expanded for the incident that made this necessary.
    //
    // Static, and only ever the LAST one: the reader wants "did this item just
    // change", asked microseconds later in the same call stack, so one slot is
    // the whole requirement. A per-item flag would go stale instead - a folder
    // expanded off-screen at startup would still be carrying it minutes later,
    // which is exactly the case being defended against.
    //
    // WEAK, and the reason is the file it is in (2026-08-17). Rows here are
    // thrown away wholesale - ReplaceAll and every refresh discard the old items -
    // and a static holding one of them by reference holds its Children subtree,
    // its Parent chain and every row's Icon with it. Those pixels live outside
    // the GC heap, so one retained subtree is real memory rather than an
    // accounting curiosity. Superseded by the next expansion either way, so it
    // was bounded - but "bounded" is not the same as "collected", and this slot
    // has no business being the reason anything survives.
    //
    // Nothing is lost by weakening it: the only question ever asked is identity,
    // and an item the collector has taken cannot be the one the accordion is
    // holding. One WeakReference is allocated for the life of the process and
    // re-pointed with SetTarget, rather than one per expansion.
    private static WeakReference<FileSystemItem>? _lastModelExpand;

    // Identity only, which is the whole of what the reader wants - see
    // MainWindow.TreeViewItem_Expanded. A method rather than the reference
    // itself so the weak slot stays in here: a caller handed the target back
    // would be holding it strongly again, which is the thing being avoided.
    internal static bool IsLastModelExpand(FileSystemItem item)
        => _lastModelExpand is { } slot
           && slot.TryGetTarget(out var last)
           && ReferenceEquals(last, item);

    internal static long LastModelExpandTicks { get; private set; }

    // Debug instrument (2026-08-14): folders collapse with no hand on the
    // tree - during a window resize with a slideshow running, the whole
    // expanded chain went false top-down and the selection fell to the drive
    // root. autocollapse.log's existing lines cover only the accordion path
    // (TreeViewItem_Expanded), and that path logged nothing at those moments,
    // so the writer is something else - suspected: virtualization container
    // work pushing values through the TwoWay IsExpanded binding. This names
    // the writer: every model-side transition lands in the same log with the
    // caller's frames, so accordion (CollapseExcept), restore, and binding
    // write-backs (WPF frames) read differently at a glance.
    [System.Diagnostics.Conditional("DEBUG")]
    private void LogExpandedWrite(bool value)
    {
        try
        {
            var trace = new System.Diagnostics.StackTrace(2, false);
            var sb = new System.Text.StringBuilder();
            int count = Math.Min(8, trace.FrameCount);
            for (int i = 0; i < count; i++)
            {
                var method = trace.GetFrame(i)?.GetMethod();
                if (i > 0)
                {
                    sb.Append(" < ");
                }

                sb.Append(method?.DeclaringType?.Name).Append('.').Append(method?.Name);
            }

            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgetree");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(dir, "autocollapse.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {(value ? "EXPAND  " : "COLLAPSE")} {FullPath}  via {sb}{Environment.NewLine}");
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    // Part of the Ctrl/Shift-click multi-selection (MainWindow._multiSelection).
    // Kept separate from IsSelected because WPF's TreeView only ever has ONE
    // native selection; the additional rows carry this flag instead, and the
    // row style paints both states with the same brushes. Maintained externally
    // by MainWindow, same pattern as IsAncestorOfSelection below.
    public bool IsMultiSelected
    {
        get => _isMultiSelected;
        set => SetField(ref _isMultiSelected, value);
    }

    // The folder a drop would land in right now, while a drag is over the
    // tree. It used to be marked by moving the native selection there, which
    // meant a drag started inside the tree pulled the selection off the very
    // row that was picked up, landing it on the parent folder and only handing
    // it back on release - two visible jumps for one gesture, and easy to read
    // as the wrong thing having been grabbed (2026-08-05).
    // A flag of its own lets the drop target and the selection be true at the
    // same time, the way Explorer shows it. Painted with the SAME brushes as
    // selection - a deliberate choice, so the mark reads as one thing and no
    // new colour has to be invented or configured. Maintained externally by
    // MainWindow, same pattern as IsMultiSelected above.
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => SetField(ref _isDropTarget, value);
    }

    // True for every ancestor folder of the currently selected item, so its
    // indent guide line can be highlighted VS Code-style to trace the path
    // down to the active selection. Maintained externally (MainWindow reacts
    // to TreeView.SelectedItemChanged) since selection itself is tracked per
    // TreeViewItem, not on this model.
    public bool IsAncestorOfSelection
    {
        get => _isAncestorOfSelection;
        set => SetField(ref _isAncestorOfSelection, value);
    }

    // True when this folder has its own remembered sort override (set via its
    // right-click "정렬", independent of the app-wide default) - drives the
    // small icon next to its name. Computed once at construction from
    // FileSystemService.SortOverrides (same map LoadChildren itself consults
    // for this folder's own path), then flipped directly by MainWindow when
    // the override is set/cleared afterward so the icon updates immediately
    // without a full reload - same externally-maintained pattern as
    // IsAncestorOfSelection above.
    private bool _hasSortOverride;
    public bool HasSortOverride
    {
        get => _hasSortOverride;
        set => SetField(ref _hasSortOverride, value);
    }

    // Which glyph this folder's sort icon currently draws: the direction arrow
    // while it has its own sort, or the neutral "follows the app-wide default"
    // one when it doesn't (see FileSystemService's geometries). Vector rather
    // than an image so it takes the row's brush and needs no light/dark pair.
    // Kept in sync with HasSortOverride by MainWindow (SetFolderSortOverride/
    // ClearFolderSortOverride), same externally-maintained pattern.
    private Geometry? _sortOverrideIconGeometry;
    public Geometry? SortOverrideIconGeometry
    {
        get => _sortOverrideIconGeometry;
        set => SetField(ref _sortOverrideIconGeometry, value);
    }

    // That icon's ToolTip, naming the state it's showing ("정렬: 이름 오름차순
    // (클릭하여 전환)") - always set together with the icon above, since the
    // image alone doesn't say which sort is active. Per-item rather than one
    // static string on the Border, precisely because it differs per folder.
    private string _sortOverrideTooltip = string.Empty;
    public string SortOverrideTooltip
    {
        get => _sortOverrideTooltip;
        set => SetField(ref _sortOverrideTooltip, value);
    }

    // Drives the inline VS Code-style rename UI in the tree row's
    // DataTemplate (a TextBox swapped in for the name TextBlock), rather
    // than a separate popup dialog.
    public bool IsEditing
    {
        get => _isEditing;
        set => SetField(ref _isEditing, value);
    }

    // Backing text for the inline rename TextBox - kept separate from Name
    // (which stays the on-disk name until the rename actually succeeds and
    // RefreshChildren rebuilds this item from disk).
    public string EditingName
    {
        get => _editingName;
        set => SetField(ref _editingName, value);
    }

    // True for a network drive's root and everything beneath it - set on the
    // root by FileSystemService.GetDriveRoots (DriveType.Network) and simply
    // inherited down as children are constructed. Drives the small marker on
    // folder icons so NAS territory is recognizable mid-scroll, the way
    // Explorer badges the network drive icon itself.
    public bool IsOnNetworkDrive { get; set; }

    // The same for a CLOUD drive - Google Drive and the rest of the providers
    // that mount themselves as a letter. Its own flag rather than a value on the
    // one above, because a place can be neither and the two marks answer
    // different questions; a cloud drive reports DriveType.Fixed, so nothing
    // about it is a shade of "network" (2026-08-26).
    public bool IsOnCloudDrive { get; set; }

    // Whether that network drive is answering right now. Drives the badge's
    // colour (green connected / red not), kept up to date by MainWindow's own
    // poll rather than asked per row - one question per drive, not per folder,
    // and never from the row's own render path where a dead mapping would cost
    // seconds. Set on the root and pushed down its loaded rows on change.
    private bool _isNetworkDriveOffline;
    public bool IsNetworkDriveOffline
    {
        get => _isNetworkDriveOffline;
        set => SetField(ref _isNetworkDriveOffline, value);
    }

    private bool _isBookmarked;

    // The 책갈피 marker (see MainWindow's ToggleBookmark and the row
    // template's bookmark glyph). Initialized from the static
    // FileSystemService.BookmarkedPaths in the constructor so refreshes that
    // rebuild items (and lazy loads that create them for the first time)
    // come up already flagged; toggling flips both this and that set.
    public bool IsBookmarked
    {
        get => _isBookmarked;
        set => SetField(ref _isBookmarked, value);
    }

    private bool _isHiddenFolderShown;

    // A folder the user has hidden that is on screen anyway - either because a
    // jump is passing through it (TemporarilyVisiblePaths) or because "숨긴
    // 폴더 표시" is on. The row marks itself so neither case reads as "this
    // folder came back on its own": italic name plus a recessed row, no dimming
    // (see the tree template). False for every ordinary row, so it costs
    // nothing to nobody who has never hidden anything.
    public bool IsHiddenFolderShown
    {
        get => _isHiddenFolderShown;
        set => SetField(ref _isHiddenFolderShown, value);
    }

    private bool _isCut;

    // Waiting on a Ctrl+X paste. Fades the row's ICON only (see the tree
    // template) - the name keeps its full weight, since dimmed text isn't how
    // this app marks anything. Initialized from FileSystemService.CutPaths in
    // the constructor, same as IsBookmarked above.
    public bool IsCut
    {
        get => _isCut;
        set => SetField(ref _isCut, value);
    }

    public FileSystemItem(string name, string fullPath, bool isDirectory, FileSystemItem? parent = null)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Parent = parent;
        IsOnNetworkDrive = parent?.IsOnNetworkDrive ?? false;
        IsOnCloudDrive = parent?.IsOnCloudDrive ?? false;
        // Inherited so a row built while the drive is out (a merge running on
        // a stale listing, a folder expanded from cache) starts out looking
        // the same as its neighbours instead of alone in full colour.
        _isNetworkDriveOffline = parent?.IsNetworkDriveOffline ?? false;
        _isBookmarked = FileSystemService.BookmarkedPaths.Contains(fullPath);
        _isCut = FileSystemService.CutPaths.Count > 0 && FileSystemService.CutPaths.Contains(fullPath);
        // Built at all while hidden means it is being shown deliberately - the
        // filter in ReadChildrenFromDisk is what keeps the other case out.
        _isHiddenFolderShown = isDirectory &&
            FileSystemService.HiddenPaths.Count > 0 &&
            FileSystemService.HiddenPaths.Contains(FileSystemService.NormalizeHiddenPath(fullPath));
        if (isDirectory)
        {
            // Always resolves to SOME icon - this folder's own override if it
            // has one, otherwise the neutral one - so the icon shown while
            // merely selected (see MainWindow.xaml's IsSelected trigger) is
            // there to click instead of rendering blank until an override
            // exists.
            if (FileSystemService.SortOverrides.TryGetValue(
                FileSystemService.NormalizeSortOverridePath(fullPath), out var over))
            {
                _hasSortOverride = true;
                _sortOverrideIconGeometry = FileSystemService.SortOverrideGeometry(over.Descending);
                _sortOverrideTooltip = FileSystemService.FormatSortTooltip(over.Field, over.Descending);
            }
            else
            {
                // No override of its own - the neutral glyph.
                _sortOverrideIconGeometry = FileSystemService.FollowsGlobalSortGeometry;
                _sortOverrideTooltip = FileSystemService.NoSortOverrideTooltip;
            }
            Children.Add(new FileSystemItem());
        }
    }

    // A blank row parked after the last drive so a jump to something near the
    // end of the tree still has somewhere to scroll into - see MainWindow's
    // 아래 여백 section. It draws nothing, takes no focus and answers no click;
    // all it does is count as one row of scroll range.
    //
    // Deliberately NOT put in _roots: the roots collection is walked in dozens
    // of places, and one of them matches a path by prefix - an empty FullPath
    // would have matched everything. It rides alongside in the tree's items
    // instead, so no walk over the model can see it.
    public bool IsBottomGap { get; private init; }

    public static FileSystemItem CreateBottomGap() => new() { IsBottomGap = true };

    private FileSystemItem()
    {
        IsPlaceholder = true;
        Name = string.Empty;
        FullPath = string.Empty;
    }

    // The trailing "더 보기" row for an oversized folder; carries the count of
    // items still hidden in the parent's overflow so the row can label itself.
    private FileSystemItem(FileSystemItem parent, int remainingCount, bool showLess)
    {
        IsShowMore = true;
        IsShowLess = showLess;
        RemainingCount = remainingCount;
        Parent = parent;
        Name = string.Empty;
        FullPath = string.Empty;
    }

    public static FileSystemItem CreateShowMore(FileSystemItem parent, int remainingCount)
        => new(parent, remainingCount, showLess: false);

    public static FileSystemItem CreateShowLess(FileSystemItem parent, int remainingCount)
        => new(parent, remainingCount, showLess: true);

    // NO COUNT, unlike the two above, and the zero is the point rather than a
    // placeholder: this row's label reads FilteredOutCount and HiddenFolderCount
    // off the PARENT (see ShowMoreLabel), because it already holds a reference
    // to the folder it stands in and a second copy of a number is a second thing
    // that can go stale.
    //
    // It was handed the SUM of those two for a day (2026-08-26 to 08-27) - made
    // at the call site, stored in RemainingCount, and read by nothing, because
    // the label had already moved to the parent. Exactly the second copy the
    // comment on ShowMoreLabel says not to keep.
    public static FileSystemItem CreateFilterNotice(FileSystemItem parent)
        => new(parent, remainingCount: 0, showLess: false) { IsFilterNotice = true };

    // Lets MainWindow's whole-tree refresh (RefreshAllLoadedFolders) skip
    // folders that were never expanded - nothing loaded means nothing stale to
    // re-read from disk.
    public bool ChildrenLoaded => _childrenLoaded;

    // When Children were last actually read off the disk. The live
    // external-change refresh (MainWindow's QueueExternalRefresh) compares this
    // against the moment a change was reported, to tell "our listing predates
    // that change" from "we already re-read it afterwards, so there's nothing
    // to redo". Environment.TickCount64, not DateTime - monotonic, so a system
    // clock change can't make a fresh load look ancient.
    public long LastLoadedTicks { get; private set; }

    // Set by MainWindow's external-change path when a watcher event lands on
    // this folder while it is loaded but COLLAPSED: the live refresh only
    // patches expanded folders, and EnsureChildrenLoaded caches - so without
    // this flag the next expand would show the stale pre-change listing
    // (2026-07-22: screenshot taken while its folder was auto-collapsed,
    // expand showed the old newest-first top row). Consumed by
    // TreeViewItem_Expanded, cleared by any real re-read.
    public bool PendingExternalRefresh { get; set; }

    public void EnsureChildrenLoaded()
    {
        if (_childrenLoaded || !IsDirectory)
        {
            return;
        }

        // Stamped BEFORE the read, not after: a file created while LoadChildren
        // is enumerating can be missed by the enumeration yet have its watcher
        // event carry an earlier tick than an after-the-read stamp - which made
        // QueueExternalRefresh's "already re-read it afterwards" check skip the
        // refresh and the file never appear. Stamping first errs the other way:
        // worst case one redundant refresh of a folder that did catch the file.
        LastLoadedTicks = Environment.TickCount64;
        var loaded = FileSystemService.LoadChildren(FullPath, this, out bool readFailed, "expand");

        // A failed read (sleeping NAS, unplugged drive) is UNKNOWN contents,
        // not empty contents: keep the placeholder so the expander arrow
        // stays, and stay "not loaded" so the next expand simply retries -
        // recording empty here is what left a network drive permanently
        // arrow-less for the session.
        if (readFailed)
        {
            return;
        }

        _childrenLoaded = true;
        PendingExternalRefresh = false;
        PopulateCapped(loaded);
    }

    // A NETWORK READ THAT MISSED ITS DEADLINE AND THEN FINISHED CLEANLY. The
    // listing is real and already in hand - see LoadChildren, where the caller
    // stops waiting at 1.5s but the read itself cannot be cancelled - so this
    // is the same landing EnsureChildrenLoaded makes, arriving a moment later.
    //
    // Refused once something else has filled the folder: a retry that beat the
    // deadline, or a refresh. The late answer is then the older of the two and
    // has nothing to add.
    public void AcceptLateChildren(List<FileSystemItem> loaded)
    {
        if (_childrenLoaded || !IsDirectory)
        {
            return;
        }

        _childrenLoaded = true;
        PendingExternalRefresh = false;
        PopulateCapped(loaded);
    }

    // WHAT AN EMPTY FOLDER SHOWS, asked in ONE place because two of them disagreed
    // (2026-08-26, on report: the note appeared and then vanished on the first
    // collapse). The note rides on IsShowMore so the tree's "not a real file"
    // guards skip it - and RecollapseOverflow uses that same flag to strip the
    // overflow control out before rebuilding, so it stripped the note too and
    // left the folder empty again. Both rebuild paths ask here now, so the
    // answer cannot depend on which one ran last.
    //
    // Nothing left to show AND something was taken out means the emptiness on
    // screen is the APP's doing, not the folder's - and the folder says so
    // rather than presenting itself as empty to whoever is about to delete it.
    // Two ways to be emptied, and both count: the file-type filter, and the
    // 숨긴 폴더 list.
    private List<FileSystemItem> WithFilterNotice(List<FileSystemItem> rows)
        => rows.Count == 0
            ? new List<FileSystemItem> { CreateFilterNotice(this) }
            : rows;

    // A CHILD TAKEN OUT FROM OUTSIDE THE READ PATH still has to be counted.
    //
    // HiddenFolderCount is filled where the 숨긴 폴더 list is applied, which is
    // FileSystemService's read - and hiding a folder deliberately does NOT read
    // the parent again (MainWindow.HideFolder: a reload would collapse whatever
    // else is open under it). So the one moment the notice is most wanted, the
    // moment the row leaves, was the one moment nothing was counting: hide the
    // only subfolder of a folder and it went back to presenting itself as empty
    // until the next read, which for a folder nobody refreshes means until the
    // app restarts (EnsureChildrenLoaded caches, so collapsing and expanding
    // does not re-ask either). Found 2026-08-27 reviewing the round that built
    // the notice.
    //
    // Called INSTEAD OF a re-read, not alongside one - the caller has already
    // removed the row, and this brings the two numbers that describe the result
    // back in step with it.
    public void NoteChildHidden()
    {
        HiddenFolderCount++;

        // Only the empty case has anything to add: while real rows remain there
        // is no notice on screen, and the count above is enough to make the next
        // rebuild say the right thing.
        if (Children.Count == 0)
        {
            Children.ReplaceAll(WithFilterNotice(new List<FileSystemItem>()));
        }
    }

    // Fills Children with at most DisplayCap items; anything beyond that is
    // parked in _overflow behind a single trailing "더 보기" row.
    private void PopulateCapped(List<FileSystemItem> loaded)
    {
        _overflow.Clear();
        _showingAll = false;

        if (loaded.Count <= DisplayCap)
        {
            Children.ReplaceAll(WithFilterNotice(loaded));
            return;
        }

        _overflow.AddRange(loaded.GetRange(DisplayCap, loaded.Count - DisplayCap));

        var capped = new List<FileSystemItem>(DisplayCap + 1);
        capped.AddRange(loaded.GetRange(0, DisplayCap));
        capped.Add(CreateShowMore(this, _overflow.Count));
        Children.ReplaceAll(capped);
    }

    // The full loaded listing with the reveal state ignored: the rows on show
    // plus whatever still waits behind "더 보기". The viewer's carousel counts
    // pictures against this, because "35 / 447" is a claim about the FOLDER,
    // and a total that grew when 더 보기 was clicked read as the counter being
    // wrong (2026-08-09). The synthetic trailing row is not a child and is
    // skipped.
    //
    // Concatenated plainly, now that _overflow means exactly "the rows not in
    // Children" whichever way the folder was revealed (see ShowAllChildren).
    // While the two could overlap this needed a guard on the reveal state, and
    // a reveal that took rows off the front of the overflow rather than all of
    // them would have slipped straight past that guard.
    public IEnumerable<FileSystemItem> AllLoadedChildren
    {
        get
        {
            foreach (var child in Children)
            {
                if (!child.IsShowMore)
                {
                    yield return child;
                }
            }
            foreach (var child in _overflow)
            {
                yield return child;
            }
        }
    }

    // "더 보기" clicked: drop that row and append everything held back. Only
    // adds to Children, so the first DisplayCap rows - and any subtrees
    // expanded within them - keep their state.
    public void ShowAllChildren()
    {
        if (_showingAll || _overflow.Count == 0)
        {
            return;
        }
        // Built as one list and applied in a single notification. Appending the
        // overflow row by row is what made a large folder's reveal - and every
        // filter toggle that replays it - repaint itself hundreds of times.
        var revealed = new List<FileSystemItem>(Children.Count + _overflow.Count);
        foreach (var child in Children)
        {
            if (!child.IsShowMore)
            {
                revealed.Add(child);
            }
        }
        revealed.AddRange(_overflow);
        revealed.Add(CreateShowLess(this, _overflow.Count));
        Children.ReplaceAll(revealed);

        // ONE RULE FOR THE OVERFLOW: it holds exactly the rows that are NOT in
        // Children. It used to be left standing through a reveal - harmless
        // while nothing read it in that state, but ShowChildrenUpTo takes its
        // rows off the front, so the two reveals disagreed about what the list
        // meant and anything recapping had to know which one it was looking at.
        // Clearing here makes the two the same shape (2026-08-12).
        _overflow.Clear();
        _showingAll = true;
    }

    // Uncovers only as far as ONE row instead of the whole overflow. Clicking a
    // thumbnail past the cap used to reveal everything, and on a folder of
    // 2,404 that put every remaining row into the tree in one go - measured as
    // 1.6 seconds between a press and its own release, during which the app
    // answered nothing and the click read as broken (2026-08-12, click.log).
    // The click was never what was slow; what it asked for was.
    //
    // Everything UP TO the target comes with it. The rows in between are what
    // the tree would have to walk to put the target on screen anyway, and a
    // sorted listing with holes in it would be a worse thing to hand back than
    // the cost of carrying them. So a click near the front of a large folder
    // now costs almost nothing, which is where most of them land; one at the
    // far end still pays, and there is no way around that while the rule is
    // that what you clicked is a row in the tree.
    public void ShowChildrenUpTo(FileSystemItem target)
    {
        if (_showingAll || _overflow.Count == 0)
        {
            return;
        }

        int index = _overflow.IndexOf(target);
        if (index < 0)
        {
            return;
        }

        var revealed = new List<FileSystemItem>(Children.Count + index + 1);
        foreach (var child in Children)
        {
            if (!child.IsShowMore)
            {
                revealed.Add(child);
            }
        }

        revealed.AddRange(_overflow.GetRange(0, index + 1));
        _overflow.RemoveRange(0, index + 1);

        if (_overflow.Count > 0)
        {
            revealed.Add(CreateShowMore(this, _overflow.Count));
        }
        else
        {
            // The last of it came across, so the folder is simply revealed now
            // and gets the way back out that a full reveal gets.
            _showingAll = true;
            revealed.Add(CreateShowLess(this, revealed.Count - DisplayCap));
        }

        Children.ReplaceAll(revealed);
    }

    // Returns a fully-revealed folder to the capped state (first DisplayCap +
    // a "더 보기" row) so navigating to another favorite never has to walk past
    // a huge realized list (see MainWindow.NavigateToPath). The first
    // DisplayCap rows - and anything expanded within them - are carried over as
    // the SAME instances, so no subtree state is lost.
    //
    // TAKEN FROM CHILDREN, NOT FROM _overflow, and that is the fix for a folder
    // that could not be re-capped at all (2026-08-12). RefreshChildren clears
    // _overflow and does not refill it while showing all - correctly, since
    // every row is in Children then - so this used to see an empty overflow and
    // return at its first line. Any refresh of a revealed folder therefore left
    // it revealed for good: the collapse row did nothing, a jump's recap did
    // nothing, and ShowAllChildren also declines while _showingAll, so nothing
    // could put the two back in step. Deriving the split here means the two
    // cannot disagree.
    public void RecollapseOverflow()
    {
        // The listing itself: the trailing synthetic row is a control, not a
        // file, and must not be carried into the overflow.
        var real = new List<FileSystemItem>(Children.Count);
        foreach (var child in Children)
        {
            if (!child.IsShowMore && !child.IsPlaceholder)
            {
                real.Add(child);
            }
        }

        // Nothing is on show past the cap, so there is nothing revealed to take
        // back and this is already the capped shape. LEAVING IT ALONE IS THE
        // WHOLE POINT: an earlier version rebuilt Children here from the rows
        // it could see, which on an ordinary capped folder threw away the "더
        // 보기" row AND everything waiting behind it - and since a collapse now
        // recaps, closing any large folder erased it (2026-08-12).
        //
        // The one case that does need settling is a folder that shrank under
        // the cap while revealed: nothing is hidden any more, so a trailing row
        // standing for nothing has to go.
        if (real.Count <= DisplayCap)
        {
            // Through the same question PopulateCapped asks - see
            // WithFilterNotice. `real` has had every IsShowMore row taken out of
            // it above, and the filter note is one of those.
            var settled = WithFilterNotice(real);
            if (_overflow.Count == 0 && (_showingAll || Children.Count != settled.Count))
            {
                _showingAll = false;
                Children.ReplaceAll(settled);
            }
            return;
        }

        // Everything past the cap goes back IN FRONT of whatever was already
        // waiting: a partial reveal takes its rows off the front of the
        // overflow, so this is where they belong when they return.
        var back = real.GetRange(DisplayCap, real.Count - DisplayCap);
        back.AddRange(_overflow);
        _overflow.Clear();
        _overflow.AddRange(back);
        _showingAll = false;

        var capped = new List<FileSystemItem>(DisplayCap + 1);
        capped.AddRange(real.GetRange(0, DisplayCap));
        capped.Add(CreateShowMore(this, _overflow.Count));
        Children.ReplaceAll(capped);
    }

    // The instance this folder currently lists under that name, revealed or
    // waiting behind "더 보기", or null if it lists nothing by that name.
    //
    // Deliberately does NOT reveal the way FindChildForNavigation does: this
    // exists to ask whether a row someone else is holding is still the one in
    // the tree, and a question that moves rows about could not be asked from
    // the places that need to ask it.
    public FileSystemItem? FindLoadedChild(string name)
        => AllLoadedChildren.FirstOrDefault(c =>
            !c.IsPlaceholder &&
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    // Finds a direct child by name for navigation, looking past the cap into
    // the overflow too - and revealing the overflow when the match is hidden
    // there, so RevealChain can realize its container. A path segment is
    // always a directory (they sort ahead of files), so this only ever forces
    // a reveal on a folder with more than DisplayCap subfolders, not on the
    // many-files folders capping exists to keep light.
    public FileSystemItem? FindChildForNavigation(string name)
    {
        var visible = Children.FirstOrDefault(c =>
            !c.IsPlaceholder && !c.IsShowMore &&
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (visible is not null)
        {
            return visible;
        }

        var hidden = _overflow.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (hidden is not null)
        {
            ShowChildrenUpTo(hidden);
            return hidden;
        }

        return null;
    }

    // Re-reads this folder from disk and applies the result as a DIFF against
    // the current rows instead of clear-and-refill: entries that survived keep
    // their existing FileSystemItem instance - and with it their loaded
    // children, expanded state, and (crucially) their realized tree container.
    // Only genuinely new/removed/moved rows touch the collection at all, so a
    // change to one file cannot tear down and regenerate the whole expanded
    // subtree beneath this folder.
    //
    // This replaced clear-and-refill for every WATCHER-driven refresh
    // (2026-07-23 02:4x): a project switch in an IDE churns some ancestor
    // folder's listing every few seconds, and rebuilding that ancestor
    // wholesale destroyed every realized row below it - the whole viewport
    // went blank until the user poked the layout, faster than the
    // post-refresh forced redraw could win back (redraw.log showed dozens of
    // external-refresh passes AROUND the blank, proving redraw-after-rebuild
    // was the wrong altitude for the fix). A no-op change (hidden file,
    // attribute flip) now results in zero collection operations.
    public void MergeChildrenFromDisk()
    {
        if (!IsDirectory || !_childrenLoaded)
        {
            return;
        }
        PendingExternalRefresh = false;
        // Same stamped-before-the-read rule as EnsureChildrenLoaded.
        LastLoadedTicks = Environment.TickCount64;

        var fresh = FileSystemService.LoadChildren(FullPath, this, out bool readFailed, "merge");

        // A refresh that couldn't actually read the folder keeps what's on
        // screen: stale rows beat wiping a NAS root (and every loaded subtree
        // under it) because the drive blinked during a background refresh.
        // The next successful watcher event or expand re-syncs for real.
        if (readFailed)
        {
            return;
        }

        // Reuse the existing instance wherever the fresh listing has an entry
        // of the same name (and same file-vs-folder kind - a name reused for
        // the other kind gets a fresh instance, its old subtree is meaningless).
        var existingByName = new Dictionary<string, FileSystemItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in Children)
        {
            if (!child.IsPlaceholder && !child.IsShowMore)
            {
                existingByName[child.Name] = child;
            }
        }
        foreach (var child in _overflow)
        {
            existingByName[child.Name] = child;
        }

        var target = new List<FileSystemItem>(fresh.Count);
        foreach (var loaded in fresh)
        {
            target.Add(existingByName.TryGetValue(loaded.Name, out var existing) && existing.IsDirectory == loaded.IsDirectory
                ? existing
                : loaded);
        }

        // Same cap semantics as PopulateCapped/ShowAllChildren: overflow only
        // exists while not showing all, and shrinking back under the cap
        // returns the folder to the plain uncapped state.
        _overflow.Clear();
        List<FileSystemItem> visibleTarget = target;
        if (!_showingAll && target.Count > DisplayCap)
        {
            visibleTarget = target.GetRange(0, DisplayCap);
            _overflow.AddRange(target.GetRange(DisplayCap, target.Count - DisplayCap));
        }
        else if (target.Count <= DisplayCap)
        {
            _showingAll = false;
        }

        // Sync Children to visibleTarget with minimal operations. Remove pass
        // first (also drops the old "더 보기" row - re-added below with a
        // fresh count), then walk the target order: after step i the first
        // i+1 rows match the target, so a wanted row is only ever found
        // further right (Move) or absent (Insert).
        var keep = new HashSet<FileSystemItem>(visibleTarget);
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Children[i]))
            {
                Children.RemoveAt(i);
            }
        }
        for (int i = 0; i < visibleTarget.Count; i++)
        {
            var wanted = visibleTarget[i];
            if (i < Children.Count && ReferenceEquals(Children[i], wanted))
            {
                continue;
            }
            int currentIndex = Children.IndexOf(wanted);
            if (currentIndex >= 0)
            {
                Children.Move(currentIndex, i);
            }
            else
            {
                Children.Insert(i, wanted);
            }
        }

        if (_overflow.Count > 0)
        {
            Children.Add(CreateShowMore(this, _overflow.Count));
        }
        else if (_showingAll)
        {
            // The remove pass above dropped the old row with the rest of the
            // synthetic ones, so a revealed folder that refreshes would lose its
            // way back out and there would be no gesture left to restore it.
            Children.Add(CreateShowLess(this, target.Count - DisplayCap));
        }
    }

    // Re-reads this folder's contents from disk after an operation that
    // changed them (rename/delete/paste). Note this rebuilds Children with
    // fresh instances, so any previously-expanded descendants collapse -
    // background/watcher refreshes must use MergeChildrenFromDisk instead.
    public void RefreshChildren()
    {
        if (!IsDirectory)
        {
            return;
        }
        _childrenLoaded = false;
        EnsureChildrenLoaded();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
