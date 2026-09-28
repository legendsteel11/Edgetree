using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SidebarExplorer.App.Native;

namespace SidebarExplorer.App.Services;

public static class ShellFileService
{
    // The classic Directory\shell\VSCode verb only exists for installs that
    // used the legacy installer's "Add to context menu" checkbox. Newer VS
    // Code builds instead register their Explorer "Open with Code" entry as
    // a modern sparse package (Microsoft.VisualStudioCode, no such registry
    // verb at all) - so neither install path reliably shows up under
    // Directory\shell. App Paths is the one registration VS Code's installer
    // writes unconditionally (Windows' standard "how do I run Code.exe"
    // lookup, used by ShellExecute("Code.exe") and the Run dialog alike),
    // so it's used here as the actual "is Code installed" signal instead.
    private const string CodeAppPathsKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths\Code.exe";

    private static string? ResolveCodeExecutablePath()
    {
        string? path = Registry.CurrentUser.OpenSubKey(CodeAppPathsKey)?.GetValue(null) as string;
        path ??= Registry.LocalMachine.OpenSubKey(CodeAppPathsKey)?.GetValue(null) as string;
        return !string.IsNullOrEmpty(path) && File.Exists(path) ? path : null;
    }

    public static bool IsCodeRegistered() => ResolveCodeExecutablePath() is not null;

    public static void OpenWithCode(string path)
    {
        string? exePath = ResolveCodeExecutablePath();
        if (exePath is null)
        {
            return;
        }

        NativeMethods.AllowNextWindowToActivate();

        // UseShellExecute=false + ArgumentList so we can (a) hand the path to
        // Code.exe with correct quoting and (b) scrub ELECTRON_RUN_AS_NODE from
        // the child's environment. If Edgetree itself was started from a shell
        // that had that variable set, Code.exe would otherwise inherit it and
        // run as a bare Node interpreter on the path - failing to launch the
        // editor at all (opening nothing) rather than opening the file/folder.
        var startInfo = new ProcessStartInfo(exePath) { UseShellExecute = false };
        startInfo.ArgumentList.Add(path);
        startInfo.Environment.Remove("ELECTRON_RUN_AS_NODE");

        try
        {
            Process.Start(startInfo);
        }
        catch (Win32Exception) { }
    }


    public static void OpenWithDefaultApp(string path)
    {
        NativeMethods.AllowNextWindowToActivate();

        // Goes through the same IContextMenu shortcut-menu-handler negotiation
        // Explorer's own double-click uses, instead of Process.Start's plain
        // ShellExecuteEx - some apps' single-instance handling (e.g. reusing an
        // already-open window) only responds correctly to that path.
        if (NativeMethods.TryOpenWithShellVerb(path))
        {
            return;
        }

        // No app is associated with this extension (common for developer file
        // types like .cs/.md), or the call otherwise failed. Explorer would show
        // the "How do you want to open this file?" picker in that case; do the
        // same instead of silently doing nothing.
        OpenWithPicker(path);
    }

    public static void OpenWithPicker(string path)
    {
        NativeMethods.AllowNextWindowToActivate();

        // "openas" is Explorer's own documented shell verb for the "How do you
        // want to open this?" picker, resolved through the same ShellExecuteEx
        // path as ShowProperties below - more reliable than the older
        // rundll32 shell32.dll,OpenAs_RunDLL trick, which several Windows 10/11
        // builds silently no-op depending on shell extension state.
        NativeMethods.TryOpenWithShellVerb(path, "openas");
    }

    public static void ShowProperties(string path)
    {
        NativeMethods.AllowNextWindowToActivate();
        NativeMethods.TryOpenWithShellVerb(path, "properties");
    }

    // Call off the UI thread: the fallback decodes the picture at full size.
    //
    // The original file goes to Windows as-is first - jpg/png/bmp are taken
    // directly, nothing is copied and the wallpaper points at the user's own
    // file. Only when that is refused (a format WIC decodes but the wallpaper
    // path won't take - webp/heic with the store codec, ico) the ORIGINAL is
    // re-encoded as a PNG under %AppData%\Edgetree and Windows pointed at the
    // copy. The original, never the panel's bitmap: the viewer decodes at
    // panel width, and a wallpaper made from that would be soft on purpose.
    // monitorBounds: the physical-pixel rectangle of the display to set, or
    // null for every display at once. Non-null goes through IDesktopWallpaper
    // (Windows 8+) and falls back to the all-monitors call if that refuses -
    // an old Windows, a slideshow/span arrangement, or a display layout that
    // changed underneath. Better the picture lands everywhere than the menu
    // item does nothing.
    public static bool TrySetDesktopWallpaper(
        string path, (int Left, int Top, int Right, int Bottom)? monitorBounds = null)
    {
        if (TryHandToWindows(path, monitorBounds))
        {
            return true;
        }

        try
        {
            var frame = BitmapFrame.Create(
                new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(frame);

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgetree");
            Directory.CreateDirectory(dir);

            // ONE FILE PER MONITOR. The re-encoded copy used to be a single
            // "wallpaper.png", which was right while every monitor showed the
            // same picture and became a trap the moment they could differ:
            // Windows holds the PATH, not the pixels, so setting a webp on the
            // second screen would rewrite the file the first screen was still
            // pointing at and both would change (2026-08-14, caught while
            // building per-monitor support rather than after shipping it).
            // Keyed by the rectangle because that is what identifies the
            // target here; the device path would be tidier and is not in hand
            // at this level.
            string name = monitorBounds is { } b
                ? $"wallpaper-{b.Left}_{b.Top}_{b.Right}_{b.Bottom}.png"
                : "wallpaper.png";
            string copy = Path.Combine(dir, name);
            using (var stream = File.Create(copy))
            {
                encoder.Save(stream);
            }

            return TryHandToWindows(copy, monitorBounds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or FileFormatException or ArgumentException or UriFormatException)
        {
            return false;
        }
    }

    private static bool TryHandToWindows(
        string path, (int Left, int Top, int Right, int Bottom)? monitorBounds)
    {
        if (monitorBounds is { } bounds &&
            NativeMethods.TrySetDesktopWallpaperOnMonitor(
                path, bounds.Left, bounds.Top, bounds.Right, bounds.Bottom))
        {
            return true;
        }

        return NativeMethods.TrySetDesktopWallpaper(path);
    }

    public static void OpenTerminal(string folderPath)
    {
        NativeMethods.AllowNextWindowToActivate();
        try
        {
            // Windows Terminal, if installed, is the nicer default; cmd.exe
            // always exists so it's a safe fallback if wt.exe isn't found.
            Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{folderPath}\"") { UseShellExecute = true });
            return;
        }
        catch (Win32Exception) { }

        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = true, WorkingDirectory = folderPath });
        }
        catch (Win32Exception) { }
    }

    public static void RevealInExplorer(string path)
    {
        NativeMethods.AllowNextWindowToActivate();

        // A FILE GOES THROUGH SHOpenFolderAndSelectItems, NOT explorer /select
        // (2026-09-28). Both select it; only this one shows it. Measured on a
        // 131-picture folder, three files, the selected item read back through
        // UI Automation: /select left it on or just past the bottom edge of the
        // file list every time (its top at 1995, 1995 and 1980 against a list
        // ending at 1991), while this call put it inside the view (1859, 1859,
        // 1844). /select stays as the fallback.
        //
        // On an STA thread of its own: this call parses the path in THIS
        // process, where /select left that to explorer.exe, and a path on a
        // sleeping NAS can take seconds to parse. The folder test moved over
        // with it for the same reason.
        var thread = new Thread(() =>
        {
            try
            {
                // /select opens the *parent* folder with the item highlighted -
                // which is what "show me where this is" means for a file, but
                // for a folder it lands on the parent instead of opening the
                // folder itself. Passing a folder's own path plainly opens its
                // contents.
                if (Directory.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                }
                else if (!NativeMethods.TryOpenFolderAndSelect(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                }
            }
            // Wider than the UI-thread version's Win32Exception alone: nothing
            // catches for this thread, and an exception leaving it ends the app.
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException
                                           or ArgumentException or IOException or UnauthorizedAccessException)
            {
            }
        })
        {
            IsBackground = true,
            Name = "Reveal in Explorer"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
}
