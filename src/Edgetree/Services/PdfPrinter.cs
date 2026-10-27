using System.ComponentModel;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FormsDialogResult = System.Windows.Forms.DialogResult;
using FormsPrintDialog = System.Windows.Forms.PrintDialog;
using IWin32Window = System.Windows.Forms.IWin32Window;

namespace SidebarExplorer.App.Services;

// Printing a PDF (2026-10-27, on report: the PDF view had no 인쇄).
//
// THE APP'S PREVIEW, THEN WINDOWS' PRINT DIALOG, THEN PDFIUM ONTO THE
// PRINTER. The preview (PrintPreviewWindow with a PdfPrintPreviewColumn) was
// added the same day on request: at first a PDF went straight to the dialog,
// its pages being on screen in the panel already, but the panel cannot show
// how they land on paper - shrunk to the margins, turned. Windows 11's own
// print window has a preview pane, but only for an app printing through
// WinRT's PrintManager, which would cost the WinRT projection and turn the
// pages into pictures; this preview is the app's instead, on the default
// printer's paper (DefaultPaper). The dialog brings the page range.
//
// NO 현재 페이지. It was offered at first (the page in view), and on a Windows
// 11 that answers PrintDlgEx with its modern print dialog it broke single
// pages: that dialog has no 현재 페이지 of its own (모든 페이지 and 사용자 지정
// 범위 only), yet while the app allows one it reports a ONE-PAGE custom range
// as CurrentPage - "1" came back as CurrentPage with From = To = 1, and the
// app printed the page in view instead (2026-10-27: page 2 came out for "1",
// twice). Measured by driving the dialog with UI Automation (scratchpad
// dlgprobe): with current page allowed, "1", "2" and "2-2" all arrived as
// CurrentPage, "1-2" as SomePages; with it disallowed every range arrived
// as SomePages with the right pages, and a "2" printed through Microsoft
// Print to PDF came out as page 2 alone - the dialog does not filter pages a
// second time on its own side. A CurrentPage that still arrives is read as
// the range it carries.
//
// GDI, NOT WPF'S PRINT PATH. WPF prints visuals, which for a PDF means a
// bitmap per page: 35MB for an A4 page at 300 dpi, and pictures of text. A
// System.Drawing PrintDocument hands over the printer's device context, and
// PDFium draws on that itself (PdfRenderService.PrintPage) at the printer's
// resolution, the way Chrome prints a PDF on Windows. The WinForms dialog
// with UseEXDialog is the same Windows dialog WPF's PrintDialog shows.
//
// SPOOLED ON A THREAD OF ITS OWN, so the window keeps answering through a
// long document. A foreground thread: an exit mid-job finishes the job
// rather than leaving half of it in the queue. No progress window - Windows'
// own printer notification says the job went.
internal static class PdfPrinter
{
    // onFailure gets a message on the UI thread. True once the job is handed
    // to the print thread; false when the dialog was cancelled or failed.
    public static bool Print(Window owner, PdfRenderService.Document document, string fileName,
        Action<string> onFailure)
    {
        if (!PdfRenderService.Hold(document))
        {
            return false;
        }

        bool handedOver = false;
        var printDocument = new PrintDocument
        {
            DocumentName = fileName,
            // No status window: the WinForms one is a bare dialog of its own,
            // and this app keeps quiet while it works.
            PrintController = new StandardPrintController(),
        };
        try
        {
            int count = document.PageCount;
            var settings = printDocument.PrinterSettings;
            settings.MinimumPage = 1;
            settings.MaximumPage = count;
            settings.FromPage = 1;
            settings.ToPage = count;

            using var dialog = new FormsPrintDialog
            {
                Document = printDocument,
                UseEXDialog = true,
                AllowSomePages = true,
                AllowCurrentPage = false,
                AllowSelection = false,
            };
            if (dialog.ShowDialog(new OwnerWindow(owner)) != FormsDialogResult.OK)
            {
                return false;
            }

            int[] pages = settings.PrintRange switch
            {
                PrintRange.SomePages or PrintRange.CurrentPage => Enumerable
                    .Range(Math.Max(1, settings.FromPage), Math.Max(0, Math.Min(count, settings.ToPage) - Math.Max(1, settings.FromPage) + 1))
                    .Select(page => page - 1)
                    .ToArray(),
                _ => Enumerable.Range(0, count).ToArray(),
            };
            if (pages.Length == 0)
            {
                return false;
            }

            int next = 0;
            // Again from the start for every pass: a driver that cannot make
            // copies itself has the document printed once per copy.
            printDocument.BeginPrint += (_, _) => next = 0;
            printDocument.PrintPage += (_, e) =>
            {
                if (e.Graphics is { } graphics)
                {
                    IntPtr hdc = graphics.GetHdc();
                    try
                    {
                        PdfRenderService.PrintPage(document, pages[next], hdc);
                    }
                    finally
                    {
                        graphics.ReleaseHdc(hdc);
                    }
                }
                next++;
                e.HasMorePages = next < pages.Length;
            };

            var dispatcher = owner.Dispatcher;
            var thread = new Thread(() =>
            {
                try
                {
                    printDocument.Print();
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidPrinterException
                                               or InvalidOperationException or ExternalException)
                {
                    dispatcher.BeginInvoke(() => onFailure(ex.Message));
                }
                finally
                {
                    printDocument.Dispose();
                    PdfRenderService.Release(document);
                }
            })
            {
                Name = "PDF print",
                IsBackground = false,
            };
            thread.Start();
            handedOver = true;
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidPrinterException or InvalidOperationException)
        {
            // No printer installed, or the dialog could not be shown.
            onFailure(ex.Message);
            return false;
        }
        finally
        {
            if (!handedOver)
            {
                printDocument.Dispose();
                PdfRenderService.Release(document);
            }
        }
    }

    // The default printer's paper and printable area, in hundredths of an
    // inch, for the preview: the page the print meets before the dialog
    // changes anything - another printer or paper chosen there can differ,
    // as with the text preview. A4 with no margins where there is no
    // printer to ask.
    public static (System.Windows.Size Paper, Rect Printable) DefaultPaper()
    {
        try
        {
            var page = new PrinterSettings().DefaultPageSettings;
            var bounds = page.Bounds;
            var area = page.PrintableArea;
            if (bounds.Width > 0 && bounds.Height > 0 && area.Width > 0 && area.Height > 0)
            {
                return (new System.Windows.Size(bounds.Width, bounds.Height),
                    new Rect(area.X, area.Y, area.Width, area.Height));
            }
        }
        catch (Exception ex) when (ex is InvalidPrinterException or Win32Exception)
        {
            System.Diagnostics.Debug.WriteLine($"default paper: {ex.Message}");
        }
        return (new System.Windows.Size(827, 1169), new Rect(0, 0, 827, 1169));
    }

    // The WinForms dialog's owner, so it opens over the app and is modal to it.
    private sealed class OwnerWindow(Window window) : IWin32Window
    {
        public IntPtr Handle { get; } = new WindowInteropHelper(window).Handle;
    }
}
