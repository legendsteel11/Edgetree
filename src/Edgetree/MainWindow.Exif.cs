using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SidebarExplorer.App.Services;
// WinForms is a global using in this project (the tray icon), and it has a
// CheckBox, a MessageBox and a KeyEventArgs of its own.
using CheckBox = System.Windows.Controls.CheckBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace SidebarExplorer.App;

// ===== The panel's EXIF view and 속성 제거 (2026-11-05, the author's design) =====
//
// A picture's shooting details under its caption, above the thumbnail bar -
// what the picture IS sits together, and the strip stays last, as the row a
// picture is chosen FROM. Turned on and off by the picture bar's EXIF
// button, and remembered. Fifteen fields, the ones a photo viewer lists,
// named as Windows' Details tab names them (PhotoInfoService).
//
// THE FIFTEEN ARE ALWAYS THERE, IN THE SAME PLACES, and a field the file does
// not have is an empty value beside its name (on request, after the first
// version left such fields out and said "EXIF 정보 없음" for a file with
// none). Every field is where it was on the last picture, so stepping through
// a folder compares like with like, and the slot's height never depends on
// the file: it changes with the column count and the text size only. An
// earlier height sized to each file moved the strip and refitted the picture
// on every arrow key - twice, the details landing a moment after the picture.
// Capped at a third of the panel; past that it scrolls.
//
// ONE, TWO OR THREE COLUMNS by the panel's width (asked for with the design),
// worked out on a size change only. Read row by row.
//
// 속성 제거 IS A MODE OF THIS VIEW (on request: a box before each row rather
// than a window of its own). The picture bar's eraser turns it on - the view
// with it - and Esc, the eraser again, another file or the view going end it.
// While it is on, each row with a value gets a box, 그 밖의 정보 appears as a
// sixteenth row for what the view does not show, and a row under them holds
// 전체 선택 and the save. The ticked rows are left out of a COPY beside the
// original, name-clean, which is written the way the crop's save writes and
// read back before it is kept (see WriteViewerExifRemoved); the picture's
// bytes are not touched (JpegMetadataRemover). The panel stays on the
// original, and the copy is put in the tree at once.
public partial class MainWindow
{
    // The file the slot is showing, or being read for; null while it is shut,
    // so showing it again reads again.
    private string? _viewerExifPath;
    private int _viewerExifTicket;

    // The fields the file on screen has values for.
    private readonly HashSet<PhotoInfoService.Field> _viewerExifPresent = new();

    private bool _viewerExifRemoving;
    // A copy being written, from the press until it is kept or refused.
    private bool _viewerExifBusy;

    private static readonly PhotoInfoService.Field[] ViewerExifFields = Enum.GetValues<PhotoInfoService.Field>();

    // Built once, the first time the view is shown: one cell per field, in
    // the fields' order, and 그 밖의 정보 after them.
    private TextBlock[]? _viewerExifValues;
    private CheckBox[]? _viewerExifFieldChecks;
    private CheckBox? _viewerExifOtherCheck;
    private FrameworkElement? _viewerExifOtherCell;

    // A picture on screen in the panel, by the file's kind rather than by
    // what has been decoded: between two pictures the slot stays, rather than
    // closing while the next one loads and opening when it lands.
    private bool ViewerExifWanted
    {
        get
        {
            if (!_settings.ViewerExif || !_viewerOpen || _viewerFullscreen || ViewerMediaIsSelection ||
                _pendingViewerPath is not { } path || IsViewerPdf(path))
            {
                return false;
            }

            string extension = Path.GetExtension(path);
            return ThumbnailExtensions.Contains(extension) || SlowThumbnailExtensions.Contains(extension);
        }
    }

    // A JPEG with its bar up - the only kind JpegMetadataRemover writes.
    private bool CanRemoveViewerExif
        => ViewerPictureBarWanted && _pendingViewerPath is { } path && IsJpegExtension(Path.GetExtension(path));

    private void ViewerExifToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.ViewerExif = ViewerExifToggle.IsChecked == true;
        _settingsService.Save(_settings);
        UpdateViewerExif();
    }

    // With the picture bar, which is asked at every moment the thing on
    // screen changes (UpdateViewerPictureBar).
    private void UpdateViewerExif()
    {
        ViewerExifToggle.IsChecked = _settings.ViewerExif;

        bool canRemove = CanRemoveViewerExif;
        ViewerExifRemoveToggle.IsEnabled = canRemove && !_viewerExifBusy;
        ViewerExifRemoveToggle.ToolTip = canRemove ? Strings.ViewerExifRemove : Strings.ViewerExifRemoveUnavailable;
        if (!canRemove)
        {
            ExitViewerExifRemoval();
        }

        if (!ViewerExifWanted || _pendingViewerPath is not { } path)
        {
            if (ViewerExifHost.Visibility != Visibility.Collapsed)
            {
                ViewerExifHost.Visibility = Visibility.Collapsed;
            }
            _viewerExifPath = null;
            ExitViewerExifRemoval();
            return;
        }

        EnsureViewerExifCells();
        if (ViewerExifHost.Visibility != Visibility.Visible)
        {
            ViewerExifHost.Visibility = Visibility.Visible;
            UpdateViewerExifLayout();
        }

        if (string.Equals(_viewerExifPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Another file: a tick on one picture means nothing on the next, and
        // the last file's values go at once rather than standing under this
        // one's name until its own arrive.
        ExitViewerExifRemoval();
        _viewerExifPath = path;
        int ticket = ++_viewerExifTicket;
        ShowViewerExif([]);
        ViewerExifScroll.ScrollToTop();

        Task.Run(() =>
        {
            var fields = PhotoInfoService.Read(path);
            Dispatcher.BeginInvoke(() =>
            {
                if (ticket == _viewerExifTicket &&
                    string.Equals(_viewerExifPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    ShowViewerExif(fields);
                }
            });
        });
    }

    private void ShowViewerExif(IReadOnlyList<(PhotoInfoService.Field Field, string Value)> fields)
    {
        EnsureViewerExifCells();
        var values = fields.ToDictionary(f => f.Field, f => f.Value);
        _viewerExifPresent.Clear();
        for (int i = 0; i < ViewerExifFields.Length; i++)
        {
            string value = values.TryGetValue(ViewerExifFields[i], out var text) ? text : string.Empty;
            _viewerExifValues![i].Text = value;
            _viewerExifValues[i].ToolTip = value.Length > 0 ? value : null;
            if (value.Length > 0)
            {
                _viewerExifPresent.Add(ViewerExifFields[i]);
            }
        }

        ApplyViewerExifRemoval();
    }

    // The cells, once. Built in code, so every brush and size is set by
    // resource reference: a TextBlock made here inherits nothing it would get
    // in XAML, and an unset foreground is black on a dark panel.
    private void EnsureViewerExifCells()
    {
        if (_viewerExifValues is not null)
        {
            return;
        }

        _viewerExifValues = new TextBlock[ViewerExifFields.Length];
        _viewerExifFieldChecks = new CheckBox[ViewerExifFields.Length];
        for (int i = 0; i < ViewerExifFields.Length; i++)
        {
            var (cell, check, value) = ViewerExifCell(ViewerExifLabel(ViewerExifFields[i]));
            _viewerExifValues[i] = value;
            _viewerExifFieldChecks[i] = check;
            ViewerExifGrid.Children.Add(cell);
        }

        var (otherCell, otherCheck, otherValue) = ViewerExifCell(Strings.ExifOther);
        otherValue.Text = Strings.ExifOtherDetail;
        otherValue.ToolTip = Strings.ExifOtherDetail;
        otherCell.Visibility = Visibility.Collapsed;
        _viewerExifOtherCheck = otherCheck;
        _viewerExifOtherCell = otherCell;
        ViewerExifGrid.Children.Add(otherCell);
    }

    private (FrameworkElement Cell, CheckBox Check, TextBlock Value) ViewerExifCell(string label)
    {
        var cell = new Grid { Margin = new Thickness(0, 1, 12, 1) };
        cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ViewerExifLabel" });
        cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var check = new CheckBox
        {
            Style = (Style)FindResource("ViewerCheckBoxStyle"),
            Margin = new Thickness(0, 0, 6, 0),
            Visibility = Visibility.Collapsed,
        };
        check.Click += ViewerExifCheck_Click;

        var name = new TextBlock { Text = label, Margin = new Thickness(0, 0, 8, 0) };
        var value = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        // The label a step under its value, in an ink of its own rather than
        // at an opacity (see ViewerExifLabelForeground).
        name.SetResourceReference(TextBlock.ForegroundProperty, "ViewerExifLabelForeground");
        value.SetResourceReference(TextBlock.ForegroundProperty, "ForegroundText");
        foreach (var block in new[] { name, value })
        {
            block.SetResourceReference(TextBlock.FontSizeProperty, "MenuThumbnailInfoFontSize");
        }

        Grid.SetColumn(name, 1);
        Grid.SetColumn(value, 2);
        cell.Children.Add(check);
        cell.Children.Add(name);
        cell.Children.Add(value);
        return (cell, check, value);
    }

    private static string ViewerExifLabel(PhotoInfoService.Field field) => field switch
    {
        PhotoInfoService.Field.CameraMaker => Strings.ExifCameraMaker,
        PhotoInfoService.Field.CameraModel => Strings.ExifCameraModel,
        PhotoInfoService.Field.LensModel => Strings.ExifLensModel,
        PhotoInfoService.Field.DateTaken => Strings.ExifDateTaken,
        PhotoInfoService.Field.ExposureTime => Strings.ExifExposureTime,
        PhotoInfoService.Field.FNumber => Strings.ExifFNumber,
        PhotoInfoService.Field.IsoSpeed => Strings.ExifIsoSpeed,
        PhotoInfoService.Field.ExposureBias => Strings.ExifExposureBias,
        PhotoInfoService.Field.ExposureProgram => Strings.ExifExposureProgram,
        PhotoInfoService.Field.MeteringMode => Strings.ExifMeteringMode,
        PhotoInfoService.Field.Flash => Strings.ExifFlash,
        PhotoInfoService.Field.FocalLength => Strings.ExifFocalLength,
        PhotoInfoService.Field.FocalLength35mm => Strings.ExifFocalLength35mm,
        PhotoInfoService.Field.Program => Strings.ExifProgram,
        PhotoInfoService.Field.Location => Strings.ExifLocation,
        _ => string.Empty,
    };

    // ----- 속성 제거 ------------------------------------------------------------

    private void ViewerExifRemoveToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_viewerExifRemoving)
        {
            ExitViewerExifRemoval();
        }
        else
        {
            EnterViewerExifRemoval();
        }
    }

    private void EnterViewerExifRemoval()
    {
        if (!CanRemoveViewerExif)
        {
            ViewerExifRemoveToggle.IsChecked = false;
            return;
        }

        // The view comes with it, and stays when the mode ends.
        if (!_settings.ViewerExif)
        {
            _settings.ViewerExif = true;
            _settingsService.Save(_settings);
        }
        UpdateViewerExif();

        _viewerExifRemoving = true;
        foreach (var check in ViewerExifAllChecks)
        {
            check.IsChecked = false;
        }
        ApplyViewerExifRemoval();
    }

    // Every way out. Safe when the mode is already off.
    private void ExitViewerExifRemoval()
    {
        ViewerExifRemoveToggle.IsChecked = false;
        if (!_viewerExifRemoving)
        {
            return;
        }

        _viewerExifRemoving = false;
        ApplyViewerExifRemoval();
    }

    private IEnumerable<CheckBox> ViewerExifAllChecks
        => (_viewerExifFieldChecks ?? []).Append(_viewerExifOtherCheck).OfType<CheckBox>();

    // The boxes, the sixteenth row and the save row, as the mode and the
    // file's values say. A row with no value keeps a box, greyed: with no box
    // at all (the first version) the column of boxes had gaps in it and read
    // as confusing (on report, 2026-11-05).
    private void ApplyViewerExifRemoval()
    {
        ViewerExifRemoveToggle.IsChecked = _viewerExifRemoving;
        if (_viewerExifFieldChecks is null || _viewerExifOtherCheck is null || _viewerExifOtherCell is null)
        {
            return;
        }

        for (int i = 0; i < ViewerExifFields.Length; i++)
        {
            bool present = _viewerExifPresent.Contains(ViewerExifFields[i]);
            var check = _viewerExifFieldChecks[i];
            check.Visibility = _viewerExifRemoving ? Visibility.Visible : Visibility.Collapsed;
            check.IsEnabled = present;
            if (!_viewerExifRemoving || !present)
            {
                check.IsChecked = false;
            }
        }

        _viewerExifOtherCheck.Visibility = _viewerExifRemoving ? Visibility.Visible : Visibility.Collapsed;
        _viewerExifOtherCell.Visibility = _viewerExifRemoving ? Visibility.Visible : Visibility.Collapsed;
        if (!_viewerExifRemoving)
        {
            _viewerExifOtherCheck.IsChecked = false;
        }

        ViewerExifRemoveRow.Visibility = _viewerExifRemoving ? Visibility.Visible : Visibility.Collapsed;
        UpdateViewerExifRemoveState();
        UpdateViewerExifLayout();
    }

    private void ViewerExifCheck_Click(object sender, RoutedEventArgs e) => UpdateViewerExifRemoveState();

    private void ViewerExifSelectAll_Click(object sender, RoutedEventArgs e)
    {
        bool on = ViewerExifSelectAll.IsChecked == true;
        foreach (var check in ViewerExifAllChecks)
        {
            if (check.Visibility == Visibility.Visible && check.IsEnabled)
            {
                check.IsChecked = on;
            }
        }
        UpdateViewerExifRemoveState();
    }

    // 전체 선택 follows the boxes that can be ticked; the save wants at least
    // one.
    private void UpdateViewerExifRemoveState()
    {
        var shown = ViewerExifAllChecks.Where(c => c.Visibility == Visibility.Visible && c.IsEnabled).ToList();
        bool any = shown.Any(c => c.IsChecked == true);
        ViewerExifSelectAll.IsChecked = shown.Count > 0 && shown.All(c => c.IsChecked == true);
        ViewerExifRemoveSave.IsEnabled = _viewerExifRemoving && any && !_viewerExifBusy;
    }

    private bool HandleViewerExifKey(KeyEventArgs e)
    {
        if (_viewerExifRemoving && e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None &&
            !KeyboardInTextField)
        {
            ExitViewerExifRemoval();
            return true;
        }
        return false;
    }

    private void ViewerExifRemoveSave_Click(object sender, RoutedEventArgs e) => SaveViewerExifRemoval();

    private async void SaveViewerExifRemoval()
    {
        if (_viewerExifBusy || !_viewerExifRemoving || _viewerExifFieldChecks is null ||
            _pendingViewerPath is not { } path || Path.GetDirectoryName(path) is not { Length: > 0 } folder)
        {
            return;
        }

        var fields = ViewerExifFields.Where((field, i) => _viewerExifFieldChecks[i].IsChecked == true).ToList();
        bool other = _viewerExifOtherCheck?.IsChecked == true;
        // EVERY ROW SHOWN, AND 그 밖의 정보: the whole lot - all fifteen asked
        // for, since a file can hold a row it shows nothing for (see
        // JpegMetadataRemover's note on a Canon's GPS directory).
        if (other && _viewerExifPresent.All(fields.Contains))
        {
            fields = [.. ViewerExifFields];
        }

        string target = FileOperationService.GetUniqueDestination(
            Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + "-clean" + Path.GetExtension(path).ToLowerInvariant()));

        (bool Saved, string? Error) outcome;
        _viewerExifBusy = true;
        UpdateViewerExifRemoveState();
        UpdateViewerExif();
        try
        {
            outcome = await Task.Run(() => WriteViewerExifRemoved(path, target, fields, other));
        }
        finally
        {
            _viewerExifBusy = false;
        }

        if (!outcome.Saved)
        {
            UpdateViewerExif();
            UpdateViewerExifRemoveState();
            MessageBox.Show(this, outcome.Error ?? string.Empty, Strings.ExifRemoveFailedTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ExitViewerExifRemoval();
        UpdateViewerExif();

        string? savedFolder = Path.GetDirectoryName(target);
        if (ViewerItem?.Parent is { IsDirectory: true } parent &&
            string.Equals(parent.FullPath, savedFolder, StringComparison.OrdinalIgnoreCase))
        {
            RefreshFolderPreservingState(parent);
        }
    }

    // A worker. The copy is written as the crop's save writes - a hidden file
    // beside the target, moved into place - under the target's own extension,
    // so Windows reads it with the same property handler the panel did.
    //
    // SAFETY DEVICE: the copy is READ BACK before it is kept. It must open at
    // the original's size and orientation, and the panel's own reading
    // (PhotoInfoService) must find none of the removed rows in it - which is
    // the one test that also catches a value kept somewhere this did not
    // know to look, Windows reading it from there. A copy that fails is not
    // kept, and the message says why. What it hides: nothing that is saved;
    // a refused copy is the only thing it can cause.
    private static (bool Saved, string? Error) WriteViewerExifRemoved(
        string source, string target, IReadOnlyList<PhotoInfoService.Field> fields, bool other)
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

        if (JpegMetadataRemover.Remove(original, fields, other) is not { } copy)
        {
            return (false, Strings.ExifRemoveUnreadable);
        }

        string folder = Path.GetDirectoryName(target) ?? string.Empty;
        string temp = FileOperationService.GetUniqueDestination(Path.Combine(folder, "~" + Path.GetFileName(target)));
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                File.SetAttributes(temp, FileAttributes.Hidden | FileAttributes.Temporary);
                stream.Write(copy);
            }

            if (!ReadsAsCleaned(temp, original, fields))
            {
                File.Delete(temp);
                return (false, Strings.ExifRemoveNotGone);
            }

            File.SetAttributes(temp, FileAttributes.Normal);
            File.Move(temp, target, overwrite: false);
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

    private static bool ReadsAsCleaned(string path, byte[] original, IReadOnlyList<PhotoInfoService.Field> removed)
    {
        try
        {
            static System.Windows.Media.Imaging.BitmapFrame Header(Stream stream)
                => System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                    System.Windows.Media.Imaging.BitmapCacheOption.None).Frames[0];

            using (var before = new MemoryStream(original, writable: false))
            using (var after = File.OpenRead(path))
            {
                var was = Header(before);
                var now = Header(after);
                if (now.PixelWidth != was.PixelWidth || now.PixelHeight != was.PixelHeight ||
                    ReadExifOrientation(now) != ReadExifOrientation(was))
                {
                    return false;
                }
            }

            return !PhotoInfoService.Read(path).Any(f => removed.Contains(f.Field));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or FileFormatException or ArgumentException or InvalidOperationException
                                       or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    // ----- Layout --------------------------------------------------------------

    private void ViewerExifHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            UpdateViewerExifLayout();
        }
    }

    // The panel's height moves the cap.
    private void ViewerPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.HeightChanged && ViewerExifHost.Visibility == Visibility.Visible)
        {
            UpdateViewerExifLayout();
        }
    }

    // Columns from the width, the slot's height from the columns. A cell needs
    // room for the longest label and a value of some length; twenty times the
    // text size is about 240 at the default size - "35mm 초점 거리" and a
    // camera model side by side.
    private void UpdateViewerExifLayout()
    {
        if (ViewerExifHost.Visibility != Visibility.Visible)
        {
            return;
        }

        // An application resource (it follows Ctrl+/- outside the window too).
        double fontSize = TryFindResource("MenuThumbnailInfoFontSize") is double size ? size : 11;
        double width = ViewerExifHost.ActualWidth - ViewerExifHost.Padding.Left - ViewerExifHost.Padding.Right;
        int columns = width > 0 ? Math.Clamp((int)(width / (fontSize * 20)), 1, 3) : 1;
        int cells = ViewerExifFields.Length + (_viewerExifRemoving ? 1 : 0);
        int rows = (cells + columns - 1) / columns;
        if (ViewerExifGrid.Columns != columns)
        {
            ViewerExifGrid.Columns = columns;
        }
        if (ViewerExifGrid.Rows != rows)
        {
            ViewerExifGrid.Rows = rows;
        }

        // A line of this text and the cell's 1px above and below.
        double row = Math.Ceiling(fontSize * ViewerFileInfo.FontFamily.LineSpacing) + 2;
        double cap = ViewerPanel.ActualHeight > 0 ? Math.Floor(ViewerPanel.ActualHeight / 3) : double.PositiveInfinity;
        double height = Math.Min(rows * row, cap);
        if (double.IsNaN(ViewerExifScroll.Height) || Math.Abs(ViewerExifScroll.Height - height) > 0.5)
        {
            ViewerExifScroll.Height = height;
        }
    }
}
