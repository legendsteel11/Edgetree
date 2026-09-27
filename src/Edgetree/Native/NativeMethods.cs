using System.IO;
using System.Runtime.InteropServices;

namespace SidebarExplorer.App.Native;

internal static class NativeMethods
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WS_EX_TOPMOST = 0x00000008;

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNoTopmost = new(-2);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const int ASFW_ANY = -1;

    // DWMWA_WINDOW_CORNER_PREFERENCE (Windows 11 22000+ only). DwmSetWindowAttribute
    // just returns a failure HRESULT on older Windows instead of throwing, so this
    // is a safe no-op there - the window keeps its already-square default look.
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;

    // Forces ShellExecuteEx to go through the target's IContextMenu shortcut-menu
    // handler (the same path Explorer's own "Open" uses) instead of a bare
    // CreateProcess with the registered command line. Apps whose single-instance
    // handling depends on that shell-level DDE/negotiation - as opposed to just
    // checking on every raw launch - only respond correctly when this is set.
    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const int SW_SHOWNORMAL = 1;

    private const uint ABM_GETSTATE = 0x00000004;
    private const uint ABM_GETTASKBARPOS = 0x00000005;
    private const int ABS_AUTOHIDE = 0x0000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    // Which screen edge an AUTO-HIDDEN taskbar sits on, in that monitor's own
    // physical pixels - null when the taskbar is always visible.
    //
    // Worth asking because Windows does NOT subtract an auto-hidden taskbar
    // from the work area: it reports the whole screen. A window that fills
    // that work area therefore covers the strip the taskbar pops out of, and
    // the cursor arriving there lands inside the window instead of on the
    // screen edge - so the taskbar never comes up (measured 2026-07-28 on a
    // 3840x2160 screen: work area 3840x2160, taskbar rect y 2112-2160).
    //
    // Returns the taskbar's own rect too, so the caller can tell whether it
    // even belongs to the monitor being positioned on.
    internal static (int Edge, int Left, int Top, int Right, int Bottom)? GetAutoHiddenTaskbar()
    {
        var state = new APPBARDATA();
        state.cbSize = Marshal.SizeOf(state);
        if (((int)SHAppBarMessage(ABM_GETSTATE, ref state) & ABS_AUTOHIDE) == 0)
        {
            return null;
        }

        var pos = new APPBARDATA();
        pos.cbSize = Marshal.SizeOf(pos);
        if (SHAppBarMessage(ABM_GETTASKBARPOS, ref pos) == IntPtr.Zero)
        {
            return null;
        }

        return ((int)pos.uEdge, pos.rc.Left, pos.rc.Top, pos.rc.Right, pos.rc.Bottom);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);

    [DllImport("kernel32.dll")]
    private static extern uint GetLogicalDrives();

    // WHICH LETTERS EXIST, and deliberately nothing else - one bit per drive
    // letter, answered from what the object manager already holds. No volume is
    // asked anything, and that is the whole reason this stands in front of
    // DriveInfo rather than beside it: DriveInfo reads IsReady and a volume
    // label per drive, and a sleeping NAS answers those in tens of seconds on
    // whichever thread does the asking. A device message can arrive at any
    // moment, so the question asked first has to be one that cannot block.
    //
    // Zero means the call failed. Callers must read that as "no answer" and not
    // as "every drive left", or a failure would empty the tree.
    internal static uint LogicalDriveMask() => GetLogicalDrives();

    private const uint SEM_FAILCRITICALERRORS = 0x0001;
    private const uint SEM_NOOPENFILEERRORBOX = 0x8000;

    // Windows, not the app, puts up a box when a drive stops answering mid-
    // read ("network path not found", "no disk in the drive"). A sidebar that
    // keeps a NAS folder open hits that every time the NAS is switched off -
    // one flashed up during a 2026-07-26 test - and the app cannot dismiss or
    // even see it. This tells the OS to fail those calls back to us instead of
    // interrupting the user with a dialog they didn't ask for; every such call
    // here already handles failure (see FileSystemService's readFailed rule).
    public static void SuppressDeviceErrorDialogs()
        => SetErrorMode(SetErrorMode(0) | SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX);

    // The window manager's own answer, not WPF's belief about it.
    public static bool HasTopmostStyle(IntPtr hWnd)
        => (GetWindowLong(hWnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;

    // WPF's Topmost property writes through to the window only when its value
    // CHANGES, so a window that lost its topmost z-order behind the
    // framework's back can never be restored by assigning Topmost = true again
    // - the property already reads true. This goes straight to the window
    // manager, and re-raises the window within the topmost band as well, which
    // reading the style bit alone can't tell you is needed.
    //
    // Measured 2026-07-25: freshly launched, the window's real extended style
    // was 0x00000080 - TOOLWINDOW only, no TOPMOST - while the app believed
    // Topmost was true. Every read-modify-write of GWL_EXSTYLE here
    // (MakeToolWindow/MakeAppWindow, called at startup and at every dock
    // change) is a chance to land in that state, which is why the callers
    // re-state the intended z-order afterwards rather than trusting it to
    // survive.
    public static bool SetTopmost(IntPtr hWnd, bool topmost)
        => SetWindowPos(hWnd, topmost ? HwndTopmost : HwndNoTopmost, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    // Position AND size in one call, which WPF cannot express: its Left and
    // Width are separate dependency properties and each one issues its own
    // SetWindowPos, so a window that has to move while it resizes passes
    // through a state where only one of the two has landed - and that state
    // gets composed. Visible as the right-docked sidebar tearing away from the
    // screen edge and snapping back, further the faster the drag (2026-08-15).
    // Everything in PHYSICAL pixels, like every other call in this file.
    public static bool MoveAndResize(IntPtr hWnd, int x, int y, int width, int height)
        => SetWindowPos(hWnd, IntPtr.Zero, x, y, width, height,
            SWP_NOZORDER | SWP_NOACTIVATE);

    // Clips the window to the horizontal band [topPx, bottomPx), in physical
    // pixels relative to the window's own top-left. The region's right edge is
    // deliberately enormous so width changes never have to re-issue it - a
    // region is a full-frame invalidate each time it is set, and this clip
    // only exists for the two horizontal edges. Ownership of the region passes
    // to the window on success.
    private const int RegionFarEdge = 1 << 20;

    public static void SetBandRegion(IntPtr hWnd, int topPx, int bottomPx)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        IntPtr region = CreateRectRgn(0, topPx, RegionFarEdge, bottomPx);
        if (region == IntPtr.Zero)
        {
            return;
        }

        if (SetWindowRgn(hWnd, region, true) == 0)
        {
            DeleteObject(region);
        }
    }


    // Clipboard change notification. Nothing else tells an app that its own
    // cut has been consumed or replaced - the 잘라내기 markers would otherwise
    // outlive the clipboard entry they stand for (2026-07-28: pasting an
    // Edgetree cut in Explorer left the row faded here forever).
    public const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    // SPIF_UPDATEINIFILE persists the change; SPIF_SENDWININICHANGE is what
    // makes every open Explorer window repaint its desktop right away.
    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPIF_UPDATEINIFILE = 0x0001;
    private const uint SPIF_SENDWININICHANGE = 0x0002;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

    public static bool TrySetDesktopWallpaper(string path)
        => SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);

    // ----- 모니터별 바탕화면 -------------------------------------------------
    //
    // SPI_SETDESKWALLPAPER above has no way to name a monitor - it sets the
    // desktop, all of it. Windows 8 added IDesktopWallpaper, which does, and
    // that is the whole of what this block is for: setting the picture on the
    // ONE display the sidebar is currently sitting on (2026-08-14 request).
    //
    // The monitors are addressed by DEVICE PATH strings, which say nothing
    // about position - so the mapping from "the screen this window is on" to
    // "the string Windows wants" has to go through GetMonitorRECT and compare
    // rectangles. That is why the caller passes a rect rather than an index:
    // Screen.AllScreens' ordering and this interface's ordering are two
    // separate enumerations, and nothing promises they agree.
    //
    // The rects here are PHYSICAL pixels, the same units
    // System.Windows.Forms.Screen.Bounds reports, so a per-monitor DPI setup
    // needs no conversion - and must not be given one.
    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        // Only the four that are used, in declaration order - a COM interface
        // is a vtable, so every method BEFORE the ones wanted has to be
        // declared even when it is never called, and the ones after can simply
        // be left off. Getting this order wrong calls a different method
        // entirely, which is why the unused three carry their real signatures
        // rather than a placeholder.
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId,
            [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId,
            [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
        void GetMonitorDevicePathAt(uint monitorIndex,
            [MarshalAs(UnmanagedType.LPWStr)] out string monitorId);
        void GetMonitorDevicePathCount(out uint count);
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out Rect displayRect);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [ComImport]
    [Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass
    {
    }

    // The picture on ONE monitor, named by the physical-pixel bounds of the
    // screen it occupies. False means the caller should fall back to the
    // all-monitors call above rather than leave the click doing nothing:
    // the interface is missing on Windows 7, the device paths can be stale
    // right after a display change, and a wallpaper set to a "slideshow" or
    // "span" arrangement can refuse a per-monitor assignment outright.
    public static bool TrySetDesktopWallpaperOnMonitor(string path, int left, int top, int right, int bottom)
    {
        IDesktopWallpaper? wallpaper = null;
        try
        {
            // Called from a Task.Run pool thread, which is MTA - and a shell
            // COM object created there can come back as a PROXY that marshals
            // its calls onto this app's STA thread, i.e. onto the UI thread
            // (the trap ShellThumbnailService documents, where it cost 1.5s
            // freezes on a cold NAS). Left as is deliberately: the expensive
            // half of setting a wallpaper is the re-encode, which happens
            // before this and stays on the pool thread, and what is left here
            // is four short calls. If the sidebar is ever seen to hitch at the
            // moment a wallpaper is set, this is the first place to look, and
            // the fix is to make the call from an STA thread of our own.
            wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            wallpaper.GetMonitorDevicePathCount(out uint count);

            for (uint i = 0; i < count; i++)
            {
                wallpaper.GetMonitorDevicePathAt(i, out string monitorId);
                if (string.IsNullOrEmpty(monitorId))
                {
                    // A path that is registered but not currently attached -
                    // Windows keeps these so a monitor coming back gets its
                    // own picture again. GetMonitorRECT throws on them.
                    continue;
                }

                wallpaper.GetMonitorRECT(monitorId, out Rect rect);
                if (rect.Left == left && rect.Top == top &&
                    rect.Right == right && rect.Bottom == bottom)
                {
                    wallpaper.SetWallpaper(monitorId, path);
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException
                                       or NotSupportedException or PlatformNotSupportedException)
        {
            return false;
        }
        finally
        {
            if (wallpaper is not null)
            {
                Marshal.ReleaseComObject(wallpaper);
            }
        }

        // Enumerated cleanly and no monitor matched - a display arrangement
        // that changed between the window being measured and this running.
        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static readonly IntPtr HwndBroadcast = new(0xffff);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo executeInfo);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT
    {
        public int cbSize;
        public int dwFlags;
        public IntPtr hwndTrack;
        public int dwHoverTime;
    }

    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT eventTrack);

    private const int TME_LEAVE = 0x0002;
    private const int TME_NONCLIENT = 0x0010;

    // ASK WINDOWS TO SAY WHEN THE CURSOR LEAVES THE FRAME. WM_NCMOUSELEAVE is
    // not sent unless it has been requested, and without it a hint drawn on the
    // caption strip would stay up after the pointer had gone somewhere else
    // entirely - there is no other message that reports it (the client area's
    // own WM_MOUSEMOVE only covers the pointer coming back INTO the window).
    //
    // Requested again on every NCMOUSEMOVE: the subscription is one-shot, spent
    // by the leave message it produces.
    public static void TrackNonClientMouseLeave(IntPtr hwnd)
    {
        var track = new TRACKMOUSEEVENT
        {
            dwFlags = TME_LEAVE | TME_NONCLIENT,
            hwndTrack = hwnd,
        };
        track.cbSize = Marshal.SizeOf(track);
        TrackMouseEvent(ref track);
    }

    // Bitmap.GetHicon hands out a handle the caller owns; disposing the Icon
    // wrapped around it does not free it.
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;

    // Whether a mouse button has been pressed since the LAST call to this
    // method - GetAsyncKeyState's low bit, which latches a press and clears on
    // read. Asking "is a button down right now" instead misses any click that
    // began and ended between two polls, and a poll running on the UI thread
    // gets pushed around by whatever else that thread is doing: during the
    // auto-hide slide the gap between polls stretches well past the length of
    // an ordinary click, which is how clicks outside the sidebar were being
    // swallowed (2026-08-05, confirmed by the watch never even logging a
    // decision).
    //
    // Both buttons are polled every time, not short-circuited, so a press on
    // one cannot sit latched waiting to be reported on some later call that
    // happened to ask about the other.
    public static bool ConsumeMouseButtonPress()
    {
        bool left = (GetAsyncKeyState(VK_LBUTTON) & 0x0001) != 0;
        bool right = (GetAsyncKeyState(VK_RBUTTON) & 0x0001) != 0;
        return left || right;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    private const int RGN_OR = 2;

    // Rounds the two corners on ONE side of a window by clipping its shape at
    // the OS level.
    //
    // Why a region and not the two obvious alternatives: DWM's corner
    // preference rounds all four corners and offers two fixed radii, and a WPF
    // Border with CornerRadius would need AllowsTransparency to show through -
    // which turns off hardware acceleration for the whole window (and takes
    // ClearType and inline IME with it).
    //
    // The shape is a round-rect OR'd with a plain rect covering the flat side,
    // which squares those two corners back off. Regions are not anti-aliased,
    // so the curve is stepped - fine at the small radius this is for, and the
    // reason not to reach for it at larger ones.
    //
    // hRgn ownership passes to the window on success, so it must not be freed
    // here; on failure it must be. Pass roundLeftSide: false to round the
    // right-hand corners.
    public static void SetRoundedSideRegion(IntPtr hWnd, int width, int height, int radius, bool roundLeftSide)
    {
        if (hWnd == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return;
        }

        radius = Math.Max(0, Math.Min(radius, Math.Min(width, height) / 2));
        if (radius == 0)
        {
            ClearWindowRegion(hWnd);
            return;
        }

        // CreateRoundRectRgn's right/bottom are exclusive, hence the +1s.
        IntPtr rounded = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        IntPtr flatSide = roundLeftSide
            ? CreateRectRgn(radius, 0, width + 1, height + 1)
            : CreateRectRgn(0, 0, width - radius + 1, height + 1);

        if (rounded == IntPtr.Zero || flatSide == IntPtr.Zero)
        {
            DeleteObject(rounded);
            DeleteObject(flatSide);
            return;
        }

        CombineRgn(rounded, rounded, flatSide, RGN_OR);
        DeleteObject(flatSide);

        if (SetWindowRgn(hWnd, rounded, true) == 0)
        {
            DeleteObject(rounded);
        }
    }

    public static void ClearWindowRegion(IntPtr hWnd)
    {
        if (hWnd != IntPtr.Zero)
        {
            SetWindowRgn(hWnd, IntPtr.Zero, true);
        }
    }

    public static void MakeToolWindow(IntPtr hWnd)
    {
        var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
        exStyle |= WS_EX_TOOLWINDOW;
        exStyle &= ~WS_EX_APPWINDOW;
        SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);
    }

    public static void MakeAppWindow(IntPtr hWnd)
    {
        var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
        exStyle &= ~WS_EX_TOOLWINDOW;
        exStyle |= WS_EX_APPWINDOW;
        SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);
    }

    // GONE (2026-08-13): a registered window message posted to HWND_BROADCAST,
    // which is how a second launch used to ask the running instance to come
    // forward. The reasoning was that a broadcast reaches every top-level
    // window, so the second process would not have to find the first one's
    // hwnd - and the flaw is in a single word of that guarantee. A broadcast
    // reaches invisible and disabled top-level windows, but only UNOWNED ones,
    // and this app's window is owned whenever ShowInTaskbar is false, which is
    // every docked moment - i.e. almost always. It was posted and never
    // arrived. App.OnStartup uses a named EventWaitHandle instead; a kernel
    // event cannot be filtered out by a window style.
    //
    // If a window message is ever wanted here again, it has to be sent to a
    // hwnd this app finds for itself, not broadcast.

    // Our sidebar typically has foreground focus at the moment it launches a
    // sibling process (e.g. opening a file), and Windows' foreground-lock
    // policy then keeps that new window/dialog from activating on top - it
    // opens but stays hidden behind everything. Granting the next
    // SetForegroundWindow call permission fixes that.
    public static void AllowNextWindowToActivate()
    {
        AllowSetForegroundWindow(ASFW_ANY);
    }

    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_AUTO = 0;
    private const int DWMSBT_MAINWINDOW = 2;

    // Declares (or withdraws) a Mica system backdrop for the window - not for
    // its looks, which don't change at all behind this app's opaque content,
    // but for what DWM paints where the app hasn't yet: with a glass frame
    // extended, a fast native resize fills the freshly exposed area with DWM's
    // under-sheet, which is ACCENT-colored by default (blue slabs, 2026-08-07)
    // and theme-colored material once a backdrop is declared - dark grey on
    // the dark theme, quiet against the sidebar. Windows 11 22621+ only; on
    // older builds DwmSetWindowAttribute fails harmlessly, same rule as the
    // corner preference below.
    public static void SetMicaBackdrop(IntPtr hWnd, bool enabled)
    {
        int type = enabled ? DWMSBT_MAINWINDOW : DWMSBT_AUTO;
        DwmSetWindowAttribute(hWnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
    }

    // DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1+ / Win11: attribute 20).
    // Declares the WINDOW itself dark-themed to DWM - which turns out to be
    // what decides the color of the sheet DWM composites behind an extended
    // glass frame during moves/resizes. The app's own brushes and
    // DWMWA_CAPTION_COLOR provably do not (2026-08-07 rounds); with this
    // set, the transition flash follows the app theme instead of the OS one:
    // dark theme gets a dark sheet, light keeps the white it always matched
    // (requested 2026-08-08). Fails harmlessly on older builds, same
    // rule as the corner preference below.
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void SetImmersiveDarkMode(IntPtr hWnd, bool dark)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        int value = dark ? 1 : 0;
        DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    // Docked, the window is flush against the screen edge - rounded corners
    // there would just look like a gap against the taskbar/desktop. Floating,
    // it's a normal window on the desktop like any other, so it should round
    // the same way Windows 11 apps do by default.
    public static void SetWindowCornerPreference(IntPtr hWnd, bool rounded)
    {
        int preference = rounded ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    // Returns false if the file has no associated app (or the call otherwise
    // failed) so the caller can fall back to the "Open With" picker.
    public static bool TryOpenWithShellVerb(string path, string verb = "open")
    {
        var info = new ShellExecuteInfo
        {
            fMask = SEE_MASK_INVOKEIDLIST,
            lpVerb = verb,
            lpFile = path,
            lpDirectory = Path.GetDirectoryName(path),
            nShow = SW_SHOWNORMAL
        };
        info.cbSize = Marshal.SizeOf(info);

        return ShellExecuteEx(ref info);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint count, IntPtr[] items, uint flags);

    // The folder a file is in, in an Explorer window, with the file selected
    // AND scrolled into view - ShellFileService.RevealInExplorer has the
    // measurement that put this in front of explorer.exe /select. Both PIDLs
    // absolute, which the call accepts. COM must be initialized on the calling
    // thread; false sends the caller to its fallback.
    public static bool TryOpenFolderAndSelect(string file)
    {
        string? folder = Path.GetDirectoryName(file);
        if (string.IsNullOrEmpty(folder))
        {
            return false;
        }

        IntPtr folderPidl = IntPtr.Zero;
        IntPtr filePidl = IntPtr.Zero;
        try
        {
            return SHParseDisplayName(folder, IntPtr.Zero, out folderPidl, 0, out _) == 0 &&
                   SHParseDisplayName(file, IntPtr.Zero, out filePidl, 0, out _) == 0 &&
                   SHOpenFolderAndSelectItems(folderPidl, 1, new[] { filePidl }, 0) == 0;
        }
        finally
        {
            if (filePidl != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(filePidl);
            }

            if (folderPidl != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(folderPidl);
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // The fix every tray menu needs, and the reason the tray's menu could not
    // simply be a WPF one before: a popup opened while another app owns the
    // foreground does not close when you click away from it - the click never
    // reaches it. Bringing our own window forward first restores the normal
    // dismissal, which is what Windows' own shell does when it shows a tray
    // menu. Failure is not worth handling: the menu still opens, it just keeps
    // the old stickiness.
    public static void BringToForeground(IntPtr hWnd)
    {
        if (hWnd != IntPtr.Zero)
        {
            SetForegroundWindow(hWnd);
        }
    }
}
