using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SidebarExplorer.App.Services;
// System.Drawing and WinForms are global usings in this project (the tray
// icon), so every name the two share with WPF is pinned here.
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace SidebarExplorer.App;

// Text selection and copy on the PDF's pages (2026-10-27, the author's three
// calls: a left drag ALWAYS selects - the drag-to-pan it replaces is gone, the
// wheel, the scrollbars and Shift+wheel move the view; a press in the PDF
// takes the keyboard, as the text view does, so Ctrl+C and Ctrl+A act on the
// document; the right-click menu is 복사 and 전체 선택).
//
// EACH PAGE'S CHARACTERS ARE READ ONCE, on the PDFium thread, when the page is
// first realised (PdfRenderService.RequestPageText, asked just before the
// page's render so the picture still comes first). Everything a drag does -
// finding the character under the pointer, drawing the band - is arithmetic
// over those boxes on the UI thread; nothing calls PDFium while the mouse
// moves. Kept for the document's life: a few dozen bytes a character, and
// a selection that spans pages scrolled away still has its text to copy.
//
// A position is (page, caret): the caret sits BEFORE character `caret`, so a
// page's text is carets 0..Count. int.MaxValue stands for "the end of this
// page" where its text has not been read yet - 전체 선택 on a 300-page file
// does not wait for 300 pages.
//
// NO AUTOSCROLL while dragging past the edge of the view: the app does not
// move the view by itself. The wheel scrolls during a drag, and the selection
// follows the pointer onto whatever scrolls under it (Scroller_ScrollChanged).
//
// A scanned PDF has no characters, only pictures of them; a drag there
// selects nothing, and the cursor never turns into the I-beam to suggest it
// would.
public partial class PdfPageColumn
{
    private readonly Dictionary<int, PdfRenderService.PageText> _texts = new();
    private readonly HashSet<int> _textAsked = new();
    private (int Page, int Caret)? _anchor;
    private (int Page, int Caret)? _focus;
    private bool _selecting;

    // Over WHITE paper in both themes, so one fixed colour: the blue band of
    // every browser's PDF viewer, translucent so the text reads through it.
    internal static readonly Brush SelectionFill = CreateSelectionBrush();

    private static Brush CreateSelectionBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0x5C, 0x33, 0x90, 0xFF));
        brush.Freeze();
        return brush;
    }

    // Esc with nothing selected: the window hands the keyboard back to the
    // list the panel follows.
    public event EventHandler? KeyboardReleaseRequested;

    public bool HasSelection => _anchor is { } a && _focus is { } f && a != f;

    private void ResetTextState()
    {
        _texts.Clear();
        _textAsked.Clear();
        _anchor = null;
        _focus = null;
        _selecting = false;
    }

    private void AskPageText(PdfRenderService.Document document, int index)
    {
        if (_texts.ContainsKey(index) || !_textAsked.Add(index))
        {
            return;
        }
        PdfRenderService.RequestPageText(document, index, text =>
        {
            if (!ReferenceEquals(_document, document))
            {
                return;
            }
            if (text is null)
            {
                // A failed read, or one dropped unrun: asked again the next
                // time the page is realised.
                _textAsked.Remove(index);
                return;
            }
            _texts[index] = text;
            // A selection may already reach this page (전체 선택, or a drag
            // that outran the read); its band can be drawn now.
            if (_slots.TryGetValue(index, out var slot))
            {
                slot.Layer.InvalidateVisual();
            }
        });
    }

    // A page leaving the column's reach has its queued read dropped
    // (PdfRenderService.CancelPage). Forgotten here at once, not only when the
    // dropped request's null answer comes back through the dispatcher, so a
    // page realised again before that answer is asked again rather than
    // taken as already asked. A read already running still lands its text.
    private void ForgetUnreadPageText(int index)
    {
        if (!_texts.ContainsKey(index))
        {
            _textAsked.Remove(index);
        }
    }

    // ----- where the pointer is ----------------------------------------------

    // The page under y, or the nearer of the two either side of a gap.
    private int PageAtY(double y)
    {
        int page = Math.Min(FirstPageBelow(y), _pageRects.Length - 1);
        if (page > 0 && y < _pageRects[page].Top &&
            _pageRects[page].Top - y > y - _pageRects[page - 1].Bottom)
        {
            page--;
        }
        return page;
    }

    // The caret nearest a point in the column: the line nearest in height,
    // then the character nearest along it, the caret on whichever side of
    // that character the point falls. Null while the page's text is not
    // read yet. onChar says the point is ON a character - the I-beam's test.
    private (int Page, int Caret)? CaretAt(Point point, out bool onChar)
    {
        onChar = false;
        if (_document is not { } document || _pageRects.Length == 0)
        {
            return null;
        }

        int page = PageAtY(point.Y);
        if (!_texts.TryGetValue(page, out var text))
        {
            return null;
        }

        var rect = _pageRects[page];
        double scale = rect.Width / document.PageSizes[page].Width;
        double px = (point.X - rect.Left) / scale;
        double py = (point.Y - rect.Top) / scale;

        int best = -1;
        double bestDy = double.MaxValue, bestDx = double.MaxValue;
        for (int i = 0; i < text.Count; i++)
        {
            if (!text.HasBox(i))
            {
                continue;
            }
            var box = text.Box(i);
            double dy = py < box.Top ? box.Top - py : py > box.Bottom ? py - box.Bottom : 0;
            double dx = px < box.Left ? box.Left - px : px > box.Right ? px - box.Right : 0;
            // Half a point counts as the same line: boxes on one line differ
            // by fractions where a font's metrics round.
            if (dy < bestDy - 0.5 || (Math.Abs(dy - bestDy) <= 0.5 && dx < bestDx))
            {
                best = i;
                bestDy = dy;
                bestDx = dx;
            }
        }

        if (best < 0)
        {
            return (page, 0);
        }
        onChar = bestDy == 0 && bestDx == 0;
        var hit = text.Box(best);
        return (page, px < (hit.Left + hit.Right) / 2 ? best : best + 1);
    }

    // ----- the gesture ---------------------------------------------------------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        // The press takes the keyboard (the author's call) - Ctrl+C, Ctrl+A
        // and the scroll keys are the document's from here, until the tree is
        // clicked or Esc is pressed with nothing selected.
        Keyboard.Focus(this);
        e.Handled = true;

        var point = e.GetPosition(this);
        var caret = CaretAt(point, out bool onChar);

        // Double-click: the word on a character, back to the fit anywhere
        // else - the fit's gesture before text could be selected, kept where
        // it collides with nothing.
        if (e.ClickCount == 2)
        {
            if (onChar && caret is { } at)
            {
                SelectWordAt(at);
            }
            else
            {
                ResetZoom();
            }
            return;
        }

        // Shift+click moves only the far end: the anchor stays where the last
        // press put it - a plain click, a drag, 전체 선택 - and the selection
        // runs from there to here, as in every text box. The drag that may
        // follow keeps extending from the same anchor.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _anchor is not null && caret is { } end)
        {
            _focus = end;
            _selecting = true;
            CaptureMouse();
            InvalidateSelection();
            return;
        }

        _anchor = caret;
        _focus = caret;
        _selecting = caret is not null;
        if (_selecting)
        {
            CaptureMouse();
        }
        InvalidateSelection();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        if (_selecting && IsMouseCaptured)
        {
            ExtendSelectionTo(point);
            return;
        }

        CaretAt(point, out bool onChar);
        Cursor = onChar ? Cursors.IBeam : null;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_selecting)
        {
            _selecting = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _selecting = false;
    }

    private void ExtendSelectionTo(Point point)
    {
        // A page whose text is not back yet leaves the end where it was; the
        // next move after the read lands it.
        if (CaretAt(point, out _) is { } caret && caret != _focus)
        {
            _focus = caret;
            InvalidateSelection();
        }
    }

    // The wheel during a drag moves new text under a pointer that did not
    // move; the selection follows it there.
    private void FollowDragAfterScroll()
    {
        if (_selecting && IsMouseCaptured)
        {
            ExtendSelectionTo(Mouse.GetPosition(this));
        }
    }

    // Letters and digits either side - which takes a run of Hangul or CJK
    // with no spaces in it whole, the same unit the text view's own
    // double-click selects.
    private void SelectWordAt((int Page, int Caret) at)
    {
        if (!_texts.TryGetValue(at.Page, out var text) || text.Count == 0)
        {
            return;
        }
        int index = Math.Clamp(at.Caret, 0, text.Count - 1);
        // The caret is on the near side of the character that was hit;
        // step back onto it when it fell after it.
        if (index > 0 && !IsWordChar(text, index) && IsWordChar(text, index - 1))
        {
            index--;
        }
        if (!IsWordChar(text, index))
        {
            return;
        }
        int start = index, end = index + 1;
        while (start > 0 && IsWordChar(text, start - 1))
        {
            start--;
        }
        while (end < text.Count && IsWordChar(text, end))
        {
            end++;
        }
        _anchor = (at.Page, start);
        _focus = (at.Page, end);
        InvalidateSelection();
    }

    private static bool IsWordChar(PdfRenderService.PageText text, int index)
    {
        int codePoint = text.CodePoints[index];
        return codePoint > 0 && codePoint <= 0x10FFFF && codePoint is < 0xD800 or > 0xDFFF &&
               (System.Text.Rune.IsLetterOrDigit(new System.Text.Rune(codePoint)) || codePoint == '_');
    }

    // ----- what is selected --------------------------------------------------

    private ((int Page, int Caret) Start, (int Page, int Caret) End)? OrderedSelection()
    {
        if (_anchor is not { } a || _focus is not { } f || a == f)
        {
            return null;
        }
        bool forward = a.Page < f.Page || (a.Page == f.Page && a.Caret <= f.Caret);
        return forward ? (a, f) : (f, a);
    }

    // [start, end) of one page's characters that the selection covers, or
    // null where it covers none.
    internal (int Start, int End)? SelectedRange(int page, int count)
    {
        if (OrderedSelection() is not { } selection ||
            page < selection.Start.Page || page > selection.End.Page)
        {
            return null;
        }
        int start = page == selection.Start.Page ? Math.Min(selection.Start.Caret, count) : 0;
        int end = page == selection.End.Page ? Math.Min(selection.End.Caret, count) : count;
        return end > start ? (start, end) : null;
    }

    private void InvalidateSelection()
    {
        foreach (var slot in _slots.Values)
        {
            slot.Layer.InvalidateVisual();
        }
    }

    // The band for one page, in its DISPLAY points: neighbouring characters
    // on one line run together into one rectangle, so the band covers the
    // spaces between words instead of stopping at every letter. A line is
    // "the same" while the boxes overlap by half their height.
    internal IEnumerable<Rect> SelectionRects(int page)
    {
        if (!_texts.TryGetValue(page, out var text) ||
            SelectedRange(page, text.Count) is not { } range)
        {
            yield break;
        }

        Rect? run = null;
        for (int i = range.Start; i < range.End; i++)
        {
            if (!text.HasBox(i))
            {
                continue;
            }
            var box = text.Box(i);
            if (run is { } current && SameLine(current, box) && box.Left >= current.Left - 1)
            {
                run = Rect.Union(current, box);
                continue;
            }
            if (run is { } done)
            {
                yield return done;
            }
            run = box;
        }
        if (run is { } last)
        {
            yield return last;
        }
    }

    private static bool SameLine(Rect a, Rect b)
    {
        double overlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return overlap > 0.5 * Math.Min(a.Height, b.Height);
    }

    // ----- the commands ------------------------------------------------------

    public void SelectAll()
    {
        if (_document is not { } document || document.PageCount == 0)
        {
            return;
        }
        _anchor = (0, 0);
        _focus = (document.PageCount - 1, int.MaxValue);
        InvalidateSelection();
    }

    public void ClearSelection()
    {
        _anchor = null;
        _focus = null;
        InvalidateSelection();
    }

    // Pages whose text is not read yet are read first - on the PDFium thread,
    // so a 300-page 전체 선택 copies without holding the UI. Pages are joined
    // with a line break, as PDFium's own line ends are.
    public async void CopySelection()
    {
        if (_document is not { } document || OrderedSelection() is not { } selection)
        {
            return;
        }

        var builder = new System.Text.StringBuilder();
        for (int page = selection.Start.Page; page <= selection.End.Page; page++)
        {
            if (!_texts.TryGetValue(page, out var text))
            {
                text = await PdfRenderService.RequestPageTextAsync(document, page);
                if (!ReferenceEquals(_document, document))
                {
                    return;
                }
                if (text is null)
                {
                    continue;
                }
                _texts[page] = text;
                _textAsked.Add(page);
            }
            if (SelectedRange(page, text.Count) is { } range)
            {
                if (builder.Length > 0)
                {
                    builder.Append("\r\n");
                }
                builder.Append(text.Slice(range.Start, range.End));
            }
        }

        if (builder.Length == 0)
        {
            return;
        }
        try
        {
            System.Windows.Clipboard.SetText(builder.ToString());
        }
        catch (ExternalException)
        {
            // Another program holding the clipboard - silent, as the app's
            // other copies are (FileOperationService.CopyPathToClipboard).
        }
    }

    // ----- the keys ----------------------------------------------------------

    // Called by the window's own key handler while the column has the
    // keyboard, ahead of its other branches (MainWindow_PreviewKeyDown); true
    // for a key answered here, false to let the window have it. Not an
    // OnKeyDown override: the window sees every key first, and several of its
    // branches want these same keys, so the column has to be asked before
    // them rather than after. The scroll keys are the ScrollViewer's own
    // steps, since the ScrollViewer itself is never focused (it would take
    // the keyboard from the tree on every click).
    public bool HandleKey(Key key)
    {
        if (_scroller is null)
        {
            return false;
        }

        // Bare keys only, apart from the chords named: Ctrl+←/→ is the tree's
        // history, and with the window now asking here first a bare-key case
        // that ignored modifiers would swallow it.
        bool none = Keyboard.Modifiers == ModifierKeys.None;
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        bool shift = Keyboard.Modifiers == ModifierKeys.Shift;
        switch (key)
        {
            case Key.C when ctrl:
                CopySelection();
                break;
            case Key.A when ctrl:
                SelectAll();
                break;
            case Key.Escape when none:
                if (HasSelection)
                {
                    ClearSelection();
                }
                else
                {
                    KeyboardReleaseRequested?.Invoke(this, EventArgs.Empty);
                }
                break;
            case Key.Down when none:
                _scroller.LineDown();
                break;
            case Key.Up when none:
                _scroller.LineUp();
                break;
            case Key.Right when none:
                _scroller.LineRight();
                break;
            case Key.Left when none:
                _scroller.LineLeft();
                break;
            case Key.PageDown when none:
            case Key.Space when none:
                _scroller.PageDown();
                break;
            case Key.PageUp when none:
            case Key.Space when shift:
                _scroller.PageUp();
                break;
            case Key.Home when ctrl || none:
                _scroller.ScrollToTop();
                break;
            case Key.End when ctrl || none:
                _scroller.ScrollToBottom();
                break;
            default:
                return false;
        }
        return true;
    }
}

// The selection band over one page - a child of the page's paper, above its
// picture, drawn from the column's selection in the page's display points
// scaled to however big the page is on screen now. Not hit-testable, so the
// column underneath takes every press.
internal sealed class PdfSelectionLayer : FrameworkElement
{
    private readonly PdfPageColumn _column;

    public PdfSelectionLayer(PdfPageColumn column, int page, double pageWidthPoints)
    {
        _column = column;
        Page = page;
        PageWidthPoints = pageWidthPoints;
        IsHitTestVisible = false;
    }

    public int Page { get; }
    public double PageWidthPoints { get; }

    protected override void OnRender(DrawingContext context)
    {
        if (ActualWidth <= 0 || PageWidthPoints <= 0)
        {
            return;
        }
        double scale = ActualWidth / PageWidthPoints;
        foreach (var rect in _column.SelectionRects(Page))
        {
            context.DrawRectangle(PdfPageColumn.SelectionFill, null,
                new Rect(rect.X * scale, rect.Y * scale, rect.Width * scale, rect.Height * scale));
        }
    }
}
