using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using SidebarExplorer.App.Services;
// The project also references WinForms, whose namespaces name the same
// types; these are the WPF ones.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using PrintDialog = System.Windows.Controls.PrintDialog;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;
using Point = System.Windows.Point;

namespace SidebarExplorer.App;

// PRINTING A TEXT (2026-10-13, on request): a preview of the pages
// (BuildPreview, shown in PrintPreviewWindow), then Windows' print dialog,
// then the text as the panel has it - unsaved edits included - on paper.
// The file name is not put into the PDF printer's save box: Microsoft Print
// to PDF ignores the job name there, and the one way round it
// (StartXpsPrintJob with an output path) is an API Microsoft lists as
// unsupported - set aside as not worth that risk (2026-10-13). The app does
// it rather than handing the file to the shell: a .txt's print verb is
// Notepad's /p (the file on disk, not the edits), and a .md has none at all.
// Pictures go to the shell's own photo printing window instead (MainWindow).
//
// ALWAYS BLACK ON WHITE. A dark theme's inks are for a dark ground, so
// Markdown is printed with the LIGHT theme's definition whatever is on
// screen, at the print size - headings keep their sizes. AvalonEdit's own
// DocumentPrinter carries colour and weight but not a highlighting colour's
// font size (checked 2026-10-13: a "# Title" run came out at body size), so
// the runs are built here from the highlighter's sections.
//
// The page is Notepad's: 0.75" sides, 1" top and bottom, the file name in
// the top margin and "page / pages" in the bottom one. Lines wrap at the
// page width; line numbers are not printed.
public sealed partial class ViewerTextEditor
{
    // 10 pt.
    private const double PrintFontSize = 10 * 96.0 / 72.0;

    // The text on screen or kept for this file, with any edits - for a print
    // asked from the tree. Null when this process holds no text for it.
    public string? TextFor(string path)
    {
        if (_session is { } shown && string.Equals(shown.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            return shown.Document.Text;
        }
        return _unsaved.TryGetValue(path, out var kept) ? kept.Document.Text : null;
    }

    // The text on screen, for the view's own 인쇄 and Ctrl+P.
    public (string Text, bool Markdown)? ShownTextForPrint
        => _session is { } session ? (session.Document.Text, session.Markdown) : null;

    // Asks Windows' print dialog, then prints. Null when it printed or the
    // dialog was cancelled - printed says which; the failure's message
    // otherwise, for the caller to show. The dialog is owned by the
    // application's main window (WPF's PrintDialog takes
    // Application.MainWindow), so it opens over the app.
    public static string? PrintText(string text, string title, bool markdown, out bool printed)
    {
        printed = false;
        var dialog = new PrintDialog { UserPageRangeEnabled = true };
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        DocumentHighlighter? highlighter = null;
        try
        {
            var inner = BuildPrintPages(text, markdown, PrintPageSize(dialog), out highlighter);

            int first = 0;
            int count = inner.PageCount;
            if (dialog.PageRangeSelection == PageRangeSelection.UserPages)
            {
                first = Math.Clamp(dialog.PageRange.PageFrom - 1, 0, Math.Max(0, inner.PageCount - 1));
                int last = Math.Clamp(dialog.PageRange.PageTo - 1, first, Math.Max(0, inner.PageCount - 1));
                count = last - first + 1;
            }

            dialog.PrintDocument(new FramedPaginator(inner, title, first, count), title);
            printed = true;
            return null;
        }
        catch (Exception ex) when (ex is PrintQueueException or PrintSystemException
                                       or InvalidOperationException or System.IO.IOException
                                       or UnauthorizedAccessException)
        {
            return ex.Message;
        }
        finally
        {
            highlighter?.Dispose();
        }
    }

    // The text laid out as pages of the given size, paginated. Apart from the
    // dialog so the pages can be rendered and looked at without a printer
    // (FramedPages; the scratchpad printprobe harness did). The highlighter,
    // when there is one, is the caller's to dispose after printing.
    private static DocumentPaginator BuildPrintPages(
        string text, bool markdown, Size pageSize, out DocumentHighlighter? highlighter)
    {
        var document = new TextDocument(text);
        highlighter = markdown
            ? new DocumentHighlighter(document, MarkdownDefinition(light: true, PrintFontSize))
            : null;

        var flow = new FlowDocument(BuildPrintParagraph(document, highlighter))
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = PrintFontSize,
            Foreground = Brushes.Black,
            Background = Brushes.White,
            PageWidth = pageSize.Width,
            PageHeight = pageSize.Height,
            PagePadding = new Thickness(72, 96, 72, 96),
            // One column the width of the page, not WPF's default of several
            // narrow ones on a wide page.
            ColumnWidth = pageSize.Width,
            TextAlignment = TextAlignment.Left,
        };

        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.PageSize = pageSize;
        paginator.ComputePageCount();
        return paginator;
    }

    // Every page as it would print, header and footer included.
    internal static DocumentPaginator FramedPages(
        string text, string title, bool markdown, Size pageSize, out DocumentHighlighter? highlighter)
    {
        var inner = BuildPrintPages(text, markdown, pageSize, out highlighter);
        return new FramedPaginator(inner, title, 0, inner.PageCount);
    }

    // The paper the dialog settled on, turned for landscape. The printable
    // area is the fallback for a driver that names no media size.
    private static Size PrintPageSize(PrintDialog dialog)
        => PageSizeOf(dialog.PrintTicket, new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight));

    private static Size PageSizeOf(PrintTicket? ticket, Size fallback)
    {
        double width = ticket?.PageMediaSize?.Width ?? fallback.Width;
        double height = ticket?.PageMediaSize?.Height ?? fallback.Height;
        bool landscape = ticket?.PageOrientation is PageOrientation.Landscape or PageOrientation.ReverseLandscape;
        if (landscape == width < height)
        {
            (width, height) = (height, width);
        }
        return new Size(width, height);
    }

    // ----- The preview (2026-10-13, on request) -----------------------------------
    //
    // The pages exactly as they will print - the same FramedPaginator - on the
    // default printer's paper as its user has set it, A4 when there is no
    // printer to ask. The print dialog can still change the paper, and then
    // the pages are laid out again for it; the preview shows what one press
    // of 인쇄 would give on the paper most likely to be in the tray.
    private static readonly Size A4 = new(210 / 25.4 * 96, 297 / 25.4 * 96);

    private static Size DefaultPrintPageSize()
    {
        try
        {
            using var queue = LocalPrintServer.GetDefaultPrintQueue();
            return PageSizeOf(queue.UserPrintTicket ?? queue.DefaultPrintTicket, A4);
        }
        catch (Exception ex) when (ex is PrintSystemException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            return A4;
        }
    }

    // A FixedDocument of the framed pages, for a DocumentViewer. Each page's
    // visual is laid on a FixedPage through PageVisualHost; every page is
    // built here, once, so no visual is ever asked to have two parents.
    public static FixedDocument BuildPreview(string text, string title, bool markdown)
    {
        var pageSize = DefaultPrintPageSize();
        var pages = FramedPages(text, title, markdown, pageSize, out var highlighter);
        try
        {
            var document = new FixedDocument();
            document.DocumentPaginator.PageSize = pageSize;
            for (int i = 0; i < pages.PageCount; i++)
            {
                var page = new FixedPage
                {
                    Width = pageSize.Width,
                    Height = pageSize.Height,
                    Background = Brushes.White,
                };
                page.Children.Add(new PageVisualHost(pages.GetPage(i).Visual, pageSize));
                var content = new PageContent();
                ((System.Windows.Markup.IAddChild)content).AddChild(page);
                document.Pages.Add(content);
            }
            return document;
        }
        finally
        {
            highlighter?.Dispose();
        }
    }

    // One printed page's visual as an element a FixedPage can hold.
    private sealed class PageVisualHost : FrameworkElement
    {
        private readonly Visual _page;
        private readonly Size _size;

        public PageVisualHost(Visual page, Size size)
        {
            _page = page;
            _size = size;
            AddVisualChild(page);
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => _page;

        protected override Size MeasureOverride(Size availableSize) => _size;
    }

    // One paragraph, a LineBreak between lines, as AvalonEdit's DocumentPrinter
    // builds it - with each highlighted section's own size, weight, style,
    // colour, underline and strikethrough.
    private static Paragraph BuildPrintParagraph(TextDocument document, DocumentHighlighter? highlighter)
    {
        var paragraph = new Paragraph();
        foreach (var line in document.Lines)
        {
            if (line.LineNumber > 1)
            {
                paragraph.Inlines.Add(new LineBreak());
            }

            if (highlighter is null)
            {
                paragraph.Inlines.Add(new Run(document.GetText(line)));
                continue;
            }

            var rich = highlighter.HighlightLine(line.LineNumber).ToRichText();
            foreach (var section in rich.GetHighlightedSections(0, rich.Length))
            {
                var run = new Run(rich.Text.Substring(section.Offset, section.Length));
                ApplyPrintColor(run, section.Color);
                paragraph.Inlines.Add(run);
            }
        }
        return paragraph;
    }

    private static void ApplyPrintColor(Run run, HighlightingColor? color)
    {
        if (color is null)
        {
            return;
        }

        if (color.Foreground?.GetBrush(null) is Brush foreground)
        {
            run.Foreground = foreground;
        }
        if (color.FontWeight is { } weight)
        {
            run.FontWeight = weight;
        }
        if (color.FontStyle is { } style)
        {
            run.FontStyle = style;
        }
        if (color.FontSize is { } size)
        {
            run.FontSize = size;
        }
        if (color.Underline == true)
        {
            run.TextDecorations.Add(TextDecorations.Underline);
        }
        if (color.Strikethrough == true)
        {
            run.TextDecorations.Add(TextDecorations.Strikethrough);
        }
    }

    // The FlowDocument's pages with the file name drawn in the top margin and
    // "page / pages" in the bottom one, over the range the dialog asked for -
    // the numbers stay the document's own, so pages 3-4 print as 3 / 9 and
    // 4 / 9.
    private sealed class FramedPaginator : DocumentPaginator
    {
        private readonly DocumentPaginator _inner;
        private readonly string _title;
        private readonly int _first;
        private readonly int _count;
        private readonly Typeface _typeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Brush Ink = Frozen(Color.FromRgb(0x55, 0x55, 0x55));
        private const double FrameFontSize = 9 * 96.0 / 72.0;

        public FramedPaginator(DocumentPaginator inner, string title, int first, int count)
        {
            _inner = inner;
            _title = title;
            _first = first;
            _count = count;
        }

        public override DocumentPage GetPage(int pageNumber)
        {
            int number = _first + pageNumber;
            var page = _inner.GetPage(number);
            var size = page.Size;

            var frame = new DrawingVisual();
            double pixelsPerDip = VisualTreeHelper.GetDpi(frame).PixelsPerDip;
            using (var context = frame.RenderOpen())
            {
                var header = Text(_title, pixelsPerDip);
                header.MaxTextWidth = Math.Max(1, size.Width - 144);
                header.MaxLineCount = 1;
                header.Trimming = TextTrimming.CharacterEllipsis;
                context.DrawText(header, new Point(72, 48 - header.Height / 2));

                var footer = Text(
                    string.Format(CultureInfo.CurrentCulture, Strings.TextPrintPageFormat, number + 1, _inner.PageCount),
                    pixelsPerDip);
                context.DrawText(footer, new Point((size.Width - footer.Width) / 2, size.Height - 48 - footer.Height / 2));
            }

            var visual = new ContainerVisual();
            visual.Children.Add(page.Visual);
            visual.Children.Add(frame);
            return new DocumentPage(visual, size, page.BleedBox, page.ContentBox);
        }

        private FormattedText Text(string text, double pixelsPerDip)
            => new(text, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
                   _typeface, FrameFontSize, Ink, pixelsPerDip);

        public override bool IsPageCountValid => _inner.IsPageCountValid;

        public override int PageCount => _count;

        public override Size PageSize
        {
            get => _inner.PageSize;
            set => _inner.PageSize = value;
        }

        public override IDocumentPaginatorSource? Source => _inner.Source;
    }
}
