using System.Windows;
using SidebarExplorer.App.Behaviors;
using SidebarExplorer.App.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace SidebarExplorer.App;

// The one input behind 표시할 파일 종류 → 사용자 지정. A window rather than a
// row inside the menu: a TextBox hosted in a menu has to fight the menu for
// every key it receives, and this app's own stuck-capture watchdog reclaims a
// popup that holds the mouse within a couple of seconds (2026-08-02).
public partial class FilterExtensionsWindow : Window
{
    // The normalised list the user settled on, or null if they backed out.
    // Empty string is a real answer - it means "remove the custom kind".
    public string? Result { get; private set; }

    // Which of the two lists is being edited. The window is identical either
    // way - one box, one hint, OK/Cancel - so it takes its words as arguments
    // rather than being copied into a second window that would then have to be
    // kept in step (the same reasoning as the one file-filter submenu built in
    // code and hung off three menus).
    private readonly string _hint;
    private readonly string _emptyHint;

    public FilterExtensionsWindow(string currentExtensions, bool forExclusion = false)
    {
        InitializeComponent();
        ModalTitleFlash.Attach(this, HeaderGrid);

        string title = forExclusion ? Strings.FilterExcludeTitle : Strings.FilterCustomTitle;
        Title = title;
        TitleText.Text = title;
        _hint = forExclusion ? Strings.FilterExcludeHint : Strings.FilterCustomHint;
        _emptyHint = forExclusion ? Strings.FilterExcludeEmptyHint : Strings.FilterCustomEmptyHint;

        // Shown in the readable form, taken back in any form (see
        // FileTypeFilter.NormalizeExtensions) - what goes in the box is what
        // the row and the chip say, so the two can be compared at a glance.
        ExtensionsBox.Text = FileTypeFilter.DescribeExtensions(currentExtensions);
        UpdateHint();
    }

    private void ExtensionsBox_Loaded(object sender, RoutedEventArgs e)
    {
        // Every text box in this app disables overtype - a bare Insert
        // otherwise flips it on and breaks Korean composition with no visible
        // sign it happened (see OvertypeGuard).
        OvertypeGuard.Disable(ExtensionsBox);

        ExtensionsBox.Focus();
        ExtensionsBox.SelectAll();
    }

    private void ExtensionsBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => UpdateHint();

    private void UpdateHint()
        => HintText.Text = ExtensionsBox.Text.Trim().Length == 0 ? _emptyHint : _hint;

    // Enter is already the OK button (IsDefault), but only once the box has
    // given up the key - a TextBox that is not multi-line does, so this exists
    // for the composition case: finishing Korean input with Enter would
    // otherwise commit the composition AND close the window in one press.
    private void ExtensionsBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.ImeProcessed)
        {
            e.Handled = true;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Result = FileTypeFilter.NormalizeExtensions(ExtensionsBox.Text);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = false;
    }

    private void HeaderGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
