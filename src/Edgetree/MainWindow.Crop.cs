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
// why, is in the project notes on the crop round. The bar's two turns
// (2026-11-05) live in MainWindow.Rotate.cs and share the temporary edit,
// its take-back and its saves with the crop.
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

    // Space held in crop mode: the next left press pans instead of drawing
    // (see the Space section below), and whether the pan it started is the
    // one under way, so letting go of Space can end it.
    private bool _viewerCropSpaceHeld;
    private bool _viewerCropSpacePanning;

    // THE TEMPORARY CROP: the cut, shown in the panel in place of the picture
    // and not yet written anywhere. Full resolution - it is what a save
    // writes and what a second crop cuts from - and the panel treats it as the
    // picture on screen in every other way (fit, zoom, navigator, caption).
    // Null while the picture on screen is the file's.
    private BitmapSource? _viewerCropApplied;

    // What the panel showed before the first temporary crop or turn, so the
    // undo puts it back without reading the file again (a NAS costs seconds). An
    // animated GIF is the exception: its frames were let go when the crop
    // replaced it, and it is read again.
    private sealed record ViewerCropOriginal(
        ImageSource Source, int PixelWidth, int PixelHeight, int DecodedWidth, string Caption, bool WasAnimated);

    private ViewerCropOriginal? _viewerCropOriginal;

    // Where the temporary crop on screen starts in the FILE's pixels - every
    // cut that made it, added up - so a rectangle drawn on it is remembered
    // as the part of the file it really covers (see RememberViewerCropArea).
    // The file as turned, if it is: a turn of the cut turns this with it.
    // Zero while the picture on screen is the file's.
    private Point _viewerCropAppliedOrigin;

    // A cut being taken for 잘라서 보기 or a save, from the moment it starts -
    // which can be a second of decoding the whole file on a worker before
    // anything shows (TakeViewerCropAsync). One at a time: until the 11-03
    // review the guard went up only once the cut was in hand, so a second
    // press inside that second ran beside the first - two saves taking the
    // same -cropped name, the second reporting a failure the first had not had,
    // and two cuts adding the rectangle's offset to the origin twice. The
    // buttons that start one are greyed meanwhile, which is also the only
    // sign the press was taken.
    private bool _viewerCropBusy;

    // When the rectangle on screen was last put there by the hand - drawn,
    // moved, sized, or brought back by 이전 영역 불러오기 - held against
    // _marksChangedAt to tell which of the two came last (HandleViewerCropKey).
    private long _viewerCropSelectionAt;

    private void SetViewerCropBusy(bool busy)
    {
        _viewerCropBusy = busy;
        UpdateViewerPictureBar();
    }

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

    // Only where the panel holds the picture's REAL pixels. A camera raw is
    // shown through its embedded preview or the shell, and an SVG through the
    // shell - cutting those would cut a stand-in, smaller than the file and
    // not the file.
    //
    // A PSD CAN BE CUT SINCE 2026-11-03 (on request: cut what the panel
    // shows), when its header gave its size (ViewerShowsSizedShellPicture).
    // What the panel shows is the shell's picture, and asked for whole it
    // comes at the file's own size where the installed handler will; the cut
    // is taken from that (TakeViewerCropAsync). Its layers are the handler's
    // business - this cuts the flattened picture it draws.
    private bool CanCropViewerPicture
        => ViewerPictureBarWanted &&
           ViewerImage.Source is BitmapSource &&
           _pendingViewerPath is { } path &&
           (ViewerShowsSizedShellPicture ||
            (_viewerShowingDecodedImage &&
             !SlowThumbnailExtensions.Contains(Path.GetExtension(path)) &&
             !string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase)));

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

        // The two turns (MainWindow.Rotate.cs), greyed while a cut is being
        // taken - a turn of a cut is taken on a worker as well.
        bool canTurn = CanTurnViewerPicture;
        ViewerRotateLeftButton.IsEnabled = canTurn && !_viewerCropBusy;
        ViewerRotateRightButton.IsEnabled = canTurn && !_viewerCropBusy;
        ViewerRotateLeftButton.ToolTip = canTurn ? Strings.ViewerRotateLeft : Strings.ViewerRotateUnavailable;
        ViewerRotateRightButton.ToolTip = canTurn ? Strings.ViewerRotateRight : Strings.ViewerRotateUnavailable;

        // 저장 and the undo answer the temporary edit - a crop, a turn, or
        // both; a rectangle alone is saved from the buttons beside it. Their
        // tooltips say which: 저장 names the file it will write, the undo what
        // it takes back.
        bool applied = _viewerCropApplied is not null;
        bool turned = _viewerTurn != 0;
        ViewerCropSaveButton.IsEnabled = (applied || turned) && !_viewerCropBusy;
        ViewerCropSaveButton.ToolTip = ViewerTurnedOnly ? Strings.ViewerRotateSave : Strings.ViewerCropSave;
        // Not while a cut is being taken: it would land after the take-back
        // and put back what the press had just removed (HandleViewerCropKey).
        ViewerCropRevertButton.IsEnabled = (applied || turned) && !_viewerCropBusy;
        ViewerCropRevertButton.ToolTip = applied && turned ? Strings.ViewerEditRevert
            : turned ? Strings.ViewerRotateRevert
            : Strings.ViewerCropRevert;
        // The buttons only, not the plate: a press on a greyed button goes to
        // its first enabled parent, and the plate's handler keeps it off the
        // picture, where it would start a new rectangle.
        ViewerCropActionButtons.IsEnabled = !_viewerCropBusy;

        // The two 이전 영역 불러오기 buttons; the image one names the place and
        // size it would put back - and, greyed because it would not fit, the
        // size of the picture it does not fit (see FitViewerCropArea).
        var lastArea = LastViewerCropArea;
        bool imageFits = canCrop && ViewerCropReuseTarget is not null;
        ViewerCropReuseButton.IsEnabled = imageFits;
        ViewerCropReuseButton.ToolTip = lastArea is not { } area
            ? Strings.ViewerCropReuse
            : imageFits || !canCrop
                ? string.Format(Strings.ViewerCropReuseFormat, area.X, area.Y, area.Width, area.Height)
                : string.Format(Strings.ViewerCropReuseOutsideFormat, area.X, area.Y, area.Width, area.Height,
                    _viewerPixelWidth, _viewerPixelHeight);
        UpdateViewerCropReusePanelButton();
        // The EXIF view's slot answers the same moments (MainWindow.Exif.cs).
        UpdateViewerExif();

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

        // A PSD's whole picture is asked for now, not at the cut: the area is
        // then drawn on the sharp one, and on a NAS the seconds the shell
        // takes are spent while the hand is still drawing.
        if (ViewerShowsSizedShellPicture && _viewerDecodedWidth < _viewerPixelWidth &&
            _pendingViewerPath is { } psd)
        {
            _ = FetchViewerShellWhole(psd);
        }

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
        // A Space pan still under way ends here too: its release would find
        // nothing to answer and leave the pan following a bare mouse.
        EndViewerCropSpacePan();
        _viewerCropSpaceHeld = false;
        if (_viewerCropDrag != CropDrag.None)
        {
            _viewerCropDrag = CropDrag.None;
            ViewerImageHost.ReleaseMouseCapture();
        }
        StopViewerCropEdgePan();
        ViewerCropOverlay.Show(null, null);
        ViewerCropOverlay.Visibility = Visibility.Collapsed;
        ViewerCropActions.Visibility = Visibility.Collapsed;
        ViewerImageHost.Cursor = ViewerHostRestCursor;
    }

    // Crop mode AND the temporary edit - a cut, a turn - for the moments the
    // picture itself goes: another file, the panel closing. Nothing is put
    // back on screen - what comes next replaces it - and nothing is asked: the
    // edit is a few seconds' work, and the picture it came from is untouched
    // on disk.
    private void DiscardViewerCrop()
    {
        ExitViewerCropMode();
        _viewerCropApplied = null;
        _viewerCropOriginal = null;
        _viewerCropAppliedOrigin = default;
        _viewerTurn = 0;
        _viewerTurnBase = null;
        ViewerCropSaveButton.IsEnabled = false;
        ViewerCropRevertButton.IsEnabled = false;
    }

    // The host's cursor between drags: the cross in crop mode - the pan hand
    // while Space is held there and there is anything to pan, the plain arrow
    // while it is held and there is not - else the pan hand where there is
    // anything to pan.
    private Cursor? ViewerHostRestCursor =>
        _viewerCropMode
            ? _viewerCropSpaceHeld ? (ViewerCanPan ? Cursors.SizeAll : null) : Cursors.Cross
            : ViewerCanPan ? Cursors.SizeAll : null;

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
    // shows is a wheel turn out, a handle at each zoom - or, since the same
    // day on request, the hand held at the edge, which pans the picture in
    // from that side (see the edge pan below). A MOVE keeps the whole picture
    // as its room: the rectangle is carried, not drawn.
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
        // Where it starts as well as how big it is (2026-11-03, on request) -
        // in the FILE's pixels, the numbers 이전 영역 불러오기 (이미지 기준)
        // will remember and show, so the two always agree.
        ViewerCropOverlay.Show(onScreen,
            $"x {selection.X + _viewerCropAppliedOrigin.X:0}, y {selection.Y + _viewerCropAppliedOrigin.Y:0}" +
            $"  ·  {selection.Width:0} × {selection.Height:0}");
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

    // A press while crop mode is on draws, moves or resizes - never flicks,
    // and a double click does not zoom: the next press is simply the next
    // rectangle. With Space held it pans instead, wherever it lands (see the
    // Space section). Returns whether it took the press.
    private bool HandleViewerCropPress(MouseButtonEventArgs e)
    {
        if (!_viewerCropMode)
        {
            return false;
        }

        if (_viewerCropSpaceHeld)
        {
            StartViewerCropSpacePan(e);
            e.Handled = true;
            return true;
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

        _viewerCropDragChanged = false;
        _viewerCropLastHost = host;
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
                ViewerImageHost.Cursor = _viewerCropSpaceHeld
                    ? ViewerHostRestCursor
                    : CropCursorFor(ViewerCropOverlay.HitPart(e.GetPosition(ViewerCropOverlay)));
            }
            return false;
        }

        _viewerCropLastHost = e.GetPosition(ViewerImageHost);
        UpdateViewerCropDrag(_viewerCropLastHost);
        UpdateViewerCropAutoPan();
        return true;
    }

    // Where the hand is on the host, for the edge pan's frames, which arrive
    // with no mouse move of their own.
    private Point _viewerCropLastHost;

    // The drag's one step, from a point on the host: a mouse move, or a frame
    // of the edge pan moving the picture under a hand that has not moved.
    private void UpdateViewerCropDrag(Point host)
    {
        var pixel = HostToPicturePixel(host);
        bool square = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var before = _viewerCropSelection;
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
                _viewerCropSelection = AtLeastOnePixel(SpanCrop(_viewerCropAnchor, corner, square), _viewerCropAnchor, corner);
                break;

            case CropDrag.Resize when _viewerCropSelection is { } current:
                var pulled = ClampToReach(pixel);
                _viewerCropSelection = AtLeastOnePixel(ResizeCrop(current, pulled, square), _viewerCropAnchor, pulled);
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

        if (_viewerCropSelection != before)
        {
            _viewerCropDragChanged = true;
        }
        UpdateViewerCropOverlay();
    }

    // ----- The edge pan ------------------------------------------------------
    //
    // A DRAWN OR PULLED POINT THAT REACHES THE PANEL'S EDGE MOVES THE PICTURE
    // (2026-11-03, on request). The point stops at what is on screen
    // (ViewerCropReach), and before this a rectangle bigger than the panel
    // could only be had by zooming out and pulling a handle; now holding the
    // hand at the edge brings more of the picture in from that side, and the
    // rectangle follows it out.
    //
    // This is the auto-scroll this app has otherwise turned down (2026-08-07,
    // the tree's 더 보기 drag): motion the app keeps going is the one kind that
    // shows every dropped frame. Asked for here, and cheaper than a list: a
    // frame is the picture's two pan values, the rectangle and the navigator -
    // no decode, no layout. Timed by CompositionTarget.Rendering, so it moves
    // once per frame the screen draws and by the time since the last one, and
    // it is subscribed only while the hand is at an edge with picture left
    // beyond it.
    //
    // The edge is a band just inside the panel as well as anything past it: a
    // panel against the screen's own edge leaves the pointer nowhere past it.
    // Faster the further in - or out - the hand goes.
    // Slowed by about two fifths after the first try (500 / 40 / 3000, read
    // as a touch fast); smooth as it was, so nothing else changed.
    private const double CropEdgePanBand = 8;
    private const double CropEdgePanSpeed = 300;      // per second, at the band's inner edge
    private const double CropEdgePanGrowth = 25;      // per second, per unit deeper
    private const double CropEdgePanMaxSpeed = 2000;

    private bool _viewerCropEdgePanning;
    private TimeSpan _viewerCropEdgePanLast;

    // Draw and resize only: a move carries a rectangle the picture already
    // holds, and its room is the whole picture (ViewerCropReach's note).
    private Vector ViewerCropEdgePanVelocity(Point host)
    {
        if (_viewerCropDrag is not (CropDrag.Draw or CropDrag.Resize))
        {
            return default;
        }

        double width = ViewerImageHost.ActualWidth;
        double height = ViewerImageHost.ActualHeight;
        double Speed(double depth) => depth <= 0
            ? 0
            : Math.Min(CropEdgePanMaxSpeed, CropEdgePanSpeed + depth * CropEdgePanGrowth);

        double vx = host.X < CropEdgePanBand ? -Speed(CropEdgePanBand - host.X)
            : host.X > width - CropEdgePanBand ? Speed(host.X - (width - CropEdgePanBand))
            : 0;
        double vy = host.Y < CropEdgePanBand ? -Speed(CropEdgePanBand - host.Y)
            : host.Y > height - CropEdgePanBand ? Speed(host.Y - (height - CropEdgePanBand))
            : 0;
        return new Vector(vx, vy);
    }

    // Started and stopped from the drag's own moves; each frame asks again.
    private void UpdateViewerCropAutoPan()
    {
        bool wanted = ViewerCropEdgePanVelocity(_viewerCropLastHost) != default && ViewerCanPan;
        if (wanted == _viewerCropEdgePanning)
        {
            return;
        }

        _viewerCropEdgePanning = wanted;
        if (wanted)
        {
            _viewerCropEdgePanLast = TimeSpan.Zero;
            CompositionTarget.Rendering += ViewerCropEdgePan_Rendering;
        }
        else
        {
            CompositionTarget.Rendering -= ViewerCropEdgePan_Rendering;
        }
    }

    private void StopViewerCropEdgePan()
    {
        if (_viewerCropEdgePanning)
        {
            _viewerCropEdgePanning = false;
            CompositionTarget.Rendering -= ViewerCropEdgePan_Rendering;
        }
    }

    private void ViewerCropEdgePan_Rendering(object? sender, EventArgs e)
    {
        if (_viewerCropDrag == CropDrag.None)
        {
            StopViewerCropEdgePan();
            return;
        }

        // Rendering can be raised more than once for the same frame; only a
        // new frame time moves anything.
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _viewerCropEdgePanLast)
        {
            return;
        }

        // The first frame only takes the time; after a stall, no jump.
        double seconds = _viewerCropEdgePanLast == TimeSpan.Zero
            ? 0
            : Math.Min(0.05, (now - _viewerCropEdgePanLast).TotalSeconds);
        _viewerCropEdgePanLast = now;

        var velocity = ViewerCropEdgePanVelocity(_viewerCropLastHost);
        if (velocity == default)
        {
            StopViewerCropEdgePan();
            return;
        }

        // The hand past the right edge wants what lies to the right, so the
        // picture moves left.
        double panX = ViewerZoomPan.X;
        double panY = ViewerZoomPan.Y;
        ViewerZoomPan.X -= velocity.X * seconds;
        ViewerZoomPan.Y -= velocity.Y * seconds;
        ClampViewerPan();
        if (ViewerZoomPan.X == panX && ViewerZoomPan.Y == panY)
        {
            // The picture's own edge, or the first frame: nothing to redraw.
            return;
        }

        UpdateViewerNavigator();
        UpdateViewerCropDrag(_viewerCropLastHost);
    }

    // Whether the drag under way has changed the rectangle - a press inside it
    // that never moved is not a new area to remember.
    private bool _viewerCropDragChanged;

    // NEVER EMPTY (2026-11-03, on request): a side pulled down to nothing - the
    // hand back over the anchor, an edge laid onto its opposite - stays one
    // pixel wide on the side the hand is on. It used to read 0 and, released
    // there, took the whole rectangle with it.
    private Rect AtLeastOnePixel(Rect area, Point anchor, Point hand)
    {
        double left = area.Left, top = area.Top, right = area.Right, bottom = area.Bottom;
        if (right - left < 1)
        {
            if (hand.X >= anchor.X)
            {
                right = left + 1;
            }
            else
            {
                left = right - 1;
            }
        }
        if (bottom - top < 1)
        {
            if (hand.Y >= anchor.Y)
            {
                bottom = top + 1;
            }
            else
            {
                top = bottom - 1;
            }
        }

        // Kept on the picture: an anchor on its far edge has no room past it.
        double shiftX = left < 0 ? -left : right > _viewerPixelWidth ? _viewerPixelWidth - right : 0;
        double shiftY = top < 0 ? -top : bottom > _viewerPixelHeight ? _viewerPixelHeight - bottom : 0;
        return new Rect(new Point(left + shiftX, top + shiftY), new Point(right + shiftX, bottom + shiftY));
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
        StopViewerCropEdgePan();
        if (ViewerImageHost.IsMouseCaptured)
        {
            ViewerImageHost.ReleaseMouseCapture();
        }

        if (_viewerCropSelection is { Width: < 1 } or { Height: < 1 })
        {
            _viewerCropSelection = null;
        }
        else
        {
            _viewerCropSelectionAt = Environment.TickCount64;
        }

        // The rectangle the hand has just finished drawing, moving or sizing
        // is the last area from now on (see RememberViewerCropArea).
        if (_viewerCropDragChanged && _viewerCropSelection is { } finished)
        {
            RememberViewerCropArea(finished);
        }
        _viewerCropDragChanged = false;
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

    // ----- Space: the pan in crop mode -----------------------------------------
    //
    // A left drag draws in crop mode, so a zoomed picture needs its pan from
    // somewhere else. It was the MIDDLE button for the first day - a drag
    // panned, an unmoved click still toggled full screen on its release -
    // and that gave the one button two meanings that changed with the mode
    // (2026-11-04, on report). Now, as in Photoshop, Space held turns the
    // cursor into the pan hand and a left drag pans, wherever on the picture
    // it starts, the rectangle included; the middle button means full screen
    // in crop mode as everywhere else.
    //
    // THE PRICE: while crop mode is on, Space is not "next picture" (↓ still
    // is) - held, it would page the folder under a hand that meant to pan.
    // Its repeats are swallowed with it.
    private bool HandleViewerCropSpace(KeyEventArgs e)
    {
        if (!_viewerCropMode || e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.None)
        {
            return false;
        }

        if (!_viewerCropSpaceHeld)
        {
            _viewerCropSpaceHeld = true;
            if (_viewerCropDrag == CropDrag.None && !_viewerPanning)
            {
                ViewerImageHost.Cursor = ViewerHostRestCursor;
            }
        }
        return true;
    }

    private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _viewerCropSpaceHeld)
        {
            ReleaseViewerCropSpace();
            e.Handled = true;
        }
    }

    // Space let go, or lost with the window's activation (OnDeactivated: the
    // key's release goes to whatever window has the keyboard by then, and a
    // Space left "held" would make every later press a pan).
    private void ReleaseViewerCropSpace()
    {
        if (!_viewerCropSpaceHeld)
        {
            return;
        }

        _viewerCropSpaceHeld = false;
        EndViewerCropSpacePan();
        if (_viewerCropMode && _viewerCropDrag == CropDrag.None)
        {
            ViewerImageHost.Cursor = CropCursorFor(ViewerCropOverlay.HitPart(Mouse.GetPosition(ViewerCropOverlay)));
        }
    }

    // The ordinary pan's own state, so the host's move handler carries it and
    // its release ends it, as for a pan outside crop mode. A picture that fits
    // has nothing to pan: the press is taken and nothing moves, rather than
    // drawing under a hand that is holding the pan key.
    private void StartViewerCropSpacePan(MouseButtonEventArgs e)
    {
        if (!ViewerCanPan)
        {
            return;
        }

        var point = e.GetPosition(ViewerImageHost);
        _viewerPanning = true;
        _viewerCropSpacePanning = true;
        _viewerPanOrigin = point;
        _viewerPanOriginX = ViewerZoomPan.X;
        _viewerPanOriginY = ViewerZoomPan.Y;
        ViewerImageHost.Cursor = Cursors.ScrollAll;
        ViewerImageHost.CaptureMouse();
    }

    private void EndViewerCropSpacePan()
    {
        if (!_viewerCropSpacePanning)
        {
            return;
        }

        _viewerCropSpacePanning = false;
        _viewerPanning = false;
        if (ViewerImageHost.IsMouseCaptured)
        {
            ViewerImageHost.ReleaseMouseCapture();
        }
    }

    // ----- Keys --------------------------------------------------------------

    // Esc ends crop mode. Ctrl+C copies, Ctrl+S saves beside the original as
    // the 저장 buttons do (2026-11-03, on request), and Ctrl+Shift+S saves
    // under a name asked for - the text view's key for it - whatever there is
    // to cut: the rectangle while there is one, else the temporary crop
    // (but see Ctrl+C with files marked, below). With neither, the keys go
    // on to mean what they always have: Ctrl+C copies the FILE,
    // Ctrl+Shift+S saves the active preset. That second one is
    // OVERRIDDEN while there is a cut to take - the price of the text view's
    // save-as key, which the crop was given on purpose. Ctrl+S takes nothing
    // from the text view: with a picture on screen there is no text being
    // edited. Asked ahead of every other window key, since all of these mean
    // something else further down.
    private bool HandleViewerCropKey(KeyEventArgs e)
    {
        if (KeyboardInTextField)
        {
            return false;
        }

        // F4 IS THE BAR'S 자르기 (2026-11-03, on request): on and off, and
        // nothing else - a temporary crop stays, as it does for the button.
        // An F key for the reason F8 is one: with a picture on screen every
        // letter already means something to the tree's type-ahead. F4 was
        // free; F10 is Windows' menu key and F11 is left for full screen.
        // Only while the picture can be cut, so it is dead rather than
        // mysterious anywhere else.
        if (e.Key == Key.F4 && Keyboard.Modifiers == ModifierKeys.None && CanCropViewerPicture)
        {
            if (_viewerCropMode)
            {
                ExitViewerCropMode();
            }
            else
            {
                EnterViewerCropMode();
            }
            return true;
        }

        if (HandleViewerCropSpace(e))
        {
            return true;
        }

        if (_viewerCropMode && e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            ExitViewerCropMode();
            return true;
        }

        // Ctrl+D lets the rectangle go, Photoshop's 선택 해제 (2026-11-04, on
        // request) - the same as a click outside it, which still does. Only
        // while there is one; Ctrl+D means nothing else here.
        if (_viewerCropMode && _viewerCropSelection is not null &&
            e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
        {
            EndViewerCropDrag();
            _viewerCropSelection = null;
            UpdateViewerCropOverlay();
            return true;
        }

        // 자르기 취소 (2026-11-03, on request), and a turn's 회전 취소 since
        // 11-05. The whole way back to the picture as the file has it, as the
        // button does - not one cut or turn at a time. With no temporary edit
        // the key is left alone: the text view answers it inside the editor,
        // and the tree since the same evening with 복사 취소 · 이동 취소 - so a
        // second Ctrl+Z after an edit is taken back undoes the last paste, as
        // a second undo does anywhere.
        //
        // WHILE A CUT IS BEING TAKEN the press is swallowed (2026-11-06
        // review): on a NAS the decode takes seconds, and a take-back in that
        // gap was undone by the cut landing, a turned cut standing on screen
        // with the turn already cleared. Nor is it handed on to the tree, whose
        // answer would be to take the last paste away.
        if ((_viewerCropApplied is not null || _viewerTurn != 0 || _viewerCropBusy) &&
            e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // A repeat is the press that was swallowed, still held: let
            // through, it would take back the cut that had just landed.
            if (!_viewerCropBusy && !e.IsRepeat)
            {
                RevertViewerCrop();
            }
            return true;
        }

        if (!HasViewerCropToTake)
        {
            return false;
        }

        // MARKED FILES TAKE Ctrl+C WHEN THEY CAME LAST (11-03 review). With a
        // rectangle up or a temporary crop standing, Ctrl+A in the tree or the
        // strip marks files, and the copy after that is of the files the
        // screen shows as chosen - it used to put the cut on the clipboard,
        // and a paste then did nothing. The rule is whichever the hand went to
        // last: marks made after the rectangle are copied, a rectangle drawn
        // (or put back) after the marks is cut. A temporary crop with no
        // rectangle is never the newer one - it sits there for a second cut.
        // The first version let any rectangle keep the key, and Ctrl+A,
        // Ctrl+C in the tree under one copied nothing a paste could use.
        //
        // Copied from here rather than handed on: only the tree's and the
        // results' own key handlers copy files, and the plate's buttons take
        // the keyboard when pressed, so after 잘라서 보기 the key would
        // reach neither and copy nothing.
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            bool rectangleIsNewer = _viewerCropMode && _viewerCropSelection is not null &&
                                    _viewerCropSelectionAt > _marksChangedAt;
            if (_multiSelection.Count > 0 && !rectangleIsNewer)
            {
                if (_isSearchViewActive)
                {
                    SearchCopy_Click(this, e);
                }
                else
                {
                    CopyItem_Click(this, e);
                }
                return true;
            }

            CopyViewerCropToClipboard();
            return true;
        }

        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveViewerCrop(askName: false);
            return true;
        }

        if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            SaveViewerCrop(askName: true);
            return true;
        }

        return false;
    }

    // A turned picture counts as a temporary crop here: Ctrl+C copies it as
    // it stands, Ctrl+S saves it as name-rotated.
    private bool HasViewerCropToTake
        => (_viewerCropMode && _viewerCropSelection is not null) || _viewerCropApplied is not null || _viewerTurn != 0;

    // ----- The buttons -------------------------------------------------------

    // By the rectangle: 잘라서 보기 and 저장 (a click outside it clears it). In
    // the bar: 저장 and 자르기 취소, for the temporary crop.
    private void ViewerCropApply_Click(object sender, RoutedEventArgs e) => ApplyViewerCrop();

    private void ViewerCropSaveSelection_Click(object sender, RoutedEventArgs e) => SaveViewerCrop(askName: false);

    // What is on screen, not a rectangle that may have been drawn over it
    // since: the bar's 저장 belongs to the temporary crop. Said to the save
    // rather than done by clearing the rectangle first, as it was until the
    // 11-03 review - a save that failed left the dashed outline standing over
    // a rectangle that no longer existed, and Ctrl+C then copied the whole cut.
    private void ViewerCropSaveApplied_Click(object sender, RoutedEventArgs e)
        => SaveViewerCrop(askName: false, wholeApplied: true);

    private void ViewerCropRevert_Click(object sender, RoutedEventArgs e) => RevertViewerCrop();

    // ----- The temporary crop ------------------------------------------------

    // THE CUT TAKES THE PICTURE'S PLACE: the same Image, sized as a picture of
    // its own (its pixels, at the rest state the panel keeps), so the wheel,
    // the navigator, the zoom strip and a second crop all work on it as they
    // do on a file. Crop mode stays on with no rectangle, for that second one.
    private async void ApplyViewerCrop()
    {
        if (_viewerCropBusy || _viewerCropSelection is not { } selection || _pendingViewerPath is not { } path)
        {
            return;
        }

        BitmapSource? cut;
        SetViewerCropBusy(true);
        try
        {
            cut = await TakeViewerCropAsync();
        }
        finally
        {
            SetViewerCropBusy(false);
        }

        if (cut is null || !string.Equals(_pendingViewerPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // The cut's place in the file, before it becomes the picture on screen.
        _viewerCropAppliedOrigin = new Point(
            _viewerCropAppliedOrigin.X + selection.X, _viewerCropAppliedOrigin.Y + selection.Y);

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

    // 자르기 취소 · 회전 취소: the picture as it was before the first cut or
    // turn, at rest, crop mode off. The file's picture a turn was applied to
    // goes back in place of the one kept at the start where it can: a sharper
    // decode may have replaced it since (TurnViewerArrival).
    private void RevertViewerCrop()
    {
        if (_viewerCropOriginal is not { } original)
        {
            return;
        }

        var turnBase = _viewerTurnBase;
        ExitViewerCropMode();
        _viewerCropApplied = null;
        _viewerCropOriginal = null;
        _viewerCropAppliedOrigin = default;
        _viewerTurn = 0;
        _viewerTurnBase = null;

        if (original.WasAnimated)
        {
            // Read again - the frames were let go (see ViewerCropOriginal).
            _pendingViewerPath = null;
            UpdateViewerPreview();
            return;
        }

        ViewerImage.Source = turnBase ?? original.Source;
        _viewerPixelWidth = original.PixelWidth;
        _viewerPixelHeight = original.PixelHeight;
        _viewerDecodedWidth = turnBase?.PixelWidth ?? original.DecodedWidth;
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
    // rectangle or when asked for it whole, the temporary crop - or, turned
    // with nothing cut, the whole turned picture at full resolution. Null when
    // there is nothing.
    private async Task<BitmapSource?> TakeViewerCropAsync(bool wholeApplied = false)
    {
        Rect selection;
        if (!wholeApplied && _viewerCropMode && _viewerCropSelection is { } drawn)
        {
            selection = drawn;
        }
        else if (ViewerTurnedOnly)
        {
            selection = new Rect(0, 0, _viewerPixelWidth, _viewerPixelHeight);
        }
        else
        {
            return _viewerCropApplied;
        }

        // Read before the first await: the picture the cut is of.
        int turn = _viewerTurn;
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

        // A PSD: the whole picture is the shell's (FetchViewerShellWhole -
        // the one already on its way if crop mode asked for it). A handler
        // that answers smaller than the file gives the area at its own scale,
        // the most there is to have.
        if (ViewerShowsSizedShellPicture)
        {
            if (await FetchViewerShellWhole(path) is not { } fetched)
            {
                return null;
            }

            var whole = TurnBitmap(fetched, turn);
            double scale = (double)whole.PixelWidth / _viewerPixelWidth;
            return CutViewerBitmap(whole, scale >= 0.999
                ? area
                : new Int32Rect((int)(area.X * scale), (int)(area.Y * scale),
                    Math.Max(1, (int)(area.Width * scale)), Math.Max(1, (int)(area.Height * scale))));
        }

        return await Task.Run(() => DecodeViewerOriginal(path, turn) is { } original
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

    // ----- The last area, again -----------------------------------------------
    //
    // 이전 영역 불러오기 (2026-11-03, on request): the last rectangle, put back
    // on the picture on screen at the same place and size - a run of captures
    // of the same page layout, cut the same way one after another. In the
    // file's own pixels, so one drawn on a temporary crop counts where it
    // really falls in the file; kept in the settings, so the run survives a
    // restart.
    //
    // REMEMBERED WHEN THE HAND FINISHES A RECTANGLE - drawn, moved or sized
    // (EndViewerCropDrag). It was "when a rectangle is used" (cut, saved,
    // copied) for the first hours, on the argument that the button promises
    // the area that was cut; in use that kept two areas in play: a rectangle
    // set with care, then lost to a wrong picture chosen, could not be had
    // back, and the button brought the older one instead. Now a new rectangle
    // simply IS the last area, and nothing is asked of the hand to keep it.
    // A rectangle the buttons PUT back changes nothing until the hand moves
    // it.
    //
    // On a picture the area does not fit, the button is greyed and says why;
    // it is never moved or cut down to fit (see FitViewerCropArea, which
    // replaced the cutting down on 2026-11-03).
    //
    // THE PANEL'S VERSION (2026-11-03, on request): the same moment also
    // records where the rectangle stood on the panel, in the host's own
    // units, and the second button puts it back THERE - over whatever picture,
    // size or zoom is under it now - reading off the pixels that place
    // covers. One area is the file's, the other the screen's; both are written
    // together, so the two buttons always mean the same last crop.

    private Rect? LastViewerCropArea
        => _settings.ViewerCropLastArea is [var x, var y, var width, var height] && width > 0 && height > 0
            ? new Rect(x, y, width, height)
            : null;

    private Rect? LastViewerCropPanelArea
        => _settings.ViewerCropLastPanelArea is [var x, var y, var width, var height] && width > 0 && height > 0
            ? new Rect(x, y, width, height)
            : null;

    private void RememberViewerCropArea(Rect selection)
    {
        int[] area =
        {
            (int)(selection.X + _viewerCropAppliedOrigin.X),
            (int)(selection.Y + _viewerCropAppliedOrigin.Y),
            (int)selection.Width,
            (int)selection.Height,
        };

        // Where the overlay draws it now (UpdateViewerCropOverlay's mapping).
        var picture = ViewerPictureRectInHost();
        double display = ViewerDisplayScale;
        double[] panelArea =
        {
            Math.Round(picture.X + selection.X * display, 2),
            Math.Round(picture.Y + selection.Y * display, 2),
            Math.Round(selection.Width * display, 2),
            Math.Round(selection.Height * display, 2),
        };

        if (_settings.ViewerCropLastArea is { } held && held.SequenceEqual(area) &&
            _settings.ViewerCropLastPanelArea is { } heldPanel && heldPanel.SequenceEqual(panelArea))
        {
            return;
        }

        _settings.ViewerCropLastArea = area;
        _settings.ViewerCropLastPanelArea = panelArea;
        _settingsService.Save(_settings);
        UpdateViewerPictureBar();
    }

    private void ViewerCropReuse_Click(object sender, RoutedEventArgs e)
    {
        if (ViewerCropReuseTarget is { } target)
        {
            PlaceViewerCropSelection(target);
        }
    }

    private void ViewerCropReusePanel_Click(object sender, RoutedEventArgs e)
    {
        if (ViewerCropReusePanelTarget is { } target)
        {
            PlaceViewerCropSelection(target);
        }
    }

    // THE LAST AREA GOES BACK WHOLE OR NOT AT ALL (2026-11-03, on request).
    // It used to be cut down to what fell on the picture - and an area wholly
    // off a smaller picture turned crop mode on and drew nothing, so the
    // button read as a second 자르기. Either way the rectangle that came back
    // was not the one remembered, and nothing said so. Now an area that does
    // not lie inside the picture on screen greys its button, with the reason
    // in its tooltip, and a press always puts back exactly what was kept.
    //
    // ONE PIXEL OVER AN EDGE COUNTS AS ON IT, and is taken back to the edge.
    // An area drawn against the picture's edge comes back from the panel's
    // units through a division, and rounding can put that edge a pixel out -
    // which would grey the button on the very picture it was drawn on.
    private const double ViewerCropReuseSlack = 1;

    private Rect? FitViewerCropArea(Rect area)
    {
        if (_viewerPixelWidth <= 0 || _viewerPixelHeight <= 0 || area.Width < 1 || area.Height < 1 ||
            area.Left < -ViewerCropReuseSlack || area.Top < -ViewerCropReuseSlack ||
            area.Right > _viewerPixelWidth + ViewerCropReuseSlack ||
            area.Bottom > _viewerPixelHeight + ViewerCropReuseSlack)
        {
            return null;
        }

        area.Intersect(new Rect(0, 0, _viewerPixelWidth, _viewerPixelHeight));
        return area.IsEmpty || area.Width < 1 || area.Height < 1 ? null : area;
    }

    // The image button's area in the pixels of the picture on screen - the
    // file's, less where a temporary crop starts - or null when it does not
    // fit there.
    private Rect? ViewerCropReuseTarget
        => LastViewerCropArea is { } area
            ? FitViewerCropArea(new Rect(area.X - _viewerCropAppliedOrigin.X, area.Y - _viewerCropAppliedOrigin.Y,
                area.Width, area.Height))
            : null;

    // The panel button's: the pixels that place on the panel covers now, to
    // the NEAREST whole ones - outward rounding would grow the same area by a
    // pixel on the same picture, out of nothing but float noise. It moves with
    // the zoom, the pan and the panel's width, so its button is asked again
    // on every one of those (UpdateViewerCropReusePanelButton).
    private Rect? ViewerCropReusePanelTarget
    {
        get
        {
            if (LastViewerCropPanelArea is not { } onPanel)
            {
                return null;
            }

            var topLeft = HostToPicturePixel(onPanel.TopLeft);
            var bottomRight = HostToPicturePixel(onPanel.BottomRight);
            return FitViewerCropArea(new Rect(
                new Point(Math.Round(topLeft.X), Math.Round(topLeft.Y)),
                new Point(Math.Round(bottomRight.X), Math.Round(bottomRight.Y))));
        }
    }

    // Asked with the crop overlay, on every zoom, pan frame and resize: one
    // rectangle mapped and compared. Its two tooltips are fixed strings, so a
    // frame that changes nothing writes nothing.
    private void UpdateViewerCropReusePanelButton()
    {
        bool stored = LastViewerCropPanelArea is not null;
        bool canCrop = CanCropViewerPicture;
        bool fits = canCrop && ViewerCropReusePanelTarget is not null;
        if (ViewerCropReusePanelButton.IsEnabled != fits)
        {
            ViewerCropReusePanelButton.IsEnabled = fits;
        }

        string tip = stored && canCrop && !fits ? Strings.ViewerCropReusePanelOutside : Strings.ViewerCropReusePanel;
        if (!ReferenceEquals(ViewerCropReusePanelButton.ToolTip, tip))
        {
            ViewerCropReusePanelButton.ToolTip = tip;
        }
    }

    // Either button's last step: crop mode on, the area shown with its
    // buttons. The callers have already checked that it fits.
    private void PlaceViewerCropSelection(Rect area)
    {
        if (!_viewerCropMode)
        {
            EnterViewerCropMode();
            if (!_viewerCropMode)
            {
                return;
            }
        }

        area.Intersect(new Rect(0, 0, _viewerPixelWidth, _viewerPixelHeight));
        if (area.IsEmpty || area.Width < 1 || area.Height < 1)
        {
            return;
        }

        _viewerCropSelection = area;
        _viewerCropSelectionAt = Environment.TickCount64;
        _viewerCropDrag = CropDrag.None;
        UpdateViewerCropOverlay();
    }

    // ----- Save --------------------------------------------------------------

    // BESIDE THE ORIGINAL, under its name with -cropped, numbered " (2)" on as
    // paste numbers a copy - never over anything. Or, with askName, wherever
    // the save dialog says, which may write over a file it asked about.
    //
    // THE SUFFIX SAYS WHAT WAS DONE TO THE COPY, in the past form: -cropped,
    // -rotated, and the EXIF view's -metadataremoved (2026-11-05, the author's
    // choice of the most exact). They were -crop and -rotate until then -
    // changed before either was released, so no copy in anyone's hands has
    // the old names.
    //
    // In the original's format where WPF can write it (JPEG at quality 95,
    // PNG, BMP, TIFF, GIF), PNG otherwise - WebP, HEIC, AVIF and an icon have
    // no encoder here. No metadata goes with it: the cut is a new picture,
    // the camera's record of the old one is not its own, and the EXIF turn is
    // already in its pixels (a separate EXIF view and editor is planned).
    //
    // A PICTURE ONLY TURNED (2026-11-05) is saved the same way, as
    // name-rotated - the author's call, over writing the turn into the file
    // itself: the original stays as it was, and 저장 means one thing whatever
    // was done. A JPEG going to a JPEG is then its own bytes with the
    // orientation tag turned (WriteViewerTurnedJpeg), so it loses nothing;
    // anything else is written from the turned pixels as a cut is.
    //
    // After it: back to the picture as the file has it, crop mode off - the
    // job is done - and the panel stays on the original. The new file is put
    // in the tree and the strip at once rather than when the watcher gets to
    // it, so the save reads as having happened.
    private async void SaveViewerCrop(bool askName, bool wholeApplied = false)
    {
        if (_viewerCropBusy || _pendingViewerPath is not { } path ||
            Path.GetDirectoryName(path) is not { Length: > 0 } folder)
        {
            return;
        }

        bool turnOnly = ViewerTurnedOnly && (wholeApplied || !(_viewerCropMode && _viewerCropSelection is not null));
        int turn = _viewerTurn;
        bool saved = false;
        string? error = null;
        string target;
        SetViewerCropBusy(true);
        try
        {
            // A turn needs no pixels yet: a JPEG's are not read at all.
            BitmapSource? cut = null;
            if (!turnOnly)
            {
                cut = await TakeViewerCropAsync(wholeApplied);
                if (cut is null || !string.Equals(_pendingViewerPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            string extension = CropSaveExtension(Path.GetExtension(path));
            target = FileOperationService.GetUniqueDestination(
                Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + (turnOnly ? "-rotated" : "-cropped") + extension));
            bool replace = false;

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

                // The dialog asked before writing over the name it was given.
                target = Path.GetFullPath(dialog.FileName);
                replace = true;

                // A name the encoders cannot write (a .webp typed in, no
                // extension at all) is written as PNG, and says so in its
                // name. That name is not the one the dialog asked about, so
                // it writes over nothing: numbered, as beside the original.
                // It went out with replace on until the 11-03 review, and
                // shot.webp.png, if there was one, was replaced unasked.
                string chosen = Path.GetExtension(target).ToLowerInvariant();
                if (CropSaveExtension(chosen) != chosen)
                {
                    target = FileOperationService.GetUniqueDestination(target + ".png");
                    replace = false;
                }
            }

            bool written = false;
            if (turnOnly && IsJpegExtension(Path.GetExtension(path)) && IsJpegExtension(Path.GetExtension(target)))
            {
                if (await Task.Run(() => WriteViewerTurnedJpeg(path, target, turn, replace)) is { } outcome)
                {
                    (saved, error) = outcome;
                    written = true;
                }
                else
                {
                    ViewerLoadLog($"rotate save: the orientation tag could not carry the turn, pixels re-encoded  {Path.GetFileName(path)}");
                }
            }

            if (!written)
            {
                cut ??= await TakeViewerCropAsync(wholeApplied: true);
                if (cut is null || !string.Equals(_pendingViewerPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                (saved, error) = await Task.Run(() => WriteViewerCrop(cut, target, replace));
            }
        }
        finally
        {
            SetViewerCropBusy(false);
        }

        if (!saved)
        {
            MessageBox.Show(this, error ?? string.Empty,
                turnOnly ? Strings.ViewerRotateSaveFailedTitle : Strings.ViewerCropSaveFailedTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.Equals(_pendingViewerPath, path, StringComparison.OrdinalIgnoreCase))
        {
            if (_viewerCropApplied is not null || _viewerTurn != 0)
            {
                RevertViewerCrop();
            }
            else
            {
                ExitViewerCropMode();
            }

            // WRITTEN OVER THE FILE ON SCREEN - its own name chosen in the save
            // dialog: what the panel holds is that file as it was, so it is
            // read again. Missing until 2026-11-05 (found reading the code,
            // not seen): the crop's save left the old picture on screen. The
            // EXIF view is read again with it: a cut is written with no
            // metadata, and the view keys on the path, so it went on listing
            // what the file no longer holds (2026-11-06 review).
            if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            {
                _viewerExifPath = null;
                _viewerExifProbePath = null;
                _pendingViewerPath = null;
                UpdateViewerPreview();
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
                                       or InvalidOperationException or ArgumentException
                                       or OutOfMemoryException or System.Runtime.InteropServices.COMException)
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
    // turn applied and then the panel's own (quarterTurns, see
    // MainWindow.Rotate.cs), frozen so it can cross threads. Null where the
    // panel's own decode would have failed too - and where the full-resolution pass's
    // would (OutOfMemoryException, COMException; see its safety device in
    // MainWindow.xaml.cs), since this is the same uncapped decode. Those two
    // were missing until the 11-03 review, and either one, coming back
    // through an async void, ended the app. The same pair is caught
    // wherever this path allocates a picture's worth: the cut, the encoders.
    private static BitmapSource? DecodeViewerOriginal(string path, int quarterTurns = 0)
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
            return TurnBitmap(oriented, quarterTurns);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or FileFormatException or ArgumentException or InvalidOperationException
                                       or OutOfMemoryException or System.Runtime.InteropServices.COMException)
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

        try
        {
            var cropped = new CroppedBitmap(source, new Int32Rect(area.X, area.Y, width, height));
            var copy = new WriteableBitmap(new FormatConvertedBitmap(cropped, PixelFormats.Bgra32, null, 0));
            copy.Freeze();
            return copy;
        }
        catch (Exception ex) when (ex is OutOfMemoryException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
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
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(png);
        }
        catch (Exception ex) when (ex is OutOfMemoryException or System.Runtime.InteropServices.COMException)
        {
            return;
        }
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
