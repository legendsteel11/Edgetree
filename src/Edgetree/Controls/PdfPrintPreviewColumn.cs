using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

// The PDF print preview's pages (2026-10-27, on request after printing
// arrived): every page on the paper it will print on - the default printer's
// paper and printable area - fitted, centred and turned by the very rule the
// print uses (PdfRenderService.FitOnPaper), so the preview cannot put a page
// where the printer will not. Rendered with FPDF_PRINTING, so what will not
// print does not show.
//
// ITS OWN SMALL COLUMN, not the panel's PdfPageColumn in another mode: the
// panel's has text selection, reload and keyboard rules the preview has no
// use for, and every page here is one paper size, so where a page sits is
// arithmetic rather than a table. The parts that matter are the same: only
// the pages within a screen of the view exist, a page leaving that reach
// gives up its bitmap and its queued render, and a zoom stretches what is
// drawn and draws it again once it rests. Its renders carry this column as
// their owner, so leaving a page here never drops the panel's ask for it -
// the two share one document.
//
// Ctrl+wheel zooms about the pointer, a drag pans, the wheel and the
// scrollbars scroll; the window owns the keys.
public sealed class PdfPrintPreviewColumn : System.Windows.Controls.Panel
{
    private const double Pad = 14;
    private const double Gap = 14;
    private const double MinZoom = 0.5;
    private const double MaxZoom = 4;

    private readonly PdfRenderService.Document _document;
    // In the paper's own units (hundredths of an inch); only proportions
    // matter here.
    private readonly Size _paper;
    private readonly Rect _printable;
    private readonly Dictionary<int, Slot> _slots = new();
    private readonly DispatcherTimer _settle;
    private ScrollViewer? _scroller;
    private double _zoom = 1;
    // DIPs per paper unit, and the paper and column as laid out.
    private double _scale;
    private double _paperWidth, _paperHeight, _contentWidth;
    private bool _settling;
    private Point? _panStart;
    private double _panStartX, _panStartY;

    private sealed class Slot
    {
        public required Border Paper { get; init; }
        public required Image Image { get; init; }
        // Where the page sits on its paper, in paper units, and whether it
        // is turned a quarter to get there.
        public required Rect Place { get; init; }
        public required bool Turn { get; init; }
        public int ShownWidth;
        public int RequestedWidth;
    }

    public PdfPrintPreviewColumn(PdfRenderService.Document document, Size paper, Rect printable)
    {
        _document = document;
        _paper = paper;
        _printable = printable;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        // The whole column takes the pointer, gaps included, for the pan.
        Background = Brushes.Transparent;

        _settle = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            _settling = false;
            // Every queued width is the old zoom's, and none is coming back
            // now, so no page may go on waiting for one.
            foreach (var (index, slot) in _slots)
            {
                if (slot.RequestedWidth != 0)
                {
                    PdfRenderService.CancelPage(_document, index, this);
                    slot.RequestedWidth = 0;
                }
            }
            RefreshSlots();
        };

        Loaded += (_, _) => AttachScroller();
        Unloaded += (_, _) =>
        {
            DetachScroller();
            ClearSlots();
        };
    }

    public int PageCount => _document.PageCount;

    // The page under the middle of the view, 0-based: the first while the
    // view is at the top, the last once it is scrolled to the end - the same
    // rule as the panel's caption.
    public int PageInView { get; private set; }
    public event EventHandler? PageInViewChanged;

    // ----- layout ------------------------------------------------------------

    private void ComputeLayout()
    {
        double fit = 0;
        if (_scroller is { ActualWidth: > 0 } scroller)
        {
            double lane = TryFindResource("ScrollGutterWidth") is GridLength gutter ? gutter.Value : 13;
            fit = Math.Max(40, scroller.ActualWidth - lane - 2 * Pad);
        }
        _scale = fit > 0 && _paper.Width > 0 ? fit * _zoom / _paper.Width : 0;
        _paperWidth = _paper.Width * _scale;
        _paperHeight = _paper.Height * _scale;
        _contentWidth = Math.Max(_paperWidth + 2 * Pad, fit + 2 * Pad);
    }

    private double Step => _paperHeight + Gap;

    private Rect PaperRect(int index)
        => new((_contentWidth - _paperWidth) / 2, Pad + index * Step, _paperWidth, _paperHeight);

    protected override Size MeasureOverride(Size availableSize)
    {
        ComputeLayout();
        foreach (var slot in _slots.Values)
        {
            slot.Paper.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        int count = PageCount;
        return _scale <= 0 || count == 0
            ? new Size(0, 0)
            : new Size(_contentWidth, 2 * Pad + count * _paperHeight + (count - 1) * Gap);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (index, slot) in _slots)
        {
            var rect = PaperRect(index);
            slot.Paper.Width = rect.Width;
            slot.Paper.Height = rect.Height;
            // Unturned, the bitmap's own width runs across; turned, it runs
            // down the paper and the layout transform swaps the two.
            double across = slot.Place.Width * _scale;
            double down = slot.Place.Height * _scale;
            slot.Image.Width = slot.Turn ? down : across;
            slot.Image.Height = slot.Turn ? across : down;
            Canvas.SetLeft(slot.Image, slot.Place.X * _scale);
            Canvas.SetTop(slot.Image, slot.Place.Y * _scale);
            slot.Paper.Arrange(rect);
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
            _scroller.PreviewMouseWheel += Scroller_PreviewMouseWheel;
            InvalidateMeasure();
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RefreshSlots);
        }
    }

    private void DetachScroller()
    {
        if (_scroller is not null)
        {
            _scroller.ScrollChanged -= Scroller_ScrollChanged;
            _scroller.SizeChanged -= Scroller_SizeChanged;
            _scroller.PreviewMouseWheel -= Scroller_PreviewMouseWheel;
        }
        _scroller = null;
    }

    private void Scroller_ScrollChanged(object sender, ScrollChangedEventArgs e) => RefreshSlots();

    private void Scroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        InvalidateMeasure();
        BeginSettle();
    }

    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_scroller is not null && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ZoomTo(_zoom * Math.Pow(1.1, e.Delta / 120.0), e.GetPosition(_scroller));
            e.Handled = true;
        }
    }

    // ----- which pages exist -------------------------------------------------

    private void RefreshSlots()
    {
        if (_scroller is null || _scale <= 0 || PageCount == 0)
        {
            return;
        }

        double top = _scroller.VerticalOffset;
        double height = _scroller.ViewportHeight;
        // A screen above and a screen below the view, as in the panel.
        int first = Math.Clamp((int)Math.Floor((top - height - Pad) / Step), 0, PageCount - 1);
        int last = Math.Clamp((int)Math.Floor((top + 2 * height - Pad) / Step), 0, PageCount - 1);

        foreach (int index in _slots.Keys.ToList())
        {
            if (index < first || index > last)
            {
                RemoveSlot(index);
            }
        }

        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        for (int index = first; index <= last; index++)
        {
            if (!_slots.TryGetValue(index, out var slot))
            {
                slot = CreateSlot(index);
                _slots[index] = slot;
                Children.Add(slot.Paper);
            }

            // A page already showing a bitmap keeps it while a zoom moves.
            if (_settling && slot.ShownWidth > 0)
            {
                continue;
            }
            double bitmapWidth = (slot.Turn ? slot.Place.Height : slot.Place.Width) * _scale;
            int wanted = Math.Max(1, (int)Math.Round(bitmapWidth * dpi));
            if (slot.ShownWidth == wanted || slot.RequestedWidth == wanted)
            {
                continue;
            }
            slot.RequestedWidth = wanted;
            RequestPage(index, slot, wanted);
        }

        UpdatePageInView();
    }

    private Slot CreateSlot(int index)
    {
        var (place, turn) = PdfRenderService.FitOnPaper(_document.PageSizes[index], _printable.Size);
        place.Offset(_printable.X, _printable.Y);
        var image = new Image
        {
            Stretch = Stretch.Fill,
            // Clockwise, as PDFium's rotate 1 turns the printed page.
            LayoutTransform = turn ? new RotateTransform(90) : Transform.Identity,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var canvas = new Canvas { ClipToBounds = true };
        canvas.Children.Add(image);
        return new Slot
        {
            Image = image,
            Place = place,
            Turn = turn,
            Paper = new Border
            {
                Background = Brushes.White,
                Child = canvas,
            },
        };
    }

    private void RequestPage(int index, Slot slot, int width)
    {
        PdfRenderService.RequestPage(_document, index, width, bitmap =>
        {
            // A page that has left, or a width a later zoom replaced.
            if (!_slots.TryGetValue(index, out var current) ||
                !ReferenceEquals(current, slot) ||
                slot.RequestedWidth != width)
            {
                return;
            }
            slot.RequestedWidth = 0;
            if (bitmap is not null)
            {
                slot.Image.Source = bitmap;
                slot.ShownWidth = width;
            }
        }, owner: this, printing: true);
    }

    private void RemoveSlot(int index)
    {
        var slot = _slots[index];
        // Dropping the reference is what lets WPF free the pixels.
        slot.Image.Source = null;
        Children.Remove(slot.Paper);
        _slots.Remove(index);
        PdfRenderService.CancelPage(_document, index, this);
    }

    private void ClearSlots()
    {
        foreach (int index in _slots.Keys.ToList())
        {
            RemoveSlot(index);
        }
    }

    private void UpdatePageInView()
    {
        if (_scroller is null || PageCount == 0 || Step <= 0)
        {
            return;
        }
        int page = _scroller.VerticalOffset <= 0.5
            ? 0
            : _scroller.VerticalOffset + _scroller.ViewportHeight >= _scroller.ExtentHeight - 1
                ? PageCount - 1
                : Math.Clamp((int)Math.Floor((_scroller.VerticalOffset + _scroller.ViewportHeight / 2 - Pad) / Step),
                    0, PageCount - 1);
        if (page != PageInView)
        {
            PageInView = page;
            PageInViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ----- zoom --------------------------------------------------------------

    // Keeps the paper point under the anchor (viewport coordinates) where it
    // is: the page index and the place within that page carry over, since
    // the gaps between pages do not scale with them.
    private void ZoomTo(double zoom, Point anchor)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (_scroller is null || _scale <= 0 || Math.Abs(zoom - _zoom) < 0.0001)
        {
            return;
        }

        double y = _scroller.VerticalOffset + anchor.Y - Pad;
        int page = Math.Clamp((int)Math.Floor(y / Step), 0, PageCount - 1);
        double within = y - page * Step;
        double x = _scroller.HorizontalOffset + anchor.X - _contentWidth / 2;
        double oldScale = _scale;

        _zoom = zoom;
        ComputeLayout();
        double ratio = _scale / oldScale;

        InvalidateMeasure();
        _scroller.UpdateLayout();
        _scroller.ScrollToVerticalOffset(Pad + page * Step + Math.Min(within * ratio, _paperHeight) - anchor.Y);
        _scroller.ScrollToHorizontalOffset(_contentWidth / 2 + x * ratio - anchor.X);
        BeginSettle();
    }

    private void BeginSettle()
    {
        _settling = true;
        _settle.Stop();
        _settle.Start();
    }

    // ----- pan ---------------------------------------------------------------

    private bool CanPan =>
        _scroller is not null &&
        (_scroller.ExtentHeight > _scroller.ViewportHeight + 0.5 ||
         _scroller.ExtentWidth > _scroller.ViewportWidth + 0.5);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_scroller is null)
        {
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
        if (_panStart is { } start && _scroller is not null && IsMouseCaptured)
        {
            var now = e.GetPosition(_scroller);
            _scroller.ScrollToHorizontalOffset(_panStartX - (now.X - start.X));
            _scroller.ScrollToVerticalOffset(_panStartY - (now.Y - start.Y));
            return;
        }
        Cursor = CanPan ? Cursors.Hand : null;
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
