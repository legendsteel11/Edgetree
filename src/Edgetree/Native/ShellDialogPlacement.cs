using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SidebarExplorer.App.Native;

// WHERE THE SHELL'S 속성 SHEET OPENS, AND IN WHICH BAND. 속성 goes through
// ShellExecuteEx("properties"), and the shell builds that sheet on a thread of
// its own inside this process, owned by a hidden StubWindow32 it creates on
// that thread first. Nothing ties the sheet to this window, and that cost two
// things (both reported 2026-09-28):
//
// - While the app is topmost - docked with auto-hide, or with 항상 위에 표시 -
//   the sheet opened in the ordinary band underneath the sidebar: covered
//   where the two overlap, and covered completely once the multimedia panel
//   is open.
// - It opened at the CURSOR. The shell puts the sheet's corner on the point a
//   command was invoked from, ShellExecuteEx has no way to name one, and the
//   cursor at that moment is on 속성 - the last row of a long menu. A sheet
//   asked for on the top row of the tree came up near the bottom of the
//   screen, and one asked for further down was pushed against the bottom edge.
//
// Measured before any of this was written (2026-09-28, probes run from a
// PowerShell host):
//
// - The sheet came up on a thread other than the caller's, 30-120ms after
//   ShellExecuteEx returned, below a topmost form; one SetWindowPos(
//   HWND_TOPMOST) put it above. Passing a window in SHELLEXECUTEINFO.hwnd
//   changed nothing - the owner was still the stub - so the sheet cannot be
//   made ours. The lift below puts it where a dialog owned by a topmost window
//   would have been anyway.
// - The sheet's corner lands on the stub, and the stub is created at the
//   cursor. The documented way to name the point, IContextMenu with
//   CMINVOKECOMMANDINFOEX.ptInvoke, was honoured to the pixel - but only once
//   the menu had been filled with CMF_NORMAL, which loads every context-menu
//   extension registered for the type into this process: 45 -> 80 new
//   modules, archivers, a music player and PowerToys among them, and 312ms on
//   the UI thread for the first fill. A fault in any of them would end this
//   app. CMF_DEFAULTONLY loads nothing extra, and ignored the point on the
//   first call in a process while honouring it on the second.
// - Moving the stub the moment it is created works instead: the sheet is
//   then created where the stub is, with nothing seen moving. 5 of 5 - a file
//   twice, a folder, C:\ and an exe - with 31-63ms between the stub and the
//   sheet, which is the margin this runs on.
//
// That margin is a race with the shell's thread, so the sheet is checked
// again when it shows and moved then if the stub was moved too late. The
// case that would otherwise open in the wrong place costs one visible jump.
//
// Two other ways out of the covered sheet were weighed and left. An owner on
// our thread, even if the shell took one, would join the two threads' input,
// and a sheet stuck on a sleeping NAS would then stall this UI with it.
// Dropping the app's own topmost while a sheet is up would let every other
// window cover the sidebar for that time.
//
// A window-event hook rather than a search after each 속성, because it covers
// a sheet that takes seconds to appear on a slow path with no timeout to
// guess. Scoped to this process and skipping the UI thread, it only hears
// about windows the shell makes on its own threads - in the probes, the stub,
// the sheet with its controls, and the input-method windows of that thread.
// The events arrive through this thread's message queue, so the callback runs
// on the UI thread and may read the window freely.
//
// Not covered: a sheet opened while the app was NOT topmost stays where it is
// if the app becomes topmost later, and a lifted sheet stays topmost until it
// is closed - as a dialog owned by a topmost window would. A sheet the shell
// already had open for the same item is brought forward where it stands.
internal sealed class ShellDialogPlacement : IDisposable
{
    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNTHREAD = 0x0001;
    private const int OBJID_WINDOW = 0;
    private const int CHILDID_SELF = 0;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CHILD = 0x40000000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const uint GW_OWNER = 4;

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_ASYNCWINDOWPOS = 0x4000;

    // The class every standard dialog is created with, property sheets included.
    private const string DialogClass = "#32770";

    // The hidden window the shell makes first and hangs the sheet from.
    private const string StubClass = "StubWindow32";

    // How long a requested point waits for its sheet. The stub followed the
    // request by 20-40ms on a local disk; the rest is allowance for a slow
    // path. It expires so that a request the shell answered by raising a
    // sheet it already had open - no new stub - cannot place a later one.
    private const long AnchorLifetimeMs = 5000;

    // A sheet this close to where it was asked for is where the shell put it
    // from the moved stub. The shell keeps a sheet inside the work area a few
    // pixels differently from KeptOnScreen (4px measured at the bottom edge);
    // one that missed the stub is a whole menu's height away.
    private const int PlacementTolerance = 16;

    private delegate void WinEventProc(
        IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr module, WinEventProc callback,
        uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private readonly Window _app;

    // Held for as long as the hook is: the native side keeps only a pointer to
    // it, and a delegate the collector has taken would still be called.
    private readonly WinEventProc _callback;
    private IntPtr _hook;

    // The point the next sheet should open at, in screen pixels, and when it
    // was asked for. Consumed by the sheet that answers it.
    private (int X, int Y)? _anchor;
    private long _anchorSetAt;

    // The stub moved for the current request, so the sheet that shows under
    // it can be recognised and a second stub is left alone.
    private IntPtr _movedStub;

    public ShellDialogPlacement(Window app)
    {
        _app = app;
        _callback = OnWindowEvent;

        // CREATE through SHOW, the one range that holds both events used here.
        // DESTROY sits between them and is dropped on arrival.
        _hook = SetWinEventHook(
            EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW, IntPtr.Zero, _callback,
            (uint)Environment.ProcessId, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNTHREAD);
    }

    // Called just before the shell is asked for a sheet.
    public void PlaceNextSheetAt(int x, int y)
    {
        _anchor = (x, y);
        _anchorSetAt = Environment.TickCount64;
        _movedStub = IntPtr.Zero;
    }

    private (int X, int Y)? LiveAnchor
        => _anchor is { } anchor && Environment.TickCount64 - _anchorSetAt <= AnchorLifetimeMs
            ? anchor
            : null;

    private void OnWindowEvent(
        IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (hwnd == IntPtr.Zero || idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
        {
            return;
        }

        // Top-level only. A sheet's pages and every control on them are
        // windows too, each announced as it is created, and a page is shown
        // again every time its tab is clicked.
        if ((GetWindowLong(hwnd, GWL_STYLE) & WS_CHILD) != 0)
        {
            return;
        }

        if (eventType == EVENT_OBJECT_CREATE)
        {
            OnTopLevelCreated(hwnd);
        }
        else if (eventType == EVENT_OBJECT_SHOW)
        {
            OnTopLevelShown(hwnd, eventThread);
        }
    }

    private void OnTopLevelCreated(IntPtr hwnd)
    {
        if (_movedStub != IntPtr.Zero || LiveAnchor is not { } anchor || ClassOf(hwnd) != StubClass)
        {
            return;
        }

        _movedStub = hwnd;

        // Asynchronous, like every call here on a window of the shell's
        // thread: a plain SetWindowPos waits for that thread to answer. In
        // every probe the request had been applied by the time the shell read
        // the stub's position for the sheet.
        SetWindowPos(hwnd, IntPtr.Zero, anchor.X, anchor.Y, 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
    }

    private void OnTopLevelShown(IntPtr hwnd, uint shellThread)
    {
        if (ClassOf(hwnd) != DialogClass)
        {
            return;
        }

        // WHERE - only for a sheet hanging from a stub while a point is
        // waiting, and only when the stub was moved too late for the shell
        // to have used it.
        IntPtr owner = GetWindow(hwnd, GW_OWNER);
        (int X, int Y)? asked = ClassOf(owner) == StubClass ? LiveAnchor : null;
        bool move = false;
        (int X, int Y) target = default;
        if (asked is { } anchor)
        {
            _anchor = null;
            GetWindowRect(hwnd, out NativeRect shown);
            target = KeptOnScreen(shown, anchor);
            move = Math.Abs(shown.Left - target.X) > PlacementTolerance ||
                   Math.Abs(shown.Top - target.Y) > PlacementTolerance;
        }

        // WHICH BAND - WPF's Topmost, not the real window's flag:
        // ApplyTopmostState keeps the property equal to what the app WANTS,
        // and that is the question here - a sheet that opens while the window
        // has briefly lost the flag would still end up underneath it once the
        // flag is put back.
        bool lift = _app.Topmost && (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0;

        if (move || lift)
        {
            uint flags = SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS;
            if (!move)
            {
                flags |= SWP_NOMOVE;
            }

            if (!lift)
            {
                flags |= SWP_NOZORDER;
            }

            SetWindowPos(hwnd, lift ? HwndTopmost : IntPtr.Zero, target.X, target.Y, 0, 0, flags);
        }

        if (asked is not null || lift)
        {
            LogOutcomeLater(hwnd, shellThread, asked is { } a
                ? $"asked ({a.X},{a.Y}) stub moved={owner == _movedStub && owner != IntPtr.Zero} moved again={move}"
                : "no point asked");
        }
    }

    // The requested corner, moved as little as it takes for the whole sheet
    // to fit the work area of the monitor it is on - what the shell does
    // itself with a stub near an edge.
    private static (int X, int Y) KeptOnScreen(NativeRect sheet, (int X, int Y) anchor)
    {
        var work = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(anchor.X, anchor.Y)).WorkingArea;
        int width = sheet.Right - sheet.Left;
        int height = sheet.Bottom - sheet.Top;
        return (Math.Max(work.Left, Math.Min(anchor.X, work.Right - width)),
                Math.Max(work.Top, Math.Min(anchor.Y, work.Bottom - height)));
    }

    private static string ClassOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        var name = new StringBuilder(32);
        return GetClassName(hwnd, name, name.Capacity) > 0 ? name.ToString() : string.Empty;
    }

    // On the thread that set the hook, which is the only one allowed to remove it.
    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    // ----- Debug instrument -------------------------------------------------
    //
    // Settles in the app itself what the probes could only show from a
    // PowerShell host: that the sheet arrives from a shell thread, ends up
    // above this window, and opens where it was asked. It records the OUTCOME,
    // read back once the asynchronous requests have had time to land - a line
    // written beside the request would report a result whether or not one
    // happened.

    private const uint GW_HWNDNEXT = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr GetTopWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Diagnostics.Conditional("DEBUG")]
    private void LogOutcomeLater(IntPtr sheet, uint shellThread, string placement)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, _app.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            var title = new StringBuilder(256);
            GetWindowText(sheet, title, title.Capacity);
            bool topmost = (GetWindowLong(sheet, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
            string order = ZOrder(sheet, new WindowInteropHelper(_app).Handle);
            GetWindowRect(sheet, out NativeRect at);

            Append($"dialog: '{title}' shell tid={shellThread} ui tid={GetCurrentThreadId()} " +
                   $"-> topmost={topmost}, {order}, at ({at.Left},{at.Top}); {placement}");
        };
        timer.Start();
    }

    // Walks the z-order from the top and reports whichever of the two it meets
    // first.
    private static string ZOrder(IntPtr sheet, IntPtr app)
    {
        for (IntPtr h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDNEXT))
        {
            if (h == sheet)
            {
                return "above the app";
            }

            if (h == app)
            {
                return "BELOW the app";
            }
        }

        return "sheet already closed";
    }

    private static void Append(string line)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgetree");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "autohide.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
