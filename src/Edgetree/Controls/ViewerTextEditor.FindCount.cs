using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Search;
using SidebarExplorer.App.Services;
// The project also references WinForms, whose namespaces name the same
// types; these are the WPF ones.
using Brush = System.Windows.Media.Brush;
using Panel = System.Windows.Controls.Panel;

namespace SidebarExplorer.App;

// HOW MANY MATCHES, AND WHERE (2026-11-27, on request). The find row says
// "3/12" at the right end of its field - the match the selection is on, of
// how many - and the scroll bar's lane carries a mark at every line that has
// one, so the spread of a search can be read without stepping through it.
// VS Code's find widget and overview ruler, the two halves of the same ask.
//
// THE PANEL'S OWN MATCHES ARE COUNTED, NOT A SECOND SEARCH. AvalonEdit's find
// panel already finds every match in the whole document whenever the pattern,
// an option or the text changes - on the UI thread, which is the cost a large
// file pays per keystroke today - and keeps them to draw the marks in the
// text. Searching again here would double that cost for a number. They are
// read by reflection (the "renderer" field, its CurrentResults), as the
// replace count reaches the panel's message tooltip: the package is pinned
// (6.3.1.120), the names were read off that build, and a version that renamed
// them falls back to a search of our own - slower, never wrong.
//
// READ AFTER THE PANEL HAS SEARCHED. Every trigger here (pattern, option, text,
// open, close) only queues the read at Background priority; the panel's own
// search runs inside the change itself, so by the time the queue gets here
// its results are the new ones. The selection's place among them is a binary
// search per selection change, which is what F3 and the arrows cost.
//
// THE MARKS ARE LINES, placed by the line's top over the document's height -
// the same proportion the scroll bar's thumb is drawn from. Wrapped lines
// not yet drawn are measured by AvalonEdit's estimate until they are, so a
// mark can move a little as the text is scrolled through; the ruler is
// redrawn when the document's height changes for that reason.
public sealed partial class ViewerTextEditor
{
    private TextBlock? _findCount;
    private FindMarkRuler? _findRuler;
    private ScrollViewer? _findRulerScroller;
    private DispatcherTimer? _findRulerTimer;
    private bool _findCountQueued;

    // A new pattern or option since the last count - the one trigger that may
    // move the selection (see UpdateFindCount).
    private bool _findSearchChanged;

    // Where each of the panel's current matches starts, ascending - the
    // order the panel keeps them in.
    private int[] _matchStarts = Array.Empty<int>();

    private static readonly FieldInfo? PanelRendererField =
        typeof(SearchPanel).GetField("renderer", BindingFlags.NonPublic | BindingFlags.Instance);

    private void InitFindCount()
    {
        _search.SearchOptionsChanged += (_, _) =>
        {
            _findSearchChanged = true;
            ScheduleFindCount();
        };
        TextChanged += (_, _) => ScheduleFindCount();
        QuietNoMatchesMessage();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        AttachFindRuler();
    }

    // The ruler goes into the scroll viewer's own grid, in the bar's column:
    // a sibling of the bar rather than part of it, so it does not fade with
    // the bar at rest, and out of the hit test so the bar under it still
    // takes every press. That column is zero wide until there is something to
    // scroll, and with nothing to scroll every match is on screen anyway.
    private void AttachFindRuler()
    {
        if (_findRulerScroller is not null)
        {
            _findRulerScroller.ScrollChanged -= FindRulerScroller_ScrollChanged;
            _findRulerScroller = null;
        }

        if (GetTemplateChild("PART_ScrollViewer") is not ScrollViewer scroller)
        {
            return;
        }

        scroller.ApplyTemplate();
        if (VisualTreeHelper.GetChildrenCount(scroller) == 0 ||
            VisualTreeHelper.GetChild(scroller, 0) is not Grid root ||
            root.ColumnDefinitions.Count < 2)
        {
            return;
        }

        _findRuler ??= new FindMarkRuler();
        (VisualTreeHelper.GetParent(_findRuler) as Panel)?.Children.Remove(_findRuler);
        Grid.SetColumn(_findRuler, 1);
        root.Children.Add(_findRuler);
        _findRuler.Visibility = _marksShown ? Visibility.Visible : Visibility.Hidden;

        _findRulerScroller = scroller;
        scroller.ScrollChanged += FindRulerScroller_ScrollChanged;
    }

    private void FindRulerScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange != 0 && _matchStarts.Length > 0)
        {
            ScheduleFindRuler();
        }
    }

    // Typing with find closed queues nothing: there is no count to keep.
    private void ScheduleFindCount()
    {
        if (_findCountQueued || (!FindIsOpen && _matchStarts.Length == 0))
        {
            return;
        }

        _findCountQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdateFindCount));
    }

    private void UpdateFindCount()
    {
        _findCountQueued = false;

        // A pattern that does not compile as a regex never reaches the
        // panel's search, so its results are the previous pattern's; nothing
        // is counted for it (the row already shows the error).
        _matchStarts = FindIsOpen && CurrentSearchStrategy() is not null
            ? ReadMatchStarts()
            : Array.Empty<int>();

        // A SEARCH THAT FINDS ONLY ABOVE THE CARET WRAPS TO THE FIRST MATCH
        // (2026-11-27, on report). Typing a pattern selects the first match AT
        // OR AFTER the caret and does not wrap, so with the caret past the last
        // one the text was marked, nothing was selected and the count read
        // "?/10" - while F3 from the same spot wraps to the top. Measured in a
        // harness on the reported text: caret at the start or mid-line selected
        // a match, caret at the end selected none. So after a new pattern or
        // option, and only then, an unselected result set is stepped into the
        // way F3 would (VS Code's find does the same). An edit to the text, or a
        // selection made by hand, never moves the selection here.
        bool searchChanged = _findSearchChanged;
        _findSearchChanged = false;
        if (searchChanged && _matchStarts.Length > 0 && CurrentMatchIndex() < 0)
        {
            DriveFind(_search.FindNext);
        }

        UpdateFindCountText();
        UpdateFindRuler();
    }

    // Which match the selection is, 0-based, or -1 for none of them.
    private int CurrentMatchIndex()
    {
        var selection = TextArea.Selection;
        return selection.IsEmpty || _matchStarts.Length == 0
            ? -1
            : Math.Max(-1, Array.BinarySearch(_matchStarts, selection.SurroundingSegment.Offset));
    }

    private int[] ReadMatchStarts()
    {
        if (PanelRendererField?.GetValue(_search) is { } renderer &&
            renderer.GetType().GetProperty("CurrentResults")?.GetValue(renderer) is IEnumerable<TextSegment> results)
        {
            return results.Select(r => r.StartOffset).ToArray();
        }

        return CurrentSearchStrategy() is { } strategy && Document is { } document
            ? strategy.FindAll(document, 0, document.TextLength).Select(r => r.Offset).ToArray()
            : Array.Empty<int>();
    }

    private void UpdateFindCountText()
    {
        if (_findCount is null)
        {
            return;
        }

        // 0/0 for a search that found nothing, which is all that says so now
        // (the panel's tooltip is quieted - QuietNoMatchesMessage). Nothing at
        // all with no pattern, or one that does not compile: the field's own
        // outline marks that one.
        int total = _matchStarts.Length;
        if (total == 0)
        {
            _findCount.Text = CurrentSearchStrategy() is not null
                ? string.Format(Strings.TextFindCount, 0, 0)
                : string.Empty;
            return;
        }

        int index = CurrentMatchIndex();
        _findCount.Text = index >= 0
            ? string.Format(Strings.TextFindCount, index + 1, total)
            : string.Format(Strings.TextFindCountUnknown, total);
    }

    private void ScheduleFindRuler()
    {
        _findRulerTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background,
            (_, _) =>
            {
                _findRulerTimer?.Stop();
                UpdateFindRuler();
            }, Dispatcher);
        _findRulerTimer.Stop();
        _findRulerTimer.Start();
    }

    // One mark per LINE that has a match, however many it has: a search for a
    // letter in a large file has a match on nearly every line, and the marks
    // are what has to stay cheap.
    private void UpdateFindRuler()
    {
        if (_findRuler is null)
        {
            return;
        }

        var textView = TextArea.TextView;
        double height = textView.DocumentHeight;
        if (_matchStarts.Length == 0 || Document is not { } document || height <= 0)
        {
            _findRuler.SetMarks(Array.Empty<double>(), 0);
            return;
        }

        var marks = new List<double>();
        int lineEnd = -1;
        foreach (int start in _matchStarts)
        {
            if (start <= lineEnd)
            {
                continue;
            }
            if (start > document.TextLength)
            {
                break;
            }

            var line = document.GetLineByOffset(start);
            lineEnd = line.EndOffset;
            marks.Add(textView.GetVisualTopByDocumentLine(line.LineNumber) / height);
        }

        _findRuler.SetMarks(marks.ToArray(), textView.DefaultLineHeight / height);
    }

    // With the marks in the text: hidden while the hand has a selection of
    // its own (HideFindMarks), back with them.
    private void SetFindRulerShown(bool shown)
    {
        if (_findRuler is not null)
        {
            _findRuler.Visibility = shown ? Visibility.Visible : Visibility.Hidden;
        }
    }

    // The lane's marks. Drawn in the app's accent - a first pick, put up to be
    // judged on screen - from the lane's 1px divider to its right edge, at
    // least 2px and at most 4px tall whatever share of the document one line
    // is, so a short file does not get slabs and a long one still shows a mark.
    private sealed class FindMarkRuler : FrameworkElement
    {
        private const double MinMark = 2;
        private const double MaxMark = 4;

        private double[] _marks = Array.Empty<double>();
        private double _lineShare;

        public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
            nameof(Fill), typeof(Brush), typeof(FindMarkRuler),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush? Fill
        {
            get => (Brush?)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        public FindMarkRuler()
        {
            IsHitTestVisible = false;
            SetResourceReference(FillProperty, "AccentForeground");
        }

        // Each mark as the share of the document above its line, 0 to 1.
        public void SetMarks(double[] marks, double lineShare)
        {
            if (marks.Length == 0 && _marks.Length == 0)
            {
                return;
            }

            _marks = marks;
            _lineShare = lineShare;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            double width = ActualWidth;
            double height = ActualHeight;
            if (Fill is not { } fill || _marks.Length == 0 || width <= 1 || height <= 0)
            {
                return;
            }

            // Whole device pixels, so a 2px mark is two crisp rows rather
            // than three blurred ones.
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
            double mark = Math.Round(Math.Clamp(_lineShare * height, MinMark, MaxMark) * scale) / scale;
            double lastTop = double.NaN;
            foreach (double share in _marks)
            {
                double top = Math.Round(Math.Min(share * height, height - mark) * scale) / scale;
                if (top == lastTop)
                {
                    continue;
                }

                lastTop = top;
                drawingContext.DrawRectangle(fill, null, new Rect(1, top, width - 1, mark));
            }
        }
    }
}
