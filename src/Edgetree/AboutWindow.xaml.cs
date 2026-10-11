using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Color = System.Windows.Media.Color;

namespace SidebarExplorer.App;

public partial class AboutWindow : Window
{
    // updateAvailable: the newer version the startup check found, or null for
    // "none known" - when set, a download link appears under the version row
    // (the options-button dot announces an update; this is the in-app way to
    // actually go get it).
    public AboutWindow(Version? updateAvailable = null)
    {
        InitializeComponent();
        Deactivated += Window_Deactivated;

        var assembly = Assembly.GetExecutingAssembly();
        VersionText.Text = assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        if (updateAvailable is not null)
        {
            UpdateLinkText.Text = string.Format(
                Services.Strings.AboutUpdateAvailableFormat, "v" + updateAvailable.ToString(3));
            UpdateLinkText.Visibility = Visibility.Visible;
        }

        // No build-time step embeds a real build date (or git commit) yet, so
        // this reads the running exe's own last-write time as a reasonable
        // stand-in - accurate as of whenever this was last built. Assembly.Location
        // is empty for a PublishSingleFile build (the release exe this app is
        // actually distributed as), so this uses Environment.ProcessPath instead
        // - the same property SetStartWithWindows already relies on for exactly
        // this reason.
        string? exePath = Environment.ProcessPath;
        DateText.Text = exePath is not null
            ? File.GetLastWriteTime(exePath).ToString("yyyy-MM-dd")
            : string.Empty;
    }

    private void HeaderGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // Nudges attention back to this dialog if the user clicks outside the
    // whole app while it's open (ShowDialog only blocks its owner, not other
    // applications, so that's still possible).
    private void Window_Deactivated(object? sender, EventArgs e)
    {
        var flashBrush = new SolidColorBrush(((SolidColorBrush)RootBorder.BorderBrush).Color);
        RootBorder.BorderBrush = flashBrush;
        flashBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
        {
            To = Color.FromRgb(0x4F, 0xA8, 0xFF),
            Duration = TimeSpan.FromMilliseconds(150),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2)
        });
    }

    private void GithubLink_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://github.com/legendsteel11/Edgetree",
            UseShellExecute = true
        });
    }

    private void UpdateLink_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://github.com/legendsteel11/Edgetree/releases/latest",
            UseShellExecute = true
        });
    }

    private void WebsiteLink_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://edgetree.vercel.app",
            UseShellExecute = true
        });
    }

    private void IconLicenseLink_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => OpenCarriedText("APACHE-2.0.txt");

    private void MitNoticesLink_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => OpenCarriedText("THIRD-PARTY-NOTICES.txt");

    // Hands over a licence text the exe carries: extracted next to the other
    // app data (not %TEMP%, which cleaners empty) and opened in whatever reads
    // .txt. Rewritten each time so a stale copy can't outlive an update.
    private static void OpenCarriedText(string fileName)
    {
        try
        {
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Edgetree");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, fileName);

            var resource = System.Windows.Application.GetResourceStream(
                new Uri("Resources/" + fileName, UriKind.Relative));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                using var file = System.IO.File.Create(path);
                stream.CopyTo(file);
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            // Nothing to recover: the same text is in the repo's
            // THIRD-PARTY-NOTICES.md, which the landing and README both link.
        }
    }

    // Shared by every row under 같은 개발자의 다른 도구. Each of those TextBlocks
    // carries its own address in Tag, so this does not grow a switch as tools are
    // added, and the names stay declared in the XAML beside the label they sit
    // under. Tag is also not one of the attributes check-text reads when it looks
    // for screen text living outside Strings.cs, which is right: these are product
    // names and there is nothing to translate.
    private void OtherToolLink_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string url)
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
