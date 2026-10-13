using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
// The project also references WinForms, whose namespaces name the same
// types; these are the WPF ones.
using Brush = System.Windows.Media.Brush;
using Cursors = System.Windows.Input.Cursors;
using FlowDirection = System.Windows.FlowDirection;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace SidebarExplorer.App;

// MARKDOWN HEADINGS THAT STAY AT THE TOP WHILE SCROLLING (2026-10-13, on
// request, pointing at VS Code's Sticky Scroll in the same README). Once a
// heading scrolls up out of view, it is drawn at the top of the text for as
// long as its section is on screen, under the headings it belongs to - so the
// reader of a long document always sees which part they are in. A click on
// one goes to that heading.
//
// VS CODE'S RULES, READ FROM ITS CODE (the installed 2026 build): up to five
// rows (editor.stickyScroll.maxLineCount), a row per enclosing heading,
// outermost first; the line number beside each row while line numbers are on;
// a heading with nothing under it never sticks; and a section's row is
// PUSHED up and out by the next heading of its own level as that one arrives,
// rather than swapped at once. One guard of our own: never more rows than half
// the view, which a short panel can reach where a full editor never does.
//
// MARKDOWN ONLY. VS Code gives plain text sticky lines by indentation; a TXT
// here has none.
//
// A HEADING IS WHAT THE HIGHLIGHTING DRAWS AS ONE: "#" to "######" and a
// space, after at most three spaces, outside a ``` or ~~~ fence - the same
// tests as Markdown.xshd, so a line that is not blue in the text never sticks
// as a heading. That also makes the row easy to draw: the highlighting paints
// a heading line whole in one colour, bold, so one run of text in that colour
// is exactly what the line looks like in place.
//
// THE COST. The headings are indexed on a worker - the whole text scanned
// once, after a 300ms pause in typing, as the changed-line marks are - and
// kept as DocumentLine objects, which follow the lines through the edits made
// before the next scan. What runs on every scroll step is at most five
// probes, each a binary search over that index plus a few height-tree lookups
// (logarithmic in the document), and nothing is drawn unless the rows or
// their positions came out different.
public sealed partial class ViewerTextEditor
{
    // VS Code's default.
    private const int MaxStickyRows = 5;

    // Past this the row's text is cut anyway; a pathological heading of a
    // hundred thousand characters is not laid out to show forty.
    private const int StickyTextLimit = 400;

    private static readonly char[] LineBreaks = { '\r', '\n' };

    // Every heading line of the Markdown text on screen, in document order.
    // Parent is the nearest heading above of a higher level, Next the first
    // heading below of the same or a higher level - the line that ends this
    // one's section. -1 for none.
    private sealed record HeadingIndex(DocumentLine[] Lines, byte[] Levels, int[] Parent, int[] Next);

    // What the worker hands back: line NUMBERS, valid for the text it was
    // given, turned into lines on the UI thread only if nothing was typed
    // meanwhile.
    private sealed record HeadingScan(int[] Lines, byte[] Levels, int[] Parent, int[] Next);

    // One row on screen: which heading, and its top in the text area - a
    // row being pushed out has a top above where its slot is.
    private readonly record struct StickyRow(int Heading, double Y);

    private HeadingIndex? _headings;
    private DispatcherTimer? _headingTimer;
    private int _headingRun;
    private List<StickyRow> _stickyRows = new();
    private StickyHeadingsAdorner? _stickyAdorner;

    private void AttachStickyHeadings()
    {
        var textView = TextArea.TextView;
        textView.ScrollOffsetChanged += (_, _) => UpdateStickyRows();
        // A scroll is not the only thing that moves lines: a font step, a new
        // width wrapping them anew, the text itself.
        textView.VisualLinesChanged += (_, _) => UpdateStickyRows();
        // The text column moves when the line numbers come or go or widen by a
        // digit, which changes neither the rows nor the text area's size.
        textView.SizeChanged += (_, _) => _stickyAdorner?.InvalidateVisual();
        TextChanged += (_, _) =>
        {
            ScheduleHeadingIndex();
            // A sticky heading's own text can change - a replace-all, say.
            if (_stickyRows.Count > 0)
            {
                _stickyAdorner?.InvalidateVisual();
            }
        };

        // Over the text area, in the scroll presenter's own adorner layer:
        // above the text and its line numbers, under the scroll bar, clipped
        // to the view. That layer exists once the editor's template has been
        // applied, so the adorner goes in on load - and back in, should the
        // editor ever be loaded into a new place.
        TextArea.Loaded += (_, _) =>
        {
            if (AdornerLayer.GetAdornerLayer(TextArea) is not { } layer)
            {
                return;
            }

            _stickyAdorner ??= new StickyHeadingsAdorner(this);
            if (layer.GetAdorners(TextArea)?.Contains(_stickyAdorner) != true)
            {
                (VisualTreeHelper.GetParent(_stickyAdorner) as AdornerLayer)?.Remove(_stickyAdorner);
                layer.Add(_stickyAdorner);
            }
        };
    }

    // ----- The index ------------------------------------------------------------

    // A new text on screen, or the text on screen turned Markdown or not: the
    // old index goes at once, and a Markdown text is indexed now rather than
    // after a pause.
    private void ResetHeadingIndex()
    {
        _headingTimer?.Stop();
        ++_headingRun;
        _headings = null;
        UpdateStickyRows();
        if (_markdown && _session is not null)
        {
            RunHeadingIndex();
        }
    }

    private void ScheduleHeadingIndex()
    {
        if (!_markdown || _session is null)
        {
            return;
        }

        _headingTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(300), DispatcherPriority.Background,
            (_, _) => RunHeadingIndex(), Dispatcher);
        _headingTimer.Stop();
        _headingTimer.Start();
    }

    private async void RunHeadingIndex()
    {
        _headingTimer?.Stop();
        if (!_markdown || _session is not { } session)
        {
            return;
        }

        int run = ++_headingRun;
        var document = session.Document;
        var version = document.Version;
        var snapshot = document.CreateSnapshot();

        // Caught whole: an async void off a timer, and anything escaping it
        // would end the process over a heading row. WHAT THIS HIDES: a fault in
        // the scan shows only as rows that stop following the text.
        HeadingScan scan;
        try
        {
            scan = await Task.Run(() => ScanHeadings(snapshot.Text));
        }
        catch (Exception)
        {
            return;
        }

        if (run != _headingRun || !ReferenceEquals(_session, session) || !_markdown)
        {
            return;
        }

        // Typed while the scan ran: its numbers belong to a text that is no
        // longer there. The index in place stays until the next pause.
        if (version is null || document.Version is not { } now ||
            !now.BelongsToSameDocumentAs(version) || now.CompareAge(version) != 0)
        {
            ScheduleHeadingIndex();
            return;
        }

        var lines = new DocumentLine[scan.Lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = document.GetLineByNumber(scan.Lines[i]);
        }
        _headings = new HeadingIndex(lines, scan.Levels, scan.Parent, scan.Next);
        UpdateStickyRows();
    }

    // WORKER THREAD. The heading lines of a Markdown text, by the rules of
    // Markdown.xshd (see the top of this file).
    private static HeadingScan ScanHeadings(string text)
    {
        var lines = new List<int>();
        var levels = new List<byte>();
        // The fence the text is inside, by its character, or none.
        char fence = '\0';
        int lineNumber = 1;
        int start = 0;
        while (true)
        {
            int end = text.IndexOfAny(LineBreaks, start);
            int stop = end < 0 ? text.Length : end;

            int at = SkipIndent(text, start, stop);
            if (at >= 0)
            {
                if (fence != '\0')
                {
                    if (IsFenceMark(text, at, stop, fence))
                    {
                        fence = '\0';
                    }
                }
                else if (at < stop && text[at] is '`' or '~' && IsFenceMark(text, at, stop, text[at]))
                {
                    // AvalonEdit looks for a span's end from where its start
                    // stopped, and treats that point as the start of a line -
                    // so a fence closes on its own line if another mark
                    // follows straight on ("``````").
                    int after = SkipIndent(text, at + 3, stop);
                    if (after < 0 || !IsFenceMark(text, after, stop, text[at]))
                    {
                        fence = text[at];
                    }
                }
                else if (HeadingLevel(text, at, stop) is var level and > 0)
                {
                    lines.Add(lineNumber);
                    levels.Add((byte)level);
                }
            }

            if (end < 0)
            {
                break;
            }

            // "\r\n" is one break, as the document counts lines.
            start = text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? end + 2 : end + 1;
            lineNumber++;
        }

        // Parent and Next in one pass, with the headings still open as a stack.
        int count = lines.Count;
        var parent = new int[count];
        var next = new int[count];
        Array.Fill(next, -1);
        var open = new Stack<int>();
        for (int i = 0; i < count; i++)
        {
            while (open.Count > 0 && levels[open.Peek()] >= levels[i])
            {
                next[open.Pop()] = i;
            }
            parent[i] = open.Count > 0 ? open.Peek() : -1;
            open.Push(i);
        }

        return new HeadingScan(lines.ToArray(), levels.ToArray(), parent, next);
    }

    // Past up to three spaces - Markdown's "^[ ]{0,3}" - or -1 for four or
    // more, which make the line something other than a heading or a fence.
    private static int SkipIndent(string text, int at, int stop)
    {
        int spaces = 0;
        while (at < stop && text[at] == ' ')
        {
            if (++spaces > 3)
            {
                return -1;
            }
            at++;
        }
        return at;
    }

    private static bool IsFenceMark(string text, int at, int stop, char mark)
        => at + 3 <= stop && text[at] == mark && text[at + 1] == mark && text[at + 2] == mark;

    // 1 to 6 for "#" to "######" followed by a space or a tab; 0 otherwise.
    private static int HeadingLevel(string text, int at, int stop)
    {
        int hashes = 0;
        while (at + hashes < stop && text[at + hashes] == '#' && hashes < 7)
        {
            hashes++;
        }
        return hashes is >= 1 and <= 6 && at + hashes < stop && text[at + hashes] is ' ' or '\t'
            ? hashes
            : 0;
    }

    // The last heading at or above a line, or -1. Also -1 while the index
    // holds a line deleted since the scan: the rows wait for the next one,
    // which the deletion has already scheduled.
    private static int LastHeadingAtOrBefore(HeadingIndex index, int lineNumber)
    {
        int low = 0;
        int high = index.Lines.Length - 1;
        int found = -1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            var line = index.Lines[middle];
            if (line.IsDeleted)
            {
                return -1;
            }

            if (line.LineNumber <= lineNumber)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        return found;
    }

    // ----- The rows -------------------------------------------------------------

    private int StickyRowLimit()
    {
        var textView = TextArea.TextView;
        double rowHeight = textView.DefaultLineHeight;
        return rowHeight > 0
            ? Math.Min(MaxStickyRows, (int)(textView.ActualHeight / rowHeight / 2))
            : 0;
    }

    private void UpdateStickyRows()
    {
        var rows = ComputeStickyRows();
        if (rows.SequenceEqual(_stickyRows))
        {
            return;
        }

        _stickyRows = rows;
        _stickyAdorner?.InvalidateVisual();
    }

    // Row by row, from the top: the line just under the rows found so far is
    // looked at, and the next of its enclosing headings sticks if that
    // heading has gone up under them. The chain has to agree with the rows
    // above - a line already in the next section belongs to headings the rows
    // do not show, and the stack stops there.
    private List<StickyRow> ComputeStickyRows()
    {
        var rows = new List<StickyRow>();
        if (_headings is not { Lines.Length: > 0 } index)
        {
            return rows;
        }

        var textView = TextArea.TextView;
        double rowHeight = textView.DefaultLineHeight;
        double scrollTop = textView.VerticalOffset;
        int limit = StickyRowLimit();
        var chain = new List<int>(6);
        while (rows.Count < limit)
        {
            int depth = rows.Count;
            double probe = scrollTop + depth * rowHeight;
            if (probe >= textView.DocumentHeight)
            {
                break;
            }

            int innermost = LastHeadingAtOrBefore(index, textView.GetDocumentLineByVisualTop(probe).LineNumber);
            if (innermost < 0)
            {
                break;
            }

            chain.Clear();
            for (int heading = innermost; heading >= 0; heading = index.Parent[heading])
            {
                chain.Add(heading);
            }
            chain.Reverse();
            if (chain.Count <= depth || !rows.Select(row => row.Heading).SequenceEqual(chain.Take(depth)))
            {
                break;
            }

            int candidate = chain[depth];
            var line = index.Lines[candidate];
            int nextHeading = index.Next[candidate];
            if (line.IsDeleted || (nextHeading >= 0 && index.Lines[nextHeading].IsDeleted))
            {
                break;
            }

            // Still below the rows' bottom: it is on screen in its own place.
            // Half a pixel's leeway: a heading RevealCaret puts exactly at the
            // rows' bottom came out 37.79999 against 37.8 in the harness and
            // stuck over itself.
            int number = line.LineNumber;
            if (textView.GetVisualTopByDocumentLine(number) - scrollTop > depth * rowHeight - 0.5)
            {
                break;
            }

            int lastLine = nextHeading >= 0 ? index.Lines[nextHeading].LineNumber - 1 : Document.LineCount;
            if (lastLine <= number)
            {
                break;
            }

            // The section's bottom; once it rises past this row's slot, the
            // row rises with it.
            double sectionBottom = nextHeading >= 0
                ? textView.GetVisualTopByDocumentLine(lastLine + 1)
                : textView.DocumentHeight;
            double slot = depth * rowHeight;
            rows.Add(new StickyRow(candidate, slot + Math.Min(0, sectionBottom - scrollTop - slot - rowHeight)));
        }
        return rows;
    }

    // How far down the rows reach, in the text area: nothing under this is
    // covered by them.
    private double StickyRowsBottom()
    {
        double rowHeight = TextArea.TextView.DefaultLineHeight;
        double bottom = 0;
        foreach (var row in _stickyRows)
        {
            bottom = Math.Max(bottom, row.Y + rowHeight);
        }
        return bottom;
    }

    // The room a line needs above it so the rows do not cover it once it is
    // brought into view: a row for each heading it lies under - not counting
    // itself, if it is one. What RevealCaret leaves above a caret it scrolls
    // to, as VS Code's reveal keeps its sticky lines' worth of room.
    private double StickyRoomAbove(int lineNumber)
    {
        if (_headings is not { } index)
        {
            return 0;
        }

        int innermost = LastHeadingAtOrBefore(index, lineNumber);
        if (innermost < 0)
        {
            return 0;
        }

        int count = 0;
        for (int heading = innermost; heading >= 0; heading = index.Parent[heading])
        {
            count++;
        }
        if (index.Lines[innermost].LineNumber == lineNumber)
        {
            count--;
        }
        return Math.Min(count, StickyRowLimit()) * TextArea.TextView.DefaultLineHeight;
    }

    // The row a point is on, or -1. An upper row is drawn over one being
    // pushed under it, so the first that holds the point is the one seen.
    private int StickyRowAt(double y)
    {
        double rowHeight = TextArea.TextView.DefaultLineHeight;
        for (int i = 0; i < _stickyRows.Count; i++)
        {
            if (y >= _stickyRows[i].Y && y < _stickyRows[i].Y + rowHeight)
            {
                return i;
            }
        }
        return -1;
    }

    // A row clicked: the caret to its heading, which RevealCaret then brings
    // down to just under the headings above it - the row's own slot. Asked for
    // here as well, for a caret that was on that heading already.
    private void JumpToStickyRow(int row)
    {
        if (row < 0 || row >= _stickyRows.Count || _headings is not { } index)
        {
            return;
        }

        var line = index.Lines[_stickyRows[row].Heading];
        if (line.IsDeleted)
        {
            return;
        }

        PlaceCurrentLine();
        TextArea.ClearSelection();
        TextArea.Caret.Offset = line.Offset;
        TextArea.Focus();
        ScheduleRevealCaret();
    }

    // ----- Drawing --------------------------------------------------------------

    // Each row on the text's own ground - the panel's background with the
    // reading shade over it while reading - so the lines going up under it
    // are hidden; the heading in the colour the highlighting gives it; its
    // number where the line numbers stand; the panel's divider under the
    // last. Drawn bottom row first, so a row being pushed out goes under the
    // one above it.
    private void DrawStickyHeadings(DrawingContext drawingContext, Point? mouse)
    {
        var rows = _stickyRows;
        if (rows.Count == 0 || _headings is not { } index)
        {
            return;
        }

        var textView = TextArea.TextView;
        double width = TextArea.ActualWidth;
        double rowHeight = textView.DefaultLineHeight;
        double textLeft = textView.TranslatePoint(default, TextArea).X;
        var ground = TryFindResource("ViewerBackground") as Brush;
        var shade = Background;
        var hover = TryFindResource("HoverBackground") as Brush;
        var ink = SyntaxHighlighting?.GetNamedColor("Heading1")?.Foreground?.GetBrush(null) ?? Foreground;
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var mode = TextOptions.GetTextFormattingMode(textView);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        int hovered = mouse is { } point ? StickyRowAt(point.Y) : -1;
        double? numbersRight = _lineNumbers is { } margin
            ? margin.TranslatePoint(new Point(margin.RenderSize.Width, 0), TextArea).X
            : null;

        for (int i = rows.Count - 1; i >= 0; i--)
        {
            var line = index.Lines[rows[i].Heading];
            if (line.IsDeleted)
            {
                continue;
            }

            double top = rows[i].Y;
            var area = new Rect(0, top, width, rowHeight);
            drawingContext.DrawRectangle(ground, null, area);
            drawingContext.DrawRectangle(shade, null, area);
            if (i == hovered)
            {
                drawingContext.DrawRectangle(hover, null, area);
            }

            var text = new FormattedText(
                Document.GetText(line.Offset, Math.Min(line.Length, StickyTextLimit)),
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, FontSize, ink, null, mode, pixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, width - textLeft),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            double baseline = top + textView.DefaultBaseline;
            drawingContext.DrawText(text, new Point(textLeft, baseline - text.Baseline));

            if (numbersRight is { } right && _lineNumbers?.FormatNumber(line) is { } number)
            {
                drawingContext.DrawText(number, new Point(right - number.Width, baseline - number.Baseline));
            }
        }

        // On a whole device pixel, one pixel thick, as the bars' divider is.
        if (TryFindResource("PanelDividerBrush") is Brush divider)
        {
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
            double y = Math.Round(StickyRowsBottom() * scale) / scale;
            drawingContext.DrawRectangle(divider, null, new Rect(0, y, width, 1 / scale));
        }
    }

    // Over the text area (see AttachStickyHeadings). Only the rows are drawn,
    // so only they take the pointer: below them the text is clicked,
    // selected and scrolled as ever, and a wheel turned over a row still
    // reaches the scroll viewer.
    private sealed class StickyHeadingsAdorner : Adorner
    {
        private readonly ViewerTextEditor _owner;
        private Point? _mouse;

        public StickyHeadingsAdorner(ViewerTextEditor owner)
            : base(owner.TextArea)
        {
            _owner = owner;
            // A row being pushed out must not reach above the text area.
            IsClipEnabled = true;
            // Not the text's I-beam: a row is a heading to go to, not text.
            Cursor = Cursors.Arrow;
        }

        protected override void OnRender(DrawingContext drawingContext)
            => _owner.DrawStickyHeadings(drawingContext, _mouse);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var at = e.GetPosition(this);
            int was = _mouse is { } previous ? _owner.StickyRowAt(previous.Y) : -1;
            _mouse = at;
            if (_owner.StickyRowAt(at.Y) != was)
            {
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            _mouse = null;
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            _owner.JumpToStickyRow(_owner.StickyRowAt(e.GetPosition(this).Y));
            e.Handled = true;
        }

        // The text's right-click menu acts where the caret is, which a row
        // is not; a right-click on a row does nothing rather than act
        // somewhere else.
        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e) => e.Handled = true;

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) => e.Handled = true;
    }
}
