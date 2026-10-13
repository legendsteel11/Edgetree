using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Panel = System.Windows.Controls.Panel;

namespace SidebarExplorer.App;

// A modal dialog's title bar FLASHES when the window behind it is clicked
// (2026-10-13, on request), as a dialog with Windows' own title bar does.
//
// Windows still does its part - the beep, and flashing the dialog - but the
// flash only changes the look of a title bar Windows draws, and these
// dialogs draw their own (WindowStyle None, for the theme and to keep
// Windows 11's bright frame line off the top). So only the beep was left.
//
// What Windows sends while it flashes is WM_NCACTIVATE, inactive and active
// in turn, to a window that stays the foreground window throughout. Measured
// 2026-10-13 with probes on WindowStyle=None WPF windows: for a click on the
// disabled owner of a modal dialog, fourteen toggles 78ms apart (seven
// flashes in about 1.1s), GetForegroundWindow == the dialog at every one;
// with FlashWindowEx, toggles 530ms apart. A real deactivation sends
// WM_NCACTIVATE(0) with the foreground already elsewhere. WPF's IsActive is
// no use for telling them apart: it was still true at that moment.
//
// TWO SLOW FLASHES OF OUR OWN, not Windows' seven quick ones (the same day,
// on report: following each toggle read as too fast and too many). The
// first toggle of a run starts them; the rest of the run is ignored. A run
// is toggles less than RunGapMs apart, so a click after the last one ended
// flashes again.
//
// THE FLASH IS THE HEADER'S OWN COLOUR, BRIGHTER - lifted towards white on a
// dark header, pressed towards black on a light one, so it shows on both
// (the same day, on request). The tree's selected-row colour was tried
// first and read as a blue the dialogs otherwise never wear. The header's
// resting ground and the dialog's background are nearly the same colour in
// the dark theme, so flashing between those two - the "inactive" look -
// would hardly show either.
internal static class ModalTitleFlash
{
    private const int WM_NCACTIVATE = 0x0086;

    // Windows' toggles come 78ms apart within a run.
    private const long RunGapMs = 300;

    // Lit, dark, lit, then rest: two flashes in about three quarters of a
    // second. 350 / 250 / 350 was the first pace, and a little slow.
    private static readonly int[] PhaseMs = { 280, 200, 280 };

    // How far the lit header moves from its resting colour. 0.22 on a dark
    // header was the first try, and a shade too bright; 0.15 by eye.
    private const double LiftDark = 0.15;
    private const double PressLight = 0.12;

    public static void Attach(Window window, Panel header)
    {
        long lastToggle = long.MinValue / 2;
        int phase = -1;
        var timer = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher);

        void Stop()
        {
            timer.Stop();
            phase = -1;
            Rest(header);
        }

        timer.Tick += (_, _) =>
        {
            phase++;
            if (phase >= PhaseMs.Length)
            {
                Stop();
                return;
            }

            ShowPhase();
        };

        void ShowPhase()
        {
            if (phase % 2 == 0 && FlashBrush(window) is { } flash)
            {
                header.Background = flash;
            }
            else
            {
                Rest(header);
            }
            timer.Interval = TimeSpan.FromMilliseconds(PhaseMs[phase]);
            timer.Start();
        }

        // Added once the window is up, after anything else that hooks its
        // messages (WindowChrome hooks at SourceInitialized and answers
        // WM_NCACTIVATE itself): a source's hooks run newest first, and this
        // one only watches, never marking the message handled.
        window.Loaded += (_, _) =>
        {
            if (PresentationSource.FromVisual(window) is not HwndSource source)
            {
                return;
            }

            source.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg != WM_NCACTIVATE || GetForegroundWindow() != hwnd)
                {
                    return IntPtr.Zero;
                }

                long now = Environment.TickCount64;
                bool startsRun = wParam == IntPtr.Zero && now - lastToggle > RunGapMs;
                lastToggle = now;
                if (startsRun)
                {
                    timer.Stop();
                    phase = 0;
                    ShowPhase();
                }
                return IntPtr.Zero;
            });
        };

        // Never left lit: the window going inactive or away puts the header
        // back and ends the flashes.
        window.Deactivated += (_, _) => Stop();
        window.Closed += (_, _) => timer.Stop();
    }

    private static void Rest(Panel header)
        => header.SetResourceReference(Panel.BackgroundProperty, "DialogHeaderBackground");

    // Worked out from the header's colour at the moment of flashing, so a
    // theme or colour change in between is followed.
    private static Brush? FlashBrush(Window window)
    {
        if (window.TryFindResource("DialogHeaderBackground") is not SolidColorBrush { Color: var rest })
        {
            return null;
        }

        double luminance = (0.2126 * rest.R + 0.7152 * rest.G + 0.0722 * rest.B) / 255;
        bool dark = luminance < 0.5;
        byte target = dark ? (byte)255 : (byte)0;
        double amount = dark ? LiftDark : PressLight;
        byte Move(byte channel) => (byte)Math.Round(channel + (target - channel) * amount);

        var brush = new SolidColorBrush(Color.FromArgb(rest.A, Move(rest.R), Move(rest.G), Move(rest.B)));
        brush.Freeze();
        return brush;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
