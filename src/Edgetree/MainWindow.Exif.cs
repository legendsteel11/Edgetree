using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SidebarExplorer.App.Models;
using SidebarExplorer.App.Services;
// WinForms is a global using in this project (the tray icon), and it has a
// CheckBox, a MessageBox and a KeyEventArgs of its own.
using CheckBox = System.Windows.Controls.CheckBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace SidebarExplorer.App;

// ===== The panel's EXIF view and 메타데이터 제거 (2026-11-05, the author's design) =====
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
// 메타데이터 제거 IS A MODE OF THIS VIEW (on request: a box before each row rather
// than a window of its own). The picture bar's eraser turns it on - the view
// with it - and Esc, the eraser again, another file or the view going end it.
// While it is on, each row with a value gets a box, 그 밖의 정보 appears as a
// sixteenth row for what the view does not show, and a row under them holds
// 전체 선택 and the save. The ticked rows are left out of a COPY beside the
// original, name-metadataremoved, which is written the way the crop's save writes and
// read back before it is kept (see WriteViewerMetadataRemoved); the picture's
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

    private bool _viewerMetadataRemoving;
    // A copy being written, from the press until it is kept or refused.
    private bool _viewerMetadataBusy;

    private static readonly PhotoInfoService.Field[] ViewerExifFields = Enum.GetValues<PhotoInfoService.Field>();

    // Built once, the first time the view is shown: one cell per field, in
    // the fields' order, and 그 밖의 정보 after them.
    private TextBlock[]? _viewerExifValues;
    private CheckBox[]? _viewerExifFieldChecks;
    private CheckBox? _viewerExifOtherCheck;
    private FrameworkElement? _viewerExifOtherCell;
    private TextBlock? _viewerExifOtherValue;

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
    private bool CanRemoveViewerMetadata
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

        bool canRemove = CanRemoveViewerMetadata;
        ViewerMetadataRemoveToggle.IsEnabled = canRemove && !_viewerMetadataBusy;
        ViewerMetadataRemoveToggle.ToolTip = canRemove ? Strings.ViewerMetadataRemove : Strings.ViewerMetadataRemoveUnavailable;
        if (!canRemove)
        {
            ExitViewerMetadataRemoval();
        }

        if (!ViewerExifWanted || _pendingViewerPath is not { } path)
        {
            if (ViewerExifHost.Visibility != Visibility.Collapsed)
            {
                ViewerExifHost.Visibility = Visibility.Collapsed;
            }
            _viewerExifPath = null;
            ExitViewerMetadataRemoval();
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
        ExitViewerMetadataRemoval();
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

        ApplyViewerMetadataRemoval();
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

        // Its value is what the row stands for, and only where the file has
        // some of it (ApplyViewerMetadataRemoval).
        var (otherCell, otherCheck, otherValue) = ViewerExifCell(Strings.ExifOther);
        _viewerExifOtherValue = otherValue;
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

    // ----- 메타데이터 제거 ------------------------------------------------------

    private void ViewerMetadataRemoveToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_viewerMetadataRemoving)
        {
            ExitViewerMetadataRemoval();
        }
        else
        {
            EnterViewerMetadataRemoval();
        }
    }

    private void EnterViewerMetadataRemoval()
    {
        if (!CanRemoveViewerMetadata)
        {
            ViewerMetadataRemoveToggle.IsChecked = false;
            return;
        }

        // The view comes with it, and stays when the mode ends.
        if (!_settings.ViewerExif)
        {
            _settings.ViewerExif = true;
            _settingsService.Save(_settings);
        }
        UpdateViewerExif();

        _viewerMetadataRemoving = true;
        foreach (var check in ViewerExifAllChecks)
        {
            check.IsChecked = false;
        }
        ApplyViewerMetadataRemoval();
        CheckViewerOtherMetadata();
    }

    // Every way out. Safe when the mode is already off.
    private void ExitViewerMetadataRemoval()
    {
        ViewerMetadataRemoveToggle.IsChecked = false;
        if (!_viewerMetadataRemoving)
        {
            return;
        }

        _viewerMetadataRemoving = false;
        _viewerOtherMetadataPresent = false;
        ++_viewerOtherMetadataTicket;
        ApplyViewerMetadataRemoval();
    }

    // WHETHER 그 밖의 정보 HOLDS ANYTHING, asked of the remover itself: the
    // file taken through it with that row alone ticked, and compared byte for
    // byte. The fifteen rows know from what Windows reads; this row is what
    // Windows does not read, so nothing else can say. Its box was live on
    // every file at first, and a copy already stripped of everything offered
    // it again (on report, 2026-11-05). Greyed until the answer is in - the
    // whole file is read, which on a share is a moment.
    private bool _viewerOtherMetadataPresent;
    private int _viewerOtherMetadataTicket;

    private void CheckViewerOtherMetadata()
    {
        _viewerOtherMetadataPresent = false;
        int ticket = ++_viewerOtherMetadataTicket;
        if (_pendingViewerPath is not { } path)
        {
            return;
        }

        Task.Run(() =>
        {
            bool present;
            try
            {
                byte[] original = File.ReadAllBytes(path);
                present = JpegMetadataRemover.Remove(original, [], other: true) is { } copy &&
                          !copy.AsSpan().SequenceEqual(original);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException)
            {
                present = false;
            }

            Dispatcher.BeginInvoke(() =>
            {
                if (ticket == _viewerOtherMetadataTicket && _viewerMetadataRemoving &&
                    string.Equals(_pendingViewerPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    _viewerOtherMetadataPresent = present;
                    ApplyViewerMetadataRemoval();
                }
            });
        });
    }

    private IEnumerable<CheckBox> ViewerExifAllChecks
        => (_viewerExifFieldChecks ?? []).Append(_viewerExifOtherCheck).OfType<CheckBox>();

    // The boxes, the sixteenth row and the save row, as the mode and the
    // file's values say. A row with no value keeps a box, greyed: with no box
    // at all (the first version) the column of boxes had gaps in it and read
    // as confusing (on report, 2026-11-05).
    private void ApplyViewerMetadataRemoval()
    {
        ViewerMetadataRemoveToggle.IsChecked = _viewerMetadataRemoving;
        if (_viewerExifFieldChecks is null || _viewerExifOtherCheck is null || _viewerExifOtherCell is null)
        {
            return;
        }

        for (int i = 0; i < ViewerExifFields.Length; i++)
        {
            bool present = _viewerExifPresent.Contains(ViewerExifFields[i]);
            var check = _viewerExifFieldChecks[i];
            check.Visibility = _viewerMetadataRemoving ? Visibility.Visible : Visibility.Collapsed;
            check.IsEnabled = present;
            if (!_viewerMetadataRemoving || !present)
            {
                check.IsChecked = false;
            }
        }

        _viewerExifOtherCheck.Visibility = _viewerMetadataRemoving ? Visibility.Visible : Visibility.Collapsed;
        _viewerExifOtherCell.Visibility = _viewerMetadataRemoving ? Visibility.Visible : Visibility.Collapsed;
        _viewerExifOtherCheck.IsEnabled = _viewerOtherMetadataPresent;
        if (!_viewerMetadataRemoving || !_viewerOtherMetadataPresent)
        {
            _viewerExifOtherCheck.IsChecked = false;
        }

        // Empty where there is nothing of it, as any row without a value is -
        // a copy already stripped showed the list of what it no longer had
        // (on report, 2026-11-05). Empty while the answer is being found too.
        if (_viewerExifOtherValue is { } otherValue)
        {
            otherValue.Text = _viewerOtherMetadataPresent ? Strings.ExifOtherDetail : string.Empty;
            otherValue.ToolTip = _viewerOtherMetadataPresent ? Strings.ExifOtherDetail : null;
        }

        ViewerMetadataRemoveRow.Visibility = _viewerMetadataRemoving ? Visibility.Visible : Visibility.Collapsed;
        UpdateViewerMetadataRemoveState();
        UpdateViewerExifLayout();
    }

    private void ViewerExifCheck_Click(object sender, RoutedEventArgs e) => UpdateViewerMetadataRemoveState();

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
        UpdateViewerMetadataRemoveState();
    }

    // 전체 선택 follows the boxes that can be ticked; the save wants at least
    // one.
    private void UpdateViewerMetadataRemoveState()
    {
        var shown = ViewerExifAllChecks.Where(c => c.Visibility == Visibility.Visible && c.IsEnabled).ToList();
        bool any = shown.Any(c => c.IsChecked == true);
        ViewerExifSelectAll.IsChecked = shown.Count > 0 && shown.All(c => c.IsChecked == true);
        // Nothing to tick, nothing to select (on report, 2026-11-05).
        ViewerExifSelectAll.IsEnabled = shown.Count > 0 && !_viewerMetadataBusy;
        ViewerMetadataRemoveSave.IsEnabled = _viewerMetadataRemoving && any && !_viewerMetadataBusy;
    }

    private bool HandleViewerMetadataKey(KeyEventArgs e)
    {
        if (_viewerMetadataRemoving && e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None &&
            !KeyboardInTextField)
        {
            ExitViewerMetadataRemoval();
            return true;
        }
        return false;
    }

    private void ViewerMetadataRemoveSave_Click(object sender, RoutedEventArgs e) => SaveViewerMetadataRemoval();

    private async void SaveViewerMetadataRemoval()
    {
        if (_viewerMetadataBusy || !_viewerMetadataRemoving || _viewerExifFieldChecks is null ||
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

        string target = ViewerMetadataCopyTarget(path, folder);

        (MetadataRemoval Outcome, string? Error) result;
        _viewerMetadataBusy = true;
        UpdateViewerMetadataRemoveState();
        UpdateViewerExif();
        try
        {
            result = await Task.Run(() => WriteViewerMetadataRemoved(path, target, fields, other));
        }
        finally
        {
            _viewerMetadataBusy = false;
        }

        if (result.Outcome == MetadataRemoval.Failed)
        {
            UpdateViewerExif();
            UpdateViewerMetadataRemoveState();
            MessageBox.Show(this, result.Error ?? string.Empty, Strings.MetadataRemoveFailedTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ExitViewerMetadataRemoval();
        UpdateViewerExif();

        // A copy the same as the file, byte for byte, is not written (see
        // WriteViewerMetadataRemoved) - and the hand is told, since no file
        // appears.
        if (result.Outcome == MetadataRemoval.NothingToRemove)
        {
            MessageBox.Show(this, Strings.MetadataRemoveNothing, Strings.ViewerMetadataRemove,
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string? savedFolder = Path.GetDirectoryName(target);
        if (ViewerItem?.Parent is { IsDirectory: true } parent &&
            string.Equals(parent.FullPath, savedFolder, StringComparison.OrdinalIgnoreCase))
        {
            RefreshFolderPreservingState(parent);
        }
    }

    private enum MetadataRemoval { Saved, NothingToRemove, Failed }

    // Beside the original as name-metadataremoved, numbered " (2)" on as a
    // paste numbers a copy - never over anything. Named for what was done to
    // it, as -cropped and -rotated are (see the crop's save), which stays true
    // of a copy with only some of it gone. "-clean" was first, and read as the
    // picture itself having been cleaned up (on request, 2026-11-05).
    private static string ViewerMetadataCopyTarget(string source, string folder)
        => FileOperationService.GetUniqueDestination(Path.Combine(folder,
            Path.GetFileNameWithoutExtension(source) + "-metadataremoved" + Path.GetExtension(source).ToLowerInvariant()));

    // A worker. The copy is written as the crop's save writes - a hidden file
    // beside the target, moved into place - under the target's own extension,
    // so Windows reads it with the same property handler the panel did.
    //
    // NOTHING TO REMOVE, NOTHING WRITTEN (2026-11-05, agreed before the
    // many-file version): a copy that comes out the same as the file, byte
    // for byte - a wallpaper with no EXIF and 그 밖의 정보 ticked - would be a
    // second file of the same thing under another name. The test is the
    // bytes, not "has EXIF": a file with none can still carry XMP, a comment
    // or something after the picture.
    //
    // SAFETY DEVICE: the copy is READ BACK before it is kept. It must open at
    // the original's size and orientation, and the panel's own reading
    // (PhotoInfoService) must find none of the removed rows in it - which is
    // the one test that also catches a value kept somewhere this did not
    // know to look, Windows reading it from there. A copy that fails is not
    // kept, and the message says why. What it hides: nothing that is saved;
    // a refused copy is the only thing it can cause.
    private static (MetadataRemoval Outcome, string? Error) WriteViewerMetadataRemoved(
        string source, string target, IReadOnlyList<PhotoInfoService.Field> fields, bool other)
    {
        byte[] original;
        try
        {
            original = File.ReadAllBytes(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (MetadataRemoval.Failed, ex.Message);
        }

        if (JpegMetadataRemover.Remove(original, fields, other) is not { } copy)
        {
            return (MetadataRemoval.Failed, Strings.MetadataRemoveUnreadable);
        }

        if (copy.AsSpan().SequenceEqual(original))
        {
            return (MetadataRemoval.NothingToRemove, null);
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

            if (!ReadsWithoutRemoved(temp, original, fields))
            {
                File.Delete(temp);
                return (MetadataRemoval.Failed, Strings.MetadataRemoveNotGone);
            }

            File.SetAttributes(temp, FileAttributes.Normal);
            File.Move(temp, target, overwrite: false);
            return (MetadataRemoval.Saved, null);
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
            return (MetadataRemoval.Failed, ex.Message);
        }
    }

    private static bool ReadsWithoutRemoved(string path, byte[] original, IReadOnlyList<PhotoInfoService.Field> removed)
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

    // ----- 선택 파일 메타데이터 제거, from the row menus --------------------------
    //
    // The panel's mode is one picture; the tree's and the thumbnail cells'
    // menus are many (2026-11-05, agreed with the design). Every JPEG among
    // the rows, with everything removed - the panel's 전체 선택 with 그 밖의
    // 정보 - each into a copy. One file goes beside itself as name-metadataremoved, as
    // the panel's save does. More go into a folder asked for, offered as a new
    // "metadataremoved" folder beside them, the copies under their own names -
    // or name-metadataremoved where the folder chosen is their own. A file with nothing to
    // remove gets no copy, nor does a row that is not a JPEG; one line says
    // so, and nothing is said when every copy was written: the files
    // appearing is the answer, as it is for 압축.
    private bool _viewerMetadataBatchBusy;

    private async void RemoveMetadataItem_Click(object sender, RoutedEventArgs e)
    {
        if (_viewerMetadataBatchBusy)
        {
            return;
        }

        var rows = GetEffectiveSelection();
        var jpegs = rows
            .Where(r => r is { IsPlaceholder: false, IsShowMore: false, IsDirectory: false } &&
                        IsJpegExtension(Path.GetExtension(r.FullPath)))
            .ToList();
        int notJpeg = rows.Count - jpegs.Count;
        if (jpegs.Count == 0)
        {
            return;
        }

        string? folder = null;
        string? created = null;
        if (jpegs.Count > 1)
        {
            string home = MenuRowSubject?.Parent?.FullPath ?? Path.GetDirectoryName(jpegs[0].FullPath) ?? string.Empty;

            // The folder offered is made first, so the dialog can open on it;
            // it goes again, if still empty, when another is chosen or none.
            try
            {
                created = FileOperationService.GetUniqueDestination(Path.Combine(home, "metadataremoved"));
                Directory.CreateDirectory(created);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                created = null;
            }

            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = Strings.MetadataRemoveFolderTitle,
                InitialDirectory = home,
                FolderName = created ?? home,
            };
            bool chosen = dialog.ShowDialog(this) == true;
            folder = chosen ? dialog.FolderName : null;
            if (created is not null && (folder is null || !SamePath(folder, created)))
            {
                DeleteIfEmpty(created);
                created = null;
            }
            if (folder is null)
            {
                return;
            }
        }

        var sources = jpegs.Select(j => j.FullPath).ToList();
        (int Saved, int Nothing, int Failed, string? Error) counts;
        _viewerMetadataBatchBusy = true;
        try
        {
            counts = await Task.Run(() =>
            {
                int saved = 0, nothing = 0, failed = 0;
                string? error = null;
                foreach (string source in sources)
                {
                    string own = Path.GetDirectoryName(source) ?? string.Empty;
                    string target = folder is null || SamePath(folder, own)
                        ? ViewerMetadataCopyTarget(source, own)
                        : FileOperationService.GetUniqueDestination(Path.Combine(folder, Path.GetFileName(source)));
                    var (outcome, message) = WriteViewerMetadataRemoved(source, target, ViewerExifFields, other: true);
                    switch (outcome)
                    {
                        case MetadataRemoval.Saved:
                            saved++;
                            break;
                        case MetadataRemoval.NothingToRemove:
                            nothing++;
                            break;
                        default:
                            failed++;
                            error ??= message;
                            break;
                    }
                }
                return (saved, nothing, failed, error);
            });
        }
        finally
        {
            _viewerMetadataBatchBusy = false;
        }

        if (counts.Saved == 0 && created is not null)
        {
            DeleteIfEmpty(created);
        }

        foreach (var parent in jpegs.Select(j => j.Parent).OfType<FileSystemItem>().Distinct())
        {
            RefreshFolderPreservingState(parent);
        }

        if (counts.Nothing == 0 && counts.Failed == 0 && notJpeg == 0)
        {
            return;
        }

        string text;
        if (jpegs.Count == 1 && notJpeg == 0 && counts.Nothing == 1)
        {
            text = Strings.MetadataRemoveNothing;
        }
        else
        {
            var lines = new List<string>();
            if (counts.Saved > 0)
            {
                lines.Add(string.Format(Strings.MetadataRemoveSavedFormat, counts.Saved));
            }
            if (counts.Nothing > 0)
            {
                lines.Add(string.Format(Strings.MetadataRemoveNothingFormat, counts.Nothing));
            }
            if (notJpeg > 0)
            {
                lines.Add(string.Format(Strings.MetadataRemoveNotJpegFormat, notJpeg));
            }
            if (counts.Failed > 0)
            {
                lines.Add(string.Format(Strings.MetadataRemoveFailedFormat, counts.Failed) +
                          (counts.Error is { Length: > 0 } reason ? " " + reason : string.Empty));
            }
            text = string.Join(" ", lines);
        }

        MessageBox.Show(this, text, Strings.MenuRemoveMetadata, MessageBoxButton.OK,
            counts.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                         Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                         StringComparison.OrdinalIgnoreCase);

    private static void DeleteIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
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
        int cells = ViewerExifFields.Length + (_viewerMetadataRemoving ? 1 : 0);
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
