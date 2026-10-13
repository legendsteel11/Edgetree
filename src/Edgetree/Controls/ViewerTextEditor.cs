using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Search;
using SidebarExplorer.App.Services;
// The project also references WinForms, whose namespaces name the same
// types.
using Application = System.Windows.Application;
using Binding = System.Windows.Data.Binding;
using TextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
// Inside a TextEditor, TextArea names the instance property, not the type.
using EditingTextArea = ICSharpCode.AvalonEdit.Editing.TextArea;

namespace SidebarExplorer.App;

// The panel's text view: a TXT or MD file, wrapped at the panel's width
// (2026-10-11, the first half of the TXT/MD round). Read-only until the edit
// bar's 편집 is pressed, which turns editing on for that file until 편집 취소
// or the panel moving to another (the second half, the same day).
//
// WHY AVALONEDIT AND NOT A WPF TEXTBOX. A TextBox lays out the whole text, so
// a long file is slow to scroll and select in proportion to its length -
// the sluggishness this round started from. AvalonEdit builds only the lines
// on screen, so the cost stays the size of the panel. It is the project's
// first package dependency, accepted for that reason (2026-09-28).
//
// EVERY REFERENCE TO AN AVALONEDIT TYPE LIVES IN THIS CLASS - this file,
// ViewerTextEditor.Markdown.cs (the 서식 commands) and
// ViewerTextEditor.Replace.cs (the replace row). MainWindow holds the editor
// as a plain FrameworkElement and reaches it only through methods that run
// after the first 보기 press, so a session that never opens a text file never
// loads the assembly. See MainWindow's ViewerText section.
public sealed partial class ViewerTextEditor : TextEditor
{
    // What the worker hands back: the document, built off the UI thread, and
    // what was learned about the file on the way.
    // LongLinesSplit: the text on screen has breaks the file does not (see
    // TextFileReader.LongLineLimit), so like a head-only one it must never be
    // saved back - which is why neither can be edited (CanEdit).
    public sealed record LoadedText(
        TextDocument Document,
        bool Truncated,
        long FileLength,
        Encoding Encoding,
        bool HasBom,
        DateTime LastWriteUtc,
        bool LongLinesSplit)
    {
        public string EncodingName => HasBom ? Encoding.WebName + " BOM" : Encoding.WebName;

        public bool CanEdit => !Truncated && !LongLinesSplit;

        // The text's lines as hashed for the changed-line marks, taken on the
        // worker with the read (ViewerTextEditor.ChangeMarks.cs).
        public long[]? LineHashes { get; init; }
    }

    // ONE FILE'S TEXT and everything a save needs to write it back: the
    // encoding it came in, and the size and time it had, which a save checks
    // the file against first (TextFileWriter). The undo history lives in the
    // document, so keeping the session keeps that too.
    internal sealed class TextSession
    {
        public TextSession(string path, TextDocument document)
        {
            Path = path;
            Document = document;
        }

        // Settable for two reasons: a file the app renames or moves keeps its
        // edits under the new name (MovePath), and 다른 이름으로 저장 makes the
        // text another file's (CompleteSaveAs) - which can be the other kind.
        public string Path { get; set; }
        public TextDocument Document { get; }
        public bool Markdown { get; set; }
        public bool Truncated { get; init; }
        public bool LongLinesSplit { get; init; }
        public bool CanEdit => !Truncated && !LongLinesSplit;
        public Encoding Encoding { get; set; } = Encoding.UTF8;
        public bool HasBom { get; set; }
        public long StampLength { get; set; }
        public DateTime StampLastWriteUtc { get; set; }
        // Where the view was when the panel moved away, put back when the
        // session is.
        public int CaretOffset { get; set; }
        public double VerticalOffset { get; set; }

        // The document version last handed to the disk copy
        // (TakeChangedUnsaved); null when it has never been, or when the text
        // went back to saved and the copy was dropped.
        public ITextSourceVersion? PersistedVersion { get; set; }

        public bool IsModified => !Document.UndoStack.IsOriginalFile;

        // The saved text's lines as hashes, and the lines found to differ
        // from it - see ViewerTextEditor.ChangeMarks.cs. Null while a new
        // baseline is still being hashed; the generation tells a late one
        // from the current.
        public long[]? Baseline { get; set; }
        public int BaselineGeneration { get; set; }
        public Dictionary<DocumentLine, LineChange> LineChanges { get; } = new();
    }

    // What the disk copy needs of an edited file (UnsavedTextStore).
    public sealed record UnsavedSnapshot(
        string Path,
        string Text,
        int CodePage,
        bool HasBom,
        long StampLength,
        DateTime StampLastWriteUtc,
        bool Markdown);

    // A save, taken as a snapshot on the UI thread so the worker never reads
    // the document. Version is how CompleteSave tells whether anything was
    // typed while the write was out.
    public sealed record SaveRequest(
        string Path,
        string Text,
        Encoding Encoding,
        bool HasBom,
        long ExpectedLength,
        DateTime ExpectedLastWriteUtc)
    {
        internal TextSession Session { get; init; } = null!;
        internal ITextSourceVersion? Version { get; init; }
    }

    // LINES SET AS FAR APART AS VS CODE SETS THEM (2026-10-13, on request:
    // beside VS Code the text read as packed). AvalonEdit has no line-spacing
    // option - a line is as tall as its font says - so the font says more:
    // a composite FontFamily that is Consolas for what Consolas has and the
    // system's own fallback (Global User Interface, which is what supplied
    // Hangul and the other scripts before) for the rest, with LineSpacing
    // 1.35, VS Code's ratio on Windows (14px text, 18.9px lines). The extra
    // is split evenly above and below the text through Baseline. A named
    // family cannot be changed (NotSupportedException), hence the composite.
    // Measured in a probe editor: 16.39 -> 18.90 at 14px, wrapped rows
    // spaced the same, Hangul drawn through the fallback map.
    // Built once; WPF fonts are not per-editor state.
    private static readonly FontFamily TextFontFamily = CreateTextFontFamily();

    private static FontFamily CreateTextFontFamily()
    {
        const double LineSpacing = 1.35;
        var consolas = new FontFamily("Consolas");
        var family = new FontFamily
        {
            LineSpacing = LineSpacing,
            Baseline = consolas.Baseline + (LineSpacing - consolas.LineSpacing) / 2,
        };
        family.FamilyNames.Add(System.Windows.Markup.XmlLanguage.GetLanguage("en-us"), "Edgetree Text");
        family.FamilyMaps.Add(new FontFamilyMap { Target = "Consolas", Unicode = "0000-10FFFF" });
        family.FamilyMaps.Add(new FontFamilyMap { Target = "Global User Interface", Unicode = "0000-10FFFF" });
        return family;
    }

    private readonly SearchPanel _search;
    private bool _markdown;
    private bool _light;

    // The text on screen. Null while the view shows nothing.
    private TextSession? _session;

    // EDITED TEXT THE PANEL HAS MOVED AWAY FROM (2026-10-11, the VS Code way,
    // decided with the author 09-28). Leaving a file never asks and never
    // drops an edit: the session waits here, the tree row says so, and coming
    // back to the file opens it again exactly as it was left - undo history
    // included. It leaves this list when it is saved or reloaded.
    // The copy that survives a crash or a restart is MainWindow's
    // UnsavedTextStore, kept in step through TakeChangedUnsaved and
    // TakeCleanedPaths; this list is only what this process holds.
    private readonly Dictionary<string, TextSession> _unsaved = new(StringComparer.OrdinalIgnoreCase);

    // Files whose edits went away since MainWindow last asked - saved,
    // reloaded, undone back to the saved text, or dropped - so their disk
    // copies can go too. A file edited again leaves the set.
    private readonly HashSet<string> _cleaned = new(StringComparer.OrdinalIgnoreCase);

    // Anything the edit bar shows has changed: editing began or ended, the
    // text went modified or back, undo or redo became possible or not, or a
    // different file came up. MainWindow re-reads the state when it fires.
    public event EventHandler? EditStateChanged;

    public ViewerTextEditor()
    {
        IsReadOnly = true;
        WordWrap = true;
        ShowLineNumbers = false;
        // Wrapped text never needs the sideways bar, and leaving it on Auto
        // would flash it for a frame each time the width settles.
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        // No Background here: it follows IsReadOnly (OnPropertyChanged), and
        // the IsReadOnly above has already set it for reading.
        Padding = new Thickness(8, 6, 2, 6);
        // Monospace, as Notepad and VS Code both open a text file, with VS
        // Code's line spacing - see TextFontFamily.
        FontFamily = TextFontFamily;
        AllowDrop = false;

        Options.HighlightCurrentLine = false;
        // A view's height of room past the end, as VS Code has by default, so
        // the LAST line can be brought to the top of the view - which is where
        // RevealCaret puts a line it has to reach (see there).
        Options.AllowScrollBelowDocument = true;
        // AvalonEdit's own text drag stays off: it would offer a drop target a
        // move out of this document and drag text around inside it. A
        // selection is dragged out as a copy by ViewerTextEditor.DragOut.cs.
        Options.EnableTextDragDrop = false;
        Options.EnableEmailHyperlinks = false;
        // Links open on Ctrl+click only (AvalonEdit's default, kept): a plain
        // click is how a selection starts, and a URL in a log is not something
        // to be sent to the browser by a stray press.
        Options.RequireControlModifierForHyperlinkClick = true;

        // The selection draws in the tree's own selected-row colour, square,
        // with the text keeping its colour on top - so a selection in the
        // panel and a selection in the tree are visibly the same thing.
        TextArea.SelectionCornerRadius = 0;
        TextArea.SelectionBorder = null;
        TextArea.SelectionForeground = null;
        TextArea.SetResourceReference(EditingTextArea.SelectionBrushProperty, "TreeRowSelectedActiveBackground");

        // The file-name ink and the text view's own size, both by reference so
        // a colour change or a size step reaches the text without being told.
        // Its own size, not the tree's: see AppSettings.ViewerTextFontSize.
        SetResourceReference(ForegroundProperty, "FileNameForeground");
        SetResourceReference(FontSizeProperty, "ViewerTextFontSize");

        // The app's overlay scroll bar rather than Windows' own. The editor's
        // template names its ScrollViewer without a style, so an implicit one
        // here reaches it - the same template the tree and the menus use.
        if (Application.Current.TryFindResource("MinimalScrollViewerTemplate") is ControlTemplate scrollTemplate)
        {
            var scrollStyle = new Style(typeof(ScrollViewer));
            scrollStyle.Setters.Add(new Setter(TemplateProperty, scrollTemplate));
            Resources.Add(typeof(ScrollViewer), scrollStyle);
        }

        // Find works in this read-only view as well as it will in an editing
        // one: case, whole words, regex, next/previous, matches marked. The
        // panel does that work with nothing of its own on screen
        // (ViewerTextSearchPanel.xaml); what is seen is the app's find row,
        // bound to it in BindFindBar. Its labels still matter for the one
        // thing it shows by itself, the "no matches" tooltip.
        _search = SearchPanel.Install(this);
        _search.Localization = new SearchLabels();
        var searchLook = (ResourceDictionary)Application.LoadComponent(
            new Uri("/Edgetree;component/Controls/ViewerTextSearchPanel.xaml", UriKind.Relative));
        _search.Template = (ControlTemplate)searchLook["ViewerTextSearchPanelTemplate"];

        // EVERY MATCH IN THE SELECTION COLOUR (2026-10-11, on request), square
        // like the selection, where AvalonEdit's default is a rounded yellow:
        // a found word is marked the way a selected one is, as Notepad does,
        // instead of in a colour of its own. Bound rather than copied, so the
        // active / inactive swap of that brush (UpdateSelectionBrushForActivation)
        // reaches the marks too.
        //
        // That makes the current match - the selection, drawn over the marks -
        // the same colour as the rest, so it alone gets an outline in the
        // text's ink while the marks are up (_findOutline).
        BindingOperations.SetBinding(_search, SearchPanel.MarkerBrushProperty, MarkerBinding());
        _search.MarkerCornerRadius = 0;
        _findOutline = new Pen { Thickness = 1 };
        BindingOperations.SetBinding(_findOutline, Pen.BrushProperty,
            new Binding(nameof(Foreground)) { Source = this });

        TextArea.SelectionChanged += (_, _) => OnSelectionChangedForFind();
        // F3 in the text, taken here rather than left to the panel's own
        // binding, so it goes through DriveFind like the row's arrows do.
        //
        // Ctrl+B and Ctrl+I are 서식's 굵게 and 기울임꼴 (2026-10-13), the keys
        // every Markdown editor gives them - only while a Markdown file is
        // being edited. Ctrl+I is AvalonEdit's own indent-the-selection, which
        // is why it is taken here, before the editor's key bindings see it;
        // everywhere else (reading, a TXT) it is left to them as before.
        TextArea.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.F3 && FindIsOpen)
            {
                if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
                {
                    FindPreviousMatch();
                }
                else
                {
                    FindNextMatch();
                }
                e.Handled = true;
            }
            else if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control &&
                     e.Key is System.Windows.Input.Key.B or System.Windows.Input.Key.I &&
                     CanFormat)
            {
                ToggleWrap(e.Key == System.Windows.Input.Key.B ? "**" : "*");
                e.Handled = true;
            }
        };

        // A RIGHT-CLICK PUTS THE CARET WHERE IT LANDS (2026-10-13), unless it
        // lands inside the selection - Notepad's and VS Code's answer, and what
        // makes the menu's 서식 act on the word that was clicked rather than
        // wherever the caret had been left. It also takes the keyboard: the
        // menu's commands act on the text, and a menu hands the focus back to
        // where it found it when it closes.
        TextArea.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (GetPositionFromPoint(e.GetPosition(this)) is { } position &&
                !TextArea.Selection.Contains(Document.GetOffset(position.Location)))
            {
                TextArea.ClearSelection();
                TextArea.Caret.Position = position;
            }
            TextArea.Focus();
        };

        AttachDragOut();

        // REDO ON Ctrl+Shift+Z AS WELL AS Ctrl+Y (2026-10-13, on request).
        // AvalonEdit takes redo from WPF's ApplicationCommands.Redo, whose only
        // gesture is Ctrl+Y (read off a live editor); Ctrl+Shift+Z is the
        // other key VS Code, the browsers and most editors give it. Bound to
        // the same command, so it does exactly what Ctrl+Y does. Not
        // Ctrl+Alt+Z: that is AltGr+Z on many keyboards (ż on a Polish one),
        // and it would fire while typing.
        TextArea.InputBindings.Add(new System.Windows.Input.KeyBinding(
            System.Windows.Input.ApplicationCommands.Redo,
            System.Windows.Input.Key.Z,
            System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift));

        // The changed-line marks follow the text (ViewerTextEditor.ChangeMarks.cs).
        TextChanged += (_, _) => ScheduleChangeMarks();

        TextArea.Caret.PositionChanged += (_, _) => ScheduleRevealCaret();
    }

    // ----- Find marks give way to a selection (2026-10-11) --------------------
    //
    // A selection made by hand while find is open is shown ALONE: the marks
    // and the current match's outline go, so what is selected is not lost
    // among same-coloured matches. Stepping through the matches again - the
    // row's arrows, Enter or F3 - or changing the pattern or an option brings
    // the marks back, with the found match selected. Asked for once the marks
    // took the selection colour, the same day.
    //
    // Telling the two apart: a selection the find made happens either inside
    // DriveFind (a step, or OpenFind seeding the pattern) or while the
    // keyboard is in the find row (typing there searches live). Anything else
    // that leaves a non-empty selection is the hand. A caret move that only
    // clears the selection leaves the marks as they are.
    private readonly Pen _findOutline;
    private bool _findDriving;
    private bool _marksShown = true;

    private Binding MarkerBinding()
        => new(nameof(EditingTextArea.SelectionBrush)) { Source = TextArea };

    private void OnSelectionChangedForFind()
    {
        if (!FindIsOpen)
        {
            return;
        }

        if (_findDriving || !TextArea.IsKeyboardFocusWithin)
        {
            ShowFindMarks();
        }
        else if (!TextArea.Selection.IsEmpty)
        {
            HideFindMarks();
        }
    }

    private void ShowFindMarks()
    {
        if (!_marksShown)
        {
            _marksShown = true;
            BindingOperations.SetBinding(_search, SearchPanel.MarkerBrushProperty, MarkerBinding());
        }
        TextArea.SelectionBorder = FindIsOpen ? _findOutline : null;
    }

    // Transparent rather than taken away: the panel keeps its matches, so
    // bringing them back is a colour, not a search.
    private void HideFindMarks()
    {
        if (!_marksShown)
        {
            return;
        }

        _marksShown = false;
        BindingOperations.ClearBinding(_search, SearchPanel.MarkerBrushProperty);
        _search.MarkerBrush = Brushes.Transparent;
        TextArea.SelectionBorder = null;
    }

    private void DriveFind(Action step)
    {
        _findDriving = true;
        try
        {
            ShowFindMarks();
            step();
        }
        finally
        {
            _findDriving = false;
        }
    }

    // Every way find closes: the row's X or Esc, a new document, the panel
    // leaving the file. The outline goes with it, and the marks are put back
    // to shown for the next open.
    private void CloseSearch()
    {
        _search.Close();
        ShowFindMarks();
    }

    // ----- Bringing a far caret into view (2026-10-11) -------------------------
    //
    // AvalonEdit scrolls a caret into view by putting its line at the BOTTOM of
    // the view, measured against estimated heights - one row each - for every
    // line above it that has not been drawn yet. With wrapping, a line that is
    // really many rows tall is drawn as it comes into view and pushes the
    // target straight back out. Measured in a harness: a find landing on line
    // 106 of a split one-line file left lines 51-52 on screen, and every
    // further F3 crept one line closer - which is exactly how it was reported
    // ("엔터를 눌러도 한줄씩 스크롤"). Not the split file's own problem: 2,000
    // ordinary 1,200-character paragraphs did the same, finishing 23 lines
    // short of the last one.
    //
    // So once the caret has moved and the layout has settled, a caret still out
    // of view has its row put at the TOP of the view instead. Nothing above the
    // top has to be right for that, and the target's own line has just been
    // measured, so it lands where it says. A few rows of the same line are kept
    // above it for context - never rows of the line before, whose real height
    // is the thing nobody knows.
    //
    // It only ever answers the caret MOVING, so a wheel or a scroll bar drag -
    // which leave the caret where it is - is never fought; and a caret already
    // in view (a click, an arrow key) is left alone.
    private bool _revealPending;

    private void ScheduleRevealCaret()
    {
        if (_revealPending)
        {
            return;
        }

        _revealPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            _revealPending = false;
            RevealCaret();
        }));
    }

    private void RevealCaret()
    {
        var textView = TextArea.TextView;
        if (Document is null || !IsVisible)
        {
            return;
        }

        // A couple of passes at most: the first lands, the second only confirms
        // or nudges once the landing line has been drawn.
        for (int pass = 0; pass < 3; pass++)
        {
            textView.EnsureVisualLines();
            var caret = TextArea.Caret.Position;
            double rowTop = textView.GetVisualPosition(caret, VisualYPosition.LineTop).Y;
            double rowBottom = textView.GetVisualPosition(caret, VisualYPosition.LineBottom).Y;
            double viewTop = textView.VerticalOffset;
            if (rowTop >= viewTop && rowBottom <= viewTop + textView.ActualHeight)
            {
                return;
            }

            double lineTop = textView.GetVisualTopByDocumentLine(caret.Line);
            double context = Math.Min(rowTop - lineTop, 2 * textView.DefaultLineHeight);
            ScrollToVerticalOffset(Math.Max(0, rowTop - context));
            UpdateLayout();
        }
    }

    // The find row's controls drive the panel through its own properties.
    //
    // EVERY ONE OF THE FOUR KEEPS AN ExceptionValidationRule. The panel
    // rebuilds its search from inside each property's change callback, and a
    // pattern that does not compile as a regex throws out of that callback -
    // so out of the property SETTER. The pattern box was known for it (the
    // panel's own template carries the rule for that reason). The options do
    // it too, measured in a harness 2026-10-11: type "(" with regex off, which
    // is a fine literal, then switch regex on, and UseRegex's setter throws.
    // The rule turns that into an error outline on the control that caused it
    // rather than relying on the binding engine to swallow the exception.
    public void BindFindBar(TextBox box, ToggleButton matchCase, ToggleButton wholeWords, ToggleButton regex)
    {
        box.SetBinding(TextBox.TextProperty, SearchBinding(nameof(SearchPanel.SearchPattern), live: true));
        matchCase.SetBinding(ToggleButton.IsCheckedProperty, SearchBinding(nameof(SearchPanel.MatchCase)));
        wholeWords.SetBinding(ToggleButton.IsCheckedProperty, SearchBinding(nameof(SearchPanel.WholeWords)));
        regex.SetBinding(ToggleButton.IsCheckedProperty, SearchBinding(nameof(SearchPanel.UseRegex)));

        // A new pattern or option is a new search: the marks come back even
        // if the selection happens not to move (see ShowFindMarks).
        box.TextChanged += (_, _) => ShowFindMarksIfOpen();
        foreach (var option in new[] { matchCase, wholeWords, regex })
        {
            option.Click += (_, _) => ShowFindMarksIfOpen();
        }
    }

    private void ShowFindMarksIfOpen()
    {
        if (FindIsOpen)
        {
            ShowFindMarks();
        }
    }

    private Binding SearchBinding(string property, bool live = false)
    {
        var binding = new Binding(property) { Source = _search };
        if (live)
        {
            binding.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
        }
        binding.ValidationRules.Add(new ExceptionValidationRule());
        return binding;
    }

    // What Ctrl+F did inside the panel's own command: open, and start from
    // the selection when it is one line of text.
    public void OpenFind() => DriveFind(() =>
    {
        _search.Open();
        ShowFindMarks();
        if (!TextArea.Selection.IsEmpty && !TextArea.Selection.IsMultiline)
        {
            _search.SearchPattern = TextArea.Selection.GetText();
        }
    });

    public void CloseFind() => CloseSearch();

    public bool FindIsOpen => !_search.IsClosed;

    public void FindNextMatch() => DriveFind(_search.FindNext);

    public void FindPreviousMatch() => DriveFind(_search.FindPrevious);

    // WORKER THREAD. Reads the file and builds the document there, then hands
    // ownership back as nobody's - a TextDocument belongs to the thread that
    // made it, and the UI thread takes it over in Show. Building the line
    // index of a ten-megabyte file is the one part of this worth keeping off
    // the UI thread.
    public static LoadedText Load(string path, CancellationToken cancellation)
    {
        var file = TextFileReader.Read(path, cancellation);
        cancellation.ThrowIfCancellationRequested();

        var document = new TextDocument(file.Text);
        document.SetOwnerThread(null);
        return new LoadedText(
            document,
            file.Truncated,
            file.FileLength,
            file.Encoding,
            file.HasBom,
            file.LastWriteUtc,
            file.LongLinesSplit)
        {
            // Only a text that can be edited can ever differ from its file.
            LineHashes = file.Truncated || file.LongLinesSplit ? null : HashLines(file.Text),
        };
    }

    // A file just read, shown read-only from its top.
    public void Show(string path, LoadedText loaded, bool markdown, bool light)
    {
        loaded.Document.SetOwnerThread(Thread.CurrentThread);
        var session = new TextSession(path, loaded.Document)
        {
            Markdown = markdown,
            Truncated = loaded.Truncated,
            LongLinesSplit = loaded.LongLinesSplit,
            Encoding = loaded.Encoding,
            HasBom = loaded.HasBom,
            StampLength = loaded.FileLength,
            StampLastWriteUtc = loaded.LastWriteUtc,
            Baseline = loaded.LineHashes,
        };
        Attach(session, light, editing: false);
        TextArea.Caret.Offset = 0;
        ScrollToHome();
    }

    public bool HasUnsaved(string path) => _unsaved.ContainsKey(path);

    // Every file with edits not yet written: the one on screen if it has
    // any, and every one waiting in _unsaved. What the tree marks.
    public IEnumerable<string> UnsavedPaths
    {
        get
        {
            if (_session is { IsModified: true } current)
            {
                yield return current.Path;
            }
            foreach (string path in _unsaved.Keys)
            {
                yield return path;
            }
        }
    }

    // An edited file the panel has come back to: straight into editing, with
    // the caret and the scroll where they were left. False when there is no
    // such session (it was saved from elsewhere in between, say).
    public bool Restore(string path, bool light)
    {
        if (!_unsaved.Remove(path, out var session))
        {
            return false;
        }

        Attach(session, light, editing: true);
        TextArea.Caret.Offset = Math.Min(session.CaretOffset, session.Document.TextLength);
        // After the layout the new document is about to get, and after the
        // caret's own reveal (queued by the caret move above at the same
        // priority), so this is the position that stays.
        double offset = session.VerticalOffset;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (ReferenceEquals(_session, session))
            {
                ScrollToVerticalOffset(offset);
            }
        }));
        return true;
    }

    private void Attach(TextSession session, bool light, bool editing)
    {
        if (_session is not null)
        {
            Release();
        }

        CloseSearch();
        _session = session;
        _markdown = session.Markdown;
        _light = light;
        // Highlighting off while the document changes hands, so the old file's
        // rules are never run over the new file's lines.
        SyntaxHighlighting = null;
        Document = session.Document;
        session.Document.UndoStack.PropertyChanged += UndoStack_PropertyChanged;
        IsReadOnly = !editing;
        ApplyHighlighting();
        // A kept session brings its marks with it; they are checked again on
        // the next pause anyway.
        _lineNumbers?.InvalidateVisual();
        ScheduleChangeMarks();
        EditStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // The panel has moved on. Unedited text is dropped - the view is offered
    // again on the next visit rather than kept, so holding it would only be
    // memory spent on something no longer on screen. EDITED text is kept in
    // _unsaved, never dropped: see there.
    public void Release()
    {
        CloseSearch();
        if (_session is { } session)
        {
            session.Document.UndoStack.PropertyChanged -= UndoStack_PropertyChanged;
            if (session.IsModified)
            {
                session.CaretOffset = TextArea.Caret.Offset;
                session.VerticalOffset = VerticalOffset;
                _unsaved[session.Path] = session;
            }
            _session = null;
        }

        // Cleared with the document, or a font step or a theme flip would go on
        // rebuilding Markdown rules for an empty page.
        _markdown = false;
        SyntaxHighlighting = null;
        IsReadOnly = true;
        Document = new TextDocument();
        EditStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UndoStack_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UndoStack.IsOriginalFile) && _session is { } session)
        {
            NoteModifiedChanged(session);
            // Back at the saved text, the marks go at once (ScheduleChangeMarks).
            ScheduleChangeMarks();
        }

        if (e.PropertyName is nameof(UndoStack.IsOriginalFile)
                           or nameof(UndoStack.CanUndo)
                           or nameof(UndoStack.CanRedo))
        {
            EditStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void NoteModifiedChanged(TextSession session)
    {
        if (session.IsModified)
        {
            _cleaned.Remove(session.Path);
        }
        else
        {
            _cleaned.Add(session.Path);
            session.PersistedVersion = null;
        }
    }

    // ----- The disk copy (2026-10-11) ------------------------------------------
    //
    // MainWindow keeps an UnsavedTextStore equal to what this holds:
    // TakeChangedUnsaved after each pause in typing, for every edited file
    // whose text moved since it last asked, and TakeCleanedPaths on every
    // EditStateChanged, for every file whose edits went away - at once, since
    // the tree's marks read the store too (see MainWindow's
    // DropCleanedUnsavedText). Both are "since the last call", so nothing is
    // written twice and a file nobody touched costs nothing. Every add to
    // _cleaned is followed by EditStateChanged; keep it so.
    public List<UnsavedSnapshot> TakeChangedUnsaved()
    {
        var changed = new List<UnsavedSnapshot>();
        foreach (var session in EditedSessions())
        {
            var now = session.Document.Version;
            if (session.PersistedVersion is { } written && now is not null &&
                now.BelongsToSameDocumentAs(written) && now.CompareAge(written) == 0)
            {
                continue;
            }

            session.PersistedVersion = now;
            changed.Add(new UnsavedSnapshot(
                session.Path,
                session.Document.Text,
                session.Encoding.CodePage,
                session.HasBom,
                session.StampLength,
                session.StampLastWriteUtc,
                session.Markdown));
        }
        return changed;
    }

    public List<string> TakeCleanedPaths()
    {
        var cleaned = _cleaned.ToList();
        _cleaned.Clear();
        return cleaned;
    }

    // Whether this process holds the file's text at all - on screen or kept.
    // A file the disk copy lists but this does not is one from a previous
    // session that has not been opened yet.
    public bool HoldsText(string path)
        => _unsaved.ContainsKey(path) ||
           (_session is not null && string.Equals(_session.Path, path, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<TextSession> EditedSessions()
    {
        if (_session is { IsModified: true } current)
        {
            yield return current;
        }
        foreach (var kept in _unsaved.Values)
        {
            yield return kept;
        }
    }

    // A file edited in an earlier session, opened from the disk copy: straight
    // into editing, from the top. The undo history did not survive the
    // restart, so the text is loaded as it is and marked as not saved -
    // DiscardOriginalFileMarker, so it counts as edited until saved or
    // reloaded, with nothing for 실행 취소 to go back to.
    public void RestoreFromStore(
        string path, string text, Encoding encoding, bool hasBom,
        long stampLength, DateTime stampLastWriteUtc, bool markdown, bool light)
    {
        var document = new TextDocument(text);
        document.UndoStack.DiscardOriginalFileMarker();
        var session = new TextSession(path, document)
        {
            Markdown = markdown,
            Encoding = encoding,
            HasBom = hasBom,
            StampLength = stampLength,
            StampLastWriteUtc = stampLastWriteUtc,
            PersistedVersion = document.Version,
        };
        // The text from the disk copy against the file it was edited from.
        SetBaselineFromDisk(session);
        Attach(session, light, editing: true);
        TextArea.Caret.Offset = 0;
        ScrollToHome();
    }

    // The app renamed or moved a file, or a folder holding files with edits:
    // their sessions take the new paths. A folder matches every path under it.
    public void MovePath(string oldPath, string newPath)
    {
        foreach (var session in AllSessions())
        {
            if (UnsavedTextStore.MovedPath(session.Path, oldPath, newPath) is { } moved)
            {
                bool kept = _unsaved.Remove(session.Path);
                session.Path = moved;
                if (kept)
                {
                    _unsaved[moved] = session;
                }
            }
        }
    }

    private List<TextSession> AllSessions()
    {
        var all = _unsaved.Values.ToList();
        if (_session is not null && !all.Contains(_session))
        {
            all.Add(_session);
        }
        return all;
    }

    // 종료 answered with 아니요: every edit in this process is let go, marked
    // saved so the closing flush finds nothing to write. The disk copies are
    // MainWindow's to delete.
    public void DiscardAllUnsaved()
    {
        foreach (var session in AllSessions())
        {
            session.Document.UndoStack.MarkAsOriginalFile();
        }
        _unsaved.Clear();
        _cleaned.Clear();
        EditStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ----- Editing (2026-10-11) ---------------------------------------------
    //
    // Off until 편집 is pressed, and only for a file shown whole: a head-only
    // or split text is not the file (LoadedText.CanEdit). Editing lasts until
    // 편집 취소 or the end of the visit - the panel moving to another file ends
    // it, and an edited file comes back already editing (Restore).
    public bool CanEdit => _session is { CanEdit: true };

    // Why CanEdit is false, for the tooltip on the disabled 편집.
    public bool IsHeadOnly => _session is { Truncated: true };

    public bool IsEditing => _session is not null && !IsReadOnly;

    public bool HasUnsavedEdits => _session is { IsModified: true };

    public bool CanUndoEdit => IsEditing && Document.UndoStack.CanUndo;

    public bool CanRedoEdit => IsEditing && Document.UndoStack.CanRedo;

    public void BeginEdit()
    {
        if (_session is not { CanEdit: true } || !IsReadOnly)
        {
            return;
        }

        IsReadOnly = false;
        EditStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // 편집 취소 with nothing to drop. With unsaved edits it goes through
    // Reload(..., endEditing: true) instead.
    public void EndEdit()
    {
        if (_session is null || IsReadOnly)
        {
            return;
        }

        IsReadOnly = true;
        EditStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // Null when there is nothing to write.
    public SaveRequest? CreateSaveRequest()
        => _session is { } session && !IsReadOnly ? RequestFor(session) : null;

    // Any file with edits this process holds - on screen or kept - for the
    // save-all that 종료 offers. Null if it has none.
    public SaveRequest? CreateSaveRequest(string path)
    {
        if (_unsaved.TryGetValue(path, out var kept))
        {
            return RequestFor(kept);
        }
        return _session is { IsModified: true } current &&
               string.Equals(current.Path, path, StringComparison.OrdinalIgnoreCase)
            ? RequestFor(current)
            : null;
    }

    private static SaveRequest RequestFor(TextSession session)
    {
        return new SaveRequest(
            session.Path,
            session.Document.Text,
            session.Encoding,
            session.HasBom,
            session.StampLength,
            session.StampLastWriteUtc)
        {
            Session = session,
            Version = session.Document.Version,
        };
    }

    // The write went through. The session may no longer be on screen - the
    // panel can move on while a save to a slow share is out - so it is the
    // request's session that is updated, wherever it now is.
    public void CompleteSave(SaveRequest request, TextFileWriter.Result result)
    {
        var session = request.Session;
        session.Encoding = request.Encoding;
        session.HasBom = request.HasBom;
        session.StampLength = result.FileLength;
        session.StampLastWriteUtc = result.LastWriteUtc;
        // What the file now holds is what was written - also when more was
        // typed while the write was out, which then shows as changed.
        SetBaseline(session, request.Text);

        // Marked as saved only if the text is still the text that was written.
        // Anything typed while the write was out is not on disk, and the file
        // has to go on saying so.
        if (request.Version is { } written &&
            session.Document.Version is { } now &&
            now.BelongsToSameDocumentAs(written) &&
            now.CompareAge(written) == 0)
        {
            session.Document.UndoStack.MarkAsOriginalFile();
        }

        // Told here as well as by the undo stack's event, which only reaches
        // the session on screen.
        if (!session.IsModified)
        {
            NoteModifiedChanged(session);
        }

        if (!session.IsModified &&
            _unsaved.TryGetValue(session.Path, out var waiting) &&
            ReferenceEquals(waiting, session))
        {
            _unsaved.Remove(session.Path);
        }
        EditStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // 다른 이름으로 저장 went through (2026-10-13). The text BECOMES the new
    // file, as it does in Notepad and VS Code: it stays on screen, undo
    // history and all, under the new name, and the file it came from is left
    // as it stands on disk. Edits it had are the new file's now, so the old
    // path counts as cleaned and its disk copy goes. Same session as
    // CompleteSave's - the panel may have moved on while the write was out.
    public void CompleteSaveAs(SaveRequest request, string newPath, TextFileWriter.Result result, bool markdown)
    {
        var session = request.Session;
        string oldPath = session.Path;
        bool kept = _unsaved.TryGetValue(oldPath, out var waiting) && ReferenceEquals(waiting, session);
        if (kept)
        {
            _unsaved.Remove(oldPath);
        }
        if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            _cleaned.Add(oldPath);
        }

        session.Path = newPath;
        session.Markdown = markdown;
        // The disk copy is kept by path, and this one has never been written
        // under the new one.
        session.PersistedVersion = null;
        if (kept)
        {
            _unsaved[newPath] = session;
        }

        if (ReferenceEquals(_session, session) && _markdown != markdown)
        {
            _markdown = markdown;
            ApplyHighlighting();
        }
        CompleteSave(request, result);
    }

    // A file 다른 이름으로 저장 has just written over: edits this process kept
    // for it were for a text that is no longer there.
    public void DropKept(string path)
    {
        if (!_unsaved.Remove(path, out var kept))
        {
            return;
        }

        kept.Document.UndoStack.MarkAsOriginalFile();
        _cleaned.Add(path);
        EditStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // The file on screen, for 다른 이름으로 저장's dialog.
    public string? ShownPath => _session?.Path;

    // 다시 불러오기: the file as it now is on disk, in place of the edits.
    // ONE UNDO STEP rather than a fresh document, so a press by mistake is
    // taken back with 실행 취소 like any other change - which is what lets the
    // button drop edits without asking first. Afterwards the text counts as
    // saved: it is what the file holds.
    //
    // endEditing is 편집 취소 with edits to drop (author's choice, 2026-10-11:
    // drop them, do not ask): the same replace, then back to reading. The step
    // is still on the undo stack, so 편집 and then 실행 취소 bring the edits
    // back for as long as the panel stays on the file.
    //
    // A file that has meanwhile grown past what can be edited (over the size
    // limit, or with a line too long to show whole) comes back as a fresh
    // read-only view instead: its text on screen would no longer be the file.
    public void Reload(string path, LoadedText loaded, bool light, bool endEditing = false)
    {
        loaded.Document.SetOwnerThread(Thread.CurrentThread);
        if (_session is not { } session ||
            !string.Equals(session.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!loaded.CanEdit)
        {
            Discard();
            Show(path, loaded, session.Markdown, light);
            return;
        }

        string text = loaded.Document.Text;
        int caret = TextArea.Caret.Offset;
        double offset = VerticalOffset;
        if (!string.Equals(session.Document.Text, text, StringComparison.Ordinal))
        {
            session.Document.Replace(0, session.Document.TextLength, text);
        }
        // Before the marker: its change clears the marks against this.
        session.BaselineGeneration++;
        session.Baseline = loaded.LineHashes;
        session.Document.UndoStack.MarkAsOriginalFile();
        session.Encoding = loaded.Encoding;
        session.HasBom = loaded.HasBom;
        session.StampLength = loaded.FileLength;
        session.StampLastWriteUtc = loaded.LastWriteUtc;
        if (endEditing)
        {
            IsReadOnly = true;
        }

        TextArea.Caret.Offset = Math.Min(caret, session.Document.TextLength);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (ReferenceEquals(_session, session))
            {
                ScrollToVerticalOffset(offset);
            }
        }));
        EditStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // The session on screen, dropped without being kept - only for the reload
    // above, which is the one request to throw edits away.
    private void Discard()
    {
        if (_session is { } session)
        {
            session.Document.UndoStack.PropertyChanged -= UndoStack_PropertyChanged;
            _session = null;
            _cleaned.Add(session.Path);
        }
        Release();
    }

    public void SetTheme(bool light)
    {
        if (_light == light)
        {
            return;
        }

        _light = light;
        ApplyHighlighting();
        // The changed-line colours are per theme.
        _lineNumbers?.InvalidateVisual();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // NO BLINKING CARET WHILE READING (2026-10-12, on request): a caret
        // says "type here", and reading cannot. Made transparent rather than
        // hidden - Caret.Hide is undone by the text area on its next focus -
        // and still there underneath, so arrows and PgDn move through the
        // text and a find lands where it says. Editing gives it its ink back
        // (null = the text area's default).
        //
        // AND THE GROUND DIFFERS BY A SHADE BETWEEN THE TWO (2026-10-13, on
        // request), so reading and editing can be told apart at a glance and
        // not only by the bar. The shade is READING's: tried on editing first,
        // the same day, and turned round on seeing both - editing gets the
        // panel's plain ground. A fill laid over the panel's background rather
        // than a colour of its own: ViewerTextReadingBackground is derived
        // against that background in MainWindow's ApplyEdgeInkOverrides, so a
        // panel colour the user picks keeps the difference.
        if (e.Property == IsReadOnlyProperty)
        {
            TextArea.Caret.CaretBrush = IsReadOnly ? Brushes.Transparent : null;
            if (IsReadOnly)
            {
                SetResourceReference(BackgroundProperty, "ViewerTextReadingBackground");
            }
            else
            {
                Background = Brushes.Transparent;
            }
            return;
        }

        if (e.Property != FontSizeProperty)
        {
            return;
        }

        // The Markdown definition is not rebuilt here any more: it held heading
        // sizes worked out from this until headings went to the text's own
        // size (2026-10-13).
        SizeLineNumbers();
    }

    // ----- Line numbers (2026-10-11) -------------------------------------------
    //
    // Smaller than the text and in a quieter ink, on request, with a gap
    // after them instead of AvalonEdit's dotted rule. Not ShowLineNumbers:
    // that adds the stock margin at the text's own size, and binds its own
    // brush. The margin is ours (SmallLineNumberMargin) so the smaller numbers
    // can sit on the text's baseline rather than float at its top.
    private SmallLineNumberMargin? _lineNumbers;
    private Border? _lineNumberGap;

    public void SetLineNumbers(bool show)
    {
        if (show == _lineNumbers is not null)
        {
            return;
        }

        if (show)
        {
            _lineNumbers = new SmallLineNumberMargin(this);
            // Derived against the panel's own ground in MainWindow's
            // ApplyEdgeInkOverrides, a step under the text's ink.
            _lineNumbers.SetResourceReference(Control.ForegroundProperty, "ViewerTextLineNumberForeground");
            _lineNumberGap = new Border();
            SizeLineNumbers();
            TextArea.LeftMargins.Insert(0, _lineNumbers);
            TextArea.LeftMargins.Insert(1, _lineNumberGap);
            // Marks are not worked out while there are no numbers to carry them.
            ScheduleChangeMarks();
        }
        else
        {
            TextArea.LeftMargins.Remove(_lineNumbers);
            TextArea.LeftMargins.Remove(_lineNumberGap!);
            _lineNumbers = null;
            _lineNumberGap = null;
        }
    }

    // 85% of the text, whole points, and a gap of most of an em - both from
    // the text's size, so a Ctrl+wheel step moves the margin with it.
    private void SizeLineNumbers()
    {
        if (_lineNumbers is null || _lineNumberGap is null)
        {
            return;
        }

        _lineNumbers.SetValue(TextBlock.FontSizeProperty, Math.Max(6.0, Math.Round(FontSize * 0.85)));
        _lineNumberGap.Width = Math.Max(6.0, Math.Round(FontSize * 0.8));
    }

    // AvalonEdit's margin with one change: each number is placed on the
    // BASELINE of its line's first row. The stock margin aligns the numbers'
    // top with the text's top, which is right only while both are the same
    // size - smaller numbers would ride high (and beside the larger Markdown
    // headings drawn until 2026-10-13, at their top edge). A wrapped line keeps one
    // number, on its first row, as the stock margin does. Since 2026-10-13 a
    // number can also take a changed line's colour (the owner's
    // LineNumberBrush, ViewerTextEditor.ChangeMarks.cs).
    private sealed class SmallLineNumberMargin : LineNumberMargin
    {
        private readonly ViewerTextEditor _owner;

        public SmallLineNumberMargin(ViewerTextEditor owner) => _owner = owner;

        protected override void OnRender(DrawingContext drawingContext)
        {
            var textView = TextView;
            if (textView is null || !textView.VisualLinesValid)
            {
                return;
            }

            var foreground = (System.Windows.Media.Brush)GetValue(Control.ForegroundProperty);
            // The stock margin builds these through AvalonEdit's internal
            // TextFormatterFactory; this is the same text with the same
            // formatting mode and DPI, built directly.
            var mode = TextOptions.GetTextFormattingMode(this);
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double right = RenderSize.Width;
            foreach (VisualLine line in textView.VisualLines)
            {
                var text = new FormattedText(
                    line.FirstDocumentLine.LineNumber.ToString(CultureInfo.CurrentCulture),
                    CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    typeface, emSize, _owner.LineNumberBrush(line.FirstDocumentLine, foreground),
                    null, mode, pixelsPerDip);
                double baseline = line.GetTextLineVisualYPosition(line.TextLines[0], VisualYPosition.Baseline);
                drawingContext.DrawText(
                    text,
                    new System.Windows.Point(right - text.Width, baseline - text.Baseline - textView.VerticalOffset));
            }
        }
    }

    // The find marks are not coloured here: they follow the selection colour
    // through a binding (see the constructor), and a value written here would
    // replace that binding.
    private void ApplyHighlighting()
        => SyntaxHighlighting = _markdown ? MarkdownDefinition(_light) : null;

    // Token colours from VS Code's default themes, Dark 2026 / Light 2026,
    // for the same Markdown scopes: markup.heading, markup.inline.raw,
    // markup.quote, punctuation.definition.list.begin.markdown and
    // string.other.link - read out of the theme files of the installed VS
    // Code (2026-10-13, on request, the text set beside VS Code's). They were
    // Dark+ / Light+ before, which VS Code no longer opens with: code orange
    // and list marks blue there, where its default now has them the other
    // way round.
    //
    // HEADINGS AT THE TEXT'S OWN SIZE, bold and coloured, as VS Code's editor
    // draws them (the same day, on request, beside it). They were drawn at
    // VS Code's PREVIEW ratios (2em down to .85em) since 2026-10-11, to make a
    // long outline readable at a glance; next to the editor the sizes read as
    // this view's own invention.
    //
    // BOLD AND ITALIC IN VS CODE'S markup.bold / markup.italic COLOUR, #C9D1D9
    // / #1F2328 - its own, a shade brighter than its editor text (#BBBEBF in
    // Dark 2026). They first took this view's own text ink instead, to follow
    // the palette, and beside VS Code a bold lead-in read a shade off (the
    // same day, on report). Either way they stand apart from a quote's green.
    private static IHighlightingDefinition MarkdownDefinition(bool light)
    {
        var heading = light ? Color.FromRgb(0x05, 0x50, 0xAE) : Color.FromRgb(0x79, 0xC0, 0xFF);
        var code = light ? Color.FromRgb(0x05, 0x50, 0xAE) : Color.FromRgb(0x79, 0xC0, 0xFF);
        var quote = light ? Color.FromRgb(0x11, 0x63, 0x29) : Color.FromRgb(0x7E, 0xE7, 0x87);
        var list = light ? Color.FromRgb(0x95, 0x38, 0x00) : Color.FromRgb(0xFF, 0xA6, 0x57);
        var link = light ? Color.FromRgb(0x0A, 0x30, 0x69) : Color.FromRgb(0xA5, 0xD6, 0xFF);
        var strong = light ? Color.FromRgb(0x1F, 0x23, 0x28) : Color.FromRgb(0xC9, 0xD1, 0xD9);

        XshdSyntaxDefinition xshd;
        var resource = Application.GetResourceStream(
            new Uri("pack://application:,,,/Edgetree;component/Resources/Markdown.xshd"));
        using (var stream = resource.Stream)
        using (var reader = XmlReader.Create(stream))
        {
            xshd = HighlightingLoader.LoadXshd(reader);
        }

        foreach (var color in xshd.Elements.OfType<XshdColor>())
        {
            switch (color.Name)
            {
                case { } name when name.StartsWith("Heading", StringComparison.Ordinal):
                    color.Foreground = new SimpleHighlightingBrush(heading);
                    break;
                case "Code":
                    color.Foreground = new SimpleHighlightingBrush(code);
                    break;
                case "BlockQuote":
                    color.Foreground = new SimpleHighlightingBrush(quote);
                    break;
                case "StrongEmphasis" or "Emphasis":
                    color.Foreground = new SimpleHighlightingBrush(strong);
                    break;
                case "ListMarker":
                    color.Foreground = new SimpleHighlightingBrush(list);
                    break;
                // A link's words; its target is LinkTarget, underlined in the
                // text's own colour (Markdown.xshd).
                case "LinkText":
                    color.Foreground = new SimpleHighlightingBrush(link);
                    break;
            }
        }

        return HighlightingLoader.Load(xshd, HighlightingManager.Instance);
    }

    private sealed class SearchLabels : ICSharpCode.AvalonEdit.Search.Localization
    {
        public override string MatchCaseText => Strings.TextFindMatchCase;
        public override string MatchWholeWordsText => Strings.TextFindWholeWords;
        public override string UseRegexText => Strings.TextFindRegex;
        public override string FindNextText => Strings.TextFindNext;
        public override string FindPreviousText => Strings.TextFindPrevious;
        public override string ErrorText => Strings.TextFindError;
        public override string NoMatchesFoundText => Strings.TextFindNoMatches;
    }
}
