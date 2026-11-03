using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SidebarExplorer.App.Services;
// System.Drawing and WinForms are global usings in this project (the tray
// icon), so every name the two share with WPF is pinned here.
using Clipboard = System.Windows.Clipboard;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using DataObject = System.Windows.DataObject;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace SidebarExplorer.App;

// ===== Cropping the panel's picture (2026-11-03, the author's design) =====
//
// The first of two stages: the bar over a picture, crop mode, a rectangle
// drawn and adjusted over the picture, and Ctrl+C copying what it covers. The
// second adds the temporary crop and the two saves; their buttons stand in the
// bar greyed until then. The whole design, with what was settled and why, is
// in the project notes on the crop round.
//
// THE RECTANGLE LIVES IN THE PICTURE'S OWN PIXELS - the original's, as shown,
// EXIF orientation applied - never in screen coordinates. The panel draws a
// panel-width decode until a zoom asks for more, it zooms, pans, fills and
// resizes, and a rectangle kept on the screen would drift off its subject
// through every one of those. Kept in pixels it is mapped onto the screen on
// each of them instead (UpdateViewerCropOverlay), and what it cuts is exact
// whatever the panel happened to have decoded.
//
// WHERE THE PICTURE IS ON SCREEN comes from the same numbers the zoom uses -
// ViewerDisplayScale, the image's margin and the pan - rather than from the
// element's layout: the element is stretched Uniform and centres the bitmap
// inside itself, so its own bounds are not the picture's (see
// LogViewerPixelFidelity).
public partial class MainWindow
{
    private bool _viewerCropMode;

    // In the picture's pixels, whole ones, or null before one is drawn.
    private Rect? _viewerCropSelection;

    private enum CropDrag { None, Draw, Move, Resize }

    private CropDrag _viewerCropDrag;
    private CropPart _viewerCropResizePart;
    // The pixel that stays put while a rectangle is drawn or a corner pulled.
    private Point _viewerCropAnchor;
    // Where in the rectangle a move took hold of it.
    private Vector _viewerCropGrabOffset;

    // A middle press in crop mode, waiting to learn whether it is a pan or the
    // click that toggles full screen (see ViewerImageHost_MouseUp).
    private Point? _viewerCropMiddleOrigin;

    // ----- The bar -----------------------------------------------------------

    // A PICTURE'S BAR: a picture file whose own pixels or preview are on
    // screen, in the panel (not full screen), with nothing playing over it. A
    // film, a track's cover, a PDF, a text and a type icon all have bars of
    // their own or none.
    private bool ViewerPictureBarWanted
    {
        get
        {
            if (!_viewerOpen || _viewerFullscreen || ViewerMediaIsSelection ||
                _pendingViewerPath is not { } path || IsViewerPdf(path) ||
                ViewerImage.Visibility != Visibility.Visible || ViewerImage.Source is null ||
                _viewerPixelWidth <= 0)
            {
                return false;
            }

            string extension = Path.GetExtension(path);
            return ThumbnailExtensions.Contains(extension) || SlowThumbnailExtensions.Contains(extension);
        }
    }

    // Only where the panel holds the picture's REAL pixels. A PSD or a camera
    // raw is shown through its embedded preview or the shell, and an SVG
    // through the shell - cutting those would cut a stand-in, smaller than the
    // file and not the file.
    private bool CanCropViewerPicture
        => ViewerPictureBarWanted &&
           _viewerShowingDecodedImage &&
           ViewerImage.Source is BitmapSource &&
           _pendingViewerPath is { } path &&
           !SlowThumbnailExtensions.Contains(Path.GetExtension(path)) &&
           !string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase);

    // Asked from UpdateViewerZoomBar, which already runs at every moment the
    // thing on screen changes - a picture arriving, an icon instead, a film
    // starting, full screen crossed, a PDF or a text taking the panel.
    private void UpdateViewerPictureBar()
    {
        bool wanted = ViewerPictureBarWanted;
        ViewerPictureBar.Visibility = wanted ? Visibility.Visible : Visibility.Collapsed;

        bool canCrop = CanCropViewerPicture;
        ViewerCropToggle.IsEnabled = canCrop;
        ViewerCropToggle.ToolTip = canCrop ? Strings.ViewerCrop : Strings.ViewerCropUnavailable;
        if (!canCrop)
        {
            ExitViewerCropMode();
        }

        if (!wanted)
        {
            ApplyViewerPictureBarInset();
        }
    }

    // The picture starts under the bar rather than behind it: the host is
    // pushed down by the bar's height, and the fit is measured against the
    // host, so the picture refits into what is left. That is the row of
    // height the bar costs - in a sidebar mostly letterbox.
    private void ViewerPictureBar_SizeChanged(object sender, SizeChangedEventArgs e)
        => ApplyViewerPictureBarInset();

    private void ApplyViewerPictureBarInset()
    {
        var inset = ViewerPictureBar.Visibility == Visibility.Visible
            ? new Thickness(0, ViewerPictureBar.ActualHeight, 0, 0)
            : default;
        if (ViewerImageHost.Margin != inset)
        {
            ViewerImageHost.Margin = inset;
        }
    }

    // ----- Crop mode ---------------------------------------------------------

    private void ViewerCropToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_viewerCropMode)
        {
            ExitViewerCropMode();
        }
        else
        {
            EnterViewerCropMode();
        }
    }

    private void EnterViewerCropMode()
    {
        if (!CanCropViewerPicture)
        {
            ViewerCropToggle.IsChecked = false;
            return;
        }

        // The hand arriving, as on the picture itself.
        StopSlideshowOnUserAction("crop");
        _viewerCropMode = true;
        _viewerCropSelection = null;
        _viewerCropDrag = CropDrag.None;
        ViewerCropToggle.IsChecked = true;
        ViewerCropOverlay.Visibility = Visibility.Visible;
        ViewerImageHost.Cursor = Cursors.Cross;
        UpdateViewerCropOverlay();
    }

    // Every way out: the button, Esc, the file changing, the bar going away.
    // Safe to call when crop mode is already off.
    private void ExitViewerCropMode()
    {
        ViewerCropToggle.IsChecked = false;
        if (!_viewerCropMode)
        {
            return;
        }

        _viewerCropMode = false;
        _viewerCropSelection = null;
        // A middle-button pan still under way ends here too: its release would
        // find no press to answer and leave the pan following a bare mouse.
        if (_viewerCropMiddleOrigin is not null)
        {
            _viewerCropMiddleOrigin = null;
            _viewerPanning = false;
            ViewerImageHost.ReleaseMouseCapture();
        }
        if (_viewerCropDrag != CropDrag.None)
        {
            _viewerCropDrag = CropDrag.None;
            ViewerImageHost.ReleaseMouseCapture();
        }
        ViewerCropOverlay.Show(null, null);
        ViewerCropOverlay.Visibility = Visibility.Collapsed;
        ViewerImageHost.Cursor = ViewerHostRestCursor;
    }

    // The host's cursor between drags: the cross in crop mode, else the pan
    // hand where there is anything to pan.
    private Cursor? ViewerHostRestCursor =>
        _viewerCropMode ? Cursors.Cross : ViewerCanPan ? Cursors.SizeAll : null;

    // ----- Pixels and the screen ---------------------------------------------

    // The picture's drawn rectangle in the host's coordinates.
    private Rect ViewerPictureRectInHost()
    {
        double display = ViewerDisplayScale;
        var margin = ViewerImage.Margin;
        double centreX = margin.Left + (ViewerImageHost.ActualWidth - margin.Left - margin.Right) / 2 + ViewerZoomPan.X;
        double centreY = margin.Top + (ViewerImageHost.ActualHeight - margin.Top - margin.Bottom) / 2 + ViewerZoomPan.Y;
        double width = _viewerPixelWidth * display;
        double height = _viewerPixelHeight * display;
        return new Rect(centreX - width / 2, centreY - height / 2, width, height);
    }

    private Point HostToPicturePixel(Point host)
    {
        var picture = ViewerPictureRectInHost();
        double display = ViewerDisplayScale;
        return display > 0
            ? new Point((host.X - picture.X) / display, (host.Y - picture.Y) / display)
            : default;
    }

    // Inside the picture and on a whole pixel: the rectangle never reaches
    // past the picture's edge and never cuts a pixel in two.
    private Point ClampToPicture(Point pixel)
        => new(Math.Round(Math.Clamp(pixel.X, 0, _viewerPixelWidth)),
               Math.Round(Math.Clamp(pixel.Y, 0, _viewerPixelHeight)));

    // Redrawn on every zoom, pan frame, resize and drag move - see
    // ViewerCropOverlay for what a frame costs.
    private void UpdateViewerCropOverlay()
    {
        if (!_viewerCropMode)
        {
            return;
        }

        if (_viewerCropSelection is not { } selection)
        {
            ViewerCropOverlay.Show(null, null);
            return;
        }

        var picture = ViewerPictureRectInHost();
        double display = ViewerDisplayScale;
        var onScreen = new Rect(
            picture.X + selection.X * display,
            picture.Y + selection.Y * display,
            selection.Width * display,
            selection.Height * display);
        ViewerCropOverlay.Show(onScreen, $"{selection.Width:0} × {selection.Height:0}");
    }

    // ----- The drag ----------------------------------------------------------

    // A press while crop mode is on draws, moves or resizes - never pans or
    // flicks, and a double click does not zoom: the next press is simply the
    // next rectangle. Returns whether it took the press.
    private bool HandleViewerCropPress(MouseButtonEventArgs e)
    {
        if (!_viewerCropMode)
        {
            return false;
        }

        var host = e.GetPosition(ViewerImageHost);
        var pixel = HostToPicturePixel(host);
        var part = ViewerCropOverlay.HitPart(e.GetPosition(ViewerCropOverlay));

        if (_viewerCropSelection is { } selection && part is not CropPart.None and not CropPart.Inside)
        {
            _viewerCropDrag = CropDrag.Resize;
            _viewerCropResizePart = part;
            // The corner or edge opposite the one taken hold of.
            _viewerCropAnchor = new Point(
                part is CropPart.Left or CropPart.TopLeft or CropPart.BottomLeft ? selection.Right : selection.Left,
                part is CropPart.Top or CropPart.TopLeft or CropPart.TopRight ? selection.Bottom : selection.Top);
        }
        else if (_viewerCropSelection is { } held && part == CropPart.Inside)
        {
            _viewerCropDrag = CropDrag.Move;
            _viewerCropGrabOffset = pixel - held.TopLeft;
        }
        else
        {
            _viewerCropDrag = CropDrag.Draw;
            _viewerCropAnchor = SnapCropPoint(ClampToPicture(pixel));
            _viewerCropSelection = null;
            UpdateViewerCropOverlay();
        }

        ViewerImageHost.CaptureMouse();
        e.Handled = true;
        return true;
    }

    // Returns whether a crop drag took the move. A hover in crop mode only sets
    // the cursor and lets the move go on (a middle-button pan needs it).
    private bool HandleViewerCropMove(MouseEventArgs e)
    {
        if (!_viewerCropMode)
        {
            return false;
        }

        if (_viewerCropDrag == CropDrag.None)
        {
            if (!_viewerPanning)
            {
                ViewerImageHost.Cursor = CropCursorFor(ViewerCropOverlay.HitPart(e.GetPosition(ViewerCropOverlay)));
            }
            return false;
        }

        var pixel = HostToPicturePixel(e.GetPosition(ViewerImageHost));
        bool square = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (_viewerCropDrag)
        {
            case CropDrag.Draw:
                // Nothing until the hand has actually moved a pixel: a click is
                // how a rectangle is cleared, not how a 0 × 0 one is made.
                var corner = ClampToPicture(pixel);
                if (_viewerCropSelection is null && corner == _viewerCropAnchor)
                {
                    break;
                }
                _viewerCropSelection = SpanCrop(_viewerCropAnchor, corner, square);
                break;

            case CropDrag.Resize when _viewerCropSelection is { } current:
                _viewerCropSelection = ResizeCrop(current, ClampToPicture(pixel), square);
                break;

            case CropDrag.Move when _viewerCropSelection is { } moving:
                // Clamped again after the grid: a snap can round past the
                // picture's far edge by up to half a grid step.
                double maxX = _viewerPixelWidth - moving.Width;
                double maxY = _viewerPixelHeight - moving.Height;
                var topLeft = SnapCropPoint(new Point(
                    Math.Round(Math.Clamp(pixel.X - _viewerCropGrabOffset.X, 0, maxX)),
                    Math.Round(Math.Clamp(pixel.Y - _viewerCropGrabOffset.Y, 0, maxY))));
                _viewerCropSelection = new Rect(
                    new Point(Math.Min(topLeft.X, maxX), Math.Min(topLeft.Y, maxY)), moving.Size);
                break;
        }

        UpdateViewerCropOverlay();
        return true;
    }

    private bool HandleViewerCropRelease()
    {
        if (_viewerCropDrag == CropDrag.None)
        {
            return false;
        }

        EndViewerCropDrag();
        return true;
    }

    // Also the capture-lost path: the same reasoning as the pan's safety
    // device - a drag left set would follow a bare mouse move.
    private void EndViewerCropDrag()
    {
        if (_viewerCropDrag == CropDrag.None)
        {
            return;
        }

        _viewerCropDrag = CropDrag.None;
        if (ViewerImageHost.IsMouseCaptured)
        {
            ViewerImageHost.ReleaseMouseCapture();
        }

        if (_viewerCropSelection is { Width: < 1 } or { Height: < 1 })
        {
            _viewerCropSelection = null;
        }
        UpdateViewerCropOverlay();
    }

    // From the anchor to the corner the hand is at. SHIFT makes it a square,
    // as it does in every drawing tool, cut down to what fits on that side of
    // the anchor; CTRL puts the corner on the grid (the anchor was put there
    // at the press), so both sides come out whole grid steps.
    private Rect SpanCrop(Point anchor, Point corner, bool square)
    {
        corner = SnapCropPoint(corner);
        if (square)
        {
            double dx = corner.X - anchor.X;
            double dy = corner.Y - anchor.Y;
            double signX = dx < 0 ? -1 : 1;
            double signY = dy < 0 ? -1 : 1;
            double room = Math.Min(
                signX > 0 ? _viewerPixelWidth - anchor.X : anchor.X,
                signY > 0 ? _viewerPixelHeight - anchor.Y : anchor.Y);
            double side = Math.Min(Math.Max(Math.Abs(dx), Math.Abs(dy)), room);
            if (CropSnapHeld)
            {
                side = Math.Floor(side / CropGrid) * CropGrid;
            }
            corner = new Point(anchor.X + signX * side, anchor.Y + signY * side);
        }

        return new Rect(anchor, corner);
    }

    // A corner moves both of its sides, an edge only its own; the opposite
    // ones stay where the press found them. Pulled past its opposite, an edge
    // simply becomes the other side - the rectangle turns inside out the way
    // it does in every editor rather than stopping dead.
    private Rect ResizeCrop(Rect current, Point pixel, bool square)
    {
        switch (_viewerCropResizePart)
        {
            case CropPart.TopLeft or CropPart.TopRight or CropPart.BottomLeft or CropPart.BottomRight:
                return SpanCrop(_viewerCropAnchor, pixel, square);

            case CropPart.Left or CropPart.Right:
                double x = SnapCropPoint(pixel).X;
                return new Rect(new Point(_viewerCropAnchor.X, current.Top), new Point(x, current.Bottom));

            case CropPart.Top or CropPart.Bottom:
                double y = SnapCropPoint(pixel).Y;
                return new Rect(new Point(current.Left, _viewerCropAnchor.Y), new Point(current.Right, y));
        }

        return current;
    }

    // 16, not the 4 it was built with: in use a 4-pixel step was too fine to
    // feel like a grid at all (2026-11-03).
    private const double CropGrid = 16;

    private static bool CropSnapHeld => Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

    // In the picture's pixels, so the grid is the result's and not the
    // screen's: at a fit of 34% one step is a pixel or two on screen, and the
    // size readout is where it shows. The picture's own far edge is always
    // reachable, grid or not - a crop to the edge of a 1366-wide screenshot
    // should not stop at 1360.
    private Point SnapCropPoint(Point pixel)
    {
        if (!CropSnapHeld)
        {
            return pixel;
        }

        double x = pixel.X >= _viewerPixelWidth ? _viewerPixelWidth : Math.Round(pixel.X / CropGrid) * CropGrid;
        double y = pixel.Y >= _viewerPixelHeight ? _viewerPixelHeight : Math.Round(pixel.Y / CropGrid) * CropGrid;
        return new Point(Math.Min(x, _viewerPixelWidth), Math.Min(y, _viewerPixelHeight));
    }

    private static Cursor CropCursorFor(CropPart part) => part switch
    {
        CropPart.Inside => Cursors.SizeAll,
        CropPart.Left or CropPart.Right => Cursors.SizeWE,
        CropPart.Top or CropPart.Bottom => Cursors.SizeNS,
        CropPart.TopLeft or CropPart.BottomRight => Cursors.SizeNWSE,
        CropPart.TopRight or CropPart.BottomLeft => Cursors.SizeNESW,
        _ => Cursors.Cross,
    };

    // ----- The middle button in crop mode -------------------------------------
    //
    // A left drag draws in crop mode, so the pan a zoomed picture needs moves
    // to the middle button - which outside crop mode toggles full screen on the
    // PRESS. Here it waits for the release: moved, it was a pan; not moved, it
    // was that click, and full screen comes on as before (and ends crop mode,
    // which is the panel's alone for now).
    private bool HandleViewerCropMiddlePress(MouseButtonEventArgs e)
    {
        if (!_viewerCropMode || e.ChangedButton != MouseButton.Middle)
        {
            return false;
        }

        var point = e.GetPosition(ViewerImageHost);
        _viewerCropMiddleOrigin = point;
        if (ViewerCanPan)
        {
            _viewerPanning = true;
            _viewerPanOrigin = point;
            _viewerPanOriginX = ViewerZoomPan.X;
            _viewerPanOriginY = ViewerZoomPan.Y;
            ViewerImageHost.Cursor = Cursors.ScrollAll;
        }
        ViewerImageHost.CaptureMouse();
        e.Handled = true;
        return true;
    }

    private void ViewerImageHost_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _viewerCropMiddleOrigin is not { } origin)
        {
            return;
        }

        _viewerCropMiddleOrigin = null;
        var release = e.GetPosition(ViewerImageHost);
        bool moved = Math.Abs(release.X - origin.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                     Math.Abs(release.Y - origin.Y) >= SystemParameters.MinimumVerticalDragDistance;
        _viewerPanning = false;
        if (ViewerImageHost.IsMouseCaptured)
        {
            ViewerImageHost.ReleaseMouseCapture();
        }
        ViewerImageHost.Cursor = ViewerHostRestCursor;
        e.Handled = true;

        if (!moved)
        {
            SetViewerFullscreen(!_viewerFullscreen);
        }
    }

    // ----- Keys --------------------------------------------------------------

    // Esc ends crop mode; Ctrl+C copies the rectangle's pixels while there is
    // one, and otherwise goes on to copy the FILE as it always has. Asked
    // ahead of every other window key, since both keys mean something else
    // further down.
    private bool HandleViewerCropKey(KeyEventArgs e)
    {
        if (!_viewerCropMode || KeyboardInTextField)
        {
            return false;
        }

        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            ExitViewerCropMode();
            return true;
        }

        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && _viewerCropSelection is not null)
        {
            CopyViewerCropToClipboard();
            return true;
        }

        return false;
    }

    // ----- Copy --------------------------------------------------------------

    // FROM THE ORIGINAL'S PIXELS. The bitmap on screen is used only when it IS
    // the original - a full-resolution decode, or the GIF's own canvas, which
    // is the frame on screen. Otherwise the file is decoded whole on a worker,
    // oriented as the panel shows it, cut, and let go: at 50 megapixels that
    // decode is 200MB outside the GC heap, so nothing holds on to it.
    private async void CopyViewerCropToClipboard()
    {
        if (_viewerCropSelection is not { } selection || _pendingViewerPath is not { } path)
        {
            return;
        }

        var area = new Int32Rect((int)selection.X, (int)selection.Y, (int)selection.Width, (int)selection.Height);
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        BitmapSource? cut;
        if (ViewerImage.Source is BitmapSource shown &&
            shown.PixelWidth == _viewerPixelWidth && shown.PixelHeight == _viewerPixelHeight)
        {
            // The GIF's canvas is live and unfrozen - a copy of this frame is
            // what gets cut, and the animation carries on.
            cut = CutViewerBitmap(shown.IsFrozen ? shown : shown.Clone(), area);
        }
        else
        {
            cut = await Task.Run(() => DecodeViewerOriginal(path) is { } original
                ? CutViewerBitmap(original, area)
                : null);
        }

        if (cut is not null)
        {
            await PutImageOnClipboard(cut);
        }
    }

    // The same decode LoadViewerImage makes, at full size: one open, the EXIF
    // turn applied, frozen so it can cross threads. Null where the panel's own
    // decode would have failed too.
    private static BitmapSource? DecodeViewerOriginal(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream,
                BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            int orientation = ReadExifOrientation(frame);

            stream.Position = 0;
            var decoded = new BitmapImage();
            decoded.BeginInit();
            decoded.StreamSource = stream;
            decoded.CacheOption = BitmapCacheOption.OnLoad;
            decoded.EndInit();

            var oriented = ApplyExifOrientation(decoded, orientation);
            oriented.Freeze();
            return oriented;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or FileFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // A plain 32-bit copy of the area, so the clipboard and any encoder after
    // it read finished pixels instead of re-running the orientation chain.
    private static BitmapSource? CutViewerBitmap(BitmapSource source, Int32Rect area)
    {
        int width = Math.Min(area.Width, source.PixelWidth - area.X);
        int height = Math.Min(area.Height, source.PixelHeight - area.Y);
        if (area.X < 0 || area.Y < 0 || width <= 0 || height <= 0)
        {
            return null;
        }

        var cropped = new CroppedBitmap(source, new Int32Rect(area.X, area.Y, width, height));
        var copy = new WriteableBitmap(new FormatConvertedBitmap(cropped, PixelFormats.Bgra32, null, 0));
        copy.Freeze();
        return copy;
    }

    // TWO FORMATS: the ordinary bitmap every program reads, and PNG beside it
    // for the ones that look for it - a browser or an office app - so a
    // picture with transparency does not arrive with black where it was clear.
    //
    // A few tries, a moment apart, then silence. Another program holding the
    // clipboard open makes it throw (see FileOperationService's note on
    // CLIPBRD_E_CANT_OPEN); the file-list copies give up at once, but a cut
    // the hand has just drawn is worth a second attempt.
    private async Task PutImageOnClipboard(BitmapSource image)
    {
        var data = new DataObject();
        data.SetImage(image);
        var png = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(png);
        data.SetData("PNG", png, false);

        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                // What the clipboard held was a cut list, if anything, and it
                // no longer is - the rows' cut marks would say otherwise.
                ClearCutMarks("crop copied");
                return;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                await Task.Delay(60);
            }
        }
    }
}
