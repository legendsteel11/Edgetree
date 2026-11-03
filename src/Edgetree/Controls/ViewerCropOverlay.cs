using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
// System.Drawing and WinForms are global usings in this project (the tray
// icon), so every name the two share with WPF is pinned here.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace SidebarExplorer.App;

// Which part of the crop rectangle a point is over - what a press there will
// move. Corners before edges, edges before the inside.
internal enum CropPart
{
    None,
    Inside,
    Left,
    Top,
    Right,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

// THE CROP RECTANGLE OVER THE PANEL'S PICTURE (2026-11-03). It draws and
// nothing else: the rectangle, the dimming outside it, its eight handles and
// the size it would cut. Where the rectangle is comes from MainWindow, which
// keeps it in the picture's own pixels and maps it into this element on every
// zoom, pan and resize - this element is told a rectangle in its own
// coordinates and knows nothing about the picture. Hit testing is off, so
// every press reaches the picture host underneath and the host decides what
// the press means; HitPart only answers which part of the rectangle is under
// a point.
//
// FIXED COLOURS, like the navigator plate: what shows through is the photo,
// so a theme brush would vanish on half of them. The outline is two lines laid
// on each other - black under a white dash - for the reason the thumbnail's
// selected frame carries two hairlines: a photo cannot be both.
//
// Redrawn on every move of a drag, every zoom step and every pan frame. A
// frame is one combined geometry, two rectangles, eight small squares and one
// short line of text - no layout pass, no decode.
internal sealed class ViewerCropOverlay : FrameworkElement
{
    private const double HandleSize = 7;
    // How far outside a handle or an edge a press still takes it. Wider than
    // the handle itself: a 7px square is a small target for a hand.
    private const double GrabTolerance = 6;

    private static readonly Brush DimBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)));
    private static readonly Brush LabelPlateBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xCC, 0x18, 0x18, 0x18)));
    private static readonly Pen DarkPen = Frozen(new Pen(Brushes.Black, 1));
    private static readonly Pen DashPen = Frozen(new Pen(Brushes.White, 1)
    {
        DashStyle = new DashStyle(new double[] { 4, 4 }, 0),
    });

    public ViewerCropOverlay()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    // The rectangle in this element's coordinates, or null for none yet.
    public Rect? Selection { get; private set; }

    private string? _label;

    public void Show(Rect? selection, string? label)
    {
        Selection = selection;
        _label = label;
        InvalidateVisual();
    }

    public CropPart HitPart(Point point)
    {
        if (Selection is not { } r)
        {
            return CropPart.None;
        }

        double reach = HandleSize / 2 + GrabTolerance;
        bool nearLeft = Math.Abs(point.X - r.Left) <= reach;
        bool nearRight = Math.Abs(point.X - r.Right) <= reach;
        bool nearTop = Math.Abs(point.Y - r.Top) <= reach;
        bool nearBottom = Math.Abs(point.Y - r.Bottom) <= reach;
        bool withinX = point.X >= r.Left - reach && point.X <= r.Right + reach;
        bool withinY = point.Y >= r.Top - reach && point.Y <= r.Bottom + reach;

        // A rectangle drawn very small puts its opposite edges inside each
        // other's reach; the nearer one wins, so it can still be pulled open.
        if (nearLeft && nearRight)
        {
            nearLeft = Math.Abs(point.X - r.Left) <= Math.Abs(point.X - r.Right);
            nearRight = !nearLeft;
        }
        if (nearTop && nearBottom)
        {
            nearTop = Math.Abs(point.Y - r.Top) <= Math.Abs(point.Y - r.Bottom);
            nearBottom = !nearTop;
        }

        if (nearTop && nearLeft) return CropPart.TopLeft;
        if (nearTop && nearRight) return CropPart.TopRight;
        if (nearBottom && nearLeft) return CropPart.BottomLeft;
        if (nearBottom && nearRight) return CropPart.BottomRight;
        if (nearLeft && withinY) return CropPart.Left;
        if (nearRight && withinY) return CropPart.Right;
        if (nearTop && withinX) return CropPart.Top;
        if (nearBottom && withinX) return CropPart.Bottom;
        return r.Contains(point) ? CropPart.Inside : CropPart.None;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Selection is not { } selection || RenderSize.Width <= 0 || RenderSize.Height <= 0)
        {
            return;
        }

        // On the device grid, so the 1px lines are one pixel and not two half
        // ones: the outline is the whole point of this element.
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Rect r = Snap(selection, dpi);

        dc.DrawGeometry(DimBrush, null, new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(RenderSize)), new RectangleGeometry(r)));

        double half = 0.5 / dpi;
        var line = new Rect(r.X + half, r.Y + half, Math.Max(0, r.Width - 2 * half), Math.Max(0, r.Height - 2 * half));
        dc.DrawRectangle(null, DarkPen, line);
        dc.DrawRectangle(null, DashPen, line);

        // ONE SIZE IN DEVICE PIXELS, and only the centre placed. Snapping each
        // handle's four sides on their own let a side round up where its
        // opposite rounded down, so handles came out a pixel apart in width or
        // height depending on where they fell - read as squares of slightly
        // different sizes, some not square (2026-11-03). Odd, so the square
        // centres on a pixel - the pixel the outline runs through, which is
        // why the centres are taken from the line's own rectangle (its sides
        // sit mid-pixel, so the floor is that pixel on all four sides).
        int sizePixels = (int)Math.Round(HandleSize * dpi);
        if (sizePixels % 2 == 0)
        {
            sizePixels++;
        }
        double side = sizePixels / dpi;
        int reachPixels = sizePixels / 2;
        foreach (var centre in HandleCentres(line))
        {
            var handle = new Rect(
                (Math.Floor(centre.X * dpi) - reachPixels) / dpi,
                (Math.Floor(centre.Y * dpi) - reachPixels) / dpi,
                side,
                side);
            dc.DrawRectangle(Brushes.White, null, handle);
            dc.DrawRectangle(null, DarkPen, new Rect(handle.X + half, handle.Y + half,
                handle.Width - 2 * half, handle.Height - 2 * half));
        }

        if (_label is { Length: > 0 } label)
        {
            DrawLabel(dc, label, r);
        }
    }

    // Above the rectangle's top-left corner, or just inside it when the
    // rectangle starts at the top of the panel.
    private void DrawLabel(DrawingContext dc, string label, Rect r)
    {
        var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            TextElement.GetFontSize(this), Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        const double padX = 5, padY = 2, gap = 4;
        var plate = new Size(text.Width + 2 * padX, text.Height + 2 * padY);
        double x = Math.Clamp(r.Left, 0, Math.Max(0, RenderSize.Width - plate.Width));
        double y = r.Top - plate.Height - gap;
        if (y < 0)
        {
            y = r.Top + gap + HandleSize;
        }

        dc.DrawRoundedRectangle(LabelPlateBrush, null, new Rect(new Point(x, y), plate), 3, 3);
        dc.DrawText(text, new Point(x + padX, y + padY));
    }

    private static IEnumerable<Point> HandleCentres(Rect r)
    {
        double midX = r.Left + r.Width / 2;
        double midY = r.Top + r.Height / 2;
        yield return new Point(r.Left, r.Top);
        yield return new Point(midX, r.Top);
        yield return new Point(r.Right, r.Top);
        yield return new Point(r.Right, midY);
        yield return new Point(r.Right, r.Bottom);
        yield return new Point(midX, r.Bottom);
        yield return new Point(r.Left, r.Bottom);
        yield return new Point(r.Left, midY);
    }

    private static Rect Snap(Rect r, double dpi)
    {
        double left = Math.Round(r.Left * dpi) / dpi;
        double top = Math.Round(r.Top * dpi) / dpi;
        double right = Math.Round(r.Right * dpi) / dpi;
        double bottom = Math.Round(r.Bottom * dpi) / dpi;
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
