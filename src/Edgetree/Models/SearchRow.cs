using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using SidebarExplorer.App.Services;

namespace SidebarExplorer.App.Models;

// One row in the search results list, which interleaves folder headers with
// files: consecutive results sharing a folder collapse under a single header
// (see MainWindow.RunSearchFilter). A header carries only its DirectoryPath; a
// file row carries the SearchEntry it came from. The results DataTemplate
// switches layout on IsHeader, and the click/keyboard/context-menu handlers act
// only on rows whose Entry is non-null.
//
// INotifyPropertyChanged is for what changes while the row is on screen: Icon
// (in Windows-shell icon mode a per-file icon, an .exe's for one, can arrive from a
// background extraction, see ShellIconService), and the two marks, IsCut and
// IsMarked. Everything else is init-only.
public sealed class SearchRow : INotifyPropertyChanged
{
    public bool IsHeader { get; init; }

    // The synthetic "… 더 보기 (N개)" row appended when the results were capped -
    // clicking it raises the display limit (see MainWindow). Its own kind so
    // the click handler can tell it apart from a real file row.
    public bool IsShowMore { get; init; }
    public string ShowMoreLabel { get; init; } = string.Empty;

    public string DirectoryPath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;

    // What the row's tooltip says. The tree and both panel lists already answer
    // "which one is this" on hover, and the results list needed it at least as
    // much: a header path is trimmed with an ellipsis the moment the panel is
    // narrow, and a file row shows nothing but a name, so two results with the
    // same name are indistinguishable on screen. A file row has to have its
    // folder put back; a header row already is one.
    public string TooltipPath => IsHeader
        ? DirectoryPath
        : System.IO.Path.Combine(DirectoryPath, FileName);
    public FileSearchService.SearchEntry? Entry { get; init; }

    // Set on file rows only while 폴더별 묶기 is OFF: with no header standing
    // over them, two results with the same name in different folders would be
    // the same row twice, and the tooltip above is not an answer somebody can
    // read down a list. A real case, not a hypothetical - a *.png search over a
    // projects drive returned en.png and og.png twice each within the first
    // screen (2026-08-31), which is also why this shows the WHOLE path and not
    // just the folder's own name: the two en.png files both sit in a folder
    // called screenshots.
    //
    // The template gives DirectoryPath its own column so the paths line up down
    // the list, in the header's smaller font and the muted folder colour, and
    // drops the row's indent, which has nothing left to indent under.
    public bool ShowsFolder { get; init; }

    // Mode-aware (PNG set vs. Windows shell icons - same switch as the tree,
    // see ShellIconService): a per-extension file icon for file rows, a folder
    // icon for header rows. Whether it actually shows is gated by the
    // ShowFolderIcons / ShowFileIcons toggles in the results template, same as
    // the tree.
    public ImageSource? Icon => IsShowMore
        ? null
        : IsHeader
            ? ShellIconService.GetFolderIcon(FolderNameOf(DirectoryPath), isExpanded: false)
            : ShellIconService.GetFileIcon(FileName, Entry?.FullPath ?? string.Empty, RaiseIconChanged);

    // Where the query matched inside FileName, so that run can be drawn in the
    // highlight color (see SearchHighlightBehavior). -1/0 means "don't
    // highlight" - used for header rows and for wildcard queries, where there's
    // no single literal substring to point at.
    public int MatchStart { get; init; } = -1;
    public int MatchLength { get; init; }

    // Same meaning as FileSystemItem.IsCut - the row's icon fades while a
    // Ctrl+X on it is waiting to be pasted. Settable (not init-only) because
    // the cut can happen while the row is already on screen, and seeded from
    // FileSystemService.CutPaths so a re-run search comes back marked.
    private bool _isCut;
    public bool IsCut
    {
        get => _isCut;
        set
        {
            if (_isCut != value)
            {
                _isCut = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCut)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CutOpacity)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CutFontStyle)));
            }
        }
    }

    // The two marks as plain values the row template binds to DIRECTLY, rather
    // than as trigger conditions. Triggers on this template applied when a row
    // was built but did not repaint a row already on screen when IsCut changed
    // under it (2026-07-28, twice - as a DataTemplate trigger and again as a
    // style trigger); a direct binding takes the same notification path as
    // Icon, which has always updated live in this very template.
    public double CutOpacity => _isCut ? 0.4 : 1.0;

    // Still italic HERE, while the tree's cut mark moved to a quieted name
    // colour (2026-08-02). Not an oversight: italic became ambiguous in the
    // TREE, where it now also means "a hidden folder being shown", and that
    // state cannot occur in a result list - these rows are files. Matching the
    // tree would mean pushing live brushes into this model, since a search row
    // repaints only from direct value bindings (see the note above), and the
    // ambiguity it would solve does not exist here.
    public System.Windows.FontStyle CutFontStyle => _isCut ? FontStyles.Italic : FontStyles.Normal;

    // One of the app's multi-selection (MainWindow._multiSelection), marked
    // from the thumbnail list while the results drive it (2026-10-06). The
    // tree's rows ARE the marked items and paint IsMultiSelected themselves;
    // these rows are not, so MainWindow sets this by path. A plain value the
    // template binds to directly, for the reason given for CutOpacity above.
    private bool _isMarked;
    public bool IsMarked
    {
        get => _isMarked;
        set
        {
            if (_isMarked != value)
            {
                _isMarked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMarked)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MarkVisibility)));
            }
        }
    }

    public Visibility MarkVisibility => _isMarked ? Visibility.Visible : Visibility.Collapsed;

    // Another result has this file's name, size and write time - a copy, as
    // far as the index can tell without reading either file (2026-10-06, see
    // MainWindow.FindSameFiles). Set when the row is built, so plain init
    // values; the template draws a faint band from them.
    public bool IsDuplicate { get; init; }

    // The first row of its set of copies in the list's order, drawn a step
    // stronger so consecutive sets do not run together. Not "the original".
    public bool IsDuplicateLead { get; init; }

    public Visibility DuplicateVisibility => IsDuplicate ? Visibility.Visible : Visibility.Collapsed;

    // The band's strength. 0.07 was read clearly on a dark palette on
    // 2026-10-06; the lead is twice that.
    public double DuplicateOpacity => IsDuplicateLead ? 0.14 : 0.07;

    public static SearchRow Header(string directoryPath) => new()
    {
        IsHeader = true,
        DirectoryPath = directoryPath
    };

    public static SearchRow ShowMore(string label) => new()
    {
        IsShowMore = true,
        ShowMoreLabel = label
    };

    public static SearchRow File(FileSearchService.SearchEntry entry, int matchStart, int matchLength,
        bool showsFolder, bool isDuplicate = false, bool isDuplicateLead = false) => new()
    {
        IsHeader = false,
        DirectoryPath = entry.DirectoryPath,
        FileName = entry.FileName,
        Entry = entry,
        MatchStart = matchStart,
        MatchLength = matchLength,
        ShowsFolder = showsFolder,
        IsDuplicate = isDuplicate,
        IsDuplicateLead = isDuplicateLead,
        IsCut = FileSystemService.CutPaths.Count > 0 && FileSystemService.CutPaths.Contains(entry.FullPath)
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseIconChanged()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));

    private static string FolderNameOf(string directoryPath)
    {
        string trimmed = directoryPath.TrimEnd('\\', '/');
        int slash = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }
}
