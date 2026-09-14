using System.Threading;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using SidebarExplorer.App.Native;
using SidebarExplorer.App.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace SidebarExplorer.App;

public partial class App : Application
{
    private NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayBaseIcon;
    private System.Drawing.Icon? _trayUpdateIcon;
    private IntPtr _trayUpdateIconHandle;

    // Held for the app's whole lifetime (a field, not a local) so it isn't
    // released early by the GC - see OnStartup/OnExit.
    private Mutex? _singleInstanceMutex;

    // How a second launch reaches the instance that is already running. Named,
    // so the two processes need nothing else in common - the same reasoning the
    // mutex above is named for.
    //
    // This replaced a PostMessage to HWND_BROADCAST (2026-08-13). A broadcast
    // reaches every top-level window "including disabled or invisible UNOWNED
    // windows" - and this app's window is owned for most of its life: docked
    // means ShowInTaskbar=false, and WPF implements that by parking the window
    // under a hidden owner. So the message went out and simply never arrived,
    // in exactly the state the app normally sits in. Measured, not deduced: the
    // tray's own routes into RestoreMainWindow worked in the same session where
    // re-running the exe did nothing at all.
    //
    // A kernel event has no opinion about window styles, ownership, z-order or
    // visibility, which is what makes it the right shape for "the window may be
    // a sliver, hidden to the tray, or behind everything".
    private EventWaitHandle? _activateSignal;
    private RegisteredWaitHandle? _activateWait;

    // Minimize-to-tray (MainWindow's "_" button calls Hide(), not Close()) needs
    // some way back - so the icon stays visible regardless of the "always show
    // tray icon" setting whenever the window is currently hidden.
    public bool IsTrayIconVisible
    {
        get => _trayIcon?.Visible ?? false;
        set
        {
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = value;
            }
        }
    }

    // Called once, by MainWindow's update check, when a newer release exists.
    // Lights a dot on the tray icon and puts a row at the top of its menu.
    //
    // Deliberately nothing else: no balloon, no dialog, no download. The tray
    // icon is the one part of this app that is on screen even when the sidebar
    // is hidden or sent to the tray, which is the whole reason the header's own
    // dot was not enough.
    public void ShowUpdateAvailable(Version version)
    {
        if (_trayIcon is null)
        {
            return;
        }

        // The menu ROW is no longer set here: the tray menu is WPF now and
        // reads MainWindow.UpdateAvailableVersion each time it opens (see
        // MainWindow.ShowTrayContextMenu), which also means it cannot go stale.
        // What stays here is the icon and its tooltip, which are this class's.
        //
        // 63 characters is the hard limit on a tray tooltip; the app name plus
        // a short version cannot reach it, but the format string is
        // translatable, so it is trimmed rather than trusted.
        string tip = $"Edgetree — {string.Format(Strings.UpdateAvailableRow, version)}";
        _trayIcon.Text = tip.Length <= 63 ? tip : tip[..63];

        if (_trayBaseIcon is not null && _trayUpdateIcon is null)
        {
            _trayUpdateIcon = CreateDottedIcon(_trayBaseIcon, out _trayUpdateIconHandle);
        }

        if (_trayUpdateIcon is not null)
        {
            _trayIcon.Icon = _trayUpdateIcon;
        }
    }

    // The dot is drawn onto a copy of the app icon rather than shipped as a
    // second .ico: a new <Resource> entry needs a clean rebuild to actually be
    // embedded (an incremental build reports success and silently leaves it
    // out), and this way the badge follows whatever the app icon becomes.
    //
    // GetHicon's handle is owned by us and is NOT freed by disposing the Icon,
    // so it is handed back to be destroyed in OnExit.
    private static System.Drawing.Icon? CreateDottedIcon(System.Drawing.Icon source, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        try
        {
            int size = source.Width > 0 ? source.Width : 32;
            using var canvas = new System.Drawing.Bitmap(size, size);
            using (var g = System.Drawing.Graphics.FromImage(canvas))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var art = source.ToBitmap())
                {
                    g.DrawImage(art, 0, 0, size, size);
                }

                // Bottom-right, a little over a third of the icon - big enough
                // to read at 16px, which is what a tray icon usually is.
                float d = size * 0.4f;
                float x = size - d;
                float y = size - d;

                // A white ring first. Its job is separating the dot from the
                // ICON underneath, not from the background - and app icons are
                // mostly dark or coloured, which a dark ring disappears into.
                // White-ring-plus-red-dot is also what a notification badge
                // looks like everywhere else, so it reads without explanation.
                // Not tied to the system light/dark mode on purpose: that would
                // mean watching for theme changes and redrawing, for a ring.
                using (var ring = new System.Drawing.SolidBrush(
                    System.Drawing.Color.FromArgb(255, 255, 255, 255)))
                {
                    g.FillEllipse(ring, x - 1.5f, y - 1.5f, d + 3, d + 3);
                }

                using var dot = new System.Drawing.SolidBrush(
                    System.Drawing.Color.FromArgb(255, 232, 78, 60));
                g.FillEllipse(dot, x, y, d, d);
            }

            handle = canvas.GetHicon();
            return System.Drawing.Icon.FromHandle(handle);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException
            or System.Runtime.InteropServices.ExternalException)
        {
            // A badge that can't be drawn just isn't drawn - the menu row still
            // carries the message.
            return null;
        }
    }

    // The landing rather than the GitHub release: it carries the "업데이트 내역"
    // card, which is what someone clicking "there is a new version" actually
    // wants to read, and its download buttons already resolve to the latest
    // release anyway. It is also ours, so the visit is measurable - hence the
    // utm_source, without which this cannot be told apart from any other
    // referrer.
    // The SOURCE is a parameter as of 2026-08-17, when the options menu became a
    // second way in. The utm exists to make these visits measurable, and one
    // label for two routes would answer "did anyone click it" while hiding which
    // row they clicked - the tray's, which is on screen even when the app is not,
    // or the menu's, which is where someone already using the app would find it.
    private static void OpenReleasesPage(string source)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                // Query first, anchor last - the other order makes utm_source
                // part of the fragment and it never reaches analytics.
                // #download is DownloadSection.vue's own id, and it lands on
                // the update-history card that sits just above the buttons.
                FileName = $"https://edgetree.vercel.app/?utm_source={source}#download",
                UseShellExecute = true
            });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception
            or InvalidOperationException or System.IO.FileNotFoundException)
        {
        }
    }

    // True when Windows started the app at sign-in rather than a person
    // launching it. The Run value carries the argument (see MainWindow's
    // TrySetStartWithWindows), because until 2026-09-14 it held the bare exe
    // path and nothing could tell the two apart.
    //
    // What it is for: a sign-in start is not the moment someone sat down to look
    // at pictures, so a multimedia panel left open at the last exit should not
    // unfold across the screen before they have asked for anything. Set before
    // base.OnStartup, which is what constructs MainWindow through StartupUri.
    public static bool StartedAtSignIn { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        StartedAtSignIn = e.Args.Any(arg => string.Equals(arg, "--autostart", StringComparison.OrdinalIgnoreCase));

        // Named (not per-version) so an old build and a freshly built one
        // still see each other as the same app - the whole point is blocking
        // duplicate launches regardless of which exe/version is running.
        _singleInstanceMutex = new Mutex(true, "Local\\Edgetree-SingleInstance-8f1d6b2e-4a3f-4c9e-9b1a-2d7e5c6f8a90", out bool createdNew);
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset,
            "Local\\Edgetree-Activate-8f1d6b2e-4a3f-4c9e-9b1a-2d7e5c6f8a90");

        if (!createdNew)
        {
            // Another Edgetree process already holds the mutex - ask it to
            // come to the foreground instead of opening a second window, and
            // exit before constructing anything (window, tray icon, Strings)
            // so there's no flicker. Deliberately no "already running" notice:
            // the answer to launching an app that is already up is the app,
            // not a dialog about it.
            //
            // Windows' foreground lock would otherwise hold the other process's
            // window behind whatever is in front, since this process is the one
            // that was just launched; handing our claim over is what lets the
            // restore actually surface.
            NativeMethods.AllowNextWindowToActivate();
            _activateSignal.Set();
            Environment.Exit(0);
        }

        // Fires on a pool thread whenever a later launch signals, for as long
        // as this process lives (executeOnlyOnce: false - the exe can be run
        // any number of times). Hopped onto the UI thread because everything
        // RestoreMainWindow touches is a window.
        _activateWait = ThreadPool.RegisterWaitForSingleObject(
            _activateSignal,
            (_, _) => Dispatcher.BeginInvoke(new Action(RestoreMainWindow)),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);

        // Must run before base.OnStartup(e) - that call is what actually
        // constructs the StartupUri (MainWindow) window, and every x:Static
        // Strings.* reference in its XAML resolves to whatever's in these
        // fields at that exact moment.
        Strings.Initialize(new SettingsService().Load().Language);

        // BeginSession first: it stamps the post-mortem of the previous
        // session, which reads better above this session's own start line.
        ExitLog.BeginSession();
        ExitLog.Record($"--- started (pid {Environment.ProcessId})");

        // Before anything touches a drive - see the method's own note.
        NativeMethods.SuppressDeviceErrorDialogs();

        // See the recovery handler below. A RATE, not a lifetime count - the
        // first cut was "5 per session", and the flaw in it is that this app
        // stays up for days, so a lifetime cap spends its
        // protection fastest on exactly the long-running sessions it exists
        // for. What separates the two cases is how BUNCHED the failures come,
        // not how many: the legitimate race is occasional (worst observed:
        // three in eleven minutes, then none across dozens of reveals), while
        // a genuinely broken layout throws on every pass - dozens a second.
        // So: recover freely, but a fifth failure within one minute of the
        // first-of-five means something is actually broken and deserves to be
        // seen crashing. Captured by the handler's closure - state lives
        // here, not on the class.
        const int LayoutCrashBurstLimit = 5;
        var layoutCrashTimes = new Queue<DateTime>();

        // Windows signing the user out or shutting down closes the app without
        // any click, and looks exactly like "it just disappeared" afterwards.
        SessionEnding += (_, args) => ExitLog.Record($"windows session ending ({args.ReasonSessionEnding})");

        // ----- 안전장치: WPF 가상화 레이아웃 크래시 복구 (Release 포함) -----
        //
        // A KNOWN WPF framework bug family, not this app's arithmetic:
        // VirtualizingStackPanel computes a negative Size when items change or
        // the viewport moves while a layout pass is in flight (dotnet/wpf
        // #2854, #382; "너비와 높이는 음수일 수 없습니다"). It reached this app
        // on 2026-08-07, the day the docked band halved the tree's viewport:
        // three crashes within minutes around auto-hide reveals, all dying in
        // SyncUniformSizeFlags under InitializeViewport. Reproduced once
        // locally, then 8 identical activation rounds survived - a race, not
        // a deterministic path, so there is nothing app-side to fix directly.
        //
        // The recovery: swallow exactly that exception shape, ask for a fresh
        // measure pass (the very next pass computes sane values - the inputs
        // were never wrong), and cap the attempts so a genuinely persistent
        // failure still surfaces as a crash instead of a silent busy-loop.
        // What this device HIDES: any real negative-size bug of our own in the
        // tree's layout would now read as log lines instead of a crash - which
        // is why every recovery is written to exit.log (Debug) and why the cap
        // exists.
        DispatcherUnhandledException += (_, args) =>
        {
            if (args.Exception is ArgumentException layoutEx &&
                layoutEx.StackTrace?.Contains("VirtualizingStackPanel") == true)
            {
                // Only the last minute's failures count against the limit.
                var now = DateTime.UtcNow;
                while (layoutCrashTimes.Count > 0 && (now - layoutCrashTimes.Peek()) > TimeSpan.FromMinutes(1))
                {
                    layoutCrashTimes.Dequeue();
                }
                if (layoutCrashTimes.Count >= LayoutCrashBurstLimit - 1)
                {
                    // Fifth within a minute: stop recovering, let it crash.
                    ExitLog.Record("virtualization layout crash burst - giving up recovery");
#if DEBUG
                    ExitLog.Record($"UNHANDLED (ui thread): {args.Exception}");
#endif
                    return;
                }
                layoutCrashTimes.Enqueue(now);

                args.Handled = true;
                ExitLog.Record($"RECOVERED (virtualization layout, {layoutCrashTimes.Count}/{LayoutCrashBurstLimit - 1} in the last minute): {layoutEx.Message}");

                // The aborted pass leaves the panel dirty; a root-level nudge
                // makes sure a full pass actually runs rather than waiting on
                // whatever input happens next.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    MainWindow?.InvalidateMeasure();
                }), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

#if DEBUG
            // Not swallowed (Handled stays false) - only written down before
            // the process goes.
            ExitLog.Record($"UNHANDLED (ui thread): {args.Exception}");
#endif
        };

#if DEBUG
        // Background-thread crashes can otherwise leave nothing behind. Never
        // recoverable - by the time this fires the process is already going.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ExitLog.Record($"UNHANDLED (background): {args.ExceptionObject}");
#endif

        base.OnStartup(e);

        var iconUri = new Uri("pack://application:,,,/Resources/app.ico");
        using var iconStream = GetResourceStream(iconUri)!.Stream;

        _trayBaseIcon = new System.Drawing.Icon(iconStream);

        _trayIcon = new NotifyIcon
        {
            Icon = _trayBaseIcon,
            Visible = true,
            Text = "Edgetree"
        };
        _trayIcon.MouseClick += TrayIcon_MouseClick;

        // No ContextMenuStrip any more. The rows moved into MainWindow.xaml as
        // a WPF ContextMenu (see MainWindow.ShowTrayContextMenu): this was the
        // one surface in the app still wearing the system's own look, because
        // WinForms cannot reach the theme brushes, DarkContextMenuStyle or the
        // font size everything else follows.
    }

    private void TrayIcon_MouseClick(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        if (e.Button == System.Windows.Forms.MouseButtons.Left)
        {
            RestoreMainWindow();
            return;
        }

        if (e.Button == System.Windows.Forms.MouseButtons.Right)
        {
            (MainWindow as MainWindow)?.ShowTrayContextMenu();
        }
    }

    // Reached from the tray menu, which now lives in MainWindow's resources -
    // the actions stay here because they are the application's, not the
    // window's, and two of them run with no window on screen at all.
    internal void OpenReleasesPageFromTray() => OpenReleasesPage("app-tray");

    internal void OpenReleasesPageFromMenu() => OpenReleasesPage("app-menu");

    internal void ToggleMainWindowFromTray() => ToggleMainWindowTray();

    internal void ShowAboutFromTray() => ShowAboutCentered();

    // THE TRAY SAYS SOMETHING HAPPENED, for an action that leaves no mark on
    // screen (2026-08-17, on request). The app does not otherwise interrupt with
    // dialogs to confirm what it just did, and this is not one: the balloon is
    // out of the way, dismisses itself, and nothing waits on it.
    //
    // Here rather than in MainWindow because the NotifyIcon is the
    // application's, and it is also the only surface that can speak while the
    // window is hidden in the tray - which the preset shortcut can be pressed
    // from, since it is not a window-scoped key.
    //
    // The timeout is passed and ignored: modern Windows shows these as toasts
    // and picks the duration itself. Kept at a plausible value rather than 0,
    // which older shells read as "forever".
    //
    // TWO PRESSES ARE ONE ANSWER (2026-08-18, on report). Ctrl+Shift+S can be
    // held down or hammered, and every press writes the same preset under the
    // same name - so the balloon after the first says nothing the first did not
    // already say, while Windows queues each one and the tray keeps popping long
    // after the hand stopped.
    //
    // The gate is here rather than at the shortcut because it is a property of
    // this surface: the tray does not repeat itself, whoever asks it to. Only
    // the balloon is held - the save itself already happened and is untouched.
    //
    // Leading edge, not trailing: the first press is the one that needs an
    // answer, and it should arrive while the finger is still on the key. The
    // rest of the burst is dropped rather than delayed, since a balloon that
    // surfaces seconds after the last press is the noise being removed.
    //
    // Keyed on the message, so what is silenced is only ever a REPEAT. Saving
    // preset A and then B inside the window are two different answers and both
    // are shown.
    internal void ShowTrayMessage(string title, string text)
    {
        if (_trayIcon is null)
        {
            return;
        }

        string message = title + '\n' + text;
        long now = Environment.TickCount64;

        if (string.Equals(message, _lastTrayMessage, StringComparison.Ordinal)
            && now - _lastTrayMessageTicks < TrayMessageRepeatMs)
        {
            return;
        }

        _lastTrayMessage = message;
        _lastTrayMessageTicks = now;
        _trayIcon.ShowBalloonTip(3000, title, text, System.Windows.Forms.ToolTipIcon.None);
    }

    // About as long as the balloon itself stays up: Windows picks that duration
    // and it runs around five seconds. Deliberately matched to it rather than
    // set past it - a press landing right on the boundary should still get an
    // answer, even if the two balloons brush past each other for a moment. What
    // is being avoided here is a stack of them, not every last overlap, and of
    // the two ways to be wrong, silence is the worse one.
    private const long TrayMessageRepeatMs = 5000;

    // Empty is the sentinel: no message this app sends is empty, so the first
    // call always passes and no starting tick value has to be invented.
    private string _lastTrayMessage = string.Empty;
    private long _lastTrayMessageTicks;

    // Same open/hide split the title bar's own "_" button and tray click use
    // (see MainWindow.MinimizeButton_Click / RestoreMainWindow) - just picks
    // which of the two applies based on current visibility, per the TODO
    // request that this read "트레이로/닫기" instead of always "열기/닫기".
    private void ToggleMainWindowTray()
    {
        if (MainWindow is not { } window)
        {
            return;
        }

        // NOT window.IsVisible: an auto-hidden sidebar is a few pixels of screen
        // edge and passes that test, so this row used to offer to hide a window
        // the user could not see in the first place, and never offered to open
        // one (see MainWindow.RestoreFromOutside).
        if (window is MainWindow { IsShowingToUser: true } shown)
        {
            // Same persist-before-hiding rule as the title bar's "_" button -
            // see MainWindow.MinimizeButton_Click for why.
            shown.SaveStateBeforeHiding();
            shown.Hide();
            IsTrayIconVisible = true;
        }
        else
        {
            RestoreMainWindow();
        }
    }

    // Opened from the tray, where the window this would normally be
    // positioned relative to (the options button) may itself be hidden - so
    // this centers on the screen instead, per the TODO request, rather than
    // reusing MainWindow.PositionNearOptionsButton.
    private void ShowAboutCentered()
    {
        var window = new AboutWindow((MainWindow as MainWindow)?.UpdateAvailableVersion)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Owner = MainWindow
        };
        window.ShowDialog();
    }

    // Every route in from outside the sidebar - a tray click, the tray menu's
    // 열기, a second launch of the exe - goes through the window's own
    // RestoreFromOutside, which is where the rule for "put it back on screen"
    // lives. This used to be Show/Normal/Activate written out here, and that
    // trio does nothing visible to an auto-hidden window; see that method for
    // the report.
    public void RestoreMainWindow()
    {
        (MainWindow as MainWindow)?.RestoreFromOutside();
    }

    // General app "chrome" (menus/context menus/the Color Settings and About
    // dialogs, header icons) - deliberately separate from AppSettings' 15
    // user-customizable tree colors (see App.xaml's own comment). Called from
    // MainWindow.ApplyColorSettings, which already runs at startup and on
    // every color/theme change, so this needs no separate wiring of its own.
    // Dark values match what used to be hardcoded throughout MainWindow.xaml/
    // ColorSettingsWindow.xaml/AboutWindow.xaml before this existed - a
    // hand-picked light palette for the other side, same reasoning as the
    // tree's own light palette (not a mathematical inversion).
    public void ApplyChromeTheme(bool isLightMode)
    {
        if (isLightMode)
        {
            SetChromeBrush("ForegroundText", "#FF3B3B3B");
            // Menu text runs ~10% DARKER than ForegroundText in light mode
            // (and ~10% brighter in dark mode below) - user call 2026-07-21:
            // menu labels wanted a touch more presence than the shared chrome
            // color in both themes, which "more" means toward-black on white
            // and toward-white on dark. Split from ForegroundText so headers/
            // footers sharing that brush stay as they were.
            SetChromeBrush("MenuForeground", "#FF353535");
            // Was #5F5F5F, which is 42 levels off the menu's own text and STILL
            // a dark ink on a near-white menu - so a disabled row read as an
            // ordinary one and the only tells were the cursor and the missing
            // hover (2026-08-12). The dark theme's pair is 68 levels apart AND
            // moves toward its background; this one moved toward black, which is
            // the wrong direction on white. It now goes toward the ground, the
            // same way its opposite does, and lands at roughly the contrast the
            // dark pair has.
            SetChromeBrush("MenuDisabledForeground", "#FF9EA2A8");
            SetChromeBrush("HighlightForeground", "#FF000000");
            SetChromeBrush("HoverBackground", "#FFE8E8E8");
            // A step past HoverBackground, because a menu's background is the
            // panel white behind it rather than the dialog grey - see the dark
            // value below for the case that forced this brush to exist.
            SetChromeBrush("MenuHighlightBackground", "#FFDCDCDC");
            SetChromeBrush("SecondaryForeground", "#FF6E6E6E");
            SetChromeBrush("PanelBackground", "#FFFFFFFF");
            SetChromeBrush("PanelBorder", "#FFD4D4D4");
            SetChromeBrush("SeparatorBrush", "#26000000");
            SetChromeBrush("ControlBackground", "#FFECECEC");
            SetChromeBrush("ControlBorder", "#FFC0C0C0");
            SetChromeBrush("ControlHoverBackground", "#FFDCDCDC");
            SetChromeBrush("ControlHoverBorder", "#FFA0A0A0");
            SetChromeBrush("DialogBackground", "#FFFFFFFF");
            SetChromeBrush("DialogHeaderBackground", "#FFF3F3F3");
            SetChromeBrush("DialogForeground", "#FF1E1E1E");
            // Darker here too, not lighter: on white there is nothing above to
            // go to, so "set apart" means pressed down in both themes.
            SetChromeBrush("PickerPanelBackground", "#FFDCDCDC");
            SetChromeBrush("AccentForeground", "#FF0969DA");
        }
        else
        {
            SetChromeBrush("ForegroundText", "#FFA8AAAE");
            // ForegroundText #A8AAAE and the old hardcoded disabled #6A6A6A,
            // each with RGB scaled by ~1.1 - see the light-mode comment.
            SetChromeBrush("MenuForeground", "#FFB9BBBF");
            SetChromeBrush("MenuDisabledForeground", "#FF757575");
            SetChromeBrush("HighlightForeground", "#FFF0F2F6");
            SetChromeBrush("HoverBackground", "#FF2A2D2E");
            // HoverBackground is two points off the menu background below
            // (#282A2C) and vanished against it once menus stopped borrowing
            // the tree's selection colour. Matched instead to how plainly a
            // tree row answers the pointer.
            SetChromeBrush("MenuHighlightBackground", "#FF3A3D41");
            SetChromeBrush("SecondaryForeground", "#FF9A9A9A");
            // RGB 40/42/44 (user-picked 2026-07-21, was a flat #282828): a
            // hint of blue-grey so menus separate from the tree behind them.
            SetChromeBrush("PanelBackground", "#FF282A2C");
            SetChromeBrush("PanelBorder", "#FF454545");
            SetChromeBrush("SeparatorBrush", "#26FFFFFF");
            SetChromeBrush("ControlBackground", "#FF3C3C3C");
            SetChromeBrush("ControlBorder", "#FF5A5A5A");
            SetChromeBrush("ControlHoverBackground", "#FF505050");
            SetChromeBrush("ControlHoverBorder", "#FF7A7A7A");
            SetChromeBrush("DialogBackground", "#FF252526");
            SetChromeBrush("DialogHeaderBackground", "#FF2D2D2D");
            SetChromeBrush("DialogForeground", "#FFCCCCCC");
            SetChromeBrush("PickerPanelBackground", "#FF141415");
            SetChromeBrush("AccentForeground", "#FF4FA8FF");
        }
    }

    // RECOLOURED IN PLACE, not replaced (2026-08-15). Replacing the brush left
    // every element that had already READ it holding the old one - a
    // DynamicResource follows the swap, but a brush handed to a property in
    // code does not. The F1 help is where that showed: it is built once and
    // kept, and its rows take their colours through FindResource, so a window
    // built in the dark theme went on drawing #CCCCCC text after the app moved
    // to the light one - pale grey on white.
    //
    // The same lesson SetBrushColor already carries for the tree's own
    // palette, and the fix belongs here rather than in each window: any code
    // that reads a chrome brush once is now correct by default.
    //
    // The first call still replaces, because a brush declared in XAML arrives
    // frozen; from then on the instance is ours and can be recoloured.
    private void SetChromeBrush(string resourceKey, string hex)
    {
        if (ColorConverter.ConvertFromString(hex) is not Color color)
        {
            return;
        }

        if (Resources[resourceKey] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        Resources[resourceKey] = new SolidColorBrush(color);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // The catch-all: whatever reason was (or wasn't) stamped above, this
        // is the process actually going away.
        ExitLog.Record($"process exiting (code {e.ApplicationExitCode})");

        // Reaching here at all is what makes this exit "clean" - dropping the
        // heartbeat is how the next launch knows not to cry foul.
        ExitLog.EndSession();

        _trayIcon?.Dispose();
        _trayUpdateIcon?.Dispose();
        // Disposing an Icon made by Icon.FromHandle does not release the HICON
        // GetHicon created - that one is ours to destroy.
        if (_trayUpdateIconHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_trayUpdateIconHandle);
        }
        _trayBaseIcon?.Dispose();
        // The wait before the handle it waits on, or the callback can be handed
        // a disposed event on its way out.
        _activateWait?.Unregister(null);
        _activateSignal?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
