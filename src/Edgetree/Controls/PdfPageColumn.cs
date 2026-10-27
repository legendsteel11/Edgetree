using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SidebarExplorer.App.Services;
// System.Drawing and WinForms are global usings in this project (the tray
// icon), so every name the two share with WPF is pinned here.
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace SidebarExplorer.App;

// The PDF's pages as one column inside a ScrollViewer (2026-10-27).
//
// ONLY THE PAGES NEAR THE VIEW EXIST. The column measures to the whole
// document's height - every page size is known the moment the file opens - so
// the scrollbar is right from the first frame, but a child is made only for a
// page within a screen of the view, and dropped (bitmap and all) when it
// leaves. A rendered page is megabytes of pixels outside the GC heap, so a
// 300-page manual scrolled end to end must not keep 300 of them.
//
// ZOOM IS RELATIVE TO THE PANEL'S WIDTH, as the picture's is to its fit: 1.0
// is the widest page filling the column, and every page shares one scale, so
// mixed paper sizes keep their real proportions to each other. While a zoom
// is moving, the bitmaps already on screen are stretched to the new size and
// nothing is re-drawn; once it rests (ZoomSettleMs) every page in reach is
// asked for again at its new width. That is the standing rule for anything
// that runs while the mouse moves - no rendering per wheel notch.
//
// THE SCROLLBAR'S LANE IS ALWAYS RESERVED in the fit width. The app's
// ScrollViewer template opens a gutter only while the bar is visible, so a fit
// that followed the viewport would narrow the pages when the bar appeared - and
// a document just about one screen tall would then stop needing the bar, widen
// again and need it again, every layout pass. Reserving the lane costs 13px on
// a document short enough to have no bar, and breaks the loop for good.
public class PdfPageColumn : System.Windows.Controls.Panel
{
    // Space around and between pages, in DIPs - the picture's own 10px margin.
    private const double Pad = 10;
    private const double Gap = 10;

    public const double MinZoom = 0.25;
    public const double MaxZoom = 4.0;
    private const int ZoomSettleMs = 150;

    private sealed class PageSlot
    {
        public required Border Paper { get; init; }
        public required Image Image { get; init; }
        // The pixel width now on screen, and the one asked for and not back
        // yet; 0 for none. A request for the width already asked is not sent
        // twice, and a bitmap of a width nobody wants any more is thrown away.
        public int ShownWidth;
        public int RequestedWidth;
    }

    private PdfRenderService.Document? _document;
    private readonly Dictionary<int, PageSlot> _slots = new();
    private ScrollViewer? _scroller;
    private double _zoom = 1.0;
    private bool _zoomSettling;
    private readonly DispatcherTimer _zoomSettle;

    // Layout in DIPs, recomputed by ComputeLayout: each page's rectangle in the
    // column, and the column's whole size.
    private Rect[] _pageRects = Array.Empty<Rect>();
    private Size _extent;
    private double _layoutWidth = -1;
    private double _layoutZoom = -1;
    // DIPs per PDF point, from the last layout.
    private double _scale;

    // Pan by dragging the page.
    private Point? _panStart;
    private double _panStartX, _panStartY;

    public PdfPageColumn()
    {
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        // The paper shows only where a page is; between pages the panel's own
        // ground shows through, but the whole column still takes the pointer
        // so a drag that starts in a gap pans like one that starts on paper.
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;

        _zoomSettle = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(ZoomSettleMs),
        };
        _zoomSettle.Tick += (_, _) =>
        {
            _zoomSettle.Stop();
            _zoomSettling = false;
            if (_document is { } document)
            {
                // Every queued width is the old zoom's.
                PdfRenderService.CancelPages(document);
            }
            RefreshSlots();
        };

        Loaded += (_, _) => AttachScroller();
        Unloaded += (_, _) => DetachScroller();
    }

    // The page whose area is under the middle of the view, 0-based; -1 with
    // no document. Raised whenever it changes, for the caption's "3 / 96".
    public int PageInView { get; private set; } = -1;
    public event EventHandler? PageInViewChanged;

    // Raised when the zoom changes, or the scale it gives does (a new panel
    // width, the first layout) - for the zoom readout.
    public event EventHandler? ZoomChanged;

    public double Zoom => _zoom;

    // The pages against their real paper size: 100 is an A4 page as wide on
    // screen as on a desk. A point is 1/72 inch and a DIP 1/96, hence 75. This
    // is the number PDF readers show, and the one that says something - "the
    // fit" is a different size on every panel.
    public double PaperPercent => _scale * 75;
    public PdfRenderService.Document? Document => _document;

    // Hands the column a newly opened document (or null to clear it). The
    // previous document's queued pages are dropped; closing it is the
    // caller's, which owns its lifetime.
    public void SetDocument(PdfRenderService.Document? document)
    {
        if (_document is { } old)
        {
            PdfRenderService.CancelPages(old);
        }
        foreach (var slot in _slots.Values)
        {
            Children.Remove(slot.Paper);
        }
        _slots.Clear();

        _document = document;
        _zoom = 1.0;
        _zoomSettle.Stop();
        _zoomSettling = false;
        _layoutWidth = -1;
        PageInView = -1;
        _scroller?.ScrollToHorizontalOffset(0);
        _scroller?.ScrollToVerticalOffset(0);
        InvalidateMeasure();
        // Pages are made from ScrollChanged, which a new document of exactly
        // the old one's height and position would never raise; asked once
        // after the layout instead, so the first pages never wait on it.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RefreshSlots);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    // ----- layout ------------------------------------------------------------

    private double FitWidth()
    {
        if (_scroller is null || _scroller.ActualWidth <= 0)
        {
            return 0;
        }
        // The gutter the app's ScrollViewer template opens for the bar (the
        // bar's thickness plus its 1px line, re-set on every Ctrl+/- step).
        double lane = TryFindResource("ScrollGutterWidth") is GridLength gutter ? gutter.Value : 13;
        return Math.Max(40, _scroller.ActualWidth - lane - 2 * Pad);
    }

    private void ComputeLayout()
    {
        double fit = FitWidth();
        if (_document is not { } document || fit <= 0)
        {
            _pageRects = Array.Empty<Rect>();
            _extent = new Size(0, 0);
            _layoutWidth = fit;
            _layoutZoom = _zoom;
            return;
        }
        if (fit == _layoutWidth && _zoom == _layoutZoom && _pageRects.Length == document.PageCount)
        {
            return;
        }

        double widest = 0;
        foreach (var size in document.PageSizes)
        {
            widest = Math.Max(widest, size.Width);
        }
        double scale = fit * _zoom / widest;
        double columnWidth = widest * scale + 2 * Pad;
        // The view less the bar's lane, NOT the ScrollViewer's ViewportWidth:
        // the viewport narrows only after the bar has appeared, a pass after
        // this measure, so sizing to it left the column one lane too wide -
        // and a horizontal bar standing at the fit. Reserving the lane keeps
        // the column inside the view whether or not the bar is up.
        double viewWidth = fit + 2 * Pad;
        double contentWidth = Math.Max(columnWidth, viewWidth);

        var rects = new Rect[document.PageCount];
        double y = Pad;
        for (int i = 0; i < rects.Length; i++)
        {
            double w = document.PageSizes[i].Width * scale;
            double h = document.PageSizes[i].Height * scale;
            // Centred in whatever is wider of the column and the view, so a
            // page zoomed out below the fit sits in the middle of the panel.
            rects[i] = new Rect((contentWidth - w) / 2, y, w, h);
            y += h + Gap;
        }

        _pageRects = rects;
        _extent = new Size(contentWidth, y - Gap + Pad);
        _layoutWidth = fit;
        _layoutZoom = _zoom;

        if (scale != _scale)
        {
            _scale = scale;
            // Not raised from inside the measure pass: the readout is another
            // element's text, and changing it here would re-enter layout.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ZoomChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        ComputeLayout();
        foreach (var slot in _slots.Values)
        {
            slot.Paper.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        return _extent;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (index, slot) in _slots)
        {
            if (index < _pageRects.Length)
            {
                var rect = _pageRects[index];
                slot.Paper.Width = rect.Width;
                slot.Paper.Height = rect.Height;
                slot.Paper.Arrange(rect);
            }
        }
        return finalSize;
    }

    // ----- the scroller ------------------------------------------------------

    private void AttachScroller()
    {
        DependencyObject? node = VisualTreeHelper.GetParent(this);
        while (node is not null and not ScrollViewer)
        {
            node = VisualTreeHelper.GetParent(node);
        }
        _scroller = node as ScrollViewer;
        if (_scroller is not null)
        {
            _scroller.ScrollChanged += Scroller_ScrollChanged;
            _scroller.SizeChanged += Scroller_SizeChanged;
            InvalidateMeasure();
        }
    }

    private void DetachScroller()
    {
        if (_scroller is not null)
        {
            _scroller.ScrollChanged -= Scroller_ScrollChanged;
            _scroller.SizeChanged -= Scroller_SizeChanged;
        }
        _scroller = null;
    }

    private void Scroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // A new panel width is a new fit: the pages re-scale at once and are
        // re-drawn once the resize rests, exactly as a zoom does.
        InvalidateMeasure();
        BeginSettle();
    }

    private void Scroller_ScrollChanged(object sender, ScrollChangedEventArgs e) => RefreshSlots();

    // ----- which pages exist -------------------------------------------------

    private void RefreshSlots()
    {
        if (_document is not { } document || _scroller is null || _pageRects.Length == 0)
        {
            return;
        }

        double top = _scroller.VerticalOffset;
        double height = _scroller.ViewportHeight;
        // A screen above and a screen below the view: enough that an ordinary
        // wheel step lands on pages already drawn.
        double reachTop = top - height;
        double reachBottom = top + 2 * height;

        int first = FirstPageBelow(reachTop);
        int last = first;
        while (last + 1 < _pageRects.Length && _pageRects[last + 1].Top < reachBottom)
        {
            last++;
        }

        foreach (int index in _slots.Keys.ToList())
        {
            if (index < first || index > last)
            {
                var slot = _slots[index];
                // The bitmap goes with the child; dropping the reference is
                // what lets WPF free the pixels.
                slot.Image.Source = null;
                Children.Remove(slot.Paper);
                _slots.Remove(index);
            }
        }

        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        for (int index = first; index <= last; index++)
        {
            if (!_slots.TryGetValue(index, out var slot))
            {
                var image = new Image
                {
                    Stretch = Stretch.Fill,
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                slot = new PageSlot
                {
                    Image = image,
                    Paper = new Border
                    {
                        Background = Brushes.White,
                        Child = image,
                    },
                };
                _slots[index] = slot;
                Children.Add(slot.Paper);
            }

            // A page already showing a bitmap keeps it while a zoom moves; one
            // with nothing yet is drawn now at the current width either way.
            if (_zoomSettling && slot.ShownWidth > 0)
            {
                continue;
            }
            int wanted = Math.Max(1, (int)Math.Round(_pageRects[index].Width * dpi));
            if (slot.ShownWidth == wanted || slot.RequestedWidth == wanted)
            {
                continue;
            }
            slot.RequestedWidth = wanted;
            RequestPage(document, index, wanted);
        }

        UpdatePageInView();
    }

    private void RequestPage(PdfRenderService.Document document, int index, int width)
    {
        PdfRenderService.RequestPage(document, index, width, bitmap =>
        {
            // Answers for a document that has since left, a page that has
            // scrolled away, or a width a later zoom replaced are dropped.
            if (!ReferenceEquals(_document, document) ||
                !_slots.TryGetValue(index, out var slot) ||
                slot.RequestedWidth != width)
            {
                return;
            }
            slot.RequestedWidth = 0;
            if (bitmap is null)
            {
                return;
            }
            slot.Image.Source = bitmap;
            slot.ShownWidth = width;
        });
    }

    // The first page whose bottom is below y (binary search; the rects are in
    // order down the column).
    private int FirstPageBelow(double y)
    {
        int lo = 0, hi = _pageRects.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_pageRects[mid].Bottom < y)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        return lo;
    }

    private void UpdatePageInView()
    {
        if (_scroller is null || _pageRects.Length == 0)
        {
            return;
        }
        // The middle of the view, or the last page once the column's end is
        // on screen - otherwise a short final page could never be "in view".
        int page = _scroller.VerticalOffset + _scroller.ViewportHeight >= _scroller.ExtentHeight - 1
            ? _pageRects.Length - 1
            : FirstPageBelow(_scroller.VerticalOffset + _scroller.ViewportHeight / 2);
        if (page != PageInView)
        {
            PageInView = page;
            PageInViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ----- zoom --------------------------------------------------------------

    // Zooms keeping the document point under `anchor` (in the scroller's
    // viewport coordinates) where it is - the picture's wheel zoom behaves the
    // same way, so the two read as one gesture.
    public void ZoomTo(double zoom, Point anchor)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (_document is null || _scroller is null || Math.Abs(zoom - _zoom) < 0.0001)
        {
            return;
        }

        ComputeLayout();
        var content = new Point(_scroller.HorizontalOffset + anchor.X, _scroller.VerticalOffset + anchor.Y);
        int page = Math.Min(FirstPageBelow(content.Y), _pageRects.Length - 1);
        var before = _pageRects[page];
        double fx = before.Width > 0 ? (content.X - before.Left) / before.Width : 0.5;
        double fy = before.Height > 0 ? (content.Y - before.Top) / before.Height : 0;

        _zoom = zoom;
        ComputeLayout();
        var after = _pageRects[page];

        InvalidateMeasure();
        // Offsets land after the measure above has grown the extent; the
        // ScrollViewer clamps them against the NEW size.
        _scroller.UpdateLayout();
        _scroller.ScrollToHorizontalOffset(after.Left + fx * after.Width - anchor.X);
        _scroller.ScrollToVerticalOffset(after.Top + fy * after.Height - anchor.Y);

        BeginSettle();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    // One wheel notch = 10%, compounded, as the picture's zoom steps.
    public void ZoomByWheel(int delta, Point anchor)
        => ZoomTo(_zoom * Math.Pow(1.1, delta / 120.0), anchor);

    // Back to the fit, anchored on the top of the view so the page being read
    // stays the page being read.
    public void ResetZoom() => ZoomTo(1.0, new Point(0, 0));

    // The zoom row's buttons and bare +/-: a quarter at a time, on the middle
    // of the view, since neither has a pointer position to keep.
    public void ZoomByStep(int direction)
    {
        if (_scroller is null)
        {
            return;
        }
        ZoomTo(_zoom * (direction > 0 ? 1.25 : 0.8),
            new Point(_scroller.ViewportWidth / 2, _scroller.ViewportHeight / 2));
    }

    private void BeginSettle()
    {
        _zoomSettling = true;
        _zoomSettle.Stop();
        _zoomSettle.Start();
    }

    // ----- pan ---------------------------------------------------------------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_scroller is null)
        {
            return;
        }
        // Double-click is back to the fit - the picture's double-click is its
        // fit/1:1 toggle, and the fit is the state a document rests in.
        if (e.ClickCount == 2)
        {
            ResetZoom();
            e.Handled = true;
            return;
        }
        _panStart = e.GetPosition(_scroller);
        _panStartX = _scroller.HorizontalOffset;
        _panStartY = _scroller.VerticalOffset;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panStart is not { } start || _scroller is null || !IsMouseCaptured)
        {
            return;
        }
        var now = e.GetPosition(_scroller);
        _scroller.ScrollToHorizontalOffset(_panStartX - (now.X - start.X));
        _scroller.ScrollToVerticalOffset(_panStartY - (now.Y - start.Y));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_panStart is not null)
        {
            _panStart = null;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _panStart = null;
    }
}
