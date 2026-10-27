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
// WINDOWS' PRINT DIALOG, THEN PDFIUM ONTO THE PRINTER. No preview window of
// our own, unlike a text: a text's pages exist only once it is laid out on
// paper, so its preview is the one place they can be seen, while a PDF's
// pages are already on screen in the panel. The dialog brings the page range
// and 현재 페이지 - the page in view.
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
    // currentPage: the page in view, 0-based, or -1 where there is none (a
    // file printed from the tree without being on screen). onFailure gets a
    // message on the UI thread.
    public static void Print(Window owner, PdfRenderService.Document document, string fileName,
        int currentPage, Action<string> onFailure)
    {
        if (!PdfRenderService.Hold(document))
        {
            return;
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
                AllowCurrentPage = currentPage >= 0,
                AllowSelection = false,
            };
            if (dialog.ShowDialog(new OwnerWindow(owner)) != FormsDialogResult.OK)
            {
                return;
            }

            int[] pages = settings.PrintRange switch
            {
                PrintRange.CurrentPage when currentPage >= 0 => new[] { currentPage },
                PrintRange.SomePages => Enumerable
                    .Range(Math.Max(1, settings.FromPage), Math.Max(0, Math.Min(count, settings.ToPage) - Math.Max(1, settings.FromPage) + 1))
                    .Select(page => page - 1)
                    .ToArray(),
                _ => Enumerable.Range(0, count).ToArray(),
            };
            if (pages.Length == 0)
            {
                return;
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
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidPrinterException or InvalidOperationException)
        {
            // No printer installed, or the dialog could not be shown.
            onFailure(ex.Message);
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

    // The WinForms dialog's owner, so it opens over the app and is modal to it.
    private sealed class OwnerWindow(Window window) : IWin32Window
    {
        public IntPtr Handle { get; } = new WindowInteropHelper(window).Handle;
    }
}
