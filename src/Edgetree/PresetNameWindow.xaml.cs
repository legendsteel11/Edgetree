using System.Windows;
using SidebarExplorer.App.Behaviors;
using SidebarExplorer.App.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace SidebarExplorer.App;

// Names a preset, on the way in and on a rename. One box, because a preset has
// exactly one thing about it the user chooses - the shape it holds was already
// chosen by having the app in it.
public partial class PresetNameWindow : Window
{
    // The name settled on, or null if they backed out. Never empty: an unnamed
    // slot cannot be told from another unnamed slot in a menu, so the caller's
    // default stands in for a blank box.
    public string? Result { get; private set; }

    private readonly string _fallback;

    // The title is passed in because the same one box serves three different
    // actions - adding, saving over a slot, and renaming one - and the title is
    // the only place saying which of them is being answered.
    //
    // The hint is passed in for the same reason and can be empty: it lists what
    // the press will STORE, which is true of adding and overwriting and is
    // exactly wrong under a rename.
    public PresetNameWindow(string currentName, string fallbackName, string title, string hint)
    {
        InitializeComponent();
        ModalTitleFlash.Attach(this, HeaderGrid);

        TitleText.Text = title;
        Title = title;
        _fallback = fallbackName;
        NameBox.Text = currentName.Length > 0 ? currentName : fallbackName;

        HintText.Text = hint;
        HintText.Visibility = hint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NameBox_Loaded(object sender, RoutedEventArgs e)
    {
        // Every text box in this app disables overtype - see OvertypeGuard.
        OvertypeGuard.Disable(NameBox);
        NameBox.Focus();
        NameBox.SelectAll();
    }

    // The composition case the extensions window records: finishing Korean
    // input with Enter would otherwise commit the composition AND press OK.
    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.ImeProcessed)
        {
            e.Handled = true;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        Result = name.Length > 0 ? name : _fallback;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }

    private void HeaderGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
