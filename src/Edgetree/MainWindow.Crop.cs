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
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Size = System.Windows.Size;

namespace SidebarExplorer.App;

// ===== Cropping the panel's picture (2026-11-03, the author's design) =====
//
// The bar over a picture, crop mode, a rectangle drawn and adjusted over the
// picture, and Ctrl+C copying what it covers (the first stage); then the
// buttons that stand by a finished rectangle, the temporary crop, and
// the two saves (the second). The whole design, with what was settled and
// why, is in the project notes on the crop round.
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

    // THE TEMPORARY CROP: the cut, shown in the panel in place of the picture
    // and not yet written anywhere. Full resolution - it is what a save
    // writes and what a second crop cuts from - and the panel treats it as the
    // picture on screen in every other way (fit, zoom, navigator, caption).
    // Null while the picture on screen is the file's.
    private BitmapSource? _viewerCropApplied;

    // What the panel showed before the first temporary crop, so 자르기 취소
    // puts it back without reading the file again (a NAS costs seconds). An
    // animated GIF is the exception: its frames were let go when the crop
    // replaced it, and it is read again.
    private sealed record ViewerCropOriginal(
        ImageSource Source, int PixelWidth, int PixelHeight, int DecodedWidth, string Caption, bool WasAnimated);

    private ViewerCropOriginal? _viewerCropOriginal;

    private bool _viewerCropSaving;

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

        // 저장 and 자르기 취소 answer the temporary crop; a rectangle alone is
        // saved from the buttons beside it.
        bool applied = _viewerCropApplied is not null;
        ViewerCropSaveButton.IsEnabled = applied && !_viewerCropSaving;
        ViewerCropRevertButton.IsEnabled = applied;

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
        ViewerCropActions.Visibility = Visibility.Collapsed;
        ViewerImageHost.Cursor = ViewerHostRestCursor;
    }

    // Crop mode AND the temporary crop, for the moments the picture itself
    // goes: another file, the panel closing. Nothing is put back on screen -
    // what comes next replaces it - and nothing is asked: the cut is a few
    // seconds' work, and the picture it came from is untouched on disk.
    private void DiscardViewerCrop()
    {
        ExitViewerCropMode();
        _viewerCropApplied = null;
        _viewerCropOriginal = null;
        ViewerCropSaveButton.IsEnabled = false;
        ViewerCropRevertButton.IsEnabled = false;
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

    // THE PART OF THE PICTURE ON SCREEN, in its pixels - what a drawn or
    // pulled edge can reach (2026-11-03, on request). Zoomed in, a drag used
    // to run on past the panel's edge, and an edge out there can neither be
    // seen nor taken hold of: the cut would be decided where nobody can look.
    // So the hand's point stops at the panel's edge; more than the panel
    // shows is a wheel turn out, and a handle at each zoom is how one corner
    // is set close up and the other from further away. The panel is NOT
    // scrolled for the hand - motion the app drives itself is what this app
    // does not do. A MOVE keeps the whole picture as its room: the rectangle
    // is carried, not drawn.
    private Rect ViewerCropReach
    {
        get
        {
            var topLeft = HostToPicturePixel(new Point(0, 0));
            var bottomRight = HostToPicturePixel(new Point(ViewerImageHost.ActualWidth, ViewerImageHost.ActualHeight));
            double left = Math.Ceiling(Math.Clamp(topLeft.X, 0, _viewerPixelWidth));
            double top = Math.Ceiling(Math.Clamp(topLeft.Y, 0, _viewerPixelHeight));
            double right = Math.Floor(Math.Clamp(bottomRight.X, 0, _viewerPixelWidth));
            double bottom = Math.Floor(Math.Clamp(bottomRight.Y, 0, _viewerPixelHeight));
            return new Rect(new Point(left, top), new Point(Math.Max(left, right), Math.Max(top, bottom)));
        }
    }

    // Inside that reach and on a whole pixel: the rectangle never passes what
    // is on screen or the picture's edge, and never cuts a pixel in two.
    private Point ClampToReach(Point pixel)
    {
        var reach = ViewerCropReach;
        return new(Math.Round(Math.Clamp(pixel.X, reach.Left, reach.Right)),
                   Math.Round(Math.Clamp(pixel.Y, reach.Top, reach.Bottom)));
    }

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
            ViewerCropActions.Visibility = Visibility.Collapsed;
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
        PlaceViewerCropActions(onScreen);
    }

    // THE BUTTONS BY A FINISHED RECTANGLE: under its bottom-right
    // corner, right-aligned to it, or just inside that corner when there is no
    // room below - always within the panel. Not while the rectangle is being
    // drawn or pulled: they would jump about under the hand.
    private void PlaceViewerCropActions(Rect onScreen)
    {
        if (_viewerCropDrag != CropDrag.None)
        {
            ViewerCropActions.Visibility = Visibility.Collapsed;
            return;
        }

        ViewerCropActions.Visibility = Visibility.Visible;
        // MEASURED WITH NO MARGIN. A DesiredSize includes the element's own
        // Margin, and the Margin is where this method put the plate last time
        // - so from the second rectangle on, the plate measured as large as
        // its previous position, the clamp below took it for something that
        // could not fit, and it landed in the panel's top-left corner
        // (2026-11-03, the first test).
        ViewerCropActions.Margin = default;
        ViewerCropActions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = ViewerCropActions.DesiredSize;
        const double gap = 6;
        double hostWidth = ViewerImageHost.ActualWidth;
        double hostHeight = ViewerImageHost.ActualHeight;

        double x = onScreen.Right - size.Width;
        double y = onScreen.Bottom + gap;
        if (y + size.Height > hostHeight)
        {
            y = onScreen.Bottom - size.Height - gap;
        }

        x = Math.Clamp(x, 0, Math.Max(0, hostWidth - size.Width));
        y = Math.Clamp(y, 0, Math.Max(0, hostHeight - size.Height));
        ViewerCropActions.Margin = new Thickness(Math.Round(x), Math.Round(y), 0, 0);
    }

    // A press on the plate between its buttons is the plate's: without this
    // it would reach the picture and start a new rectangle under them.
    private void ViewerCropActions_MouseDown(object sender, MouseButtonEventArgs e)
        => e.Handled = true;

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
            _viewerCropAnchor = SnapCropPointInReach(ClampToReach(pixel));
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
                var corner = ClampToReach(pixel);
                if (_viewerCropSelection is null && corner == _viewerCropAnchor)
                {
                    break;
                }
                _viewerCropSelection = SpanCrop(_viewerCropAnchor, corner, square);
                break;

            case CropDrag.Resize when _viewerCropSelection is { } current:
                _viewerCropSelection = ResizeCrop(current, ClampToReach(pixel), square);
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
        corner = SnapCropPointInReach(corner);
        if (square)
        {
            double dx = corner.X - anchor.X;
            double dy = corner.Y - anchor.Y;
            double signX = dx < 0 ? -1 : 1;
            double signY = dy < 0 ? -1 : 1;
            // What is on screen on that side of the anchor (ViewerCropReach) -
            // nothing, rather than a negative, for an anchor a pulled corner
            // left off screen.
            var reach = ViewerCropReach;
            double room = Math.Max(0, Math.Min(
                signX > 0 ? reach.Right - anchor.X : anchor.X - reach.Left,
                signY > 0 ? reach.Bottom - anchor.Y : anchor.Y - reach.Top));
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
                double x = SnapCropPointInReach(pixel).X;
                return new Rect(new Point(_viewerCropAnchor.X, current.Top), new Point(x, current.Bottom));

            case CropPart.Top or CropPart.Bottom:
                double y = SnapCropPointInReach(pixel).Y;
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

    // The same grid for a point the hand draws or pulls, held inside what is
    // on screen (ViewerCropReach): the reach's own edges stay reachable the
    // way the picture's do, and a step that would round past one stops on it.
    private Point SnapCropPointInReach(Point pixel)
    {
        if (!CropSnapHeld)
        {
            return pixel;
        }

        var reach = ViewerCropReach;
        double Snap(double value, double low, double high) =>
            value >= high ? high
            : value <= low ? low
            : Math.Clamp(Math.Round(value / CropGrid) * CropGrid, low, high);

        return new Point(Snap(pixel.X, reach.Left, reach.Right), Snap(pixel.Y, reach.Top, reach.Bottom));
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

    // Esc ends crop mode. Ctrl+C copies, and Ctrl+Shift+S saves under a name
    // asked for - the text view's key for it - whatever there is to cut: the
    // rectangle while there is one, else the temporary crop. With neither,
    // both keys go on to mean what they always have (Ctrl+C copies the FILE).
    // Asked ahead of every other window key, since all three mean something
    // else further down.
    private bool HandleViewerCropKey(KeyEventArgs e)
    {
        if (KeyboardInTextField)
        {
            return false;
        }

        if (_viewerCropMode && e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            ExitViewerCropMode();
            return true;
        }

        // 자르기 취소 (2026-11-03, on request). The whole way back to the
        // picture as the file has it, as the button does - not one cut at a
        // time. Nothing else in the window answers Ctrl+Z (the text view's is
        // its own, inside the editor), so with no temporary crop it is left
        // alone.
        if (_viewerCropApplied is not null && e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            RevertViewerCrop();
            return true;
        }

        if (!HasViewerCropToTake)
        {
            return false;
        }

        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopyViewerCropToClipboard();
            return true;
        }

        if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            SaveViewerCrop(askName: true);
            return true;
        }

        return false;
    }

    private bool HasViewerCropToTake
        => (_viewerCropMode && _viewerCropSelection is not null) || _viewerCropApplied is not null;

    // ----- The buttons -------------------------------------------------------

    // By the rectangle: 잘라서 보기 and 저장 (a click outside it clears it). In
    // the bar: 저장 and 자르기 취소, for the temporary crop.
    private void ViewerCropApply_Click(object sender, RoutedEventArgs e) => ApplyViewerCrop();

    private void ViewerCropSaveSelection_Click(object sender, RoutedEventArgs e) => SaveViewerCrop(askName: false);

    private void ViewerCropSaveApplied_Click(object sender, RoutedEventArgs e)
    {
        // What is on screen, not a rectangle that may have been drawn over it
        // since: the bar's 저장 belongs to the temporary crop.
        _viewerCropSelection = null;
        SaveViewerCrop(askName: false);
    }

    private void ViewerCropRevert_Click(object sender, RoutedEventArgs e) => RevertViewerCrop();

    // ----- The temporary crop ------------------------------------------------

    // THE CUT TAKES THE PICTURE'S PLACE: the same Image, sized as a picture of
    // its own (its pixels, at the rest state the panel keeps), so the wheel,
    // the navigator, the zoom strip and a second crop all work on it as they
    // do on a file. Crop mode stays on with no rectangle, for that second one.
    private async void ApplyViewerCrop()
    {
        if (_viewerCropSelection is null || _pendingViewerPath is not { } path)
        {
            return;
        }

        var cut = await TakeViewerCropAsync();
        if (cut is null || !string.Equals(_pendingViewerPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _viewerCropOriginal ??= new ViewerCropOriginal(
            ViewerImage.Source, _viewerPixelWidth, _viewerPixelHeight, _viewerDecodedWidth,
            _viewerCaptionFacts, _viewerGifDecoder is not null);
        // An animation would go on drawing its next frame into the Image.
        StopViewerGif();

        _viewerCropApplied = cut;
        ViewerImage.Source = cut;
        _viewerPixelWidth = cut.PixelWidth;
        _viewerPixelHeight = cut.PixelHeight;
        // Whole already, so the full-resolution pass has nothing to fetch -
        // and must not, since what it fetches is the FILE.
        _viewerDecodedWidth = cut.PixelWidth;
        SetViewerCaption(string.Format(Strings.ViewerCropCaptionFormat, cut.PixelWidth, cut.PixelHeight));

        _viewerCropSelection = null;
        ShowViewerAtRest();
        UpdateViewerPictureBar();
    }

    // 자르기 취소: the picture as it was before the first temporary crop, at
    // rest, crop mode off.
    private void RevertViewerCrop()
    {
        if (_viewerCropOriginal is not { } original)
        {
            return;
        }

        ExitViewerCropMode();
        _viewerCropApplied = null;
        _viewerCropOriginal = null;

        if (original.WasAnimated)
        {
            // Read again - the frames were let go (see ViewerCropOriginal).
            _pendingViewerPath = null;
            UpdateViewerPreview();
            return;
        }

        ViewerImage.Source = original.Source;
        _viewerPixelWidth = original.PixelWidth;
        _viewerPixelHeight = original.PixelHeight;
        _viewerDecodedWidth = original.DecodedWidth;
        SetViewerCaption(original.Caption);
        ShowViewerAtRest();
        UpdateViewerPictureBar();
    }

    // Where a picture arriving lands: the remembered rest state, centred.
    private void ShowViewerAtRest()
    {
        _viewerZoom = ViewerRestZoom;
        _viewerFill = ViewerRestIsFill;
        ViewerZoomPan.X = 0;
        ViewerZoomPan.Y = 0;
        ApplyViewerZoom();
    }

    // What a copy or a save takes: the rectangle, cut from the temporary crop
    // if there is one and from the file's own pixels if not; or, with no
    // rectangle, the temporary crop whole. Null when there is nothing.
    private async Task<BitmapSource?> TakeViewerCropAsync()
    {
        if (!(_viewerCropMode && _viewerCropSelection is { } selection))
        {
            return _viewerCropApplied;
        }

        var area = new Int32Rect((int)selection.X, (int)selection.Y, (int)selection.Width, (int)selection.Height);
        if (area.Width <= 0 || area.Height <= 0)
        {
            return null;
        }

        if (_viewerCropApplied is { } applied)
        {
            return CutViewerBitmap(applied, area);
        }

        if (ViewerImage.Source is BitmapSource shown &&
            shown.PixelWidth == _viewerPixelWidth && shown.PixelHeight == _viewerPixelHeight)
        {
            // The GIF's canvas is live and unfrozen - a copy of this frame is
            // what gets cut, and the animation carries on.
            return CutViewerBitmap(shown.IsFrozen ? shown : shown.Clone(), area);
        }

        if (_pendingViewerPath is not { } path)
        {
            return null;
        }

        return await Task.Run(() => DecodeViewerOriginal(path) is { } original
            ? CutViewerBitmap(original, area)
            : null);
    }

    // ----- Copy --------------------------------------------------------------

    // FROM THE ORIGINAL'S PIXELS. The bitmap on screen is used only when it IS
    // the original - a full-resolution decode, or the GIF's own canvas, which
    // is the frame on screen. Otherwise the file is decoded whole on a worker,
    // oriented as the panel shows it, cut, and let go: at 50 megapixels that
    // decode is 200MB outside the GC heap, so nothing holds on to it.
    private async void CopyViewerCropToClipboard()
    {
        if (await TakeViewerCropAsync() is { } cut)
        {
            await PutImageOnClipboard(cut);
        }
    }

    // ----- Save --------------------------------------------------------------

    // BESIDE THE ORIGINAL, under its name with -crop, numbered " (2)" on as
    // paste numbers a copy - never over anything. Or, with askName, wherever
    // the save dialog says, which may write over a file it asked about.
    //
    // In the original's format where WPF can write it (JPEG at quality 95,
    // PNG, BMP, TIFF, GIF), PNG otherwise - WebP, HEIC, AVIF and an icon have
    // no encoder here. No metadata goes with it: the cut is a new picture,
    // the camera's record of the old one is not its own, and the EXIF turn is
    // already in its pixels (a separate EXIF view and editor is planned).
    //
    // After it: back to the picture as the file has it, crop mode off - the
    // job is done - and the panel stays on the original. The new file is put
    // in the tree and the strip at once rather than when the watcher gets to
    // it, so the save reads as having happened.
    private async void SaveViewerCrop(bool askName)
    {
        if (_viewerCropSaving || _pendingViewerPath is not { } path ||
            Path.GetDirectoryName(path) is not { Length: > 0 } folder)
        {
            return;
        }

        var cut = await TakeViewerCropAsync();
        if (cut is null || !string.Equals(_pendingViewerPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string extension = CropSaveExtension(Path.GetExtension(path));
        string target = FileOperationService.GetUniqueDestination(
            Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + "-crop" + extension));

        if (askName)
        {
            var dialog = new SaveFileDialog
            {
                InitialDirectory = folder,
                FileName = Path.GetFileName(target),
                Filter = Strings.ViewerCropSaveFilter,
                FilterIndex = CropFilterIndex(extension),
                DefaultExt = extension,
                AddExtension = true,
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            target = Path.GetFullPath(dialog.FileName);
            // A name the encoders cannot write (a .webp typed in, no
            // extension at all) is written as PNG, and says so in its name.
            string chosen = Path.GetExtension(target).ToLowerInvariant();
            if (CropSaveExtension(chosen) != chosen)
            {
                target += ".png";
            }
        }

        _viewerCropSaving = true;
        UpdateViewerPictureBar();
        (bool saved, string? error) = await Task.Run(() => WriteViewerCrop(cut, target, replace: askName));
        _viewerCropSaving = false;

        if (!saved)
        {
            UpdateViewerPictureBar();
            MessageBox.Show(this, error ?? string.Empty, Strings.ViewerCropSaveFailedTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.Equals(_pendingViewerPath, path, StringComparison.OrdinalIgnoreCase))
        {
            if (_viewerCropApplied is not null)
            {
                RevertViewerCrop();
            }
            else
            {
                ExitViewerCropMode();
            }
        }

        string? savedFolder = Path.GetDirectoryName(target);
        if (ViewerItem?.Parent is { IsDirectory: true } parent &&
            string.Equals(parent.FullPath, savedFolder, StringComparison.OrdinalIgnoreCase))
        {
            RefreshFolderPreservingState(parent);
        }
        UpdateViewerPictureBar();
    }

    // The dialog's type row for an extension, in Strings.ViewerCropSaveFilter's
    // order (1-based, as the dialog counts).
    private static int CropFilterIndex(string extension) => extension switch
    {
        ".jpg" or ".jpeg" or ".jfif" => 2,
        ".bmp" => 3,
        ".tif" or ".tiff" => 4,
        ".gif" => 5,
        _ => 1,
    };

    // The extension a save keeps: the original's, lower-cased, where there is
    // an encoder for it; .png where there is not.
    private static string CropSaveExtension(string originalExtension) => originalExtension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".jfif" or ".png" or ".bmp" or ".tif" or ".tiff" or ".gif" => originalExtension.ToLowerInvariant(),
        _ => ".png",
    };

    private static BitmapEncoder CropEncoderFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".jfif" => new JpegBitmapEncoder { QualityLevel = 95 },
        ".bmp" => new BmpBitmapEncoder(),
        ".tif" or ".tiff" => new TiffBitmapEncoder(),
        ".gif" => new GifBitmapEncoder(),
        _ => new PngBitmapEncoder(),
    };

    // A worker. Written to a hidden temporary file beside the target and then
    // moved into place, as the zip is (ArchiveService.CreateZip): a save that
    // fails half way leaves no half-written picture under the real name.
    private static (bool Saved, string? Error) WriteViewerCrop(BitmapSource image, string target, bool replace)
    {
        string folder = Path.GetDirectoryName(target) ?? string.Empty;
        string temp = FileOperationService.GetUniqueDestination(
            Path.Combine(folder, "~" + Path.GetFileName(target) + ".tmp"));
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                File.SetAttributes(temp, FileAttributes.Hidden | FileAttributes.Temporary);
                var encoder = CropEncoderFor(Path.GetExtension(target));
                // JPEG has no alpha; given one, the encoder's own conversion
                // is not something to leave to chance.
                BitmapSource frame = encoder is JpegBitmapEncoder
                    ? new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0)
                    : image;
                encoder.Frames.Add(BitmapFrame.Create(frame));
                encoder.Save(stream);
            }

            File.SetAttributes(temp, FileAttributes.Normal);
            File.Move(temp, target, overwrite: replace);
            return (true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or InvalidOperationException or ArgumentException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
            return (false, ex.Message);
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
