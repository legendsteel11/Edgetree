using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using SidebarExplorer.App.Models;
using SidebarExplorer.App.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace SidebarExplorer.App;

// The text view's print preview (2026-10-13, on request): the pages as they
// will print, then 인쇄… for Windows' print dialog, or 닫기. Every way into
// printing a text comes here first - the view's menu row and bar button,
// Ctrl+P, the tree row's 인쇄. Pictures do not: Windows' photo printing
// window has a preview of its own.
//
// Holds no AvalonEdit type: the pages arrive as a FixedDocument
// (ViewerTextEditor.BuildPreview), and printing is a callback, so opening
// this window costs nothing the text view has not already loaded.
public partial class PrintPreviewWindow : Window
{
    // Shows the print dialog and prints; true once the pages went out, false
    // when the dialog was cancelled or the print failed (which the callback
    // has already said).
    private readonly Func<bool> _print;
    private readonly AppSettings _settings;
    private readonly Action _onSave;

    public PrintPreviewWindow(
        FixedDocument document, string fileName, Func<bool> print,
        AppSettings settings, Action onSave)
    {
        InitializeComponent();
        _print = print;
        _settings = settings;
        _onSave = onSave;

        TitleText.Text = $"{Strings.PrintPreviewTitle} · {fileName}";
        // "page / pages" for the page in view, following the scroll - the
        // footer's own form (2026-10-13, on request: a bare "3쪽" read as a
        // count with nothing to say which page was showing).
        //
        // THE PAGE AT THE MIDDLE OF THE VIEW, not DocumentViewer's
        // MasterPageNumber. That one is the page at the top, and a window a
        // little taller than a page never lets the last page reach the top:
        // scrolled to the end, the one before still showed its bottom edge,
        // and the count stopped at 2 / 3 with page 3 filling the view (seen
        // the same day). Every page here is the same size, so the page under
        // the middle line falls out of the scroll position and the extent.
        Viewer.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => ShowPageInView()));

        // THE SIZE IT WAS LEFT AT, or else ONE PAGE FITTED TO THE WIDTH
        // (2026-10-13, on report). It first opened at 88% of the work area,
        // which on a tall screen stood far taller than a page - the page sat
        // in the middle of empty space - and came back that tall however it
        // had been resized. Now the first size is a page's own height at the
        // default width, plus the bars (estimated from the resources, so it
        // can be a few pixels out either way), and a resize is kept as
        // HelpWindow keeps its own.
        var work = SystemParameters.WorkArea;
        if (_settings.PrintPreviewWidth > 0 && _settings.PrintPreviewHeight > 0)
        {
            Width = Math.Min(_settings.PrintPreviewWidth, work.Width);
            Height = Math.Min(_settings.PrintPreviewHeight, work.Height);
        }
        else
        {
            Width = Math.Min(640, Math.Round(work.Width * 0.5));
            var page = document.DocumentPaginator.PageSize;
            double aspect = page.Width > 0 ? page.Height / page.Width : Math.Sqrt(2);
            double titleBar = TryFindResource("DialogTitleBarHeight") is double title ? title : 30;
            // The bottom bar: its padding, a button, and the line above it.
            double bottomBar = 20 + Math.Round((TryFindResource("DialogFontSize") is double font ? font : 13) * 1.4) + 12 + 2 + 1;
            Height = Math.Min(
                Math.Round(titleBar + bottomBar + 2 + (Width - 2) * aspect),
                Math.Round(work.Height * 0.88));
        }

        Viewer.Document = document;
        // Fitted once the viewer has a width to fit to, and again whenever the
        // width changes - fitting is a zoom DocumentViewer applies once, not a
        // mode it keeps.
        Loaded += (_, _) =>
        {
            Viewer.FitToWidth();
            Viewer.Focus();
            ShowPageInView();
        };
        SizeChanged += (_, e) =>
        {
            if (IsLoaded && e.WidthChanged)
            {
                Viewer.FitToWidth();
            }
        };
    }

    // On the way out, as HelpWindow does: RestoreBounds when maximized, so the
    // size kept is the one it comes back at.
    protected override void OnClosed(EventArgs e)
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, ActualWidth, ActualHeight)
            : RestoreBounds;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _settings.PrintPreviewWidth = Math.Round(bounds.Width);
            _settings.PrintPreviewHeight = Math.Round(bounds.Height);
            _onSave();
        }

        base.OnClosed(e);
    }

    private void ShowPageInView()
    {
        int pages = Viewer.PageCount;
        int page = 1;
        if (pages > 1 && Viewer.ExtentHeight > 0)
        {
            double middle = Viewer.VerticalOffset + Viewer.ViewportHeight / 2;
            page = Math.Clamp((int)(middle / (Viewer.ExtentHeight / pages)) + 1, 1, pages);
        }
        PageCountText.Text = string.Format(
            System.Globalization.CultureInfo.CurrentCulture, Strings.PrintPreviewPages, page, Math.Max(pages, 1));
    }

    private void PrintNow()
    {
        if (_print())
        {
            Close();
        }
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e) => PrintNow();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void HeaderGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    // Ctrl+P is ours before it is DocumentViewer's: its own binding prints the
    // preview's pages straight to a dialog of its own, on the preview's paper,
    // without our header and range handling.
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
        {
            PrintNow();
            e.Handled = true;
        }
    }
}
