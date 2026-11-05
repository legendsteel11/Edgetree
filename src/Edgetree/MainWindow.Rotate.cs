using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SidebarExplorer.App.Services;
using Size = System.Windows.Size;

namespace SidebarExplorer.App;

// ===== Turning the panel's picture (2026-11-05, the author's design) =====
//
// 왼쪽으로 회전 and 오른쪽으로 회전 on the picture's bar, a quarter turn a
// press, and a turned picture lights the bar's 저장 - turning it and then not
// being able to keep it would be half a feature. It is a temporary edit of
// the same kind as the crop's (MainWindow.Crop.cs): the same take-back
// (Ctrl+Z, the bar's undo) to the picture as the file has it, the same save
// beside the original - as name-rotated when nothing was cut - and a crop can
// be drawn on a turned picture or a cut turned.
//
// THE PICTURE ON SCREEN TURNS AT ONCE, from whatever the panel had decoded:
// a press must not wait on a NAS. A quarter turn then asks for a decode sized
// for the picture's other side (RequestViewerTurnedDecode), and every sharper
// picture of the file that arrives afterwards - the zoom's full-resolution
// pass, a PSD's whole picture - is turned on its way in (TurnViewerArrival).
//
// EVERYTHING DOWNSTREAM SEES THE TURNED PICTURE: its pixel sides, the
// rectangle, the last area remembered, the cut. "The file's pixels" in the
// crop's notes means the picture as shown, turned if it is.
//
// A JPEG IS SAVED BY ITS ORIENTATION TAG, not its pixels - see JpegOrientation
// for the measurement that decided it. Everything else is saved as the crop
// saves, from the turned pixels.
public partial class MainWindow
{
    // Quarter turns clockwise from the picture as the file has it, 0 to 3.
    private int _viewerTurn;

    // The file's own picture behind a turn with nothing cut: what each press
    // turns again, rather than turning a turn, and what a sharper decode
    // replaces. Null while the picture is not turned.
    private BitmapSource? _viewerTurnBase;

    // Turned, and nothing cut: what a save writes as name-rotated.
    private bool ViewerTurnedOnly => _viewerTurn != 0 && _viewerCropApplied is null;

    // Where a crop can be, except an animation: a turn would stop it on one
    // frame, and saving that frame would quietly drop the rest.
    private bool CanTurnViewerPicture => CanCropViewerPicture && _viewerGifDecoder is null;

    private void ViewerRotateLeft_Click(object sender, RoutedEventArgs e) => TurnViewerPicture(-1);

    private void ViewerRotateRight_Click(object sender, RoutedEventArgs e) => TurnViewerPicture(1);

    private async void TurnViewerPicture(int quarterTurns)
    {
        if (!CanTurnViewerPicture || _viewerCropBusy || _pendingViewerPath is not { } path ||
            ViewerImage.Source is not BitmapSource shown)
        {
            return;
        }

        StopSlideshowOnUserAction("rotate");

        int pictureWidth = _viewerPixelWidth;
        int pictureHeight = _viewerPixelHeight;
        int turn = ((_viewerTurn + quarterTurns) % 4 + 4) % 4;

        if (_viewerCropApplied is { } applied)
        {
            // A cut is full resolution and turned again on the next press, so
            // it is turned into a copy of its own, on a worker: a chain of
            // turns over turns would be redrawn whole at every one.
            BitmapSource? turned;
            SetViewerCropBusy(true);
            try
            {
                turned = await Task.Run(() => CopyTurned(applied, quarterTurns));
            }
            finally
            {
                SetViewerCropBusy(false);
            }

            if (turned is null || !ReferenceEquals(_viewerCropApplied, applied) ||
                !string.Equals(_pendingViewerPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Where the cut sits in the whole picture turns with it - the
            // whole picture's sides being the file's, crossed while the turn
            // before this press was a quarter one.
            if (_viewerCropOriginal is { } original)
            {
                bool crossed = _viewerTurn % 2 == 1;
                _viewerCropAppliedOrigin = TurnRect(
                    new Rect(_viewerCropAppliedOrigin, new Size(applied.PixelWidth, applied.PixelHeight)),
                    crossed ? original.PixelHeight : original.PixelWidth,
                    crossed ? original.PixelWidth : original.PixelHeight,
                    quarterTurns).TopLeft;
            }

            _viewerCropApplied = turned;
            ViewerImage.Source = turned;
            _viewerPixelWidth = turned.PixelWidth;
            _viewerPixelHeight = turned.PixelHeight;
            _viewerDecodedWidth = turned.PixelWidth;
            SetViewerCaption(string.Format(Strings.ViewerCropCaptionFormat, turned.PixelWidth, turned.PixelHeight));
        }
        else
        {
            var original = _viewerCropOriginal ??= new ViewerCropOriginal(
                shown, _viewerPixelWidth, _viewerPixelHeight, _viewerDecodedWidth, _viewerCaptionFacts, WasAnimated: false);
            var turnBase = _viewerTurnBase ??= shown;
            var turnedShown = TurnBitmap(turnBase, turn);
            bool crossed = turn % 2 == 1;

            ViewerImage.Source = turnedShown;
            _viewerPixelWidth = crossed ? original.PixelHeight : original.PixelWidth;
            _viewerPixelHeight = crossed ? original.PixelWidth : original.PixelHeight;
            _viewerDecodedWidth = turnedShown.PixelWidth;

            if (turn == 0)
            {
                // All the way round: the file's own picture again, with
                // nothing to save or take back.
                SetViewerCaption(original.Caption);
                _viewerCropOriginal = null;
                _viewerTurnBase = null;
            }
            else
            {
                SetViewerCaption(string.Format(Strings.ViewerRotateCaptionFormat, _viewerPixelWidth, _viewerPixelHeight));
            }
        }

        _viewerTurn = turn;

        // A rectangle up in crop mode turns with the picture under it.
        if (_viewerCropMode && _viewerCropSelection is { } selection)
        {
            _viewerCropSelection = TurnRect(selection, pictureWidth, pictureHeight, quarterTurns);
        }

        ShowViewerAtRest();
        UpdateViewerPictureBar();
        UpdateViewerCropOverlay();
        if (_viewerCropApplied is null)
        {
            RequestViewerTurnedDecode();
        }
    }

    // Render-time, as ApplyExifOrientation's turns are: no copy of the pixels.
    private static BitmapSource TurnBitmap(BitmapSource source, int quarterTurns)
    {
        int turn = (quarterTurns % 4 + 4) % 4;
        if (turn == 0)
        {
            return source;
        }

        var turned = new TransformedBitmap(source, new RotateTransform(90 * turn));
        if (turned.CanFreeze)
        {
            turned.Freeze();
        }
        return turned;
    }

    private static BitmapSource? CopyTurned(BitmapSource source, int quarterTurns)
    {
        try
        {
            var copy = new WriteableBitmap(TurnBitmap(source, quarterTurns));
            copy.Freeze();
            return copy;
        }
        catch (Exception ex) when (ex is OutOfMemoryException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    // A rectangle in a picture frameWidth by frameHeight, in the frame that
    // picture has after the turn. A quarter clockwise takes the left edge to
    // the top: x' = height - bottom, y' = x.
    private static Rect TurnRect(Rect rect, double frameWidth, double frameHeight, int quarterTurns)
    {
        int turn = (quarterTurns % 4 + 4) % 4;
        for (int i = 0; i < turn; i++)
        {
            rect = new Rect(frameHeight - rect.Y - rect.Height, rect.X, rect.Height, rect.Width);
            (frameWidth, frameHeight) = (frameHeight, frameWidth);
        }
        return rect;
    }

    // A sharper picture of the file arriving - the full-resolution pass, the
    // turned decode below, a PSD's whole picture - while it is turned: it
    // becomes what the turn is applied to, and goes on screen turned. Kept as
    // it came while the picture is not turned.
    private BitmapSource TurnViewerArrival(BitmapSource fileBitmap)
    {
        if (_viewerTurn == 0)
        {
            return fileBitmap;
        }

        _viewerTurnBase = fileBitmap;
        return TurnBitmap(fileBitmap, _viewerTurn);
    }

    // A QUARTER TURN NEEDS THE PICTURE'S OTHER SIDE. The panel decodes a
    // picture for the slot's width, and turned on its side that width is its
    // height: a landscape photo decoded 900 wide for a 900 slot stands up 675
    // wide and is drawn blown up. So a turn asks for a decode whose side now
    // running across meets the slot - the pass a zoom asks for
    // (RequestViewerFullResolution), at the size this needs rather than whole.
    // A half turn keeps its sides and asks nothing.
    private void RequestViewerTurnedDecode()
    {
        if (_viewerResizing ||
            _viewerFullResPending ||
            _pendingViewerPath is not { } path ||
            _viewerPixelWidth <= 0 ||
            _viewerPixelHeight <= 0)
        {
            return;
        }

        int slot = ViewerDecodeWidth();
        if (_viewerDecodedWidth >= Math.Min(slot, _viewerPixelWidth))
        {
            return;
        }

        if (ViewerShowsSizedShellPicture)
        {
            _ = FetchViewerShellWhole(path);
            return;
        }

        // The loader caps the side the file shows across BEFORE this turn,
        // which after a quarter turn is the height on screen - so the cap
        // asked for is the slot over the turned picture's proportion.
        int decodeWidth = _viewerTurn % 2 == 0
            ? slot
            : (int)Math.Ceiling((double)slot * _viewerPixelHeight / _viewerPixelWidth);

        // The same safety device as RequestViewerFullResolution's, for the
        // same uncapped-decode reason.
        _viewerFullResPending = true;
        Task.Run(() =>
        {
            try
            {
                LoadViewerImage(path, decodeWidth, fullResolution: true);
            }
            catch (Exception ex) when (ex is OutOfMemoryException
                                           or System.Runtime.InteropServices.COMException)
            {
            }
            finally
            {
                Dispatcher.BeginInvoke(() => _viewerFullResPending = false);
            }
        });
    }

    private static bool IsJpegExtension(string extension)
        => extension.ToLowerInvariant() is ".jpg" or ".jpeg" or ".jfif";

    // A worker. The file's own bytes with the orientation tag turned
    // (JpegOrientation), written as WriteViewerCrop writes - a hidden
    // temporary file beside the target, then moved into place.
    //
    // SAFETY DEVICE: the copy is READ BACK before the move. It has to open
    // with the orientation it was given and the original's pixel size, read
    // by the same WIC query the panel reads with; otherwise the temporary file
    // goes and the answer is null, and the caller saves the turned pixels
    // instead, at the crop's quality 95, with a line in viewerload.log. What
    // it hides: a file whose structure JpegOrientation reads one way and WIC
    // another - it is still saved, re-encoded once, and only the log says so.
    private static (bool Saved, string? Error)? WriteViewerTurnedJpeg(
        string source, string target, int quarterTurns, bool replace)
    {
        byte[] original;
        try
        {
            original = File.ReadAllBytes(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, ex.Message);
        }

        if (JpegOrientation.Turn(original, quarterTurns, out int orientation) is not { } turned)
        {
            return null;
        }

        string folder = Path.GetDirectoryName(target) ?? string.Empty;
        string temp = FileOperationService.GetUniqueDestination(
            Path.Combine(folder, "~" + Path.GetFileName(target) + ".tmp"));
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                File.SetAttributes(temp, FileAttributes.Hidden | FileAttributes.Temporary);
                stream.Write(turned);
            }

            if (!ReadsAsTurned(temp, orientation, original))
            {
                File.Delete(temp);
                return null;
            }

            File.SetAttributes(temp, FileAttributes.Normal);
            File.Move(temp, target, overwrite: replace);
            return (true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException)
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

    private static bool ReadsAsTurned(string path, int orientation, byte[] original)
    {
        try
        {
            static BitmapFrame Header(Stream stream)
                => BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];

            using var before = new MemoryStream(original, writable: false);
            using var after = File.OpenRead(path);
            var was = Header(before);
            var now = Header(after);
            return ReadExifOrientation(now) == orientation &&
                   now.PixelWidth == was.PixelWidth &&
                   now.PixelHeight == was.PixelHeight;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or FileFormatException or ArgumentException or InvalidOperationException
                                       or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }
}
