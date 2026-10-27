using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;
using Size = System.Windows.Size;

namespace SidebarExplorer.App.Services;

// The multimedia panel's PDF pages, drawn by PDFium (2026-10-27).
//
// WHY PDFIUM. WPF has no PDF renderer, and of the four ways to get one the
// other three each cost something this app keeps: Windows.Data.Pdf through
// the WinRT projection is +25.4MB that WPF cannot trim (measured 2026-08-14),
// WebView2 / pdf.js is a browser process per view and an HWND the panel could
// not draw over, and MuPDF is AGPL. PDFium is Chrome's own engine under a
// BSD/Apache licence, and the build used here (bblanchon/pdfium-binaries,
// no V8, so no JavaScript and no XFA forms) is one 7.5MB DLL.
//
// THE DLL RIDES INSIDE THE EXE AND IS WRITTEN OUT ON THE FIRST PDF. The light
// build is a framework-dependent single file published without
// IncludeNativeLibrariesForSelfExtract, so a native package asset would land
// BESIDE the exe and the exe moved on its own would lose PDFs without a word.
// Bundling it with that flag instead would extract it on every first start,
// PDF or not. So it is an embedded resource, copied to %LOCALAPPDATA% the
// first time a PDF is shown - the same "not loaded until asked" rule the text
// view keeps for AvalonEdit. The folder is named by the DLL's own hash, so a
// new PDFium never meets an old copy and two versions never fight over one
// file name.
//
// ONE THREAD OWNS EVERY PDFIUM CALL. PDFium is not thread-safe at all - not
// per document, the whole library - so every call, from init to the last
// close, is made on the one thread below. Callers hand it work and get frozen
// bitmaps back on the UI thread.
//
// THE FILE IS READ THROUGH OUR OWN STREAM, not PDFium's FPDF_LoadDocument:
// opened with ReadWrite | Delete sharing, so a PDF on screen can still be
// renamed, moved or deleted from the tree (the app's own file operations
// would otherwise fail on the panel's file), and PDFium fetches only the
// blocks it needs - the first page of a 200MB scan on a share does not wait
// for the whole file.
public static class PdfRenderService
{
    // Why a document could not be opened - the caption says which.
    public enum OpenError
    {
        None,
        Password,
        Unreadable,
        // The DLL could not be written out or loaded (an ARM64 process, a
        // locked-down profile). Reported once per session in the Debug log.
        EngineUnavailable,
    }

    public sealed class Document
    {
        internal Document(string path) => Path = path;

        public string Path { get; }
        public int PageCount => PageSizes.Length;
        // In PDF points (1/72 inch), as PDFium reports them for drawing - the
        // page's /Rotate already applied, so a landscape page set sideways in
        // a portrait file comes back wide.
        public Size[] PageSizes { get; internal set; } = Array.Empty<Size>();

        // Owned by the render thread from here down.
        internal IntPtr Handle;
        internal FileStream? Stream;
        internal GCHandle StreamHandle;
        internal IntPtr FileAccess;
        internal bool Closed;
    }

    public readonly record struct OpenResult(Document? Document, OpenError Error);

    // ----- the thread --------------------------------------------------------

    private static readonly object Gate = new();
    // Page renders are taken NEWEST FIRST: after a fast scroll the page now in
    // view was asked for last, and the ones scrolled past are still queued
    // ahead of it. Opens and closes go in their own FIFO and always run before
    // any page, so a close is never stuck behind renders of the document it
    // closes.
    private static readonly LinkedList<Action> Control = new();
    private static readonly LinkedList<PageRequest> Pages = new();
    // First pages for the thumbnail bar and the row menu: after the panel's
    // own pages, since those are what is being looked at, and newest first
    // for the same reason the pages are.
    private static readonly LinkedList<Action> Thumbnails = new();
    private static Thread? _thread;

    private sealed class PageRequest
    {
        public required Document Document { get; init; }
        public required int Index { get; init; }
        public required int PixelWidth { get; init; }
        public required Action<BitmapSource?> Done { get; init; }
    }

    private static void Post(Action work)
    {
        lock (Gate)
        {
            Control.AddLast(work);
            EnsureThread();
            Monitor.Pulse(Gate);
        }
    }

    private static void EnsureThread()
    {
        if (_thread is not null)
        {
            return;
        }

        // Background, so it never holds the process open on exit; PDFium needs
        // no orderly shutdown, the process going away is the whole of it.
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "PDFium",
        };
        _thread.Start();
    }

    private static void Run()
    {
        while (true)
        {
            Action? control = null;
            PageRequest? page = null;
            lock (Gate)
            {
                while (Control.Count == 0 && Pages.Count == 0 && Thumbnails.Count == 0)
                {
                    Monitor.Wait(Gate);
                }

                if (Control.First is { } first)
                {
                    control = first.Value;
                    Control.RemoveFirst();
                }
                else if (Pages.Last is { } last)
                {
                    page = last.Value;
                    Pages.RemoveLast();
                }
                else if (Thumbnails.Last is { } thumbnail)
                {
                    control = thumbnail.Value;
                    Thumbnails.RemoveLast();
                }
            }

            try
            {
                if (control is not null)
                {
                    control();
                }
                else if (page is not null)
                {
                    // The answer goes back even when the render failed: the
                    // column keeps a page's request open until it hears, and
                    // an unanswered one would never be asked again.
                    BitmapSource? bitmap = null;
                    try
                    {
                        if (!page.Document.Closed)
                        {
                            bitmap = RenderPage(page.Document, page.Index, page.PixelWidth);
                        }
                    }
                    finally
                    {
                        Application.Current?.Dispatcher.BeginInvoke(() => page.Done(bitmap));
                    }
                }
            }
            catch (Exception ex)
            {
                // A native fault in PDFium does not arrive here - it takes the
                // process, which is what exit.log is for. This is the managed
                // side (a dispatcher gone at shutdown, a stream torn away).
                System.Diagnostics.Debug.WriteLine($"PDFium work failed: {ex}");
            }
        }
    }

    // ----- loading the engine ------------------------------------------------

    private static bool _engineTried;
    private static bool _engineReady;
    private static IntPtr _engineModule;

    // Render thread only.
    private static bool EnsureEngine()
    {
        if (_engineTried)
        {
            return _engineReady;
        }
        _engineTried = true;

        try
        {
            // The embedded DLL is the x64 build; an ARM64 process (a Debug run
            // on an ARM machine - the release assets are x64 and run emulated
            // there) could not load it, and saying so beats a BadImageFormat.
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                return false;
            }

            string path = ExtractEngine();
            _engineModule = NativeLibrary.Load(path);
            NativeLibrary.SetDllImportResolver(typeof(PdfRenderService).Assembly, (name, _, _) =>
                name == Native.Library ? _engineModule : IntPtr.Zero);
            Native.FPDF_InitLibrary();
            _engineReady = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or DllNotFoundException or BadImageFormatException
                                       or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"PDFium unavailable: {ex}");
        }
        return _engineReady;
    }

    private const string ResourceName = "Edgetree.Native.pdfium.dll";

    private static string ExtractEngine()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("pdfium.dll is not embedded");
        var bytes = new byte[resource.Length];
        resource.ReadExactly(bytes);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));

        string folder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Edgetree", "pdfium", hash[..16]);
        string target = System.IO.Path.Combine(folder, "pdfium.dll");

        // An earlier run's copy is used only if it is byte-for-byte this one:
        // a copy cut short by a crash mid-write, or touched by anything else,
        // is written again rather than handed to LoadLibrary.
        if (File.Exists(target))
        {
            try
            {
                using var existing = File.OpenRead(target);
                if (existing.Length == bytes.Length &&
                    Convert.ToHexString(SHA256.HashData(existing)) == hash)
                {
                    return target;
                }
            }
            catch (IOException)
            {
                // Unreadable - fall through and replace it.
            }
        }

        Directory.CreateDirectory(folder);
        // Written beside and moved into place, so a half-written file is never
        // under the name the check above trusts.
        string temp = target + "." + Environment.ProcessId + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, target, overwrite: true);
        return target;
    }

    // ----- documents ---------------------------------------------------------

    // A password is passed through as given and kept nowhere here. PDFium
    // takes it as UTF-8 and retries it as Latin-1 itself when the file's
    // security handler wants the other (fpdfview.h, FPDF_LoadDocument).
    public static Task<OpenResult> OpenAsync(string path, string? password = null)
    {
        var done = new TaskCompletionSource<OpenResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => done.SetResult(Open(path, password)));
        return done.Task;
    }

    // Queued behind whatever is already asked of the document; renders still
    // queued for it are dropped first, so closing a 300-page file does not
    // wait for pages nobody is looking at any more.
    public static void Close(Document document)
    {
        CancelPages(document);
        Post(() => CloseNow(document));
    }

    public static void RequestPage(Document document, int index, int pixelWidth, Action<BitmapSource?> done)
    {
        lock (Gate)
        {
            Pages.AddLast(new PageRequest
            {
                Document = document,
                Index = index,
                PixelWidth = pixelWidth,
                Done = done,
            });
            EnsureThread();
            Monitor.Pulse(Gate);
        }
    }

    // Drops queued renders of one document - the panel moving to another file,
    // or a zoom that makes every queued width wrong. A render already running
    // finishes; its callback is the caller's to ignore.
    public static void CancelPages(Document document)
    {
        lock (Gate)
        {
            for (var node = Pages.First; node is not null;)
            {
                var next = node.Next;
                if (ReferenceEquals(node.Value.Document, document))
                {
                    Pages.Remove(node);
                }
                node = next;
            }
        }
    }

    // A first page fitted inside a square of `pixelSize`, for a caller on a
    // worker thread that can wait (ShellThumbnailService's). Opens and closes
    // its own document, so it never touches the panel's. Null for anything
    // that will not open - a password, a damaged file - which the caller
    // shows as the file-type icon.
    public static BitmapSource? RenderFirstPage(string path, int pixelSize)
    {
        var done = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Gate)
        {
            Thumbnails.AddLast(() =>
            {
                PdfRenderService.Document? document = null;
                try
                {
                    document = Open(path, null).Document;
                    if (document is null)
                    {
                        done.TrySetResult(null);
                        return;
                    }
                    var size = document.PageSizes[0];
                    int width = size.Width >= size.Height
                        ? pixelSize
                        : Math.Max(1, (int)Math.Round(pixelSize * size.Width / size.Height));
                    done.TrySetResult(RenderPage(document, 0, width));
                }
                finally
                {
                    // Whatever happened, the waiting worker is released - a
                    // managed fault here must not hang a thumbnail thread.
                    done.TrySetResult(null);
                    if (document is not null)
                    {
                        CloseNow(document);
                    }
                }
            });
            EnsureThread();
            Monitor.Pulse(Gate);
        }

        try
        {
            return done.Task.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static OpenResult Open(string path, string? password)
    {
        if (!EnsureEngine())
        {
            return new OpenResult(null, OpenError.EngineUnavailable);
        }

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.RandomAccess);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            return new OpenResult(null, OpenError.Unreadable);
        }

        // FPDF_FILEACCESS carries the length as an unsigned long, which is 32
        // bits on Windows. A PDF past 4GB is not one this panel will open.
        if (stream.Length is 0 or > uint.MaxValue)
        {
            stream.Dispose();
            return new OpenResult(null, OpenError.Unreadable);
        }

        var document = new Document(path)
        {
            Stream = stream,
            StreamHandle = GCHandle.Alloc(stream),
        };

        // The struct PDFium keeps a pointer to for the document's whole life,
        // so it lives in native memory rather than anywhere the GC may move.
        document.FileAccess = Marshal.AllocHGlobal(Marshal.SizeOf<Native.FileAccess>());
        Marshal.StructureToPtr(new Native.FileAccess
        {
            FileLength = (uint)stream.Length,
            GetBlock = GetBlockPointer,
            Param = GCHandle.ToIntPtr(document.StreamHandle),
        }, document.FileAccess, fDeleteOld: false);

        document.Handle = Native.FPDF_LoadCustomDocument(document.FileAccess, password);
        if (document.Handle == IntPtr.Zero)
        {
            // Password covers both "one is needed" and "that one is wrong" -
            // PDFium does not tell them apart, and the caller knows whether it
            // gave one. An unsupported security handler (certificate
            // encryption) is NOT a password case: no password would open it.
            uint error = Native.FPDF_GetLastError();
            ReleaseFile(document);
            return new OpenResult(null, error == Native.ErrPassword
                ? OpenError.Password
                : OpenError.Unreadable);
        }

        int count = Native.FPDF_GetPageCount(document.Handle);
        var sizes = new Size[Math.Max(0, count)];
        for (int i = 0; i < sizes.Length; i++)
        {
            // By index, without loading the page: a 1,000-page file has its
            // whole column laid out before a single page is drawn. A page the
            // engine cannot size gets A4 rather than a zero-height hole.
            sizes[i] = Native.FPDF_GetPageSizeByIndexF(document.Handle, i, out var size) != 0 &&
                       size.Width > 0 && size.Height > 0
                ? new Size(size.Width, size.Height)
                : new Size(595, 842);
        }

        if (sizes.Length == 0)
        {
            CloseNow(document);
            return new OpenResult(null, OpenError.Unreadable);
        }

        document.PageSizes = sizes;
        return new OpenResult(document, OpenError.None);
    }

    private static void CloseNow(Document document)
    {
        if (document.Closed)
        {
            return;
        }
        document.Closed = true;
        if (document.Handle != IntPtr.Zero)
        {
            Native.FPDF_CloseDocument(document.Handle);
            document.Handle = IntPtr.Zero;
        }
        ReleaseFile(document);
    }

    private static void ReleaseFile(Document document)
    {
        if (document.FileAccess != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(document.FileAccess);
            document.FileAccess = IntPtr.Zero;
        }
        if (document.StreamHandle.IsAllocated)
        {
            document.StreamHandle.Free();
        }
        document.Stream?.Dispose();
        document.Stream = null;
    }

    // PDFium's read callback. One delegate for every document - the stream
    // arrives through m_Param - held in a static so the GC never collects the
    // thunk PDFium is holding a pointer to.
    private static readonly Native.GetBlockCallback GetBlockDelegate = GetBlock;
    private static readonly IntPtr GetBlockPointer = Marshal.GetFunctionPointerForDelegate(GetBlockDelegate);
    // The render thread is the only caller, so one buffer serves every read.
    private static byte[] _blockBuffer = new byte[64 * 1024];

    private static int GetBlock(IntPtr param, uint position, IntPtr buffer, uint size)
    {
        try
        {
            if (GCHandle.FromIntPtr(param).Target is not FileStream stream)
            {
                return 0;
            }
            if (_blockBuffer.Length < size)
            {
                _blockBuffer = new byte[size];
            }
            stream.Position = position;
            stream.ReadExactly(_blockBuffer, 0, (int)size);
            Marshal.Copy(_blockBuffer, 0, buffer, (int)size);
            return 1;
        }
        catch (Exception)
        {
            // A share that dropped mid-read, a file cut short under us. Zero
            // tells PDFium the read failed and it draws what it has; an
            // exception must never cross back into native code.
            return 0;
        }
    }

    // ----- drawing -----------------------------------------------------------

    // Bitmaps past this many pixels are not made: at 4x on a wide panel a page
    // would otherwise be a 100MB allocation, and the column shows it scaled up
    // rather than sharp - soft, not missing.
    private const long MaxPagePixels = 16L * 1024 * 1024;

    // Render thread only.
    private static BitmapSource? RenderPage(Document document, int index, int pixelWidth)
    {
        if (index < 0 || index >= document.PageCount || pixelWidth <= 0)
        {
            return null;
        }

        var pageSize = document.PageSizes[index];
        int width = pixelWidth;
        int height = Math.Max(1, (int)Math.Round(width * pageSize.Height / pageSize.Width));
        if ((long)width * height > MaxPagePixels)
        {
            double shrink = Math.Sqrt((double)MaxPagePixels / ((long)width * height));
            width = Math.Max(1, (int)(width * shrink));
            height = Math.Max(1, (int)(height * shrink));
        }

        IntPtr page = Native.FPDF_LoadPage(document.Handle, index);
        if (page == IntPtr.Zero)
        {
            return null;
        }

        IntPtr bitmap = IntPtr.Zero;
        try
        {
            bitmap = Native.FPDFBitmap_Create(width, height, 0);
            if (bitmap == IntPtr.Zero)
            {
                return null;
            }

            // Paper first: a page that paints no background of its own is
            // transparent in PDF terms and white on every reader's screen.
            Native.FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
            // FPDF_ANNOT draws annotations' own appearances (stamps, highlights,
            // filled form fields that carry one). No LCD text: the bitmap is
            // scaled on screen while a zoom settles, and subpixel colour
            // fringes would show through that.
            Native.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, Native.RenderAnnotations);

            IntPtr pixels = Native.FPDFBitmap_GetBuffer(bitmap);
            int stride = Native.FPDFBitmap_GetStride(bitmap);
            // Copied straight out of PDFium's buffer into WPF's, with no
            // managed array in between - a page is megabytes, and an array per
            // page would be a large-object-heap allocation per scroll step.
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null,
                pixels, stride * height, stride);
            source.Freeze();
            return source;
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
            {
                Native.FPDFBitmap_Destroy(bitmap);
            }
            Native.FPDF_ClosePage(page);
        }
    }

    // ----- the C API ---------------------------------------------------------

    // Declarations from fpdfview.h of the build in use (157.0.8100). x64 has
    // one calling convention, so none is spelled out.
    private static class Native
    {
        public const string Library = "pdfium";

        public const uint ErrPassword = 4;
        public const int RenderAnnotations = 0x01;

        public delegate int GetBlockCallback(IntPtr param, uint position, IntPtr buffer, uint size);

        [StructLayout(LayoutKind.Sequential)]
        public struct FileAccess
        {
            public uint FileLength;
            public IntPtr GetBlock;
            public IntPtr Param;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SizeF
        {
            public float Width;
            public float Height;
        }

        [DllImport(Library)]
        public static extern void FPDF_InitLibrary();

        [DllImport(Library)]
        public static extern uint FPDF_GetLastError();

        [DllImport(Library)]
        public static extern IntPtr FPDF_LoadCustomDocument(IntPtr fileAccess,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);

        [DllImport(Library)]
        public static extern void FPDF_CloseDocument(IntPtr document);

        [DllImport(Library)]
        public static extern int FPDF_GetPageCount(IntPtr document);

        [DllImport(Library)]
        public static extern int FPDF_GetPageSizeByIndexF(IntPtr document, int index, out SizeF size);

        [DllImport(Library)]
        public static extern IntPtr FPDF_LoadPage(IntPtr document, int index);

        [DllImport(Library)]
        public static extern void FPDF_ClosePage(IntPtr page);

        [DllImport(Library)]
        public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page,
            int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

        [DllImport(Library)]
        public static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);

        [DllImport(Library)]
        public static extern int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top,
            int width, int height, uint color);

        [DllImport(Library)]
        public static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);

        [DllImport(Library)]
        public static extern int FPDFBitmap_GetStride(IntPtr bitmap);

        [DllImport(Library)]
        public static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    }
}
