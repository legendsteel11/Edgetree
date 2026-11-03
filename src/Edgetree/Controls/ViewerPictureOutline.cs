using System.Windows;
using System.Windows.Media;
// System.Drawing and WinForms are global usings in this project (the tray
// icon), so every name the two share with WPF is pinned here.
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;

namespace SidebarExplorer.App;

// THE PICTURE'S EDGE, ONE DEVICE PIXEL OUTSIDE IT (2026-11-03, on request:
// with crop in the panel, a picture whose edge is the colour of the panel's
// ground no longer shows where it ends - and where it ends is where a crop
// stops). Draws and nothing else, as ViewerCropOverlay does: MainWindow tells
// it the picture's rectangle as DRAWN, measured in this element's
// coordinates, on every zoom, pan frame and resize, and the line takes the
// pixels next to it. Hit testing is off; the host underneath takes every
// press.
//
// TOUCHING, NOT OVERLAPPING AND NOT ONE PIXEL OFF - the first version was
// both, on two sides of one picture (2026-11-03, the author's loupe, read
// pixel by pixel): a line tinted with the picture's colours along the bottom
// and a pixel of ground between line and picture along the top. Two causes.
// The rectangle was DERIVED from the zoom, and layout rounding had put the
// picture under a pixel elsewhere; it is measured now. And a picture at a
// zoom that is not whole ends part way through a pixel, which the default
// rendering blends with the ground - a half-tone pixel that reads as a gap
// on one picture and as nothing on the next. So while this is on, the
// picture is drawn with aliased edges (MainWindow sets EdgeMode): it covers
// exactly the pixels whose centres it covers, which is the nearest whole
// pixel at each edge, and the line goes on the next one out.
//
// A THEME BRUSH (ViewerPictureOutlineBrush), not the crop overlay's fixed
// colours: this line lies on the panel's ground, not on the photo. Not the
// divider brush either, which goes clear when 영역 구분선 is switched off.
//
// A frame is one rectangle - no layout pass, no decode.
internal sealed class ViewerPictureOutline : FrameworkElement
{
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(ViewerPictureOutline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    private Rect? _picture;

    // No SnapsToDevicePixels: the bars are placed on the device grid by hand
    // (OnRender), and a second snap on top could move them off it.
    public ViewerPictureOutline()
    {
        IsHitTestVisible = false;
    }

    // The picture's rectangle in this element's coordinates, or null for none.
    // Unchanged is common - a pan frame along one axis of a picture that fits
    // the other - and costs nothing.
    public void Show(Rect? picture)
    {
        if (_picture == picture)
        {
            return;
        }

        _picture = picture;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_picture is not { } picture || Stroke is not { } stroke ||
            picture.Width <= 0 || picture.Height <= 0)
        {
            return;
        }

        // To the NEAREST whole device pixel, the edge the aliased picture
        // actually has (see above) - ON THE SCREEN'S GRID, not this element's.
        // The panel is laid out in DIPs and this element's origin need not
        // fall on a whole pixel; rounded in local units, the left side came
        // out as a 40% smear over two pixels beside a picture that sat on a
        // whole one (measured 2026-11-03, screen captured at fit).
        var dpi = VisualTreeHelper.GetDpi(this);
        Point origin = default;
        if (PresentationSource.FromVisual(this)?.RootVisual is Visual root)
        {
            origin = TransformToAncestor(root).Transform(default);
        }
        double offsetX = origin.X * dpi.DpiScaleX;
        double offsetY = origin.Y * dpi.DpiScaleY;
        double SnapX(double x) => (Math.Round(x * dpi.DpiScaleX + offsetX) - offsetX) / dpi.DpiScaleX;
        double SnapY(double y) => (Math.Round(y * dpi.DpiScaleY + offsetY) - offsetY) / dpi.DpiScaleY;

        double left = SnapX(picture.Left);
        double top = SnapY(picture.Top);
        double right = SnapX(picture.Right);
        double bottom = SnapY(picture.Bottom);

        // Four one-pixel bars on the pixels next to the picture rather than a
        // stroked rectangle: a fill on whole pixels has nothing to blend, and
        // the two axes may scale differently.
        double pixelX = 1 / dpi.DpiScaleX;
        double pixelY = 1 / dpi.DpiScaleY;
        dc.DrawRectangle(stroke, null, new Rect(left - pixelX, top - pixelY, right - left + 2 * pixelX, pixelY));
        dc.DrawRectangle(stroke, null, new Rect(left - pixelX, bottom, right - left + 2 * pixelX, pixelY));
        dc.DrawRectangle(stroke, null, new Rect(left - pixelX, top, pixelX, bottom - top));
        dc.DrawRectangle(stroke, null, new Rect(right, top, pixelX, bottom - top));
    }
}
