using System.IO;
using System.Windows;
using System.Windows.Controls;
using SidebarExplorer.App.Services;

namespace SidebarExplorer.App;

// ===== The panel's EXIF view (2026-11-05, the author's design) =====
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
public partial class MainWindow
{
    // The file the slot is showing, or being read for; null while it is shut,
    // so showing it again reads again.
    private string? _viewerExifPath;
    private int _viewerExifTicket;

    private static readonly PhotoInfoService.Field[] ViewerExifFields = Enum.GetValues<PhotoInfoService.Field>();

    // Built once, the first time the view is shown: one cell per field, in
    // the fields' order.
    private TextBlock[]? _viewerExifValues;

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

        if (!ViewerExifWanted || _pendingViewerPath is not { } path)
        {
            if (ViewerExifHost.Visibility != Visibility.Collapsed)
            {
                ViewerExifHost.Visibility = Visibility.Collapsed;
            }
            _viewerExifPath = null;
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

        // Another file: the last file's values go at once rather than
        // standing under this one's name until its own arrive.
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
        for (int i = 0; i < ViewerExifFields.Length; i++)
        {
            string value = values.TryGetValue(ViewerExifFields[i], out var text) ? text : string.Empty;
            _viewerExifValues![i].Text = value;
            _viewerExifValues[i].ToolTip = value.Length > 0 ? value : null;
        }
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
        for (int i = 0; i < ViewerExifFields.Length; i++)
        {
            var (cell, value) = ViewerExifCell(ViewerExifLabel(ViewerExifFields[i]));
            _viewerExifValues[i] = value;
            ViewerExifGrid.Children.Add(cell);
        }
    }

    private static (FrameworkElement Cell, TextBlock Value) ViewerExifCell(string label)
    {
        var cell = new Grid { Margin = new Thickness(0, 1, 12, 1) };
        cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ViewerExifLabel" });
        cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

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

        Grid.SetColumn(value, 1);
        cell.Children.Add(name);
        cell.Children.Add(value);
        return (cell, value);
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
        int rows = (ViewerExifFields.Length + columns - 1) / columns;
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
