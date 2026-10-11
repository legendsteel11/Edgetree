using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using SidebarExplorer.App.Services;
// The project also references WinForms, whose namespaces name the same
// types.
using Application = System.Windows.Application;
using Binding = System.Windows.Data.Binding;
using TextBox = System.Windows.Controls.TextBox;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
// Inside a TextEditor, TextArea names the instance property, not the type.
using EditingTextArea = ICSharpCode.AvalonEdit.Editing.TextArea;

namespace SidebarExplorer.App;

// The panel's text view: a TXT or MD file, read-only, wrapped at the panel's
// width (2026-10-11, the first half of the TXT/MD round).
//
// WHY AVALONEDIT AND NOT A WPF TEXTBOX. A TextBox lays out the whole text, so
// a long file is slow to scroll and select in proportion to its length -
// the sluggishness this round started from. AvalonEdit builds only the lines
// on screen, so the cost stays the size of the panel. It is the project's
// first package dependency, accepted for that reason (2026-09-28).
//
// EVERY REFERENCE TO AN AVALONEDIT TYPE LIVES IN THIS FILE. MainWindow holds
// the editor as a plain FrameworkElement and reaches it only through methods
// that run after the first 보기 press, so a session that never opens a text
// file never loads the assembly. See MainWindow's ViewerText section.
public sealed class ViewerTextEditor : TextEditor
{
    // What the worker hands back: the document, built off the UI thread, and
    // what was learned about the file on the way.
    public sealed record LoadedText(
        TextDocument Document,
        bool Truncated,
        long FileLength,
        string EncodingName);

    private readonly SearchPanel _search;
    private bool _markdown;
    private bool _light;

    public ViewerTextEditor()
    {
        IsReadOnly = true;
        WordWrap = true;
        ShowLineNumbers = false;
        // Wrapped text never needs the sideways bar, and leaving it on Auto
        // would flash it for a frame each time the width settles.
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Background = Brushes.Transparent;
        Padding = new Thickness(8, 6, 2, 6);
        // Monospace, as Notepad and VS Code both open a text file. WPF's own
        // font fallback supplies Hangul and the other scripts Consolas lacks.
        FontFamily = new FontFamily("Consolas");
        AllowDrop = false;

        Options.HighlightCurrentLine = false;
        Options.AllowScrollBelowDocument = false;
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
    public void OpenFind()
    {
        _search.Open();
        if (!TextArea.Selection.IsEmpty && !TextArea.Selection.IsMultiline)
        {
            _search.SearchPattern = TextArea.Selection.GetText();
        }
    }

    public void CloseFind() => _search.Close();

    public bool FindIsOpen => !_search.IsClosed;

    public void FindNextMatch() => _search.FindNext();

    public void FindPreviousMatch() => _search.FindPrevious();

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
            file.HasBom ? file.Encoding.WebName + " BOM" : file.Encoding.WebName);
    }

    public void Show(LoadedText loaded, bool markdown, bool light)
    {
        loaded.Document.SetOwnerThread(Thread.CurrentThread);
        _search.Close();
        _markdown = markdown;
        _light = light;
        // Highlighting off while the document changes hands, so the old file's
        // rules are never run over the new file's lines.
        SyntaxHighlighting = null;
        Document = loaded.Document;
        ApplyHighlighting();
        TextArea.Caret.Offset = 0;
        ScrollToHome();
    }

    // The document goes when the panel moves on. The view is offered again
    // on the next text file rather than kept, so holding the last file's text
    // would only be memory spent on something no longer on screen.
    public void Release()
    {
        _search.Close();
        // Cleared with the document, or a font step or a theme flip would go on
        // rebuilding Markdown rules for an empty page.
        _markdown = false;
        SyntaxHighlighting = null;
        Document = new TextDocument();
    }

    public void SetTheme(bool light)
    {
        if (_light == light)
        {
            return;
        }

        _light = light;
        ApplyHighlighting();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Heading sizes are a ratio of this, written into the definition when
        // it is built - so a font step rebuilds it.
        if (e.Property == FontSizeProperty && _markdown)
        {
            ApplyHighlighting();
        }
    }

    private void ApplyHighlighting()
    {
        SyntaxHighlighting = _markdown ? MarkdownDefinition(_light, FontSize) : null;
        _search.MarkerBrush = new SolidColorBrush(_light
            ? Color.FromArgb(0x80, 0xF8, 0xC9, 0x5A)
            : Color.FromArgb(0x70, 0xB8, 0x86, 0x0B));
    }

    // VS Code's preview ratios for h1..h6 (2em, 1.5em, 1.25em, 1em, .875em,
    // .85em). The source is what is on screen here, not a rendering of it, but
    // the sizes are what make a long document's outline readable at a glance -
    // which is the reason a sized mode was chosen over the plain one.
    private static readonly double[] HeadingScale = { 2.0, 1.5, 1.25, 1.0, 0.875, 0.85 };

    // Token colours from VS Code's Dark+ / Light+ for the same Markdown scopes
    // (heading, inline raw, quote, list punctuation), with links in each
    // theme's link colour. A judgement to be confirmed on screen.
    private static IHighlightingDefinition MarkdownDefinition(bool light, double baseSize)
    {
        var heading = light ? Color.FromRgb(0x80, 0x00, 0x00) : Color.FromRgb(0x56, 0x9C, 0xD6);
        var code = light ? Color.FromRgb(0xA3, 0x15, 0x15) : Color.FromRgb(0xCE, 0x91, 0x78);
        var quote = light ? Color.FromRgb(0x04, 0x51, 0xA5) : Color.FromRgb(0x6A, 0x99, 0x55);
        var list = light ? Color.FromRgb(0x04, 0x51, 0xA5) : Color.FromRgb(0x67, 0x96, 0xE6);
        var link = light ? Color.FromRgb(0x00, 0x6A, 0xB1) : Color.FromRgb(0x37, 0x94, 0xFF);

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
                    int level = name[^1] - '1';
                    color.Foreground = new SimpleHighlightingBrush(heading);
                    color.FontSize = (int)Math.Round(baseSize * HeadingScale[level]);
                    break;
                case "Code":
                    color.Foreground = new SimpleHighlightingBrush(code);
                    break;
                case "BlockQuote":
                    color.Foreground = new SimpleHighlightingBrush(quote);
                    break;
                case "ListMarker":
                    color.Foreground = new SimpleHighlightingBrush(list);
                    break;
                case "Link":
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
